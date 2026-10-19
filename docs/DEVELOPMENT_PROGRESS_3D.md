# 3D / equipment extension progress

**Updated:** 2026-10-19  
**Repository:** https://github.com/yurineko73/procedural-pixel-creatures  
**Branch:** `feature/3d-modular-equipment`  
**Google Drive folder:** https://drive.google.com/drive/folders/1mATA3Xa9aJXC5EdDJ2_sOOrug1BSVuLO  
**Plan:** https://docs.google.com/document/d/1qAsRSdQkgOEmtjrjcnfJy1kydkVrl8q765LII8C-rPc/edit  
**Progress mirror:** https://docs.google.com/document/d/1uSl8Mu3wpOSDv6lnofj3idFyS4scTn2cOiuah0gC_gE/edit

## Status

| Milestone | Status | Verification |
|---|---|---|
| M0 Repository, branch and documents | complete | Branch and Drive folder created |
| M1 Static OBJ + MTL geometry exporter | code committed, not run | Requires .NET 8 build + OBJ visual inspection |
| M2 Independent style pack framework | code committed, not run | Modern / sci-fi / wasteland deterministic overlays |
| M3 Godot 3D previewer | code committed, not run | Standalone scene with family/seed/style/equipment controls, orbit camera and OBJ export |
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

- **No claim of successful build/test yet.** The working environment does not have a .NET SDK or Godot executable available.
- OBJ is static rest-pose only. No skinning, animation, UV, PBR, continuous SDF surface or GLB.
- New equipment overlays share base rig and palette slots, are designed for 3D export and do not recompute pixel canvas.
- Existing generator's default behaviour is not modified. All new features are opt-in.
- New character gear is deliberately artistic/stylized; firing, damage and AI are outside the equipment module.

## Immediate next steps

1. Run `dotnet build tools/core_harness/CoreHarness.csproj` in .NET 8; fix any compiler errors.
2. Run `dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test all 1`; inspect baseline regressions.
3. Run `dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/scifi biped 12345 scifi`; inspect generated OBJ in Blender; verify shoulders, head, hand attachments and intersections.
4. Create Godot 3D preview scene, load material groups and expose style/seed controls.
5. Design GLB exporter and true skinning pipeline only after static mesh prototype passes geometry review.

## Continuation note

Do not start by modifying `BipedFamily` or original gene schema. Continue from this branch, run the build and report the first real test results before marking M1/M2 as validated. Use small, reusable components; prefer independent shape builders and style-pack registration over giant switches or scene-specific scripts.


## M3 preview scene added (2026-10-19)

- `examples/preview_3d/CreaturePreview3D.tscn` is a separate run-current-scene entry point; the existing `workshop/Main.tscn` remains the project start scene.
- `examples/preview_3d/CreaturePreview3D.cs` owns seed/style/equipment UI and regeneration flow.\n- `examples/preview_3d/PreviewStageBuilder.cs` owns environment, lighting, ground grid, pedestal and orbit camera construction.\n- `examples/preview_3d/ObjPreviewMeshBuilder.cs` parses the OBJ payload into indexed Godot surfaces and applies the sampled palette by material slot.
- UI includes family selector, text/numeric seed + random seed, regenerate, equipment toggle, Fantasy/Modern/Sci-Fi/Wasteland style selector, reset view and OBJ/MTL export.
- Modern/Sci-Fi/Wasteland use the separate biped equipment packs; other families fall back to Fantasy.
- Live meshes use the sampled palette by material slot. OBJ/MTL exports are saved to `user://exports/3d-preview/`.
- `examples/preview_3d/README.md` documents running and limitations.

**Validation remains pending:** neither Godot nor .NET SDK is installed in the current execution environment, and no GitHub Actions run/status was returned for the branch. Do not mark M3 complete until C# import/build, headless checks and visual inspection in Godot pass.
