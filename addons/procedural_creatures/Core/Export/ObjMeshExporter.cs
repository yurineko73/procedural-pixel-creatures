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
            var organicBody = ImplicitBodyMesher.Build(anatomy);

            for (int index = 0; index < anatomy.Primitives.Count; index++)
            {
                PrimitiveDef p = anatomy.Primitives[index];
                if (organicBody.PrimitiveIndices.Contains(index)) continue;
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

            // Emit the fused organic skin after standalone hard details and equipment.
            // Its single outer surface replaces overlapping body ellipsoids/cones that previously
            // exposed internal shells and dark holes at joints.
            emitter.AppendMesh(organicBody);

            // Facial features are stored separately from body primitives in CreatureAnatomy.
            // Export them explicitly so 3D previews retain eyes, pupils, highlights and nostrils.
            for (int index = 0; index < anatomy.Features.Count; index++)
                EmitFeature(obj, emitter, anatomy, anatomy.Features[index], index, sides, rings);

            return new ObjMeshResult(obj.ToString(), BuildDefaultMaterials(), emitter.VertexCount, emitter.TriangleCount);
        }

        private static void EmitFeature(StringBuilder obj, MeshEmitter emitter, CreatureAnatomy anatomy,
            FeatureDef feature, int index, int sides, int rings)
        {
            if (feature.Bone < 0 || feature.Bone >= anatomy.Skeleton.Count) return;
            if (!feature.LocalPosition.IsFinite || !feature.LocalNormal.IsFinite || !DMath.IsFinite(feature.Size) || feature.Size <= 0) return;

            Xform bone = anatomy.Skeleton.RestWorld(feature.Bone);
            Vec3 position = bone.Point(feature.LocalPosition);
            Vec3 normal = bone.Dir(feature.LocalNormal).NormalizedOr(Vec3.UnitZ);
            Vec3 referenceUp = Math.Abs(Vec3.Dot(normal, Vec3.UnitY)) > 0.92 ? Vec3.UnitX : Vec3.UnitY;
            Vec3 right = Vec3.Cross(referenceUp, normal).NormalizedOr(Vec3.UnitX);
            Vec3 up = Vec3.Cross(normal, right).NormalizedOr(Vec3.UnitY);
            Func<Vec3, Vec3> localToWorld = p => position + right * p.X + up * p.Y + normal * p.Z;

            double size = Math.Max(0.7, feature.Size);
            double aspect = DMath.Clamp(feature.Aspect, 0.55, 1.8);
            string prefix = "feature_" + index.ToString(CultureInfo.InvariantCulture) + "_";

            if (feature.Kind == FeatureKind.Nostril)
            {
                EmitFeatureEllipsoid(obj, emitter, localToWorld, Vec3.Zero,
                    new Vec3(Math.Max(0.22, size * 0.28), Math.Max(0.22, size * 0.20), Math.Max(0.10, size * 0.10)),
                    "FeatureNostril", prefix + "nostril", sides, rings);
                return;
            }

            if (feature.Kind == FeatureKind.Spot)
            {
                string spotMaterial = feature.Material == MaterialSlot.Glow ? "FeatureGlow" : "FeatureHighlight";
                double radius = Math.Max(0.16, size * 0.22);
                EmitFeatureEllipsoid(obj, emitter, localToWorld, new Vec3(0, 0, size * 0.08),
                    new Vec3(radius, radius, radius * 0.65), spotMaterial, prefix + "spot", sides, rings);
                return;
            }

            double width = size * aspect;
            EmitFeatureEllipsoid(obj, emitter, localToWorld, new Vec3(0, 0, size * 0.02),
                new Vec3(width * 0.70, size * 0.90, size * 0.24),
                "FeatureSocket", prefix + "socket", sides, rings);

            Vec3 eyeCenter = new Vec3(0, 0, size * 0.13);
            if (feature.Style == EyeStyle.Round)
            {
                EmitFeatureEllipsoid(obj, emitter, localToWorld, eyeCenter,
                    new Vec3(width * 0.56, size * 0.70, size * 0.22),
                    "FeatureSclera", prefix + "sclera", sides, rings);
            }

            if (feature.Style == EyeStyle.Glow)
            {
                EmitFeatureEllipsoid(obj, emitter, localToWorld, eyeCenter,
                    new Vec3(width * 0.52, size * 0.70, size * 0.25),
                    "FeatureGlow", prefix + "glow", sides, rings);
                EmitFeatureEllipsoid(obj, emitter, localToWorld,
                    new Vec3(0, size * 0.22, size * 0.36),
                    new Vec3(width * 0.13, size * 0.17, size * 0.08),
                    "FeatureHighlight", prefix + "glow_highlight", sides, rings);
                return;
            }

            EmitFeatureEllipsoid(obj, emitter, localToWorld,
                eyeCenter + new Vec3(feature.Side * size * 0.10, 0, size * 0.16),
                new Vec3(width * (feature.Style == EyeStyle.Button ? 0.37 : 0.31),
                    size * (feature.Style == EyeStyle.Button ? 0.46 : 0.43), size * 0.14),
                feature.Style == EyeStyle.Bead || feature.Style == EyeStyle.Button || feature.Style == EyeStyle.Compound
                    ? "FeaturePupil" : "FeatureIris",
                prefix + "iris", sides, rings);

            if (feature.Style == EyeStyle.Slit)
            {
                EmitFeatureEllipsoid(obj, emitter, localToWorld,
                    eyeCenter + new Vec3(feature.Side * size * 0.10, 0, size * 0.27),
                    new Vec3(Math.Max(0.08, width * 0.075), size * 0.34, size * 0.075),
                    "FeaturePupil", prefix + "slit", sides, rings);
            }
            else if (feature.Style == EyeStyle.Round)
            {
                EmitFeatureEllipsoid(obj, emitter, localToWorld,
                    eyeCenter + new Vec3(feature.Side * size * 0.10, -size * 0.02, size * 0.27),
                    new Vec3(width * 0.16, size * 0.25, size * 0.08),
                    "FeaturePupil", prefix + "pupil", sides, rings);
            }
            else if (feature.Style == EyeStyle.Compound)
            {
                for (int dot = -1; dot <= 1; dot++)
                    EmitFeatureEllipsoid(obj, emitter, localToWorld,
                        eyeCenter + new Vec3(dot * width * 0.13, size * 0.12, size * 0.28),
                        new Vec3(width * 0.07, size * 0.09, size * 0.055),
                        "FeatureHighlight", prefix + "facet_" + (dot + 1).ToString(CultureInfo.InvariantCulture), sides, rings);
            }

            double shineScale = feature.Style == EyeStyle.Button ? 0.19 : 0.12;
            EmitFeatureEllipsoid(obj, emitter, localToWorld,
                eyeCenter + new Vec3(-feature.Side * size * 0.13, size * 0.20, size * 0.32),
                new Vec3(width * shineScale, size * shineScale, size * 0.07),
                "FeatureHighlight", prefix + "shine", sides, rings);
        }

        private static void EmitFeatureEllipsoid(StringBuilder obj, MeshEmitter emitter,
            Func<Vec3, Vec3> localToWorld, Vec3 center, Vec3 radii,
            string material, string objectName, int sides, int rings)
        {
            obj.Append("o ").AppendLine(objectName);
            obj.Append("usemtl ").AppendLine(material);
            emitter.Ellipsoid(v => localToWorld(center + v), radii, Math.Max(8, sides / 2), Math.Max(5, rings / 2));
        }

        /// <summary>Neutral placeholder materials. The Godot preview applies the sampled six-shade palette.</summary>
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
            sb.AppendLine("Ka 0.02 0.02 0.02");
            sb.AppendLine("d 1.0");
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
                if (twoSided)
                {
                    // Give the reverse face its own vertices. Sharing the same three indices
                    // makes generated smooth normals cancel to zero in indexed mesh consumers.
                    int reverseA = Vertex(a), reverseB = Vertex(b), reverseC = Vertex(c);
                    Face(reverseC, reverseB, reverseA);
                }
            }

            public void AppendMesh(ImplicitBodyMesher.MeshData mesh)
            {
                if (mesh == null || mesh.Vertices.Count < 3 || mesh.Faces.Count == 0) return;
                int[] globalIndices = new int[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; i++) globalIndices[i] = Vertex(mesh.Vertices[i]);

                foreach (MaterialSlot material in Enum.GetValues<MaterialSlot>())
                {
                    if (material == MaterialSlot.Count) continue;
                    bool hasFaces = false;
                    foreach (var face in mesh.Faces)
                        if (face.Material == material) { hasFaces = true; break; }
                    if (!hasFaces) continue;

                    _obj.Append("o organic_body_").AppendLine(material.ToString());
                    _obj.Append("usemtl ").AppendLine(material.ToString());
                    foreach (var face in mesh.Faces)
                        if (face.Material == material)
                            Face(globalIndices[face.A], globalIndices[face.B], globalIndices[face.C]);
                }
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
                // Cap rings and side rings deliberately do not share indices. Otherwise
                // GenerateNormals averages the flat cap normal with the curved side normal,
                // creating bright/dark pinstripes at every limb and weapon end.
                int[] sideRingA = new int[sides], sideRingB = new int[sides];
                int[] capRingA = new int[sides], capRingB = new int[sides];
                for (int i = 0; i < sides; i++)
                {
                    double t = 2 * Math.PI * i / sides;
                    Vec3 dir = u * Math.Cos(t) + v * Math.Sin(t);
                    Vec3 pointA = a + dir * ra;
                    Vec3 pointB = b + dir * rb;
                    sideRingA[i] = Vertex(pointA);
                    sideRingB[i] = Vertex(pointB);
                    capRingA[i] = Vertex(pointA);
                    capRingB[i] = Vertex(pointB);
                }
                for (int i = 0; i < sides; i++)
                {
                    int n = (i + 1) % sides;
                    Face(centerA, capRingA[n], capRingA[i]);
                    Face(sideRingA[i], sideRingA[n], sideRingB[i]);
                    Face(sideRingA[n], sideRingB[n], sideRingB[i]);
                    Face(centerB, capRingB[i], capRingB[n]);
                }
            }
        }
    }
}
