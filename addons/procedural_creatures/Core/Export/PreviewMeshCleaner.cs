// Conservative cleanup for accidental microscopic disconnected SDF islands.
// This deliberately does NOT delete legitimate separate clothing/armour/eye pieces: the caller
// runs it only on the fused organic or garment surface, before writing the OBJ.
using System;
using System.Collections.Generic;

namespace PixelCreatures.Core.Export
{
    public static class PreviewMeshCleaner
    {
        public static int RemoveTinyIslands(ImplicitBodyMesher.MeshData mesh)
        {
            if (mesh.Faces.Count < 12) return 0;
            int n = mesh.Vertices.Count;
            var parents = new int[n];
            var ranks = new byte[n];
            for (int i = 0; i < n; i++) parents[i] = i;

            int Find(int a)
            {
                while (parents[a] != a)
                {
                    parents[a] = parents[parents[a]];
                    a = parents[a];
                }
                return a;
            }
            void Join(int a, int b)
            {
                int x = Find(a), y = Find(b);
                if (x == y) return;
                if (ranks[x] < ranks[y]) { int t = x; x = y; y = t; }
                parents[y] = x;
                if (ranks[x] == ranks[y]) ranks[x]++;
            }

            foreach (var f in mesh.Faces)
            {
                Join(f.A, f.B);
                Join(f.B, f.C);
            }

            var counts = new Dictionary<int, int>();
            var lower = new Dictionary<int, PixelCreatures.Core.Vec3>();
            var upper = new Dictionary<int, PixelCreatures.Core.Vec3>();
            int largest = 0;
            foreach (var f in mesh.Faces)
            {
                int root = Find(f.A);
                counts.TryGetValue(root, out int count);
                counts[root] = count + 1;
                if (counts[root] > largest) largest = counts[root];
                foreach (int i in new[] { f.A, f.B, f.C })
                {
                    var v = mesh.Vertices[i];
                    lower[root] = lower.TryGetValue(root, out var lo) ? PixelCreatures.Core.Vec3.Min(lo, v) : v;
                    upper[root] = upper.TryGetValue(root, out var hi) ? PixelCreatures.Core.Vec3.Max(hi, v) : v;
                }
            }

            // Even a tiny but intentional disconnected limb can be a valid anatomical island.
            // Only reject micro-components with both a tiny face count AND a tiny world extent.
            var rejected = new HashSet<int>();
            foreach (var kv in counts)
            {
                if (kv.Value >= largest || kv.Value >= Math.Max(8, largest / 100)) continue;
                if ((upper[kv.Key] - lower[kv.Key]).Length < 1.5)
                    rejected.Add(kv.Key);
            }
            if (rejected.Count == 0) return 0;
            int removed = mesh.Faces.RemoveAll(f => rejected.Contains(Find(f.A)));
            return removed;
        }
    }
}
