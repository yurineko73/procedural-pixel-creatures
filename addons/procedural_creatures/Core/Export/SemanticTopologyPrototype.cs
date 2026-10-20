// Experimental semantic-topology mesh path.
// The final skin mesh is not generated from SDF or voxel volumes. The legacy SDF exporter is
// called only to measure a comparison baseline; its geometry is never appended to the final output.
// The new path builds indexed semantic surfaces and relaxes their vertices against multi-view data.
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
    public sealed class SemanticTopologyOptions
    {
        public int Directions = 12;
        public int LatitudeSegments = 18;
        public int LongitudeSegments = 36;
        public int RelaxationPasses = 3;
        public int SilhouetteSearchRadius = 3;
        public double DepthSlack = 0.25;
        public double MaxMovePerPass = 0.30;
        public double UnitsPerPixel = 0.04;
        public int SampleSides = 24;
        public int SampleRings = 12;
    }

    public sealed class SemanticTopologyResult
    {
        public ObjMeshResult Mesh { get; }
        public PixelDepthCapture[] ReferenceViews { get; }
        public int RegionCount { get; }
        public int RelaxedVertices { get; }
        public double BaselineMeanIoU { get; }
        public double TemplateMeanIoU { get; }
        public double FittedMeanIoU { get; }
        public double BaselineWorstIoU { get; }
        public double TemplateWorstIoU { get; }
        public double FittedWorstIoU { get; }

        internal SemanticTopologyResult(ObjMeshResult mesh, PixelDepthCapture[] referenceViews,
            int regionCount, int relaxedVertices,
            double baselineMeanIoU, double templateMeanIoU, double fittedMeanIoU,
            double baselineWorstIoU, double templateWorstIoU, double fittedWorstIoU)
        {
            Mesh = mesh;
            ReferenceViews = referenceViews;
            RegionCount = regionCount;
            RelaxedVertices = relaxedVertices;
            BaselineMeanIoU = baselineMeanIoU;
            TemplateMeanIoU = templateMeanIoU;
            FittedMeanIoU = fittedMeanIoU;
            BaselineWorstIoU = baselineWorstIoU;
            TemplateWorstIoU = templateWorstIoU;
            FittedWorstIoU = fittedWorstIoU;
        }
    }

    /// <summary>
    /// A separate mesh construction path: semantic primitive samples seed a regular indexed
    /// radial surface for each connected anatomy component. Vertex positions are then constrained
    /// by multi-view raster observations. SDF output is used only as a comparison metric, not as final geometry.
    /// </summary>
    public static partial class SemanticTopologyPrototype
    {
        private sealed class Patch
        {
            public int GroupIndex;
            public string Name = string.Empty;
            public Vec3 Center;
            public readonly List<Vec3> Vertices = new List<Vec3>();
            public readonly List<(int A, int B, int C)> Faces = new List<(int, int, int)>();
        }

        private sealed class PrimitiveSample
        {
            public int PrimitiveIndex;
            public int GroupIndex;
            public List<Vec3> Points = new List<Vec3>();
            public Vec3 Min;
            public Vec3 Max;
        }

        private sealed class ViewConstraint
        {
            public PixelDepthCapture Capture = null!;
            public Mat3 Rotation;
            public Vec3 Offset;
        }

        public static SemanticTopologyResult Build(CreatureModel model, SemanticTopologyOptions? options = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            options ??= new SemanticTopologyOptions();
            Validate(options);

            CreatureAnatomy anatomy = model.Anatomy;
            PixelDepthCapture[] captures = MultiviewReconstructor.Capture(model, options.Directions);
            ViewConstraint[] views = CreateViews(captures);
            var samplesByGroup = new SortedDictionary<int, List<PrimitiveSample>>();
            var corePrimitiveIndices = new HashSet<int>();

            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef primitive = anatomy.Primitives[i];
                if (!IsCoreSkin(anatomy, primitive)) continue;
                List<Vec3> samples = SamplePrimitive(anatomy, primitive, options.SampleSides, options.SampleRings);
                if (samples.Count < 12) continue;
                GetBounds(samples, out Vec3 min, out Vec3 max);
                var sampledPrimitive = new PrimitiveSample
                {
                    PrimitiveIndex = i,
                    GroupIndex = primitive.Group,
                    Points = samples,
                    Min = min,
                    Max = max,
                };
                if (!samplesByGroup.TryGetValue(primitive.Group, out List<PrimitiveSample>? regionSamples) || regionSamples == null)
                {
                    regionSamples = new List<PrimitiveSample>();
                    samplesByGroup.Add(primitive.Group, regionSamples);
                }
                regionSamples.Add(sampledPrimitive);
            }

            var patches = new List<Patch>();
            foreach (KeyValuePair<int, List<PrimitiveSample>> pair in samplesByGroup)
            {
                // A legacy semantic group can contain both left and right limbs. Split it into
                // spatially connected components first so they do not collapse into one radial blob.
                var pending = new List<PrimitiveSample>(pair.Value);
                double connectionGap = Math.Max(1.0, anatomy.Groups[pair.Key].BlendRadius * 1.5);
                int componentIndex = 0;
                while (pending.Count > 0)
                {
                    var component = new List<PrimitiveSample> { pending[0] };
                    pending.RemoveAt(0);
                    for (int cursor = 0; cursor < component.Count; cursor++)
                    {
                        for (int candidate = pending.Count - 1; candidate >= 0; candidate--)
                        {
                            if (BoundsGap(component[cursor], pending[candidate]) > connectionGap) continue;
                            component.Add(pending[candidate]);
                            pending.RemoveAt(candidate);
                        }
                    }

                    var regionSamples = new List<Vec3>();
                    foreach (PrimitiveSample primitiveSample in component)
                        regionSamples.AddRange(primitiveSample.Points);
                    if (regionSamples.Count < 12) continue;
                    string regionName = (anatomy.Groups[pair.Key].Name ?? "group") +
                        "_part_" + componentIndex.ToString(CultureInfo.InvariantCulture);
                    Patch patch = BuildRadialPatch(pair.Key, regionName, regionSamples,
                        options.LatitudeSegments, options.LongitudeSegments);
                    if (patch.Vertices.Count >= 8 && patch.Faces.Count >= 12)
                    {
                        patches.Add(patch);
                        foreach (PrimitiveSample primitiveSample in component)
                            corePrimitiveIndices.Add(primitiveSample.PrimitiveIndex);
                    }
                    componentIndex++;
                }
            }
            if (patches.Count == 0)
                throw new InvalidOperationException("No semantic skin regions could be converted to indexed topology.");

            MultiviewResult sourceMesh = ParseObjForMetrics(
                ObjMeshExporter.Export(anatomy, 16, 8, options.UnitsPerPixel).ObjText, options.UnitsPerPixel, "organic_body_");
            double[] baselineScores = MultiviewSilhouetteMetrics.IntersectionOverUnion(sourceMesh, captures);
            MultiviewResult templateMesh = ToMetricMesh(patches);
            double[] templateScores = MultiviewSilhouetteMetrics.IntersectionOverUnion(templateMesh, captures);

            int relaxed = RelaxVertices(patches, views, options);
            MultiviewResult fittedMesh = ToMetricMesh(patches);
            double[] fittedScores = MultiviewSilhouetteMetrics.IntersectionOverUnion(fittedMesh, captures);

            ObjMeshResult mesh = EmitObj(model, patches, corePrimitiveIndices, captures, options.UnitsPerPixel);
            return new SemanticTopologyResult(mesh, captures, patches.Count, relaxed,
                Mean(baselineScores), Mean(templateScores), Mean(fittedScores),
                Worst(baselineScores), Worst(templateScores), Worst(fittedScores));
        }

        private static void Validate(SemanticTopologyOptions options)
        {
            if (options.Directions < 4 || options.Directions > 32 ||
                options.LatitudeSegments < 8 || options.LatitudeSegments > 48 ||
                options.LongitudeSegments < 12 || options.LongitudeSegments > 96 ||
                options.RelaxationPasses < 0 || options.RelaxationPasses > 8 ||
                options.SilhouetteSearchRadius < 1 || options.SilhouetteSearchRadius > 8 ||
                !DMath.IsFinite(options.DepthSlack) || options.DepthSlack < 0 || options.DepthSlack > 3 ||
                !DMath.IsFinite(options.MaxMovePerPass) || options.MaxMovePerPass <= 0 || options.MaxMovePerPass > 1 ||
                !DMath.IsFinite(options.UnitsPerPixel) || options.UnitsPerPixel <= 0 ||
                options.SampleSides < 8 || options.SampleSides > 64 ||
                options.SampleRings < 4 || options.SampleRings > 32)
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid semantic topology settings.");
        }

        private static bool IsCoreSkin(CreatureAnatomy anatomy, PrimitiveDef primitive)
        {
            if (primitive.Group < 0 || primitive.Group >= anatomy.Groups.Count) return false;
            if (primitive.Kind != PrimKind.Ellipsoid && primitive.Kind != PrimKind.RoundCone) return false;
            if (primitive.Material != MaterialSlot.Primary && primitive.Material != MaterialSlot.Secondary &&
                primitive.Material != MaterialSlot.Accent && primitive.Material != MaterialSlot.White) return false;
            if ((primitive.Flags & (PrimFlags.Hard | PrimFlags.Thin | PrimFlags.Translucent | PrimFlags.FrontOnly)) != 0)
                return false;
            PartGroup group = anatomy.Groups[primitive.Group];
            if ((group.Flags & GroupFlags.Interior) != 0) return false;
            string name = group.Name ?? string.Empty;
            if (IsEquipmentGroup(name) || IsStandaloneDetailGroup(name)) return false;
            return true;
        }

        private static bool IsEquipmentGroup(string name) =>
            name.StartsWith("gear", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("equipment", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("pauldron", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsStandaloneDetailGroup(string name)
        {
            string[] tokens =
            {
                "horn", "ear.", ".ear", "antenna", "whisker", "claw", "teeth", "fang", "tusk",
                "mandible", "pincer", "stinger", "spike", "thorn", "wing", "fin", "membrane",
                "shell", "crown", "crest", "leaf", "tentacle", "eyestalk", "stalkeye"
            };
            foreach (string token in tokens)
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static List<Vec3> SamplePrimitive(CreatureAnatomy anatomy, PrimitiveDef p, int sides, int rings)
        {
            var points = new List<Vec3>();
            if (p.BoneA < 0 || p.BoneA >= anatomy.Skeleton.Count) return points;
            Xform boneA = anatomy.Skeleton.RestWorld(p.BoneA);
            if (p.Kind == PrimKind.Ellipsoid)
            {
                Xform local = Xform.FromQuat(p.LocalRotation, p.LocalA);
                points.Add(boneA.Point(local.Point(new Vec3(0, -p.Radii.Y, 0))));
                points.Add(boneA.Point(local.Point(new Vec3(0, p.Radii.Y, 0))));
                for (int j = 1; j < rings; j++)
                {
                    double latitude = -Math.PI * 0.5 + Math.PI * j / rings;
                    double h = Math.Cos(latitude), y = Math.Sin(latitude);
                    for (int i = 0; i < sides; i++)
                    {
                        double angle = 2.0 * Math.PI * i / sides;
                        Vec3 surface = new Vec3(p.Radii.X * h * Math.Cos(angle),
                            p.Radii.Y * y, p.Radii.Z * h * Math.Sin(angle));
                        points.Add(boneA.Point(local.Point(surface)));
                    }
                }
            }
            else if (p.Kind == PrimKind.RoundCone && p.BoneB >= 0 && p.BoneB < anatomy.Skeleton.Count)
            {
                Vec3 a = boneA.Point(p.LocalA);
                Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                Vec3 axis = (b - a).NormalizedOr(Vec3.UnitY);
                Vec3 guide = Math.Abs(Vec3.Dot(axis, Vec3.UnitY)) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
                Vec3 u = Vec3.Cross(axis, guide).NormalizedOr(Vec3.UnitZ);
                Vec3 v = Vec3.Cross(axis, u).NormalizedOr(Vec3.UnitX);
                for (int j = 0; j <= rings; j++)
                {
                    double t = j / (double)rings;
                    Vec3 center = Vec3.Lerp(a, b, t);
                    double radius = p.RadiusA + (p.RadiusB - p.RadiusA) * t;
                    for (int i = 0; i < sides; i++)
                    {
                        double angle = 2.0 * Math.PI * i / sides;
                        points.Add(center + (u * Math.Cos(angle) + v * Math.Sin(angle)) * radius);
                    }
                }
            }
            return points;
        }


    }
}
