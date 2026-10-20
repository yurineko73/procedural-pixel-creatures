// OBJ emission for fixed-topology semantic patches and preserved detail geometry.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Palettes;
using PixelCreatures.Core.Rendering;

namespace PixelCreatures.Core.Export
{
    public static partial class SemanticTopologyPrototype
    {
        private static ObjMeshResult EmitObj(CreatureModel model, List<Patch> patches,
            HashSet<int> corePrimitiveIndices, PixelDepthCapture[] captures, double unitsPerPixel)
        {
            CreatureAnatomy anatomy = model.Anatomy;
            var obj = new StringBuilder();
            obj.AppendLine("# Semantic topology prototype: indexed per-region radial surfaces + multiview vertex relaxation");
            obj.AppendLine("mtllib creature.mtl");
            var writer = new ObjWriter(obj, unitsPerPixel);
            ViewConstraint[] views = CreateViews(captures);
            Mat3[] rotations = GetRotations(views);
            Vec3[] offsets = GetOffsets(views);
            Vec3[] eyes = GetEyes(views);

            foreach (Patch patch in patches)
            {
                int[] ids = new int[patch.Vertices.Count];
                for (int i = 0; i < patch.Vertices.Count; i++) ids[i] = writer.Vertex(patch.Vertices[i]);
                obj.Append("o topology_").Append(Sanitize(patch.Name)).AppendLine();
                int previousMaterial = -1;
                foreach (var face in patch.Faces)
                {
                    Vec3 a = patch.Vertices[face.A], b = patch.Vertices[face.B], c = patch.Vertices[face.C];
                    Vec3 normal = Vec3.Cross(b - a, c - a).NormalizedOr(Vec3.UnitY);
                    MaterialSlot material = MultiviewReconstructor.MaterialAt((a + b + c) / 3.0,
                        normal, captures, rotations, offsets, eyes, model.Palette, anatomy);
                    if ((int)material != previousMaterial)
                    {
                        obj.Append("usemtl MV_").AppendLine(material.ToString());
                        previousMaterial = (int)material;
                    }
                    writer.Face(ids[face.A], ids[face.B], ids[face.C]);
                }
            }

            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                if (corePrimitiveIndices.Contains(i)) continue;
                PrimitiveDef source = anatomy.Primitives[i];
                PrimitiveDef p;
                if (PreviewDentalFit.Applies(anatomy, source))
                {
                    if (!PreviewDentalFit.TryFit(anatomy, source, out p)) continue;
                }
                else if (PreviewClawFit.Applies(source))
                {
                    if (!PreviewClawFit.TryFit(anatomy, source, out p)) continue;
                }
                else if (!PreviewAttachmentRepair.TryRepair(anatomy, source, out p)) continue;
                if (p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                obj.Append("o detail_").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine();
                obj.Append("usemtl MV_").AppendLine(p.Material.ToString());
                if (p.BoneA < 0 || p.BoneA >= anatomy.Skeleton.Count) continue;
                Xform boneA = anatomy.Skeleton.RestWorld(p.BoneA);
                if (p.Kind == PrimKind.Ellipsoid)
                {
                    Xform local = Xform.FromQuat(p.LocalRotation, p.LocalA);
                    writer.Ellipsoid(v => boneA.Point(local.Point(v)), p.Radii, 16, 8);
                }
                else if (p.Kind == PrimKind.RoundCone && p.BoneB >= 0 && p.BoneB < anatomy.Skeleton.Count)
                {
                    Vec3 a = boneA.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    writer.Frustum(a, b, p.RadiusA, p.RadiusB, 12);
                }
                else if (p.Kind == PrimKind.Triangle &&
                    p.BoneB >= 0 && p.BoneB < anatomy.Skeleton.Count &&
                    p.BoneC >= 0 && p.BoneC < anatomy.Skeleton.Count)
                {
                    Vec3 a = boneA.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    Vec3 c = anatomy.Skeleton.RestWorld(p.BoneC).Point(p.LocalC);
                    writer.Triangle(a, b, c, (p.Flags & PrimFlags.TwoSided) != 0);
                }
            }

            (int featureVertices, int featureTriangles) = ObjMeshExporter.AppendFeatures(
                obj, anatomy, writer.VertexCount, 20, 12, unitsPerPixel);
            string mtl = MultiviewResult.MaterialLibrary(model.Palette);
            return new ObjMeshResult(obj.ToString(), mtl, writer.VertexCount + featureVertices,
                writer.TriangleCount + featureTriangles);
        }

        private static string Sanitize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "region" : sb.ToString();
        }

        private sealed class ObjWriter
        {
            private readonly StringBuilder _obj;
            private readonly double _scale;
            public int VertexCount { get; private set; }
            public int TriangleCount { get; private set; }

            public ObjWriter(StringBuilder obj, double scale) { _obj = obj; _scale = scale; }

            public int Vertex(Vec3 p)
            {
                if (!p.IsFinite) throw new InvalidOperationException("Semantic topology generated a non-finite vertex.");
                _obj.Append("v ").Append((p.X * _scale).ToString("0.########", CultureInfo.InvariantCulture))
                    .Append(' ').Append((p.Y * _scale).ToString("0.########", CultureInfo.InvariantCulture))
                    .Append(' ').Append((p.Z * _scale).ToString("0.########", CultureInfo.InvariantCulture)).AppendLine();
                return ++VertexCount;
            }

            public void Face(int a, int b, int c)
            {
                _obj.Append("f ").Append(a).Append(' ').Append(b).Append(' ').Append(c).AppendLine();
                TriangleCount++;
            }

            public void Triangle(Vec3 a, Vec3 b, Vec3 c, bool twoSided)
            {
                if (Vec3.Cross(b - a, c - a).LengthSquared < 1e-12) return;
                int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c);
                Face(ia, ib, ic);
                if (twoSided)
                {
                    int ra = Vertex(a), rb = Vertex(b), rc = Vertex(c);
                    Face(rc, rb, ra);
                }
            }

            public void Ellipsoid(Func<Vec3, Vec3> transform, Vec3 radii, int sides, int rings)
            {
                int[,] ring = new int[rings - 1, sides];
                int bottom = Vertex(transform(new Vec3(0, -radii.Y, 0)));
                for (int j = 1; j < rings; j++)
                {
                    double latitude = -Math.PI * 0.5 + Math.PI * j / rings;
                    double h = Math.Cos(latitude), y = Math.Sin(latitude);
                    for (int i = 0; i < sides; i++)
                    {
                        double angle = DMath.TwoPi * i / sides;
                        ring[j - 1, i] = Vertex(transform(new Vec3(
                            radii.X * h * Math.Cos(angle), radii.Y * y, radii.Z * h * Math.Sin(angle))));
                    }
                }
                int top = Vertex(transform(new Vec3(0, radii.Y, 0)));
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Face(bottom, ring[0, i], ring[0, next]);
                    for (int j = 0; j < rings - 2; j++)
                    {
                        int a = ring[j, i], b = ring[j, next], c = ring[j + 1, i], d = ring[j + 1, next];
                        Face(a, c, b);
                        Face(b, c, d);
                    }
                    Face(ring[rings - 2, i], top, ring[rings - 2, next]);
                }
            }

            public void Frustum(Vec3 a, Vec3 b, double ra, double rb, int sides)
            {
                Vec3 axis = b - a;
                if (axis.LengthSquared < 1e-12)
                {
                    double r = Math.Max(ra, rb);
                    Ellipsoid(v => a + v, new Vec3(r, r, r), sides, 6);
                    return;
                }
                Vec3 n = axis.Normalized();
                Vec3 guide = Math.Abs(Vec3.Dot(n, Vec3.UnitY)) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
                Vec3 u = Vec3.Cross(n, guide).NormalizedOr(Vec3.UnitZ);
                Vec3 v = Vec3.Cross(n, u).NormalizedOr(Vec3.UnitX);
                int ca = Vertex(a), cb = Vertex(b);
                int[] ringA = new int[sides], ringB = new int[sides];
                int[] capA = new int[sides], capB = new int[sides];
                for (int i = 0; i < sides; i++)
                {
                    double t = DMath.TwoPi * i / sides;
                    Vec3 dir = u * Math.Cos(t) + v * Math.Sin(t);
                    Vec3 pa = a + dir * ra, pb = b + dir * rb;
                    ringA[i] = Vertex(pa); ringB[i] = Vertex(pb);
                    capA[i] = Vertex(pa); capB[i] = Vertex(pb);
                }
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Face(ca, capA[next], capA[i]);
                    Face(ringA[i], ringA[next], ringB[i]);
                    Face(ringA[next], ringB[next], ringB[i]);
                    Face(cb, capB[i], capB[next]);
                }
            }
        }
    }
}
