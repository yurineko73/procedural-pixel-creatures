// Multi-view vertex constraints and silhouette diagnostics for the semantic topology experiment.
using System;
using System.Collections.Generic;
using System.Globalization;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Rendering;

namespace PixelCreatures.Core.Export
{
    public static partial class SemanticTopologyPrototype
    {
        private static ViewConstraint[] CreateViews(PixelDepthCapture[] captures)
        {
            var result = new ViewConstraint[captures.Length];
            for (int i = 0; i < captures.Length; i++)
            {
                PixelDepthCapture capture = captures[i];
                Mat3 rotation = PixelCamera.WorldToView(capture.View.Elevation) *
                    PixelCamera.YawRotation(capture.View.Yaw);
                result[i] = new ViewConstraint
                {
                    Capture = capture,
                    Rotation = rotation,
                    Offset = PixelCamera.WorldToView(capture.View.Elevation).Mul(capture.View.RootOffset),
                };
            }
            return result;
        }

        private static int RelaxVertices(List<Patch> patches, ViewConstraint[] views, SemanticTopologyOptions options)
        {
            int moved = 0;
            for (int pass = 0; pass < options.RelaxationPasses; pass++)
            {
                foreach (Patch patch in patches)
                {
                    for (int vertexIndex = 0; vertexIndex < patch.Vertices.Count; vertexIndex++)
                    {
                        Vec3 point = patch.Vertices[vertexIndex];
                        Vec3 correction = Vec3.Zero;
                        int votes = 0;
                        foreach (ViewConstraint view in views)
                        {
                            PixelDepthCapture cap = view.Capture;
                            Vec3 projected = view.Rotation.Mul(point) + view.Offset;
                            int x = (int)Math.Floor(cap.OriginX + projected.X);
                            int y = (int)Math.Floor(cap.OriginY - projected.Y);
                            bool inFrame = x >= 0 && y >= 0 && x < cap.Width && y < cap.Height;
                            int pixel = inFrame ? y * cap.Width + x : -1;
                            if (pixel < 0 || !cap.Visible[pixel])
                            {
                                if (!TryFindNearestVisible(cap, x, y, options.SilhouetteSearchRadius,
                                    out int nearestX, out int nearestY)) continue;
                                Vec3 desiredView = new Vec3(nearestX + 0.5 - cap.OriginX,
                                    cap.OriginY - (nearestY + 0.5), projected.Z);
                                Vec3 deltaView = new Vec3(desiredView.X - projected.X,
                                    desiredView.Y - projected.Y, 0);
                                correction += view.Rotation.Transposed.Mul(deltaView);
                                votes++;
                            }
                            else if (cap.Group[pixel] == (byte)patch.GroupIndex && DMath.IsFinite(cap.Depth[pixel]))
                            {
                                double targetDepth = cap.Depth[pixel] + options.DepthSlack;
                                if (projected.Z > targetDepth)
                                {
                                    double delta = Math.Max(-options.MaxMovePerPass * 2.0, targetDepth - projected.Z);
                                    correction += view.Rotation.Transposed.Mul(new Vec3(0, 0, delta));
                                    votes++;
                                }
                            }
                        }
                        if (votes == 0) continue;
                        correction = correction / votes * 0.32;
                        double length = correction.Length;
                        if (length > options.MaxMovePerPass)
                            correction = correction * (options.MaxMovePerPass / length);
                        if (correction.LengthSquared < 1e-8) continue;
                        patch.Vertices[vertexIndex] = point + correction;
                        moved++;
                    }
                }
            }
            return moved;
        }

        private static bool TryFindNearestVisible(PixelDepthCapture cap, int x, int y, int radius, out int nearestX, out int nearestY)
        {
            nearestX = -1;
            nearestY = -1;
            int bestDistance = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int sx = x + dx, sy = y + dy;
                if (sx < 0 || sy < 0 || sx >= cap.Width || sy >= cap.Height) continue;
                int distance = dx * dx + dy * dy;
                if (distance >= bestDistance || !cap.Visible[sy * cap.Width + sx]) continue;
                bestDistance = distance;
                nearestX = sx;
                nearestY = sy;
            }
            return nearestX >= 0;
        }

        private static MultiviewResult ToMetricMesh(List<Patch> patches)
        {
            var result = new MultiviewResult();
            foreach (Patch patch in patches)
            {
                int offset = result.Vertices.Count;
                result.Vertices.AddRange(patch.Vertices);
                foreach (var face in patch.Faces)
                    result.Triangles.Add((offset + face.A, offset + face.B, offset + face.C));
            }
            return result;
        }

        private static MultiviewResult ParseObjForMetrics(string objText, double unitsPerPixel, string? objectPrefix = null)
        {
            var result = new MultiviewResult();
            bool includeFaces = objectPrefix == null;
            foreach (string raw in objText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 4) continue;
                    result.Vertices.Add(new Vec3(
                        double.Parse(f[1], CultureInfo.InvariantCulture) / unitsPerPixel,
                        double.Parse(f[2], CultureInfo.InvariantCulture) / unitsPerPixel,
                        double.Parse(f[3], CultureInfo.InvariantCulture) / unitsPerPixel));
                }
                else if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    includeFaces = objectPrefix == null || line.Substring(2).StartsWith(objectPrefix, StringComparison.Ordinal);
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal) && includeFaces)
                {
                    string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 4) continue;
                    int first = ParseFaceIndex(f[1], result.Vertices.Count);
                    int previous = ParseFaceIndex(f[2], result.Vertices.Count);
                    for (int i = 3; i < f.Length; i++)
                    {
                        int current = ParseFaceIndex(f[i], result.Vertices.Count);
                        result.Triangles.Add((first, previous, current));
                        previous = current;
                    }
                }
            }
            return result;
        }

        private static int ParseFaceIndex(string token, int vertexCount)
        {
            string value = token.Split('/')[0];
            int index = int.Parse(value, CultureInfo.InvariantCulture);
            return index > 0 ? index - 1 : vertexCount + index;
        }

        private static Mat3[] GetRotations(ViewConstraint[] views)
        {
            var result = new Mat3[views.Length];
            for (int i = 0; i < views.Length; i++) result[i] = views[i].Rotation;
            return result;
        }

        private static Vec3[] GetOffsets(ViewConstraint[] views)
        {
            var result = new Vec3[views.Length];
            for (int i = 0; i < views.Length; i++) result[i] = views[i].Offset;
            return result;
        }

        private static Vec3[] GetEyes(ViewConstraint[] views)
        {
            var result = new Vec3[views.Length];
            for (int i = 0; i < views.Length; i++) result[i] = views[i].Rotation.Transposed.Mul(Vec3.UnitZ);
            return result;
        }

        private static double Mean(double[] values)
        {
            if (values.Length == 0) return 0;
            double sum = 0;
            foreach (double value in values) sum += value;
            return sum / values.Length;
        }

        private static double Worst(double[] values)
        {
            if (values.Length == 0) return 0;
            double worst = 1;
            foreach (double value in values) worst = Math.Min(worst, value);
            return worst;
        }


    }
}
