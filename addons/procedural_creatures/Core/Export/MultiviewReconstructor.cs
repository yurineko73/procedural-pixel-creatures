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
        public double SilhouetteSlack = 0.65;
        public double DepthSlack = 1.2;
    }

    public sealed class MultiviewResult
    {
        public readonly List<Vec3> Vertices = new List<Vec3>();
        public readonly List<(int A, int B, int C)> Triangles = new List<(int, int, int)>();
        public int ViewCount;
        public double GridStep;
        public int SampledGridPoints;

        public string ToObj(PixelDepthCapture[] captures, CreaturePalette palette, double scale = 0.01)
        {
            var inv = CultureInfo.InvariantCulture;
            var obj = new StringBuilder("# Pixel-depth multi-view reconstructed experimental mesh\n");
            obj.AppendLine("mtllib creature.mtl");
            obj.AppendLine("o multiview_surface");
            foreach (Vec3 p in Vertices)
                obj.Append("v ").Append((p.X * scale).ToString("G9", inv)).Append(' ')
                    .Append((p.Y * scale).ToString("G9", inv)).Append(' ')
                    .Append((p.Z * scale).ToString("G9", inv)).AppendLine();
            int previous = -1;
            foreach (var f in Triangles)
            {
                Vec3 a = Vertices[f.A], b = Vertices[f.B], c = Vertices[f.C];
                Vec3 normal = Vec3.Cross(b - a, c - a).NormalizedOr(Vec3.UnitY);
                int color = MultiviewReconstructor.PaletteAt((a + b + c) / 3.0, normal, captures);
                if (color < CreaturePalette.FirstColorIndex || color >= palette.Colors.Count)
                    color = palette.Index(MaterialSlot.Primary, Shade.Base);
                if (color != previous)
                {
                    obj.Append("usemtl px_").Append(color.ToString(inv)).AppendLine();
                    previous = color;
                }
                obj.Append("f ").Append(f.A + 1).Append(' ').Append(f.B + 1)
                    .Append(' ').Append(f.C + 1).AppendLine();
            }
            return obj.ToString();
        }

        public static string MaterialLibrary(CreaturePalette palette)
        {
            var sb = new StringBuilder("# Original procedural creature palette\n");
            for (int i = 0; i < palette.Colors.Count; i++)
            {
                Rgba c = palette.Colors[i];
                if (i < CreaturePalette.FirstColorIndex) continue;
                sb.Append("newmtl px_").Append(i).AppendLine();
                sb.Append("Kd ").Append((c.R / 255.0).ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((c.G / 255.0).ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((c.B / 255.0).ToString("F6", CultureInfo.InvariantCulture)).AppendLine();
                sb.AppendLine("Ka 0 0 0\nKs 0 0 0\nillum 1");
            }
            return sb.ToString();
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
                options.SilhouetteSlack < 0 || options.DepthSlack < 0)
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
                    bound = Math.Max(bound, v.Z - cap.Depth[i] - options.DepthSlack);
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

        internal static int PaletteAt(Vec3 point, Vec3 normal, PixelDepthCapture[] captures)
        {
            double best = -Far;
            int color = 0;
            foreach (var cap in captures)
            {
                Mat3 transform = PixelCamera.WorldToView(cap.View.Elevation) * PixelCamera.YawRotation(cap.View.Yaw);
                Vec3 eye = transform.Transposed.Mul(Vec3.UnitZ);
                double facing = Vec3.Dot(normal, eye);
                if (facing < best || facing <= 0) continue;
                if (!cap.Project(point, out int x, out int y, out double z)) continue;
                int i = y * cap.Width + x;
                // Only associate a painted pixel with the surface if its observed depth is nearby.
                // Otherwise a distant occluding limb could paint the wrong material onto the body.
                if (!cap.Visible[i] || Math.Abs(cap.Depth[i] - z) > 2.5) continue;
                if (cap.PaletteIndex[i] < CreaturePalette.FirstColorIndex) continue;
                best = facing;
                color = cap.PaletteIndex[i];
            }
            return color;
        }
    }
}
