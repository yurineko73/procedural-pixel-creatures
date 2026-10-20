// Static 3D-only fitting for ears and horns. The 2D generator anchors appendages to a
// silhouette; closed 3D shells need their roots buried in the cranium and horns kept clear
// of facial features. All offsets are export-only and never mutate source anatomy.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public sealed class PreviewHeadAttachmentFit
    {
        private readonly Dictionary<int, Vec3> _groupOffsets = new Dictionary<int, Vec3>();

        public PreviewHeadAttachmentFit(CreatureAnatomy anatomy)
        {
            for (int index = 0; index < anatomy.Primitives.Count; index++)
            {
                PrimitiveDef primitive = anatomy.Primitives[index];
                if (primitive.Group < 0 || primitive.Group >= anatomy.Groups.Count ||
                    _groupOffsets.ContainsKey(primitive.Group) ||
                    primitive.Kind != PrimKind.RoundCone ||
                    primitive.BoneA < 0 || primitive.BoneA >= anatomy.Skeleton.Count) continue;

                string groupName = anatomy.Groups[primitive.Group].Name ?? string.Empty;
                bool ear = primitive.Tag.EndsWith(".outer", StringComparison.OrdinalIgnoreCase) &&
                    anatomy.Skeleton.Bones[primitive.BoneA].Role == BoneRole.Ear;
                bool horn = groupName.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    anatomy.Skeleton.Bones[primitive.BoneA].Role == BoneRole.Head;
                if (!ear && !horn) continue;

                int headBone = ear ? anatomy.Skeleton.Bones[primitive.BoneA].Parent : primitive.BoneA;
                if (headBone < 0 || headBone >= anatomy.Skeleton.Count) continue;

                PrimitiveDef? skull = null;
                foreach (PrimitiveDef candidate in anatomy.Primitives)
                {
                    if (candidate.Kind == PrimKind.Ellipsoid && candidate.BoneA == headBone &&
                        candidate.Tag.EndsWith(".cranium", StringComparison.OrdinalIgnoreCase))
                    {
                        skull = candidate;
                        break;
                    }
                }
                if (skull == null) continue;

                Xform head = anatomy.Skeleton.RestWorld(headBone);
                Vec3 rootWorld = anatomy.Skeleton.RestWorld(primitive.BoneA).Point(primitive.LocalA);
                Vec3 relative = skull.LocalRotation.Conjugate.Rotate(
                    head.InversePoint(rootWorld) - skull.LocalA);
                double x = Math.Max(0.4, skull.Radii.X);
                double y = Math.Max(0.4, skull.Radii.Y);
                double z = Math.Max(0.4, skull.Radii.Z);
                double normalized = Math.Sqrt(relative.X * relative.X / (x * x) +
                    relative.Y * relative.Y / (y * y) + relative.Z * relative.Z / (z * z));
                if (normalized < 1e-8) continue;

                // Embed by a fraction of the actual root radius, not a fixed skull percentage.
                // This hides the flat cap without dragging small horns/ears far into the head.
                double minRadius = Math.Min(x, Math.Min(y, z));
                double rootRadius = Math.Max(0.3, primitive.RadiusA);
                double embedDepth = horn
                    ? Math.Max(0.45, Math.Min(rootRadius * 0.9, minRadius * 0.42))
                    : Math.Max(0.35, Math.Min(rootRadius * 0.9, minRadius * 0.35));
                double target = DMath.Clamp(1.0 - embedDepth / minRadius, horn ? 0.48 : 0.55, 0.92);
                Vec3 offset = Vec3.Zero;
                if (normalized > target)
                {
                    Vec3 localTarget = skull.LocalA + skull.LocalRotation.Rotate(relative / normalized * target);
                    offset = head.Point(localTarget) - rootWorld;
                }

                if (horn)
                {
                    // Eye avoidance can move a horn root back outside the skull. Re-seat it
                    // after each small clearance step so the correction slides around the head
                    // surface instead of leaving the appendage detached.
                    for (int pass = 0; pass < 3; pass++)
                    {
                        Vec3 clearance = EyeClearanceOffset(anatomy, primitive.Group, headBone, head, offset);
                        if (clearance.LengthSquared < 1e-10) break;
                        offset += clearance;
                        offset = KeepRootEmbedded(skull, head, rootWorld, offset, target);
                    }
                }

                if (offset.LengthSquared > 1e-10)
                    _groupOffsets[primitive.Group] = offset;
            }
        }

        private static Vec3 KeepRootEmbedded(PrimitiveDef skull, Xform head,
            Vec3 rootWorld, Vec3 offset, double target)
        {
            Vec3 localRoot = head.InversePoint(rootWorld + offset);
            Vec3 relative = skull.LocalRotation.Conjugate.Rotate(localRoot - skull.LocalA);
            double x = Math.Max(0.4, skull.Radii.X);
            double y = Math.Max(0.4, skull.Radii.Y);
            double z = Math.Max(0.4, skull.Radii.Z);
            double normalized = Math.Sqrt(relative.X * relative.X / (x * x) +
                relative.Y * relative.Y / (y * y) + relative.Z * relative.Z / (z * z));
            if (normalized <= target || normalized < 1e-8) return offset;
            Vec3 localTarget = skull.LocalA + skull.LocalRotation.Rotate(relative / normalized * target);
            return offset + head.Point(localTarget) - (rootWorld + offset);
        }

        private static Vec3 EyeClearanceOffset(CreatureAnatomy anatomy, int groupIndex,
            int headBone, Xform head, Vec3 rootOffset)
        {
            Vec3 additional = Vec3.Zero;
            foreach (FeatureDef eye in anatomy.Features)
            {
                if (eye.Kind != FeatureKind.Eye || eye.Bone != headBone) continue;
                Vec3 eyeWorld = head.Point(eye.LocalPosition);
                double nearestDistance = double.PositiveInfinity;
                double nearestRadius = 0;
                Vec3 nearestPoint = Vec3.Zero;

                foreach (PrimitiveDef segment in anatomy.Primitives)
                {
                    if (segment.Group != groupIndex || segment.Kind != PrimKind.RoundCone ||
                        segment.BoneA < 0 || segment.BoneA >= anatomy.Skeleton.Count ||
                        segment.BoneB < 0 || segment.BoneB >= anatomy.Skeleton.Count) continue;

                    Vec3 a = anatomy.Skeleton.RestWorld(segment.BoneA).Point(segment.LocalA) + rootOffset + additional;
                    Vec3 b = anatomy.Skeleton.RestWorld(segment.BoneB).Point(segment.LocalB) + rootOffset + additional;
                    Vec3 ab = b - a;
                    double lengthSquared = ab.LengthSquared;
                    double t = lengthSquared > 1e-12
                        ? DMath.Clamp(Vec3.Dot(eyeWorld - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
                    Vec3 point = a + ab * t;
                    double distance = Vec3.Distance(eyeWorld, point);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearestPoint = point;
                    nearestRadius = segment.RadiusA + (segment.RadiusB - segment.RadiusA) * t;
                }

                if (double.IsPositiveInfinity(nearestDistance)) continue;
                double clearance = Math.Max(0.8, eye.Size * 0.55 + nearestRadius * 0.25);
                if (nearestDistance >= clearance) continue;

                // Move the entire horn away from the closest eye-to-horn vector. The root is
                // already buried deeply enough to remain attached after this small correction.
                Vec3 away = (nearestPoint - eyeWorld).NormalizedOr(head.Dir(Vec3.UnitY));
                additional += away * Math.Min(1.75, clearance - nearestDistance + 0.15);
            }
            return additional;
        }

        public Vec3 Transform(PrimitiveDef primitive, Vec3 worldPoint) =>
            _groupOffsets.TryGetValue(primitive.Group, out Vec3 offset) ? worldPoint + offset : worldPoint;

        public bool HasFittedEar(int groupIndex) => _groupOffsets.ContainsKey(groupIndex);
        public bool HasFittedHorn(int groupIndex) => _groupOffsets.ContainsKey(groupIndex);
    }
}
