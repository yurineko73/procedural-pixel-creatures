// Experimental geometry-fit prototype.
// Fits a small set of semantic primitive groups against the original 2D renderer silhouettes.
// This is deliberately not a depth-map-to-volume reconstruction: it keeps the source anatomy,
// adjusts only bounded group transforms, and scores a lightweight projection of parametric surfaces.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Rendering;

namespace PixelCreatures.Core.Export
{
    public sealed class GeometryFitOptions
    {
        public int Directions = 8;
        public int Passes = 2;
        public double ScaleStep = 0.04;
        public double OffsetStep = 0.25;
        public double MaxScaleDelta = 0.08;
        public double MaxOffset = 0.5;
        public int EllipsoidSides = 8;
        public int EllipsoidRings = 5;
        public int ExportSides = 20;
        public int ExportRings = 12;
        public double UnitsPerPixel = 0.04;
    }

    public sealed class GeometryFitResult
    {
        public CreatureAnatomy Anatomy { get; }
        public ObjMeshResult FittedMesh { get; }
        public PixelDepthCapture[] ReferenceViews { get; }
        public double InitialMeanIoU { get; }
        public double FittedMeanIoU { get; }
        public double InitialWorstIoU { get; }
        public double FittedWorstIoU { get; }
        public double InitialMeshMeanIoU { get; }
        public double FittedMeshMeanIoU { get; }
        public double InitialMeshWorstIoU { get; }
        public double FittedMeshWorstIoU { get; }
        public int Evaluations { get; }
        public int AdjustedRegions { get; }
        public string ParameterSummary { get; }

        internal GeometryFitResult(CreatureAnatomy anatomy, ObjMeshResult fittedMesh, PixelDepthCapture[] referenceViews,
            double initialMeanIoU, double fittedMeanIoU, double initialWorstIoU, double fittedWorstIoU,
            double initialMeshMeanIoU, double fittedMeshMeanIoU, double initialMeshWorstIoU, double fittedMeshWorstIoU,
            int evaluations, int adjustedRegions, string parameterSummary)
        {
            Anatomy = anatomy;
            FittedMesh = fittedMesh;
            ReferenceViews = referenceViews;
            InitialMeanIoU = initialMeanIoU;
            FittedMeanIoU = fittedMeanIoU;
            InitialWorstIoU = initialWorstIoU;
            FittedWorstIoU = fittedWorstIoU;
            InitialMeshMeanIoU = initialMeshMeanIoU;
            FittedMeshMeanIoU = fittedMeshMeanIoU;
            InitialMeshWorstIoU = initialMeshWorstIoU;
            FittedMeshWorstIoU = fittedMeshWorstIoU;
            Evaluations = evaluations;
            AdjustedRegions = adjustedRegions;
            ParameterSummary = parameterSummary;
        }
    }

    /// <summary>
    /// Small deterministic experiment for reducing silhouette mismatch between the original 2D
    /// renderer and a mesh made from the same anatomy primitives. It optimizes only a few semantic
    /// group transforms with tight bounds. The reported IoU is for a lightweight triangulated
    /// primitive projection, not the final SDF-exported OBJ; final mesh quality must still be checked.
    /// </summary>
    public static class GeometryFitPrototype
    {
        private static readonly string[] FitGroupOrder =
        {
            "body", "torso", "pelvis", "head", "neck", "arms", "legs", "tail",
            "thorax", "abdomen", "wing", "wings", "fin", "fins"
        };
        private static readonly HashSet<string> FitGroupNames =
            new HashSet<string>(FitGroupOrder, StringComparer.OrdinalIgnoreCase);

        private sealed class FitRegion
        {
            public string Name = string.Empty;
            public Vec3 Center;
            public double Scale = 1.0;
            public Vec3 Offset = Vec3.Zero;

            public FitRegion Copy() => new FitRegion { Name = Name, Center = Center, Scale = Scale, Offset = Offset };
        }

        private readonly struct Triangle3
        {
            public readonly Vec3 A, B, C;
            public Triangle3(Vec3 a, Vec3 b, Vec3 c) { A = a; B = b; C = c; }
        }

        private readonly struct Metrics
        {
            public readonly double Mean;
            public readonly double Worst;
            public Metrics(double mean, double worst) { Mean = mean; Worst = worst; }
            public double Score => Mean * 0.7 + Worst * 0.3;
        }

        public static GeometryFitResult Fit(CreatureModel model, GeometryFitOptions? options = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            options ??= new GeometryFitOptions();
            if (options.Directions < 4 || options.Directions > 16)
                throw new ArgumentOutOfRangeException(nameof(options.Directions));
            if (options.Passes < 0 || options.Passes > 4)
                throw new ArgumentOutOfRangeException(nameof(options.Passes));
            if (options.EllipsoidSides < 6 || options.EllipsoidSides > 24 ||
                options.EllipsoidRings < 4 || options.EllipsoidRings > 12)
                throw new ArgumentOutOfRangeException(nameof(options.EllipsoidSides));
            if (options.ExportSides < 6 || options.ExportSides > 128 ||
                options.ExportRings < 4 || options.ExportRings > 128 ||
                !DMath.IsFinite(options.UnitsPerPixel) || options.UnitsPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.ExportSides));

            CreatureAnatomy source = model.Anatomy;
            PixelDepthCapture[] references = MultiviewReconstructor.Capture(model, options.Directions);
            List<FitRegion> regions = DiscoverRegions(source);
            Metrics initial = Evaluate(source, references, options);
            int evaluations = 1;
            ObjMeshResult initialObj = ObjMeshExporter.Export(source, options.ExportSides, options.ExportRings, options.UnitsPerPixel);
            Metrics initialMesh = EvaluateObj(initialObj.ObjText, references, options.UnitsPerPixel);

            if (regions.Count == 0 || options.Passes == 0)
            {
                return new GeometryFitResult(source, initialObj, references,
                    initial.Mean, initial.Mean, initial.Worst, initial.Worst,
                    initialMesh.Mean, initialMesh.Mean, initialMesh.Worst, initialMesh.Worst,
                    evaluations, 0, "没有可拟合的主体分组");
            }

            Metrics current = initial;
            double scaleStep = options.ScaleStep;
            double offsetStep = options.OffsetStep;

            // Coordinate descent over a very small, regularized parameter set. Each accepted
            // change must improve a score that values both average and worst-view silhouette IoU.
            for (int pass = 0; pass < options.Passes; pass++)
            {
                for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
                {
                    for (int parameter = 0; parameter < 4; parameter++)
                    {
                        List<FitRegion>? bestRegions = null;
                        Metrics bestMetrics = current;
                        foreach (double direction in new[] { -1.0, 1.0 })
                        {
                            List<FitRegion> candidateRegions = CopyRegions(regions);
                            FitRegion candidate = candidateRegions[regionIndex];
                            switch (parameter)
                            {
                                case 0:
                                    candidate.Scale = Clamp(candidate.Scale + direction * scaleStep,
                                        1.0 - options.MaxScaleDelta, 1.0 + options.MaxScaleDelta);
                                    break;
                                case 1:
                                    candidate.Offset = candidate.Offset.WithX(Clamp(candidate.Offset.X + direction * offsetStep,
                                        -options.MaxOffset, options.MaxOffset));
                                    break;
                                case 2:
                                    candidate.Offset = candidate.Offset.WithY(Clamp(candidate.Offset.Y + direction * offsetStep,
                                        -options.MaxOffset, options.MaxOffset));
                                    break;
                                default:
                                    candidate.Offset = candidate.Offset.WithZ(Clamp(candidate.Offset.Z + direction * offsetStep,
                                        -options.MaxOffset, options.MaxOffset));
                                    break;
                            }

                            if (SameParameters(candidateRegions, regions)) continue;
                            CreatureAnatomy candidateAnatomy = ApplyRegions(source, candidateRegions);
                            Metrics candidateMetrics = Evaluate(candidateAnatomy, references, options);
                            evaluations++;
                            if (candidateMetrics.Score > bestMetrics.Score + 0.00005)
                            {
                                bestMetrics = candidateMetrics;
                                bestRegions = candidateRegions;
                            }
                        }

                        if (bestRegions != null)
                        {
                            regions = bestRegions;
                            current = bestMetrics;
                        }
                    }
                }
                scaleStep *= 0.5;
                offsetStep *= 0.5;
            }

            CreatureAnatomy fitted = ApplyRegions(source, regions);
            ObjMeshResult fittedObj = ObjMeshExporter.Export(fitted, options.ExportSides, options.ExportRings, options.UnitsPerPixel);
            Metrics fittedMesh = EvaluateObj(fittedObj.ObjText, references, options.UnitsPerPixel);
            int changed = 0;
            var summary = new List<string>();
            foreach (FitRegion region in regions)
            {
                if (Math.Abs(region.Scale - 1.0) < 1e-5 && region.Offset.Length < 1e-4) continue;
                changed++;
                summary.Add($"{region.Name}: scale {region.Scale:0.###}, offset {region.Offset}");
            }

            return new GeometryFitResult(fitted, fittedObj, references,
                initial.Mean, current.Mean, initial.Worst, current.Worst,
                initialMesh.Mean, fittedMesh.Mean, initialMesh.Worst, fittedMesh.Worst,
                evaluations, changed,
                summary.Count == 0 ? "参数未发生有效变化" : string.Join("; ", summary));
        }

        private static List<FitRegion> DiscoverRegions(CreatureAnatomy anatomy)
        {
            var anchors = new Dictionary<string, (Vec3 Sum, int Count)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                string name = anatomy.Groups[p.Group].Name;
                if (!FitGroupNames.Contains(name) || name.StartsWith("gear", StringComparison.OrdinalIgnoreCase)) continue;
                AddAnchor(name, WorldPoint(anatomy, p.BoneA, p.LocalA), anchors);
                if (p.Kind != PrimKind.Ellipsoid)
                    AddAnchor(name, WorldPoint(anatomy, p.BoneB, p.LocalB), anchors);
                if (p.Kind == PrimKind.Triangle)
                    AddAnchor(name, WorldPoint(anatomy, p.BoneC, p.LocalC), anchors);
            }

            var result = new List<FitRegion>();
            foreach (string name in FitGroupOrder)
            {
                if (!anchors.TryGetValue(name, out var value) || value.Count == 0) continue;
                result.Add(new FitRegion { Name = name, Center = value.Sum / value.Count });
            }
            return result;
        }

        private static void AddAnchor(string name, Vec3 point, Dictionary<string, (Vec3 Sum, int Count)> anchors)
        {
            if (anchors.TryGetValue(name, out var value))
                anchors[name] = (value.Sum + point, value.Count + 1);
            else
                anchors[name] = (point, 1);
        }

        private static List<FitRegion> CopyRegions(List<FitRegion> source)
        {
            var copy = new List<FitRegion>(source.Count);
            foreach (FitRegion region in source) copy.Add(region.Copy());
            return copy;
        }

        private static bool SameParameters(List<FitRegion> a, List<FitRegion> b)
        {
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i].Scale - b[i].Scale) > 1e-9 || (a[i].Offset - b[i].Offset).Length > 1e-9)
                    return false;
            return true;
        }

        private static CreatureAnatomy ApplyRegions(CreatureAnatomy source, List<FitRegion> regions)
        {
            var byName = new Dictionary<string, FitRegion>(StringComparer.OrdinalIgnoreCase);
            foreach (FitRegion region in regions) byName[region.Name] = region;

            var primitives = new List<PrimitiveDef>(source.Primitives.Count);
            foreach (PrimitiveDef original in source.Primitives)
            {
                PrimitiveDef p = original.Clone();
                if (p.Group >= 0 && p.Group < source.Groups.Count &&
                    byName.TryGetValue(source.Groups[p.Group].Name, out FitRegion region))
                {
                    Xform boneA = source.Skeleton.RestWorld(p.BoneA);
                    p.LocalA = TransformLocalPoint(boneA, p.LocalA, region);
                    if (p.Kind != PrimKind.Ellipsoid)
                    {
                        Xform boneB = source.Skeleton.RestWorld(p.BoneB);
                        p.LocalB = TransformLocalPoint(boneB, p.LocalB, region);
                    }
                    if (p.Kind == PrimKind.Triangle)
                    {
                        Xform boneC = source.Skeleton.RestWorld(p.BoneC);
                        p.LocalC = TransformLocalPoint(boneC, p.LocalC, region);
                    }
                    if (p.Kind == PrimKind.Ellipsoid) p.Radii *= region.Scale;
                    else if (p.Kind == PrimKind.RoundCone)
                    {
                        p.RadiusA *= region.Scale;
                        p.RadiusB *= region.Scale;
                    }
                }
                primitives.Add(p);
            }

            var features = new List<FeatureDef>(source.Features.Count);
            foreach (FeatureDef original in source.Features)
            {
                var feature = new FeatureDef
                {
                    Kind = original.Kind, Bone = original.Bone, LocalPosition = original.LocalPosition,
                    LocalNormal = original.LocalNormal, Size = original.Size, Side = original.Side,
                    Style = original.Style, Aspect = original.Aspect, Brow = original.Brow,
                    Material = original.Material, Group = original.Group
                };
                if (feature.Group >= 0 && feature.Group < source.Groups.Count &&
                    byName.TryGetValue(source.Groups[feature.Group].Name, out FitRegion? region))
                {
                    Xform bone = source.Skeleton.RestWorld(feature.Bone);
                    feature.LocalPosition = TransformLocalPoint(bone, feature.LocalPosition, region);
                    feature.Size *= region.Scale;
                }
                features.Add(feature);
            }

            return new CreatureAnatomy(source.FamilyId, source.Skeleton, new List<PartGroup>(source.Groups),
                primitives, features, source.Rig, source.Metrics, source.Canvas,
                new List<string>(source.Repairs), new List<string>(source.Traits), source.AnatomyKey);
        }

        private static Vec3 TransformLocalPoint(Xform bone, Vec3 local, FitRegion region)
        {
            Vec3 world = bone.Point(local);
            world = region.Center + (world - region.Center) * region.Scale + region.Offset;
            return bone.InversePoint(world);
        }

        private static Vec3 WorldPoint(CreatureAnatomy anatomy, int bone, Vec3 local) =>
            anatomy.Skeleton.RestWorld(bone).Point(local);

        private static Metrics Evaluate(CreatureAnatomy anatomy, PixelDepthCapture[] references, GeometryFitOptions options)
        {
            List<Triangle3> triangles = BuildTriangles(anatomy, options.EllipsoidSides, options.EllipsoidRings);
            double sum = 0;
            double worst = 1;
            for (int viewIndex = 0; viewIndex < references.Length; viewIndex++)
            {
                PixelDepthCapture reference = references[viewIndex];
                bool[] projected = new bool[reference.Width * reference.Height];
                Mat3 rotation = PixelCamera.WorldToView(reference.View.Elevation) *
                    PixelCamera.YawRotation(reference.View.Yaw);
                Vec3 offset = PixelCamera.WorldToView(reference.View.Elevation).Mul(reference.View.RootOffset);
                foreach (Triangle3 triangle in triangles)
                {
                    Vec3 a = Project(triangle.A, rotation, offset, reference);
                    Vec3 b = Project(triangle.B, rotation, offset, reference);
                    Vec3 c = Project(triangle.C, rotation, offset, reference);
                    RasterizeTriangle(a, b, c, reference.Width, reference.Height, projected);
                }

                int intersection = 0, union = 0;
                for (int i = 0; i < projected.Length; i++)
                {
                    bool target = reference.Visible[i];
                    bool candidate = projected[i];
                    if (target && candidate) intersection++;
                    if (target || candidate) union++;
                }
                double iou = union == 0 ? 1.0 : (double)intersection / union;
                sum += iou;
                worst = Math.Min(worst, iou);
            }
            return new Metrics(sum / references.Length, worst);
        }

        private static List<Triangle3> BuildTriangles(CreatureAnatomy anatomy, int sides, int rings)
        {
            var triangles = new List<Triangle3>(anatomy.Primitives.Count * sides * rings * 2);
            foreach (PrimitiveDef p in anatomy.Primitives)
            {
                if (p.MinQuality > 2 || p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                if ((anatomy.Groups[p.Group].Flags & GroupFlags.Interior) != 0 ||
                    (p.Flags & PrimFlags.FrontOnly) != 0) continue;

                Xform boneA = anatomy.Skeleton.RestWorld(p.BoneA);
                if (p.Kind == PrimKind.Ellipsoid)
                {
                    Xform local = Xform.FromQuat(p.LocalRotation, p.LocalA);
                    var vertices = new Vec3[rings + 1, sides];
                    for (int j = 0; j <= rings; j++)
                    {
                        double latitude = -Math.PI * 0.5 + Math.PI * j / rings;
                        double h = Math.Cos(latitude), y = Math.Sin(latitude);
                        for (int i = 0; i < sides; i++)
                        {
                            double angle = 2 * Math.PI * i / sides;
                            Vec3 point = new Vec3(p.Radii.X * h * Math.Cos(angle),
                                p.Radii.Y * y, p.Radii.Z * h * Math.Sin(angle));
                            vertices[j, i] = boneA.Point(local.Point(point));
                        }
                    }
                    for (int j = 0; j < rings; j++)
                    for (int i = 0; i < sides; i++)
                    {
                        int next = (i + 1) % sides;
                        AddTriangle(triangles, vertices[j, i], vertices[j + 1, i], vertices[j, next]);
                        AddTriangle(triangles, vertices[j, next], vertices[j + 1, i], vertices[j + 1, next]);
                    }
                }
                else if (p.Kind == PrimKind.RoundCone)
                {
                    Vec3 a = boneA.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    Vec3 axis = (b - a).NormalizedOr(Vec3.UnitY);
                    Vec3 reference = Math.Abs(Vec3.Dot(axis, Vec3.UnitY)) > 0.9 ? Vec3.UnitX : Vec3.UnitY;
                    Vec3 u = Vec3.Cross(axis, reference).NormalizedOr(Vec3.UnitX);
                    Vec3 v = Vec3.Cross(axis, u).NormalizedOr(Vec3.UnitZ);
                    Vec3[] ringA = new Vec3[sides], ringB = new Vec3[sides];
                    for (int i = 0; i < sides; i++)
                    {
                        double angle = 2 * Math.PI * i / sides;
                        Vec3 radial = u * Math.Cos(angle) + v * Math.Sin(angle);
                        ringA[i] = a + radial * p.RadiusA;
                        ringB[i] = b + radial * p.RadiusB;
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        int next = (i + 1) % sides;
                        AddTriangle(triangles, ringA[i], ringB[i], ringA[next]);
                        AddTriangle(triangles, ringA[next], ringB[i], ringB[next]);
                        AddTriangle(triangles, a, ringA[next], ringA[i]);
                        AddTriangle(triangles, b, ringB[i], ringB[next]);
                    }
                }
                else
                {
                    Vec3 a = boneA.Point(p.LocalA);
                    Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                    Vec3 c = anatomy.Skeleton.RestWorld(p.BoneC).Point(p.LocalC);
                    AddTriangle(triangles, a, b, c);
                }
            }
            return triangles;
        }

        private static void AddTriangle(List<Triangle3> triangles, Vec3 a, Vec3 b, Vec3 c)
        {
            if (Vec3.Cross(b - a, c - a).LengthSquared < 1e-12) return;
            triangles.Add(new Triangle3(a, b, c));
        }

        private static Vec3 Project(Vec3 point, Mat3 rotation, Vec3 offset, PixelDepthCapture reference)
        {
            Vec3 view = rotation.Mul(point) + offset;
            return new Vec3(reference.OriginX + view.X, reference.OriginY - view.Y, view.Z);
        }

        private static void RasterizeTriangle(Vec3 a, Vec3 b, Vec3 c, int width, int height, bool[] mask)
        {
            double area = Edge(a, b, c.X, c.Y);
            if (Math.Abs(area) < 1e-9) return;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double e0 = Edge(b, c, px, py) / area;
                double e1 = Edge(c, a, px, py) / area;
                double e2 = Edge(a, b, px, py) / area;
                if (e0 >= -1e-8 && e1 >= -1e-8 && e2 >= -1e-8) mask[y * width + x] = true;
            }
        }

        private static double Edge(Vec3 a, Vec3 b, double x, double y) =>
            (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    }
}
