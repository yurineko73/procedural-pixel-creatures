// Arthropod legs are too thin for the globally budgeted organic SDF grid: individual
// sub-voxel pieces disappear and look disconnected. Emit one continuous rest-pose bone
// chain per leg instead. All positions are inherited from the original LegRig bones.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public sealed class PreviewSegmentChainFit
    {
        public readonly struct Segment
        {
            public readonly Vec3 Start, End;
            public readonly double StartRadius, EndRadius;
            public readonly MaterialSlot Material;
            public Segment(Vec3 start, Vec3 end, double startRadius, double endRadius, MaterialSlot material)
            {
                Start = start; End = end;
                StartRadius = startRadius; EndRadius = endRadius; Material = material;
            }
        }

        public sealed class Chain
        {
            public int Group;
            public readonly List<Segment> Segments = new List<Segment>();
        }

        public readonly HashSet<int> PrimitiveIndices = new HashSet<int>();
        public readonly List<Chain> Chains = new List<Chain>();

        public static bool ShouldHandle(CreatureAnatomy anatomy, PrimitiveDef p)
        {
            if (!string.Equals(anatomy.FamilyId, "arthropod", StringComparison.OrdinalIgnoreCase) ||
                p.Kind != PrimKind.RoundCone || p.Group < 0 || p.Group >= anatomy.Groups.Count)
                return false;
            string name = anatomy.Groups[p.Group].Name ?? string.Empty;
            if (!name.StartsWith("leg", StringComparison.OrdinalIgnoreCase)) return false;
            string tag = p.Tag ?? string.Empty;
            return tag.EndsWith(".upper", StringComparison.Ordinal) ||
                tag.EndsWith(".lower", StringComparison.Ordinal) ||
                tag.EndsWith(".distal", StringComparison.Ordinal);
        }

        public PreviewSegmentChainFit(CreatureAnatomy anatomy)
        {
            var groups = new SortedDictionary<int, List<(int Index, PrimitiveDef Primitive)>>();
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (!ShouldHandle(anatomy, p)) continue;
                if (!groups.TryGetValue(p.Group, out var group))
                    groups.Add(p.Group, group = new List<(int, PrimitiveDef)>());
                group.Add((i, p));
            }

            foreach (var pair in groups)
            {
                // Only replace a COMPLETE three-segment limb. An incomplete chain should
                // fall back to its original exported geometry, never lose a leg entirely.
                if (pair.Value.Count != 3) continue;
                pair.Value.Sort((a, b) => Order(a.Primitive.Tag).CompareTo(Order(b.Primitive.Tag)));
                if (Order(pair.Value[0].Primitive.Tag) != 0 ||
                    Order(pair.Value[1].Primitive.Tag) != 1 ||
                    Order(pair.Value[2].Primitive.Tag) != 2) continue;

                var chain = new Chain { Group = pair.Key };
                Vec3 previousEnd = Vec3.Zero;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    var item = pair.Value[i];
                    PrimitiveDef p = item.Primitive;
                    Vec3 start = anatomy.Skeleton.RestWorld(p.BoneA).Point(p.LocalA);
                    Vec3 end = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    // Shared joint is authoritative. This closes gaps from tiny floating
                    // point/bone rest-pose differences without shifting the rest of the leg.
                    if (i > 0) start = previousEnd;
                    double ra = Math.Max(0.65, p.RadiusA);
                    double rb = Math.Max(0.60, p.RadiusB);
                    if ((end - start).Length <= 1e-5) break;
                    chain.Segments.Add(new Segment(start, end, ra, rb, p.Material));
                    previousEnd = end;
                }
                if (chain.Segments.Count != 3) continue;
                Chains.Add(chain);
                foreach (var item in pair.Value) PrimitiveIndices.Add(item.Index);
            }
        }

        private static int Order(string tag) =>
            tag.EndsWith(".upper", StringComparison.Ordinal) ? 0 :
            tag.EndsWith(".lower", StringComparison.Ordinal) ? 1 :
            tag.EndsWith(".distal", StringComparison.Ordinal) ? 2 : 3;
    }
}
