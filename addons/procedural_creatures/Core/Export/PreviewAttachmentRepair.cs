// 3D-export-only safeguards for small details whose authored 2D anchor may not touch the
// underlying 3D surface. Source anatomy is immutable; every correction is applied to a clone.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewAttachmentRepair
    {
        /// <summary>
        /// Repair claws, teeth, fangs and hooves against a nearby body surface. Host geometry may
        /// live on the same bone or on an ancestor limb segment (e.g. a claw attached to a foot).
        /// </summary>
        public static bool TryRepair(CreatureAnatomy anatomy, PrimitiveDef source, out PrimitiveDef repaired)
        {
            repaired = source;
            if (source.Kind != PrimKind.RoundCone) return true;
            if (source.Group < 0 || source.Group >= anatomy.Groups.Count) return true;
            if (source.BoneA < 0 || source.BoneA >= anatomy.Skeleton.Count) return true;

            string groupName = anatomy.Groups[source.Group].Name ?? string.Empty;
            bool claw = source.Tag.EndsWith(".claw", StringComparison.Ordinal);
            bool teeth = groupName.EndsWith(".teeth", StringComparison.Ordinal) ||
                groupName.EndsWith(".fangs", StringComparison.Ordinal);
            bool strictTinyPart = claw || teeth;
            bool fittedPart = strictTinyPart ||
                source.Tag.EndsWith(".hoof", StringComparison.Ordinal) ||
                groupName.EndsWith(".tusks", StringComparison.Ordinal) ||
                groupName.EndsWith(".mandibles", StringComparison.Ordinal);
            if (!fittedPart) return true;

            Xform sourceA = anatomy.Skeleton.RestWorld(source.BoneA);
            Vec3 worldA = sourceA.Point(source.LocalA);
            PrimitiveDef? bestHost = null;
            Vec3 bestSurface = Vec3.Zero;
            double bestGap = double.PositiveInfinity;
            double bestHostRadius = 1.0;
            bool embedded = false;

            foreach (PrimitiveDef host in anatomy.Primitives)
            {
                if (ReferenceEquals(host, source) || host.Group < 0 || host.Group >= anatomy.Groups.Count) continue;
                if (host.Material != MaterialSlot.Primary && host.Material != MaterialSlot.Secondary &&
                    host.Material != MaterialSlot.Accent && host.Material != MaterialSlot.White) continue;
                string hostGroup = anatomy.Groups[host.Group].Name ?? string.Empty;
                if (IsEquipmentGroup(hostGroup) || (anatomy.Groups[host.Group].Flags & GroupFlags.Interior) != 0) continue;

                // The host must be on the source bone's ancestry chain. This avoids snapping a
                // left-hand claw to the right arm merely because the two happen to be nearby.
                bool related = IsAncestorOrSame(anatomy, host.BoneA, source.BoneA);
                if (host.Kind == PrimKind.RoundCone)
                    related |= IsAncestorOrSame(anatomy, host.BoneB, source.BoneA);
                if (!related) continue;

                Vec3 surface;
                double gap, hostRadius;
                bool contains;
                if (host.Kind == PrimKind.Ellipsoid)
                {
                    Xform hostXf = anatomy.Skeleton.RestWorld(host.BoneA);
                    Vec3 local = hostXf.InversePoint(worldA);
                    Vec3 relative = host.LocalRotation.Conjugate.Rotate(local - host.LocalA);
                    double rx = Math.Max(0.25, host.Radii.X);
                    double ry = Math.Max(0.25, host.Radii.Y);
                    double rz = Math.Max(0.25, host.Radii.Z);
                    double normalized = Math.Sqrt(relative.X * relative.X / (rx * rx) +
                        relative.Y * relative.Y / (ry * ry) + relative.Z * relative.Z / (rz * rz));
                    if (normalized < 1e-9) continue;
                    Vec3 projected = host.LocalA + host.LocalRotation.Rotate(relative / normalized);
                    surface = hostXf.Point(projected);
                    contains = normalized <= 1.0;
                    gap = (worldA - surface).Length;
                    hostRadius = Math.Max(rx, Math.Max(ry, rz));
                }
                else if (host.Kind == PrimKind.RoundCone)
                {
                    Vec3 a = anatomy.Skeleton.RestWorld(host.BoneA).Point(host.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(host.BoneB).Point(host.LocalB);
                    Vec3 ab = b - a;
                    double lengthSquared = ab.LengthSquared;
                    double t = lengthSquared > 1e-12 ? DMath.Clamp(Vec3.Dot(worldA - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
                    Vec3 center = a + ab * t;
                    double radius = host.RadiusA + (host.RadiusB - host.RadiusA) * t;
                    Vec3 radial = worldA - center;
                    double radialLength = radial.Length;
                    Vec3 outward = radial.NormalizedOr(Vec3.Cross(ab, Vec3.UnitY).NormalizedOr(Vec3.UnitZ));
                    surface = center + outward * radius;
                    contains = radialLength <= radius;
                    gap = Math.Abs(radialLength - radius);
                    hostRadius = Math.Max(host.RadiusA, host.RadiusB);
                }
                else
                {
                    continue;
                }

                // If the anchor is already buried in a valid host, its base is attached; do not
                // pull it back out just because another nearby surface is mathematically closer.
                if (contains) { embedded = true; break; }
                if (gap < bestGap)
                {
                    bestHost = host;
                    bestSurface = surface;
                    bestGap = gap;
                    bestHostRadius = hostRadius;
                }
            }

            if (embedded || bestHost == null) return true;

            double maxCorrection = strictTinyPart
                ? Math.Max(0.85, Math.Min(2.8, bestHostRadius * 0.45))
                : Math.Max(1.0, Math.Min(3.5, bestHostRadius * 0.65));
            if (bestGap > maxCorrection)
            {
                // A tiny claw/tooth far away from any valid host is almost certainly a detached
                // shard. Prominent tusks/hooves are retained if the authored shape is unusual.
                return !strictTinyPart;
            }

            // Sink the base just below the host surface so the round-cone has a real intersection.
            Vec3 outwardFromSurface = (worldA - bestSurface).NormalizedOr(Vec3.UnitY);
            Vec3 correctedWorldA = bestSurface - outwardFromSurface * 0.10;
            Vec3 offset = correctedWorldA - worldA;
            repaired = source.Clone();
            repaired.LocalA = anatomy.Skeleton.RestWorld(source.BoneA).InversePoint(worldA + offset);
            if (source.BoneB >= 0 && source.BoneB < anatomy.Skeleton.Count)
            {
                Xform sourceB = anatomy.Skeleton.RestWorld(source.BoneB);
                Vec3 worldB = sourceB.Point(source.LocalB);
                repaired.LocalB = sourceB.InversePoint(worldB + offset);
            }
            return true;
        }

        private static bool IsAncestorOrSame(CreatureAnatomy anatomy, int candidate, int bone)
        {
            if (candidate < 0 || candidate >= anatomy.Skeleton.Count) return false;
            for (int current = bone; current >= 0 && current < anatomy.Skeleton.Count;
                current = anatomy.Skeleton.Bones[current].Parent)
                if (current == candidate) return true;
            return false;
        }

        private static bool IsEquipmentGroup(string name) =>
            name.StartsWith("gear", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("equipment", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("pauldron", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
