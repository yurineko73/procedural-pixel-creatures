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

    public enum EquipmentSlot
    {
        Outfit,
        Weapon,
        Headgear,
        Accessory,
    }

    /// <summary>
    /// Independent deterministic random streams for equipment categories. Re-rolling one slot
    /// leaves the geometry of all other slots unchanged.
    /// </summary>
    public sealed class EquipmentSeedSet
    {
        public ulong OutfitSeed { get; }
        public ulong WeaponSeed { get; }
        public ulong HeadgearSeed { get; }
        public ulong AccessorySeed { get; }

        public EquipmentSeedSet(ulong outfitSeed, ulong weaponSeed, ulong headgearSeed, ulong accessorySeed)
        {
            OutfitSeed = outfitSeed;
            WeaponSeed = weaponSeed;
            HeadgearSeed = headgearSeed;
            AccessorySeed = accessorySeed;
        }

        public ulong For(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Outfit => OutfitSeed,
            EquipmentSlot.Weapon => WeaponSeed,
            EquipmentSlot.Headgear => HeadgearSeed,
            _ => AccessorySeed,
        };

        public static EquipmentSeedSet FromMaster(ulong seed) => new EquipmentSeedSet(
            StableHash.Derive(seed, "equipment.slot.outfit"),
            StableHash.Derive(seed, "equipment.slot.weapon"),
            StableHash.Derive(seed, "equipment.slot.headgear"),
            StableHash.Derive(seed, "equipment.slot.accessory"));

        public string Fingerprint => $"{OutfitSeed:x16}-{WeaponSeed:x16}-{HeadgearSeed:x16}-{AccessorySeed:x16}";
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
        private readonly EquipmentSeedSet _seeds;
        private readonly List<PartGroup> _groups;
        private readonly List<PrimitiveDef> _shapes;
        private readonly List<string> _traits;
        private readonly string _style;

        internal EquipmentBuildContext(CreatureAnatomy source, string style, EquipmentSeedSet seeds)
        {
            _source = source; _style = style; _seeds = seeds;
            _groups = new List<PartGroup>(source.Groups);
            _shapes = new List<PrimitiveDef>(source.Primitives);
            _traits = new List<string>(source.Traits);
            _traits.Add("equipment.style:" + style);
        }

        /// <summary>Compatibility overload: unclassified plugin parts use the Outfit stream.</summary>
        public Rng RandomFor(string part) => RandomFor(part, EquipmentSlot.Outfit);

        /// <summary>Named per-component stream derived from an independently seeded equipment slot.</summary>
        public Rng RandomFor(string part, EquipmentSlot slot) =>
            Rng.Derive(_seeds.For(slot), "equipment." + _style + "." + slot.ToString().ToLowerInvariant() + "." + part);

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
            ulong key = StableHash.Derive(_source.AnatomyKey, "equipment." + _style + "." + _seeds.Fingerprint);
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
            EquipmentStyleRegistry? registry = null) =>
            Apply(source, styleId, EquipmentSeedSet.FromMaster(seed), registry);

        public static CreatureAnatomy Apply(CreatureAnatomy source, string styleId, EquipmentSeedSet seeds,
            EquipmentStyleRegistry? registry = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));
            if (string.Equals(styleId, "fantasy", StringComparison.OrdinalIgnoreCase)) return source;
            registry ??= EquipmentStyleRegistry.CreateDefault();
            IEquipmentStylePack pack = registry.Get(styleId);
            var context = new EquipmentBuildContext(source, pack.Id, seeds);
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
            var jacket = c.RandomFor("jacket", EquipmentSlot.Outfit);
            c.Cover("jacket", chest, jacket.Range(1.09, 1.18), MaterialSlot.Cloth);
            var shell = c.FindShell(chest)!;
            if (c.RandomFor("vest", EquipmentSlot.Outfit).Chance(0.7))
                c.Ellipsoid("vest", chest, shell.LocalA + new Vec3(0, -shell.Radii.Y * 0.5, 0),
                    new Vec3(shell.Radii.X * 0.75, shell.Radii.Y * 0.5, shell.Radii.Z * 0.92), MaterialSlot.Cloth);
            bool backpackAdded = false;
            if (c.RandomFor("backpack", EquipmentSlot.Accessory).Chance(0.7))
            {
                c.Ellipsoid("backpack", chest, shell.LocalA + new Vec3(0, shell.Radii.Y * 1.12, 0),
                    new Vec3(shell.Radii.X * 0.65, shell.Radii.Y * 0.45, shell.Radii.Z * 0.82), MaterialSlot.Cloth);
                backpackAdded = true;
            }
            var headgear = c.RandomFor("headgear", EquipmentSlot.Headgear);
            var skull = c.FindShell(head);
            if (skull != null)
            {
                int variant = headgear.Range(0, 2);
                if (variant == 0)
                {
                    c.Ellipsoid("cap", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.66, 0),
                        new Vec3(skull.Radii.X * 1.05, skull.Radii.Y * 0.37, skull.Radii.Z * 1.1), MaterialSlot.Cloth);
                }
                else if (variant == 1)
                {
                    c.Ellipsoid("helmet", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.12, 0),
                        skull.Radii * headgear.Range(1.04, 1.12), MaterialSlot.Metal);
                    c.Ellipsoid("helmet.visor", head, skull.LocalA + new Vec3(skull.Radii.X * 0.42, skull.Radii.Y * 0.04, 0),
                        new Vec3(skull.Radii.X * 0.18, skull.Radii.Y * 0.30, skull.Radii.Z * 0.86), MaterialSlot.Glow, PrimFlags.Emissive);
                }
                else
                {
                    c.Ellipsoid("headset.band", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.52, 0),
                        new Vec3(skull.Radii.X * 0.72, skull.Radii.Y * 0.17, skull.Radii.Z * 1.08), MaterialSlot.Metal);
                    foreach (int side in new[] { -1, 1 })
                        c.Ellipsoid("headset.cup." + side, head,
                            skull.LocalA + new Vec3(0, 0, side * skull.Radii.Z * 0.92),
                            new Vec3(skull.Radii.X * 0.22, skull.Radii.Y * 0.34, skull.Radii.Z * 0.22), MaterialSlot.Cloth);
                }
            }
            var accessory = c.RandomFor("radio", EquipmentSlot.Accessory);
            if (accessory.Chance(0.55) || !backpackAdded)
                c.Ellipsoid("radio", chest, shell.LocalA + new Vec3(-shell.Radii.X * 0.36, -shell.Radii.Y * 0.25, shell.Radii.Z * 0.86),
                    new Vec3(shell.Radii.X * 0.18, shell.Radii.Y * 0.22, shell.Radii.Z * 0.20), MaterialSlot.Metal);
            // Fictional sidearm game prop (visual only, no functioning mechanisms).
            var prop = c.RandomFor("prop", EquipmentSlot.Weapon);
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
            var s = c.RandomFor("armor", EquipmentSlot.Outfit);
            c.Cover("breastplate", chest, s.Range(1.12, 1.21), MaterialSlot.Metal);
            var shell = c.FindShell(chest)!;
            c.Ellipsoid("core", chest, shell.LocalA + new Vec3(0, -shell.Radii.Y * 1.17, 0),
                new Vec3(shell.Radii.X * 0.28, 0.5, shell.Radii.Z * 0.27),
                MaterialSlot.Glow, PrimFlags.Emissive);
            foreach (int side in new[] { -1, 1 })
            {
                int shoulder = c.FindBone(BoneRole.ArmUpper, side);
                if (shoulder < 0) continue;
                double size = c.RandomFor("shoulder." + side, EquipmentSlot.Accessory).Range(1.1, 1.8);
                c.Ellipsoid("shoulder." + side, shoulder, new Vec3(0, 0.5, 0),
                    new Vec3(size * 1.3, size * 0.72, size * 1.25), MaterialSlot.Metal);
            }
            var skull = c.FindShell(head);
            if (skull != null)
            {
                var helmet = c.RandomFor("helmet", EquipmentSlot.Headgear);
                c.Ellipsoid("helmet", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.13, 0),
                    skull.Radii * helmet.Range(1.06, 1.16), MaterialSlot.Metal);
                c.Ellipsoid("visor", head, skull.LocalA + new Vec3(skull.Radii.X * helmet.Range(0.28, 0.40), 0, 0),
                    new Vec3(skull.Radii.X * helmet.Range(0.18, 0.27), skull.Radii.Y * helmet.Range(0.38, 0.54), skull.Radii.Z * 0.80),
                    MaterialSlot.Glow, PrimFlags.Emissive);
            }
            // Stylized energy weapon: component proportions vary deterministically.
            var w = c.RandomFor("blaster", EquipmentSlot.Weapon);
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
            int head = c.RequireBone(BoneRole.Head);
            int hand = c.RequireBone(BoneRole.Hand, 1);
            var shell = c.FindShell(chest)!;
            var coat = c.RandomFor("coat", EquipmentSlot.Outfit);
            c.Cover("patched.coat", chest, coat.Range(1.08, 1.18), MaterialSlot.Cloth);
            c.Ellipsoid("coat.collar", chest, shell.LocalA + new Vec3(0, -shell.Radii.Y * 0.68, 0),
                new Vec3(shell.Radii.X * 0.58, shell.Radii.Y * 0.25, shell.Radii.Z * 1.03), MaterialSlot.Cloth);

            var random = c.RandomFor("scrap.plates", EquipmentSlot.Accessory);
            int count = random.Range(2, 4);
            for (int i = 0; i < count; i++)
                c.Ellipsoid("scrap.plate." + i, chest,
                    shell.LocalA + new Vec3(random.Range(-0.6, 0.6), -shell.Radii.Y * 1.13, random.Range(-shell.Radii.Z * 0.7, shell.Radii.Z * 0.7)),
                    new Vec3(random.Range(0.7, 1.5), 0.6, random.Range(0.8, 1.4)), MaterialSlot.Metal);

            var skull = c.FindShell(head);
            if (skull != null)
            {
                var headgear = c.RandomFor("headgear", EquipmentSlot.Headgear);
                int variant = headgear.Range(0, 2);
                if (variant == 0)
                    c.Ellipsoid("bandana", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.48, 0),
                        new Vec3(skull.Radii.X * 0.92, skull.Radii.Y * 0.24, skull.Radii.Z * 1.04), MaterialSlot.Cloth);
                else if (variant == 1)
                {
                    c.Ellipsoid("scrap.helmet", head, skull.LocalA + new Vec3(0, skull.Radii.Y * 0.1, 0),
                        skull.Radii * headgear.Range(1.04, 1.13), MaterialSlot.Metal);
                    c.Ellipsoid("scrap.visor", head, skull.LocalA + new Vec3(skull.Radii.X * 0.4, -skull.Radii.Y * 0.08, 0),
                        new Vec3(skull.Radii.X * 0.16, skull.Radii.Y * 0.24, skull.Radii.Z * 0.82), MaterialSlot.Metal);
                }
                else
                    c.Ellipsoid("respirator", head, skull.LocalA + new Vec3(skull.Radii.X * 0.68, -skull.Radii.Y * 0.30, 0),
                        new Vec3(skull.Radii.X * 0.25, skull.Radii.Y * 0.25, skull.Radii.Z * 0.58), MaterialSlot.Metal);
            }

            var tool = c.RandomFor("scrap.tool", EquipmentSlot.Weapon);
            double length = tool.Range(1.7, 2.6);
            c.Ellipsoid("scrap.tool", hand, new Vec3(length * 0.55, 0, 0),
                new Vec3(length, tool.Range(0.42, 0.72), 0.55), MaterialSlot.Metal);
        }
    }
}
