// Experimental semantic-topology mesh path.
// The final skin mesh is not generated from SDF or voxel volumes. The legacy SDF exporter is
// called only to measure a comparison baseline; its geometry is never appended to the final output.
// The new path preserves source primitive boundaries and relaxes their vertices against multi-view data.
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
        public double MaxMovePerPass = 0.15;
        public double MaxTotalMove = 0.50;
        public double UnitsPerPixel = 0.04;
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
    /// indexed surface for each source anatomy primitive. Vertex positions are then constrained
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
            var patches = new List<Patch>();
            var corePrimitiveIndices = new HashSet<int>();

            // Preserve the source anatomy's primitive boundaries. A connected-component radial
            // envelope destroys concavities and can wrap an entire torso/limb chain into a blob.
            // Each source ellipsoid or tapered segment therefore gets its own indexed surface.
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef primitive = anatomy.Primitives[i];
                if (!IsCoreSkin(anatomy, primitive)) continue;
                Patch patch = BuildPrimitivePatch(anatomy, primitive, i,
                    options.LatitudeSegments, options.LongitudeSegments);
                if (patch.Vertices.Count < 8 || patch.Faces.Count < 12) continue;
                patches.Add(patch);
                corePrimitiveIndices.Add(i);
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
                !DMath.IsFinite(options.MaxTotalMove) || options.MaxTotalMove <= 0 || options.MaxTotalMove > 2 ||
                !DMath.IsFinite(options.UnitsPerPixel) || options.UnitsPerPixel <= 0)
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



    }
}
