// The original 2D limb generator places claws beyond an authored toe silhouette.
// 3D claws must instead start inside the matching paw/foot/hand ellipsoid and follow
// its forward axis. This happens only in static OBJ export; 2D anatomy is unchanged.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewClawFit
    {
        public static bool Applies(PrimitiveDef source) =>
            source.Kind == PrimKind.RoundCone &&
            (source.Tag ?? string.Empty).EndsWith(".claw", StringComparison.OrdinalIgnoreCase);

        public static bool TryFit(CreatureAnatomy anatomy, PrimitiveDef source, out PrimitiveDef fitted)
        {
            fitted = source;
            if (!Applies(source)) return true;
            PrimitiveDef? host = null;
            double best = double.PositiveInfinity;
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.Kind != PrimKind.Ellipsoid || p.BoneA != source.BoneA ||
                    p.Material != MaterialSlot.Primary && p.Material != MaterialSlot.Secondary &&
                    p.Material != MaterialSlot.Accent && p.Material != MaterialSlot.White) continue;
                string tag = p.Tag ?? string.Empty;
                if (!tag.EndsWith(".foot", StringComparison.OrdinalIgnoreCase) &&
                    !tag.EndsWith(".paw", StringComparison.OrdinalIgnoreCase) &&
                    !tag.EndsWith(".palm", StringComparison.OrdinalIgnoreCase)) continue;
                double distance = (p.LocalA - source.LocalA).LengthSquared;
                if (distance < best) { best = distance; host = p; }
            }
            // Non-paw geometries (e.g. jointed arthropod tips) still use the ancestor-limb
            // surface repair rather than disappearing just because they lack a paw ellipsoid.
            if (host == null) return PreviewAttachmentRepair.TryRepair(anatomy, source, out fitted);

            // The bone's +X is the authored toe/finger direction. Do not select an arbitrary
            // nearest parent surface: it can be the other leg when the feet are close together.
            double rx = Math.Max(0.3, host.Radii.X);
            double ry = Math.Max(0.3, host.Radii.Y);
            double rz = Math.Max(0.3, host.Radii.Z);
            Vec3 relative = host.LocalRotation.Conjugate.Rotate(source.LocalA - host.LocalA);
            double z = DMath.Clamp(relative.Z, -rz * 0.42, rz * 0.42);
            Vec3 anchored = host.LocalA + host.LocalRotation.Rotate(
                new Vec3(rx * 0.55, ry * 0.05, z));
            Vec3 axis = (source.LocalB - source.LocalA).NormalizedOr(Vec3.UnitX);
            // Forward/outward, with a shallow downward curve, not a free-floating vertical spike.
            axis = new Vec3(Math.Max(0.55, axis.X), DMath.Clamp(axis.Y, -0.38, 0.10),
                DMath.Clamp(axis.Z, -0.22, 0.22)).Normalized();
            double authoredLength = (source.LocalB - source.LocalA).Length;
            double length = Math.Min(authoredLength, Math.Max(0.45, rx * 0.68));
            Vec3 tip = anchored + host.LocalRotation.Rotate(axis * length);
            Xform foot = anatomy.Skeleton.RestWorld(source.BoneA);
            Vec3 worldTip = foot.Point(tip);
            double baseHeight = foot.Point(anchored).Y;
            // Prevent ground penetration without forcing a low-lying claw to curl upward
            // above its root. If the toe itself is near the floor, preserve a downward
            // direction and leave full rest-pose floor correction to the scene placement.
            double floorClearance = Math.Min(0.34, baseHeight - 0.06);
            if (worldTip.Y < floorClearance)
                tip = foot.InversePoint(new Vec3(worldTip.X, floorClearance, worldTip.Z));

            fitted = source.Clone();
            fitted.LocalA = anchored;
            fitted.LocalB = tip;
            fitted.RadiusA = Math.Min(source.RadiusA, Math.Max(0.25, ry * 0.34));
            fitted.RadiusB = Math.Min(source.RadiusB, 0.22);
            return true;
        }
    }
}
