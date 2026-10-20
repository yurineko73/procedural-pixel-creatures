# 3D + modular equipment extension (experimental)

> Branch: `feature/3d-modular-equipment`. Original 2D framework and legacy genome seeds remain unchanged. This stage is an additive proof of concept, **not** a skinned game-ready 3D creature generator.

## What is implemented

- `examples/preview_3d/CreaturePreview3D.tscn`: standalone Godot 3D preview scene; does not replace `workshop/Main.tscn`.
- `examples/preview_3d/CreaturePreview3D.cs`: seed, family, equipment toggle, style selection and export controller.
- `examples/preview_3d/PreviewStageBuilder.cs`: reusable lighting / ground / pedestal / orbit-camera stage.
- `examples/preview_3d/ObjPreviewMeshBuilder.cs`: OBJ-to-Godot indexed surfaces, face-feature material mapping and a six-level palette-ramp toon shader.

- `Core/Export/ObjMeshExporter.cs`: engine-independent **rest-pose OBJ + MTL** output from `CreatureAnatomy` primitives (ellipsoids, round cones, triangles) plus separately stored facial features (eye sockets, sclera, iris/pupil, highlights, nostrils and spots), with deterministic vertex order, material-slot grouping and unit scaling.
- `Core/Equipment/EquipmentPacks.cs`: a separate registry and deterministic `EquipmentGenerator.Apply` overlay. `EquipmentSeedSet` exposes independent `Outfit`, `Weapon`, `Headgear` and `Accessory` streams.
- `Core/Equipment/NativeGearRandomizer.cs`: applies independent seeds to the legacy Fantasy gear genes and keeps clothing/metal colour genes tied to the master equipment seed, without changing the BipedFamily schema. Bundled styles: `modern` (jacket/vest, accessories, multiple headgear variants, fictional sidearm prop), `scifi` (breastplate, light core, shoulders, variable helmet, fictional energy blaster prop), and `wasteland` (coat, randomized scrap plates, bandana/scrap helmet/respirator, tool prop).
- `tools/core_harness` command `obj`: export an existing creature, optionally with additional geometry from a style pack.
- Engine-independent tests for deterministic OBJ generation, face-feature materials, nonempty meshes, overlay slot isolation, native Fantasy gear slot isolation, and no source anatomy mutation.

## Try it (requires .NET SDK 8)

From repository root:

```shell
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/obj biped 12345 scifi
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/modern biped 12345 modern
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/original quadruped 12345 fantasy
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test export.obj_static_all_families 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test export.obj_two_sided_triangle_normals 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test export.obj_unifies_organic_primitives 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test equipment.style_overlay_deterministic 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test equipment.slot_seeds_are_independent 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test equipment.native_gear_slot_seeds_are_independent 1
```

The CLI outputs `creature.obj` and `creature.mtl` to the output directory. Load them together in Blender or a compatible OBJ viewer. `fantasy` is a no-op pass-through: the original fantasy clothes and weapons, if any, remain as built by the source family.

For C# integration:

```csharp
var registry = CreatureRegistry.CreateDefault();
var family = registry.GetFamily("biped");
var genome = GenomeFactory.Sample(family, 12345UL)
    .With("gear.weapon", 0).With("gear.outfit", 0).With("gear.helmet", 0)
    .With("gear.shield", 0).With("gear.pauldrons", 0);
var body = family.BuildAnatomy(genome, registry);
var decorated = EquipmentGenerator.Apply(body, "scifi", 12345UL);
var output = ObjMeshExporter.Export(decorated);
File.WriteAllText("creature.obj", output.ObjText);
File.WriteAllText("creature.mtl", output.MtlText);
```

Use relevant namespaces `PixelCreatures.Core`, `PixelCreatures.Core.Genetics`, `PixelCreatures.Core.Equipment`, and `PixelCreatures.Core.Export`.

## Extension design

```text
Existing Genome -> CreatureAnatomy -> pixel pipeline [unchanged]
                              |
                              +-> NativeGearRandomizer.ApplyNativeGearGenes(baseGenome, family, masterSeed, slotSeeds)
                              +-> EquipmentGenerator.Apply(baseAnatomy, stylePack, slotSeeds)
                                         |
                                         +-> IEquipmentStylePack registry
                                         +-> derived seed per piece
                                         +-> bone-attached primitive overlays
                                                    |
                                                    +-> ObjMeshExporter -> OBJ/MTL
```

Register more styles by implementing `IEquipmentStylePack` and calling `EquipmentStyleRegistry.Register`. Keep style geometry isolated; avoid editing the original `BipedFamily` or widening its schema until schema migration and golden-test compatibility are designed.

## Explicit limitations

1. OBJ is a **static rest-pose surface export** without glTF, skeletal skinning, animation clips, UVs or PBR materials.
2. Each ellipsoid, round-cone and triangle is triangulated independently. The original 2D soft-union shading is **not** a real 3D boolean/SDF union: overlapping shells and seams are expected. Round cones are represented by simple capped frustums.
3. Generated OBJ does not carry original pixel patterns or genome colours. The MTL is a **placeholder neutral palette**.
4. Overlays attach shapes to **existing** humanoid bones; they do not include cloth simulation, collision checking, two-hand grips, or game combat logic. The three new packs currently require a biped rig.
5. The overlay reuses the original pixel canvas bounds intentionally and is **for 3D export only**. Do not pass the decorated anatomy to the existing pixel renderer without recomputing canvas bounds; it can clip.
6. Fantasy gear uses the legacy shared `gear.size` gene; per-slot random buttons reroll category choices independently, while the shared scale changes only with the master equipment seed.
7. The toon shader approximates the old palette ramp, but does not reproduce the original 2D renderer's pixel contours, surface patterns, soft unions, contact shading or animation expressions.
8. Existing fantasy dress/weapon shapes are still created when the sampled original genome has those genes. The CLI suppresses them for themed previews. The independent API caller must do the same when desired.
9. The code has not yet been validated by executing the .NET/Godot test runner in this environment; local build/test verification is required before merging.

## Next work

- Validate the standalone Godot preview scene (`examples/preview_3d/CreaturePreview3D.tscn`) with seed/family/style/equipment controls, orbit camera and OBJ export; fix any import/build/runtime issues before calling M3 complete.
- Convert intersecting primitives into continuous game-ready meshes (SDF extraction / remeshing).
- Generate proper fitted clothing mesh topology, UVs, normals, material mapping, runtime collision and accessory sockets.
- Add Skeleton3D / skin weighting / glTF & GLB export / motion baking; verify rest pose and animated poses.
- Add style-pack compatibility checks, custom wearable slots and more varied morphology-driven equipment silhouettes.

## Scope & safety

Weapons are **fictional videogame visual props**, not real-world mechanical designs. Damage, firing and gameplay interactions must be implemented separately.


## Godot preview shading / triangle normals

The custom `light()` shader processor uses a guarded block rather than `return;` (Godot rejects early returns in this processor). Two-sided triangle exports duplicate the reverse face with a separate vertex triplet; sharing indices for opposite faces makes generated normals cancel. The preview keeps back-face culling enabled to avoid depth fighting between coincident front and reverse faces. This source fix is committed; rebuild and visual inspection in Godot 4.7.1 are still required.


## Static 3D preview: organic surface union

The static exporter now routes eligible ellipsoid and tapered-segment primitives using Primary/Secondary skin materials through `ImplicitBodyMesher`. The mesher evaluates an approximate signed-distance field, applies a small smooth minimum around overlaps, and emits a shared outer surface with material regions. Gear-prefixed groups, interior groups, hard/faceted primitives, triangles, and other material slots remain independent. If meshing produces no faces, the exporter falls back to the original primitive geometry.

The preview toon shader shades primarily by scaling each material's base hue rather than applying the full 2D palette hue shift to every light band; fixed-shade facial details retain their intended color. Ambient lighting is enabled to avoid black unlit surfaces. These changes have source regression coverage, but still need a Godot 4.7.1 rebuild and visual validation.


## Opaque preview surface and garment fusion follow-up (2026-10-19)

The static 3D exporter now fuses eligible organic skin primitives into a continuous signed-distance surface and fuses selected soft garment groups separately. Clothing groups include tunic, undercoat, breastplate, robe, skirt, and loincloth flaps; the group boundaries preserve equipment layering, and hard/faceted/thin details remain standalone. Garment meshing uses a smaller smooth-union radius and finer voxel step than body skin.

To reduce surface holes, the SDF field no longer switches between hard-min and smooth-min at a distance threshold; extracted triangles are oriented against the field gradient, and intersections landing exactly on sampled grid points share a single cached vertex. MeshData reports boundary-edge and non-manifold-edge counts. The Godot preview shader explicitly requests `depth_draw_opaque` and `cull_disabled`, so reversed triangles are not simply culled and the surfaces remain depth-writing opaque. Biped cover offsets were increased slightly to keep cloth/armour outside the fused body.

Regression coverage now checks a closed body manifold and verifies garment primitive shells are replaced by one garment surface. These are source-level checks only until the CoreHarness test and Godot 4.7.1 preview are actually run.

The preview mesh builder calculates shared source-vertex normals across all material groups before splitting them into Godot surfaces, reducing artificial shading seams along skin color transitions.


## P0 preview stabilization modules (2026-10-20)

`PreviewAttachmentRepair`: surface-repair/omit rules for tiny claws/teeth/fangs, restricted to a valid same-bone host ellipsoid. This avoids general connected-component deletion of intentional floating objects such as particles or separate equipped pieces. Clones are used; original anatomy is never mutated.

`PreviewEquipmentSockets`: export-only grip/forearm placement for weapon and shield object groups. Uses rest-pose hand/forearm transforms and bounded scaling, keeping equipment physically closer to its intended anchor on unusual biped proportions. No additional runtime animation bones or new skeleton schema.

`PreviewMeshCleaner`: union-find micro-island filter on SDF surface meshes only. Removes only very small and disconnected artifacts by face-count and world-space extent; it does not delete discrete raw weapon, claw or face objects. `PreviewGarmentFit`: narrow-waist/long-leg proportion safeguard that replaces unstable hanging skirt panels with a short fitted cloth hem for static preview. Both preserve deterministic Seed behavior.

`EquipmentBuildContext.Cover`: now inherits the referenced chest ellipsoid's `LocalRotation`, so fitted modern/scifi overlays follow body orientation.

CoreHarness regressions: `export.preview_attached_claws_and_teeth`, `export.preview_weapon_hand_socket`, `export.preview_extreme_garment_fallback`, `export.preview_tiny_sdf_island_cleanup`.

**Current validation:** code committed but no local .NET SDK / Godot runtime available in this execution environment; visual and compiled test results are pending. Do not claim verified wearables or collision-free procedural clothing yet. Equipment variation expansion will follow after this stabilization pass is accepted.


## Screenshot-driven repair follow-up (2026-10-20)

The latest screenshots exposed three upstream exporter issues in addition to attachment placement:
- Soft Accent/White anatomy and Faceted core primitives were emitted as separate shells, exposing internal intersections. The skin union now includes soft core accents and faceted anatomy while continuing to keep hard/thin/translucent/front-only pieces, gear and isolated appendage groups separate.
- Surface material selection used absolute SDF distance, allowing buried shapes to win material assignment. Deeply buried candidates are now penalized.
- Tiny-island cleanup previously ran on garment groups as well as skin; garment panels are now preserved.

Attachment repair now searches a source bone and its ancestors and can project against ellipsoids or round-cone limb segments. Long weapon scaling uses actual floor clearance instead of a fixed minimum size. Caps/helmets are raised above the eye line, hoods are shifted backward, and pauldrons are placed relative to the shoulder frame.

Additional CoreHarness tests cover ancestor-segment claw attachment, soft Accent/Faceted skin fusion, helmet eye clearance, and weapon floor clearance. All are **pending execution** because this environment cannot launch the project's .NET/Godot toolchain. The current GitHub branch also does not contain the `PlantfolkFamily` source shown in the screenshots; shared exporter changes are family-agnostic, but plantfolk-specific generator fixes remain blocked on that source being checked in.


## P0 eye and appendage export rules (2026-10-20)

Static 3D export now enforces semantic separation of standalone appendage groups irrespective of material slot, avoiding the earlier Primary/Secondary vs Accent/White union inconsistency. Eye features use `PreviewFeatureFit` to cap stamp-derived 3D mesh sizes to a same-bone host ellipsoid, optionally add a connected backing bulb for a bare eye-stalk on an eligible antenna/tentacle bone, and apply a bounded host-surface correction to **all** eye layers together. Distant features are not teleported to unrelated geometry. Tiny unsupported claws/teeth are dropped at export rather than left floating; source Genome/Anatomy remains unchanged.

Regression additions include multi-family seeds from user screenshots, semantic appendage preservation, eye-stalk support and bounded whole-eye projection. **No .NET / Godot runtime verification has been performed yet.** The branch is ready for local compilation and front/side/rear screenshot comparison; do not mark eye fitting or attachment behavior as visually validated until then.


## Legacy-authored semantic geometry fit (2026-10-20)

The original 2D blocks intentionally create upper teeth from the head's underside `-Y`, paw claws near the paw's `+X` silhouette, and weapons on hand-local `(0.12,1,0)`. Those authored points are not generic 3D surface projections. The static OBJ exporter now routes these elements through independent anatomy-aware modules:

- `PreviewDentalFit`: upper lip placement from the actual `.snout` tapered segment on the same head bone, capped exposed length for a closed mouth; lower jaw tusks remain controlled by their own jaw-local coordinates.
- `PreviewClawFit`: attach nails/claws to the same-bone `.foot/.paw/.palm` front surface, preserving the forward axis and modest downward pitch; fall back to ancestor repair when an arthropod/jointed limb has no paw.
- `PreviewEquipmentSockets`: rotate a *whole grouped weapon* from its original hand-local shaft direction to a type-appropriate rest vector, about its original hand grip. No change to weapon-seed selection or 2D construction.
- `ObjMeshExporter`: exclusive semantic dispatch prevents generic nearest-ellipsoid repair from undoing intentional mouth/toe positions.

Hand claws now have explicit tags, consistent with foot claws, to identify their parent. The new modules operate only on exported primitive clones/coordinates and leave original 2D genome/anatomy layout intact.

Regressions: `export.preview_dental_lip_fit`, `export.preview_claw_toe_fit`, `export.preview_fixed_user_seeds` (Body=12345, Equipment=54321). **All pending executable test / Godot rendering verification**.


## 3D face topology and segmented-limb specialization (2026-10-20)

2D-to-3D mismatch: the old `.mouthInterior` is intentionally hidden when the jaw is closed, while the 3D exporter had no active jaws or visible mouth cut. Merely limiting teeth length cannot produce a believable face if no oral opening exists. The static 3D body mesher now derives a shallow subtractive elliptical slit from the actual head snout and jaw bone metadata; the resulting cavity wall is assigned `MaterialSlot.Mouth`. The shape is currently a *static closed-mouth slit*, not a skinned animated open-mouth cavity. Underlying `HeadSpec`, mouth bones and 2D rendering remain untouched.

`PreviewHeadAttachmentFit`: skull-based per-ear-group root embed, applying one rigid adjustment to both `ear.outer` and `ear.inner` primitives. Other head ornaments are not reattached until given explicit semantic bone definitions.

`PreviewSegmentChainFit`: uses the source three-segment `LegRig` hierarchy for arthropod legs instead of globally sampled coarse SDF. Creates tubes at source rest-pose joint coordinates and overlap spheres at chain joints, leaving isolated non-leg appendages unaffected. Only complete tagged chains use this path; incomplete groups fall back.

New regressions: `export.preview_visible_mouth_cavity`, `export.preview_ears_embed_in_skull`, `export.preview_arthropod_leg_chain`. No runtime green build/visual approval yet; use actual reported fixed seeds, and only accept P0 once mouth, teeth, ears and arthropod connections are verified across viewpoints.


## 2026-10-20 — Visual rejection and root-cause audit

The latest 3D screenshot set rejects the current fixes: the mouth is misplaced beneath the muzzle, teeth still look unsupported, paw claws/toes look detached, horn-like points are capped cone shapes with weak skull roots, and a horn can cover an eye.

Main and feature branches have identical source for `Heads.cs` and `Appendages.cs`. The original builders use simple cone primitives for teeth, ear segments and horn chains, but the old renderer is not equivalent to an OBJ primitive dump. `Heads.cs` explicitly puts the upper tooth row inside the upper lip; mouth interior is attached to the head in a location computed from hinge/chin. `CreatureRenderer.CleanupInterior()` peels exposed pixels from `GroupFlags.Interior` so the closed mouth does not leak. The 3D static exporter lacks this cleanup and jaw animation.

The previous `BuildMouthCutters` implementation was a failed heuristic: it checks for mouth metadata but then chooses an arbitrary point at 70% of the snout and shifts it downward, instead of using the original hinge/chin seam. `PreviewHeadAttachmentFit` fits only ear-bone groups, not horns. `PreviewClawFit` shifts a claw but does not guarantee a watertight union with the paw. These are symptoms of missing 3D semantic topology rather than isolated offsets.

Required redesign before more feature work:
- Replace the guessed mouth cut with a jaw/snout/lip/cavity assembly driven by explicit head metadata.
- Mesh curved horns/ears as continuous tapered geometry, embed the root into the cranium, and enforce clearance from eye features.
- Union/embed claws into paw surfaces and verify multi-view contact.
- Keep 2D rendering untouched; add real Godot visual regression renders for the user's exact seeds. Do not mark these fixes as passed until inspected.


## 2026-10-20 — First direct fixes for 3D conversion defects

Kept the existing 2D anatomy and adjusted only the static 3D export:
- Mouth subtraction now follows the original `.mouthInterior` ellipsoid instead of a guessed point at 70% of the snout plus a downward offset.
- Upper teeth are shorter and anchored closer to the muzzle axis so they enter the mouth cavity instead of hanging below it.
- Head attachment fitting now covers horns as well as ears, embeds horn roots more deeply to hide flat caps, and applies a small group translation when a horn approaches an eye.
- Claw roots are moved further inside their matching paw/foot/palm.
- Added regression coverage for mouth position, horn roots/eye clearance, and both user screenshot seed pairs.

These are targeted corrections, not a redesign of the 2D generator. Tests have been added but are not yet executed; no GitHub Actions/check-run results are available for the current commit. Visual approval in Godot is still pending.

