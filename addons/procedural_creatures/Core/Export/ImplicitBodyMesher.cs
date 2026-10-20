// Experimental implicit union for the static 3D preview.
// The 2D renderer blends overlapping primitives; exporting every ellipsoid as a closed shell
// exposes internal faces in 3D. This mesher unions organic primary/secondary shapes into one skin.
// Equipment, hard details, triangles, and interior shapes stay as separate meshes.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class ImplicitBodyMesher
    {
        // A little more blending is needed in 3D than in the 2D depth renderer: separate limb
        // shells must actually meet at joints instead of leaving visible seams in the static mesh.
        private const double Smoothness = 1.15;
        private const double ClothingSmoothness = 0.4;
        private const double TargetStep = 1.25;
        private const double ClothingTargetStep = 0.8;
        private const long MaxGridPoints = 260_000;

        public sealed class Face
        {
            public int A { get; }
            public int B { get; }
            public int C { get; }
            public MaterialSlot Material { get; }

            public Face(int a, int b, int c, MaterialSlot material)
            {
                A = a; B = b; C = c; Material = material;
            }
        }

        public sealed class MeshData
        {
            public List<Vec3> Vertices { get; } = new List<Vec3>();
            public List<Face> Faces { get; } = new List<Face>();
            public HashSet<int> PrimitiveIndices { get; } = new HashSet<int>();
            public int GroupIndex { get; set; } = -1;
            public int BoundaryEdgeCount { get; internal set; }
            public int NonManifoldEdgeCount { get; internal set; }
        }

        private sealed class Shape
        {
            public int PrimitiveIndex;
            public MaterialSlot Material;
            public PrimKind Kind;
            public Xform BoneA;
            public Xform BoneB;
            public Vec3 Center;
            public Quat InverseRotation;
            public Vec3 Radii;
            public Vec3 A;
            public Vec3 B;
            public double RadiusA;
            public double RadiusB;
        }

        private static readonly int[][] Tetrahedra =
        {
            new[] { 0, 5, 1, 6 },
            new[] { 0, 1, 2, 6 },
            new[] { 0, 2, 3, 6 },
            new[] { 0, 3, 7, 6 },
            new[] { 0, 7, 4, 6 },
            new[] { 0, 4, 5, 6 },
        };

        public static bool ShouldUnify(CreatureAnatomy anatomy, PrimitiveDef primitive)
        {
            if (primitive.Kind != PrimKind.Ellipsoid && primitive.Kind != PrimKind.RoundCone) return false;
            if (primitive.Material != MaterialSlot.Primary && primitive.Material != MaterialSlot.Secondary &&
                primitive.Material != MaterialSlot.Accent && primitive.Material != MaterialSlot.White) return false;
            // Faceted is a 2D shading instruction, not a reason to leave overlapping closed shells
            // in the 3D preview. Hard/thin/translucent pieces (claws, plates, membranes) stay separate.
            if ((primitive.Flags & (PrimFlags.Hard | PrimFlags.Thin | PrimFlags.Translucent | PrimFlags.FrontOnly)) != 0) return false;
            if (primitive.Group < 0 || primitive.Group >= anatomy.Groups.Count) return false;

            PartGroup group = anatomy.Groups[primitive.Group];
            if ((group.Flags & GroupFlags.Interior) != 0) return false;
            string name = group.Name ?? string.Empty;
            // Apply semantic separation to ALL material slots. Otherwise primary-coloured
            // horns, feelers and tentacles inadvertently join the global body SDF even
            // though identical Accent variants are left as standalone meshes.
            if (IsEquipmentGroup(name) || IsStandaloneDetailGroup(name)) return false;
            return true;
        }

        private static bool IsEquipmentGroup(string name) =>
            name.StartsWith("gear", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("equipment", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("pauldron", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsStandaloneDetailGroup(string name)
        {
            // Accent/white soft anatomy (muzzles, beaks, cheeks, paws and hooves) belongs to the
            // fused skin. Truly separate appendages and facial hardware must retain their own mesh.
            string[] tokens = { "horn", "ear.", ".ear", "antenna", "whisker", "claw", "teeth", "fang",
                "tusk", "mandible", "pincer", "stinger", "spike", "thorn", "wing", "fin", "membrane",
                "shell", "crown", "crest", "leaf", "tentacle", "eyestalk", "stalkeye" };
            foreach (string token in tokens)
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static MeshData Build(CreatureAnatomy anatomy) =>
            BuildSelected(anatomy, p => ShouldUnify(anatomy, p), Smoothness, TargetStep, -1, true);

        /// <summary>
        /// Fuses the layered ellipsoids that make up soft garments into one fitted surface per
        /// garment group. Metal armour is fused only for the breastplate; helmets, shields,
        /// pauldrons, buckles and other hard gear remain independent.
        /// </summary>
        public static List<MeshData> BuildClothingGroups(CreatureAnatomy anatomy)
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));
            var result = new List<MeshData>();
            for (int groupIndex = 0; groupIndex < anatomy.Groups.Count; groupIndex++)
            {
                int capturedGroup = groupIndex;
                string name = anatomy.Groups[capturedGroup].Name ?? string.Empty;
                if (!IsGarmentGroup(name)) continue;
                var mesh = BuildSelected(anatomy,
                    p => p.Group == capturedGroup && ShouldUnifyClothing(anatomy, p),
                    ClothingSmoothness, ClothingTargetStep, capturedGroup, false);
                if (mesh.Faces.Count > 0) result.Add(mesh);
            }
            return result;
        }

        private static bool IsGarmentGroup(string name) =>
            name == "gear.tunic" ||
            name == "gear.undercoat" ||
            name == "gear.breastplate" ||
            name == "gear.robe" ||
            name == "gear.skirt" ||
            name == "gear.flapF" ||
            name == "gear.flapB";

        private static bool ShouldUnifyClothing(CreatureAnatomy anatomy, PrimitiveDef primitive)
        {
            if (primitive.Kind != PrimKind.Ellipsoid && primitive.Kind != PrimKind.RoundCone) return false;
            if ((primitive.Flags & (PrimFlags.Hard | PrimFlags.Faceted | PrimFlags.Thin | PrimFlags.Translucent | PrimFlags.FrontOnly)) != 0) return false;
            if (primitive.Group < 0 || primitive.Group >= anatomy.Groups.Count) return false;
            string name = anatomy.Groups[primitive.Group].Name ?? string.Empty;
            if (!IsGarmentGroup(name)) return false;
            if (name == "gear.breastplate") return primitive.Material == MaterialSlot.Metal;
            return primitive.Material == MaterialSlot.Cloth;
        }

        private static MeshData BuildSelected(CreatureAnatomy anatomy, Func<PrimitiveDef, bool> include,
            double smoothness, double targetStep, int groupIndex, bool cleanTinyIslands)
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));
            var mesh = new MeshData { GroupIndex = groupIndex };
            var shapes = new List<Shape>();
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (!include(p)) continue;

                var shape = new Shape
                {
                    PrimitiveIndex = i,
                    Material = p.Material,
                    Kind = p.Kind,
                    BoneA = anatomy.Skeleton.RestWorld(p.BoneA),
                };
                if (p.Kind == PrimKind.Ellipsoid)
                {
                    shape.Center = p.LocalA;
                    shape.InverseRotation = p.LocalRotation.Conjugate;
                    shape.Radii = p.Radii;
                    Vec3 center = shape.BoneA.Point(p.LocalA);
                    double radius = Math.Max(p.Radii.X, Math.Max(p.Radii.Y, p.Radii.Z));
                    ExpandBounds(center, radius, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                }
                else
                {
                    shape.BoneB = anatomy.Skeleton.RestWorld(p.BoneB);
                    shape.A = shape.BoneA.Point(p.LocalA);
                    shape.B = shape.BoneB.Point(p.LocalB);
                    shape.RadiusA = p.RadiusA;
                    shape.RadiusB = p.RadiusB;
                    double radius = Math.Max(p.RadiusA, p.RadiusB);
                    ExpandBounds(shape.A, radius, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    ExpandBounds(shape.B, radius, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                }
                shapes.Add(shape);
                mesh.PrimitiveIndices.Add(i);
            }

            if (shapes.Count == 0) return mesh;
            // Closed-mouth 3D preview: the 2D mouth interior is deliberately hidden under
            // GroupFlags.Interior. Subtract a shallow seam from the rest-pose outer skin.
            List<Shape> mouthCutters = groupIndex < 0 ? BuildMouthCutters(anatomy) : new List<Shape>();

            double margin = smoothness + targetStep;
            minX -= margin; minY -= margin; minZ -= margin;
            maxX += margin; maxY += margin; maxZ += margin;
            double step = targetStep;
            int nx, ny, nz;
            while (true)
            {
                nx = Math.Max(2, (int)Math.Ceiling((maxX - minX) / step) + 1);
                ny = Math.Max(2, (int)Math.Ceiling((maxY - minY) / step) + 1);
                nz = Math.Max(2, (int)Math.Ceiling((maxZ - minZ) / step) + 1);
                if ((long)nx * ny * nz <= MaxGridPoints) break;
                step *= 1.15;
            }

            int pointCount = checked(nx * ny * nz);
            var points = new Vec3[pointCount];
            var distances = new double[pointCount];
            int Index(int x, int y, int z) => (x * ny + y) * nz + z;

            for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                int index = Index(x, y, z);
                Vec3 point = new Vec3(minX + x * step, minY + y * step, minZ + z * step);
                points[index] = point;
                distances[index] = Field(point, shapes, mouthCutters, smoothness);
            }

            var edgeVertices = new Dictionary<long, int>();
            var exactGridVertices = new Dictionary<int, int>();
            int[] cube = new int[8];
            int[] tetra = new int[4];
            int[] inside = new int[4];
            int[] outside = new int[4];
            for (int x = 0; x < nx - 1; x++)
            for (int y = 0; y < ny - 1; y++)
            for (int z = 0; z < nz - 1; z++)
            {
                cube[0] = Index(x, y, z);
                cube[1] = Index(x + 1, y, z);
                cube[2] = Index(x + 1, y + 1, z);
                cube[3] = Index(x, y + 1, z);
                cube[4] = Index(x, y, z + 1);
                cube[5] = Index(x + 1, y, z + 1);
                cube[6] = Index(x + 1, y + 1, z + 1);
                cube[7] = Index(x, y + 1, z + 1);

                foreach (int[] split in Tetrahedra)
                {
                    for (int i = 0; i < 4; i++) tetra[i] = cube[split[i]];
                    EmitTetra(tetra, points, distances, inside, outside, edgeVertices, exactGridVertices, mesh, shapes, mouthCutters, smoothness);
                }
            }

            // Only the fused body gets micro-island cleanup. Garment groups may intentionally
            // contain several separate skirt/flap panels and must not lose those pieces.
            if (cleanTinyIslands) PreviewMeshCleaner.RemoveTinyIslands(mesh);
            if (mesh.Faces.Count == 0)
            {
                mesh.PrimitiveIndices.Clear();
                mesh.Vertices.Clear();
                return mesh;
            }

            // A closed opaque skin should use every surface edge exactly twice. Keep these
            // counters available to regression tests so extraction cracks cannot go unnoticed.
            var edgeUse = new Dictionary<long, int>();
            foreach (Face face in mesh.Faces)
            {
                CountEdge(edgeUse, face.A, face.B);
                CountEdge(edgeUse, face.B, face.C);
                CountEdge(edgeUse, face.C, face.A);
            }
            foreach (int uses in edgeUse.Values)
            {
                if (uses == 1) mesh.BoundaryEdgeCount++;
                else if (uses != 2) mesh.NonManifoldEdgeCount++;
            }
            return mesh;
        }

        private static void CountEdge(Dictionary<long, int> edgeUse, int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            long key = ((long)low << 32) | (uint)high;
            edgeUse.TryGetValue(key, out int count);
            edgeUse[key] = count + 1;
        }

        private static void ExpandBounds(Vec3 p, double radius,
            ref double minX, ref double minY, ref double minZ,
            ref double maxX, ref double maxY, ref double maxZ)
        {
            minX = Math.Min(minX, p.X - radius); minY = Math.Min(minY, p.Y - radius); minZ = Math.Min(minZ, p.Z - radius);
            maxX = Math.Max(maxX, p.X + radius); maxY = Math.Max(maxY, p.Y + radius); maxZ = Math.Max(maxZ, p.Z + radius);
        }

        private static double Field(Vec3 point, List<Shape> shapes, List<Shape> mouthCutters, double smoothness)
        {
            double result = OrganicField(point, shapes, smoothness);
            // Hard subtraction keeps the slit open at low detail levels.
            foreach (Shape mouth in mouthCutters)
                result = Math.Max(result, -Distance(point, mouth));
            return result;
        }

        private static double OrganicField(Vec3 point, List<Shape> shapes, double smoothness)
        {
            double result = double.PositiveInfinity;
            foreach (Shape shape in shapes)
            {
                double d = Distance(point, shape);
                result = double.IsPositiveInfinity(result) ? d : SmoothMin(result, d, smoothness);
            }
            return result;
        }

        private static List<Shape> BuildMouthCutters(CreatureAnatomy anatomy)
        {
            var cutters = new List<Shape>();
            // Only a head that actually has an authored mouth interior and jaw needs this
            // rest-pose slit; do not carve out of eyes, mandibles or decorative skulls.
            foreach (PrimitiveDef cavity in anatomy.Primitives)
            {
                if (cavity.Group < 0 || cavity.Group >= anatomy.Groups.Count ||
                    !cavity.Tag.EndsWith(".mouthInterior", StringComparison.Ordinal)) continue;
                int headBone = cavity.BoneA;
                bool jaw = false;
                PrimitiveDef? snout = null, skull = null;
                foreach (PrimitiveDef p in anatomy.Primitives)
                {
                    if (p.BoneA != headBone) continue;
                    if (p.Tag.EndsWith(".snout", StringComparison.Ordinal) &&
                        p.Kind == PrimKind.RoundCone) snout = p;
                    if (p.Tag.EndsWith(".cranium", StringComparison.Ordinal) &&
                        p.Kind == PrimKind.Ellipsoid) skull = p;
                }
                foreach (PrimitiveDef p in anatomy.Primitives)
                    if (p.Tag.EndsWith(".jawbone", StringComparison.Ordinal) &&
                        p.BoneA >= 0 && p.BoneA < anatomy.Skeleton.Count &&
                        anatomy.Skeleton.Bones[p.BoneA].Parent == headBone) { jaw = true; break; }
                if (!jaw || (snout == null && skull == null)) continue;
                Vec3 center, radii;
                if (snout != null)
                {
                    Vec3 axis = snout.LocalB - snout.LocalA;
                    double length = axis.Length;
                    double radius = Math.Max(0.8, snout.RadiusB);
                    center = Vec3.Lerp(snout.LocalA, snout.LocalB, 0.70) +
                        new Vec3(0, -radius * 0.88, 0);
                    radii = new Vec3(Math.Max(1.0, length * 0.52),
                        Math.Max(0.35, radius * 0.21),
                        Math.Max(0.65, radius * 0.95));
                }
                else
                {
                    // Short humanlike faces may have no separate snout primitive.
                    center = skull!.LocalA + new Vec3(skull.Radii.X * 0.91,
                        -skull.Radii.Y * 0.48, 0);
                    radii = new Vec3(Math.Max(0.5, skull.Radii.X * 0.22),
                        Math.Max(0.32, skull.Radii.Y * 0.10),
                        Math.Max(0.65, skull.Radii.Z * 0.65));
                }
                cutters.Add(new Shape
                {
                    Kind = PrimKind.Ellipsoid, BoneA = anatomy.Skeleton.RestWorld(headBone),
                    Center = center, Radii = radii, InverseRotation = Quat.Identity,
                    Material = MaterialSlot.Mouth
                });
            }
            return cutters;
        }

        private static double Distance(Vec3 point, Shape shape)
        {
            if (shape.Kind == PrimKind.Ellipsoid)
            {
                Vec3 local = shape.InverseRotation.Rotate(shape.BoneA.InversePoint(point) - shape.Center);
                double x = local.X, y = local.Y, z = local.Z;
                double rx = Math.Max(0.001, shape.Radii.X);
                double ry = Math.Max(0.001, shape.Radii.Y);
                double rz = Math.Max(0.001, shape.Radii.Z);
                double k0 = Math.Sqrt(x * x / (rx * rx) + y * y / (ry * ry) + z * z / (rz * rz));
                double k1 = Math.Sqrt(x * x / (rx * rx * rx * rx) + y * y / (ry * ry * ry * ry) + z * z / (rz * rz * rz * rz));
                return k1 > 1e-9 ? k0 * (k0 - 1.0) / k1 : -Math.Min(rx, Math.Min(ry, rz));
            }

            Vec3 ab = shape.B - shape.A;
            double lengthSquared = ab.LengthSquared;
            double t = lengthSquared > 1e-12 ? Vec3.Dot(point - shape.A, ab) / lengthSquared : 0;
            t = Math.Max(0, Math.Min(1, t));
            Vec3 nearest = shape.A + ab * t;
            double radius = shape.RadiusA + (shape.RadiusB - shape.RadiusA) * t;
            return (point - nearest).Length - radius;
        }

        private static double SmoothMin(double a, double b, double k)
        {
            double h = Math.Max(0, Math.Min(1, 0.5 + 0.5 * (b - a) / k));
            return b + (a - b) * h - k * h * (1 - h);
        }

        private static void EmitTetra(int[] tetra, Vec3[] points, double[] distances,
            int[] inside, int[] outside, Dictionary<long, int> edgeVertices,
            Dictionary<int, int> exactGridVertices, MeshData mesh, List<Shape> shapes,
            List<Shape> mouthCutters, double smoothness)
        {
            int inCount = 0, outCount = 0;
            for (int i = 0; i < 4; i++)
            {
                int node = tetra[i];
                if (distances[node] < 0) inside[inCount++] = node;
                else outside[outCount++] = node;
            }
            if (inCount == 0 || inCount == 4) return;

            if (inCount == 1)
            {
                int a = inside[0], b = outside[0], c = outside[1], d = outside[2];
                int v0 = EdgeVertex(a, b, points, distances, edgeVertices, exactGridVertices, mesh);
                int v1 = EdgeVertex(a, c, points, distances, edgeVertices, exactGridVertices, mesh);
                int v2 = EdgeVertex(a, d, points, distances, edgeVertices, exactGridVertices, mesh);
                Vec3 outward = (points[b] + points[c] + points[d]) / 3.0 - points[a];
                AddTriangle(v0, v1, v2, outward, mesh, shapes, mouthCutters, smoothness);
                return;
            }

            if (inCount == 3)
            {
                int a = outside[0], b = inside[0], c = inside[1], d = inside[2];
                int v0 = EdgeVertex(a, b, points, distances, edgeVertices, exactGridVertices, mesh);
                int v1 = EdgeVertex(a, c, points, distances, edgeVertices, exactGridVertices, mesh);
                int v2 = EdgeVertex(a, d, points, distances, edgeVertices, exactGridVertices, mesh);
                Vec3 outward = points[a] - (points[b] + points[c] + points[d]) / 3.0;
                AddTriangle(v0, v1, v2, outward, mesh, shapes, mouthCutters, smoothness);
                return;
            }

            int i0 = inside[0], i1 = inside[1], o0 = outside[0], o1 = outside[1];
            int p00 = EdgeVertex(i0, o0, points, distances, edgeVertices, exactGridVertices, mesh);
            int p01 = EdgeVertex(i0, o1, points, distances, edgeVertices, exactGridVertices, mesh);
            int p10 = EdgeVertex(i1, o0, points, distances, edgeVertices, exactGridVertices, mesh);
            int p11 = EdgeVertex(i1, o1, points, distances, edgeVertices, exactGridVertices, mesh);
            Vec3 direction = (points[o0] + points[o1] - points[i0] - points[i1]) * 0.5;
            AddTriangle(p00, p01, p10, direction, mesh, shapes, mouthCutters, smoothness);
            AddTriangle(p01, p11, p10, direction, mesh, shapes, mouthCutters, smoothness);
        }

        private static int EdgeVertex(int a, int b, Vec3[] points, double[] distances,
            Dictionary<long, int> edgeVertices, Dictionary<int, int> exactGridVertices, MeshData mesh)
        {
            double da = distances[a], db = distances[b];
            // If the isosurface passes exactly through a sampled grid point, all intersecting
            // tetrahedron edges must reuse that one vertex. Edge-only caching duplicates it and
            // leaves tiny cracks / non-manifold seams in the otherwise closed skin.
            if (Math.Abs(da) < 1e-10) return ExactGridVertex(a, points, exactGridVertices, mesh);
            if (Math.Abs(db) < 1e-10) return ExactGridVertex(b, points, exactGridVertices, mesh);

            int low = Math.Min(a, b), high = Math.Max(a, b);
            long key = ((long)low << 32) | (uint)high;
            if (edgeVertices.TryGetValue(key, out int existing)) return existing;
            double denominator = da - db;
            double t = Math.Abs(denominator) > 1e-12 ? da / denominator : 0.5;
            t = Math.Max(0, Math.Min(1, t));
            int index = mesh.Vertices.Count;
            mesh.Vertices.Add(Vec3.Lerp(points[a], points[b], t));
            edgeVertices.Add(key, index);
            return index;
        }

        private static int ExactGridVertex(int gridIndex, Vec3[] points,
            Dictionary<int, int> exactGridVertices, MeshData mesh)
        {
            if (exactGridVertices.TryGetValue(gridIndex, out int existing)) return existing;
            int index = mesh.Vertices.Count;
            mesh.Vertices.Add(points[gridIndex]);
            exactGridVertices.Add(gridIndex, index);
            return index;
        }

        private static void AddTriangle(int a, int b, int c, Vec3 approximateOutward,
            MeshData mesh, List<Shape> shapes, List<Shape> mouthCutters, double smoothness)
        {
            Vec3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c];
            Vec3 cross = Vec3.Cross(pb - pa, pc - pa);
            if (cross.LengthSquared < 1e-10) return;
            Vec3 center = (pa + pb + pc) / 3.0;
            Vec3 outward = FieldGradient(center, shapes, mouthCutters, smoothness);
            if (outward.LengthSquared < 1e-10) outward = approximateOutward;
            if (Vec3.Dot(cross, outward) < 0) { int swap = b; b = c; c = swap; }
            MaterialSlot material = MaterialAt(center, shapes, smoothness);
            double bodyDistance = OrganicField(center, shapes, smoothness);
            foreach (Shape mouth in mouthCutters)
            {
                double cut = Distance(center, mouth);
                // The newly exposed inner cavity wall is visibly dark; the surrounding
                // organic skin continues to use its original Primary/Secondary palette.
                if (Math.Abs(cut) < 0.8 && -cut >= bodyDistance - 0.08)
                {
                    material = MaterialSlot.Mouth;
                    break;
                }
            }
            mesh.Faces.Add(new Face(a, b, c, material));
        }

        private static Vec3 FieldGradient(Vec3 point, List<Shape> shapes, List<Shape> mouthCutters, double smoothness)
        {
            double h = Math.Max(0.25, smoothness * 0.5);
            Vec3 dx = new Vec3(h, 0, 0);
            Vec3 dy = new Vec3(0, h, 0);
            Vec3 dz = new Vec3(0, 0, h);
            return new Vec3(
                Field(point + dx, shapes, mouthCutters, smoothness) - Field(point - dx, shapes, mouthCutters, smoothness),
                Field(point + dy, shapes, mouthCutters, smoothness) - Field(point - dy, shapes, mouthCutters, smoothness),
                Field(point + dz, shapes, mouthCutters, smoothness) - Field(point - dz, shapes, mouthCutters, smoothness));
        }

        private static MaterialSlot MaterialAt(Vec3 point, List<Shape> shapes, double smoothness)
        {
            double best = double.PositiveInfinity;
            MaterialSlot material = MaterialSlot.Primary;
            foreach (Shape shape in shapes)
            {
                double signedDistance = Distance(point, shape);
                // Do not let a deeply buried ellipsoid paint the visible outer surface merely
                // because its internal surface happens to be close in absolute distance.
                double buried = Math.Max(0.0, -signedDistance - smoothness * 0.25);
                double score = Math.Abs(signedDistance) + buried * 4.0;
                if (score < best) { best = score; material = shape.Material; }
            }
            return material;
        }
    }
}
