// Experimental semantic-topology mesh path.
// Unlike ImplicitBodyMesher and MultiviewReconstructor, this path never builds an SDF or voxel volume.
// It builds a fixed indexed surface per semantic anatomy group, then relaxes those vertices against
// the original renderer's multi-view silhouette/depth observations. It is a prototype, not a final rig.
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
    /// radial surface for each anatomy group. Vertex positions are then constrained by multi-view
    /// raster observations. No SDF union, marching tetrahedra, or group-scale/offset search is used.
    /// </summary>
    public static class SemanticTopologyPrototype
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
                if (!samplesByGroup.TryGetValue(primitive.Group, out List<PrimitiveSample>? regionSamples))
                    samplesByGroup.Add(primitive.Group, regionSamples = new List<PrimitiveSample>());
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
                    {
                        regionSamples.AddRange(primitiveSample.Points);
                        corePrimitiveIndices.Add(primitiveSample.PrimitiveIndex);
                    }
                    if (regionSamples.Count < 12) continue;
                    string regionName = (anatomy.Groups[pair.Key].Name ?? "group") +
                        "_part_" + componentIndex.ToString(CultureInfo.InvariantCulture);
                    Patch patch = BuildRadialPatch(pair.Key, regionName, regionSamples,
                        options.LatitudeSegments, options.LongitudeSegments);
                    if (patch.Vertices.Count >= 8 && patch.Faces.Count >= 12) patches.Add(patch);
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

        private static ObjMeshResult EmitObj(CreatureModel model, List<Patch> patches,
            HashSet<int> corePrimitiveIndices, PixelDepthCapture[] captures, double unitsPerPixel)
        {
            CreatureAnatomy anatomy = model.Anatomy;
            var obj = new StringBuilder();
            obj.AppendLine("# Semantic topology prototype: indexed per-region radial surfaces + multiview vertex relaxation");
            obj.AppendLine("mtllib creature.mtl");
            var writer = new ObjWriter(obj, unitsPerPixel);
            ViewConstraint[] views = CreateViews(captures);

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
                        normal, captures, GetRotations(views), GetOffsets(views), GetEyes(views), model.Palette, anatomy);
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
