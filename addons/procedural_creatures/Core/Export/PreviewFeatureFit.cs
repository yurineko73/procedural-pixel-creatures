// Export-only facial-feature fitting. FeatureDef is a 2D stamp description; it is not
// an unconstrained 3D eyeball radius. Never mutate anatomy or the 2D feature layout.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewFeatureFit
    {
        public readonly struct EyeFit
        {
            public readonly double Size;
            public readonly bool NeedsBacking;
            public readonly Vec3 WorldOffset;
            public EyeFit(double size, bool needsBacking, Vec3 worldOffset = default)
            {
                Size = size;
                NeedsBacking = needsBacking;
                WorldOffset = worldOffset;
            }
        }

        public static EyeFit Evaluate(CreatureAnatomy anatomy, FeatureDef feature)
        {
            double size = Math.Max(0.7, feature.Size);
            if (feature.Kind != FeatureKind.Eye ||
                feature.Bone < 0 || feature.Bone >= anatomy.Skeleton.Count)
                return new EyeFit(size, false);

            Xform owner = anatomy.Skeleton.RestWorld(feature.Bone);
            Vec3 featureWorld = owner.Point(feature.LocalPosition);
            double nearest = double.PositiveInfinity;
            PrimitiveDef? supportingShape = null;
            Vec3 nearestSurface = Vec3.Zero;
            double nearestNormalized = 0;

            // Only look on the owning bone: a huge chest shape on a parent bone must not
            // determine the size of an eyeball attached to a small head or eye stalk.
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.Kind != PrimKind.Ellipsoid || p.BoneA != feature.Bone ||
                    p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                if (p.Material != MaterialSlot.Primary && p.Material != MaterialSlot.Secondary &&
                    p.Material != MaterialSlot.Accent && p.Material != MaterialSlot.White) continue;
                string group = anatomy.Groups[p.Group].Name ?? string.Empty;
                if (group.StartsWith("gear", StringComparison.OrdinalIgnoreCase) ||
                    group.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) ||
                    group.StartsWith("equipment", StringComparison.OrdinalIgnoreCase)) continue;
                if ((anatomy.Groups[p.Group].Flags & GroupFlags.Interior) != 0) continue;

                Vec3 local = p.LocalRotation.Conjugate.Rotate(feature.LocalPosition - p.LocalA);
                double rx = Math.Max(0.25, p.Radii.X);
                double ry = Math.Max(0.25, p.Radii.Y);
                double rz = Math.Max(0.25, p.Radii.Z);
                double normalized = Math.Sqrt(local.X * local.X / (rx * rx) +
                    local.Y * local.Y / (ry * ry) + local.Z * local.Z / (rz * rz));
                if (normalized < 1e-9) continue;
                Vec3 surface = owner.Point(p.LocalA + p.LocalRotation.Rotate(local / normalized));
                double gap = Vec3.Distance(featureWorld, surface);
                if (gap >= nearest) continue;
                nearest = gap;
                supportingShape = p;
                nearestSurface = surface;
                nearestNormalized = normalized;
            }

            if (supportingShape != null && nearest <= Math.Max(1.25, size * 1.4))
            {
                // 3D ellipsoid radii are half-extents; FeatureDef.Size denotes the full
                // height of a 2D eye stamp. Keep the entire 3D socket within the host.
                double limit = Math.Max(0.7, Math.Min(supportingShape.Radii.Y,
                    supportingShape.Radii.Z) * 0.8);
                // If a nominal 2D eye stamp floats just outside its host's 3D surface,
                // translate the whole feature stack together instead of moving only the pupil.
                // Do not pull eyes deeply out of the skull or snap distant stalk eyes to a head.
                Vec3 correction = Vec3.Zero;
                if (nearestNormalized > 1.02 && nearest > 0.12 &&
                    nearest <= Math.Min(1.25, size * 0.65))
                    correction = nearestSurface - featureWorld;
                return new EyeFit(Math.Min(size, limit), false, correction);
            }

            // Eye-stalk endpoints (notably arthropods) sometimes have only a skinny cone and
            // no bulb. Support the stamp with a small eye cap in the 3D preview, leaving
            // the source 2D anatomy untouched. Not enabled for ordinary eyes or glow spots.
            BoneRole role = anatomy.Skeleton.Bones[feature.Bone].Role;
            bool stalk = feature.Group < 0 &&
                (role == BoneRole.Antenna || role == BoneRole.Tentacle);
            if (stalk)
            {
                // Check that a cone from this bone or its immediate parent actually reaches
                // the eye. Without a nearby stalk, emitting a bulb would itself float.
                foreach (PrimitiveDef p in anatomy.Primitives)
                {
                    if (p.Kind != PrimKind.RoundCone) continue;
                    if (p.BoneA != feature.Bone && p.BoneB != feature.Bone &&
                        anatomy.Skeleton.Bones[feature.Bone].Parent != p.BoneA &&
                        anatomy.Skeleton.Bones[feature.Bone].Parent != p.BoneB) continue;
                    Vec3 a = anatomy.Skeleton.RestWorld(p.BoneA).Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    Vec3 ab = b - a;
                    double t = ab.LengthSquared > 1e-12
                        ? DMath.Clamp(Vec3.Dot(featureWorld - a, ab) / ab.LengthSquared, 0.0, 1.0) : 0;
                    Vec3 nearestAxis = a + ab * t;
                    double radius = p.RadiusA + (p.RadiusB - p.RadiusA) * t;
                    if (Vec3.Distance(featureWorld, nearestAxis) <= radius + Math.Max(1.0, size * 1.1))
                        return new EyeFit(Math.Min(size, Math.Max(0.9, radius * 3.2)), true);
                }
            }
            return new EyeFit(size, false);
        }
    }
}
