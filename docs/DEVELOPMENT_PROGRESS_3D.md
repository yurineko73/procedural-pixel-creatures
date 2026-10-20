# 3D / equipment extension progress

**Updated:** 2026-10-19 (gear isolation + face-detail pass)  
**Repository:** https://github.com/yurineko73/procedural-pixel-creatures  
**Branch:** `feature/3d-modular-equipment`  
**Google Drive folder:** https://drive.google.com/drive/folders/1mATA3Xa9aJXC5EdDJ2_sOOrug1BSVuLO  
**Plan:** https://docs.google.com/document/d/1qAsRSdQkgOEmtjrjcnfJy1kydkVrl8q765LII8C-rPc/edit  
**Progress mirror:** https://docs.google.com/document/d/1uSl8Mu3wpOSDv6lnofj3idFyS4scTn2cOiuah0gC_gE/edit

## Status

**Baseline verification before the latest gear/face/toon changes (2026-10-19, Asia/Shanghai):** Godot 4.7.1 .NET was available locally. The prior preview controller build succeeded; both standalone examples passed import/headless startup; the core harness and Godot quick suite passed at that point. Those results are historical and do **not** validate the latest independent-seed, facial-feature-export, native gear randomizer or toon-shader changes below. The latest delta has only had static source review; rebuild and rerun all checks before declaring it validated.

| Milestone | Status | Verification |
|---|---|---|
| M0 Repository, branch and documents | complete | Branch and Drive folder created |
| M1 Static OBJ + MTL geometry exporter | earlier baseline passed; latest face-feature export needs re-test | rerun `export.obj_static_all_families`; inspect eyes/material groups in Blender |
| M2 Independent style pack framework | earlier baseline passed; latest per-slot seed changes need re-test | rerun `equipment.style_overlay_deterministic`, `equipment.slot_seeds_are_independent` and `equipment.native_gear_slot_seeds_are_independent` |
| M3 Godot 3D previewer | prior baseline passed; latest controls/shader changes need re-test | rebuild, headless startup, verify slot rerolls and toon-shaded face details visually |
| M4 Skeleton3D / skin / glTF / GLB | planned | Export + re-import + animation comparison |
| M5 Continuous topology / material / fitted clothes | planned | Body geometry, UV and clipping validation |
| M6 Performance, packaging and documentation | planned | Batch benchmark + release checks |

## Latest change set

- New `Core/Export/ObjMeshExporter.cs` exports geometry directly from rest-pose primitive definitions.
- New `Core/Equipment/EquipmentPacks.cs` adds `IEquipmentStylePack`, registry, deterministic per-part seed streams, and three experimental themed packs.
- New `CoreHarness obj` command writes `creature.obj` and `creature.mtl`.
- New engine-independent test cases for OBJ and equipment determinism.
- New reference: `docs/3D_EQUIPMENT_EXTENSION.md`.

## Important constraints

- Local Godot 4.7.1 build and headless verification have passed. The automatic downloader still pins 4.5.2; use the local editor for this branch until that launcher is migrated and verified.
- OBJ is static rest-pose only. No skinning, animation, UV, PBR, continuous SDF surface or GLB.
- New equipment overlays share base rig and palette slots, are designed for 3D export and do not recompute pixel canvas.
- Existing generator's default behaviour is not modified. All new features are opt-in.
- New character gear is deliberately artistic/stylized; firing, damage and AI are outside the equipment module.

## Immediate next steps

1. Manually inspect both examples using F6 in Godot 4.7.1 .NET; verify family/style selection, orbit, zoom and OBJ export.
2. Migrate the automatic launcher toolchain with verified archive hashes before claiming Start.cmd support for this branch.
3. Run `dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/scifi biped 12345 scifi`; inspect generated OBJ in Blender; verify shoulders, head, hand attachments and intersections.
4. Review the existing 3D preview material groups and style/seed controls visually.
5. Design GLB exporter and true skinning pipeline only after static mesh prototype passes geometry review.

## Continuation note

Do not start by modifying `BipedFamily` or original gene schema. Continue from this branch, run the build and report the first real test results before marking M1/M2 as validated. Use small, reusable components; prefer independent shape builders and style-pack registration over giant switches or scene-specific scripts.


## M3 preview scene added (2026-10-19)

- `examples/preview_3d/CreaturePreview3D.tscn` is a separate run-current-scene entry point; the existing `workshop/Main.tscn` remains the project start scene.
- `examples/preview_3d/CreaturePreview3D.cs` owns seed/style/equipment UI and regeneration flow.
- `examples/preview_3d/PreviewStageBuilder.cs` owns environment, lighting, ground grid, pedestal and orbit camera construction.
- `examples/preview_3d/ObjPreviewMeshBuilder.cs` parses the OBJ payload into indexed Godot surfaces and applies the sampled palette by material slot.
- UI includes family selector, text/numeric seed + random seed, regenerate, equipment toggle, Fantasy/Modern/Sci-Fi/Wasteland style selector, reset view and OBJ/MTL export.
- Modern/Sci-Fi/Wasteland use the separate biped equipment packs; other families fall back to Fantasy.
- Live meshes use the sampled palette by material slot. OBJ/MTL exports are saved to `user://exports/3d-preview/`.
- `examples/preview_3d/README.md` documents running and limitations.

**Historical validation note (superseded on 2026-10-19):** tools were unavailable when this prototype was added. Local build/headless results now appear at the top; visual inspection and CI verification remain pending.

## Examples build repair (2026-10-19)

- Branch: `feature/3d-modular-equipment`; commit: this repair commit (use `git log -1` after checkout). Local commit only; no push or Drive mirror update in this bug-fix task.
- Added root `AGENTS.md`, adapted from side-scrollerRPG with this repository's paths, milestone record and Drive progress mirror.
- Replaced `SizeFlags.ExpandFill` with `Control.SizeFlags.ExpandFill`: the controller derives from `Node3D`, so Control's nested enum is not inherited.
- Retained the existing local 4.7.1 project upgrade; unrelated minimal-example formatting and the `.csproj.old` backup are left outside the repair commit.
- Added `runtime.examples_build_models`: checks five actors (including generated serpent/offspring), nonempty preview surfaces and active camera.
- Validation: `dotnet build`; `dotnet run -c Release --project tools/core_harness/CoreHarness.csproj -- test reports all 10`; Godot console `--headless --path . --editor --import --quit`; each example `--headless --audio-driver Dummy --path . <scene> --quit-after 180 --max-fps 60`; Godot `--headless --audio-driver Dummy --path . -- --self-test --quick --report=res://reports/self_test_report.json`.
- Two existing `SplitContainer.SplitOffset` deprecation warnings remain in `workshop/Main.cs`; they do not block builds. Headless execution does not validate rendered appearance or manual interactions.


## Latest implementation delta: independent equipment + face detail (2026-10-19)

**Status: code committed, not yet recompiled or visually verified after these changes.**

- EquipmentSeedSet adds four stable random streams: Outfit, Weapon, Headgear and Accessory. Existing EquipmentGenerator.Apply(source, style, ulong) remains as a compatibility overload.
- Modern / Sci-Fi / Wasteland packs assign each random draw to a named slot. Headgear is now a seeded variant in all three styles; backpack/radio, shoulders/scrap plates and weapon props use their own slots.
- The preview UI now has separate Body Seed and Equipment Seed controls, plus Random Body, Random All and Random All Gear actions. Individual Random Outfit / Weapon / Headgear / Accessory buttons change only that slot's seed and show the active slot seeds; Fantasy slot buttons retry deterministically until that category has an item, and the Modern accessory pack always generates at least one accessory.
- Fantasy native gear is sampled from independent slot seeds (gear.outfit, gear.weapon, gear.helmet, gear.shield, gear.pauldrons). Its legacy shared gear.size remains controlled by the master gear seed. Cloth/metal colour genes are also tied to the master equipment seed.
- OBJ now exports the separately stored CreatureAnatomy.Features: eye sockets, sclera, iris/pupil, highlights, nostrils and spots, each with named material groups.
- The Godot preview now maps the palette's six shades through a stepped toon shader rather than using only each slot's base colour. Feature material names map to dark sockets/pupils, sclera, iris, highlights and glow.
- Tests were added for facial-feature OBJ output, overlay slot isolation (weapon reroll leaves jacket geometry stable), and native Fantasy gear slot isolation (weapon reroll leaves outfit/headgear/accessory genes stable; changing Body Seed does not reroll gear choices or clothing colour).

### Required revalidation before merge

1. Run dotnet build for the Godot C# project and dotnet build tools/core_harness/CoreHarness.csproj.
2. Run the full core harness, including export.obj_static_all_families, equipment.style_overlay_deterministic, equipment.slot_seeds_are_independent and equipment.native_gear_slot_seeds_are_independent.
3. Run Godot editor import and both existing headless example checks / quick suite.
4. In the preview, confirm eyes/pupils are visible from multiple angles, the toon ramp remains readable, body seed leaves gear shapes stable, and each slot button leaves other categories unchanged.
5. Export OBJ + MTL and inspect in Blender. If tests fail, fix before updating status to validated.

## Preview rendering repair from visual report (2026-10-19)

**Status: source fix committed; Godot recompilation and visual recheck are still required.**

- The screenshot showed Godot rejecting the custom shader because `return;` was used in the `light()` processor. Replaced the early return with a guarded lighting block, so the shader uses valid Godot spatial-light syntax.
- Removed `cull_disabled` from the preview shader. The OBJ exporter intentionally emits reverse-wound faces for two-sided triangles; rendering both copies with culling disabled could cause coplanar depth fighting.
- Fixed `MeshEmitter.Triangle`: the reverse-wound face now gets its own three OBJ vertices instead of sharing the front face's vertex indices. The previous shared indices caused generated vertex normals to average opposite directions and cancel, which can produce pale/fragmented-looking triangles.
- Reduced the custom toon ramp's direct-light multiplier to `0.62` so the two preview lights are less likely to wash the palette toward white.
- Added a CoreHarness regression assertion for a two-sided triangle object containing six independent vertices and two faces.

**Revalidation:** rebuild the Godot C# project, reopen `CreaturePreview3D.tscn`, and confirm there is no shader compilation error. Check that the body and equipment show palette colours, two-sided triangle pieces no longer flicker or form white shards, and the eye materials remain visible. Primitive intersections are still an architectural limitation of this static-mesh prototype; if obvious gaps remain after the shader/normal fix, the next task is to improve the actual surface construction rather than only adjusting lighting.


## Follow-up after the user's second visual report (2026-10-19)

The new screenshot showed that the shader compiles and colors are visible, but the character still reads as disconnected intersecting shells, with very dark interior-looking patches and exaggerated hue bands.

Additional source changes committed (still **not visually verified in Godot**):
- The toon shader no longer disables ambient lighting. Its light function also returns a deep palette contribution for back-facing normals rather than zero diffuse light, reducing pure-black surfaces; the stage ambient/fill levels were reduced to preserve form.
- 3D lighting now derives most shade levels from each material's base hue, with only a small blend from the 2D palette's hue-shifted shadow/highlight colors. Fixed-shade facial materials keep their authored shade.
- Added `ImplicitBodyMesher`, an engine-independent signed-distance union for organic ellipsoids and tapered segments using Primary/Secondary materials. It emits one continuous outer skin with material regions, preventing internal closed shells from showing through at overlapping torso/limb joints.
- Gear groups, interior mouth parts, hard/faceted details, triangles, and non-skin material slots remain separate. If the implicit surface is empty, the exporter falls back to the original primitives.
- Frustum cap rings now use separate vertex indices from the curved side rings so generated normals do not average flat cap normals with curved side normals.
- Added a CoreHarness regression for fused organic skin while ensuring gear remains separate.

**Next acceptance gate:** rebuild the Godot C# project and run `examples/preview_3d/CreaturePreview3D.tscn`; verify no shader errors, inspect biped plus at least one faceted/golem and non-biped family, and run the new `export.obj_unifies_organic_primitives` and existing export tests. This is a source-level fix only until those checks are run. If the skin is fused but still too lumpy, tune voxel resolution and smooth-union radius based on screenshots rather than reverting to overlapping closed ellipsoids.


## Follow-up: opaque surface, closed topology, and fitted garments (2026-10-19)

The three new screenshots show that the apparent transparency is actually consistent with missing/open surface patches and unreliable triangle orientation rather than alpha blending alone. The preview shader uses RGB albedo, but its default face culling could still discard incorrectly oriented surface triangles.

Further source changes committed:
- Explicitly set the preview toon material to `cull_disabled, depth_draw_opaque`. This keeps the preview opaque and depth-writing while ensuring a reversed face cannot disappear solely because of winding.
- Lowered organic SDF smooth-union radius from 2.0 to 0.9 pixels and refined the target voxel step from 2.0 to 1.25 pixels (bounded by the grid-point cap). The field now uses a continuous smooth-min instead of switching abruptly at a distance threshold.
- Orient each extracted triangle using the numerical gradient of the signed-distance field, so normals follow the actual outer surface rather than relying only on tetrahedron-centroid direction.
- Weld exact grid-point intersections in a dedicated cache. Previously, a surface passing exactly through a sampled grid vertex could create multiple coincident vertices on different edges, leaving cracks in the shell.
- Added closed-manifold diagnostics (boundary and non-manifold edge counts) and a regression assertion for the fused body.
- Fuse soft clothing ellipsoids by garment group for tunic, undercoat, breastplate, robe, skirt and loincloth flaps. Hard helmets, shields, pauldrons, buckles and thin/faceted details remain separate.
- Increase the biped tunic/undercoat/breastplate/robe clearance slightly so the garment skin sits outside the fused body instead of z-fighting/intersecting it.

**Validation status:** source and structural checks only; this environment cannot resolve GitHub to clone/build the repository, and the changes have not yet been compiled or visually tested in Godot. Rebuild the preview, inspect front/side/back, and run `export.obj_unifies_organic_primitives`. If the boundary-edge assertion fails, investigate the mesher topology before further visual tuning.

## Final preview seam refinement (2026-10-19)

The OBJ-to-Godot builder now calculates vertex normals across the complete OBJ before splitting geometry into per-material surfaces. This avoids sharp artificial normal seams where the fused body changes from Primary to Secondary material. The shader remains opaque and depth-writing. This is still awaiting an actual Godot scene run.


## P0 attachment and garment stabilization (2026-10-20)

**Implementation: committed to `feature/3d-modular-equipment`; validation: source-level only (build / CoreHarness / Godot visual test pending).** These changes are intentionally limited to the *static 3D export* so original 2D generation genes and animation definitions remain intact.

- Added `Core/Export/PreviewAttachmentRepair.cs`: for small `.claw`, `.teeth` and `.fangs` cone details, match a valid ellipsoid on the same bone; modestly detached tips are shifted inward into the host surface, while distant unsupported pieces are omitted. The immutable source primitive is never mutated, and anatomy with no suitable host is preserved.
- Added `Core/Export/PreviewEquipmentSockets.cs`: legacy biped and modern/scifi/wasteland held-weapon shapes are reoriented around a hand-local grip for the preview and shortened when their resting shaft would fall under the display floor. Native shield subparts are moved as a group toward the left forearm, with size limited by ground clearance. Source transforms/2D rig remain unchanged.
- Added `Core/Export/PreviewMeshCleaner.cs`: a conservative connected-component pass on the *fused* SDF skin/clothing mesh removes only tiny low-face-count and low-extent stray islands. Large disconnected anatomy, hard details and deliberate separate equipment pieces are preserved.
- Added `Core/Export/PreviewGarmentFit.cs`: for narrow-hip or exceptionally long-legged bipeds, hanging skirt/loincloth panels can be replaced by a short pelvis-fitted cloth hem; replaces only the identified groups without modifying original 2D outfit genes. This is a preview stability fallback, not a finished tailoring/cloth-simulation system.
- `Core/Export/ObjMeshExporter.cs` now incorporates the attachment repair, socket transforms, mesh cleanup and garment downgrade before emitting the static OBJ.
- `Core/Equipment/EquipmentPacks.cs` now preserves the chest shell's local rotation when generating a `Cover` for modern/scifi outfits, addressing geometry misalignment.
- `tests/Core/CoreTests.cs` adds four named regressions: `export.preview_attached_claws_and_teeth`, `export.preview_weapon_hand_socket`, `export.preview_extreme_garment_fallback`, `export.preview_tiny_sdf_island_cleanup`.

### Required validation before claiming P0 complete

1. Build `.NET 8` CoreHarness and the Godot 4.7.1 .NET solution; run all CoreHarness tests including the four named regressions.
2. Reopen `examples/preview_3d/CreaturePreview3D.tscn`, inspect user-supplied problematic biped seeds from at least front/side/rear angles, with/without equipment, and across Fantasy, Modern, Sci-Fi and Wasteland.
3. Check whether claws/fangs still float, shields and weapon shafts still hit the floor, thin fabric appears shredded, or the short-hem downgrade triggers too often. Do not remove valid wings/spines/eyes as 'debris'.
4. Open OBJ/MTL in Blender and check raw object groups. Current exporter still produces a static rest-pose prototype, **not** a finished rigged/animated/clothed GLB asset.

### Scope / next phase
P1 equipment catalogue expansion is **deferred**, not cancelled. First obtain green compilation/visual verification and tune P0 thresholds if necessary. Then expand curated equipment variants per slot (fantasy first, then modern/scifi/wasteland) with attachment socket and collision fit tests.


## P0 screenshot triage follow-up (2026-10-20)

**Code committed; build and visual verification still pending.** Follow-up analysis of the latest 3D preview screenshots found that the problem was not limited to detached claws: the exporter was excluding every soft Accent/White surface and every Faceted core primitive from the skin union, while using one fixed low blend radius. This left soft muzzles/hooves and some hard-shaded body parts as overlapping closed shells.

Implemented on `feature/3d-modular-equipment`:
- `ImplicitBodyMesher.ShouldUnify`: include soft Accent/White core anatomy and Faceted core shapes in the fused skin; continue to exclude hard/thin/translucent/front-only details, interior shapes, equipment and separately grouped appendages.
- Raise organic skin blend radius from 0.9 to 1.15. Change material attribution to penalize deeply buried primitives so interior ellipsoids do not paint the visible surface solely because their absolute SDF distance is small.
- `BuildSelected` now runs tiny-island cleanup on the fused organic body only. It no longer deletes small disconnected panels from garment meshes. Micro-island threshold was widened to match actual SDF grid scale while still requiring a small face count.
- `PreviewAttachmentRepair` now searches same-bone and ancestor limb hosts, including round-cone segments, and translates both endpoints in world space. It fits claws/teeth/fangs plus hoof/tusk/mandible anchors; only tiny claws/teeth/fangs are suppressed when clearly detached.
- `PreviewEquipmentSockets`: removed the 30% minimum weapon scale that could still let long spears/staves cross the display floor; scale now derives from the actual hand-to-floor clearance.
- `Shapes/Gear.cs`: raise caps/helmets above the eye band, shift the hood shell backward to leave the face visible, and seat pauldrons closer to the shoulder/upper-arm frame.

Added regressions:
- `export.preview_ancestor_limb_attachment`
- `export.preview_fuses_soft_accent_and_faceted_skin`
- `export.preview_headgear_eye_clearance`
- Expanded `export.preview_weapon_hand_socket` to assert floor clearance.

### Important scope note
The screenshots include a `plantfolk` family, but the current tracked GitHub branch's `Core/Families` directory does not contain a `PlantfolkFamily` implementation. The shared 3D export fixes apply to any anatomy passed to the exporter, but plantfolk-specific source-generation defects cannot be patched until that local family implementation is available in the repository.

### Next validation
1. Run .NET CoreHarness and Godot 4.7.1 self-tests; fix any compile/test failures before declaring the pass complete.
2. Re-test the exact screenshots' seeds, where reproducible, from front/side/rear views.
3. Inspect biped headgear, pauldrons, teeth, claws, weapon floor clearance; quadruped hooves; reptile jaw/claws; arthropod legs; and the plantfolk family if its source is checked in.
4. Verify the fusion did not over-smooth intended hard anatomy or merge separate horns/wings into the skin. P1 equipment expansion remains deferred until this pass is visually accepted.
