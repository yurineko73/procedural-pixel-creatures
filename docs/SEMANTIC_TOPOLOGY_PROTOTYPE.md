# Semantic topology mesh prototype

**Branch:** `experiment/semantic-topology-prototype`  
**Base:** `experiment/geometry-fit-prototype`  
**Status:** implementation added; build, automated tests, and visual acceptance are pending.

## Why this is a different experiment

This path deliberately does **not** use `ImplicitBodyMesher`, SDF union, marching tetrahedra, or `GeometryFitPrototype` as its final geometry source. It also does not turn the captured depth buffers into a voxel volume as `MultiviewReconstructor` does.

Instead, it builds an indexed surface for each spatially connected component of a semantic anatomy group, then adjusts those existing vertices against the original 2D renderer's multi-view silhouette and front-depth observations. The topology is created before fitting and remains fixed during fitting.

## Current algorithm

1. Capture the original renderer at eight yaw directions by default.
2. Sample the surface of each eligible source ellipsoid or tapered segment. Samples are control data only; they are not emitted as the final skin mesh.
3. Split each existing anatomy group into spatially connected components using primitive sample bounds. This prevents left and right limbs in a shared legacy group from being wrapped into one surface.
4. Build a regular indexed radial surface for each component. It is a fixed triangle topology with welded vertices, not a voxel/SDF union.
5. Relax surface vertices using two kinds of evidence:
   - if a vertex projects outside the original silhouette, move its projection toward the nearest nearby visible pixel while preserving view depth;
   - if a vertex is in front of the original front-depth sample for the same semantic group, move it slightly backward.
6. Restore non-skin details and the original bone-anchored eye/nostril/spot geometry, then emit OBJ + MTL.
7. Report the old SDF skin's silhouette IoU, the new template's IoU before relaxation, and its IoU after relaxation. These metrics measure silhouette only, not anatomy or surface quality.

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

- A radial surface is star-shaped around its component center. It can fill concavities, soften sharp appendages, or make a snout/jaw region too round.
- Connected components are based on source primitive bounds. They are not yet authored anatomical templates with explicit shared boundary loops.
- Separate component surfaces can still overlap or show seams. This prototype does not yet provide a watertight, continuous whole-creature skin.
- The mouth interior and teeth are still restored as detail geometry; the new path does not yet create a true articulated jaw or a topology-aware oral cavity.
- Each topology face samples the original renderer's exact palette index and uses an unshaded `px_*` material. This preserves an approximate version of the pixel pattern at triangle resolution, but it is not a UV texture bake; fine patterns can be lost or smeared across large faces.
- There is no Skeleton3D skinning, animation, UV unwrap, or GLB pipeline.
- Multi-view silhouette IoU can improve while anatomy looks worse. Do not accept the method from IoU alone.

## Acceptance gate

Keep this as an experiment until all of the following are checked against the same Body Seed:

1. Compare the old SDF skin, the new radial topology before fitting, and the fitted topology in the same front/side/rear views.
2. Confirm that head, muzzle, mouth, eyes, feet, and limbs look structurally better—not merely that the silhouette score increases.
3. Check mesh boundaries, self-intersections, and part attachment in Blender or another mesh inspector.
4. If the radial topology loses the mouth/jaw concavity or produces a blob-like head, do not add more smoothing or reintroduce SDF union as the fix. Replace the radial head patch with an authored head/jaw template or stop this route.
5. Run the full repository check workflow before treating the branch as build-verified.

This experiment is intentionally isolated. It does not change the original 2D generator or replace the existing default preview/export path.
