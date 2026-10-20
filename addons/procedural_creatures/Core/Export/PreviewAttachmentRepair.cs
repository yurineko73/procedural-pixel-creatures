// 3D-export-only safeguards for little pieces made for the original 2D renderer.
// Does not modify the source anatomy: every adjustment is made on a clone.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewAttachmentRepair
    {
        /// <summary>Validate claws, teeth and fangs against a nearby same-bone host surface.</summary>
        public static bool TryRepair(CreatureAnatomy anatomy, PrimitiveDef source, out PrimitiveDef repaired)
        {
            repaired = source;
            if (source.Kind != PrimKind.RoundCone) return true;
            if (source.Group < 0 || source.Group >= anatomy.Groups.Count) return true;
            string groupName = anatomy.Groups[source.Group].Name;
            bool tinyPart = source.Tag.EndsWith(".claw", StringComparison.Ordinal) ||
                groupName.EndsWith(".teeth", StringComparison.Ordinal) ||
                groupName.EndsWith(".fangs", StringComparison.Ordinal);
            if (!tinyPart) return true;

            // Claws, teeth and fangs must have a real body/paw/skull ellipsoid on the SAME bone.
            // Do not compare against unrelated nearby limbs (they can be much closer in 3D).
            PrimitiveDef? host = null;
            double bestDistance = double.PositiveInfinity;
            Vec3 bestSurface = Vec3.Zero;
            bool embedded = false;
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.Kind != PrimKind.Ellipsoid || p.BoneA != source.BoneA ||
                    (p.Material != MaterialSlot.Primary && p.Material != MaterialSlot.Secondary &&
                     p.Material != MaterialSlot.Accent && p.Material != MaterialSlot.White) ||
                    p.Group < 0 || p.Group >= anatomy.Groups.Count ||
                    (anatomy.Groups[p.Group].Flags & GroupFlags.Interior) != 0)
                    continue;

                Vec3 relative = p.LocalRotation.Conjugate.Rotate(source.LocalA - p.LocalA);
                double rx = Math.Max(0.25, p.Radii.X), ry = Math.Max(0.25, p.Radii.Y), rz = Math.Max(0.25, p.Radii.Z);
                double normalized = Math.Sqrt(relative.X * relative.X / (rx * rx) +
                    relative.Y * relative.Y / (ry * ry) + relative.Z * relative.Z / (rz * rz));
                if (normalized < 1e-9) continue;
                Vec3 projected = p.LocalA + p.LocalRotation.Rotate(relative / normalized);
                double distance = (projected - source.LocalA).Length;
                bool isInside = normalized <= 1.0;
                if (distance < bestDistance)
                {
                    host = p;
                    bestDistance = distance;
                    bestSurface = projected;
                    embedded = isInside;
                }
            }

            // Some species do not have an ellipsoid on the same bone. Preserve their authored
            // details instead of blindly deleting them because no suitable host was found.
            if (host == null || embedded) return true;
            double maxCorrection = Math.Max(0.85, Math.Min(2.0, Math.Max(host.Radii.X,
                Math.Max(host.Radii.Y, host.Radii.Z)) * 0.34));
            if (bestDistance > maxCorrection)
            {
                // Tiny isolated teeth/claws are optional decoration, not a reason to create
                // a visible floating shard outside the character silhouette.
                return false;
            }

            // Move the base *into* the host slightly, preserving the segment's length,
            // taper and orientation. Only export is changed, not the 2D anatomy or animation.
            Vec3 inward = (host.LocalA - bestSurface).NormalizedOr(Vec3.UnitY) * 0.18;
            Vec3 offset = bestSurface + inward - source.LocalA;
            repaired = source.Clone();
            repaired.LocalA = source.LocalA + offset;
            if (source.BoneA == source.BoneB) repaired.LocalB = source.LocalB + offset;
            return true;
        }
    }
}
