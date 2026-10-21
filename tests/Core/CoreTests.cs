// Procedural Pixel Creatures - engine independent test suite (runs in the console harness and inside
// Godot via --self-test). Everything here is deterministic.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Equipment;
using PixelCreatures.Core.Export;
using PixelCreatures.Core.Genetics;
using PixelCreatures.Core.Motion;
using PixelCreatures.Core.Rendering;
using PixelCreatures.Core.Serialization;
using PixelCreatures.Core.Shapes;

namespace PixelCreatures.Tests
{
    public static class CoreTests
    {
        public static void RunAll(TestRunner r, CreatureRegistry registry, int seedsPerFamily = 100)
        {
            var factory = new CreatureFactory(registry);
            r.Run("math.deterministic_kernels", ctx => DeterministicMath(ctx));
            r.Run("random.golden_sequences", ctx => RandomGolden(ctx));
            r.Run("genome.sampling_is_deterministic", ctx => SamplingDeterminism(ctx, registry));
            r.Run("genome.streams_are_independent", ctx => StreamIndependence(ctx, registry, factory));
            r.Run("genome.quantized_json_roundtrip", ctx => JsonRoundTrip(ctx, registry));
            r.Run("serializer.rejects_invalid_input", ctx => SerializerValidation(ctx, registry));
            r.Run("serializer.migration_chain", ctx => MigrationChain(ctx, registry));
            r.Run("genetics.mutation_deterministic_and_locked", ctx => MutationTests(ctx, registry));
            r.Run("genetics.crossover_and_compatibility", ctx => CrossoverTests(ctx, registry));
            r.Run("genetics.regenerate_respects_locks", ctx => RegenerateTests(ctx, registry));
            r.Run($"anatomy.valid_for_{seedsPerFamily}_seeds_per_family", ctx => AnatomyValidity(ctx, registry, factory, seedsPerFamily));
            r.Run("render.deterministic_and_golden", ctx => RenderDeterminism(ctx, registry, factory));
            r.Run("motion.framerate_independent", ctx => MotionFramerateIndependence(ctx, registry, factory));
            r.Run("motion.planted_feet_do_not_slide", ctx => FootSliding(ctx, registry, factory));
            r.Run("motion.states_and_directions_stay_in_canvas", ctx => CanvasSafety(ctx, registry, factory));
            r.Run("motion.turning_in_place_reorients_body", ctx => TurnInPlace(ctx, registry, factory));
            r.Run("anatomy.extreme_genes_build_move_and_render", ctx => ExtremeGenes(ctx, registry, factory));
            r.Run("export.obj_static_all_families", ctx => ObjExportSmoke(ctx, registry));
            r.Run("export.preview_reported_seed_regressions", ctx => PreviewScreenshotSeedsSmoke(ctx, registry));
            r.Run("export.obj_unifies_organic_primitives", ctx => ObjOrganicUnion(ctx));
            r.Run("export.topology_first_body_stitches_shared_loops", ctx => TopologyFirstBodySmoke(ctx));
            r.Run("export.obj_two_sided_triangle_normals", ctx => ObjTwoSidedTriangleNormals(ctx));
            r.Run("export.preview_attached_claws_and_teeth", ctx => PreviewAttachmentSmoke(ctx));
            r.Run("export.preview_dental_lip_fit", ctx => PreviewDentalLipFitSmoke(ctx));
            r.Run("export.preview_visible_mouth_cavity", ctx => PreviewVisibleMouthSmoke(ctx));
            r.Run("export.preview_ears_embed_in_skull", ctx => PreviewEarRootSmoke(ctx));
            r.Run("export.preview_horn_roots_and_eye_clearance", ctx => PreviewHornRootSmoke(ctx));
            r.Run("export.preview_arthropod_leg_chain", ctx => PreviewArthropodChainSmoke(ctx));
            r.Run("export.preview_claw_toe_fit", ctx => PreviewClawToeFitSmoke(ctx));
            r.Run("export.preview_fixed_user_seeds", ctx => PreviewFixedUserSeedsSmoke(ctx, registry));
            r.Run("export.preview_ancestor_limb_attachment", ctx => PreviewAncestorAttachmentSmoke(ctx));
            r.Run("export.preview_fuses_soft_accent_and_faceted_skin", ctx => PreviewSkinUnionSmoke(ctx));
            r.Run("export.preview_standalone_appendage_materials", ctx => PreviewStandaloneAppendageSmoke(ctx));
            r.Run("export.preview_eyestalk_bulb_fit", ctx => PreviewEyeStalkSmoke(ctx));
            r.Run("export.preview_eye_surface_projection", ctx => PreviewEyeSurfaceProjectionSmoke(ctx));
            r.Run("export.preview_headgear_eye_clearance", ctx => PreviewHeadgearClearanceSmoke(ctx));
            r.Run("export.preview_eye_stamp_scale", ctx => PreviewEyeStampScaleSmoke(ctx));
            r.Run("export.preview_weapon_hand_socket", ctx => PreviewSocketSmoke(ctx));
            r.Run("export.preview_extreme_garment_fallback", ctx => PreviewGarmentFitSmoke(ctx));
            r.Run("export.preview_tiny_sdf_island_cleanup", ctx => PreviewMeshCleanerSmoke(ctx));
            r.Run("equipment.style_overlay_deterministic", ctx => EquipmentStyleSmoke(ctx, registry));
            r.Run("equipment.slot_seeds_are_independent", ctx => EquipmentSlotIndependence(ctx, registry));
            r.Run("equipment.native_gear_slot_seeds_are_independent", ctx => NativeGearSlotIndependence(ctx, registry));
        }

        // ------------------------------------------------------------------ math & random

        // ------------------------------------------------------------------ optional 3D export & style packs

        private static void ObjExportSmoke(TestContext ctx, CreatureRegistry reg)
        {
            foreach (var family in reg.Families)
            {
                var g = GenomeFactory.Sample(family, 4271);
                var a = family.BuildAnatomy(g, reg);
                var first = ObjMeshExporter.Export(a, 8, 4);
                var second = ObjMeshExporter.Export(a, 8, 4);
                ctx.True(first.ObjText == second.ObjText, family.Id + ": OBJ changed for identical input");
                ctx.True(first.Vertices > 0 && first.Triangles > 0, family.Id + ": no OBJ geometry");
                ctx.True(first.ObjText.Contains("mtllib creature.mtl", StringComparison.Ordinal), family.Id + ": missing MTL");
                ctx.True(first.MtlText.Contains("newmtl Primary", StringComparison.Ordinal), family.Id + ": missing material");
                if (family.Id == "biped")
                {
                    ctx.True(first.ObjText.Contains("usemtl FeatureSocket", StringComparison.Ordinal), "biped: missing 3D eye sockets");
                    ctx.True(first.ObjText.Contains("usemtl FeaturePupil", StringComparison.Ordinal), "biped: missing 3D pupils");
                    ctx.True(first.ObjText.Contains("usemtl FeatureHighlight", StringComparison.Ordinal), "biped: missing eye highlights");
                    ctx.True(first.MtlText.Contains("newmtl FeatureSclera", StringComparison.Ordinal), "biped: missing sclera material");

                }
                ctx.True(!first.ObjText.Contains("NaN", StringComparison.Ordinal) &&
                    !first.ObjText.Contains("Infinity", StringComparison.Ordinal), family.Id + ": nonfinite OBJ values");
            }
        }

        private static void PreviewScreenshotSeedsSmoke(TestContext ctx, CreatureRegistry reg)
        {
            // These are resolved Body Seeds shown in user-reported multi-family screenshots
            // of detached claws, face pieces, hoof shells and arthropod appendages. Equipment
            // is independently seeded in the UI; that second dimension has separate tests.
            var cases = new (string Family, ulong BodySeed)[]
            {
                ("biped", 1120767436259506315UL),
                ("biped", 479287935487803784UL),
                ("biped", 5437137294946840838UL), // user's detached ear screenshot
                ("quadruped", 6484199581679128020UL),
                ("reptile", 5588374102731924563UL),
                ("arthropod", 17995465277924679519UL),
                ("arthropod", 14734810077418951438UL), // user's disconnected leg screenshot
                ("amorphous", 12144879715512382633UL),
            };
            foreach (var testCase in cases)
            {
                var family = reg.Families.First(f => f.Id == testCase.Family);
                var genome = GenomeFactory.Sample(family, testCase.BodySeed);
                var anatomy = family.BuildAnatomy(genome, reg);
                var first = ObjMeshExporter.Export(anatomy, 8, 4);
                var second = ObjMeshExporter.Export(anatomy, 8, 4);
                string description = testCase.Family + " seed " + testCase.BodySeed;
                ctx.True(first.Triangles > 0, description + ": missing preview mesh");
                ctx.True(first.ObjText == second.ObjText, description + ": nondeterministic OBJ");
                ctx.True(first.ObjText.Contains("o organic_body_", StringComparison.Ordinal),
                    description + ": no fused organic skin");
                ctx.True(!first.ObjText.Contains("NaN", StringComparison.Ordinal) &&
                    !first.ObjText.Contains("Infinity", StringComparison.Ordinal),
                    description + ": invalid exported vertex");
            }
        }

        private static void ObjOrganicUnion(TestContext ctx)
        {
            var builder = new AnatomyBuilder("organic_union_test", 1);
            int root = builder.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int skin = builder.Group("body", 1.2);
            builder.Ellipsoid(skin, root, new Vec3(-1, 0, 0), new Vec3(3, 4, 3), MaterialSlot.Primary);
            builder.Ellipsoid(skin, root, new Vec3(1, 0, 0), new Vec3(3, 4, 3), MaterialSlot.Secondary);
            int gear = builder.Group("gear.helmet", 0.2);
            builder.Ellipsoid(gear, root, new Vec3(0, 5, 0), new Vec3(1, 1, 1), MaterialSlot.Cloth);
            int tunic = builder.Group("gear.tunic", 0.4);
            builder.Ellipsoid(tunic, root, new Vec3(0, 0, 0), new Vec3(4, 5, 4), MaterialSlot.Cloth);
            builder.Ellipsoid(tunic, root, new Vec3(0, 1, 0), new Vec3(4.5, 4.5, 4.5), MaterialSlot.Cloth);

            var anatomy = builder.Build(1);
            var fusedSkin = ImplicitBodyMesher.Build(anatomy);
            var result = ObjMeshExporter.Export(anatomy, 8, 4);
            ctx.True(result.ObjText.Contains("o organic_body_Primary", StringComparison.Ordinal),
                "organic primary surfaces should be emitted as one fused outer skin");
            ctx.True(result.ObjText.Contains("o organic_body_Secondary", StringComparison.Ordinal),
                "organic secondary surfaces should preserve their material regions");
            ctx.True(!result.ObjText.Contains("o primitive_0", StringComparison.Ordinal) &&
                !result.ObjText.Contains("o primitive_1", StringComparison.Ordinal),
                "unified organic primitives must not also be exported as overlapping closed shells");
            ctx.True(result.ObjText.Contains("o primitive_2", StringComparison.Ordinal),
                "hard/unsupported gear groups must remain separate from the organic skin union");
            ctx.True(result.ObjText.Contains("o garment_gear_tunic_Cloth", StringComparison.Ordinal),
                "soft tunic ellipsoids should be fused into one fitted garment surface");
            ctx.True(!result.ObjText.Contains("o primitive_3", StringComparison.Ordinal) &&
                !result.ObjText.Contains("o primitive_4", StringComparison.Ordinal),
                "fused garment primitives must not also be exported as overlapping closed shells");
            ctx.True(fusedSkin.BoundaryEdgeCount == 0 && fusedSkin.NonManifoldEdgeCount == 0,
                "the fused organic skin must be a closed two-manifold surface");
            ctx.True(result.Triangles > 0, "organic union must generate a nonempty surface");
        }


        private static void TopologyFirstBodySmoke(TestContext ctx)
        {
            var builder = new AnatomyBuilder("topology_first_test", 1);
            int root = builder.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int limb = builder.Bone("limb", root, new Vec3(5, 0, 0), Quat.Identity, 5, BoneRole.Foot, 1);
            int body = builder.Group("body", 1.0);
            builder.Ellipsoid(body, root, Vec3.Zero, new Vec3(4, 4, 4), MaterialSlot.Primary).Tag = "body.core";
            builder.Ellipsoid(body, limb, Vec3.Zero, new Vec3(1.2, 1.2, 1.2), MaterialSlot.Primary).Tag = "limb.joint";
            builder.Cone(body, root, new Vec3(3.2, 0, 0), 0.9,
                limb, new Vec3(-0.4, 0, 0), 0.6, MaterialSlot.Primary).Tag = "limb.segment";

            var anatomy = builder.Build(1);
            var mesh = TopologyFirstBodyMesher.Build(anatomy);
            ctx.True(mesh.PrimitiveIndices.Contains(0) && mesh.PrimitiveIndices.Contains(1) &&
                mesh.PrimitiveIndices.Contains(2),
                "the topology-first mesh must consume both source skin patches and the source segment");
            ctx.True(mesh.Vertices.Count > 0 && mesh.Faces.Count > 0,
                "the explicit patch mesh must contain vertices and faces");
            ctx.True(mesh.FailedConnections == 0,
                "the explicit source segment should find a boundary loop on both end patches");
            ctx.True(mesh.BoundaryEdgeCount == 0 && mesh.NonManifoldEdgeCount == 0,
                "stitched patch boundaries should form a closed two-manifold mesh");
            ctx.True(mesh.ConnectedComponentCount == 1,
                "the torso and limb patches should become one connected surface");

            ObjMeshResult exported = ObjMeshExporter.Export(anatomy, 12, 6, topologyFirst: true);
            ctx.True(exported.ObjText.Contains("o topology_body_Primary", StringComparison.Ordinal),
                "the preview exporter should expose the explicit topology body");
            ctx.True(!exported.ObjText.Contains("o organic_body_", StringComparison.Ordinal),
                "the topology-first route must not invoke the legacy SDF body exporter");
            ctx.True(!exported.ObjText.Contains("o primitive_0", StringComparison.Ordinal) &&
                !exported.ObjText.Contains("o primitive_1", StringComparison.Ordinal) &&
                !exported.ObjText.Contains("o primitive_2", StringComparison.Ordinal),
                "consumed source skin and connector primitives must not reappear as independent shells");
            ctx.True(exported.MethodSummary.Contains("显式拓扑", StringComparison.Ordinal),
                "the exported preview should identify the topology-first algorithm");
            ctx.True(exported.ObjText.Contains("s 1", StringComparison.Ordinal),
                "the OBJ should enable smooth normals for connected body surfaces");
        }

        private static void PreviewVisibleMouthSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 115);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            Heads.Build(b, new HeadSpec
            {
                Name = "head", ParentBone = root, Position = new Vec3(0, 20, 0),
                Forward = Vec3.UnitX, SkullLength = 11, SkullHeight = 9, SkullWidth = 9,
                SnoutLength = 8, SnoutRadius = 4, SnoutTaper = 0.78,
                JawLength = 0.8, JawRadius = 3, JawDepth = 0.8,
                Teeth = TeethKind.Fangs, EyeCount = 0
            });
            var anatomy = b.Build(1);
            ctx.True(anatomy.Primitives.Any(p => p.Tag == "head.mouthInterior"),
                "legacy anatomy must still contain the originally hidden mouth cavity");
            var surface = ImplicitBodyMesher.Build(anatomy);
            ctx.True(surface.Faces.Any(f => f.Material == MaterialSlot.Mouth),
                "3D preview must expose a visible dark mouth surface, not merely detached teeth");
            var mouthFaces = surface.Faces.Where(f => f.Material == MaterialSlot.Mouth).ToArray();
            var cavity = anatomy.Primitives.First(p => p.Tag == "head.mouthInterior");
            Vec3 cavityWorld = anatomy.Skeleton.RestWorld(cavity.BoneA).Point(cavity.LocalA);
            double mouthSurfaceY = mouthFaces.Average(f =>
                (surface.Vertices[f.A].Y + surface.Vertices[f.B].Y + surface.Vertices[f.C].Y) / 3.0);
            ctx.True(mouthSurfaceY > cavityWorld.Y - cavity.Radii.Y * 1.15,
                "mouth cut must follow the authored cavity, not a guessed point far below the muzzle");
            ctx.True(surface.BoundaryEdgeCount == 0 && surface.NonManifoldEdgeCount == 0,
                "mouth subtraction should not produce holes or non-manifold skin");
            string obj = ObjMeshExporter.Export(anatomy, 10, 6).ObjText;
            ctx.True(obj.Contains("o organic_body_Mouth", StringComparison.Ordinal),
                "OBJ export must retain the mouth material on the carved inner wall");
        }

        private static void PreviewEarRootSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 116);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int head = b.Bone("head", root, new Vec3(0, 12, 0), Quat.Identity, 0, BoneRole.Head);
            int group = b.Group("head", 0.8);
            b.Ellipsoid(group, head, Vec3.Zero, new Vec3(5, 5, 4), MaterialSlot.Primary)
                .Tag = "head.cranium";
            Ears.Build(b, new EarSpec
            {
                Name = "ear", HeadBone = head,
                Base = new Vec3(0, 4.6, 4.2), Size = 7,
                Kind = EarKind.Pointed, Width = 0.65
            });
            var anatomy = b.Build(1);
            var fit = new PreviewHeadAttachmentFit(anatomy);
            var ear = anatomy.Primitives.First(p => p.Tag == "ear.outer");
            var inner = anatomy.Primitives.First(p => p.Tag == "ear.inner" &&
                p.Group == ear.Group);
            Vec3 worldRoot = anatomy.Skeleton.RestWorld(ear.BoneA).Point(ear.LocalA);
            Vec3 newRoot = fit.Transform(ear, worldRoot);
            Vec3 shift = newRoot - worldRoot;
            ctx.True(fit.HasFittedEar(ear.Group) && shift.Length > 0.2,
                "overhanging ear should move its root into the head surface");
            ctx.True((fit.Transform(inner, worldRoot) - worldRoot - shift).Length < 1e-8,
                "inner ear cartilage must receive the exact same displacement as its outer shell");
            Vec3 local = anatomy.Skeleton.RestWorld(head).InversePoint(newRoot);
            double normalized = Math.Sqrt(local.X * local.X / 25 +
                local.Y * local.Y / 25 + local.Z * local.Z / 16);
            ctx.True(normalized < 1.0,
                "the corrected ear root should be slightly embedded inside the skull");
            ctx.True(anatomy.Skeleton.RestWorld(ear.BoneA).Point(ear.LocalA).Equals(worldRoot),
                "3D ear fitting must not mutate source bones");
        }

        private static void PreviewHornRootSmoke(TestContext ctx)
        {
            var withoutEye = BuildHornRootTestAnatomy(false);
            var withEye = BuildHornRootTestAnatomy(true);
            var hornWithoutEye = withoutEye.Primitives.First(p =>
                withoutEye.Groups[p.Group].Name == "horn.R");
            var hornWithEye = withEye.Primitives.First(p =>
                withEye.Groups[p.Group].Name == "horn.R");

            var fitWithoutEye = new PreviewHeadAttachmentFit(withoutEye);
            var fitWithEye = new PreviewHeadAttachmentFit(withEye);
            Vec3 rootWithoutEye = withoutEye.Skeleton.RestWorld(hornWithoutEye.BoneA)
                .Point(hornWithoutEye.LocalA);
            Vec3 rootWithEye = withEye.Skeleton.RestWorld(hornWithEye.BoneA)
                .Point(hornWithEye.LocalA);
            Vec3 offsetWithoutEye = fitWithoutEye.Transform(hornWithoutEye, rootWithoutEye) - rootWithoutEye;
            Vec3 offsetWithEye = fitWithEye.Transform(hornWithEye, rootWithEye) - rootWithEye;

            ctx.True(fitWithoutEye.HasFittedHorn(hornWithoutEye.Group) &&
                offsetWithoutEye.Length > 0.2,
                "horn root must be buried inside the skull so its flat cap is hidden");
            Vec3 localRoot = withoutEye.Skeleton.RestWorld(hornWithoutEye.BoneA)
                .InversePoint(rootWithoutEye + offsetWithoutEye);
            double normalized = Math.Sqrt(localRoot.X * localRoot.X / 25.0 +
                localRoot.Y * localRoot.Y / 25.0 + localRoot.Z * localRoot.Z / 16.0);
            ctx.True(normalized < 1.0, "fitted horn root must lie inside the cranium");
            Vec3 localRootWithEye = withEye.Skeleton.RestWorld(hornWithEye.BoneA)
                .InversePoint(rootWithEye + offsetWithEye);
            double normalizedWithEye = Math.Sqrt(localRootWithEye.X * localRootWithEye.X / 25.0 +
                localRootWithEye.Y * localRootWithEye.Y / 25.0 + localRootWithEye.Z * localRootWithEye.Z / 16.0);
            ctx.True(normalizedWithEye < 1.0,
                "eye clearance must not pull the horn root back outside the cranium");

            FeatureDef eye = withEye.Features.First(f => f.Kind == FeatureKind.Eye);
            Vec3 eyeWorld = withEye.Skeleton.RestWorld(eye.Bone).Point(eye.LocalPosition);
            double before = MinDistanceToAppendageGroup(withEye, hornWithEye.Group,
                eyeWorld, offsetWithoutEye);
            double after = MinDistanceToAppendageGroup(withEye, hornWithEye.Group,
                eyeWorld, offsetWithEye);
            ctx.True(after > before + 0.1,
                "a horn close to an eye must be shifted away from the eye in the 3D preview");
        }

        private static CreatureAnatomy BuildHornRootTestAnatomy(bool withEye)
        {
            var b = new AnatomyBuilder("biped", 118);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int head = b.Bone("head", root, new Vec3(0, 12, 0), Quat.Identity, 0, BoneRole.Head);
            int headGroup = b.Group("head", 0.8);
            b.Ellipsoid(headGroup, head, Vec3.Zero, new Vec3(5, 5, 4), MaterialSlot.Primary)
                .Tag = "head.cranium";
            Horns.Build(b, new HornSpec
            {
                Name = "horn", HeadBone = head, Base = new Vec3(0, 4.6, 3.8),
                Size = 5, Thickness = 0.55, Kind = HornKind.Curved, Material = MaterialSlot.Accent
            });
            if (withEye)
            {
                b.Feature(new FeatureDef
                {
                    Kind = FeatureKind.Eye, Bone = head,
                    LocalPosition = new Vec3(0.2, 3.9, 2.4), LocalNormal = Vec3.UnitZ,
                    Size = 3, Side = 1, Group = headGroup
                });
            }
            return b.Build(1);
        }

        private static double MinDistanceToAppendageGroup(CreatureAnatomy anatomy,
            int groupIndex, Vec3 point, Vec3 offset)
        {
            double nearest = double.PositiveInfinity;
            foreach (PrimitiveDef segment in anatomy.Primitives)
            {
                if (segment.Group != groupIndex || segment.Kind != PrimKind.RoundCone ||
                    segment.BoneA < 0 || segment.BoneA >= anatomy.Skeleton.Count ||
                    segment.BoneB < 0 || segment.BoneB >= anatomy.Skeleton.Count) continue;
                Vec3 a = anatomy.Skeleton.RestWorld(segment.BoneA).Point(segment.LocalA) + offset;
                Vec3 b = anatomy.Skeleton.RestWorld(segment.BoneB).Point(segment.LocalB) + offset;
                Vec3 ab = b - a;
                double t = ab.LengthSquared > 1e-12
                    ? Math.Max(0.0, Math.Min(1.0, Vec3.Dot(point - a, ab) / ab.LengthSquared)) : 0.0;
                nearest = Math.Min(nearest, Vec3.Distance(point, a + ab * t));
            }
            return nearest;
        }

        private static void PreviewArthropodChainSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("arthropod", 117);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int body = b.Group("body", 0.8);
            b.Ellipsoid(body, root, new Vec3(0, 6, 0),
                new Vec3(4, 3, 4), MaterialSlot.Primary);
            int knee = b.Bone("knee", root, new Vec3(2, 4, 3), Quat.Identity, 0, BoneRole.LegLower);
            int ankle = b.Bone("ankle", knee, new Vec3(2, -2, 3), Quat.Identity, 0, BoneRole.LegMeta);
            int foot = b.Bone("foot", ankle, new Vec3(0, -2, 3), Quat.Identity, 0, BoneRole.Foot);
            int group = b.Group("leg0R", 0.4);
            b.Cone(group, root, new Vec3(0, 5, 3), 0.5, knee, Vec3.Zero, 0.45,
                MaterialSlot.Primary).Tag = "leg0R.upper";
            b.Cone(group, knee, Vec3.Zero, 0.45, ankle, Vec3.Zero, 0.38,
                MaterialSlot.Primary).Tag = "leg0R.lower";
            b.Cone(group, ankle, Vec3.Zero, 0.38, foot, Vec3.Zero, 0.32,
                MaterialSlot.Primary).Tag = "leg0R.distal";
            var anatomy = b.Build(1);
            var chains = new PreviewSegmentChainFit(anatomy);
            ctx.True(chains.Chains.Count == 1 && chains.PrimitiveIndices.Count == 3,
                "arthropod leg must retain all three semantically linked parts");
            var chain = chains.Chains[0];
            for (int i = 1; i < chain.Segments.Count; i++)
                ctx.True(Vec3.Distance(chain.Segments[i - 1].End, chain.Segments[i].Start) < 1e-9,
                    "successive arthropod limb pieces must share a single continuous joint");
            ctx.True(!ImplicitBodyMesher.ShouldUnify(anatomy, anatomy.Primitives.Last()),
                "thin distal arthropod segment must bypass coarse global SDF");
            string obj = ObjMeshExporter.Export(anatomy, 8, 4).ObjText;
            ctx.True(obj.Contains("o arthropod_leg_", StringComparison.Ordinal),
                "3D exporter should emit continuous arthropod leg tubes");
        }

        private static void PreviewDentalLipFitSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 113);
            int head = b.Bone("head", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Head);
            int body = b.Group("head", 0.6);
            b.Ellipsoid(body, head, Vec3.Zero, new Vec3(4, 4, 4), MaterialSlot.Primary)
                .Tag = "head.cranium";
            b.Cone(body, head, new Vec3(2, 0, 0), 3, head,
                new Vec3(8, 0, 0), 2.5, MaterialSlot.Primary).Tag = "head.snout";
            int teethGroup = b.Group("head.teeth", 0.4);
            var tooth = b.Cone(teethGroup, head, new Vec3(6, -1.4, 1.3), 0.55,
                head, new Vec3(6.2, -5.0, 1.3), 0.3, MaterialSlot.Teeth);
            var a = b.Build(1);
            ctx.True(PreviewDentalFit.Applies(a, tooth), "upper tooth should use the lip fitter");
            ctx.True(PreviewDentalFit.TryFit(a, tooth, out PrimitiveDef fitted),
                "lip fitting should preserve a readable upper tooth");
            ctx.True(fitted.LocalA.Y > tooth.LocalA.Y,
                "3D tooth root should not be pushed below the authored upper-lip line");
            ctx.True((fitted.LocalB - fitted.LocalA).Length <
                (tooth.LocalB - tooth.LocalA).Length,
                "the closed-mouth static preview must not export the entire exposed 2D fang");
            ctx.True(Math.Abs(fitted.LocalA.Z) <= 3 &&
                fitted.LocalB.Y < fitted.LocalA.Y,
                "upper teeth must remain inside the muzzle width and point down");
            ctx.True(tooth.LocalA.Y == -1.4, "3D lip fitting cannot mutate 2D tooth genes");
        }

        private static void PreviewClawToeFitSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 114);
            int foot = b.Bone("foot", -1, new Vec3(0, 1.0, 0),
                Quat.Identity, 0, BoneRole.Foot, 1);
            int group = b.Group("legR", 0.5);
            var paw = b.Ellipsoid(group, foot, new Vec3(0, 0.10, 0),
                new Vec3(2, 1, 1), MaterialSlot.Primary);
            paw.Tag = "legR.foot";
            var claw = b.Cone(group, foot, new Vec3(4, 0.4, 0.3), 0.4,
                foot, new Vec3(6, -0.6, 0.4), 0.3, MaterialSlot.Accent);
            claw.Tag = "legR.claw";
            var a = b.Build(1);
            ctx.True(PreviewClawFit.Applies(claw), "toe should use the semantic claw fitter");
            ctx.True(PreviewClawFit.TryFit(a, claw, out PrimitiveDef fixedClaw),
                "a toe nail should remain visible on the final foot");
            Vec3 relative = fixedClaw.LocalA - paw.LocalA;
            double q = relative.X * relative.X / (paw.Radii.X * paw.Radii.X) +
                relative.Y * relative.Y / (paw.Radii.Y * paw.Radii.Y) +
                relative.Z * relative.Z / (paw.Radii.Z * paw.Radii.Z);
            ctx.True(q < 1, "claw base must overlap the same-bone paw instead of floating");
            ctx.True(fixedClaw.LocalA.X < claw.LocalA.X &&
                fixedClaw.LocalB.X > fixedClaw.LocalA.X,
                "claw should connect to and point forward from the paw");
            ctx.True(a.Skeleton.RestWorld(foot).Point(fixedClaw.LocalB).Y >= 0.33,
                "toe tip must not poke beneath the preview platform");
            ctx.True(claw.LocalA.X == 4, "toe fitting cannot mutate original anatomy");
        }

        private static void PreviewFixedUserSeedsSmoke(TestContext ctx, CreatureRegistry reg)
        {
            // Reproduce both body/equipment seed pairs shown in the user's screenshots.
            var family = reg.Families.First(f => f.Id == "biped");
            var cases = new (ulong BodySeed, ulong GearSeed)[]
            {
                (12345UL, 54321UL),
                (16114805862360663785UL, 3668598035714744795UL),
            };
            foreach (var testCase in cases)
            {
                var body = GenomeFactory.Sample(family, testCase.BodySeed);
                var colours = NativeGearRandomizer.ApplyEquipmentColors(body, family,
                    StableHash.Derive(testCase.GearSeed, "equipment.colors"));
                var equipped = NativeGearRandomizer.ApplyNativeGearGenes(colours, family,
                    testCase.GearSeed, EquipmentSeedSet.FromMaster(testCase.GearSeed));
                var anatomy = family.BuildAnatomy(equipped, reg);
                string result = ObjMeshExporter.Export(anatomy, 8, 4).ObjText;
                string description = $"body {testCase.BodySeed} / equipment {testCase.GearSeed}";
                ctx.True(result.Contains("o organic_body_", StringComparison.Ordinal),
                    description + ": fixed seeds should produce continuous skin");
                ctx.True(!result.Contains("NaN", StringComparison.Ordinal) &&
                    !result.Contains("Infinity", StringComparison.Ordinal),
                    description + ": fixed seeds should not export nonfinite geometry");
                ctx.True(result == ObjMeshExporter.Export(anatomy, 8, 4).ObjText,
                    description + ": OBJ export must be deterministic");
            }
        }

        private static void PreviewAttachmentSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 100);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int hand = b.Bone("hand", root, Vec3.Zero, Quat.Identity, 0, BoneRole.Hand, 1);
            int g = b.Group("armR", 0.4);
            b.Ellipsoid(g, hand, Vec3.Zero, new Vec3(2, 2, 2), MaterialSlot.Primary).Tag = "armR.palm";
            var near = b.Cone(g, hand, new Vec3(2.4, 0, 0), 0.4,
                hand, new Vec3(3.4, 0, 0), 0.3, MaterialSlot.Accent);
            near.Tag = "armR.claw";
            var far = b.Cone(g, hand, new Vec3(20, 0, 0), 0.4,
                hand, new Vec3(21, 0, 0), 0.3, MaterialSlot.Accent);
            far.Tag = "armR.claw";
            var a = b.Build(1);
            ctx.True(PreviewAttachmentRepair.TryRepair(a, near, out PrimitiveDef corrected),
                "nearby claw should remain attached");
            ctx.True(corrected.LocalA.X < near.LocalA.X && corrected.LocalB.X < near.LocalB.X,
                "claw should be projected back into the host shell");
            ctx.True(!PreviewAttachmentRepair.TryRepair(a, far, out _),
                "faraway microscopic claw must be suppressed");
            var empty = new AnatomyBuilder("biped", 111);
            int emptyBone = empty.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int emptyGroup = empty.Group("legR", 0.2);
            var unsupported = empty.Cone(emptyGroup, emptyBone, new Vec3(0, 3, 0), 0.4,
                emptyBone, new Vec3(0, 4, 0), 0.2, MaterialSlot.Accent);
            unsupported.Tag = "legR.claw";
            ctx.True(!PreviewAttachmentRepair.TryRepair(empty.Build(1), unsupported, out _),
                "tiny claw with no body host must not float in the exported preview");
            ctx.True(near.LocalA.X == 2.4, "export repair must not mutate source anatomy");
            string obj = ObjMeshExporter.Export(a, 8, 4).ObjText;
            ctx.True(!obj.Contains("o primitive_2", StringComparison.Ordinal),
                "detached claw must not appear in final OBJ");
        }

        private static void PreviewAncestorAttachmentSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 104);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int foot = b.Bone("foot", root, new Vec3(2, 0, 0), Quat.Identity, 0, BoneRole.Foot, 1);
            int leg = b.Group("legR", 0.8);
            b.Cone(leg, root, Vec3.Zero, 0.9, foot, Vec3.Zero, 0.8, MaterialSlot.Primary).Tag = "legR.distal";
            var claw = b.Cone(leg, foot, new Vec3(0, 1.2, 0), 0.35,
                foot, new Vec3(0, 2.0, 0), 0.2, MaterialSlot.Accent);
            claw.Tag = "legR.claw";
            var a = b.Build(1);
            ctx.True(PreviewAttachmentRepair.TryRepair(a, claw, out PrimitiveDef corrected),
                "a claw near its parent limb segment should be retained");
            ctx.True(corrected.LocalA.Y < claw.LocalA.Y && corrected.LocalB.Y < claw.LocalB.Y,
                "the claw base and tip should move together when its host is on an ancestor bone");
            ctx.True(claw.LocalA.Y == 1.2, "ancestor repair must not mutate source anatomy");
        }

        private static void PreviewSkinUnionSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("skin_union_test", 105);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int body = b.Group("body", 1.5);
            b.Ellipsoid(body, root, Vec3.Zero, new Vec3(5, 5, 4), MaterialSlot.Primary).Tag = "body.core";
            var softAccent = b.Ellipsoid(body, root, new Vec3(3.8, 0, 0),
                new Vec3(1.8, 1.5, 1.5), MaterialSlot.Accent);
            softAccent.Tag = "head.snout";
            var facetedSkin = b.Ellipsoid(body, root, new Vec3(-3.8, 0, 0),
                new Vec3(1.8, 1.5, 1.5), MaterialSlot.Secondary);
            facetedSkin.Flags |= PrimFlags.Faceted;
            var a = b.Build(1);
            ctx.True(ImplicitBodyMesher.ShouldUnify(a, softAccent),
                "soft accent muzzle geometry in a core group should join the outer skin");
            ctx.True(ImplicitBodyMesher.ShouldUnify(a, facetedSkin),
                "2D faceted shading flags must not force a separate overlapping 3D shell");
            var mesh = ImplicitBodyMesher.Build(a);
            ctx.True(mesh.PrimitiveIndices.Contains(1) && mesh.PrimitiveIndices.Contains(2),
                "the fused skin should own both soft accent and faceted core primitives");
        }

        private static void PreviewStandaloneAppendageSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("appendage_union_test", 108);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int body = b.Group("body", 0.7);
            b.Ellipsoid(body, root, Vec3.Zero, new Vec3(3, 3, 3), MaterialSlot.Primary);
            int hornGroup = b.Group("horn.left", 0.4);
            var primaryHorn = b.Ellipsoid(hornGroup, root, new Vec3(0, 3, 0),
                new Vec3(0.6, 2, 0.6), MaterialSlot.Primary);
            int tentacleGroup = b.Group("tentacle.0", 0.4);
            var secondaryTentacle = b.Ellipsoid(tentacleGroup, root, new Vec3(0, -3, 0),
                new Vec3(0.7, 2, 0.7), MaterialSlot.Secondary);
            var anatomy = b.Build(1);
            ctx.True(!ImplicitBodyMesher.ShouldUnify(anatomy, primaryHorn) &&
                !ImplicitBodyMesher.ShouldUnify(anatomy, secondaryTentacle),
                "semantic appendage groups must stay separate even when they use core body colours");
            var mesh = ImplicitBodyMesher.Build(anatomy);
            ctx.True(mesh.PrimitiveIndices.SetEquals(new[] { 0 }),
                "global SDF body union should not consume semantically independent appendages");
        }

        private static void PreviewEyeStalkSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("arthropod", 109);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int stalk = b.Bone("stalk", root, new Vec3(0, 2, 0), Quat.Identity, 0, BoneRole.Antenna, 1);
            int g = b.Group("eyestalk.R", 0.4);
            b.Cone(g, root, Vec3.Zero, 0.7, stalk, Vec3.Zero, 0.55, MaterialSlot.Primary);
            var eye = new FeatureDef
            {
                Kind = FeatureKind.Eye,
                Bone = stalk,
                LocalPosition = new Vec3(0.2, 0, 0),
                LocalNormal = Vec3.UnitX,
                Size = 2.2,
                Style = EyeStyle.Bead,
                Side = 1,
                Group = -1
            };
            b.Feature(eye);
            var a = b.Build(1);
            var fit = PreviewFeatureFit.Evaluate(a, eye);
            ctx.True(fit.NeedsBacking, "bare eye-stalk tip must generate a fitted 3D bulb");
            ctx.True(fit.Size > 0 && fit.Size <= eye.Size, "eye bulb should be within the authored stamp scale");
            string obj = ObjMeshExporter.Export(a, 8, 4).ObjText;
            ctx.True(obj.Contains("o feature_0_backing", StringComparison.Ordinal),
                "OBJ preview should contain a supporting eye bulb at the end of the stalk");

            var b2 = new AnatomyBuilder("biped", 110);
            int head = b2.Bone("head", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Head);
            int headGroup = b2.Group("head", 0.7);
            b2.Ellipsoid(headGroup, head, Vec3.Zero, new Vec3(3, 3, 3), MaterialSlot.Primary)
                .Tag = "head.cranium";
            var normalEye = new FeatureDef
            {
                Kind = FeatureKind.Eye,
                Bone = head,
                LocalPosition = new Vec3(0, 0, 3),
                LocalNormal = Vec3.UnitZ,
                Size = 5,
                Group = headGroup
            };
            b2.Feature(normalEye);
            var normalFit = PreviewFeatureFit.Evaluate(b2.Build(1), normalEye);
            ctx.True(!normalFit.NeedsBacking && normalFit.Size <= 2.4,
                "eyes on an existing skull should scale to the skull instead of growing another bulb");
        }

        private static void PreviewEyeSurfaceProjectionSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 112);
            int head = b.Bone("head", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Head);
            int headGroup = b.Group("head", 0.7);
            b.Ellipsoid(headGroup, head, Vec3.Zero, new Vec3(3, 3, 3), MaterialSlot.Primary)
                .Tag = "head.cranium";
            var eye = new FeatureDef
            {
                Kind = FeatureKind.Eye, Bone = head,
                LocalPosition = new Vec3(0, 0, 3.55),
                LocalNormal = Vec3.UnitZ, Size = 2,
                Style = EyeStyle.Round, Group = headGroup
            };
            b.Feature(eye);
            var a = b.Build(1);
            var fitted = PreviewFeatureFit.Evaluate(a, eye);
            ctx.True(fitted.WorldOffset.Z < -0.45 && fitted.WorldOffset.Z > -0.7,
                "a slightly floating eyeball should be moved back onto the cranium");
            ctx.True(!fitted.NeedsBacking, "an eye on a cranium must not get an extra eyeball");
            ctx.True(eye.LocalPosition.Z == 3.55, "export-only eye adjustment cannot mutate 2D data");
            eye.LocalPosition = new Vec3(0, 0, 6);
            var far = PreviewFeatureFit.Evaluate(a, eye);
            ctx.True(far.WorldOffset.LengthSquared < 1e-12,
                "a far-away eye must not be snapped across empty space to an unrelated surface");
        }

        private static void PreviewHeadgearClearanceSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 106);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            var spec = new HeadSpec
            {
                Name = "head",
                ParentBone = root,
                Position = new Vec3(0, 10, 0),
                Forward = Vec3.UnitX,
                SkullLength = 8,
                SkullHeight = 6,
                SkullWidth = 5,
                SkullOffset = 0.3,
                SnoutLength = 2,
                SnoutRadius = 1.5,
                JawLength = 0,
                NoseSize = 0,
                EyeSize = 2,
                Teeth = TeethKind.None,
            };
            HeadResult head = Heads.Build(b, spec);
            Gear.HeadGear(b, head, HeadGearKind.Helmet, -1, 1.0);
            var anatomy = b.Build(1);
            PrimitiveDef? helmet = null;
            foreach (PrimitiveDef p in anatomy.Primitives)
                if (p.Tag == "gear.helmet") { helmet = p; break; }
            ctx.True(helmet != null, "helmet should be emitted");
            ctx.True(helmet!.LocalA.Y - helmet.Radii.Y >
                head.SkullCenterLocal.Y + head.SkullRadii.Y * 0.15,
                "helmet lower edge should stay above the eye band rather than covering the face");
        }

        private static void PreviewEyeStampScaleSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 107);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            var spec = new HeadSpec
            {
                Name = "head",
                ParentBone = root,
                Position = new Vec3(0, 10, 0),
                Forward = Vec3.UnitX,
                SkullLength = 8,
                SkullHeight = 6,
                SkullWidth = 5,
                SkullOffset = 0.3,
                SnoutLength = 2,
                SnoutRadius = 1.5,
                JawLength = 0,
                NoseSize = 0,
                EyeSize = 5,
                EyeStyle = EyeStyle.Round,
                Teeth = TeethKind.None,
            };
            Heads.Build(b, spec);
            var anatomy = b.Build(1);
            string obj = ObjMeshExporter.Export(anatomy, 8, 4, unitsPerPixel: 0.01).ObjText;
            double socketHeight = ObjObjectExtent(obj, "feature_0_socket", 1);
            ctx.True(socketHeight > 0 && socketHeight <= 0.024,
                "3D eye socket height should follow nominal eye-stamp size and fit within the cranium");
        }

        private static double ObjObjectExtent(string objText, string objectName, int axis)
        {
            bool capture = false;
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (string raw in objText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    if (capture) break;
                    capture = string.Equals(line, "o " + objectName, StringComparison.Ordinal);
                    continue;
                }
                if (!capture || !line.StartsWith("v ", StringComparison.Ordinal)) continue;
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double value = double.Parse(parts[axis + 1], CultureInfo.InvariantCulture);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            return DMath.IsFinite(min) && DMath.IsFinite(max) ? max - min : 0;
        }

        private static void PreviewSocketSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 101);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int lower = b.Bone("forearm", root, new Vec3(0, 4, 0), Quat.Identity, 0, BoneRole.ArmLower, 1);
            int hand = b.Bone("hand", lower, new Vec3(0, -2, 0), Quat.Identity, 0, BoneRole.Hand, 1);
            int g = b.Group("armR", 0.4);
            b.Ellipsoid(g, hand, new Vec3(-0.3, 0, 0), new Vec3(1, 1, 1),
                MaterialSlot.Primary).Tag = "armR.palm";
            int weapon = b.Group("weapon", 0.3);
            var shaft = b.Cone(weapon, hand, new Vec3(0, -8, 0), 0.45,
                hand, new Vec3(0, 8, 0), 0.45, MaterialSlot.Accent);
            shaft.Tag = "weapon.spear";
            var a = b.Build(1);
            var sockets = new PreviewEquipmentSockets(a);
            var rest = a.Skeleton.RestWorld(hand);
            Vec3 oldBase = rest.Point(shaft.LocalA), oldTip = rest.Point(shaft.LocalB);
            Vec3 newBase = sockets.Transform(a, shaft, oldBase);
            Vec3 newTip = sockets.Transform(a, shaft, oldTip);
            ctx.True((newTip - newBase).Length < (oldTip - oldBase).Length,
                "weapon socket should reduce an excessively low-hanging long shaft");
            ctx.True(newTip.Y > newBase.Y, "weapon should point upright in rest-pose preview");
            double radiusScale = sockets.RadiusScale(a, shaft);
            ctx.True(radiusScale > 0 && radiusScale <= 1,
                "weapon socket must use a valid positive uniform scale");
            double lowestAfterFit = Math.Min(newBase.Y - shaft.RadiusA * radiusScale,
                newTip.Y - shaft.RadiusB * radiusScale);
            ctx.True(lowestAfterFit >= 0.19,
                "long weapons must be scaled to stay above the static preview floor");
            ctx.True(shaft.LocalA.Y == -8, "socket adjustment must not change the source weapon");
        }

        private static void PreviewGarmentFitSmoke(TestContext ctx)
        {
            var b = new AnatomyBuilder("biped", 102);
            int root = b.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int g = b.Group("body", 0.8);
            b.Ellipsoid(g, root, Vec3.Zero, new Vec3(3, 2, 2), MaterialSlot.Primary).Tag = "hips";
            b.Ellipsoid(g, root, new Vec3(3, 0, 0), new Vec3(3, 8, 8), MaterialSlot.Primary).Tag = "chest";
            int skirtGroup = b.Group("gear.skirt", 0.4);
            b.Ellipsoid(skirtGroup, root, new Vec3(-2, 0, 0),
                new Vec3(3, 3, 3), MaterialSlot.Cloth);
            var a = b.Build(1);
            var garments = ImplicitBodyMesher.BuildClothingGroups(a);
            var fit = PreviewGarmentFit.Evaluate(a, garments);
            ctx.True(fit.ReplacedGroupIndices.Contains(skirtGroup), "narrow-hip skirt must downgrade");
            ctx.True(fit.FallbackHem != null && fit.FallbackHem.Material == MaterialSlot.Cloth,
                "downgrade must generate one fitted cloth hem");
            ctx.True(ObjMeshExporter.Export(a, 8, 4).ObjText.Contains("o garment_fitted_short_hem", StringComparison.Ordinal),
                "export must emit the fitted garment instead of hanging panel shells");
        }

        private static void PreviewMeshCleanerSmoke(TestContext ctx)
        {
            var mesh = new ImplicitBodyMesher.MeshData();
            mesh.Vertices.Add(new Vec3(0, 0, 0));
            mesh.Vertices.Add(new Vec3(3, 0, 0));
            mesh.Vertices.Add(new Vec3(0, 3, 0));
            mesh.Vertices.Add(new Vec3(20, 0, 0));
            mesh.Vertices.Add(new Vec3(20.1, 0, 0));
            mesh.Vertices.Add(new Vec3(20, 0.1, 0));
            for (int i = 0; i < 12; i++)
                mesh.Faces.Add(new ImplicitBodyMesher.Face(0, 1, 2, MaterialSlot.Primary));
            mesh.Faces.Add(new ImplicitBodyMesher.Face(3, 4, 5, MaterialSlot.Primary));
            ctx.True(PreviewMeshCleaner.RemoveTinyIslands(mesh) == 1,
                "tiny isolated mesh triangle should be removed");
            ctx.True(mesh.Faces.Count == 12, "primary mesh component must remain intact");
        }

        private static void ObjTwoSidedTriangleNormals(TestContext ctx)
        {
            var builder = new AnatomyBuilder("triangle_test", 1);
            int root = builder.Bone("root", -1, Vec3.Zero, Quat.Identity, 0, BoneRole.Root);
            int group = builder.Group("plate", 0);
            builder.Triangle(group, root, new Vec3(-1, 0, 0),
                root, new Vec3(1, 0, 0), root, new Vec3(0, 1, 0),
                MaterialSlot.Primary, flags: PrimFlags.TwoSided);
            var anatomy = builder.Build(1);
            var result = ObjMeshExporter.Export(anatomy, 8, 4);
            ctx.True(HasSeparatedTwoSidedTriangleVertices(result.ObjText),
                "two-sided triangle must export two independent vertex triplets and two faces");
        }

        private static bool HasSeparatedTwoSidedTriangleVertices(string objText)
        {
            int vertices = 0, faces = 0;
            bool inObject = false;
            foreach (string raw in objText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    if (inObject && vertices == 6 && faces == 2) return true;
                    inObject = true;
                    vertices = 0;
                    faces = 0;
                    continue;
                }
                if (!inObject) continue;
                if (line.StartsWith("v ", StringComparison.Ordinal)) vertices++;
                else if (line.StartsWith("f ", StringComparison.Ordinal)) faces++;
            }
            return inObject && vertices == 6 && faces == 2;
        }

        private static void EquipmentStyleSmoke(TestContext ctx, CreatureRegistry reg)
        {
            var family = reg.Families.First(f => f.Id == "biped");
            // Disable original fantasy gear so overlays don't collide in this smoke test.
            var g = GenomeFactory.Sample(family, 9701)
                .With("gear.weapon", 0)
                .With("gear.outfit", 0)
                .With("gear.helmet", 0)
                .With("gear.shield", 0)
                .With("gear.pauldrons", 0);
            var original = family.BuildAnatomy(g, reg);
            ulong originalHash = AnatomyHash(original);
            foreach (string style in new[] { "modern", "scifi", "wasteland" })
            {
                var first = EquipmentGenerator.Apply(original, style, 123456UL);
                var second = EquipmentGenerator.Apply(original, style, 123456UL);
                ctx.True(first.Primitives.Count > original.Primitives.Count, style + ": no added equipment");
                ctx.True(first.Primitives.Count == second.Primitives.Count, style + ": unstable shape count");
                ctx.True(AnatomyHash(first) == AnatomyHash(second), style + ": nondeterministic equipment geometry");
                ctx.True(ObjMeshExporter.Export(first, 8, 4).Triangles > 0, style + ": no 3D mesh");
                ctx.True(AnatomyHash(original) == originalHash, style + ": source anatomy mutated");
            }
            ctx.True(object.ReferenceEquals(original, EquipmentGenerator.Apply(original, "fantasy", 123)),
                "fantasy pass-through should preserve original anatomy");
        }

        private static void EquipmentSlotIndependence(TestContext ctx, CreatureRegistry reg)
        {
            var family = reg.Families.First(f => f.Id == "biped");
            var genome = GenomeFactory.Sample(family, 7654321)
                .With("gear.weapon", 0).With("gear.outfit", 0).With("gear.helmet", 0)
                .With("gear.shield", 0).With("gear.pauldrons", 0);
            var original = family.BuildAnatomy(genome, reg);
            var seeds = EquipmentSeedSet.FromMaster(123456UL);
            var baseline = EquipmentGenerator.Apply(original, "modern", seeds);
            var weaponOnly = new EquipmentSeedSet(seeds.OutfitSeed,
                StableHash.Derive(seeds.WeaponSeed, "reroll.weapon"), seeds.HeadgearSeed, seeds.AccessorySeed);
            var rerolledWeapon = EquipmentGenerator.Apply(original, "modern", weaponOnly);

            var jacketA = baseline.Primitives.First(p => p.Tag == "equipment.modern.jacket");
            var jacketB = rerolledWeapon.Primitives.First(p => p.Tag == "equipment.modern.jacket");
            ctx.True(jacketA.Radii.Equals(jacketB.Radii), "weapon reroll changed outfit geometry");

            var propA = baseline.Primitives.First(p => p.Tag == "equipment.modern.prop.body");
            var propB = rerolledWeapon.Primitives.First(p => p.Tag == "equipment.modern.prop.body");
            ctx.True(!propA.Radii.Equals(propB.Radii), "weapon reroll did not change weapon geometry");

            var sameAgain = EquipmentGenerator.Apply(original, "modern", seeds);
            ctx.True(AnatomyHash(baseline) == AnatomyHash(sameAgain), "same per-slot seeds must be deterministic");
            ctx.True(AnatomyHash(original) == AnatomyHash(family.BuildAnatomy(genome, reg)),
                "equipment generation must not mutate source anatomy");
        }

        private static void NativeGearSlotIndependence(TestContext ctx, CreatureRegistry reg)
        {
            var family = reg.Families.First(f => f.Id == "biped");
            var body = GenomeFactory.Sample(family, 887766UL);
            const ulong masterSeed = 123456789UL;
            var seeds = EquipmentSeedSet.FromMaster(masterSeed);
            var coloredBody = NativeGearRandomizer.ApplyEquipmentColors(body, family,
                StableHash.Derive(masterSeed, "equipment.colors"));
            var baseline = NativeGearRandomizer.ApplyNativeGearGenes(coloredBody, family, masterSeed, seeds);

            string originalWeapon = GenomeFactory.Sample(family, seeds.WeaponSeed).GetChoice("gear.weapon");
            ulong alternateWeaponSeed = seeds.WeaponSeed;
            bool foundDifferentWeapon = false;
            for (int i = 1; i <= 128; i++)
            {
                ulong candidate = StableHash.Derive(masterSeed, "test.native.weapon", i);
                if (GenomeFactory.Sample(family, candidate).GetChoice("gear.weapon") == originalWeapon) continue;
                alternateWeaponSeed = candidate;
                foundDifferentWeapon = true;
                break;
            }
            ctx.True(foundDifferentWeapon, "test setup could not find a different deterministic weapon sample");

            var weaponOnly = new EquipmentSeedSet(seeds.OutfitSeed, alternateWeaponSeed,
                seeds.HeadgearSeed, seeds.AccessorySeed);
            var changed = NativeGearRandomizer.ApplyNativeGearGenes(coloredBody, family, masterSeed, weaponOnly);
            ctx.Equal(baseline.Get("gear.outfit"), changed.Get("gear.outfit"), "weapon reroll changed outfit gene");
            ctx.Equal(baseline.Get("gear.helmet"), changed.Get("gear.helmet"), "weapon reroll changed headgear gene");
            ctx.Equal(baseline.Get("gear.shield"), changed.Get("gear.shield"), "weapon reroll changed shield gene");
            ctx.Equal(baseline.Get("gear.pauldrons"), changed.Get("gear.pauldrons"), "weapon reroll changed pauldron gene");
            ctx.Equal(baseline.Get("gear.size"), changed.Get("gear.size"), "slot reroll changed shared gear size");
            ctx.True(baseline.GetChoice("gear.weapon") != changed.GetChoice("gear.weapon"),
                "weapon reroll did not change native weapon gene");
            ctx.Equal(body.Get("form"), changed.Get("form"), "gear reroll changed body archetype");
            ctx.Equal(body.Get("head.size"), changed.Get("head.size"), "gear reroll changed body head size");
            ctx.Equal(coloredBody.Get("color.clothHue"), changed.Get("color.clothHue"), "weapon reroll changed gear colour");

            var otherBody = GenomeFactory.Sample(family, 887767UL);
            var otherColoredBody = NativeGearRandomizer.ApplyEquipmentColors(otherBody, family,
                StableHash.Derive(masterSeed, "equipment.colors"));
            var sameGearOnOtherBody = NativeGearRandomizer.ApplyNativeGearGenes(otherColoredBody, family, masterSeed, seeds);
            ctx.Equal(baseline.Get("gear.outfit"), sameGearOnOtherBody.Get("gear.outfit"), "body seed changed outfit choice");
            ctx.Equal(baseline.Get("gear.weapon"), sameGearOnOtherBody.Get("gear.weapon"), "body seed changed weapon choice");
            ctx.Equal(baseline.Get("gear.helmet"), sameGearOnOtherBody.Get("gear.helmet"), "body seed changed headgear choice");
            ctx.Equal(baseline.Get("color.clothHue"), sameGearOnOtherBody.Get("color.clothHue"), "body seed changed clothing colour");
        }

        private static void DeterministicMath(TestContext ctx)
        {
            // Bit exact reference values of the DMath kernels. They must be identical on every OS/CPU.
            ctx.Equal(0x3FEAED548F090CEEL, BitConverter.DoubleToInt64Bits(DMath.Sin(1.0)), "sin(1)");
            ctx.Equal(0x3FE14A280FB5068CL, BitConverter.DoubleToInt64Bits(DMath.Cos(1.0)), "cos(1)");
            // DMath.Exp(1) is one ulp above the correctly rounded value; determinism, not rounding, is what matters
            ctx.Equal(0x4005BF0A8B14576AL, BitConverter.DoubleToInt64Bits(DMath.Exp(1.0)), "exp(1)");
            ctx.Equal(0x3FE62E42FEFA39EFL, BitConverter.DoubleToInt64Bits(DMath.Log(2.0)), "log(2)");
            ctx.Equal(0x3FE921FB54442D18L, BitConverter.DoubleToInt64Bits(DMath.Atan2(1.0, 1.0)), "atan2(1,1)");
            double maxErr = 0;
            for (int i = -2000; i <= 2000; i++)
            {
                double x = i * 0.0137;
                maxErr = Math.Max(maxErr, Math.Abs(DMath.Sin(x) - Math.Sin(x)));
                maxErr = Math.Max(maxErr, Math.Abs(DMath.Cos(x) - Math.Cos(x)));
            }
            ctx.True(maxErr < 1e-15, $"sin/cos accuracy {maxErr}");
            ctx.Metric("maxTrigError", maxErr);
        }

        private static void RandomGolden(TestContext ctx)
        {
            var rng = new Rng(42);
            var seq = new uint[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };
            ctx.Note("pcg32(42): " + string.Join(",", seq));
            ctx.Equal(Golden.Pcg42[0], seq[0], "pcg32[0]");
            ctx.Equal(Golden.Pcg42[1], seq[1], "pcg32[1]");
            ctx.Equal(Golden.Pcg42[2], seq[2], "pcg32[2]");
            ctx.Equal(Golden.Pcg42[3], seq[3], "pcg32[3]");
            ctx.Equal(Golden.Fnv, StableHash.Fnv1a64("Procedural Pixel Creatures"), "fnv1a64");
            ctx.Equal(Golden.Derived, StableHash.Derive(123456789UL, "stream.anatomy"), "derive");
            // unbiased bounded ints stay in range
            var r2 = new Rng(7);
            for (int i = 0; i < 10000; i++)
            {
                int v = r2.NextInt(37);
                ctx.True(v >= 0 && v < 37, "NextInt range");
            }
        }

        // ------------------------------------------------------------------ genome

        private static void SamplingDeterminism(TestContext ctx, CreatureRegistry reg)
        {
            foreach (var f in reg.Families)
            {
                var a = GenomeFactory.Sample(f, 424242);
                var b = GenomeFactory.Sample(f, 424242);
                var c = GenomeFactory.Sample(f, 424243);
                ctx.True(a.ContentEquals(b), $"{f.Id}: same seed must give same genome");
                ctx.True(!a.ContentEquals(c), $"{f.Id}: different seed should differ");
                ctx.Equal(a.ContentHash, b.ContentHash, $"{f.Id}: content hash");
            }
        }

        private static void StreamIndependence(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            foreach (var f in reg.Families)
            {
                var g = GenomeFactory.Sample(f, 99);
                var colored = g.With("color.hue", (g.Get("color.hue") + 137) % 360).With("color.saturation", 1 - g.Get("color.saturation"));
                ctx.Equal(g.AnatomyKey, colored.AnatomyKey, $"{f.Id}: anatomy key must ignore colour genes");
                ctx.Equal(g.PatternKey, colored.PatternKey, $"{f.Id}: pattern key must ignore colour genes");
                ctx.True(g.ColorKey != colored.ColorKey, $"{f.Id}: colour key must change");
                var a1 = f.BuildAnatomy(g, reg);
                var a2 = f.BuildAnatomy(colored, reg);
                ctx.Equal(AnatomyHash(a1), AnatomyHash(a2), $"{f.Id}: anatomy geometry after colour change");
                var patterned = g.With("pattern.density", 1 - g.Get("pattern.density"));
                ctx.Equal(AnatomyHash(a1), AnatomyHash(f.BuildAnatomy(patterned, reg)), $"{f.Id}: anatomy after pattern change");
                var moved = g.With("motion.energy", 1 - g.Get("motion.energy"));
                ctx.Equal(g.AnatomyKey, moved.AnatomyKey, $"{f.Id}: anatomy key must ignore motion genes");
            }
        }

        public static ulong AnatomyHash(CreatureAnatomy a)
        {
            ulong h = 17;
            void D(double v) => h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(v));
            foreach (var b in a.Skeleton.Bones)
            {
                h = StableHash.Combine(h, (ulong)(b.Parent + 7));
                D(b.RestOffset.X); D(b.RestOffset.Y); D(b.RestOffset.Z);
                D(b.RestRotation.X); D(b.RestRotation.Y); D(b.RestRotation.Z); D(b.RestRotation.W);
            }
            foreach (var p in a.Primitives)
            {
                h = StableHash.Combine(h, (ulong)p.Kind * 31 + (ulong)p.Material);
                D(p.LocalA.X); D(p.LocalA.Y); D(p.LocalA.Z); D(p.LocalB.X); D(p.LocalB.Y); D(p.LocalB.Z);
                D(p.RadiusA); D(p.RadiusB); D(p.Radii.X); D(p.Radii.Y); D(p.Radii.Z);
            }
            foreach (var fe in a.Features) { D(fe.LocalPosition.X); D(fe.LocalPosition.Y); D(fe.LocalPosition.Z); D(fe.Size); }
            return h;
        }

        private static void JsonRoundTrip(TestContext ctx, CreatureRegistry reg)
        {
            var ser = new GenomeSerializer(reg);
            foreach (var f in reg.Families)
            {
                for (ulong s = 1; s <= 20; s++)
                {
                    var g = GenomeFactory.Sample(f, s * 7919);
                    string json = ser.Serialize(g);
                    var res = ser.Deserialize(json);
                    ctx.True(res.Ok, $"{f.Id}/{s}: reload failed: {res.Describe()}");
                    ctx.True(res.Warnings.Count == 0, $"{f.Id}/{s}: unexpected warnings: {res.Describe()}");
                    ctx.True(res.Genome!.ContentEquals(g), $"{f.Id}/{s}: genome changed by save/load");
                    ctx.Equal(g.Name, res.Genome.Name, "name");
                    // second round trip is byte identical
                    ctx.Equal(json, ser.Serialize(res.Genome), $"{f.Id}/{s}: serializer output stable");
                }
            }
        }

        private static void SerializerValidation(TestContext ctx, CreatureRegistry reg)
        {
            var ser = new GenomeSerializer(reg);
            var f = reg.Families[0];
            var g = GenomeFactory.Sample(f, 5);
            string good = ser.Serialize(g);

            void ExpectError(string json, string contains, string what)
            {
                var r = ser.Deserialize(json);
                ctx.True(!r.Ok, $"{what}: must fail");
                ctx.True(r.Errors.Any(e => e.Contains(contains, StringComparison.OrdinalIgnoreCase)), $"{what}: error should mention '{contains}', got: {r.Describe()}");
            }

            JsonObject Mut(Action<JsonObject> change)
            {
                var o = (JsonObject)JsonNode.Parse(good)!;
                change(o);
                return o;
            }

            ExpectError("{ not json", "Invalid JSON", "broken json");
            ExpectError("[1,2]", "does not contain a JSON object", "array root");
            ExpectError(Mut(o => o["format"] = "something").ToJsonString(), "file format", "wrong format");
            ExpectError(Mut(o => o.Remove("formatVersion")).ToJsonString(), "formatVersion", "missing version");
            ExpectError(Mut(o => o["formatVersion"] = 99).ToJsonString(), "format version 99", "newer format");
            ExpectError(Mut(o => o["generatorVersion"] = 42).ToJsonString(), "generator version 42", "newer generator");
            ExpectError(Mut(o => o["family"] = "dragons-from-mars").ToJsonString(), "Unknown body-plan family", "unknown family");
            ExpectError(Mut(o => o["seeds"]!["color"] = "-5").ToJsonString(), "Seed 'color'", "negative seed");
            ExpectError(Mut(o => o["seeds"]!.AsObject().Remove("motion")).ToJsonString(), "Seed 'motion'", "missing seed");
            ExpectError(Mut(o => o["genes"]!["size"] = "big").ToJsonString(), "Expected a number", "string for float");
            ExpectError(Mut(o => o["genes"]!["pattern.dark"] = 3).ToJsonString(), "Boolean", "number for bool");

            // recoverable problems become warnings
            var w1 = ser.Deserialize(Mut(o => o["genes"]!["size"] = 5.0).ToJsonString());
            ctx.True(w1.Ok && w1.Warnings.Any(x => x.Contains("clamped")), "out of range clamps with warning: " + w1.Describe());
            ctx.Near(1.0, w1.Genome!.Get("size"), 1e-9, "clamped size");
            var w2 = ser.Deserialize(Mut(o => o["genes"]!.AsObject().Remove("mass")).ToJsonString());
            ctx.True(w2.Ok && w2.Warnings.Any(x => x.Contains("'mass'")), "missing gene defaults with warning");
            var w3 = ser.Deserialize(Mut(o => o["genes"]!["legacy.thing"] = 1).ToJsonString());
            ctx.True(w3.Ok && w3.Warnings.Any(x => x.Contains("legacy.thing")), "unknown gene ignored with warning");
            var w4 = ser.Deserialize(Mut(o => o["genes"]!["color.scheme"] = "neon").ToJsonString());
            ctx.True(w4.Ok && w4.Warnings.Any(x => x.Contains("neon")), "unknown option falls back with warning");
        }

        private sealed class TestMigrationV0 : IGenomeMigration
        {
            public int FromVersion => 0;
            public string Description => "Test migration: 'traits' -> 'genes', 'species' -> 'family'";
            public void Apply(JsonObject root)
            {
                if (root["traits"] is JsonNode t) { root.Remove("traits"); root["genes"] = t; }
                if (root["species"] is JsonNode s) { root.Remove("species"); root["family"] = s; }
            }
        }

        private static void MigrationChain(TestContext ctx, CreatureRegistry reg)
        {
            var ser = new GenomeSerializer(reg);
            var f = reg.Families[0];
            var g = GenomeFactory.Sample(f, 77);
            var o = (JsonObject)JsonNode.Parse(ser.Serialize(g))!;
            // build a synthetic legacy (format 0) file
            o["formatVersion"] = 0;
            var genes = o["genes"]!;
            o.Remove("genes");
            o["traits"] = genes;
            var fam = o["family"]!;
            o.Remove("family");
            o["species"] = fam;
            string legacy = o.ToJsonString();
            var noMigration = ser.Deserialize(legacy);
            ctx.True(!noMigration.Ok && noMigration.Errors.Any(e => e.Contains("No migration")), "missing migration must be an explicit error");
            ser.RegisterMigration(new TestMigrationV0());
            var migrated = ser.Deserialize(legacy);
            ctx.True(migrated.Ok, "migrated load: " + migrated.Describe());
            ctx.True(migrated.Migrations.Count == 1, "one migration step recorded");
            ctx.True(migrated.Genome!.ContentEquals(g), "migration preserves the creature");
            // gene alias (renamed gene)
            var ser2 = new GenomeSerializer(reg);
            ser2.RegisterGeneAlias(f.Id, "bodySize", "size");
            var o2 = (JsonObject)JsonNode.Parse(ser2.Serialize(g))!;
            var size = o2["genes"]!["size"]!.DeepClone();
            o2["genes"]!.AsObject().Remove("size");
            o2["genes"]!["bodySize"] = size;
            var aliased = ser2.Deserialize(o2.ToJsonString());
            ctx.True(aliased.Ok && aliased.Genome!.ContentEquals(g), "gene alias migration: " + aliased.Describe());
        }

        // ------------------------------------------------------------------ genetics

        private static void MutationTests(TestContext ctx, CreatureRegistry reg)
        {
            foreach (var f in reg.Families)
            {
                var parent = GenomeFactory.Sample(f, 1234);
                var settings = new MutationSettings { Strength = 0.5 };
                var a = GenomeOps.Mutate(parent, settings, 55);
                var b = GenomeOps.Mutate(parent, settings, 55);
                ctx.True(a.ContentEquals(b), $"{f.Id}: mutation deterministic");
                ctx.True(!a.ContentEquals(parent), $"{f.Id}: mutation changes something");
                var locks = new GeneLocks();
                locks.SetGroup("palette", true);
                locks.SetGene("size", true);
                var c = GenomeOps.Mutate(parent, new MutationSettings { Strength = 1.0 }, 56, locks);
                foreach (var def in f.Schema.Genes)
                {
                    if (def.Group == "palette" || def.Id == "size")
                        ctx.Equal(parent.Get(def.Id), c.Get(def.Id), $"{f.Id}: locked gene {def.Id}");
                }
                ctx.Equal(parent.ColorSeed, c.ColorSeed, $"{f.Id}: locked colour stream seed");
                var only = GenomeOps.Mutate(parent, new MutationSettings { Strength = 1.0, Anatomy = false, Pattern = false, Motion = false }, 57);
                ctx.Equal(parent.AnatomyKey, only.AnatomyKey, $"{f.Id}: colour-only mutation keeps anatomy");
                // strength 0 is identity for genes
                var zero = GenomeOps.Mutate(parent, new MutationSettings { Strength = 0 }, 58);
                ctx.True(zero.CopyValues().SequenceEqual(parent.CopyValues()), $"{f.Id}: zero strength keeps genes");
            }
        }

        private static void CrossoverTests(TestContext ctx, CreatureRegistry reg)
        {
            var fams = reg.Families;
            var f = fams[0];
            var a = GenomeFactory.Sample(f, 1);
            var b = GenomeFactory.Sample(f, 2);
            var r1 = GenomeOps.Cross(a, b, 9, new MutationSettings { Strength = 0.1 });
            var r2 = GenomeOps.Cross(a, b, 9, new MutationSettings { Strength = 0.1 });
            ctx.True(r1.Child.ContentEquals(r2.Child), "crossover deterministic");
            ctx.True(r1.Provenance.Count > 0, "provenance recorded");
            ctx.Equal("crossover", r1.Child.Lineage.Origin, "lineage origin");
            ctx.Equal(2, r1.Child.Lineage.ParentIds.Count, "two parents recorded");
            // without mutation every linked group comes from A, B or a blend of both
            var pure = GenomeOps.Cross(a, b, 10, new MutationSettings { Strength = 0 });
            foreach (var def in f.Schema.Genes)
            {
                double va = a.Get(def.Id), vb = b.Get(def.Id), vc = pure.Child.Get(def.Id);
                string src = pure.Provenance[def.Linkage];
                if (src == "A") ctx.Equal(va, vc, $"{def.Id} from A");
                else if (src == "B") ctx.Equal(vb, vc, $"{def.Id} from B");
                else if (def.Inheritance == InheritanceMode.Blend && def.Kind == GeneKind.Float)
                    ctx.True(vc >= Math.Min(va, vb) - 1e-6 && vc <= Math.Max(va, vb) + 1e-6, $"{def.Id} blended within parents");
            }
            if (fams.Count > 1)
            {
                var other = GenomeFactory.Sample(fams[1], 3);
                ctx.True(!GenomeOps.CanCross(a, other, out string reason) && reason.Length > 0, "different families are incompatible");
            }
        }

        private static void RegenerateTests(TestContext ctx, CreatureRegistry reg)
        {
            foreach (var f in reg.Families)
            {
                var g = GenomeFactory.Sample(f, 31337);
                var locks = new GeneLocks();
                string lockedGroup = f.Schema.Groups.First(x => x.Stream == GeneStream.Anatomy && x.Id != "body").Id;
                locks.SetGroup(lockedGroup, true);
                locks.SetGroup("palette", true);
                var n = GenomeFactory.RegenerateUnlocked(f, g, locks, 777);
                int changed = 0;
                foreach (var def in f.Schema.Genes)
                {
                    if (locks.IsLocked(def)) ctx.Equal(g.Get(def.Id), n.Get(def.Id), $"{f.Id}: locked {def.Id}");
                    else if (g.Get(def.Id) != n.Get(def.Id)) changed++;
                }
                ctx.True(changed > 3, $"{f.Id}: unlocked genes should change ({changed})");
                ctx.Equal(g.ColorSeed, n.ColorSeed, $"{f.Id}: colour seed kept with locked palette");
            }
        }

        // ------------------------------------------------------------------ anatomy & rendering

        private static void AnatomyValidity(TestContext ctx, CreatureRegistry reg, CreatureFactory factory, int seeds)
        {
            int total = 0, repairs = 0;
            int maxW = 0, maxH = 0;
            foreach (var f in reg.Families)
            {
                for (int i = 0; i < seeds; i++)
                {
                    var g = GenomeFactory.Sample(f, (ulong)(50000 + i * 131));
                    var m = factory.Build(g);
                    var a = m.Anatomy;
                    ctx.True(a.Primitives.Count >= 2, $"{f.Id}#{i}: primitives");
                    ctx.True(a.Skeleton.Count > 2, $"{f.Id}#{i}: bones");
                    foreach (var p in a.Primitives)
                    {
                        ctx.True(p.LocalA.IsFinite && p.LocalB.IsFinite && p.LocalC.IsFinite && p.Radii.IsFinite && DMath.IsFinite(p.RadiusA) && DMath.IsFinite(p.RadiusB), $"{f.Id}#{i}: finite primitive {p.Tag}");
                    }
                    ctx.True(a.Canvas.Width > 8 && a.Canvas.Width <= 320 && a.Canvas.Height > 8 && a.Canvas.Height <= 320, $"{f.Id}#{i}: canvas {a.Canvas}");
                    ctx.True(a.Metrics.BodyLength > 4 && DMath.IsFinite(a.Metrics.WalkSpeed) && a.Metrics.WalkSpeed > 0, $"{f.Id}#{i}: metrics");
                    maxW = Math.Max(maxW, a.Canvas.Width);
                    maxH = Math.Max(maxH, a.Canvas.Height);
                    repairs += a.Repairs.Count;
                    total++;
                }
            }
            ctx.Metric("anatomies", total);
            ctx.Metric("repairs", repairs);
            ctx.Metric("maxCanvasW", maxW);
            ctx.Metric("maxCanvasH", maxH);
        }

        public static ulong RenderHash(CreatureModel m, CreaturePose pose, double yaw)
        {
            var r = new CreatureRenderer();
            var c = m.Anatomy.Canvas;
            var f = new PixelFrame(c.Width, c.Height) { OriginX = c.OriginX, OriginY = c.OriginY };
            r.Render(m.Anatomy, m.Palette, m.Surface, pose, ViewSpec.Default(yaw), f);
            return f.ContentHash();
        }

        /// <summary>
        /// Values checked against the golden table: genome content hash of seed 2024, the rest pose
        /// image and an image after 1.5 s of simulated walking (motion + rendering). Keys: family id,
        /// "id:genome", "id:walk".
        /// </summary>
        public static IEnumerable<(string key, ulong value)> GoldenValues(CreatureRegistry reg, CreatureFactory factory)
        {
            foreach (var f in reg.Families)
            {
                var g = GenomeFactory.Sample(f, 2024);
                var m = factory.Build(g);
                yield return ($"{f.Id}:genome", g.ContentHash);
                yield return (f.Id, RenderHash(m, new CreaturePose(m.Anatomy.Skeleton), PixelCamera.DirectionYaw(7)));
                var motor = new CreatureMotor(m, reg);
                double yaw = PixelCamera.DirectionYaw(7);
                motor.Teleport(Vec3.Zero, yaw);
                motor.FacingOverride = yaw;
                motor.DesiredVelocity = new Vec3(DMath.Cos(yaw), 0, -DMath.Sin(yaw)) * m.Motion.WalkSpeed;
                for (int i = 0; i < 90; i++) motor.Step(FixedClock.DefaultStep);
                yield return ($"{f.Id}:walk", RenderHash(m, motor.Pose, motor.Yaw));
            }
        }

        private static void RenderDeterminism(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            foreach (var f in reg.Families)
            {
                var m = factory.Build(GenomeFactory.Sample(f, 2024));
                var pose = new CreaturePose(m.Anatomy.Skeleton);
                ulong h1 = RenderHash(m, pose, PixelCamera.DirectionYaw(7));
                ulong h2 = RenderHash(m, pose, PixelCamera.DirectionYaw(7));
                ctx.Equal(h1, h2, $"{f.Id}: identical render twice");
                // a fresh factory (cold caches) produces the same image
                var m2 = new CreatureFactory(reg).Build(GenomeFactory.Sample(f, 2024));
                ctx.Equal(h1, RenderHash(m2, new CreaturePose(m2.Anatomy.Skeleton), PixelCamera.DirectionYaw(7)), $"{f.Id}: cold cache render");
            }
            // golden values (produced on Linux x64): identical results on every platform prove that
            // sampling, motion and rasterization are bit-exact (DMath instead of platform libm)
            var values = GoldenValues(reg, new CreatureFactory(reg)).ToList();
            int checkedCount = 0;
            foreach (var (key, value) in values)
            {
                if (!Golden.Values.TryGetValue(key, out ulong golden)) { ctx.Note($"no golden value for {key}"); continue; }
                ctx.Equal(golden, value, $"golden {key}");
                checkedCount++;
            }
            ctx.Metric("goldenChecked", checkedCount);
            ctx.Note("values: " + string.Join(" ", values.Select(v => $"{v.key}={v.value:x16}")));
        }

        private static CreatureMotor Scenario(CreatureRegistry reg, CreatureModel m, Func<int, double> frameDeltas, double seconds, out long steps)
        {
            var motor = new CreatureMotor(m, reg);
            motor.Teleport(Vec3.Zero, 0);
            var clock = new FixedClock();
            double t = 0;
            int frame = 0;
            while (t < seconds)
            {
                double d = frameDeltas(frame++);
                t += d;
                int n = clock.Advance(d);
                for (int i = 0; i < n; i++)
                {
                    double st = motor.Time;
                    motor.DesiredVelocity = st > 0.3 && st < 1.6 ? new Vec3(m.Motion.WalkSpeed, 0, -m.Motion.WalkSpeed * 0.4) : Vec3.Zero;
                    if (Math.Abs(st - 1.9) < 1e-9 || (st >= 1.9 && st < 1.9 + FixedClock.DefaultStep)) motor.TriggerAction();
                    motor.Step(clock.StepSize);
                }
            }
            steps = motor.StepCount;
            return motor;
        }

        private static ulong PoseHash(CreaturePose p)
        {
            ulong h = 1;
            foreach (var x in p.Skeleton.World)
            {
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(x.Origin.X));
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(x.Origin.Y));
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(x.Origin.Z));
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(x.Basis.C0.X));
            }
            return h;
        }

        private static void MotionFramerateIndependence(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            foreach (var f in reg.Families)
            {
                var m = factory.Build(GenomeFactory.Sample(f, 8080));
                // exactly 3 seconds of simulation fed with 60 fps, 144 fps and irregular frame times
                var a = Scenario(reg, m, i => 1.0 / 60.0, 3.0, out long sa);
                var b = Scenario(reg, m, i => 1.0 / 144.0, 3.0, out long sb);
                var c = Scenario(reg, m, i => (i % 3 == 0 ? 0.031 : 0.007), 3.0, out long sc);
                long steps = Math.Min(sa, Math.Min(sb, sc));
                // run all to the same step count
                void Advance(CreatureMotor motor, long have)
                {
                    for (long k = have; k < steps + 3; k++)
                    {
                        motor.DesiredVelocity = Vec3.Zero;
                        motor.Step(FixedClock.DefaultStep);
                    }
                }
                Advance(a, sa); Advance(b, sb); Advance(c, sc);
                ctx.Equal(a.StepCount, b.StepCount, $"{f.Id}: step count");
                ctx.Equal(PoseHash(a.Pose), PoseHash(b.Pose), $"{f.Id}: 60 vs 144 fps pose");
                ctx.Equal(PoseHash(a.Pose), PoseHash(c.Pose), $"{f.Id}: 60 fps vs irregular frames pose");
            }
        }

        private static void FootSliding(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            double worst = 0;
            int checkedContacts = 0;
            foreach (var f in reg.Families)
            {
                for (int s = 0; s < 6; s++)
                {
                    var m = factory.Build(GenomeFactory.Sample(f, (ulong)(300 + s)));
                    if (m.Anatomy.Rig.Locomotion != LocomotionKind.Legged || m.Anatomy.Rig.Legs.Count == 0) continue;
                    var motor = new CreatureMotor(m, reg);
                    motor.Teleport(Vec3.Zero, 0);
                    var prev = new Dictionary<int, Vec3>();
                    for (int i = 0; i < 360; i++)
                    {
                        double t = i / 60.0;
                        // walk, run, turn and stop
                        double speed = t < 2 ? m.Motion.WalkSpeed : (t < 4 ? m.Motion.RunSpeed : 0);
                        double yaw = t < 3 ? 0 : 1.2;
                        motor.DesiredVelocity = new Vec3(DMath.Cos(yaw), 0, -DMath.Sin(yaw)) * speed;
                        motor.Step(FixedClock.DefaultStep);
                        var ctxM = motor.Context;
                        foreach (var c in motor.Pose.Contacts)
                        {
                            if (!c.Planted) { prev.Remove(c.Leg); continue; }
                            Vec3 w = ctxM.CreatureToWorld(c.Position);
                            if (prev.TryGetValue(c.Leg, out Vec3 pw))
                            {
                                double slide = new Vec2(w.X - pw.X, w.Z - pw.Z).Length;
                                worst = Math.Max(worst, slide);
                                checkedContacts++;
                            }
                            prev[c.Leg] = w;
                        }
                    }
                }
            }
            ctx.Metric("maxPlantedSlidePx", worst);
            ctx.Metric("contactSamples", checkedContacts);
            ctx.True(worst < 0.05, $"planted feet slid by {worst:0.000} px");
            // A shallow world-space slope must also meet each planted foot when the root moves.
            var slopeModel = factory.Build(GenomeFactory.Sample(reg.GetFamily("quadruped"), 26));
            var slopeMotor = new CreatureMotor(slopeModel, reg);
            slopeMotor.Context.GroundHeightAt = (x, z) => x * 0.04 + z * 0.03;
            slopeMotor.DesiredVelocity = new Vec3(slopeModel.Motion.WalkSpeed, 0, 0);
            double worstHeight = 0;
            int slopeContacts = 0;
            for (int i = 0; i < 180; i++)
            {
                slopeMotor.Step(FixedClock.DefaultStep);
                var context = slopeMotor.Context;
                foreach (var c in slopeMotor.Pose.Contacts.Where(c => c.Planted))
                {
                    var world = context.CreatureToWorld(c.Position);
                    double rootHeight = context.GroundHeightAt(context.Position.X, context.Position.Z);
                    double groundHeight = context.GroundHeightAt(world.X, world.Z);
                    worstHeight = Math.Max(worstHeight, Math.Abs(world.Y + rootHeight - groundHeight));
                    slopeContacts++;
                }
            }
            ctx.Metric("maxSlopeContactErrorPx", worstHeight);
            ctx.True(slopeContacts > 100 && worstHeight < 0.05, $"feet must follow a shallow slope: {worstHeight:0.000} px");
        }

        private static void CanvasSafety(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            var renderer = new CreatureRenderer();
            int overflow = 0, frames = 0;
            var problems = new List<string>();
            foreach (var f in reg.Families)
            {
                for (int s = 0; s < 8; s++)
                {
                    var m = factory.Build(GenomeFactory.Sample(f, (ulong)(900 + s * 17)));
                    var c = m.Anatomy.Canvas;
                    var frame = new PixelFrame(c.Width, c.Height) { OriginX = c.OriginX, OriginY = c.OriginY };
                    var motor = new CreatureMotor(m, reg);
                    for (int i = 0; i < 60 * 7; i++)
                    {
                        double t = i / 60.0;
                        int dir = (int)(t * 1.5) % 8;
                        motor.FacingOverride = PixelCamera.DirectionYaw(dir);
                        motor.DesiredVelocity = t > 1 && t < 2.5 ? new Vec3(DMath.Cos(dir * 0.785), 0, -DMath.Sin(dir * 0.785)) * m.Motion.RunSpeed : Vec3.Zero;
                        if (i == 60 * 3) motor.TriggerAction();
                        if (i == 60 * 4) motor.TriggerHit(new Vec3(1, 0, 0));
                        motor.Resting = t > 4.6 && t < 6.2;
                        motor.Step(FixedClock.DefaultStep);
                        if (i % 6 != 0) continue;
                        var st = renderer.Render(m.Anatomy, m.Palette, m.Surface, motor.Pose, ViewSpec.Default(motor.Yaw), frame);
                        frames++;
                        if (st.Overflow)
                        {
                            overflow++;
                            if (problems.Count < 6) problems.Add($"{f.Id}/{s} t={t:0.0} L{st.OverflowLeft} R{st.OverflowRight} T{st.OverflowTop} B{st.OverflowBottom}");
                        }
                    }
                }
            }
            ctx.Metric("framesChecked", frames);
            ctx.Metric("overflowFrames", overflow);
            ctx.True(overflow == 0, "parts left the canvas: " + string.Join("; ", problems));
        }

        /// <summary>
        /// Every gene at its extremes (all minimum, all maximum, alternating) and every option of every
        /// choice gene: the creature must build, stay finite, move through all states without leaving
        /// its canvas and render visible pixels. Extremes are legal genomes (the editor allows them).
        /// </summary>
        /// <summary>Variant 0: all genes minimal, 1: all maximal, 2+: alternating; choices cycle through all options.</summary>
        public static CreatureGenome ExtremeGenome(ICreatureFamily f, int variant)
        {
            var schema = f.Schema;
            var baseGenome = GenomeFactory.Sample(f, (ulong)(900 + variant));
            var values = baseGenome.CopyValues();
            for (int gi = 0; gi < schema.Count; gi++)
            {
                var def = schema.Genes[gi];
                bool high = (variant % 3) switch { 0 => false, 1 => true, _ => ((gi + variant) & 1) == 0 };
                switch (def.Kind)
                {
                    case GeneKind.Float:
                    case GeneKind.Int:
                        values[gi] = high ? def.Max : def.Min;
                        break;
                    case GeneKind.Bool:
                        values[gi] = high ? 1 : 0;
                        break;
                    case GeneKind.Choice:
                        values[gi] = (variant + gi) % def.Options.Count;
                        break;
                }
            }
            return baseGenome.WithValues(values).WithName($"Extrem {variant}");
        }

        private static void ExtremeGenes(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            var renderer = new CreatureRenderer();
            var problems = new List<string>();
            int cases = 0, frames = 0, maxCanvas = 0;
            foreach (var f in reg.Families)
            {
                var schema = f.Schema;
                int maxOptions = schema.Genes.Where(d => d.Kind == GeneKind.Choice).Select(d => d.Options.Count).DefaultIfEmpty(1).Max();
                int variants = Math.Max(6, maxOptions);
                for (int variant = 0; variant < variants; variant++)
                {
                    var genome = ExtremeGenome(f, variant);
                    string label = $"{f.Id}/v{variant}({genome.GetChoice("form")})";
                    cases++;
                    try
                    {
                        var m = factory.Build(genome);
                        var a = m.Anatomy;
                        bool finite = a.Primitives.All(p => p.LocalA.IsFinite && p.LocalB.IsFinite && p.LocalC.IsFinite && p.Radii.IsFinite && DMath.IsFinite(p.RadiusA) && DMath.IsFinite(p.RadiusB));
                        if (!finite) { problems.Add(label + ": non-finite primitive"); continue; }
                        // all-maximum genomes are allowed to be larger than sampled creatures, but bounded
                        maxCanvas = Math.Max(maxCanvas, Math.Max(a.Canvas.Width, a.Canvas.Height));
                        if (a.Canvas.Width > 400 || a.Canvas.Height > 400) { problems.Add($"{label}: canvas {a.Canvas.Width}x{a.Canvas.Height}"); continue; }
                        var c = a.Canvas;
                        var frame = new PixelFrame(c.Width, c.Height) { OriginX = c.OriginX, OriginY = c.OriginY };
                        var motor = new CreatureMotor(m, reg);
                        double yaw = PixelCamera.DirectionYaw(variant % 8);
                        motor.Teleport(Vec3.Zero, yaw);
                        bool visible = false;
                        string? issue = null;
                        for (int i = 0; i < 60 * 6 && issue == null; i++)
                        {
                            double t = i / 60.0;
                            int dir = (variant + (int)(t * 2)) % 8;
                            double dyaw = PixelCamera.DirectionYaw(dir);
                            motor.FacingOverride = dyaw;
                            double speed = t < 1 ? 0 : (t < 2 ? m.Motion.WalkSpeed : (t < 3 ? m.Motion.RunSpeed : 0));
                            motor.DesiredVelocity = new Vec3(DMath.Cos(dyaw), 0, -DMath.Sin(dyaw)) * speed;
                            if (i == 60 * 3) motor.TriggerAction();
                            if (i == 60 * 4) motor.TriggerHit(new Vec3(1, 0, 0));
                            motor.Resting = t > 4.4 && t < 5.6;
                            motor.Step(FixedClock.DefaultStep);
                            if (i % 10 != 0) continue;
                            var world = motor.Pose.Skeleton.World;
                            for (int b = 0; b < world.Length; b++)
                                if (!world[b].Origin.IsFinite) { issue = $"non-finite bone at t={t:0.00}"; break; }
                            if (issue != null) break;
                            var st = renderer.Render(a, m.Palette, m.Surface, motor.Pose, ViewSpec.Default(motor.Yaw), frame);
                            frames++;
                            if (st.Overflow) issue = $"left the canvas at t={t:0.00} (L{st.OverflowLeft} R{st.OverflowRight} T{st.OverflowTop} B{st.OverflowBottom})";
                            if (frame.ContentBounds(out _, out _, out _, out _)) visible = true;
                        }
                        if (issue == null && !visible) issue = "no visible pixels";
                        if (issue != null) problems.Add(label + ": " + issue);
                    }
                    catch (Exception e)
                    {
                        problems.Add($"{label}: {e.GetType().Name} {e.Message}");
                    }
                }
            }
            ctx.Metric("cases", cases);
            ctx.Metric("framesChecked", frames);
            ctx.Metric("maxCanvas", maxCanvas);
            ctx.True(problems.Count == 0, $"{problems.Count} of {cases} extreme genomes failed: " + string.Join("; ", problems.Take(12)));
        }

        /// <summary>
        /// Facing changes while standing still must turn the whole creature. Trail based bodies
        /// (snakes, worms) cannot pivot, their head has to crawl around and the body must follow.
        /// </summary>
        private static void TurnInPlace(TestContext ctx, CreatureRegistry reg, CreatureFactory factory)
        {
            var problems = new List<string>();
            int creatures = 0, trailBodies = 0;
            Vec3 want = PixelCamera.YawRotation(DMath.Pi).Mul(Vec3.UnitX);
            foreach (var f in reg.Families)
            {
                for (int s = 0; s < 4; s++)
                {
                    var m = factory.Build(GenomeFactory.Sample(f, (ulong)(500 + s * 13)));
                    var rig = m.Anatomy.Rig;
                    var motor = new CreatureMotor(m, reg);
                    motor.Teleport(Vec3.Zero, 0);
                    motor.FacingOverride = 0;
                    for (int i = 0; i < 60; i++) motor.Step(FixedClock.DefaultStep);
                    motor.FacingOverride = DMath.Pi;
                    for (int i = 0; i < 60 * 6; i++) motor.Step(FixedClock.DefaultStep);
                    creatures++;
                    if (Math.Abs(DMath.WrapAngle(motor.Yaw - DMath.Pi)) > 0.05)
                        problems.Add($"{f.Id}/{s}: yaw {motor.Yaw:0.00}");
                    if (rig.LocomotionModule == "serpentine" && rig.Body != null && rig.Body.Bones.Length > 2)
                    {
                        trailBodies++;
                        var yawM = PixelCamera.YawRotation(motor.Yaw);
                        var world = motor.Pose.Skeleton.World;
                        Vec3 head = yawM.Mul(world[rig.Body.Bones[0]].Origin);
                        Vec3 mid = yawM.Mul(world[rig.Body.Bones[rig.Body.Bones.Length / 2]].Origin);
                        Vec3 d = new Vec3(head.X - mid.X, 0, head.Z - mid.Z).NormalizedOr(Vec3.Zero);
                        double dot = Vec3.Dot(d, want);
                        if (dot < 0.6) problems.Add($"{f.Id}/{s}: body not turned (dot {dot:0.00})");
                    }
                }
            }
            ctx.Metric("creatures", creatures);
            ctx.Metric("trailBodies", trailBodies);
            ctx.True(trailBodies > 0, "no trail based body was checked");
            ctx.True(problems.Count == 0, string.Join("; ", problems));
        }
    }

    /// <summary>Golden reference values (produced on Linux x64; must match on every platform).</summary>
    public static class Golden
    {
        public static readonly uint[] Pcg42 = { 1210624746u, 4125136630u, 2074946571u, 3309376910u };
        public static readonly ulong Fnv = 114984965007321940UL;
        public static readonly ulong Derived = 16299427822044037963UL;
        /// <summary>Regenerate with: CoreHarness goldens (after an intended change of generation rules).</summary>
        public static readonly Dictionary<string, ulong> Values = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            ["quadruped:genome"] = 0xb347eafec5ed47edUL,
            ["quadruped"] = 0xc24f96a97be5a1f5UL,
            ["quadruped:walk"] = 0x37257c62ce5e5420UL,
            ["biped:genome"] = 0x5ef08293f551f95aUL,
            ["biped"] = 0x9d5f6ba037ea81f1UL,
            ["biped:walk"] = 0x5f13d3534d41b54fUL,
            ["reptile:genome"] = 0x53993ac7a73b23b3UL,
            ["reptile"] = 0x5207d3574a0a7b7bUL,
            ["reptile:walk"] = 0x56604dac7c734731UL,
            ["arthropod:genome"] = 0x17a527c0f16e660eUL,
            ["arthropod"] = 0x653189d69a32ee55UL,
            ["arthropod:walk"] = 0xa4df8330c029a0a1UL,
            ["winged:genome"] = 0x773625d175b173b9UL,
            ["winged"] = 0x1ddf0f2d627a3dc8UL,
            ["winged:walk"] = 0xd1ba207325472d3fUL,
            ["serpent:genome"] = 0xb6bd6dcb29822ee6UL,
            ["serpent"] = 0x76e6faaf990bc548UL,
            ["serpent:walk"] = 0x228b7ca9a57b9021UL,
            ["aquatic:genome"] = 0x44b268859a62d96fUL,
            ["aquatic"] = 0xd8ea4b1e8583f131UL,
            ["aquatic:walk"] = 0x52c411ea737c831eUL,
            ["amorphous:genome"] = 0x96fa889d3f873a1eUL,
            ["amorphous"] = 0x29545e9a6eb3f823UL,
            ["amorphous:walk"] = 0x62f57528386e2cbbUL,
            ["plantfolk:genome"] = 0x5e86703c03aa3251UL,
            ["plantfolk"] = 0x0365c2d8cc0518cbUL,
            ["plantfolk:walk"] = 0xacc0ea74aee3ec28UL,
        };
    }
}
