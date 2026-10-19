// Procedural Pixel Creatures - additive equipment style packs for 3D asset prototypes.
// Independent of the original BipedFamily gene schema to preserve legacy seed/golden results.
// Shape overlays use existing bones, so no changes to the source rig or anatomy are required.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Equipment
{
    public interface IEquipmentStylePack
    {
        string Id { get; }
        void Build(EquipmentBuildContext context);
    }

    /// <summary>Pluggable registry: new styles register without modifying the core assembler.</summary>
    public sealed class EquipmentStyleRegistry
    {
        private readonly Dictionary<string, IEquipmentStylePack> _packs =
            new Dictionary<string, IEquipmentStylePack>(StringComparer.OrdinalIgnoreCase);

        public void Register(IEquipmentStylePack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (string.IsNullOrWhiteSpace(pack.Id)) throw new ArgumentException("Style id cannot be empty.");
            if (_packs.ContainsKey(pack.Id)) throw new InvalidOperationException("Duplicate equipment style: " + pack.Id);
            _packs.Add(pack.Id, pack);
        }

        public IEquipmentStylePack Get(string id) =>
            _packs.TryGetValue(id, out var pack) ? pack :
            throw new KeyNotFoundException("Unknown equipment style: " + id);

        public static EquipmentStyleRegistry CreateDefault()
        {
            var registry = new EquipmentStyleRegistry();
            registry.Register(new ModernStylePack());
            registry.Register(new SciFiStylePack());
            registry.Register(new WastelandStylePack());
            return registry;
        }
    }

    public sealed class EquipmentBuildContext
    {
        private readonly CreatureAnatomy _source;
        private readonly ulong _seed;
        private readonly List<PartGroup> _groups;
        private readonly List<PrimitiveDef> _shapes;
        private readonly List<string> _traits;
        private readonly string _style;

        internal EquipmentBuildContext(CreatureAnatomy source, string style, ulong seed)
        {
            _source = source; _style = style; _seed = seed;
            _groups = new List<PartGroup>(source.Groups);
            _shapes = new List<PrimitiveDef>(source.Primitives);
            _traits = new List<string>(source.Traits);
            _traits.Add("equipment.style:" + style);
        }

        /// <summary>Independent per-component streams, stable across optional component additions.</summary>
        public Rng RandomFor(string part) => Rng.Derive(_seed, "equipment." + _style + "." + part);

        public int FindBone(BoneRole role, int side = 0)
        {
            foreach (BoneDef bone in _source.Skeleton.Bones)
                if (bone.Role == role && (side == 0 || bone.Side == side))
                    return bone.Index;
            return -1;
        }

        public int RequireBone(BoneRole role, int side = 0)
        {
            int index = FindBone(role, side);
            if (index < 0) throw new InvalidOperationException(
                "Equipment style '" + _style + "' needs a " + role + " bone; use a compatible creature family.");
            return index;
        }

        public PrimitiveDef? FindShell(int bone)
        {
            PrimitiveDef? best = null;
            double area = 0;
            foreach (var p in _source.Primitives)
            {
                if (p.BoneA != bone || p.Kind != PrimKind.Ellipsoid) continue;
                double a = p.Radii.X * p.Radii.Y * p.Radii.Z;
                if (a > area) { area = a; best = p; }
            }
            return best;
        }

        /// <summary>Clothing/armour wraps a selected ellipsoid in rest pose.</summary>
        public void Cover(string name, int bone, double scale, MaterialSlot material)
        {
            var shell = FindShell(bone);
            if (shell == null) throw new InvalidOperationException("No compatible shell for bone " + bone);
            Ellipsoid(name, bone, shell.LocalA, shell.Radii * scale, material);
        }

        public void Ellipsoid(string name, int bone, Vec3 localCenter, Vec3 radii, MaterialSlot material,
            PrimFlags flags = PrimFlags.None)
        {
            if (bone < 0 || bone >= _source.Skeleton.Count) throw new ArgumentOutOfRangeException(nameof(bone));
            if (radii.X <= 0 || radii.Y <= 0 || radii.Z <= 0 || !radii.IsFinite || !localCenter.IsFinite)
                throw new ArgumentException("Equipment requires finite positive ellipsoid dimensions.");
            int index = _groups.Count;
            string tag = "equipment." + _style + "." + name;
            _groups.Add(new PartGroup(tag, 0.3, 0, GroupFlags.None, 0.7) { Index = index });
            _shapes.Add(new PrimitiveDef
            {
                Kind = PrimKind.Ellipsoid, Group = index, BoneA = bone,
                LocalA = localCenter, Radii = radii, Material = material,
                Flags = flags | PrimFlags.Hard | PrimFlags.NoPattern, Tag = tag
            });
            if (!_traits.Contains(tag)) _traits.Add(tag);
        }

        public void Rod(string name, int bone, Vec3 from, Vec3 to, double radius, MaterialSlot material)
        {
            if (bone < 0 || bone >= _source.Skeleton.Count) throw new ArgumentOutOfRangeException(nameof(bone));
            if (radius <= 0 || !DMath.IsFinite(radius) || !from.IsFinite || !to.IsFinite)
                throw new ArgumentException("Invalid equipment rod.");
            int index = _groups.Count;
            string tag = "equipment." + _style + "." + name;
            _groups.Add(new PartGroup(tag, 0.3, 0, GroupFlags.None, 0.7) { Index = index });
            _shapes.Add(new PrimitiveDef
            {
                Kind = PrimKind.RoundCone, Group = index, BoneA = bone, BoneB = bone,
                LocalA = from, LocalB = to, RadiusA = radius, RadiusB = radius * 0.9,
                Material = material, Flags = PrimFlags.Hard | PrimFlags.NoPattern, Tag = tag
            });
        }

        internal CreatureAnatomy Build()
        {
            // The original pixel canvas is deliberately unchanged. This decorated model is
            // intended for the OBJ/3D pipeline, not the 2D sprite renderer.
            ulong key = StableHash.Derive(_source.AnatomyKey, "equipment." + _style + "." + _seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new CreatureAnatomy(_source.FamilyId, _source.Skeleton, _groups, _shapes,
                new List<FeatureDef>(_source.Features), _source.Rig, _source.Metrics, _source.Canvas,
                new List<string>(_source.Repairs), _traits, key);
        }
    }

    public static class EquipmentGenerator
    {
        /// <summary>
        /// Returns a new immutable export anatomy. Does not mutate source and does not alter
        /// the BipedFamily schema/generation. Use 'fantasy' to keep the original equipment.
        /// </summary>
        public static CreatureAnatomy Apply(CreatureAnatomy source, string styleId, ulong seed,
            EquipmentStyleRegistry? registry = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.Equals(styleId, "fantasy", StringComparison.OrdinalIgnoreCase)) return source;
            registry ??= EquipmentStyleRegistry.CreateDefault();
            IEquipmentStylePack pack = registry.Get(styleId);
            var context = new EquipmentBuildContext(source, pack.Id, seed);
            pack.Build(context);
            return context.Build();
        }
    }

    /// <summary>Contemporary game outfit: fitted jacket, straps, backpack, and an abstract prop.</summary>
    public sealed class ModernStylePack : IEquipmentStylePack
    {
        public string Id => "modern";

        public void Build(EquipmentBuildContext c)
        {
            int chest = c.RequireBone(BoneRole.Chest);
            int head = c.RequireBone(BoneRole.Head);
            int hand = c.RequireBone(BoneRole.Hand, 1);
            var jacket = c.RandomFor("jacket");
            c.Cover("jacket", chest, jacket.Range(1.09, 1.18), MaterialSlot.Cloth);
            var shell = c.FindShell(chest)!;
            if (c.RandomFor("vest").Chance(0.7))
                c.Ellipsoid("vest", chest, shell.LocalA + new Vec3(0, -shell.Radii.Y * 0.5, 0),
                    new Vec3(shell.Radii.X * 0.75, shell.Radii.Y * 0.5, shell.Radii.Z * 0.92), MaterialSlot.Cloth);
            if (c.RandomFor("backpack").Chance(0.7))
                c.Ellipsoid("backpack", chest, shell.LocalA + new Vec3(0, shell.Radii.Y * 1.12, 0),
                    new Vec3(shell.Radii.X * 0.65, shell.Radii.Y * 0.45, shell.Radii.Z * 0.82), MaterialSlot.Cloth);
            if (c.RandomFor("cap").Chance(0.55))
            {
                var skull = c.FindShell(head);
                if (skull != null)
                    c.Ellipsoid("cap", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.66, 0),
                        new Vec3(skull.Radii.X * 1.05, skull.Radii.Y * 0.37, skull.Radii.Z * 1.1), MaterialSlot.Cloth);
            }
            // Fictional sidearm game prop (visual only, no functioning mechanisms).
            var prop = c.RandomFor("prop");
            double s = prop.Range(1.6, 2.5);
            c.Ellipsoid("prop.body", hand, new Vec3(s * 0.48, 0.15, 0),
                new Vec3(s * 0.8, s * 0.23, s * 0.20), MaterialSlot.Metal);
            c.Ellipsoid("prop.grip", hand, new Vec3(-0.1, -s * 0.34, 0),
                new Vec3(s * 0.18, s * 0.55, s * 0.16), MaterialSlot.Cloth);
        }
    }

    /// <summary>Fictional science-fiction gear: armour shells, luminous core, energy blaster prop.</summary>
    public sealed class SciFiStylePack : IEquipmentStylePack
    {
        public string Id => "scifi";

        public void Build(EquipmentBuildContext c)
        {
            int chest = c.RequireBone(BoneRole.Chest);
            int head = c.RequireBone(BoneRole.Head);
            int hand = c.RequireBone(BoneRole.Hand, 1);
            var s = c.RandomFor("armor");
            c.Cover("breastplate", chest, s.Range(1.12, 1.21), MaterialSlot.Metal);
            var shell = c.FindShell(chest)!;
            c.Ellipsoid("core", chest, shell.LocalA + new Vec3(0, -shell.Radii.Y * 1.17, 0),
                new Vec3(shell.Radii.X * 0.28, 0.5, shell.Radii.Z * 0.27),
                MaterialSlot.Glow, PrimFlags.Emissive);
            foreach (int side in new[] { -1, 1 })
            {
                int shoulder = c.FindBone(BoneRole.ArmUpper, side);
                if (shoulder < 0) continue;
                double size = c.RandomFor("shoulder." + side).Range(1.1, 1.8);
                c.Ellipsoid("shoulder." + side, shoulder, new Vec3(0, 0.5, 0),
                    new Vec3(size * 1.3, size * 0.72, size * 1.25), MaterialSlot.Metal);
            }
            var skull = c.FindShell(head);
            if (skull != null)
            {
                c.Ellipsoid("helmet", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.13, 0),
                    skull.Radii * 1.10, MaterialSlot.Metal);
                c.Ellipsoid("visor", head, skull.LocalA + new Vec3(skull.Radii.X * 0.33, 0, 0),
                    new Vec3(skull.Radii.X * 0.2, skull.Radii.Y * 0.47, skull.Radii.Z * 0.78),
                    MaterialSlot.Glow, PrimFlags.Emissive);
            }
            // Stylized energy weapon: component proportions vary deterministically.
            var w = c.RandomFor("blaster");
            double length = w.Range(2.4, 4.8), width = w.Range(0.38, 0.75);
            c.Ellipsoid("blaster.receiver", hand, new Vec3(length * 0.42, 0, 0),
                new Vec3(length * 0.48, width, width * 0.85), MaterialSlot.Metal);
            c.Rod("blaster.emitter", hand, new Vec3(length * 0.62, 0, 0),
                new Vec3(length * 1.18, 0, 0), width * 0.45, MaterialSlot.Metal);
            c.Ellipsoid("blaster.glow", hand, new Vec3(length * 0.93, width * 0.32, 0),
                new Vec3(length * 0.25, width * 0.22, width * 0.46), MaterialSlot.Glow, PrimFlags.Emissive);
            c.Ellipsoid("blaster.grip", hand, new Vec3(0, -width * 0.7, 0),
                new Vec3(width * 0.46, width * 1.45, width * 0.55), MaterialSlot.Cloth);
        }
    }

    public sealed class WastelandStylePack : IEquipmentStylePack
    {
        public string Id => "wasteland";
        public void Build(EquipmentBuildContext c)
        {
            int chest = c.RequireBone(BoneRole.Chest);
            int hand = c.RequireBone(BoneRole.Hand, 1);
            var shell = c.FindShell(chest)!;
            c.Cover("patched.coat", chest, 1.12, MaterialSlot.Cloth);
            var random = c.RandomFor("scrap.plates");
            int count = random.Range(2, 4);
            for (int i = 0; i < count; i++)
                c.Ellipsoid("scrap.plate." + i, chest,
                    shell.LocalA + new Vec3(random.Range(-0.6, 0.6), -shell.Radii.Y * 1.13, random.Range(-shell.Radii.Z * 0.7, shell.Radii.Z * 0.7)),
                    new Vec3(random.Range(0.7, 1.5), 0.6, random.Range(0.8, 1.4)), MaterialSlot.Metal);
            c.Ellipsoid("scrap.tool", hand, new Vec3(1.2, 0, 0),
                new Vec3(2.2, 0.6, 0.55), MaterialSlot.Metal);
        }
    }
}
