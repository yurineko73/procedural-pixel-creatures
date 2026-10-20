# Multi-view depth reconstruction experiment (not merged)

Branch: `experiment/multiview-depth-reconstruction` based on `feature/3d-modular-equipment`.

This is a **separate geometry-generation experiment**, not another adjustment to
`ImplicitBodyMesher` / `PreviewDentalFit` / `PreviewClawFit`. The source
2D generation pipeline is unchanged apart from a read-only G-buffer snapshot accessor.

## Try it in Godot 4.7.1 .NET

1. Check out **this experiment branch** (do not run the original 3D branch).
2. Open `examples/preview_3d/CreaturePreview3D.tscn` and run the scene (F6).
3. Choose **biped**, Body Seed **12345** (then **5437137294946840838** and
   **16114805862360663785**).
4. Under **03 实验性重建模式**, enable **多视角深度重建（实验 / 不含装备）**.
5. The left panel shows an **actual original 2D pixel image** from the first view.
   The right panel shows the **new multi-view reconstructed 3D mesh**. Disable the
   option to see the old OBJ/SDF path with the same seed. Drag to rotate the 3D.
   Use **对齐原版 2D 摄像机（0° / 30°）** to switch the right-hand camera to a
   matching orthographic orientation (pixel scale still depends on viewport).
6. The Export button writes `creature.obj` and `creature.mtl` to
   `user://exports/3d-preview/`.

The original exporter remains the default. Experimental mode deliberately
suppresses **all biped equipment**, including existing fantasy gear, to avoid
comparing an old-style equipment overlay against body-only source images.
The reconstructed model is a **single static surface**; it is not rigged.

## Engine-independent command

Requires .NET SDK 8:

```bash
dotnet run --project tools/core_harness/CoreHarness.csproj -- \
  multiview out/multiview biped 12345 16 1.2
```

Output: `out/multiview/creature.obj`, `creature.mtl`, and `silhouette_iou.txt`.
The last two arguments control view count and requested grid spacing. The grid
automatically coarsens to obey the 320,000-grid-node memory budget.

Focused source-level regression command:

```bash
dotnet run --project tools/core_harness/CoreHarness.csproj -- \
  test out/test export.multiview_depth_reconstruction 1
```

**Important:** These commands and the Godot scene have **not yet been executed
in the implementation environment**, where the .NET SDK and Godot binary are
unavailable. No passing build, measured visual similarity, or render screenshot
is claimed. GitHub Actions are intentionally left unchanged/disabled.

## What is genuinely different

Old path:
`CreatureAnatomy -> ImplicitBodyMesher smooth-min over selected primitives
-> dedicated attachment repair -> OBJ`.

New path:
`CreatureAnatomy -> original CreatureRenderer 16 direction rasterization ->
post-cleanup G-buffer depth/silhouette/primitive provenance -> multi-view visual hull + bilinearly sampled, edge-aware smoothed front-depth constraints -> marching tetrahedra -> lightly smoothed mesh -> OBJ`.

The reconstructed body does not call `ImplicitBodyMesher.Build` or `ObjMeshExporter.Export`. It uses rendered silhouettes as the conservative volume constraint and only uses front depth in a truncated band more than 0.75 pixels inside the silhouette. Captured normals are retained for diagnostics/material selection, not used to carve the body field. The separate eye/pupil/highlight/nostril meshes reuse `ObjMeshExporter.AppendFeatures` and its existing `PreviewFeatureFit` solely to position those details.

### Details

- `CreatureRenderer.CaptureDepth` takes a defensive copy **after** the renderer's original pixel pipeline. `PixelDepthCapture` stores depth, normals, original primitive/group IDs, visibility and final palette indices. Ground shadows are excluded from the geometry mask. Eye pixel stamps are not treated as mesh geometry; the original bone-anchored feature definitions are exported as separate meshes.
- `PixelDepthCapture.BackProject` uses the inverse of the original Y-axis
  yaw plus 30° oblique orthographic camera to obtain observed 3D points.
- Per-image chamfer distance approximates the final 2D silhouette. A 3D
  sample must remain inside the silhouette of **every** observation. The outer
  pixel band is controlled by silhouettes alone; interior front-depth samples
  are bilinearly sampled and lightly smoothed without crossing semantic group or
  large depth boundaries. They refine only nearby surface points and are ignored
  farther than `DepthTruncation` behind the visible surface.
- A bounded 3D grid samples the resulting intersection field. Marching
  tetrahedra with shared edge vertices produces an OBJ surface.
- `MultiviewSilhouetteMetrics` rasterizes the reconstructed triangle silhouettes
  into each source camera and reports per-view/average/worst intersection-over-union.
  This tests *shape overlap only*, NOT facial fidelity or color quality.
- The OBJ assigns semantic material slots from original primitive IDs and the final pixel palette. This preserves distinct materials and some pattern colors without assigning a different shade to every triangle. It is **not** a UV texture atlas, so detailed markings remain approximate.
- The Godot viewer uses the project's existing **semantic toon material ramps**. The appended eye/nostril meshes use dedicated feature materials.


## Current boundaries / rejection conditions

The screenshot review found severe faceting and missing facial structure. The first screenshot-driven revision regressed badly: source-normal tangent-plane constraints fragmented limbs and produced a low silhouette IoU. They have been removed. The current branch instead builds a conservative silhouette visual hull and applies depth only within a truncation band near each observed front surface; hidden interior samples no longer carve through the body. Voxel spacing is finer, depth sampling is continuous, depth smoothing is edge-aware, and a single low-strength field smoothing pass reduces corrugation. These measures may still soften or disconnect sub-pixel appendages. This correction is not yet visually verified. This proof of concept does NOT guarantee better visual quality. In particular:

1. **2D G-buffer depth is synthetic.** Soft screen-space unions and depth
   bias can make independent views geometrically inconsistent. The current
   tolerance makes intersections possible; it is not an optimization solver
   yet. A strict multi-view match might be mathematically impossible. Depth constraints are deliberately truncated and excluded from the outer silhouette band: they refine interior surface depth but do not define the hidden interior or erode narrow silhouettes.
2. **Occlusion:** A hidden ear root, mouth interior, occluded tooth or underside
   cannot be reconstructed from visible pixels alone. Anatomical priors and
   bone-constrained reconstruction are still required for a usable final model.
3. **Resolution and topology:** Small tips and thin limbs may vanish from the
   sampled grid. A closed scalar-field surface is not equivalent to a connected,
   articulated anatomical rig. Adjacent biological parts may fuse unintentionally.
4. **Face fidelity:** Eyes, pupils, highlights and nostrils now use explicit bone-anchored feature meshes. Mouth and teeth still need dedicated topology validation; their rasterized color alone does not guarantee a true mouth cavity or connected dental arc. No UVs, skinned animation, or GLB.
5. **Performance:** Generating 16 full pixel views and a 3D grid is CPU
   intensive and currently synchronous. The GUI may block briefly; this is
   an offline experiment, not a production per-frame preview.
6. **View comparability:** The left reference is the exact 0° pixel output.
   The 3D orbit camera uses the existing preview stage, which is not a
   pixel-perfect synchronized replica of the original oblique orthographic
   camera; manual camera matching is approximate.

**Acceptance:** compare per-direction silhouette IoU AND visually inspect ears, mouth, eyes, claws and intermediate free-rotation views; first prove the fixed biped seeds look materially better
than the old export, including eye/mouth/ear connection. If this fails,
**do not replace** the old path with this method. Next steps would require
per-part depth masks, back-side anatomy priors and a multi-view consistency objective.

This experiment deliberately does not claim that a single ordinary textured
mesh can exactly reproduce all view-dependent pixel cleanup rules.
