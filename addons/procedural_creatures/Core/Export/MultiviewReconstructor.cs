// Experimental 2D-guided static surface reconstruction.
// Unlike ImplicitBodyMesher, the field comes from the actual 2D renderer's multi-view silhouettes
// and front-depth buffers. It never invokes OBJ primitive fitting or guessed mouth/toe offsets.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Palettes;
using PixelCreatures.Core.Rendering;

namespace PixelCreatures.Core.Export
{
    public sealed class MultiviewOptions
    {
        public int Directions = 16;
        public double GridStep = 1.2;
        public int MaxGridPoints = 320000;
        public double SilhouetteSlack = 0.4;
        public double DepthSlack = 0.45;
        /// <summary>Number of gentle scalar-field smoothing passes before mesh extraction.</summary>
        public int FieldSmoothingPasses = 1;
        /// <summary>Blend of a 1-2-1 neighborhood filter into the field (0..1).</summary>
        public double FieldSmoothingStrength = 0.65;
    }

    public sealed class MultiviewResult
    {
        public readonly List<Vec3> Vertices = new List<Vec3>();
        public readonly List<(int A, int B, int C)> Triangles = new List<(int, int, int)>();
        public int ViewCount;
        public double GridStep;
        public int SampledGridPoints;

        public string ToObj(PixelDepthCapture[] captures, CreaturePalette palette, CreatureAnatomy anatomy, double scale = 0.01)
        {
            var inv = CultureInfo.InvariantCulture;
            var obj = new StringBuilder("# Pixel-depth multi-view reconstructed experimental mesh\n");
            obj.AppendLine("mtllib creature.mtl");
            obj.AppendLine("o multiview_surface");
            foreach (Vec3 p in Vertices)
                obj.Append("v ").Append((p.X * scale).ToString("G9", inv)).Append(' ')
                    .Append((p.Y * scale).ToString("G9", inv)).Append(' ')
                    .Append((p.Z * scale).ToString("G9", inv)).AppendLine();
            // Reuse each camera transform for all faces; deterministic trigonometry is expensive.
            var rotations = new Mat3[captures.Length];
            var offsets = new Vec3[captures.Length];
            var eyes = new Vec3[captures.Length];
            for (int v = 0; v < captures.Length; v++)
            {
                var cap = captures[v];
                rotations[v] = PixelCamera.WorldToView(cap.View.Elevation) * PixelCamera.YawRotation(cap.View.Yaw);
                offsets[v] = PixelCamera.WorldToView(cap.View.Elevation).Mul(cap.View.RootOffset);
                eyes[v] = rotations[v].Transposed.Mul(Vec3.UnitZ);
            }
            int previous = -1;
            foreach (var f in Triangles)
            {
                Vec3 a = Vertices[f.A], b = Vertices[f.B], c = Vertices[f.C];
                Vec3 normal = Vec3.Cross(b - a, c - a).NormalizedOr(Vec3.UnitY);
                MaterialSlot material = MultiviewReconstructor.MaterialAt(
                    (a + b + c) / 3.0, normal, captures, rotations, offsets, eyes, palette, anatomy);
                int materialId = (int)material;
                if (materialId != previous)
                {
                    obj.Append("usemtl ").Append(material.ToString()).AppendLine();
                    previous = materialId;
                }
                obj.Append("f ").Append(f.A + 1).Append(' ').Append(f.B + 1)
                    .Append(' ').Append(f.C + 1).AppendLine();
            }
            // Facial features are explicit geometry anchored to the original anatomy bones;
            // rasterized eye pixels alone cannot create a physical eye or nostril.
            ObjMeshExporter.AppendFeatures(obj, anatomy, Vertices.Count, sides: 20, rings: 12, unitsPerPixel: scale);
            return obj.ToString();
        }

        public static string MaterialLibrary(CreaturePalette palette)
        {
            var sb = new StringBuilder("# Semantic materials sampled from the original anatomy\n");
            foreach (MaterialSlot slot in Enum.GetValues(typeof(MaterialSlot)))
            {
                if (slot == MaterialSlot.Count) continue;
                Rgba c = palette.Color(slot, Shade.Base);
                sb.Append("newmtl ").Append(slot.ToString()).AppendLine();
                sb.Append("Kd ").Append((c.R / 255.0).ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((c.G / 255.0).ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((c.B / 255.0).ToString("F6", CultureInfo.InvariantCulture)).AppendLine();
                sb.AppendLine("Ka 0 0 0\nKs 0 0 0\nillum 1");
            }
            AddFeatureMaterial(sb, "FeatureOutline", "0.025 0.02 0.035");
            AddFeatureMaterial(sb, "FeatureSocket", "0.07 0.045 0.07");
            AddFeatureMaterial(sb, "FeatureSclera", "0.92 0.87 0.77");
            AddFeatureMaterial(sb, "FeatureIris", "0.48 0.22 0.16");
            AddFeatureMaterial(sb, "FeaturePupil", "0.018 0.012 0.025");
            AddFeatureMaterial(sb, "FeatureHighlight", "1.0 0.96 0.86");
            AddFeatureMaterial(sb, "FeatureGlow", "0.2 0.85 1.0");
            AddFeatureMaterial(sb, "FeatureNostril", "0.06 0.025 0.035");
            return sb.ToString();
        }

        private static void AddFeatureMaterial(StringBuilder sb, string name, string color)
        {
            sb.Append("newmtl ").AppendLine(name);
            sb.Append("Kd ").AppendLine(color);
            sb.AppendLine("Ka 0.02 0.02 0.02\nKs 0 0 0\nillum 1");
        }
    }

    public static class MultiviewReconstructor
    {
        private const double Far = 1e8;
        private static readonly int[][] Tetrahedra =
        {
            new[] { 0, 5, 1, 6 }, new[] { 0, 1, 2, 6 },
            new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 },
            new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 }
        };

        private sealed class ViewData
        {
            public PixelDepthCapture Capture = null!;
            public float[] Distance = Array.Empty<float>();
            public Mat3 Transform;
            public Vec3 Offset;
            public Vec3 CameraToSurface;
        }

        public static PixelDepthCapture[] Capture(CreatureModel model, int directions = 16)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (directions < 4 || directions > 64) throw new ArgumentOutOfRangeException(nameof(directions));
            var list = new PixelDepthCapture[directions];
            var pose = new CreaturePose(model.Anatomy.Skeleton);
            var renderer = new CreatureRenderer();
            var canvas = model.Anatomy.Canvas;
            var image = new PixelFrame(canvas.Width, canvas.Height)
            {
                OriginX = canvas.OriginX, OriginY = canvas.OriginY
            };
            for (int direction = 0; direction < directions; direction++)
            {
                var view = ViewSpec.Default(direction * DMath.TwoPi / directions);
                view.DrawShadow = false;
                renderer.Render(model.Anatomy, model.Palette, model.Surface, pose, view, image);
                list[direction] = renderer.CaptureDepth(image, view);
            }
            return list;
        }

        public static MultiviewResult Build(PixelDepthCapture[] captures, MultiviewOptions? options = null)
        {
            options ??= new MultiviewOptions();
            if (captures == null || captures.Length < 4) throw new ArgumentException("At least four views are required.", nameof(captures));
            if (options.GridStep <= 0 || options.MaxGridPoints < 1000 ||
                options.SilhouetteSlack < 0 || options.DepthSlack < 0 || options.FieldSmoothingPasses < 0 || options.FieldSmoothingStrength < 0 || options.FieldSmoothingStrength > 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid reconstruction resolution or tolerance.");
            var views = new ViewData[captures.Length];
            Vec3 lower = new Vec3(Far, Far, Far), upper = new Vec3(-Far, -Far, -Far);
            int visible = 0;
            for (int v = 0; v < captures.Length; v++)
            {
                var cap = captures[v];
                Mat3 worldToView = PixelCamera.WorldToView(cap.View.Elevation) * PixelCamera.YawRotation(cap.View.Yaw);
                views[v] = new ViewData
                {
                    Capture = cap, Transform = worldToView,
                    Offset = PixelCamera.WorldToView(cap.View.Elevation).Mul(cap.View.RootOffset),
                    CameraToSurface = worldToView.Transposed.Mul(Vec3.UnitZ),
                    Distance = SignedSilhouetteDistance(cap)
                };
                for (int y = 0; y < cap.Height; y++)
                    for (int x = 0; x < cap.Width; x++)
                    {
                        if (!cap.Visible[y * cap.Width + x]) continue;
                        Vec3 point = cap.BackProject(x, y);
                        lower = new Vec3(Math.Min(lower.X, point.X), Math.Min(lower.Y, point.Y), Math.Min(lower.Z, point.Z));
                        upper = new Vec3(Math.Max(upper.X, point.X), Math.Max(upper.Y, point.Y), Math.Max(upper.Z, point.Z));
                        visible++;
                    }
            }
            if (visible == 0) throw new InvalidOperationException("Original 2D renderer yielded no body pixels.");
            double margin = Math.Max(3.0, options.GridStep * 2);
            lower -= new Vec3(margin, margin, margin);
            upper += new Vec3(margin, margin, margin);
            double step = options.GridStep;
            int nx, ny, nz;
            while (true)
            {
                nx = (int)Math.Ceiling((upper.X - lower.X) / step);
                ny = (int)Math.Ceiling((upper.Y - lower.Y) / step);
                nz = (int)Math.Ceiling((upper.Z - lower.Z) / step);
                long nodes = (long)(nx + 1) * (ny + 1) * (nz + 1);
                if (nodes <= options.MaxGridPoints) break;
                step *= Math.Max(1.05, Math.Pow(nodes / (double)options.MaxGridPoints, 1.0 / 3.0) * 1.01);
            }
            int wx = nx + 1, wy = ny + 1, wz = nz + 1;
            int n = checked(wx * wy * wz);
            double[] field = new double[n];
            int Index(int x, int y, int z) => (z * wy + y) * wx + x;
            Vec3 Position(int x, int y, int z) => new Vec3(lower.X + x * step, lower.Y + y * step, lower.Z + z * step);
            for (int z = 0; z < wz; z++)
                for (int y = 0; y < wy; y++)
                    for (int x = 0; x < wx; x++)
                        field[Index(x, y, z)] = Evaluate(Position(x, y, z), views, options);
            if (options.FieldSmoothingPasses > 0 && options.FieldSmoothingStrength > 0)
                SmoothField(field, wx, wy, wz, options.FieldSmoothingPasses, options.FieldSmoothingStrength);
            var mesh = new MultiviewResult { ViewCount = captures.Length, GridStep = step, SampledGridPoints = n };
            var edges = new Dictionary<long, int>();
            int Vertex(int ga, int gb, Vec3 pa, Vec3 pb)
            {
                long key = ((long)Math.Min(ga, gb) << 32) | (uint)Math.Max(ga, gb);
                if (edges.TryGetValue(key, out int id)) return id;
                double a = field[ga], b = field[gb];
                double div = a - b;
                double t = Math.Abs(div) < 1e-12 ? 0.5 : Math.Max(0, Math.Min(1, a / div));
                id = mesh.Vertices.Count;
                mesh.Vertices.Add(Vec3.Lerp(pa, pb, t));
                edges.Add(key, id);
                return id;
            }
            void Triangle(int a, int b, int c, Vec3 outward)
            {
                Vec3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c];
                Vec3 cross = Vec3.Cross(pb - pa, pc - pa);
                if (cross.LengthSquared < 1e-10) return;
                if (Vec3.Dot(cross, outward) < 0) { int swap = b; b = c; c = swap; }
                mesh.Triangles.Add((a, b, c));
            }
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int[] index =
                        {
                            Index(x,y,z), Index(x+1,y,z), Index(x+1,y+1,z), Index(x,y+1,z),
                            Index(x,y,z+1), Index(x+1,y,z+1), Index(x+1,y+1,z+1), Index(x,y+1,z+1)
                        };
                        Vec3[] pos =
                        {
                            Position(x,y,z), Position(x+1,y,z), Position(x+1,y+1,z), Position(x,y+1,z),
                            Position(x,y,z+1), Position(x+1,y,z+1), Position(x+1,y+1,z+1), Position(x,y+1,z+1)
                        };
                        foreach (int[] tet in Tetrahedra)
                        {
                            int[] inner = new int[4], outer = new int[4];
                            int ni = 0, no = 0;
                            foreach (int v in tet)
                            {
                                if (field[index[v]] <= 0) inner[ni++] = v;
                                else outer[no++] = v;
                            }
                            if (ni == 0 || no == 0) continue;
                            Vec3 inCenter = Vec3.Zero, outCenter = Vec3.Zero;
                            for (int i = 0; i < ni; i++) inCenter += pos[inner[i]];
                            for (int i = 0; i < no; i++) outCenter += pos[outer[i]];
                            Vec3 outward = outCenter / no - inCenter / ni;
                            int Edge(int a, int b) => Vertex(index[a], index[b], pos[a], pos[b]);
                            if (ni == 1)
                            {
                                int a = Edge(inner[0], outer[0]), b = Edge(inner[0], outer[1]), c = Edge(inner[0], outer[2]);
                                Triangle(a, b, c, outward);
                            }
                            else if (ni == 3)
                            {
                                int a = Edge(outer[0], inner[0]), b = Edge(outer[0], inner[1]), c = Edge(outer[0], inner[2]);
                                Triangle(a, b, c, outward);
                            }
                            else
                            {
                                int a = Edge(inner[0], outer[0]), b = Edge(inner[0], outer[1]);
                                int c = Edge(inner[1], outer[0]), d = Edge(inner[1], outer[1]);
                                Triangle(a, b, c, outward);
                                Triangle(b, d, c, outward);
                            }
                        }
                    }
            if (mesh.Triangles.Count == 0) throw new InvalidOperationException("No multi-view intersection found; try a larger silhouette slack.");
            return mesh;
        }

        private static void SmoothField(double[] field, int wx, int wy, int wz, int passes, double strength)
        {
            int n = field.Length;
            var buffer = new double[n];
            for (int pass = 0; pass < passes; pass++)
            {
                // Separable 1-2-1 Gaussian; smooth one axis at a time to retain thin features.
                for (int axis = 0; axis < 3; axis++)
                {
                    Array.Copy(field, buffer, n);
                    for (int z = 0; z < wz; z++)
                        for (int y = 0; y < wy; y++)
                            for (int x = 0; x < wx; x++)
                            {
                                int coordinate = axis == 0 ? x : axis == 1 ? y : z;
                                int limit = axis == 0 ? wx : axis == 1 ? wy : wz;
                                if (coordinate == 0 || coordinate == limit - 1) continue;
                                int i = (z * wy + y) * wx + x;
                                int before = axis == 0 ? i - 1 : axis == 1 ? i - wx : i - wx * wy;
                                int after = axis == 0 ? i + 1 : axis == 1 ? i + wx : i + wx * wy;
                                double filtered = (buffer[before] + 2.0 * buffer[i] + buffer[after]) * 0.25;
                                field[i] = buffer[i] + (filtered - buffer[i]) * strength;
                            }
                }
            }
        }

        /// <summary>Intersection of multi-view visible silhouettes and constraints from observed front depth.</summary>
        private static double Evaluate(Vec3 point, ViewData[] views, MultiviewOptions options)
        {
            double field = -Far;
            foreach (ViewData view in views)
            {
                Vec3 v = view.Transform.Mul(point) + view.Offset;
                var cap = view.Capture;
                int x = (int)Math.Floor(cap.OriginX + v.X);
                int y = (int)Math.Floor(cap.OriginY - v.Y);
                if (x < 0 || y < 0 || x >= cap.Width || y >= cap.Height)
                    return 5.0; // outside a recorded viewport cannot be reconstructed reliably
                int i = y * cap.Width + x;
                double bound = view.Distance[i] - options.SilhouetteSlack;
                if (cap.Visible[i])
                {
                    // Use the source rasterizer's measured surface normal as a local tangent plane.
                    // A pure Z-depth constraint over-expands steep surfaces and creates chunky facets
                    // around shoulders, head pieces and feet.
                    double nx = cap.NormalX[i], ny = cap.NormalY[i], nz = cap.NormalZ[i];
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl > 1e-8)
                    {
                        nx /= nl; ny /= nl; nz /= nl;
                        if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
                        double sx = x + 0.5 - cap.OriginX;
                        double sy = cap.OriginY - (y + 0.5);
                        double planeDistance = nx * (v.X - sx) + ny * (v.Y - sy) + nz * (v.Z - cap.Depth[i]);
                        bound = Math.Max(bound, planeDistance - options.DepthSlack);
                    }
                    else
                    {
                        bound = Math.Max(bound, v.Z - cap.Depth[i] - options.DepthSlack);
                    }
                }
                field = Math.Max(field, bound);
                if (field > 2.0) return field;
            }
            return field;
        }

        // Approximate signed pixel distance; zero-crossing lies midway between foreground/background.
        // The original 2D silhouette cleanup is preserved, including its visual fillets.
        private static float[] SignedSilhouetteDistance(PixelDepthCapture cap)
        {
            int n = cap.Width * cap.Height;
            var inside = DistanceToClass(cap.Visible, cap.Width, cap.Height, true);
            var outside = DistanceToClass(cap.Visible, cap.Width, cap.Height, false);
            var result = new float[n];
            for (int i = 0; i < n; i++)
                result[i] = cap.Visible[i] ? -(outside[i] - 0.5f) : inside[i] - 0.5f;
            return result;
        }

        private static float[] DistanceToClass(bool[] mask, int w, int h, bool value)
        {
            const float diagonal = 1.41421356f;
            int n = checked(w * h);
            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = mask[i] == value ? 0 : 100000;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (x > 0) dist[i] = Math.Min(dist[i], dist[i - 1] + 1);
                    if (y > 0)
                    {
                        dist[i] = Math.Min(dist[i], dist[i - w] + 1);
                        if (x > 0) dist[i] = Math.Min(dist[i], dist[i - w - 1] + diagonal);
                        if (x + 1 < w) dist[i] = Math.Min(dist[i], dist[i - w + 1] + diagonal);
                    }
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    if (x + 1 < w) dist[i] = Math.Min(dist[i], dist[i + 1] + 1);
                    if (y + 1 < h)
                    {
                        dist[i] = Math.Min(dist[i], dist[i + w] + 1);
                        if (x > 0) dist[i] = Math.Min(dist[i], dist[i + w - 1] + diagonal);
                        if (x + 1 < w) dist[i] = Math.Min(dist[i], dist[i + w + 1] + diagonal);
                    }
                }
            return dist;
        }

        internal static MaterialSlot MaterialAt(Vec3 point, Vec3 normal, PixelDepthCapture[] captures,
            Mat3[] rotations, Vec3[] offsets, Vec3[] eyes, CreaturePalette palette, CreatureAnatomy anatomy)
        {
            double best = -Far;
            MaterialSlot selected = MaterialSlot.Primary;
            for (int view = 0; view < captures.Length; view++)
            {
                var cap = captures[view];
                double facing = Vec3.Dot(normal, eyes[view]);
                if (facing < best || facing <= 0) continue;
                Vec3 p = rotations[view].Mul(point) + offsets[view];
                int x = (int)Math.Floor(cap.OriginX + p.X), y = (int)Math.Floor(cap.OriginY - p.Y);
                if (x < 0 || y < 0 || x >= cap.Width || y >= cap.Height) continue;
                int i = y * cap.Width + x;
                if (!cap.Visible[i] || Math.Abs(cap.Depth[i] - p.Z) > 3.0) continue;
                int primitive = cap.Primitive[i];
                if (primitive < 0 || primitive >= anatomy.Primitives.Count) continue;
                MaterialSlot baseSlot = anatomy.Primitives[primitive].Material;
                selected = ResolveRenderedSlot(baseSlot, cap.PaletteIndex[i], palette);
                best = facing;
            }
            return selected;
        }

        private static MaterialSlot ResolveRenderedSlot(MaterialSlot baseSlot, byte color, CreaturePalette palette)
        {
            bool InRamp(MaterialSlot slot)
            {
                for (int shade = 0; shade < Shade.Count; shade++)
                    if (palette.Index(slot, shade) == color) return true;
                return false;
            }
            if (InRamp(baseSlot)) return baseSlot;
            // Keep pattern and face-feature materials when their pixel color differs from the base ramp.
            MaterialSlot[] special =
            {
                MaterialSlot.Eye, MaterialSlot.Teeth, MaterialSlot.Mouth, MaterialSlot.Glow,
                MaterialSlot.White, MaterialSlot.Marking, MaterialSlot.Hair, MaterialSlot.Cloth,
                MaterialSlot.Metal, MaterialSlot.Accent, MaterialSlot.Membrane, MaterialSlot.Secondary
            };
            foreach (MaterialSlot slot in special)
                if (InRamp(slot)) return slot;
            return baseSlot;
        }
    }
}