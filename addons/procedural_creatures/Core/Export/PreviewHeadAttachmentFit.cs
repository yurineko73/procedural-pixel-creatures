// Static 3D-only ear attachment: the 2D generator positions ear bones on a silhouette.
// Real 3D ears must begin slightly *inside* their owning skull, not beside it.
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
                PrimitiveDef p = anatomy.Primitives[index];
                if (!p.Tag.EndsWith(".outer", StringComparison.OrdinalIgnoreCase) ||
                    p.Kind != PrimKind.RoundCone || p.Group < 0 ||
                    p.Group >= anatomy.Groups.Count ||
                    _groupOffsets.ContainsKey(p.Group)) continue;
                if (p.BoneA < 0 || p.BoneA >= anatomy.Skeleton.Count ||
                    anatomy.Skeleton.Bones[p.BoneA].Role != BoneRole.Ear) continue;

                int headBone = anatomy.Skeleton.Bones[p.BoneA].Parent;
                if (headBone < 0 || headBone >= anatomy.Skeleton.Count) continue;
                PrimitiveDef? skull = null;
                foreach (PrimitiveDef candidate in anatomy.Primitives)
                    if (candidate.Kind == PrimKind.Ellipsoid && candidate.BoneA == headBone &&
                        candidate.Tag.EndsWith(".cranium", StringComparison.OrdinalIgnoreCase))
                    { skull = candidate; break; }
                if (skull == null) continue;

                Xform head = anatomy.Skeleton.RestWorld(headBone);
                Vec3 rootWorld = anatomy.Skeleton.RestWorld(p.BoneA).Point(p.LocalA);
                Vec3 relative = skull.LocalRotation.Conjugate.Rotate(head.InversePoint(rootWorld) - skull.LocalA);
                double x = Math.Max(0.4, skull.Radii.X), y = Math.Max(0.4, skull.Radii.Y), z = Math.Max(0.4, skull.Radii.Z);
                double t = Math.Sqrt(relative.X * relative.X / (x * x) +
                    relative.Y * relative.Y / (y * y) + relative.Z * relative.Z / (z * z));
                if (t < 1e-8) continue;
                // A root already deep enough in the skull does not need translation.
                double depth = Math.Min(p.RadiusA * 0.32, Math.Min(x, Math.Min(y, z)) * 0.12);
                if (t <= 1.0 - depth / Math.Max(1.0, Math.Min(x, Math.Min(y, z)))) continue;
                Vec3 localTarget = skull.LocalA + skull.LocalRotation.Rotate(relative / t *
                    (1.0 - depth / Math.Max(1.0, Math.Min(x, Math.Min(y, z)))));
                Vec3 offset = head.Point(localTarget) - rootWorld;
                // Entire outer/inner ear is moved together, rather than independently
                // projecting inner cartilage and opening a new gap.
                _groupOffsets[p.Group] = offset;
            }
        }

        public Vec3 Transform(PrimitiveDef primitive, Vec3 worldPoint) =>
            _groupOffsets.TryGetValue(primitive.Group, out Vec3 offset) ? worldPoint + offset : worldPoint;

        public bool HasFittedEar(int groupIndex) => _groupOffsets.ContainsKey(groupIndex);
    }
}
