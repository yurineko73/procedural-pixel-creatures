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
            var facesByMaterial = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            string currentMaterial = nameof(MaterialSlot.Primary);

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
                    currentMaterial = line.Substring(7).Trim();
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

            // Calculate normals from the complete OBJ before splitting by material. Otherwise
            // each material surface gets independent normals at shared skin boundaries, producing
            // harsh seams that look like cuts through the body.
            var sourceNormals = new Vector3[vertices.Count];
            foreach (var faceIndices in facesByMaterial.Values)
            {
                for (int i = 0; i + 2 < faceIndices.Count; i += 3)
                {
                    int a = faceIndices[i], b = faceIndices[i + 1], c = faceIndices[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count)
                        throw new InvalidOperationException("OBJ contains an out-of-range vertex index.");
                    Vector3 normal = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
                    if (normal.LengthSquared() < 1e-10f) continue;
                    sourceNormals[a] += normal;
                    sourceNormals[b] += normal;
                    sourceNormals[c] += normal;
                }
            }

            foreach (var entry in facesByMaterial.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var remap = new Dictionary<int, int>();
                var localVertices = new List<Vector3>();
                var localSourceIndices = new List<int>();
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
                        localSourceIndices.Add(sourceIndex);
                    }
                    localIndices.Add(localIndex);
                }

                if (localVertices.Count < 3 || localIndices.Count < 3) continue;
                var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = 0; i < localVertices.Count; i++)
                {
                    Vector3 normal = sourceNormals[localSourceIndices[i]];
                    surface.SetNormal(normal.LengthSquared() > 1e-10f ? normal.Normalized() : Vector3.Up);
                    surface.AddVertex(localVertices[i]);
                }
                foreach (int index in localIndices) surface.AddIndex(index);
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

        private static readonly string[] RampUniforms =
        {
            "ramp_outline", "ramp_deep", "ramp_shadow", "ramp_base", "ramp_light", "ramp_highlight",
        };

        private static Shader? _toonShader;

        private static Shader GetToonShader()
        {
            if (_toonShader != null) return _toonShader;
            _toonShader = new Shader
            {
                Code = @"shader_type spatial;
render_mode specular_disabled, cull_disabled, depth_draw_opaque;

uniform vec4 ramp_outline : source_color = vec4(0.04, 0.03, 0.05, 1.0);
uniform vec4 ramp_deep : source_color = vec4(0.12, 0.10, 0.15, 1.0);
uniform vec4 ramp_shadow : source_color = vec4(0.28, 0.25, 0.32, 1.0);
uniform vec4 ramp_base : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform vec4 ramp_light : source_color = vec4(0.75, 0.75, 0.75, 1.0);
uniform vec4 ramp_highlight : source_color = vec4(0.95, 0.95, 0.95, 1.0);
uniform bool use_emission = false;
uniform float emission_strength = 0.0;
uniform bool use_fixed_shade = false;

void fragment() {
    ALBEDO = ramp_base.rgb;
    ROUGHNESS = 0.92;
    if (use_emission) {
        EMISSION = ramp_light.rgb * emission_strength;
    }
}

void light() {
    float n_dot_l = max(dot(NORMAL, LIGHT), 0.0);
    if (use_fixed_shade) {
        DIFFUSE_LIGHT += ramp_base.rgb * LIGHT_COLOR * ATTENUATION * 0.52;
    } else {
        // Use the material's base hue for the main ramp. The 2D palette deliberately
        // shifts shadows toward violet and highlights toward yellow; applying those
        // shifts at full strength to 3D lighting made every curved part look striped.
        vec3 band = ramp_base.rgb * 0.34;
        if (n_dot_l >= 0.20) band = mix(ramp_base.rgb * 0.58, ramp_shadow.rgb, 0.12);
        if (n_dot_l >= 0.42) band = ramp_base.rgb;
        if (n_dot_l >= 0.68) band = mix(min(ramp_base.rgb * 1.14, vec3(1.0)), ramp_light.rgb, 0.12);
        if (n_dot_l >= 0.88) band = mix(min(ramp_base.rgb * 1.27, vec3(1.0)), ramp_highlight.rgb, 0.16);
        DIFFUSE_LIGHT += band * LIGHT_COLOR * ATTENUATION * 0.52;
    }
}"
            };
            return _toonShader;
        }

        private static ShaderMaterial CreateMaterial(string materialName, CreaturePalette palette)
        {
            (MaterialSlot slot, int fixedShade, bool emissive) = ResolveMaterial(materialName);
            var material = new ShaderMaterial { Shader = GetToonShader() };
            for (int shade = 0; shade < Shade.Count; shade++)
            {
                int paletteShade = fixedShade >= 0 ? fixedShade : shade;
                Rgba rgba = palette.Color(slot, paletteShade);
                material.SetShaderParameter(RampUniforms[shade],
                    new Color(rgba.R / 255f, rgba.G / 255f, rgba.B / 255f, rgba.A / 255f));
            }
            material.SetShaderParameter("use_emission", emissive);
            material.SetShaderParameter("emission_strength", emissive ? 0.55f : 0.0f);
            material.SetShaderParameter("use_fixed_shade", fixedShade >= 0);
            return material;
        }

        private static (MaterialSlot Slot, int FixedShade, bool Emissive) ResolveMaterial(string name)
        {
            switch (name)
            {
                case "FeatureOutline": return (MaterialSlot.Primary, Shade.Outline, false);
                case "FeatureSocket": return (MaterialSlot.Primary, Shade.Deep, false);
                case "FeatureSclera": return (MaterialSlot.Teeth, Shade.Base, false);
                case "FeatureIris": return (MaterialSlot.Eye, Shade.Base, false);
                case "FeaturePupil": return (MaterialSlot.Primary, Shade.Outline, false);
                case "FeatureHighlight": return (MaterialSlot.Teeth, Shade.Highlight, false);
                case "FeatureGlow": return (MaterialSlot.Glow, Shade.Light, true);
                case "FeatureNostril": return (MaterialSlot.Primary, Shade.Deep, false);
            }

            if (Enum.TryParse(name, out MaterialSlot slot) && slot != MaterialSlot.Count)
                return (slot, -1, slot == MaterialSlot.Glow);
            return (MaterialSlot.Primary, -1, false);
        }
    }
}
