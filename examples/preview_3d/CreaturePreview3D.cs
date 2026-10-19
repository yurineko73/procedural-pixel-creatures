// Standalone Godot 3D preview for procedural creatures and modular equipment.
// Open examples/preview_3d/CreaturePreview3D.tscn and run it (F6).
// Geometry is generated from the engine-independent rest-pose OBJ backend; no external assets required.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Equipment;
using PixelCreatures.Core.Export;
using PixelCreatures.Core.Genetics;
using PixelCreatures.Core.Palettes;
using PixelCreatures.Runtime;
using PixelCreatures.Workshop;

namespace PixelCreatures.Examples
{
    public partial class CreaturePreview3D : Node3D
    {
        private const float SidebarWidth = 366f;
        private const double PreviewUnitsPerPixel = 0.04;
        private static readonly (string Id, string Label)[] Styles =
        {
            ("fantasy", "奇幻（原生装备）"),
            ("modern", "现代"),
            ("scifi", "科幻"),
            ("wasteland", "废土"),
        };

        private readonly List<string> _familyIds = new List<string>();
        private Node3D _modelRoot = null!;
        private Node3D _cameraRig = null!;
        private Camera3D _camera = null!;
        private OptionButton _familySelector = null!;
        private OptionButton _styleSelector = null!;
        private LineEdit _seedInput = null!;
        private CheckBox _equipmentToggle = null!;
        private Label _status = null!;
        private Label _seedResolved = null!;
        private Button _exportButton = null!;
        private string _lastObj = string.Empty;
        private string _lastMtl = string.Empty;
        private string _lastExportSummary = string.Empty;
        private bool _dragging;
        private float _yaw = 0.58f;
        private float _pitch = 0.12f;
        private float _cameraDistance = 6.5f;

        public override void _Ready()
        {
            BuildWorld();
            BuildUi();
            GeneratePreview();
        }

        private void BuildWorld()
        {
            var world = new WorldEnvironment();
            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.055f, 0.067f, 0.09f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.57f, 0.62f, 0.72f),
                AmbientLightEnergy = 0.72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true,
                GlowIntensity = 0.35f,
            };
            world.Environment = environment;
            AddChild(world);

            var key = new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-43, -32, 0),
                LightColor = new Color(1.0f, 0.91f, 0.78f),
                LightEnergy = 1.25f,
                ShadowEnabled = true,
                ShadowOpacity = 0.38f,
            };
            AddChild(key);

            var fill = new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-20, 145, 0),
                LightColor = new Color(0.46f, 0.68f, 1.0f),
                LightEnergy = 0.62f,
                ShadowEnabled = false,
            };
            AddChild(fill);

            var floorMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.075f, 0.09f, 0.12f),
                Roughness = 0.92f,
            };
            var floor = new MeshInstance3D
            {
                Name = "PreviewFloor",
                Mesh = new PlaneMesh { Size = new Vector2(80, 80) },
                MaterialOverride = floorMaterial,
                Position = new Vector3(0, -0.19f, 0),
            };
            AddChild(floor);

            var gridMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.18f, 0.22f, 0.29f, 0.58f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = false,
            };
            var gridMesh = new ImmediateMesh();
            gridMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, gridMaterial);
            for (int i = -12; i <= 12; i++)
            {
                float p = i * 0.75f;
                gridMesh.SurfaceAddVertex(new Vector3(p, -0.185f, -9));
                gridMesh.SurfaceAddVertex(new Vector3(p, -0.185f, 9));
                gridMesh.SurfaceAddVertex(new Vector3(-9, -0.185f, p));
                gridMesh.SurfaceAddVertex(new Vector3(9, -0.185f, p));
            }
            gridMesh.SurfaceEnd();
            AddChild(new MeshInstance3D { Name = "PreviewGrid", Mesh = gridMesh });

            var pedestal = new MeshInstance3D
            {
                Name = "DisplayPedestal",
                Mesh = new CylinderMesh { TopRadius = 1.05f, BottomRadius = 1.18f, Height = 0.16f, RadialSegments = 48 },
                Position = new Vector3(0, -0.08f, 0),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.15f, 0.18f, 0.23f),
                    Metallic = 0.38f,
                    Roughness = 0.42f,
                },
            };
            AddChild(pedestal);

            _modelRoot = new Node3D { Name = "GeneratedCreature" };
            AddChild(_modelRoot);

            _cameraRig = new Node3D { Name = "OrbitRig", Position = new Vector3(0, 1.0f, 0) };
            AddChild(_cameraRig);
            _camera = new Camera3D
            {
                Name = "PreviewCamera",
                Position = new Vector3(0, 0, _cameraDistance),
                Current = true,
                Fov = 42,
                Near = 0.03f,
                Far = 100,
            };
            _cameraRig.AddChild(_camera);
            UpdateCameraRig();
        }

        private void BuildUi()
        {
            var layer = new CanvasLayer { Layer = 10 };
            AddChild(layer);
            var root = new Control { Name = "PreviewOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.Theme = UiKit.CreateTheme();
            layer.AddChild(root);

            var panel = new PanelContainer { Name = "ControlsPanel" };
            panel.AnchorLeft = 0;
            panel.AnchorRight = 0;
            panel.AnchorTop = 0;
            panel.AnchorBottom = 1;
            panel.OffsetLeft = 18;
            panel.OffsetRight = SidebarWidth - 16;
            panel.OffsetTop = 18;
            panel.OffsetBottom = -18;
            var panelStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.075f, 0.083f, 0.11f, 0.96f),
                BorderColor = new Color(0.28f, 0.32f, 0.4f, 0.95f),
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10,
            };
            panel.AddThemeStyleboxOverride("panel", panelStyle);
            root.AddChild(panel);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 16);
            margin.AddThemeConstantOverride("margin_right", 16);
            margin.AddThemeConstantOverride("margin_top", 16);
            margin.AddThemeConstantOverride("margin_bottom", 16);
            panel.AddChild(margin);

            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
            margin.AddChild(scroll);
            var column = new VBoxContainer();
            column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            column.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(column);

            var eyebrow = new Label { Text = "PROCEDURAL WORKSHOP" };
            eyebrow.AddThemeFontSizeOverride("font_size", 10);
            eyebrow.AddThemeColorOverride("font_color", new Color(0.76f, 0.62f, 0.38f));
            column.AddChild(eyebrow);

            var title = new Label { Text = "3D 生物预览" };
            title.AddThemeFontSizeOverride("font_size", 24);
            title.AddThemeColorOverride("font_color", new Color(0.94f, 0.94f, 0.91f));
            column.AddChild(title);

            var intro = new Label
            {
                Text = "使用同一套程序化骨架数据生成静态网格。切换种子、装备与外观风格，查看几何组合效果。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            intro.AddThemeFontSizeOverride("font_size", 12);
            intro.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
            column.AddChild(intro);
            column.AddChild(new HSeparator());

            column.AddChild(SectionLabel("01  生物与 Seed"));
            column.AddChild(FieldLabel("生物家族"));
            _familySelector = new OptionButton { CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var family in CreatureRuntime.Registry.Families)
            {
                _familyIds.Add(family.Id);
                _familySelector.AddItem(family.Id);
            }
            int bipedIndex = _familyIds.FindIndex(id => string.Equals(id, "biped", StringComparison.OrdinalIgnoreCase));
            _familySelector.Selected = bipedIndex >= 0 ? bipedIndex : 0;
            _familySelector.ItemSelected += _ =>
            {
                EnsureStyleCompatibility();
                GeneratePreview();
            };
            column.AddChild(_familySelector);

            column.AddChild(FieldLabel("Seed（数字或任意文本）"));
            var seedRow = new HBoxContainer();
            seedRow.AddThemeConstantOverride("separation", 6);
            _seedInput = new LineEdit
            {
                Text = "12345",
                PlaceholderText = "输入 seed…",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34),
                ClearButtonEnabled = true,
            };
            _seedInput.TextSubmitted += _ => GeneratePreview();
            seedRow.AddChild(_seedInput);
            var randomSeed = new Button { Text = "随机", CustomMinimumSize = new Vector2(64, 34), TooltipText = "生成一个新 Seed" };
            randomSeed.Pressed += () =>
            {
                _seedInput.Text = Math.Max(1u, GD.Randi()).ToString(CultureInfo.InvariantCulture);
                GeneratePreview();
            };
            seedRow.AddChild(randomSeed);
            column.AddChild(seedRow);

            var generate = new Button
            {
                Text = "重新生成",
                CustomMinimumSize = new Vector2(0, 38),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            generate.Pressed += GeneratePreview;
            column.AddChild(generate);
            _seedResolved = new Label { Text = "解析后的 Seed：12345" };
            _seedResolved.AddThemeFontSizeOverride("font_size", 11);
            _seedResolved.AddThemeColorOverride("font_color", new Color(0.61f, 0.67f, 0.76f));
            column.AddChild(_seedResolved);

            column.AddChild(new HSeparator());
            column.AddChild(SectionLabel("02  装备与风格"));
            _equipmentToggle = new CheckBox { Text = "启用装备", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
            _equipmentToggle.Toggled += _ => GeneratePreview();
            column.AddChild(_equipmentToggle);

            column.AddChild(FieldLabel("装备风格"));
            _styleSelector = new OptionButton { CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var style in Styles) _styleSelector.AddItem(style.Label);
            _styleSelector.Selected = 0;
            _styleSelector.ItemSelected += _ =>
            {
                EnsureStyleCompatibility();
                GeneratePreview();
            };
            column.AddChild(_styleSelector);

            var styleHint = new Label
            {
                Text = "奇幻使用原生随机装备；现代、科幻、废土使用独立装备包。后面三种目前只适配双足骨架。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            styleHint.AddThemeFontSizeOverride("font_size", 11);
            styleHint.AddThemeColorOverride("font_color", new Color(0.63f, 0.67f, 0.73f));
            column.AddChild(styleHint);

            column.AddChild(new HSeparator());
            column.AddChild(SectionLabel("03  当前模型"));
            _status = new Label
            {
                Text = "正在生成…",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(0, 80),
            };
            _status.AddThemeFontSizeOverride("font_size", 12);
            column.AddChild(_status);

            _exportButton = new Button
            {
                Text = "导出 OBJ + MTL",
                CustomMinimumSize = new Vector2(0, 38),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = "保存当前静态 3D 网格到 user://exports/3d-preview/",
            };
            _exportButton.Pressed += ExportCurrentModel;
            column.AddChild(_exportButton);

            var resetCamera = new Button { Text = "重置视角", CustomMinimumSize = new Vector2(0, 32) };
            resetCamera.Pressed += () =>
            {
                _yaw = 0.58f;
                _pitch = 0.12f;
                _cameraDistance = 6.5f;
                UpdateCameraRig();
            };
            column.AddChild(resetCamera);

            column.AddChild(new HSeparator());
            var tips = new Label
            {
                Text = "操作\n• 在右侧模型区域拖动：旋转\n• 滚轮：缩放\n• 修改 Seed 后回车或点“重新生成”\n\n注意：当前为静态三角网格预览，尚无蒙皮动画、UV 和连续融合曲面。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            tips.AddThemeFontSizeOverride("font_size", 11);
            tips.AddThemeColorOverride("font_color", new Color(0.62f, 0.66f, 0.72f));
            column.AddChild(tips);

            var viewHint = new Label
            {
                Text = "DRAG  ROTATE     ·     WHEEL  ZOOM",
                AnchorLeft = 1,
                AnchorRight = 1,
                AnchorTop = 1,
                AnchorBottom = 1,
                OffsetLeft = -320,
                OffsetRight = -18,
                OffsetTop = -34,
                OffsetBottom = -14,
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            viewHint.AddThemeFontSizeOverride("font_size", 10);
            viewHint.AddThemeColorOverride("font_color", new Color(0.65f, 0.69f, 0.77f));
            root.AddChild(viewHint);
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.66f));
            return label;
        }

        private static Label FieldLabel(string text)
        {
            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(0.76f, 0.79f, 0.84f));
            return label;
        }

        private string SelectedFamilyId
        {
            get
            {
                int index = Math.Clamp(_familySelector.Selected, 0, Math.Max(0, _familyIds.Count - 1));
                return _familyIds.Count > 0 ? _familyIds[index] : "biped";
            }
        }

        private string SelectedStyleId
        {
            get
            {
                int index = Math.Clamp(_styleSelector.Selected, 0, Styles.Length - 1);
                return Styles[index].Id;
            }
        }

        private void EnsureStyleCompatibility()
        {
            if (!string.Equals(SelectedFamilyId, "biped", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(SelectedStyleId, "fantasy", StringComparison.OrdinalIgnoreCase))
            {
                _styleSelector.Selected = 0;
                _status.Text = "现代 / 科幻 / 废土装备包目前只适配 biped；已切回原生奇幻风格。";
            }
        }

        private static CreatureGenome DisableNativeGear(CreatureGenome genome)
        {
            return genome
                .With("gear.weapon", 0)
                .With("gear.outfit", 0)
                .With("gear.helmet", 0)
                .With("gear.shield", 0)
                .With("gear.pauldrons", 0);
        }

        private void GeneratePreview()
        {
            if (_familySelector == null || _styleSelector == null || _equipmentToggle == null) return;
            try
            {
                EnsureStyleCompatibility();
                string familyId = SelectedFamilyId;
                string style = SelectedStyleId;
                ulong seed = StableHash.SeedFromText(_seedInput.Text);
                bool showEquipment = _equipmentToggle.ButtonPressed;

                CreatureGenome genome = CreatureRuntime.Sample(familyId, seed);
                if (string.Equals(familyId, "biped", StringComparison.OrdinalIgnoreCase) &&
                    (!showEquipment || !string.Equals(style, "fantasy", StringComparison.OrdinalIgnoreCase)))
                    genome = DisableNativeGear(genome);

                CreatureModel model = CreatureRuntime.Build(genome);
                CreatureAnatomy anatomy = model.Anatomy;
                if (showEquipment && !string.Equals(style, "fantasy", StringComparison.OrdinalIgnoreCase))
                    anatomy = EquipmentGenerator.Apply(anatomy, style, seed);

                ObjMeshResult result = ObjMeshExporter.Export(
                    anatomy, sides: 20, rings: 12, unitsPerPixel: PreviewUnitsPerPixel);
                ReplaceModelMeshes(result.ObjText, model.Palette);

                _lastObj = result.ObjText;
                _lastMtl = result.MtlText;
                _lastExportSummary = $"{familyId} / {style} / seed {seed}";
                _seedResolved.Text = $"解析后的 Seed：{seed}";
                _status.Text =
                    $"家族：{familyId}\n风格：{Styles[Math.Clamp(_styleSelector.Selected, 0, Styles.Length - 1)].Label}\n" +
                    $"装备：{(showEquipment ? "开启" : "关闭")}\n" +
                    $"网格：{result.Vertices:N0} 顶点 · {result.Triangles:N0} 三角形\n" +
                    $"模型键：{anatomy.AnatomyKey:X16}";
                _exportButton.Disabled = false;
            }
            catch (Exception ex)
            {
                _status.Text = "生成失败：\n" + ex.Message;
                _exportButton.Disabled = true;
                GD.PushError("[CreaturePreview3D] " + ex);
            }
        }

        private void ReplaceModelMeshes(string objText, CreaturePalette palette)
        {
            foreach (Node child in _modelRoot.GetChildren())
            {
                _modelRoot.RemoveChild(child);
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

                var instance = new MeshInstance3D
                {
                    Name = "Material_" + entry.Key,
                    Mesh = mesh,
                    MaterialOverride = CreateMaterial(entry.Key, palette),
                };
                _modelRoot.AddChild(instance);
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

        private void ExportCurrentModel()
        {
            if (string.IsNullOrEmpty(_lastObj) || string.IsNullOrEmpty(_lastMtl)) return;
            const string outputDir = "user://exports/3d-preview";
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(outputDir));
            string objPath = ProjectSettings.GlobalizePath(outputDir + "/creature.obj");
            string mtlPath = ProjectSettings.GlobalizePath(outputDir + "/creature.mtl");
            try
            {
                File.WriteAllText(objPath, _lastObj);
                File.WriteAllText(mtlPath, _lastMtl);
                _status.Text += $"\n\n已导出：{outputDir}\n{_lastExportSummary}";
                OS.ShellOpen(ProjectSettings.GlobalizePath(outputDir));
            }
            catch (Exception ex)
            {
                _status.Text = "导出失败：" + ex.Message;
                GD.PushError("[CreaturePreview3D] Export failed: " + ex);
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton button)
            {
                if (button.ButtonIndex == MouseButton.Left)
                {
                    _dragging = button.Pressed && button.Position.X > SidebarWidth;
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                {
                    _cameraDistance = Mathf.Max(2.4f, _cameraDistance - 0.45f);
                    UpdateCameraRig();
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                {
                    _cameraDistance = Mathf.Min(13f, _cameraDistance + 0.45f);
                    UpdateCameraRig();
                }
            }
            else if (@event is InputEventMouseMotion motion && _dragging)
            {
                _yaw -= motion.Relative.X * 0.008f;
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.008f, -0.65f, 0.8f);
                UpdateCameraRig();
            }
        }

        private void UpdateCameraRig()
        {
            if (_cameraRig == null || _camera == null) return;
            _cameraRig.Rotation = new Vector3(_pitch, _yaw, 0);
            _camera.Position = new Vector3(0, 0, _cameraDistance);
        }
    }
}
