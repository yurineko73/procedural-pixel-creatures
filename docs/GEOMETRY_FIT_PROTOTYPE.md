# Multi-view geometry-fit prototype

Branch: `experiment/geometry-fit-prototype`

## Goal

Test a third direction without replacing either existing 3D export path. The prototype keeps the source anatomy primitives and fits a small number of bounded semantic-group transforms against silhouettes rendered by the original 2D renderer.

It does **not** reconstruct a volume from captured depth. It also does not change the original 2D renderer, genetics, or seed generation.

## How it works

1. Capture the original 2D silhouette at eight evenly spaced yaw angles.
2. Build a lightweight triangle projection from the existing ellipsoids, round cones, and triangle primitives. Its IoU is a cheap diagnostic and helps choose which sign of a parameter change to try first.
3. Discover up to six major semantic groups for the selected family (body, head, ears, neck, limbs, tail, wings/fins, shell/back, and other named anatomy where present).
4. Run deterministic coordinate search over each group's uniform scale and X/Y/Z world-space offset. The allowed change is small (scale within ±8%, translation within ±0.5 source pixels by default).
5. **Evaluate candidate changes using the silhouette of the actual OBJ emitted by the current exporter.** A candidate is accepted only if the weighted mean/worst-view OBJ IoU improves. Proxy IoU never overrides a worse OBJ score.
6. Use a hard OBJ-evaluation budget (32 including the unmodified baseline by default) to bound the cost of synchronous preview generation. The first sweep tests a proxy-preferred direction for every coordinate before spending remaining budget on reverse directions.
7. Reuse the best accepted OBJ for the live preview/export. The returned anatomy is a clone; the original source anatomy object is not mutated.

The score is `0.7 × mean view IoU + 0.3 × worst-view IoU`. The optimizer never accepts a candidate with a lower score than the best mesh already found. The proxy scores are reported separately because they can improve or decline independently of the actual export.

## Preview

Open `examples/preview_3d/CreaturePreview3D.tscn` in Godot 4.7.1 .NET and run it with F6.

Enable **几何拟合原型（实验 / 不含装备）**. The reference thumbnail shows the original 2D render. The status panel reports:

- Initial/fitted primitive-projection IoU (diagnostic only).
- Initial/fitted silhouette IoU measured from the actual exported OBJ, including worst-view scores.
- Number of adjusted groups, actual OBJ evaluations, proxy evaluations, and chosen parameters.

The geometry-fit toggle and the multi-view depth-reconstruction toggle are mutually exclusive. The original OBJ mode remains the default. Equipment is disabled in this mode.

## Important limitations

- This optimizes a small set of group-level transforms, not individual primitive topology, per-bone positions, local rotations, materials, or the 2D renderer itself.
- A silhouette score cannot measure surface quality, self-intersections, anatomical plausibility, color/pattern fidelity, or hidden-side correctness. The current OBJ exporter may still contain seams and overlapping pieces.
- Only groups recognized by the explicit semantic-name list can be fitted. A family with many groups is capped at six prioritized regions by default.
- The mesh-evaluation budget is intentionally bounded. Some parameter directions or later refinement passes may remain untested.
- The fit runs synchronously from the preview UI and may briefly block the main thread.
- Animation, skinning, UVs, and game-ready GLB export are not included.

## Acceptance criteria

For a fixed seed, compare the original OBJ mode and geometry-fit mode:

1. Report deterministic proxy and actual OBJ silhouette metrics.
2. Confirm the fitted actual-OBJ mean/worst score never regresses against the initial OBJ score.
3. Inspect front, side, rear, and intermediate angles for detached limbs, floating facial details, obvious gaps, and new self-intersections.
4. If the silhouette score improves but the surface looks worse, do not widen the parameter bounds. Add geometry-quality checks or revise the fit parameters/objective instead.

## Validation status

Source-level implementation and regression assertions are present. The code has not yet been compiled or visually inspected in a running Godot scene in this environment; passing the source review is not visual acceptance.
