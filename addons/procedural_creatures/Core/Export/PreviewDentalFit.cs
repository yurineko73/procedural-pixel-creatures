// Export-only 3D placement of teeth using the original head/snout axes.
// The 2D generator intentionally stores upper teeth *inside* the upper lip so that they
// appear when animated open. A closed 3D preview must expose only a short, supported tip.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewDentalFit
    {
        public static bool Applies(CreatureAnatomy anatomy, PrimitiveDef source)
        {
            if (source.Kind != PrimKind.RoundCone || source.Group < 0 || source.Group >= anatomy.Groups.Count)
                return false;
            string group = anatomy.Groups[source.Group].Name ?? string.Empty;
            return group.EndsWith(".teeth", StringComparison.OrdinalIgnoreCase) ||
                   group.EndsWith(".fangs", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryFit(CreatureAnatomy anatomy, PrimitiveDef source, out PrimitiveDef fitted)
        {
            fitted = source;
            if (!Applies(anatomy, source)) return true;
            // Upper teeth are authored in the head frame; lower tusks are jaw-local and
            // intentionally not handled here.
            if (source.BoneA != source.BoneB) return true;

            PrimitiveDef? snout = null;
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.Kind != PrimKind.RoundCone || p.BoneA != source.BoneA || p.BoneB != source.BoneA ||
                    !p.Tag.EndsWith(".snout", StringComparison.OrdinalIgnoreCase)) continue;
                snout = p;
                break;
            }
            if (snout == null) return true; // preserve anatomy from other family head builders

            Vec3 axis = snout.LocalB - snout.LocalA;
            double axisLen2 = axis.LengthSquared;
            if (axisLen2 <= 1e-10) return true;
            double t = DMath.Clamp(Vec3.Dot(source.LocalA - snout.LocalA, axis) / axisLen2, 0.0, 1.0);
            Vec3 center = Vec3.Lerp(snout.LocalA, snout.LocalB, t);
            double radius = Math.Max(0.55, snout.RadiusA + (snout.RadiusB - snout.RadiusA) * t);
            double z = DMath.Clamp(source.LocalA.Z - center.Z, -radius * 0.58, radius * 0.58);
            bool fang = (anatomy.Groups[source.Group].Name ?? string.Empty).EndsWith(".fangs",
                StringComparison.OrdinalIgnoreCase);
            double originalLength = (source.LocalB - source.LocalA).Length;
            double visibleLength = Math.Min(originalLength, radius * (fang ? 0.42 : 0.24));
            // A minimum of 0.2 keeps tiny teeth legible but never invents huge saber-length tips.
            visibleLength = Math.Max(Math.Min(originalLength, 0.20), visibleLength);

            // Keep the root near the original upper-lip line and bury it in the muzzle. The 2D
            // renderer hides most of this primitive by layer order; a 3D shell has no such
            // depth bias, so use a short tooth that descends into the authored mouth cavity.
            Vec3 anchor = new Vec3(
                DMath.Clamp(source.LocalA.X, Math.Min(snout.LocalA.X, snout.LocalB.X),
                    Math.Max(snout.LocalA.X, snout.LocalB.X)),
                center.Y - radius * 0.12,
                center.Z + z);
            Vec3 oldDir = (source.LocalB - source.LocalA).NormalizedOr(-Vec3.UnitY);
            // Keep a slight forward slant but never let the tip turn upward/outward.
            Vec3 direction = new Vec3(DMath.Clamp(oldDir.X, -0.18, 0.22), -1.0,
                DMath.Clamp(oldDir.Z, -0.08, 0.08)).Normalized();
            fitted = source.Clone();
            fitted.LocalA = anchor;
            fitted.LocalB = anchor + direction * visibleLength;
            fitted.RadiusA = Math.Min(source.RadiusA, radius * 0.18);
            fitted.RadiusB = Math.Min(source.RadiusB, Math.Max(0.08, radius * 0.07));
            return true;
        }
    }
}
