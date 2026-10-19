# 3D + modular equipment extension (experimental)

> Branch: `feature/3d-modular-equipment`. Original 2D framework and legacy genome seeds remain unchanged. This stage is an additive proof of concept, **not** a skinned game-ready 3D creature generator.

## What is implemented

- `Core/Export/ObjMeshExporter.cs`: engine-independent **rest-pose OBJ + MTL** output from `CreatureAnatomy` three-dimensional primitive definitions (ellipsoids, round cones, triangles), with deterministic vertex order, material-slot grouping and unit scaling.
- `Core/Equipment/EquipmentPacks.cs`: a separate registry and deterministic `EquipmentGenerator.Apply` overlay. Bundled styles: `modern` (jacket, optional vest/backpack/cap, fictional sidearm prop), `scifi` (breastplate, light core, shoulders, helmet, fictional energy blaster prop), and `wasteland` (coat, randomized scrap plates, tool prop).
- `tools/core_harness` command `obj`: export an existing creature, optionally with additional geometry from a style pack.
- Engine-independent tests for deterministic OBJ generation, nonempty meshes, equipment generation, and no source anatomy mutation.

## Try it (requires .NET SDK 8)

From repository root:

```shell
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/obj biped 12345 scifi
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/modern biped 12345 modern
dotnet run --project tools/core_harness/CoreHarness.csproj -- obj out/original quadruped 12345 fantasy
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test export.obj_static_all_families 1
dotnet run --project tools/core_harness/CoreHarness.csproj -- test out/test equipment.style_overlay_deterministic 1
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
                              +-> EquipmentGenerator.Apply(base, pack, seed)
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
6. Existing fantasy dress/weapon shapes are still created when the sampled original genome has those genes. The CLI suppresses them for themed previews. The independent API caller must do the same when desired.
7. The code has not yet been validated by executing the .NET/Godot test runner in this environment; local build/test verification is required before merging.

## Next work

- Add Godot 3D preview scene and deterministic seed/style controls.
- Convert intersecting primitives into continuous game-ready meshes (SDF extraction / remeshing).
- Generate proper fitted clothing mesh topology, UVs, normals, material mapping, runtime collision and accessory sockets.
- Add Skeleton3D / skin weighting / glTF & GLB export / motion baking; verify rest pose and animated poses.
- Add style-pack compatibility checks, custom wearable slots and more varied morphology-driven equipment silhouettes.

## Scope & safety

Weapons are **fictional videogame visual props**, not real-world mechanical designs. Damage, firing and gameplay interactions must be implemented separately.
