# Interactive 3D creature preview

This is a standalone scene. It does not replace the existing workshop start scene.

## Run

1. Open the repository with **Godot 4.7.1 .NET** (the current branch targets Godot.NET.Sdk 4.7.1 and .NET 8).
2. In the FileSystem dock, open `examples/preview_3d/CreaturePreview3D.tscn`.
3. Run the current scene with **F6**.

Alternatively, after the project's C# assembly has been built/imported, run the scene directly:

```shell
godot --path . res://examples/preview_3d/CreaturePreview3D.tscn
```

## Controls

- Choose a creature family.
- **Body Seed** controls the body morphology and body palette. Enter a numeric or text seed; press Enter or **Regenerate** to reproduce it. **Random Body** changes only this seed. **Random All** changes both body and equipment seeds.
- **Equipment Seed** is independent of the body. Changing it re-derives the outfit, weapon, headgear and accessory streams, as well as the equipment colour seed. **Random All Gear** changes only this master seed.
- Use **Random Outfit**, **Random Weapon**, **Random Headgear**, and **Random Accessory** to reroll one equipment category while preserving the other category seeds. The active per-slot seeds are shown below the master equipment seed.
- Enable/disable equipment.
- Select Fantasy (the family's original procedural gear), Modern, Sci-Fi, or Wasteland. The three new style packs use the same four independent slot streams.
- In the original Fantasy schema, `gear.size` is shared by several pieces and therefore changes only when the master equipment seed changes; slot rerolls only change the genes belonging to that slot.
- Drag the model area to orbit; use the mouse wheel to zoom; reset the camera if needed.
- **Export OBJ + MTL** writes the current preview to `user://exports/3d-preview/` and opens that directory.

The three new equipment packs currently support the `biped` family. Selecting another family automatically falls back to Fantasy.

## What this prototype renders

The preview exports both anatomy primitives and the separately stored face features (eye socket, sclera, iris/pupil, shine, nostrils and spots) into Godot triangle meshes. Materials use the creature's six palette shades through a custom stepped toon shader instead of a single base colour. The world includes a simple pedestal, ground grid, environment lighting and an orbit camera.

This is a geometry proof of concept, not a game-ready rig:
- no Skeleton3D skinning or animation;
- no UV textures or faithful reproduction of pixel patterns, original pixel contours, or full 2D soft-union shading;
- no 3D boolean/SDF union, so primitive intersections and seams are expected;
- the OBJ/MTL export uses placeholder material definitions even though the live preview uses the sampled creature palette.

If the preview does not open, first run the project's normal C# import/build and headless checks. The preview and its equipment styles have not been marked as validated until those checks and a visual inspection in Godot/Blender pass.


## Rendering repair note (2026-10-19)

The preview shader's `light()` processor must not use an early `return`; lighting is now guarded with an `if` block. Two-sided triangle backfaces are exported with separate vertices so generated normals do not cancel, and the preview uses back-face culling to avoid z-fighting between coincident front/back faces. These fixes are committed but still require a Godot 4.7.1 rebuild and visual verification.


## Follow-up mesh and shading changes (2026-10-19)

Organic Primary/Secondary ellipsoids and tapered segments are now extracted as a shared signed-distance outer skin; equipment, hard/faceted pieces, membranes/triangles, and interior details stay separate. This prevents the preview from displaying the inside shells of every overlapping body primitive. The toon shader also preserves the base material hue more consistently and allows ambient fill so shadowed surfaces do not collapse to black. Frustum caps use separate vertices from side rings for clean normals.

These changes are committed but require a Godot 4.7.1 rebuild and visual verification. The preview is still a static mesh, not a rigged or animated character.


## Opaque mesh and garment fit follow-up (2026-10-19)

The preview shader now explicitly uses opaque depth drawing and disabled face culling to avoid holes caused solely by inconsistent face winding. The organic mesher uses a continuous smooth-min field, finer voxel resolution, gradient-based triangle orientation, and exact-grid-point vertex welding; it exposes boundary/non-manifold edge counters for tests. Soft tunic/robe/undercoat/breastplate/skirt/flap groups are fused independently into fitted outer surfaces, while hard armour details and other equipment remain separate. Biped garment clearance was increased slightly to avoid skin/cloth intersections.

Not yet validated in a running Godot scene. Check front, side, and rear views after rebuilding; this source change must not be considered visually accepted until the body shell has no holes and garments no longer appear as stacked overlapping blobs.

The OBJ preview builder also computes shared normals before material splitting, keeping skin shading smooth across material boundaries. Runtime verification remains pending.
