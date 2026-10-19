// Independent seed routing for the legacy fantasy gear genes.
// Keeps the original BipedFamily schema intact while allowing body and equipment to be re-rolled separately.
using System;
using PixelCreatures.Core;
using PixelCreatures.Core.Genetics;

namespace PixelCreatures.Core.Equipment
{
    public static class NativeGearRandomizer
    {
        private static readonly string[] EquipmentColorGenes =
        {
            "color.clothHue", "color.clothSaturation", "color.clothValue", "color.metal",
        };

        public static CreatureGenome ApplyEquipmentColors(CreatureGenome bodyGenome, ICreatureFamily family, ulong colorSeed)
        {
            ValidateFamily(bodyGenome, family);
            CreatureGenome colorSample = GenomeFactory.Sample(family, colorSeed);
            CreatureGenome result = bodyGenome;
            foreach (string gene in EquipmentColorGenes)
                if (result.Schema.Contains(gene) && colorSample.Schema.Contains(gene))
                    result = result.With(gene, colorSample.Get(gene));
            return result;
        }

        /// <summary>
        /// Replaces only native gear genes. Body morphology and all unrelated genes remain untouched.
        /// gear.size is a legacy shared scale and is therefore controlled by masterSeed, not a single slot.
        /// </summary>
        public static CreatureGenome ApplyNativeGearGenes(CreatureGenome bodyGenome, ICreatureFamily family,
            ulong masterSeed, EquipmentSeedSet seeds)
        {
            ValidateFamily(bodyGenome, family);
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));
            if (!bodyGenome.Schema.Contains("gear.outfit")) return bodyGenome;

            CreatureGenome outfit = GenomeFactory.Sample(family, seeds.OutfitSeed);
            CreatureGenome weapon = GenomeFactory.Sample(family, seeds.WeaponSeed);
            CreatureGenome headgear = GenomeFactory.Sample(family, seeds.HeadgearSeed);
            CreatureGenome accessory = GenomeFactory.Sample(family, seeds.AccessorySeed);
            CreatureGenome master = GenomeFactory.Sample(family, masterSeed);

            return bodyGenome
                .With("gear.outfit", outfit.Get("gear.outfit"))
                .With("gear.weapon", weapon.Get("gear.weapon"))
                .With("gear.helmet", headgear.Get("gear.helmet"))
                .With("gear.shield", accessory.Get("gear.shield"))
                .With("gear.pauldrons", accessory.Get("gear.pauldrons"))
                .With("gear.size", master.Get("gear.size"));
        }

        private static void ValidateFamily(CreatureGenome genome, ICreatureFamily family)
        {
            if (genome == null) throw new ArgumentNullException(nameof(genome));
            if (family == null) throw new ArgumentNullException(nameof(family));
            if (!string.Equals(genome.FamilyId, family.Id, StringComparison.Ordinal) ||
                genome.Schema.Fingerprint() != family.Schema.Fingerprint())
                throw new ArgumentException("Genome and family must use the same family schema.", nameof(family));
        }
    }
}
