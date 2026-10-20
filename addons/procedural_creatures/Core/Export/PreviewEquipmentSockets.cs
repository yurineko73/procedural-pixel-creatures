// Static-preview-only placement of held gear. The original 2D gameplay rig and seed behavior are unchanged.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public sealed class PreviewEquipmentSockets
    {
        public readonly struct Placement
        {
            public readonly Vec3 Pivot;
            public readonly Vec3 Target;
            public readonly Quat Rotation;
            public readonly double Scale;
            public Placement(Vec3 pivot, Vec3 target, Quat rotation, double scale)
            {
                Pivot = pivot; Target = target; Rotation = rotation; Scale = scale;
            }
            public Vec3 Point(Vec3 world) => Target + Rotation.Rotate(world - Pivot) * Scale;
        }

        private readonly Placement _weapon;
        private readonly Placement _shield;
        private readonly bool _hasWeapon, _hasShield;

        public PreviewEquipmentSockets(CreatureAnatomy anatomy)
        {
            if (!string.Equals(anatomy.FamilyId, "biped", StringComparison.OrdinalIgnoreCase)) return;
            int rightHand = FindBone(anatomy, BoneRole.Hand, 1);
            int leftHand = FindBone(anatomy, BoneRole.Hand, -1);
            if (rightHand >= 0)
            {
                Xform hand = anatomy.Skeleton.RestWorld(rightHand);
                double handR = HandRadius(anatomy, rightHand);
                Vec3 grip = hand.Point(new Vec3(-handR * 0.6, 0, 0));
                // Legacy weapons extend along local +Y of a freely rotated hand bone. In a
                // static model, reorient the shaft toward world-up while retaining its grip.
                Quat rotation = Quat.FromTo(hand.Dir(new Vec3(0.12, 1, 0)), Vec3.UnitY);
                double lowest = grip.Y;
                bool any = false;
                foreach (PrimitiveDef p in anatomy.Primitives)
                {
                    if (!IsWeapon(anatomy, p) || p.BoneA != rightHand) continue;
                    any = true;
                    Xform x = anatomy.Skeleton.RestWorld(p.BoneA);
                    if (p.Kind == PrimKind.Ellipsoid)
                    {
                        Vec3 center = grip + rotation.Rotate(x.Point(p.LocalA) - grip);
                        double radius = Math.Max(p.Radii.X, Math.Max(p.Radii.Y, p.Radii.Z));
                        lowest = Math.Min(lowest, center.Y - radius);
                    }
                    else if (p.Kind == PrimKind.RoundCone)
                    {
                        Vec3 a = grip + rotation.Rotate(x.Point(p.LocalA) - grip);
                        Vec3 b = grip + rotation.Rotate(anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB) - grip);
                        lowest = Math.Min(lowest, Math.Min(a.Y - p.RadiusA, b.Y - p.RadiusB));
                    }
                }
                if (any)
                {
                    double available = Math.Max(0.5, grip.Y - 0.2);
                    double reachBelow = Math.Max(0.0, grip.Y - lowest);
                    double scale = reachBelow > available ? Math.Max(0.30, available / reachBelow) : 1.0;
                    _weapon = new Placement(grip, grip, rotation, scale);
                    _hasWeapon = true;
                }
            }

            if (leftHand >= 0)
            {
                Xform hand = anatomy.Skeleton.RestWorld(leftHand);
                Vec3 center = Vec3.Zero;
                double radius = 0;
                int count = 0;
                foreach (PrimitiveDef p in anatomy.Primitives)
                {
                    if (!IsShield(anatomy, p) || p.Kind != PrimKind.Ellipsoid) continue;
                    center += anatomy.Skeleton.RestWorld(p.BoneA).Point(p.LocalA);
                    radius = Math.Max(radius, Math.Max(p.Radii.Y, p.Radii.Z));
                    count++;
                }
                if (count > 0)
                {
                    center /= count;
                    double handR = HandRadius(anatomy, leftHand);
                    Vec3 handPos = hand.Origin;
                    int forearm = FindParentBone(anatomy, leftHand);
                    Vec3 forearmPos = forearm >= 0 ? anatomy.Skeleton.RestWorld(forearm).Origin : handPos;
                    Vec3 target = Vec3.Lerp(forearmPos, handPos, 0.48) +
                        new Vec3(0, Math.Max(0.2, handR * 0.15), -Math.Max(0.5, handR * 0.65));
                    // Large shields cannot be wider than the hand/forearm clearance allows.
                    double scale = radius > 0.1 ? Math.Min(1.0, Math.Max(0.38, (target.Y - 0.2) / radius)) : 1.0;
                    _shield = new Placement(center, target, Quat.Identity, scale);
                    _hasShield = true;
                }
            }
        }

        public Vec3 Transform(CreatureAnatomy anatomy, PrimitiveDef p, Vec3 point)
        {
            if (_hasWeapon && IsWeapon(anatomy, p)) return _weapon.Point(point);
            if (_hasShield && IsShield(anatomy, p)) return _shield.Point(point);
            return point;
        }

        public double RadiusScale(CreatureAnatomy anatomy, PrimitiveDef p)
        {
            if (_hasWeapon && IsWeapon(anatomy, p)) return _weapon.Scale;
            if (_hasShield && IsShield(anatomy, p)) return _shield.Scale;
            return 1.0;
        }

        private static bool IsWeapon(CreatureAnatomy a, PrimitiveDef p)
        {
            if (p.Group < 0 || p.Group >= a.Groups.Count) return false;
            string name = a.Groups[p.Group].Name;
            return name == "weapon" || (name.StartsWith("equipment.", StringComparison.Ordinal) &&
                (name.Contains(".prop.", StringComparison.Ordinal) ||
                 name.Contains(".blaster.", StringComparison.Ordinal) ||
                 name.Contains(".scrap.tool", StringComparison.Ordinal)));
        }

        private static bool IsShield(CreatureAnatomy a, PrimitiveDef p) =>
            p.Group >= 0 && p.Group < a.Groups.Count &&
            a.Groups[p.Group].Name.StartsWith("gear.shield", StringComparison.Ordinal);

        private static int FindBone(CreatureAnatomy a, BoneRole role, int side)
        {
            foreach (BoneDef bone in a.Skeleton.Bones)
                if (bone.Role == role && bone.Side == side) return bone.Index;
            return -1;
        }

        private static int FindParentBone(CreatureAnatomy a, int bone) =>
            bone >= 0 && bone < a.Skeleton.Count ? a.Skeleton.Bones[bone].Parent : -1;

        private static double HandRadius(CreatureAnatomy a, int hand)
        {
            foreach (PrimitiveDef p in a.Primitives)
                if (p.BoneA == hand && p.Kind == PrimKind.Ellipsoid &&
                    p.Tag.EndsWith(".palm", StringComparison.Ordinal))
                    return Math.Max(0.6, p.Radii.Y / 0.9);
            return 1.0;
        }
    }
}
