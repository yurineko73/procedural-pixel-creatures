// Procedural Pixel Creatures - experimental static 3D OBJ export.
// Converts the underlying 3D anatomy primitives, never the rasterized pixel frames.
// Intentionally exports REST POSE ONLY: no skinning, animation, SDF unions or UVs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public sealed class ObjMeshResult
    {
        public string ObjText { get; }
        public string MtlText { get; }
        public int Vertices { get; }
        public int Triangles { get; }

        public ObjMeshResult(string objText, string mtlText, int vertices, int triangles)
        {
            ObjText = objText;
            MtlText = mtlText;
            Vertices = vertices;
            Triangles = triangles;
        }
    }

    /// <summary>
    /// Engine-independent OBJ preview exporter. A primitive creates its own closed/flat mesh;
    /// intersecting parts remain intersecting rather than being boolean-unioned.
    /// </summary>
    public static class ObjMeshExporter
    {
        public static ObjMeshResult Export(CreatureAnatomy anatomy, int sides = 16, int rings = 8,
            double unitsPerPixel = 0.01, string materialFile = "creature.mtl")
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));
            if (sides < 6 || sides > 128) throw new ArgumentOutOfRangeException(nameof(sides));
            if (rings < 4 || rings > 128) throw new ArgumentOutOfRangeException(nameof(rings));
            if (!DMath.IsFinite(unitsPerPixel) || unitsPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(unitsPerPixel));
            if (string.IsNullOrWhiteSpace(materialFile) || materialFile.Contains('\n') || materialFile.Contains('\r'))
                throw new ArgumentException("A single MTL file name is required.", nameof(materialFile));

            var obj = new StringBuilder();
            obj.AppendLine("# Procedural Pixel Creatures - static primitive preview; not a skinned mesh");
            obj.Append("mtllib ").AppendLine(materialFile);
            var emitter = new MeshEmitter(obj, unitsPerPixel);

            for (int index = 0; index < anatomy.Primitives.Count; index++)
            {
                PrimitiveDef p = anatomy.Primitives[index];
                if (p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                obj.Append("o primitive_").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine();
                obj.Append("usemtl ").Append(p.Material.ToString()).AppendLine();
                Xform xa = anatomy.Skeleton.RestWorld(p.BoneA);
                if (p.Kind == PrimKind.Ellipsoid)
                {
                    Xform local = Xform.FromQuat(p.LocalRotation, p.LocalA);
                    emitter.Ellipsoid(v => xa.Point(local.Point(v)), p.Radii, sides, rings);
                }
                else if (p.Kind == PrimKind.RoundCone)
                {
                    Vec3 a = xa.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    emitter.Frustum(a, b, p.RadiusA, p.RadiusB, sides);
                }
                else if (p.Kind == PrimKind.Triangle)
                {
                    Vec3 a = xa.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    Vec3 c = anatomy.Skeleton.RestWorld(p.BoneC).Point(p.LocalC);
                    emitter.Triangle(a, b, c, (p.Flags & PrimFlags.TwoSided) != 0);
                }
            }
            return new ObjMeshResult(obj.ToString(), BuildDefaultMaterials(), emitter.VertexCount, emitter.TriangleCount);
        }

        /// <summary>Neutral placeholder materials. Source genome colour ramps are not exported yet.</summary>
        private static string BuildDefaultMaterials()
        {
            var sb = new StringBuilder("# Placeholder 3D palette; author proper materials in your DCC\n");
            foreach (MaterialSlot material in Enum.GetValues<MaterialSlot>())
            {
                if (material == MaterialSlot.Count) continue;
                string color = material switch
                {
                    MaterialSlot.Metal => "0.55 0.58 0.65",
                    MaterialSlot.Cloth => "0.30 0.38 0.48",
                    MaterialSlot.Hair => "0.22 0.17 0.12",
                    MaterialSlot.Glow => "0.20 0.85 1.00",
                    MaterialSlot.Eye => "0.85 0.85 0.85",
                    MaterialSlot.Mouth => "0.32 0.10 0.14",
                    MaterialSlot.Teeth => "0.90 0.87 0.78",
                    _ => "0.54 0.62 0.52",
                };
                sb.Append("newmtl ").Append(material.ToString()).AppendLine();
                sb.Append("Kd ").AppendLine(color);
                sb.AppendLine("Ka 0.02 0.02 0.02");
                sb.AppendLine("d 1.0");
            }
            return sb.ToString();
        }

        private sealed class MeshEmitter
        {
            private readonly StringBuilder _obj;
            private readonly double _scale;
            public int VertexCount { get; private set; }
            public int TriangleCount { get; private set; }
            public MeshEmitter(StringBuilder obj, double scale) { _obj = obj; _scale = scale; }

            private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

            private int Vertex(Vec3 point)
            {
                if (!point.IsFinite) throw new InvalidOperationException("Nonfinite OBJ coordinate");
                _obj.Append("v ").Append(F(point.X * _scale)).Append(' ')
                    .Append(F(point.Y * _scale)).Append(' ').Append(F(point.Z * _scale)).AppendLine();
                return ++VertexCount;
            }

            private void Face(int a, int b, int c)
            {
                _obj.Append("f ").Append(a).Append(' ').Append(b).Append(' ').Append(c).AppendLine();
                TriangleCount++;
            }

            public void Triangle(Vec3 a, Vec3 b, Vec3 c, bool twoSided)
            {
                if (Vec3.Cross(b - a, c - a).LengthSquared < 1e-12) return;
                int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c);
                Face(ia, ib, ic);
                if (twoSided) Face(ic, ib, ia);
            }

            public void Ellipsoid(Func<Vec3, Vec3> transform, Vec3 radii, int sides, int rings)
            {
                int[,] vertices = new int[rings + 1, sides];
                // Interior latitude rings only; explicit poles avoid degenerate triangles.
                int bottom = Vertex(transform(new Vec3(0, -radii.Y, 0)));
                for (int j = 1; j < rings; j++)
                {
                    double latitude = -Math.PI * 0.5 + Math.PI * j / rings;
                    double h = Math.Cos(latitude), y = Math.Sin(latitude);
                    for (int i = 0; i < sides; i++)
                    {
                        double angle = 2 * Math.PI * i / sides;
                        vertices[j, i] = Vertex(transform(new Vec3(
                            radii.X * h * Math.Cos(angle), radii.Y * y,
                            radii.Z * h * Math.Sin(angle))));
                    }
                }
                int top = Vertex(transform(new Vec3(0, radii.Y, 0)));
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Face(bottom, vertices[1, i], vertices[1, next]);
                    for (int j = 1; j < rings - 1; j++)
                    {
                        int a = vertices[j, i], b = vertices[j, next];
                        int c = vertices[j + 1, i], d = vertices[j + 1, next];
                        Face(a, c, b);
                        Face(b, c, d);
                    }
                    Face(vertices[rings - 1, i], top, vertices[rings - 1, next]);
                }
            }

            public void Frustum(Vec3 a, Vec3 b, double ra, double rb, int sides)
            {
                Vec3 axis = b - a;
                if (axis.LengthSquared < 1e-12)
                {
                    Ellipsoid(v => a + v, new Vec3(Math.Max(ra, rb), Math.Max(ra, rb), Math.Max(ra, rb)), sides, 6);
                    return;
                }
                Vec3 normal = axis.Normalized();
                Vec3 guide = Math.Abs(Vec3.Dot(normal, Vec3.UnitY)) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
                Vec3 u = Vec3.Cross(normal, guide).Normalized();
                Vec3 v = Vec3.Cross(normal, u).Normalized();
                int centerA = Vertex(a), centerB = Vertex(b);
                int[] ringA = new int[sides], ringB = new int[sides];
                for (int i = 0; i < sides; i++)
                {
                    double t = 2 * Math.PI * i / sides;
                    Vec3 dir = u * Math.Cos(t) + v * Math.Sin(t);
                    ringA[i] = Vertex(a + dir * ra);
                    ringB[i] = Vertex(b + dir * rb);
                }
                for (int i = 0; i < sides; i++)
                {
                    int n = (i + 1) % sides;
                    Face(centerA, ringA[n], ringA[i]);
                    Face(ringA[i], ringA[n], ringB[i]);
                    Face(ringA[n], ringB[n], ringB[i]);
                    Face(centerB, ringB[i], ringB[n]);
                }
            }
        }
    }
}
