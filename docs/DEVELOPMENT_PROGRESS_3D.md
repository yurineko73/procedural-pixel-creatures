# 3D / equipment extension progress

**Updated:** 2026-10-20 (geometry-fit experiment; validation pending)  
**Repository:** https://github.com/yurineko73/procedural-pixel-creatures  
**Branch:** `experiment/geometry-fit-prototype`  
**Google Drive folder:** https://drive.google.com/drive/folders/1mATA3Xa9aJXC5EdDJ2_sOOrug1BSVuLO  
**Plan:** https://docs.google.com/document/d/1qAsRSdQkgOEmtjrjcnfJy1kydkVrl8q765LII8C-rPc/edit  
**Progress mirror:** https://docs.google.com/document/d/1uSl8Mu3wpOSDv6lnofj3idFyS4scTn2cOiuah0gC_gE/edit

## Status

**Build repair on geometry-fit branch (2026-10-20, Asia/Shanghai):** `dotnet build` passes
with **0 errors and 0 warnings**. Fixed two `GeometryFitPrototype.ApplyRegions` dictionary
lookups to receive nullable `FitRegion?` values and explicitly guard null before transforming
primitives/features. Also applied the Godot `MetallicSpecular` and `SplitOffsets` API corrections
that were absent from this branch; fitting transforms and layout offset values are unchanged.
Godot 4.7.1 console import and both examples' standalone headless startup passed
(`--quit-after 120 --max-fps 60`). Core harness command
`dotnet run -c Release --project tools/core_harness/CoreHarness.csproj -- test reports geometry_fit 10`
passed **1/1**. Godot `--headless --audio-driver Dummy --path . -- --self-test --quick
--report=res://reports/self_test_report.json` passed **52/56**, including geometry fitting,
example model generation and workshop checks. The same four geometry failures remain:
`export.obj_static_all_families` (missing biped pupils), `export.obj_unifies_organic_primitives`
(closed manifold), `export.preview_visible_mouth_cavity` (mouth manifold), and
`export.preview_eyestalk_bulb_fit` (eye scaling). These are not build failures; separate geometry
investigation is still needed. Visual acceptance remains pending. This is a local repair record;
no Drive mirror update or GitHub push is claimed.

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


## 2026-10-20 — Eye attachment / appendage regression follow-up (committed, unverified)

Continuation of P0 screenshot stabilization, on `feature/3d-modular-equipment`:

- Fixed a remaining hole in `ImplicitBodyMesher.ShouldUnify`: independent horns, antennae, tentacles, eye-stalks and other separately grouped appendages must stay independent for **all** palette materials, including Primary/Secondary. The previous filtering applied appendage-name exclusions only to Accent/White, so changing the palette could silently alter geometry.
- `PreviewAttachmentRepair`: microscale claws/fangs/teeth with **no eligible supporting host** are omitted from the static OBJ instead of silently keeping floating geometry. Larger intentional anatomy remains.
- New `PreviewFeatureFit.cs`: caps nominal eye-stamp size to a same-bone host ellipsoid, detects eligible bare eye-stalk ends with a nearby segment and requests a supporting primary-colour bulb without changing the 2D source.
- `ObjMeshExporter.cs`: generates those bare-stalk backing bulbs and applies bounded whole-eye position correction (socket/iris/pupil/shine share the same offset) for slightly detached cranium eyes; does not snap distant features to unrelated geometry.
- New smoke tests: `export.preview_standalone_appendage_materials`, `export.preview_eyestalk_bulb_fit`, `export.preview_eye_surface_projection`, `export.preview_reported_seed_regressions` (reported biped, quadruped, reptile, arthropod and amorphous body seeds). Existing eye-size and equipment-fit regressions retained.

**Verification:** inspected changed source for balanced delimiters and consistency. A local `git ls-remote` attempt failed DNS resolution of github.com; there is no `dotnet`, `csc`, or `godot` executable in this runtime. Therefore the tests were added **but not actually executed** and the visual defects cannot be declared resolved. Do not enable GitHub Actions to work around this (workflow was previously paused). Next: run full CoreHarness and Godot 4.7.1 locally, inspect exact problematic seed/style combinations in three views; record actual pass/fail, fix any compile or visual regression, then reassess P1 equipment expansion.


## 2026-10-20 — Legacy 2D attachment semantics restored for static 3D (code committed; unverified)

User reproduced remaining regressions with two fixed Body/Equipment combinations:
- `biped`, Body Seed **12345**, Equipment Seed **54321**: teeth protruded away from the snout; nails/claws floated in front of the feet; a shield/weapon needed correct hand frame.
- `biped`, Body Seed **973718778264341244**, Equipment Seed **3941624577870863579**: headgear and additional held equipment appeared, highlighting the need for consistent hand- and face-local frames.

**Root cause found in original family source, not just shader/SDF:** `Shapes/Heads.AddTeeth` authors upper teeth partly *inside* the 2D lip with -Y tips in head coordinates. `Shapes/Limbs.Build` authors claws at `PawLength * 0.62`, which can be farther forward than the 3D ellipsoid's actual front edge (notably plantigrade feet). `BipedFamily.BuildWeapon` authors shafts on hand-local `new Vec3(0.12, 1, 0)`, but the previous 3D preview replaced this with a single world-up rule. Using the generic nearest-surface helper on teeth/claws introduced additional incorrect repositioning.

Code committed to `feature/3d-modular-equipment`:
1. New `Core/Export/PreviewDentalFit.cs`: route `.teeth` and `.fangs` through a snout-specific upper-lip fit (matching the original same-head `.snout` cone). Root is inset behind the closed lip, tooth length and width bounded by the tapering snout radius, downward orientation preserved. Unhandled families keep their existing authored geometry.
2. New `Core/Export/PreviewClawFit.cs`: route tagged `.claw` segments through same-bone `.foot/.paw/.palm` ellipsoid; begin inside its front surface, point toward positive local X and limit tip size. When no appropriate paw exists, fall back to ancestor-segment attachment repair instead of globally deleting jointed toes.
3. `ObjMeshExporter.cs` dispatches semantic upper teeth and claws **before** generic attachment repair, eliminating duplicate / conflicting snapping.
4. `Shapes/Limbs.cs` now labels hand claws with `s.Name + ".claw"` just like original foot claws; 2D geometry unchanged.
5. `PreviewEquipmentSockets.cs` computes weapon orientation from the original hand-local shaft axis and assigns readable rest directions based on weapon type (spear/staff, dagger, axe/hammer/club, blaster/gun), pivoting around the original grip. Existing floor-clearance scaling remains. `PreviewClawFit` guards against bending a low-lying claw tip upward to meet a fixed absolute height.
6. Tests added: `export.preview_dental_lip_fit`, `export.preview_claw_toe_fit`, `export.preview_fixed_user_seeds`. The fixed-seed test reproduces Body Seed 12345 and Equipment Seed 54321 using `NativeGearRandomizer`, checks mesh validity and deterministic OBJ output.

**Validation status:** code and tests committed and source delimiter / wiring inspected, but **not built or run**. This tool environment lacks `dotnet`, `csc` and `godot` and cannot clone GitHub due to DNS. GitHub Actions remains disabled per prior user instruction; no workflow was reenabled.

**Acceptance criteria for next Godot 4.7.1 run:** for seeds 12345/54321 and 973718778264341244/3941624577870863579, check upper and side lip contact (teeth should not fan outside the upper lip); left/right hand/foot nails should grow from the attached palm/paw and not sit on the pedestal; shaft axis should be consistently held at the hand pivot without dragging through the platform. Run the three named regressions and existing CoreHarness suite. If failures persist, report exact failing Seed, viewpoint and whether equipment enabled; do not mark P0 visually complete or expand gear catalogue until fixed.


## 2026-10-20 — Mouth cavity, ear roots and arthropod leg chains (code committed; Godot verification pending)

User screenshot feedback: `biped` Body Seed **12345** / Equipment Seed **54321** now has improved claws but still shows external-looking fangs and, more fundamentally, has **no visible mouth**. Another biped Body Seed **5437137294946840838** / Equipment Seed **17047702928857436918** shows free-looking ear bases. An `arthropod` Body Seed **14734810077418951438** / Equipment Seed **9423138326607858554** shows discontinuous limb segments.

**Root causes verified in original code:**
- `Heads.Build()` already generates an animated jaw plus a `.mouthInterior` primitive, but sets `GroupFlags.Interior`, hiding it in the original 2D closed-mouth pose. Static 3D global SDF also excludes interior shapes, so the head mesh has no visible lips, slit or cavity despite independently exported teeth.
- `Ears.Build()` creates separate ear bone chains from a silhouette base; when their starting bone is beyond the true skull ellipsoid, the 3D outer and inner ear do not touch the head. Moving inner and outer primitives separately is wrong.
- `Limbs.Build()` supplies a continuous upper/lower/distal arthropod bone chain, but thin segments at or below the globally capped SDF grid step can disappear or fragment, despite correct source joints.

**Implemented on `feature/3d-modular-equipment`:**
1. `ImplicitBodyMesher.cs`: use the existing `.mouthInterior` + head/jaw/snout semantic information to construct a 3D rest-pose mouth cutter in the head bone frame. Subtract its small elliptical slit from the organic body SDF and label the exposed inner wall `MaterialSlot.Mouth`. No permanent changes to source anatomy or the original 2D jaw animation. Short-snouted faces fall back to a compact cranium-front slot. This is a shallow closed-mouth indentation, not a fully animated open-mouth topology.
2. `PreviewHeadAttachmentFit.cs`: compute a deterministic translation per ear group so its original `.outer` root is embedded just beneath the same-bone skull ellipsoid. Apply the same correction to both outer and inner ear geometry to prevent cartilage/root separation. Head and ear source skeletons are unchanged.
3. `PreviewSegmentChainFit.cs`: for `arthropod` leg groups `leg*N`, find complete `.upper/.lower/.distal` chains, preserve each original joint as the next segment's origin, use a minimum leg tube thickness and overlapping joint spheres. Incomplete chains fall back to legacy exporter.
4. `ImplicitBodyMesher.ShouldUnify` excludes those arthropod leg segments from the full-body coarse voxel skin. `ObjMeshExporter` emits exact rest-pose chain tubes and applies ear-group transforms; other animal families retain existing geometry rules.
5. New regressions `export.preview_visible_mouth_cavity`, `export.preview_ears_embed_in_skull`, `export.preview_arthropod_leg_chain` verify mouth material exposure/manifold surface, ear-root enclosure, and chain continuity, respectively.

**Verification:** structural checks and source wiring inspected; still **not compiled, unit-tested or rendered in Godot** because no `dotnet`, `csc`, `godot` or GitHub-clone network access is available in this session. Existing GitHub Actions remains disabled. Do not mark P0 resolved until the tests and visual checks pass.

**Next local acceptance:** first re-run fixed seeds above from front, side and top views. Require a recognizable dark mouth slit below the snout, teeth actually rooted to that lip/cavity, two embedded ear bases, and continuous arthropod leg chains that connect to the thorax. Also inspect quadruped/reptile and humanoid faces for unintended mouth carving. Expand gear variants only after geometry stabilizes.


## 2026-10-20 — Visual acceptance FAILED: 3D conversion is losing 2D rendering semantics

User tested the latest preview and provided four screenshots. Do NOT mark mouth/appendage P0 complete.

**Observed failures**
- Biped Body Seed 12345 / Equipment Seed 54321: mouth cut is below the muzzle and looks like an exposed red cavity with detached-looking white teeth. The synthetic cutter in the latest patch is not an acceptable mouth solution.
- Biped Body Seed 16114805862360663785 / Equipment Seed 3668598035714744795: giant pale horn/ear-like appendages look like capped cones, their bases are not convincingly rooted, and toe/claw tips look separated from the paw.
- Side view of the same model: teeth are a row of isolated white/red primitives rather than an anatomically framed oral cavity.
- Red-creature screenshot: horn-like points visually collide with/cover the eye.

**Source audit — confirmed**
- Main and feature branches have identical hashes for `Core/Shapes/Heads.cs` and `Core/Shapes/Appendages.cs`. The original source already uses cones/round-cone segments for individual teeth, ears and horns; this is intentional primitive anatomy, not proof that the original final 2D image showed disconnected cones.
- `Heads.cs` explicitly documents the upper tooth row as hidden inside the lip until the mouth opens. Its mouth interior is placed relative to `hingeLocal` and `chinLocal`, in an `Interior` group.
- Original `CreatureRenderer.cs` contains `CleanupInterior()`: it peels interior-group pixels touching the background so the closed mouth does not leak below the head. The 2D renderer also uses group depth/layer semantics. The 3D OBJ exporter does not reproduce this pixel-layer cleanup or the jaw animation.
- The last patch's `BuildMouthCutters` detects the original cavity but then ignores its actual center and jaw/chin seam, placing a guessed cutter at 70% along the snout with a fixed downward offset. This is why the mouth can appear on the underside of the snout. Rework or remove this heuristic; do not treat it as a successful fix.
- `PreviewHeadAttachmentFit` only targets `BoneRole.Ear`; it does not fix horn groups. `Horns.BuildChain` generates segmented cone primitives, and the OBJ exporter emits each as a flat-capped frustum without a fused smooth root. Eye/horn clearance is not enforced.
- `PreviewClawFit` only relocates the claw primitive. It does not fuse the claw base into the paw mesh or validate the visible surface intersection; the user's floating-looking toe tips show this is insufficient.
- Arthropod direct-chain output has not yet been visually accepted; code existence alone is not a pass.

**Architectural conclusion**
The recurring issue is not a few bad offsets. The original is a 2D silhouette/layer/animation generator, while the OBJ exporter turns its primitives into independent capped 3D shells and only fuses selected body masses. The 2D renderer's interior cleanup, depth bias, occlusion order, and animated jaw cannot be assumed to transfer to a static 3D mesh.

**Next implementation must be anatomy-aware, not another nearest-surface patch**
1. Remove/rework the guessed mouth cutter. Build a real static mouth assembly from the source jaw hinge, chin, upper muzzle and cavity geometry. Make upper teeth short and embedded at the lip line; show only plausible tooth tips with mouth closed. If the generator has no suitable lip seam, add explicit 3D mouth metadata in the head builder rather than guessing from snout percentage.
2. Add a separate semantic appendage mesher for ears and horns: smooth tapered curved tubes, a base collar/root embedded into the skull, connected segment joints, and eye/face clearance checks. Never confuse a horn root with an ear root.
3. Treat paw + toe/claw as a single connected assembly (fuse/union or deliberately embed roots past the visible surface); verify front/side/underside views.
4. Render all reported fixed seeds in Godot before accepting. Add geometry-level checks for root intersection and eye clearance, plus visual approval screenshots. Keep the original 2D pipeline unchanged.

Status: source audit done; visual acceptance failed. No further geometry changes should be accepted as complete without a local Godot render and test run.


## 2026-10-20 — First direct geometry fixes committed; runtime acceptance pending

User feedback was correct: do not redesign the creature system. Reuse the complete 2D anatomy and make the static 3D export respect its authored geometry.

**Changes committed on `feature/3d-modular-equipment`:**
- `ImplicitBodyMesher.BuildMouthCutters` now uses the original `.mouthInterior` ellipsoid's local center, rotation and radii, with only a small expansion to open the static lip seam. Removed the guessed snout-70%-plus-downward-offset placement.
- `PreviewDentalFit` now shortens teeth more and anchors them close to the snout axis so they enter the authored mouth cavity instead of being pushed below the muzzle.
- `PreviewHeadAttachmentFit` now fits horn groups as well as ears. Horn roots are buried deeper to hide flat frustum caps; a close horn/eye pair receives a small group-wide clearance offset. These corrections are 3D-export-only.
- `PreviewClawFit` now embeds claw roots deeper into the matching paw/foot/palm ellipsoid.
- `tests/Core/CoreTests.cs` adds a horn-root/eye-clearance regression, checks that mouth surfaces stay near the authored cavity, and includes the two user-reported body/equipment seed pairs (12345/54321 and 16114805862360663785/3668598035714744795).

**Verification status:** Regression tests are added but have NOT been executed in this environment. The repository reports zero GitHub Actions runs and zero check-runs for the latest commit, so there is no CI result to claim. A local Godot render is still required to visually approve the exact seeds from front/side views. If the mouth/eye-clearance tests fail, fix the implementation before asking the user to retest.

Do not mark the 3D visual work complete yet.



### Follow-up refinement

Horn/ear root embedding now scales with the actual appendage root radius rather than forcing every root to a fixed percentage of the skull. Horn eye-clearance offsets are followed by a root re-seat, repeated for up to three small corrections, so avoiding an eye does not simply pull the horn free of the cranium. The horn regression also checks that the root remains inside the skull after eye avoidance.



## 2026-10-20 — Geometry-fit objective correction

**Branch:** `experiment/geometry-fit-prototype` (not merged).  
**Core implementation commit:** `892610b882759ae2478a6c8a1ed1f2f21bf750f5`.

The experiment now scores candidate adjustments using the actual OBJ silhouette, not just the lightweight primitive-projection proxy. The proxy only selects a preferred search direction; a candidate is retained only when the actual OBJ's weighted mean/worst-view IoU improves. The fitter also prioritizes up to six semantic groups and caps OBJ evaluations to keep the preview bounded.

- Core entry: `Core/Export/GeometryFitPrototype.cs`.
- Preview: `examples/preview_3d/CreaturePreview3D.tscn` / `CreaturePreview3D.cs`.
- Regression: `export.geometry_fit_prototype_is_bounded` now checks the actual OBJ score does not regress, the evaluation budget is respected, and the returned OBJ exactly matches the returned fitted anatomy.
- Validation: source changes committed; .NET build, CoreHarness, and Godot visual run are still pending. Do not treat improved silhouette IoU as proof of anatomical or surface quality.
- Next: run `dotnet build`, the CoreHarness suite, and the preview in Godot 4.7.1 .NET; compare the same fixed seed in original OBJ and geometry-fit modes from front/side/rear/45-degree views.


## 2026-10-21: semantic topology experiment (separate branch)

**Branch:** `experiment/semantic-topology-prototype`  
**Base:** `experiment/geometry-fit-prototype`  
**Status:** code and a preview toggle added; automated build/test and visual review pending.

- Added `Core/Export/SemanticTopologyPrototype.cs` as a separate final-mesh path. It samples the source primitive surfaces as control points, splits semantic groups into spatially connected components, builds fixed indexed radial surfaces, and relaxes vertices against original-renderer multi-view silhouette/depth constraints.
- The final body skin from this path does not call the old SDF skin builder or the multi-view voxel-volume mesher. The old SDF exporter is invoked only to measure a baseline silhouette score; its body mesh is not included in the new output.
- Added a new mutually exclusive preview toggle: **语义拓扑网格（实验 / 不含装备）**. Recommended first comparison: biped, Body Seed `12345`, equipment off; inspect front/side/rear and intermediate angles.
- The preview reports baseline SDF skin IoU, new topology IoU before relaxation, and after relaxation. IoU is a silhouette metric only and must not be treated as proof of anatomical quality.
- Added the CoreHarness test `export.semantic_topology_prototype_is_sdf_free` and `docs/SEMANTIC_TOPOLOGY_PROTOTYPE.md`.
- Known limitations: radial surfaces may fill concavities or round the muzzle/jaw; separate parts may overlap or show seams; true shared anatomical boundary loops, full pixel-pattern UV baking, Skeleton3D skinning, and animation are not implemented.
- **Do not merge or make this the default based on the code change alone.** Run the source audit, CoreHarness and full Godot checks, then visually compare the same seed. If the head/mouth and attachment quality do not improve, replace the radial head patch with an authored topology template or stop this route rather than returning to SDF tuning.
