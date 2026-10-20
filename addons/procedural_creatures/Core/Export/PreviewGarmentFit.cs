// Deterministic preview-specific fallback for loose cloth on extreme/skinny body proportions.
// Keeps the source 2D anatomy untouched and retains belts, pouches and armour details.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class PreviewGarmentFit
    {
        public sealed class Result
        {
            public readonly HashSet<int> ReplacedGroupIndices = new HashSet<int>();
            public readonly HashSet<int> ReplacedPrimitiveIndices = new HashSet<int>();
            public PrimitiveDef? FallbackHem;
        }

        public static Result Evaluate(CreatureAnatomy anatomy,
            IReadOnlyList<ImplicitBodyMesher.MeshData> garments)
        {
            var result = new Result();
            if (!string.Equals(anatomy.FamilyId, "biped", StringComparison.OrdinalIgnoreCase)) return result;

            PrimitiveDef? hips = null, chest = null;
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.Kind != PrimKind.Ellipsoid ||
                    (p.Material != MaterialSlot.Primary && p.Material != MaterialSlot.Secondary)) continue;
                if (p.Tag == "hips") hips = p;
                if (p.Tag == "chest") chest = p;
            }
            if (hips == null) return result;

            double hipWidth = Math.Max(hips.Radii.Y, hips.Radii.Z);
            double chestWidth = chest != null ? Math.Max(chest.Radii.Y, chest.Radii.Z) : hipWidth;
            bool narrow = hipWidth < chestWidth * 0.65;
            bool longLegged = anatomy.Metrics.LegLength > hipWidth * 4.5;

            foreach (ImplicitBodyMesher.MeshData mesh in garments)
            {
                if (mesh.GroupIndex < 0 || mesh.GroupIndex >= anatomy.Groups.Count) continue;
                string name = anatomy.Groups[mesh.GroupIndex].Name;
                if (name != "gear.skirt" && name != "gear.flapF" && name != "gear.flapB") continue;

                // The skirt consists of up to four separate hanging panel chains. On narrow
                // hips/very long legs these can detach visually from the torso, so use a short
                // fitted hem instead of letting four independent, unsupported pieces float.
                bool broken = mesh.BoundaryEdgeCount != 0 || mesh.NonManifoldEdgeCount != 0;
                if (!narrow && !longLegged && !broken) continue;
                result.ReplacedGroupIndices.Add(mesh.GroupIndex);
                foreach (int index in mesh.PrimitiveIndices) result.ReplacedPrimitiveIndices.Add(index);
            }

            // Reuse a single fitted hem for all downgraded hanging groups; do not stack one
            // replacement per panel. The hem follows the pelvis bone and body dimensions.
            if (result.ReplacedGroupIndices.Count > 0)
            {
                result.FallbackHem = new PrimitiveDef
                {
                    Kind = PrimKind.Ellipsoid,
                    BoneA = hips.BoneA,
                    Group = anatomy.FindGroup("gear.skirt") >= 0 ? anatomy.FindGroup("gear.skirt") :
                        anatomy.Groups.Count > 0 ? 0 : -1,
                    LocalA = hips.LocalA + new Vec3(-hips.Radii.X * 0.22, 0, 0),
                    LocalRotation = hips.LocalRotation,
                    Radii = new Vec3(Math.Max(0.65, hips.Radii.X * 0.36),
                        hips.Radii.Y * 1.14, hips.Radii.Z * 1.14),
                    Material = MaterialSlot.Cloth,
                    Flags = PrimFlags.Hard | PrimFlags.NoPattern,
                    Tag = "preview.fitted_short_hem",
                };
            }
            return result;
        }
    }
}
