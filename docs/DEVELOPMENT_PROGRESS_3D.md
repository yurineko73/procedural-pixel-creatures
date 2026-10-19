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
| M2 Independent style pack framework | earlier baseline passed; latest per-slot seed changes need re-test | rerun `equipment.style_overlay_deterministic` and `equipment.slot_seeds_are_independent` |
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
- Tests were added for facial-feature OBJ output, overlay slot isolation (weapon reroll leaves jacket geometry stable), and native Fantasy gear slot isolation (weapon reroll leaves outfit/headgear/accessory genes stable).

### Required revalidation before merge

1. Run dotnet build for the Godot C# project and dotnet build tools/core_harness/CoreHarness.csproj.
2. Run the full core harness, including export.obj_static_all_families, equipment.style_overlay_deterministic, equipment.slot_seeds_are_independent and equipment.native_gear_slot_seeds_are_independent.
3. Run Godot editor import and both existing headless example checks / quick suite.
4. In the preview, confirm eyes/pupils are visible from multiple angles, the toon ramp remains readable, body seed leaves gear shapes stable, and each slot button leaves other categories unchanged.
5. Export OBJ + MTL and inspect in Blender. If tests fail, fix before updating status to validated.