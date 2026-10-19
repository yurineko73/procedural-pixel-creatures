// Converts the deterministic OBJ preview payload into Godot meshes and sampled palette materials.
// OBJ parsing is isolated here so the stage controller does not own geometry/topology details.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Palettes;

namespace PixelCreatures.Examples
{
    public static class ObjPreviewMeshBuilder
    {
        public static void ReplaceModelMeshes(Node3D modelRoot, string objText, CreaturePalette palette)
        {
            foreach (Node child in modelRoot.GetChildren())
            {
                modelRoot.RemoveChild(child);
                child.QueueFree();
            }

            var vertices = new List<Vector3>();
            var facesByMaterial = new Dictionary<MaterialSlot, List<int>>();
            MaterialSlot currentMaterial = MaterialSlot.Primary;

            foreach (string rawLine in objText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 4) continue;
                    vertices.Add(new Vector3(
                        float.Parse(fields[1], CultureInfo.InvariantCulture),
                        float.Parse(fields[2], CultureInfo.InvariantCulture),
                        float.Parse(fields[3], CultureInfo.InvariantCulture)));
                }
                else if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                {
                    string name = line.Substring(7).Trim();
                    if (Enum.TryParse(name, out MaterialSlot parsed)) currentMaterial = parsed;
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 4) continue;
                    if (!facesByMaterial.TryGetValue(currentMaterial, out var indices))
                    {
                        indices = new List<int>();
                        facesByMaterial[currentMaterial] = indices;
                    }
                    for (int i = 1; i < fields.Length; i++)
                    {
                        string indexText = fields[i].Split('/')[0];
                        int index = int.Parse(indexText, CultureInfo.InvariantCulture);
                        indices.Add(index > 0 ? index - 1 : vertices.Count + index);
                    }
                }
            }

            foreach (var entry in facesByMaterial.OrderBy(pair => (int)pair.Key))
            {
                var remap = new Dictionary<int, int>();
                var localVertices = new List<Vector3>();
                var localIndices = new List<int>();
                foreach (int sourceIndex in entry.Value)
                {
                    if (sourceIndex < 0 || sourceIndex >= vertices.Count)
                        throw new InvalidOperationException("OBJ contains an out-of-range vertex index.");
                    if (!remap.TryGetValue(sourceIndex, out int localIndex))
                    {
                        localIndex = localVertices.Count;
                        remap.Add(sourceIndex, localIndex);
                        localVertices.Add(vertices[sourceIndex]);
                    }
                    localIndices.Add(localIndex);
                }

                if (localVertices.Count < 3 || localIndices.Count < 3) continue;
                var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                foreach (Vector3 vertex in localVertices) surface.AddVertex(vertex);
                foreach (int index in localIndices) surface.AddIndex(index);
                surface.GenerateNormals();
                ArrayMesh mesh = surface.Commit();
                if (mesh == null || mesh.GetSurfaceCount() == 0) continue;

                modelRoot.AddChild(new MeshInstance3D
                {
                    Name = "Material_" + entry.Key,
                    Mesh = mesh,
                    MaterialOverride = CreateMaterial(entry.Key, palette),
                });
            }
        }

        private static StandardMaterial3D CreateMaterial(MaterialSlot slot, CreaturePalette palette)
        {
            Rgba rgba = palette.Color(slot, Shade.Base);
            var color = new Color(rgba.R / 255f, rgba.G / 255f, rgba.B / 255f);
            var material = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = slot == MaterialSlot.Metal ? 0.36f : 0.76f,
                Metallic = slot == MaterialSlot.Metal ? 0.72f : 0.02f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            if (slot == MaterialSlot.Glow)
            {
                material.EmissionEnabled = true;
                material.Emission = color;
                material.EmissionEnergyMultiplier = 1.7f;
            }
            return material;
        }
    }
}
