# Semantic topology mesh prototype

**Branch:** `experiment/semantic-topology-prototype`  
**Base:** `experiment/geometry-fit-prototype`  
**Status:** implementation added; build, automated tests, and visual acceptance are pending.

## Why this is a different experiment

This path deliberately does **not** use `ImplicitBodyMesher`, SDF union, marching tetrahedra, or `GeometryFitPrototype` as its final geometry source. It also does not turn the captured depth buffers into a voxel volume as `MultiviewReconstructor` does.

Instead, it creates an indexed surface for each original ellipsoid or tapered segment independently, preserving the source primitive's center, orientation, endpoints, and radii. It then allows only bounded vertex adjustments against the original 2D renderer's multi-view silhouette and front-depth observations. The topology is created before fitting and remains fixed during fitting.

## Current algorithm

1. Capture the original renderer at eight yaw directions by default.
2. Build one regular indexed surface directly from each eligible source ellipsoid or tapered segment, retaining its exact source transform and dimensions. Different source primitives are never combined into a bounding-volume envelope.
3. Fit these fixed triangle surfaces against multi-view constraints, with a strict total-displacement limit. If fitting lowers the silhouette score materially, discard the fit and restore the source surfaces.
4. Relax surface vertices using two kinds of evidence:
   - if a vertex projects outside the original silhouette, move its projection toward the nearest nearby visible pixel while preserving view depth;
   - if a vertex is in front of the original front-depth sample for the same semantic group, move it slightly backward.
5. Restore non-skin details and the original bone-anchored eye/nostril/spot geometry, then emit OBJ + MTL.
6. Report the old SDF skin's silhouette IoU, the new template's IoU before relaxation, and its IoU after relaxation. These metrics measure silhouette only, not anatomy or surface quality.

## Preview

Open `examples/preview_3d/CreaturePreview3D.tscn` with Godot 4.7.1 .NET and run the current scene with **F6**.

- Family: `biped`
- Body Seed: `12345`
- Equipment: off while testing
- Enable **语义拓扑网格（实验 / 不含装备）**

The original 2D reference is displayed beside the 3D preview. Rotate the model and inspect front, side, rear, and intermediate angles. Export is available through the existing OBJ + MTL button.

## Automated test

From the repository root:

```powershell
dotnet run -c Release --project tools/core_harness/CoreHarness.csproj -- test reports semantic_topology 1
```

The test checks deterministic output, non-empty indexed topology, restored facial features, absence of the old `organic_body_*` SDF mesh in the final output, finite silhouette scores, and finite OBJ coordinates. Passing this test is not visual acceptance.

## Known limitations — do not overstate this prototype

- The mesh still consists of separate overlapping primitive shells; this avoids the radial-envelope blobs but does not yet create a watertight, continuous skin. Intersections and seams can remain.
- The source primitive shells are not yet joined by shared anatomical boundary loops, so this is not a watertight, continuous whole-creature skin.
- The mouth interior and teeth are still restored as detail geometry; the new path does not yet create a true articulated jaw or a topology-aware oral cavity.
- Each topology face samples the original renderer's exact palette index and uses an unshaded `px_*` material. This preserves an approximate version of the pixel pattern at triangle resolution, but it is not a UV texture bake; fine patterns can be lost or smeared across large faces.
- There is no Skeleton3D skinning, animation, UV unwrap, or GLB pipeline.
- Multi-view silhouette IoU can improve while anatomy looks worse. Do not accept the method from IoU alone.

## Acceptance gate

Keep this as an experiment until all of the following are checked against the same Body Seed:

1. Compare the old SDF skin, the independent source-primitive surfaces before fitting, and the bounded fitted result in the same front/side/rear views.
2. Confirm that head, muzzle, mouth, eyes, feet, and limbs look structurally better—not merely that the silhouette score increases.
3. Check mesh boundaries, self-intersections, and part attachment in Blender or another mesh inspector.
4. If separate source primitives still look disconnected or faceted, do not merge them into a group-level radial envelope. The next valid step is explicit shared anatomical boundary loops or authored head/jaw topology.
5. Run the full repository check workflow before treating the branch as build-verified.

This experiment is intentionally isolated. It does not change the original 2D generator or replace the existing default preview/export path.
