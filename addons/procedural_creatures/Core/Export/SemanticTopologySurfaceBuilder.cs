// Indexed surfaces built directly from source ellipsoids and tapered segments.
// Keep each anatomy primitive separate: no radial envelope, SDF union, or voxel union.
using System;
using System.Globalization;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static partial class SemanticTopologyPrototype
    {
        private static Patch BuildPrimitivePatch(CreatureAnatomy anatomy, PrimitiveDef primitive,
            int primitiveIndex, int latitudeSegments, int longitudeSegments)
        {
            string groupName = anatomy.Groups[primitive.Group].Name ?? "group";
            var patch = new Patch
            {
                GroupIndex = primitive.Group,
                Name = groupName + "_primitive_" + primitiveIndex.ToString(CultureInfo.InvariantCulture),
            };
            if (primitive.BoneA < 0 || primitive.BoneA >= anatomy.Skeleton.Count) return patch;

            Xform boneA = anatomy.Skeleton.RestWorld(primitive.BoneA);
            if (primitive.Kind == PrimKind.Ellipsoid)
            {
                Xform local = Xform.FromQuat(primitive.LocalRotation, primitive.LocalA);
                Func<Vec3, Vec3> transform = v => boneA.Point(local.Point(v));
                patch.Center = transform(Vec3.Zero);
                AddEllipsoidSurface(patch, transform, primitive.Radii,
                    latitudeSegments, longitudeSegments);
            }
            else if (primitive.Kind == PrimKind.RoundCone &&
                primitive.BoneB >= 0 && primitive.BoneB < anatomy.Skeleton.Count)
            {
                Vec3 a = boneA.Point(primitive.LocalA);
                Vec3 b = anatomy.Skeleton.RestWorld(primitive.BoneB).Point(primitive.LocalB);
                AddTaperedSurface(patch, a, b, primitive.RadiusA, primitive.RadiusB,
                    latitudeSegments, longitudeSegments);
            }
            return patch;
        }

        private static void AddEllipsoidSurface(Patch patch, Func<Vec3, Vec3> transform,
            Vec3 radii, int latitudeSegments, int longitudeSegments)
        {
            patch.Vertices.Add(transform(new Vec3(0, -radii.Y, 0)));
            int FirstRing(int latitude) => 1 + (latitude - 1) * longitudeSegments;
            for (int latitude = 1; latitude < latitudeSegments; latitude++)
            {
                double angleY = -Math.PI * 0.5 + Math.PI * latitude / latitudeSegments;
                double horizontal = Math.Cos(angleY);
                double y = Math.Sin(angleY);
                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    double angle = DMath.TwoPi * longitude / longitudeSegments;
                    patch.Vertices.Add(transform(new Vec3(
                        radii.X * horizontal * Math.Cos(angle),
                        radii.Y * y,
                        radii.Z * horizontal * Math.Sin(angle))));
                }
            }
            int top = patch.Vertices.Count;
            patch.Vertices.Add(transform(new Vec3(0, radii.Y, 0)));

            int first = FirstRing(1);
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
                AddFace(patch, 0, first + (longitude + 1) % longitudeSegments, first + longitude);

            for (int latitude = 1; latitude < latitudeSegments - 1; latitude++)
            {
                int current = FirstRing(latitude);
                int next = FirstRing(latitude + 1);
                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    int n = (longitude + 1) % longitudeSegments;
                    AddFace(patch, current + longitude, current + n, next + longitude);
                    AddFace(patch, current + n, next + n, next + longitude);
                }
            }

            int last = FirstRing(latitudeSegments - 1);
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
                AddFace(patch, last + longitude, last + (longitude + 1) % longitudeSegments, top);
        }

        private static void AddTaperedSurface(Patch patch, Vec3 a, Vec3 b,
            double radiusA, double radiusB, int ringSegments, int sideSegments)
        {
            Vec3 axisVector = b - a;
            if (axisVector.LengthSquared < 1e-10) return;
            Vec3 axis = axisVector.NormalizedOr(Vec3.UnitY);
            Vec3 guide = Math.Abs(Vec3.Dot(axis, Vec3.UnitY)) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
            Vec3 u = Vec3.Cross(axis, guide).NormalizedOr(Vec3.UnitZ);
            Vec3 v = Vec3.Cross(axis, u).NormalizedOr(Vec3.UnitX);
            patch.Center = (a + b) * 0.5;

            int[,] rings = new int[ringSegments + 1, sideSegments];
            for (int ring = 0; ring <= ringSegments; ring++)
            {
                double t = ring / (double)ringSegments;
                Vec3 center = Vec3.Lerp(a, b, t);
                double radius = radiusA + (radiusB - radiusA) * t;
                for (int side = 0; side < sideSegments; side++)
                {
                    double angle = DMath.TwoPi * side / sideSegments;
                    Vec3 point = center + (u * Math.Cos(angle) + v * Math.Sin(angle)) * radius;
                    rings[ring, side] = patch.Vertices.Count;
                    patch.Vertices.Add(point);
                }
            }

            int capA = patch.Vertices.Count;
            patch.Vertices.Add(a);
            int capB = patch.Vertices.Count;
            patch.Vertices.Add(b);

            for (int side = 0; side < sideSegments; side++)
            {
                int next = (side + 1) % sideSegments;
                AddFace(patch, capA, rings[0, next], rings[0, side]);
                AddFace(patch, rings[ringSegments, side], rings[ringSegments, next], capB);
                for (int ring = 0; ring < ringSegments; ring++)
                {
                    int a0 = rings[ring, side];
                    int a1 = rings[ring, next];
                    int b0 = rings[ring + 1, side];
                    int b1 = rings[ring + 1, next];
                    AddFace(patch, a0, a1, b0);
                    AddFace(patch, a1, b1, b0);
                }
            }
        }

        private static void AddFace(Patch patch, int a, int b, int c)
        {
            Vec3 pa = patch.Vertices[a];
            Vec3 pb = patch.Vertices[b];
            Vec3 pc = patch.Vertices[c];
            Vec3 normal = Vec3.Cross(pb - pa, pc - pa);
            if (normal.LengthSquared < 1e-10) return;
            Vec3 outward = (pa + pb + pc) / 3.0 - patch.Center;
            if (Vec3.Dot(normal, outward) < 0) (b, c) = (c, b);
            patch.Faces.Add((a, b, c));
        }
    }
}
