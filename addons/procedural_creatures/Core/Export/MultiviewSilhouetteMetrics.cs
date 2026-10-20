// Mesh-to-2D silhouette comparison against the original rasterizer (diagnostics only).
using System;
using PixelCreatures.Core.Rendering;

namespace PixelCreatures.Core.Export
{
    public static class MultiviewSilhouetteMetrics
    {
        /// <summary>
        /// Pixel IoU at the original renderer's native logical resolution. This is a silhouette
        /// check, not an appearance, eye-position, topology, or texture-quality score.
        /// </summary>
        public static double[] IntersectionOverUnion(MultiviewResult mesh, PixelDepthCapture[] views)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (views == null) throw new ArgumentNullException(nameof(views));
            var result = new double[views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                var cap = views[i];
                var covered = new bool[cap.Width * cap.Height];
                var projection = new Vec2[mesh.Vertices.Count];
                Mat3 rotation = PixelCamera.WorldToView(cap.View.Elevation) * PixelCamera.YawRotation(cap.View.Yaw);
                Vec3 offset = PixelCamera.WorldToView(cap.View.Elevation).Mul(cap.View.RootOffset);
                for (int v = 0; v < mesh.Vertices.Count; v++)
                {
                    Vec3 p = rotation.Mul(mesh.Vertices[v]) + offset;
                    projection[v] = new Vec2(cap.OriginX + p.X - 0.5, cap.OriginY - p.Y - 0.5);
                }
                foreach (var face in mesh.Triangles)
                {
                    Vec2 a = projection[face.A], b = projection[face.B], c = projection[face.C];
                    int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
                    int maxX = Math.Min(cap.Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
                    int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                    int maxY = Math.Min(cap.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                    if (minX > maxX || minY > maxY) continue;
                    double winding = Edge(a, b, c.X, c.Y);
                    if (Math.Abs(winding) < 1e-9) continue;
                    for (int y = minY; y <= maxY; y++)
                        for (int x = minX; x <= maxX; x++)
                        {
                            double e0 = Edge(a, b, x, y);
                            double e1 = Edge(b, c, x, y);
                            double e2 = Edge(c, a, x, y);
                            if (winding > 0 ? e0 < 0 || e1 < 0 || e2 < 0 : e0 > 0 || e1 > 0 || e2 > 0)
                                continue;
                            covered[y * cap.Width + x] = true;
                        }
                }
                int intersection = 0, union = 0;
                for (int p = 0; p < covered.Length; p++)
                {
                    if (covered[p] && cap.Visible[p]) intersection++;
                    if (covered[p] || cap.Visible[p]) union++;
                }
                result[i] = union == 0 ? 0 : intersection / (double)union;
            }
            return result;
        }

        private static double Edge(Vec2 a, Vec2 b, double x, double y) =>
            (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
    }
}
