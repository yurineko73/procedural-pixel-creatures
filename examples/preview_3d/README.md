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
- Enter a numeric or text seed; press Enter or **Regenerate** to reproduce that creature.
- **Random** writes a new numeric seed into the field.
- Enable/disable equipment.
- Select Fantasy (the family's original procedural gear), Modern, Sci-Fi, or Wasteland.
- Drag the model area to orbit; use the mouse wheel to zoom; reset the camera if needed.
- **Export OBJ + MTL** writes the current preview to `user://exports/3d-preview/` and opens that directory.

The three new equipment packs currently support the `biped` family. Selecting another family automatically falls back to Fantasy.

## What this prototype renders

The preview uses the current creature's base palette and converts the rest-pose primitives into Godot triangle meshes. Material slots are split into separate mesh instances. The world includes a simple pedestal, ground grid, environment lighting and an orbit camera.

This is a geometry proof of concept, not a game-ready rig:
- no Skeleton3D skinning or animation;
- no UV textures or faithful reproduction of pixel patterns;
- no 3D boolean/SDF union, so primitive intersections and seams are expected;
- the OBJ/MTL export uses placeholder material definitions even though the live preview uses the sampled creature palette.

If the preview does not open, first run the project's normal C# import/build and headless checks. The preview and its equipment styles have not been marked as validated until those checks and a visual inspection in Godot/Blender pass.
