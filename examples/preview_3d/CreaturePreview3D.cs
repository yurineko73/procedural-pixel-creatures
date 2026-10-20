// Standalone Godot 3D preview for procedural creatures and modular equipment.
// Open examples/preview_3d/CreaturePreview3D.tscn and run it (F6).
// Geometry is generated from the engine-independent rest-pose OBJ backend; no external assets required.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Equipment;
using PixelCreatures.Core.Export;
using PixelCreatures.Core.Genetics;
using PixelCreatures.Core.Rendering;
using PixelCreatures.Runtime;

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
        private LineEdit _gearSeedInput = null!;
        private Label _gearSeedSummary = null!;
        private CheckBox _equipmentToggle = null!;
        private CheckBox _multiviewToggle = null!;
        private CheckBox _geometryFitToggle = null!;
        private CheckBox _topologyToggle = null!;
        private TextureRect _reference2D = null!;
        private EquipmentSeedSet _equipmentSeeds = EquipmentSeedSet.FromMaster(54321UL);
        private ulong _masterGearSeed = 54321UL;
        private ulong _gearColorSeed = StableHash.Derive(54321UL, "equipment.colors");
        private string _lastMasterGearSeedText = string.Empty;
        private int _outfitRerolls, _weaponRerolls, _headgearRerolls, _accessoryRerolls;
        private Label _status = null!;
        private Label _seedResolved = null!;
        private Button _exportButton = null!;
        private string _lastObj = string.Empty;
        private string _lastMtl = string.Empty;
        private string _lastExportSummary = string.Empty;
        private string _compatibilityNote = string.Empty;
        private bool _dragging;
        private float _yaw = 0.58f;
        private float _pitch = 0.12f;
        private float _cameraDistance = 6.5f;

        public override void _Ready()
        {
            var stage = PreviewStageBuilder.Build(this);
            _modelRoot = stage.ModelRoot;
            _cameraRig = stage.CameraRig;
            _camera = stage.Camera;
            BuildUi();
            GeneratePreview();
        }

        private void BuildUi()
        {
            var layer = new CanvasLayer { Layer = 10 };
            AddChild(layer);
            var root = new Control { Name = "PreviewOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.Theme = CreatePreviewTheme();
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
            scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            margin.AddChild(scroll);
            var column = new VBoxContainer();
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
            _familySelector = new OptionButton { CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var family in CreatureRuntime.Registry.Families)
            {
                _familyIds.Add(family.Id);
                _familySelector.AddItem(family.Id);
            }
            int bipedIndex = _familyIds.FindIndex(id => string.Equals(id, "biped", StringComparison.OrdinalIgnoreCase));
            _familySelector.Selected = bipedIndex >= 0 ? bipedIndex : 0;
            _familySelector.ItemSelected += _ => GeneratePreview();
            column.AddChild(_familySelector);

            column.AddChild(FieldLabel("Seed（数字或任意文本）"));
            var seedRow = new HBoxContainer();
            seedRow.AddThemeConstantOverride("separation", 6);
            _seedInput = new LineEdit
            {
                Text = "12345",
                PlaceholderText = "输入 seed…",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34),
                ClearButtonEnabled = true,
            };
            _seedInput.TextSubmitted += _ => GeneratePreview();
            seedRow.AddChild(_seedInput);
            var randomSeed = new Button { Text = "随机身体", CustomMinimumSize = new Vector2(76, 34), TooltipText = "只改变身体 Seed" };
            randomSeed.Pressed += () =>
            {
                _seedInput.Text = NextRandomSeed().ToString(CultureInfo.InvariantCulture);
                GeneratePreview();
            };
            seedRow.AddChild(randomSeed);
            var randomAll = new Button { Text = "全部随机", CustomMinimumSize = new Vector2(68, 34), TooltipText = "同时重新生成身体与整套装备" };
            randomAll.Pressed += () =>
            {
                _seedInput.Text = NextRandomSeed().ToString(CultureInfo.InvariantCulture);
                _gearSeedInput.Text = NextRandomSeed().ToString(CultureInfo.InvariantCulture);
                GeneratePreview();
            };
            seedRow.AddChild(randomAll);
            column.AddChild(seedRow);

            var generate = new Button
            {
                Text = "重新生成",
                CustomMinimumSize = new Vector2(0, 38),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            generate.Pressed += GeneratePreview;
            column.AddChild(generate);
            _seedResolved = new Label { Text = "解析后的 Seed：12345" };
            _seedResolved.AddThemeFontSizeOverride("font_size", 11);
            _seedResolved.AddThemeColorOverride("font_color", new Color(0.61f, 0.67f, 0.76f));
            column.AddChild(_seedResolved);

            column.AddChild(new HSeparator());
            column.AddChild(SectionLabel("02  装备与风格"));
            column.AddChild(FieldLabel("装备 Seed（独立于身体）"));
            var gearSeedRow = new HBoxContainer();
            gearSeedRow.AddThemeConstantOverride("separation", 6);
            _gearSeedInput = new LineEdit
            {
                Text = "54321",
                PlaceholderText = "输入装备 Seed…",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34),
                ClearButtonEnabled = true,
            };
            _gearSeedInput.TextSubmitted += _ => GeneratePreview();
            gearSeedRow.AddChild(_gearSeedInput);
            var randomGearSeed = new Button
            {
                Text = "整套随机",
                CustomMinimumSize = new Vector2(76, 34),
                TooltipText = "只重新生成装备 Seed，保留身体外形",
            };
            randomGearSeed.Pressed += () =>
            {
                _gearSeedInput.Text = NextRandomSeed().ToString(CultureInfo.InvariantCulture);
                GeneratePreview();
            };
            gearSeedRow.AddChild(randomGearSeed);
            column.AddChild(gearSeedRow);

            _gearSeedSummary = new Label
            {
                Text = "服装 / 武器 / 头部 / 配件：待生成",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _gearSeedSummary.AddThemeFontSizeOverride("font_size", 10);
            _gearSeedSummary.AddThemeColorOverride("font_color", new Color(0.58f, 0.66f, 0.76f));
            column.AddChild(_gearSeedSummary);

            var rerollRowA = new HBoxContainer();
            rerollRowA.AddThemeConstantOverride("separation", 6);
            var rerollOutfit = new Button { Text = "随机服装", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            rerollOutfit.Pressed += () => RerollEquipmentSlot(EquipmentSlot.Outfit);
            rerollRowA.AddChild(rerollOutfit);
            var rerollWeapon = new Button { Text = "随机武器", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            rerollWeapon.Pressed += () => RerollEquipmentSlot(EquipmentSlot.Weapon);
            rerollRowA.AddChild(rerollWeapon);
            column.AddChild(rerollRowA);

            var rerollRowB = new HBoxContainer();
            rerollRowB.AddThemeConstantOverride("separation", 6);
            var rerollHeadgear = new Button { Text = "随机头部装备", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            rerollHeadgear.Pressed += () => RerollEquipmentSlot(EquipmentSlot.Headgear);
            rerollRowB.AddChild(rerollHeadgear);
            var rerollAccessory = new Button { Text = "随机配件", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            rerollAccessory.Pressed += () => RerollEquipmentSlot(EquipmentSlot.Accessory);
            rerollRowB.AddChild(rerollAccessory);
            column.AddChild(rerollRowB);

            _equipmentToggle = new CheckBox { Text = "启用装备", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
            _equipmentToggle.Toggled += _ => GeneratePreview();
            column.AddChild(_equipmentToggle);

            column.AddChild(FieldLabel("装备风格"));
            _styleSelector = new OptionButton { CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var style in Styles) _styleSelector.AddItem(style.Label);
            _styleSelector.Selected = 0;
            _styleSelector.ItemSelected += _ => GeneratePreview();
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
            column.AddChild(SectionLabel("03  实验性重建模式"));
            _multiviewToggle = new CheckBox
            {
                Text = "多视角深度重建（实验 / 不含装备）",
                ButtonPressed = false,
                TooltipText = "从16个方向读取原版2D深度与轮廓构建视觉壳，再用局部深度约束修整表面；不是旧版SDF导出。",
            };
            _multiviewToggle.Toggled += enabled =>
            {
                if (enabled)
                {
                    _geometryFitToggle.SetPressedNoSignal(false);
                    _topologyToggle.SetPressedNoSignal(false);
                }
                GeneratePreview();
            };
            column.AddChild(_multiviewToggle);
            var multiviewHint = new Label
            {
                Text = "从原版2D多视角深度与轮廓重建体积；保留为对照实验，不作为默认导出方案。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            multiviewHint.AddThemeFontSizeOverride("font_size", 11);
            column.AddChild(multiviewHint);

            _geometryFitToggle = new CheckBox
            {
                Text = "几何拟合原型（实验 / 不含装备）",
                ButtonPressed = false,
                TooltipText = "固定原始2D渲染轮廓，自动对身体、头部、四肢、尾巴等原始几何分组做小幅有界调整；不从深度图反推网格。",
            };
            _geometryFitToggle.Toggled += enabled =>
            {
                if (enabled)
                {
                    _multiviewToggle.SetPressedNoSignal(false);
                    _topologyToggle.SetPressedNoSignal(false);
                }
                GeneratePreview();
            };
            column.AddChild(_geometryFitToggle);
            var geometryFitHint = new Label
            {
                Text = "开启后比较多个方向的原版2D轮廓与参数化3D几何投影，自动微调少量部件组。首次拟合可能短暂卡顿；指标是拟合代理轮廓，仍需检查最终网格。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            geometryFitHint.AddThemeFontSizeOverride("font_size", 11);
            column.AddChild(geometryFitHint);

            _topologyToggle = new CheckBox
            {
                Text = "语义拓扑网格（实验 / 不含装备）",
                ButtonPressed = false,
                TooltipText = "按原始语义部件生成固定索引曲面，再用原版2D多视角轮廓和深度约束调整顶点；不构建SDF或体素并集。",
            };
            _topologyToggle.Toggled += enabled =>
            {
                if (enabled)
                {
                    _multiviewToggle.SetPressedNoSignal(false);
                    _geometryFitToggle.SetPressedNoSignal(false);
                }
                GeneratePreview();
            };
            column.AddChild(_topologyToggle);
            var topologyHint = new Label
            {
                Text = "独立拓扑实验：每个语义分组生成索引曲面，再按多视角约束移动顶点。当前仍可能有部件交叠、凹陷丢失或接缝，不代表已达到最终质量。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            topologyHint.AddThemeFontSizeOverride("font_size", 11);
            column.AddChild(topologyHint);
            _reference2D = new TextureRect
            {
                Visible = false,
                CustomMinimumSize = new Vector2(0, 160),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                TooltipText = "原版2D渲染器的真实基准画面（0°）。与右侧新重建的3D模型对照。",
            };
            column.AddChild(_reference2D);
            var align2dCamera = new Button
            {
                Text = "对齐原版 2D 摄像机（0° / 30°）",
                TooltipText = "切换正交摄影，按原版30度俯视方向观察3D网格；精确像素对比仍受预览窗口尺寸影响。",
            };
            align2dCamera.Pressed += () =>
            {
                _camera.Projection = Camera3D.ProjectionType.Orthogonal;
                _camera.Size = 5.0f;
                _yaw = 0;
                _pitch = -Mathf.Pi / 6f;
                UpdateCameraRig();
            };
            column.AddChild(align2dCamera);
            column.AddChild(new HSeparator());
            column.AddChild(SectionLabel("04  当前模型"));
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
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "保存当前静态 3D 网格到 user://exports/3d-preview/",
            };
            _exportButton.Pressed += ExportCurrentModel;
            column.AddChild(_exportButton);

            var resetCamera = new Button { Text = "重置视角", CustomMinimumSize = new Vector2(0, 32) };
            resetCamera.Pressed += () =>
            {
                _camera.Projection = Camera3D.ProjectionType.Perspective;
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

        private static Theme CreatePreviewTheme()
        {
            var theme = new Theme { DefaultFontSize = 14 };
            theme.SetColor("font_color", "Label", new Color(0.88f, 0.89f, 0.92f));
            theme.SetColor("font_color", "Button", new Color(0.9f, 0.91f, 0.94f));
            theme.SetColor("font_color", "LineEdit", new Color(0.92f, 0.93f, 0.95f));
            var button = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.18f, 0.23f),
                BorderColor = new Color(0.28f, 0.33f, 0.41f),
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
                ContentMarginLeft = 8, ContentMarginRight = 8,
                ContentMarginTop = 5, ContentMarginBottom = 5,
            };
            theme.SetStylebox("normal", "Button", button);
            theme.SetStylebox("hover", "Button", new StyleBoxFlat
            {
                BgColor = new Color(0.21f, 0.25f, 0.31f),
                BorderColor = new Color(0.75f, 0.59f, 0.34f),
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            });
            return theme;
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
            _compatibilityNote = string.Empty;
            if (!string.Equals(SelectedFamilyId, "biped", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(SelectedStyleId, "fantasy", StringComparison.OrdinalIgnoreCase))
            {
                _styleSelector.Selected = 0;
                _compatibilityNote = "现代 / 科幻 / 废土装备包目前只适配 biped；已切回原生奇幻风格。";
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

        private static ulong NextRandomSeed()
        {
            ulong seed = ((ulong)GD.Randi() << 32) | GD.Randi();
            return seed == 0 ? 1UL : seed;
        }

        private void SyncEquipmentSeeds()
        {
            if (_gearSeedInput == null) return;
            string text = _gearSeedInput.Text.Trim();
            if (text.Length == 0) text = "0";
            if (string.Equals(text, _lastMasterGearSeedText, StringComparison.Ordinal)) return;

            _lastMasterGearSeedText = text;
            _masterGearSeed = StableHash.SeedFromText(text);
            _gearColorSeed = StableHash.Derive(_masterGearSeed, "equipment.colors");
            _equipmentSeeds = EquipmentSeedSet.FromMaster(_masterGearSeed);
            _outfitRerolls = _weaponRerolls = _headgearRerolls = _accessoryRerolls = 0;
            UpdateEquipmentSeedSummary();
        }

        private void RerollEquipmentSlot(EquipmentSlot slot)
        {
            SyncEquipmentSeeds();
            int count = slot switch
            {
                EquipmentSlot.Outfit => _outfitRerolls,
                EquipmentSlot.Weapon => _weaponRerolls,
                EquipmentSlot.Headgear => _headgearRerolls,
                _ => _accessoryRerolls,
            };
            bool requireNativeItem = string.Equals(SelectedFamilyId, "biped", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SelectedStyleId, "fantasy", StringComparison.OrdinalIgnoreCase);
            ulong rerolled = _equipmentSeeds.For(slot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                count++;
                rerolled = StableHash.Derive(_masterGearSeed,
                    "preview.reroll." + slot.ToString().ToLowerInvariant(), count);
                if (!requireNativeItem || NativeSlotHasGear(slot, rerolled)) break;
            }
            switch (slot)
            {
                case EquipmentSlot.Outfit: _outfitRerolls = count; break;
                case EquipmentSlot.Weapon: _weaponRerolls = count; break;
                case EquipmentSlot.Headgear: _headgearRerolls = count; break;
                default: _accessoryRerolls = count; break;
            }
            _equipmentSeeds = slot switch
            {
                EquipmentSlot.Outfit => new EquipmentSeedSet(rerolled, _equipmentSeeds.WeaponSeed, _equipmentSeeds.HeadgearSeed, _equipmentSeeds.AccessorySeed),
                EquipmentSlot.Weapon => new EquipmentSeedSet(_equipmentSeeds.OutfitSeed, rerolled, _equipmentSeeds.HeadgearSeed, _equipmentSeeds.AccessorySeed),
                EquipmentSlot.Headgear => new EquipmentSeedSet(_equipmentSeeds.OutfitSeed, _equipmentSeeds.WeaponSeed, rerolled, _equipmentSeeds.AccessorySeed),
                _ => new EquipmentSeedSet(_equipmentSeeds.OutfitSeed, _equipmentSeeds.WeaponSeed, _equipmentSeeds.HeadgearSeed, rerolled),
            };
            UpdateEquipmentSeedSummary();
            GeneratePreview();
        }

        private static bool NativeSlotHasGear(EquipmentSlot slot, ulong seed)
        {
            CreatureGenome sample = CreatureRuntime.Sample("biped", seed);
            return slot switch
            {
                EquipmentSlot.Outfit => sample.GetChoice("gear.outfit") != "none",
                EquipmentSlot.Weapon => sample.GetChoice("gear.weapon") != "none",
                EquipmentSlot.Headgear => sample.GetChoice("gear.helmet") != "none",
                _ => sample.GetBool("gear.shield") || sample.GetBool("gear.pauldrons"),
            };
        }

        private void UpdateEquipmentSeedSummary()
        {
            if (_gearSeedSummary == null) return;
            static string Short(ulong seed) => (seed & 0xffffffffUL).ToString("x8", CultureInfo.InvariantCulture);
            _gearSeedSummary.Text =
                $"服装 {Short(_equipmentSeeds.OutfitSeed)} · 武器 {Short(_equipmentSeeds.WeaponSeed)}\n" +
                $"头部 {Short(_equipmentSeeds.HeadgearSeed)} · 配件 {Short(_equipmentSeeds.AccessorySeed)}";
        }

        private void GeneratePreview()
        {
            if (_familySelector == null || _styleSelector == null || _equipmentToggle == null) return;
            try
            {
                EnsureStyleCompatibility();
                SyncEquipmentSeeds();
                string familyId = SelectedFamilyId;
                string style = SelectedStyleId;
                ulong seed = StableHash.SeedFromText(_seedInput.Text);
                bool experimental = _multiviewToggle.ButtonPressed;
                bool geometryFit = _geometryFitToggle.ButtonPressed;
                bool topology = _topologyToggle.ButtonPressed;
                bool showEquipment = _equipmentToggle.ButtonPressed && !experimental && !geometryFit && !topology;
                bool isBiped = string.Equals(familyId, "biped", StringComparison.OrdinalIgnoreCase);

                CreatureGenome genome = CreatureRuntime.Sample(familyId, seed);
                if (isBiped)
                {
                    ICreatureFamily bipedFamily = CreatureRuntime.Registry.GetFamily("biped");
                    // Equipment colours and native gear genes are routed through a reusable Core module.
                    genome = NativeGearRandomizer.ApplyEquipmentColors(genome, bipedFamily, _gearColorSeed);
                    if (!showEquipment || !string.Equals(style, "fantasy", StringComparison.OrdinalIgnoreCase))
                        genome = DisableNativeGear(genome);
                    else
                        genome = NativeGearRandomizer.ApplyNativeGearGenes(genome, bipedFamily, _masterGearSeed, _equipmentSeeds);
                }

                CreatureModel model = CreatureRuntime.Build(genome);
                CreatureAnatomy anatomy = model.Anatomy;
                if (showEquipment && isBiped && !string.Equals(style, "fantasy", StringComparison.OrdinalIgnoreCase))
                    anatomy = EquipmentGenerator.Apply(anatomy, style, _equipmentSeeds);

                string objText;
                string mtlText;
                int vertexCount, triangleCount;
                string reconstructionNote = string.Empty;
                if (experimental)
                {
                    // Body surface comes from the original 2D renderer's G-buffer; bone-anchored facial features are appended separately.
                    var captures = MultiviewReconstructor.Capture(model, 16);
                    MultiviewResult reconstructed = MultiviewReconstructor.Build(
                        captures, new MultiviewOptions { Directions = 16 });
                    objText = reconstructed.ToObj(captures, model.Palette, model.Anatomy, PreviewUnitsPerPixel);
                    SetReference(captures[0], model);
                    mtlText = MultiviewResult.MaterialLibrary(model.Palette);
                    vertexCount = reconstructed.ExportedVertices;
                    triangleCount = reconstructed.ExportedTriangles;
                    double[] iou = MultiviewSilhouetteMetrics.IntersectionOverUnion(reconstructed, captures);
                    double mean = 0, worst = 1;
                    foreach (double value in iou) { mean += value; worst = Math.Min(worst, value); }
                    mean /= iou.Length;
                    reconstructionNote = $"\n方法：16方向轮廓视觉壳 + 连续深度采样/边缘保留平滑\n主体轮廓IoU：平均 {mean:P1} / 最低 {worst:P1}\n体素步长：{reconstructed.GridStep:F2}，采样点：{reconstructed.SampledGridPoints:N0}";
                }
                else if (geometryFit)
                {
                    GeometryFitResult fit = GeometryFitPrototype.Fit(model, new GeometryFitOptions
                    {
                        Directions = 8,
                        Passes = 1,
                        ScaleStep = 0.04,
                        OffsetStep = 0.25,
                        MaxScaleDelta = 0.08,
                        MaxOffset = 0.5,
                        MaxMeshEvaluations = 32,
                        MaxRegions = 6,
                    });
                    anatomy = fit.Anatomy;
                    SetReference(fit.ReferenceViews[0], model);
                    ObjMeshResult result = fit.FittedMesh;
                    objText = result.ObjText;
                    mtlText = result.MtlText;
                    vertexCount = result.Vertices;
                    triangleCount = result.Triangles;
                    reconstructionNote =
                        $"\n方法：原始几何分组 + 8视角有界坐标优化" +
                        $"\n拟合代理IoU：平均 {fit.InitialMeanIoU:P1} → {fit.FittedMeanIoU:P1}" +
                        $"\n代理最差视角：{fit.InitialWorstIoU:P1} → {fit.FittedWorstIoU:P1}" +
                        $"\n最终OBJ轮廓IoU：平均 {fit.InitialMeshMeanIoU:P1} → {fit.FittedMeshMeanIoU:P1}" +
                        $"\n最终OBJ最差视角：{fit.InitialMeshWorstIoU:P1} → {fit.FittedMeshWorstIoU:P1}" +
                        $"\n调整分组：{fit.AdjustedRegions}，OBJ目标评估：{fit.Evaluations} 次，代理评估：{fit.ProxyEvaluations} 次" +
                        $"\n参数：{fit.ParameterSummary}";
                }
                else if (topology)
                {
                    SemanticTopologyResult result = SemanticTopologyPrototype.Build(model, new SemanticTopologyOptions
                    {
                        Directions = 8,
                        LatitudeSegments = 12,
                        LongitudeSegments = 24,
                        RelaxationPasses = 2,
                        SilhouetteSearchRadius = 3,
                        SampleSides = 16,
                        SampleRings = 8,
                        UnitsPerPixel = PreviewUnitsPerPixel,
                    });
                    SetReference(result.ReferenceViews[0], model);
                    objText = result.Mesh.ObjText;
                    mtlText = result.Mesh.MtlText;
                    vertexCount = result.Mesh.Vertices;
                    triangleCount = result.Mesh.Triangles;
                    reconstructionNote =
                        $"\\n方法：语义分组径向拓扑 + 8视角顶点约束（不使用SDF/体素并集）" +
                        $"\\n旧SDF主体轮廓IoU：平均 {result.BaselineMeanIoU:P1} / 最差 {result.BaselineWorstIoU:P1}" +
                        $"\\n新拓扑初始IoU：平均 {result.TemplateMeanIoU:P1} / 最差 {result.TemplateWorstIoU:P1}" +
                        $"\\n约束后IoU：平均 {result.FittedMeanIoU:P1} / 最差 {result.FittedWorstIoU:P1}" +
                        $"\\n语义网格分组：{result.RegionCount}，调整顶点：{result.RelaxedVertices:N0}";
                }
                else
                {
                    _reference2D.Visible = false;
                    ObjMeshResult result = ObjMeshExporter.Export(
                        anatomy, sides: 20, rings: 12, unitsPerPixel: PreviewUnitsPerPixel);
                    objText = result.ObjText;
                    mtlText = result.MtlText;
                    vertexCount = result.Vertices;
                    triangleCount = result.Triangles;
                }
                ObjPreviewMeshBuilder.ReplaceModelMeshes(_modelRoot, objText, model.Palette);

                _lastObj = objText;
                _lastMtl = mtlText;
                _lastExportSummary = $"{familyId} / {(experimental ? "multiview" : geometryFit ? "geometry-fit" : topology ? "semantic-topology" : style)} / seed {seed}";
                _seedResolved.Text = $"解析后的 Seed：{seed}";
                _status.Text =
                    $"家族：{familyId}\n风格：{Styles[Math.Clamp(_styleSelector.Selected, 0, Styles.Length - 1)].Label}\n" +
                    $"装备：{(showEquipment ? "开启" : "关闭")}\n" +
                    $"网格：{vertexCount:N0} 顶点 · {triangleCount:N0} 三角形\n" +
                    $"模型键：{anatomy.AnatomyKey:X16}" +
                    $"\n身体 Seed：{seed}" +
                    $"\n装备 Seed：{_masterGearSeed}" +
                    reconstructionNote +
                    (_compatibilityNote.Length > 0 ? $"\n\n{_compatibilityNote}" : string.Empty);
                _exportButton.Disabled = false;
            }
            catch (Exception ex)
            {
                _reference2D.Visible = false;
                _status.Text = "生成失败：\n" + ex.Message;
                _exportButton.Disabled = true;
                GD.PushError("[CreaturePreview3D] " + ex);
            }
        }

        private void SetReference(PixelDepthCapture capture, CreatureModel model)
        {
            var lut = new byte[256 * 4];
            model.Palette.BuildLut(lut, 0);
            byte[] rgba = new byte[capture.Width * capture.Height * 4];
            for (int i = 0; i < capture.PaletteIndex.Length; i++)
            {
                int source = capture.PaletteIndex[i] * 4;
                int destination = i * 4;
                rgba[destination] = lut[source];
                rgba[destination + 1] = lut[source + 1];
                rgba[destination + 2] = lut[source + 2];
                rgba[destination + 3] = lut[source + 3];
            }
            Image image = Image.CreateFromData(capture.Width, capture.Height, false, Image.Format.Rgba8, rgba);
            _reference2D.Texture = ImageTexture.CreateFromImage(image);
            _reference2D.Visible = true;
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
