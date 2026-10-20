// Indexed radial surface construction from semantic primitive samples; no SDF or voxel grid.
using System;
using System.Collections.Generic;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static partial class SemanticTopologyPrototype
    {
        private static void GetBounds(List<Vec3> points, out Vec3 min, out Vec3 max)
        {
            min = points[0];
            max = points[0];
            foreach (Vec3 point in points)
            {
                min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }
        }

        private static double BoundsGap(PrimitiveSample a, PrimitiveSample b)
        {
            double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static Patch BuildRadialPatch(int groupIndex, string name, List<Vec3> samples, int latSegments, int lonSegments)
        {
            Vec3 min = samples[0], max = samples[0];
            foreach (Vec3 p in samples)
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            Vec3 center = (min + max) * 0.5;
            int fieldLength = (latSegments + 1) * lonSegments;
            double[] radius = new double[fieldLength];
            double topRadius = Math.Max(0.05, max.Y - center.Y);
            double bottomRadius = Math.Max(0.05, center.Y - min.Y);

            foreach (Vec3 point in samples)
            {
                Vec3 delta = point - center;
                double distance = delta.Length;
                if (distance < 1e-6) continue;
                double theta = Math.Acos(DMath.Clamp(delta.Y / distance, -1.0, 1.0));
                double phi = Math.Atan2(delta.Z, delta.X);
                if (phi < 0) phi += DMath.TwoPi;
                int lat = Math.Clamp((int)Math.Round(theta / Math.PI * latSegments), 1, latSegments - 1);
                int lon = ((int)Math.Round(phi / DMath.TwoPi * lonSegments) % lonSegments + lonSegments) % lonSegments;
                int index = lat * lonSegments + lon;
                radius[index] = Math.Max(radius[index], distance);
            }

            FillMissingRadii(radius, latSegments, lonSegments);
            SmoothRadii(radius, latSegments, lonSegments);
            var patch = new Patch { GroupIndex = groupIndex, Name = name, Center = center };
            patch.Vertices.Add(center + new Vec3(0, topRadius, 0));
            for (int lat = 1; lat < latSegments; lat++)
            {
                double theta = Math.PI * lat / latSegments;
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    double phi = DMath.TwoPi * lon / lonSegments;
                    Vec3 direction = new Vec3(Math.Sin(theta) * Math.Cos(phi), Math.Cos(theta),
                        Math.Sin(theta) * Math.Sin(phi));
                    double r = Math.Max(0.05, radius[lat * lonSegments + lon]);
                    patch.Vertices.Add(center + direction * r);
                }
            }
            int bottom = patch.Vertices.Count;
            patch.Vertices.Add(center - new Vec3(0, bottomRadius, 0));

            void AddFace(int a, int b, int c)
            {
                Vec3 pa = patch.Vertices[a], pb = patch.Vertices[b], pc = patch.Vertices[c];
                Vec3 normal = Vec3.Cross(pb - pa, pc - pa);
                Vec3 outward = (pa + pb + pc) / 3.0 - center;
                if (Vec3.Dot(normal, outward) < 0) (b, c) = (c, b);
                if (Vec3.Cross(patch.Vertices[b] - pa, patch.Vertices[c] - pa).LengthSquared > 1e-10)
                    patch.Faces.Add((a, b, c));
            }

            int FirstRing(int lat) => 1 + (lat - 1) * lonSegments;
            int first = FirstRing(1);
            for (int lon = 0; lon < lonSegments; lon++)
                AddFace(0, first + lon, first + (lon + 1) % lonSegments);
            for (int lat = 1; lat < latSegments - 1; lat++)
            {
                int current = FirstRing(lat), next = FirstRing(lat + 1);
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int n = (lon + 1) % lonSegments;
                    AddFace(current + lon, next + lon, current + n);
                    AddFace(current + n, next + lon, next + n);
                }
            }
            int last = FirstRing(latSegments - 1);
            for (int lon = 0; lon < lonSegments; lon++)
                AddFace(last + lon, bottom, last + (lon + 1) % lonSegments);
            return patch;
        }

        private static void FillMissingRadii(double[] radius, int latSegments, int lonSegments)
        {
            for (int lat = 1; lat < latSegments; lat++)
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int index = lat * lonSegments + lon;
                if (radius[index] > 0) continue;
                bool found = false;
                for (int distance = 1; distance <= Math.Max(latSegments, lonSegments / 2) && !found; distance++)
                {
                    for (int dy = -distance; dy <= distance && !found; dy++)
                    for (int dx = -distance; dx <= distance; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != distance) continue;
                        int y = lat + dy;
                        if (y < 1 || y >= latSegments) continue;
                        int x = (lon + dx) % lonSegments;
                        if (x < 0) x += lonSegments;
                        double candidate = radius[y * lonSegments + x];
                        if (candidate <= 0) continue;
                        radius[index] = candidate;
                        found = true;
                        break;
                    }
                }
                if (!found) radius[index] = 0.05;
            }
        }

        private static void SmoothRadii(double[] radius, int latSegments, int lonSegments)
        {
            double[] copy = (double[])radius.Clone();
            for (int lat = 1; lat < latSegments; lat++)
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int left = (lon + lonSegments - 1) % lonSegments;
                int right = (lon + 1) % lonSegments;
                int above = Math.Max(1, lat - 1);
                int below = Math.Min(latSegments - 1, lat + 1);
                double neighbor = copy[above * lonSegments + lon] +
                    copy[below * lonSegments + lon] +
                    copy[lat * lonSegments + left] + copy[lat * lonSegments + right];
                int index = lat * lonSegments + lon;
                radius[index] = copy[index] * 0.88 + neighbor * 0.03;
            }
        }


    }
}
