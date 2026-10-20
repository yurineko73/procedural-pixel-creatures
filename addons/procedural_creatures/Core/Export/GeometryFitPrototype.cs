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
        public int Passes = 1;
        public double ScaleStep = 0.04;
        public double OffsetStep = 0.25;
        public double MaxScaleDelta = 0.08;
        public double MaxOffset = 0.5;
        public int EllipsoidSides = 8;
        public int EllipsoidRings = 5;
        public int ExportSides = 20;
        public int ExportRings = 12;
        public double UnitsPerPixel = 0.04;
        // Includes the baseline OBJ evaluation. A hard budget keeps the editor preview responsive.
        public int MaxMeshEvaluations = 32;
        public int MaxRegions = 6;
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
        /// <summary>Number of actual OBJ meshes evaluated, including the unmodified baseline.</summary>
        public int Evaluations { get; }
        /// <summary>Number of cheap primitive-projection evaluations used to guide search direction.</summary>
        public int ProxyEvaluations { get; }
        public int AdjustedRegions { get; }
        public string ParameterSummary { get; }

        internal GeometryFitResult(CreatureAnatomy anatomy, ObjMeshResult fittedMesh, PixelDepthCapture[] referenceViews,
            double initialMeanIoU, double fittedMeanIoU, double initialWorstIoU, double fittedWorstIoU,
            double initialMeshMeanIoU, double fittedMeshMeanIoU, double initialMeshWorstIoU, double fittedMeshWorstIoU,
            int evaluations, int proxyEvaluations, int adjustedRegions, string parameterSummary)
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
            ProxyEvaluations = proxyEvaluations;
            AdjustedRegions = adjustedRegions;
            ParameterSummary = parameterSummary;
        }
    }

    /// <summary>
    /// Small deterministic experiment for reducing silhouette mismatch between the original 2D
    /// renderer and the actual OBJ export. A lightweight primitive projection guides candidate
    /// direction, but only the exported-OBJ silhouette score can accept a geometry change.
    /// </summary>
    public static class GeometryFitPrototype
    {
        private static readonly string[] FitGroupOrder =
        {
            // Prioritize the broad anatomy groups that contribute most to silhouette.
            // Keep the list explicit so a family with many optional details cannot consume
            // the entire mesh-evaluation budget before its head and limbs are considered.
            "body", "torso", "pelvis", "head", "ears", "face", "neck", "arms", "legs", "tail",
            "wings", "wing", "fins", "fin", "abdomen", "thorax", "back", "shell", "elytra",
            "frill", "crest", "tailfeathers", "tentacles", "hood", "tailfin", "caudal"
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

        private enum FitParameter : byte
        {
            Scale,
            OffsetX,
            OffsetY,
            OffsetZ,
        }

        private sealed class ReverseCandidate
        {
            public string RegionName = string.Empty;
            public FitParameter Parameter;
            public double Step;
            public double Direction;
            public double ProxyGap;
        }

        public static GeometryFitResult Fit(CreatureModel model, GeometryFitOptions? options = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            options ??= new GeometryFitOptions();
            if (options.Directions < 4 || options.Directions > 16)
                throw new ArgumentOutOfRangeException(nameof(options.Directions));
            if (options.Passes < 0 || options.Passes > 4)
                throw new ArgumentOutOfRangeException(nameof(options.Passes));
            if (!DMath.IsFinite(options.ScaleStep) || options.ScaleStep <= 0 ||
                !DMath.IsFinite(options.OffsetStep) || options.OffsetStep <= 0 ||
                !DMath.IsFinite(options.MaxScaleDelta) || options.MaxScaleDelta < 0 || options.MaxScaleDelta > 0.5 ||
                !DMath.IsFinite(options.MaxOffset) || options.MaxOffset < 0 || options.MaxOffset > 5)
                throw new ArgumentOutOfRangeException(nameof(options.ScaleStep), "Step sizes must be positive and fit bounds must be finite and non-negative.");
            if (options.EllipsoidSides < 6 || options.EllipsoidSides > 24 ||
                options.EllipsoidRings < 4 || options.EllipsoidRings > 12)
                throw new ArgumentOutOfRangeException(nameof(options.EllipsoidSides));
            if (options.ExportSides < 6 || options.ExportSides > 128 ||
                options.ExportRings < 4 || options.ExportRings > 128 ||
                !DMath.IsFinite(options.UnitsPerPixel) || options.UnitsPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.ExportSides));
            if (options.MaxMeshEvaluations < 1 || options.MaxMeshEvaluations > 256)
                throw new ArgumentOutOfRangeException(nameof(options.MaxMeshEvaluations));
            if (options.MaxRegions < 1 || options.MaxRegions > 32)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRegions));

            CreatureAnatomy source = model.Anatomy;
            PixelDepthCapture[] references = MultiviewReconstructor.Capture(model, options.Directions);
            List<FitRegion> regions = DiscoverRegions(source);
            if (regions.Count > options.MaxRegions)
                regions.RemoveRange(options.MaxRegions, regions.Count - options.MaxRegions);

            Metrics initialProxy = Evaluate(source, references, options);
            int proxyEvaluations = 1;
            ObjMeshResult initialObj = ObjMeshExporter.Export(
                source, options.ExportSides, options.ExportRings, options.UnitsPerPixel);
            Metrics initialMesh = EvaluateObj(initialObj.ObjText, references, options.UnitsPerPixel);
            int meshEvaluations = 1;

            if (regions.Count == 0 || options.Passes == 0 || options.MaxMeshEvaluations == 1)
            {
                return new GeometryFitResult(source, initialObj, references,
                    initialProxy.Mean, initialProxy.Mean, initialProxy.Worst, initialProxy.Worst,
                    initialMesh.Mean, initialMesh.Mean, initialMesh.Worst, initialMesh.Worst,
                    meshEvaluations, proxyEvaluations, 0, "没有可拟合的主体分组或评估预算不足");
            }

            Metrics currentProxy = initialProxy;
            Metrics currentMesh = initialMesh;
            ObjMeshResult currentObj = initialObj;
            double scaleStep = options.ScaleStep;
            double offsetStep = options.OffsetStep;
            const double improvementEpsilon = 0.00005;

            // The actual OBJ silhouette is the acceptance objective. The fast primitive projection
            // only chooses which direction to try first; it can never cause a worse OBJ to be kept.
            // One preferred candidate is tested for every coordinate before any reverse candidates,
            // avoiding a small budget being spent entirely on the first few anatomy groups.
            for (int pass = 0; pass < options.Passes && meshEvaluations < options.MaxMeshEvaluations; pass++)
            {
                var reverseCandidates = new List<ReverseCandidate>();
                bool budgetExhausted = false;

                for (int regionIndex = 0; regionIndex < regions.Count && !budgetExhausted; regionIndex++)
                {
                    foreach (FitParameter parameter in Enum.GetValues<FitParameter>())
                    {
                        if (meshEvaluations >= options.MaxMeshEvaluations)
                        {
                            budgetExhausted = true;
                            break;
                        }

                        double step = parameter == FitParameter.Scale ? scaleStep : offsetStep;
                        List<FitRegion>? negative = CreateCandidate(regions, regionIndex, parameter, -1, step, options);
                        List<FitRegion>? positive = CreateCandidate(regions, regionIndex, parameter, 1, step, options);
                        if (negative == null && positive == null) continue;

                        Metrics negativeProxy = negative == null ? currentProxy :
                            Evaluate(ApplyRegions(source, negative), references, options);
                        if (negative != null) proxyEvaluations++;
                        Metrics positiveProxy = positive == null ? currentProxy :
                            Evaluate(ApplyRegions(source, positive), references, options);
                        if (positive != null) proxyEvaluations++;

                        bool preferNegative = negative != null &&
                            (positive == null || negativeProxy.Score >= positiveProxy.Score);
                        List<FitRegion> preferredRegions = preferNegative ? negative! : positive!;
                        Metrics preferredProxy = preferNegative ? negativeProxy : positiveProxy;
                        List<FitRegion>? alternateRegions = preferNegative ? positive : negative;
                        double preferredDirection = preferNegative ? -1 : 1;
                        double proxyGap = negative == null || positive == null
                            ? double.PositiveInfinity
                            : Math.Abs(negativeProxy.Score - positiveProxy.Score);

                        CreatureAnatomy candidateAnatomy = ApplyRegions(source, preferredRegions);
                        ObjMeshResult candidateObj = ObjMeshExporter.Export(
                            candidateAnatomy, options.ExportSides, options.ExportRings, options.UnitsPerPixel);
                        Metrics candidateMesh = EvaluateObj(candidateObj.ObjText, references, options.UnitsPerPixel);
                        meshEvaluations++;

                        if (candidateMesh.Score > currentMesh.Score + improvementEpsilon)
                        {
                            regions = preferredRegions;
                            currentProxy = preferredProxy;
                            currentMesh = candidateMesh;
                            currentObj = candidateObj;
                        }
                        else if (alternateRegions != null)
                        {
                            // Retry the other sign only after the primary sweep, and only if budget
                            // remains. Rebuild it from the then-current state so other accepted
                            // coordinate changes are preserved.
                            reverseCandidates.Add(new ReverseCandidate
                            {
                                RegionName = regions[regionIndex].Name,
                                Parameter = parameter,
                                Step = step,
                                Direction = -preferredDirection,
                                ProxyGap = proxyGap,
                            });
                        }
                    }
                }

                // Low proxy-gap coordinates are the least certain direction choices, so test their
                // opposite sign first. All candidates still need to improve the actual OBJ score.
                reverseCandidates.Sort((a, b) =>
                {
                    int gap = a.ProxyGap.CompareTo(b.ProxyGap);
                    if (gap != 0) return gap;
                    int region = StringComparer.Ordinal.Compare(a.RegionName, b.RegionName);
                    return region != 0 ? region : a.Parameter.CompareTo(b.Parameter);
                });

                foreach (ReverseCandidate reverse in reverseCandidates)
                {
                    if (meshEvaluations >= options.MaxMeshEvaluations) break;
                    int regionIndex = regions.FindIndex(r =>
                        string.Equals(r.Name, reverse.RegionName, StringComparison.OrdinalIgnoreCase));
                    if (regionIndex < 0) continue;
                    List<FitRegion>? candidateRegions = CreateCandidate(
                        regions, regionIndex, reverse.Parameter, reverse.Direction, reverse.Step, options);
                    if (candidateRegions == null) continue;

                    CreatureAnatomy candidateAnatomy = ApplyRegions(source, candidateRegions);
                    Metrics candidateProxy = Evaluate(candidateAnatomy, references, options);
                    proxyEvaluations++;
                    ObjMeshResult candidateObj = ObjMeshExporter.Export(
                        candidateAnatomy, options.ExportSides, options.ExportRings, options.UnitsPerPixel);
                    Metrics candidateMesh = EvaluateObj(candidateObj.ObjText, references, options.UnitsPerPixel);
                    meshEvaluations++;

                    if (candidateMesh.Score > currentMesh.Score + improvementEpsilon)
                    {
                        regions = candidateRegions;
                        currentProxy = candidateProxy;
                        currentMesh = candidateMesh;
                        currentObj = candidateObj;
                    }
                }

                scaleStep *= 0.5;
                offsetStep *= 0.5;
            }

            int changed = 0;
            var summary = new List<string>();
            foreach (FitRegion region in regions)
            {
                if (Math.Abs(region.Scale - 1.0) < 1e-5 && region.Offset.Length < 1e-4) continue;
                changed++;
                summary.Add($"{region.Name}: scale {region.Scale:0.###}, offset {region.Offset}");
            }

            CreatureAnatomy fitted = ApplyRegions(source, regions);
            return new GeometryFitResult(fitted, currentObj, references,
                initialProxy.Mean, currentProxy.Mean, initialProxy.Worst, currentProxy.Worst,
                initialMesh.Mean, currentMesh.Mean, initialMesh.Worst, currentMesh.Worst,
                meshEvaluations, proxyEvaluations, changed,
                summary.Count == 0 ? "参数未发生有效变化" : string.Join("; ", summary));
        }

        private static List<FitRegion>? CreateCandidate(List<FitRegion> regions, int regionIndex,
            FitParameter parameter, double direction, double step, GeometryFitOptions options)
        {
            var candidate = CopyRegions(regions);
            return TryAdjustParameter(candidate[regionIndex], parameter, direction * step, options)
                ? candidate : null;
        }

        private static bool TryAdjustParameter(FitRegion region, FitParameter parameter,
            double delta, GeometryFitOptions options)
        {
            switch (parameter)
            {
                case FitParameter.Scale:
                {
                    double value = Clamp(region.Scale + delta, 1.0 - options.MaxScaleDelta, 1.0 + options.MaxScaleDelta);
                    if (Math.Abs(value - region.Scale) < 1e-9) return false;
                    region.Scale = value;
                    return true;
                }
                case FitParameter.OffsetX:
                {
                    double value = Clamp(region.Offset.X + delta, -options.MaxOffset, options.MaxOffset);
                    if (Math.Abs(value - region.Offset.X) < 1e-9) return false;
                    region.Offset = region.Offset.WithX(value);
                    return true;
                }
                case FitParameter.OffsetY:
                {
                    double value = Clamp(region.Offset.Y + delta, -options.MaxOffset, options.MaxOffset);
                    if (Math.Abs(value - region.Offset.Y) < 1e-9) return false;
                    region.Offset = region.Offset.WithY(value);
                    return true;
                }
                default:
                {
                    double value = Clamp(region.Offset.Z + delta, -options.MaxOffset, options.MaxOffset);
                    if (Math.Abs(value - region.Offset.Z) < 1e-9) return false;
                    region.Offset = region.Offset.WithZ(value);
                    return true;
                }
            }
        }

        private static List<FitRegion> DiscoverRegions(CreatureAnatomy anatomy)
        {
            var anchors = new Dictionary<string, (Vec3 Sum, int Count)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (p.Group < 0 || p.Group >= anatomy.Groups.Count) continue;
                string groupName = anatomy.Groups[p.Group].Name;
                if (!TryGetFitRegionName(groupName, out string name)) continue;
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

        private static bool TryGetFitRegionName(string groupName, out string regionName)
        {
            if (FitGroupNames.Contains(groupName))
            {
                regionName = groupName;
                return true;
            }
            foreach (string candidate in FitGroupOrder)
            {
                if (!groupName.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase)) continue;
                regionName = candidate;
                return true;
            }
            regionName = string.Empty;
            return false;
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

        private static CreatureAnatomy ApplyRegions(CreatureAnatomy source, List<FitRegion> regions)
        {
            var byName = new Dictionary<string, FitRegion>(StringComparer.OrdinalIgnoreCase);
            foreach (FitRegion region in regions) byName[region.Name] = region;

            var primitives = new List<PrimitiveDef>(source.Primitives.Count);
            foreach (PrimitiveDef original in source.Primitives)
            {
                PrimitiveDef p = original.Clone();
                if (p.Group >= 0 && p.Group < source.Groups.Count &&
                    TryGetFitRegionName(source.Groups[p.Group].Name, out string regionName) &&
                    byName.TryGetValue(regionName, out FitRegion region))
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
                    TryGetFitRegionName(source.Groups[feature.Group].Name, out string regionName) &&
                    byName.TryGetValue(regionName, out FitRegion region))
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
            return EvaluateTriangles(BuildTriangles(anatomy, options.EllipsoidSides, options.EllipsoidRings), references);
        }

        // Measures the actual OBJ emitted by the existing exporter, so the proxy objective and
        // the final output can be compared independently. OBJ coordinates are restored to source
        // pixel units before projection into the original renderer's camera.
        private static Metrics EvaluateObj(string objText, PixelDepthCapture[] references, double unitsPerPixel)
        {
            var vertices = new List<Vec3>();
            var triangles = new List<Triangle3>();
            using var reader = new StringReader(objText);
            string? line;
            bool skipFeatureObject = false;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    // The 2D G-buffer silhouette intentionally excludes the separately stamped
                    // eyes/nostrils/spots. Exclude their dedicated OBJ objects for a fair body IoU.
                    skipFeatureObject = line.StartsWith("o feature_", StringComparison.Ordinal);
                    continue;
                }
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    vertices.Add(new Vec3(
                        double.Parse(parts[1], CultureInfo.InvariantCulture) / unitsPerPixel,
                        double.Parse(parts[2], CultureInfo.InvariantCulture) / unitsPerPixel,
                        double.Parse(parts[3], CultureInfo.InvariantCulture) / unitsPerPixel));
                }
                else if (!skipFeatureObject && line.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    int first = ObjVertexIndex(parts[1]);
                    for (int i = 2; i < parts.Length - 1; i++)
                    {
                        int second = ObjVertexIndex(parts[i]);
                        int third = ObjVertexIndex(parts[i + 1]);
                        if (first < 0 || second < 0 || third < 0 ||
                            first >= vertices.Count || second >= vertices.Count || third >= vertices.Count) continue;
                        AddTriangle(triangles, vertices[first], vertices[second], vertices[third]);
                    }
                }
            }
            return EvaluateTriangles(triangles, references);
        }

        private static int ObjVertexIndex(string token)
        {
            int slash = token.IndexOf('/');
            string indexText = slash >= 0 ? token.Substring(0, slash) : token;
            return int.Parse(indexText, CultureInfo.InvariantCulture) - 1;
        }

        private static Metrics EvaluateTriangles(List<Triangle3> triangles, PixelDepthCapture[] references)
        {
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
