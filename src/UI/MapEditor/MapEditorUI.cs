using System;
using System.Collections.Generic;
using Godot;
using CorditeWars.Game.World;

namespace CorditeWars.UI.MapEditor;

/// <summary>
/// UI overlay for the map editor. Provides:
/// - Left panel: tool selection buttons
/// - Right panel: prop browser from terrain manifest categories
/// - Top bar: save/load/new/undo/redo/test
/// - Bottom: brush settings (size, intensity, biome)
/// - Properties panel: edit selected object
/// </summary>
public partial class MapEditorUI : Control
{
    // ── References ──────────────────────────────────────────────────────────
    private Game.World.MapEditor _editor = null!;
    private TerrainManifest _manifest = null!;
    private Camera3D _camera = null!;

    // ── UI Containers ──────────────────────────────────────────────────────
    private VBoxContainer _leftPanel = null!;
    private VBoxContainer _rightPanel = null!;
    private HBoxContainer _topBar = null!;
    private HBoxContainer _bottomBar = null!;
    private PanelContainer _propertiesPanel = null!;

    // ── Tool Buttons ───────────────────────────────────────────────────────
    private readonly SortedList<int, Button> _toolButtons = new();
    private Button _activeToolButton = null!;

    // ── Brush Settings Controls ────────────────────────────────────────────
    private HSlider _brushSizeSlider = null!;
    private HSlider _brushIntensitySlider = null!;
    private Label _brushSizeLabel = null!;
    private Label _brushIntensityLabel = null!;
    private OptionButton _biomeSelector = null!;

    // ── Prop Browser ───────────────────────────────────────────────────────
    private OptionButton _propCategorySelector = null!;
    private ItemList _propList = null!;

    // ── Undo/Redo Buttons ──────────────────────────────────────────────────
    private Button _undoButton = null!;
    private Button _redoButton = null!;

    // ── Properties Panel Fields ────────────────────────────────────────────
    private SpinBox _propRotation = null!;
    private SpinBox _propScale = null!;
    private SpinBox _corditeAmount = null!;
    private Label _statusLabel = null!;

    // ── Mouse State (for terrain editing) ──────────────────────────────────
    private bool _isDragging;
    private Vector2 _lastMousePos;

    // ── Initialization ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Create editor node
        _editor = new Game.World.MapEditor();
        AddChild(_editor);

        // Load terrain manifest
        _manifest = new TerrainManifest();
        try
        {
            _manifest.Load("res://data/terrain_manifest.json");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MapEditorUI] Failed to load terrain manifest: {e.Message}");
        }

        // Create 3D camera
        SetupCamera();

        // Create UI panels
        BuildTopBar();
        BuildLeftPanel();
        BuildRightPanel();
        BuildBottomBar();
        BuildPropertiesPanel();
        BuildStatusBar();

        // Connect editor signals
        _editor.ToolChanged += OnToolChanged;
        _editor.UndoStackChanged += OnUndoStackChanged;
        _editor.MapModified += OnMapModified;

        // Create default new map
        _editor.NewMap(128, 128, "temperate");

        GD.Print("[MapEditorUI] Map Editor ready.");
    }

    private void SetupCamera()
    {
        var camera = new Camera3D();
        camera.Position = new Vector3(64, 80, 64);
        camera.LookAt(new Vector3(64, 0, 64), Vector3.Up);
        camera.Fov = 50;
        camera.Far = 500;
        AddChild(camera);
        _camera = camera;

        // Add directional light
        var light = new DirectionalLight3D();
        light.Position = new Vector3(0, 100, 0);
        light.Rotation = new Vector3(Mathf.DegToRad(-45), Mathf.DegToRad(30), 0);
        light.ShadowEnabled = true;
        AddChild(light);

        // World environment
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.4f, 0.6f, 0.8f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.6f, 0.6f, 0.7f);
        env.AmbientLightEnergy = 0.5f;

        var worldEnv = new WorldEnvironment();
        worldEnv.Environment = env;
        AddChild(worldEnv);
    }

    // ── Top Bar ────────────────────────────────────────────────────────────

    private void BuildTopBar()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        panel.CustomMinimumSize = new Vector2(0, 44);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        _topBar = new HBoxContainer();
        _topBar.AddThemeConstantOverride("separation", 8);
        panel.AddChild(_topBar);

        // File operations
        AddTopBarButton("New", OnNewMapPressed);
        AddTopBarButton("Save", OnSavePressed);
        AddTopBarButton("Load", OnLoadPressed);

        // Separator
        var sep1 = new VSeparator();
        _topBar.AddChild(sep1);

        // Undo/Redo
        _undoButton = AddTopBarButton("Undo", OnUndoPressed);
        _redoButton = AddTopBarButton("Redo", OnRedoPressed);
        _undoButton.Disabled = true;
        _redoButton.Disabled = true;

        // Separator
        var sep2 = new VSeparator();
        _topBar.AddChild(sep2);

        // Test play
        AddTopBarButton("Test Play", OnTestPlayPressed);

        // Separator
        var sep3 = new VSeparator();
        _topBar.AddChild(sep3);

        // Regenerate terrain
        AddTopBarButton("Refresh", OnRefreshPressed);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _topBar.AddChild(spacer);

        // Back to menu
        AddTopBarButton("Main Menu", OnBackToMenuPressed);
    }

    private Button AddTopBarButton(string text, Action onPressed)
    {
        var btn = new Button();
        btn.Text = text;
        UITheme.StyleButton(btn);
        btn.Pressed += onPressed;
        _topBar.AddChild(btn);
        return btn;
    }

    // ── Left Panel (Tool Selection) ────────────────────────────────────────

    private void BuildLeftPanel()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        panel.OffsetTop = 48;
        panel.OffsetBottom = -80;
        panel.CustomMinimumSize = new Vector2(180, 0);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        var scroll = new ScrollContainer();
        panel.AddChild(scroll);

        _leftPanel = new VBoxContainer();
        _leftPanel.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_leftPanel);

        // Section: Terrain
        AddToolSection("TERRAIN");
        AddToolButton("Raise", EditorTool.TerrainRaise);
        AddToolButton("Lower", EditorTool.TerrainLower);
        AddToolButton("Smooth", EditorTool.TerrainSmooth);
        AddToolButton("Flatten", EditorTool.TerrainFlatten);

        // Section: Paint
        AddToolSection("PAINT");
        AddToolButton("Biome", EditorTool.BiomeBrush);

        // Section: Features
        AddToolSection("FEATURES");
        AddToolButton("River", EditorTool.RiverTool);
        AddToolButton("Bridge", EditorTool.BridgeTool);

        // Section: Objects
        AddToolSection("OBJECTS");
        AddToolButton("Props", EditorTool.PropPlacer);
        AddToolButton("Structures", EditorTool.StructurePlacer);
        AddToolButton("Cordite Node", EditorTool.CorditeNodePlacer);
        AddToolButton("Start Pos", EditorTool.StartingPositionPlacer);

        // Section: Edit
        AddToolSection("EDIT");
        AddToolButton("Eraser", EditorTool.Eraser);
    }

    private void AddToolSection(string name)
    {
        var label = new Label();
        label.Text = name;
        UITheme.StyleLabel(label, UITheme.FontSizeSmall, UITheme.TextSecondary);
        _leftPanel.AddChild(label);
    }

    private void AddToolButton(string text, EditorTool tool)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(160, 32);
        btn.Alignment = HorizontalAlignment.Left;
        UITheme.StyleButton(btn);
        btn.Pressed += () => OnToolButtonPressed(tool, btn);
        _leftPanel.AddChild(btn);

        _toolButtons.Add((int)tool, btn);
    }

    // ── Right Panel (Prop Browser) ─────────────────────────────────────────

    private void BuildRightPanel()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
        panel.OffsetTop = 48;
        panel.OffsetBottom = -80;
        panel.CustomMinimumSize = new Vector2(200, 0);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        _rightPanel = new VBoxContainer();
        _rightPanel.AddThemeConstantOverride("separation", 6);
        panel.AddChild(_rightPanel);

        // Title
        var title = new Label();
        title.Text = "PROP BROWSER";
        UITheme.StyleLabel(title, UITheme.FontSizeNormal, UITheme.Accent);
        _rightPanel.AddChild(title);

        // Category selector
        _propCategorySelector = new OptionButton();
        UITheme.StyleOptionButton(_propCategorySelector);
        _rightPanel.AddChild(_propCategorySelector);

        // Populate categories
        var categories = _manifest.GetCategories();
        for (int i = 0; i < categories.Count; i++)
        {
            _propCategorySelector.AddItem(categories[i]);
        }
        _propCategorySelector.ItemSelected += OnPropCategorySelected;

        // Prop list
        _propList = new ItemList();
        _propList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _propList.AddThemeStyleboxOverride("panel", UITheme.MakePanelNoBorder());
        _propList.AddThemeColorOverride("font_color", UITheme.TextPrimary);
        _propList.AddThemeFontSizeOverride("font_size", UITheme.FontSizeSmall);
        _propList.ItemSelected += OnPropSelected;
        _rightPanel.AddChild(_propList);

        // Load first category if available
        if (categories.Count > 0)
        {
            OnPropCategorySelected(0);
        }
    }

    // ── Bottom Bar (Brush Settings) ────────────────────────────────────────

    private void BuildBottomBar()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        panel.CustomMinimumSize = new Vector2(0, 76);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        _bottomBar = new HBoxContainer();
        _bottomBar.AddThemeConstantOverride("separation", 20);
        panel.AddChild(_bottomBar);

        // Brush Size
        var sizeBox = new VBoxContainer();
        _bottomBar.AddChild(sizeBox);

        _brushSizeLabel = new Label();
        _brushSizeLabel.Text = "Brush Size: 5";
        UITheme.StyleLabel(_brushSizeLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        sizeBox.AddChild(_brushSizeLabel);

        _brushSizeSlider = new HSlider();
        _brushSizeSlider.MinValue = 1;
        _brushSizeSlider.MaxValue = 30;
        _brushSizeSlider.Value = 5;
        _brushSizeSlider.Step = 1;
        _brushSizeSlider.CustomMinimumSize = new Vector2(150, 20);
        UITheme.StyleSlider(_brushSizeSlider);
        _brushSizeSlider.ValueChanged += OnBrushSizeChanged;
        sizeBox.AddChild(_brushSizeSlider);

        // Brush Intensity
        var intensityBox = new VBoxContainer();
        _bottomBar.AddChild(intensityBox);

        _brushIntensityLabel = new Label();
        _brushIntensityLabel.Text = "Intensity: 0.50";
        UITheme.StyleLabel(_brushIntensityLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        intensityBox.AddChild(_brushIntensityLabel);

        _brushIntensitySlider = new HSlider();
        _brushIntensitySlider.MinValue = 0.05f;
        _brushIntensitySlider.MaxValue = 2.0f;
        _brushIntensitySlider.Value = 0.5f;
        _brushIntensitySlider.Step = 0.05f;
        _brushIntensitySlider.CustomMinimumSize = new Vector2(150, 20);
        UITheme.StyleSlider(_brushIntensitySlider);
        _brushIntensitySlider.ValueChanged += OnBrushIntensityChanged;
        intensityBox.AddChild(_brushIntensitySlider);

        // Biome Selector
        var biomeBox = new VBoxContainer();
        _bottomBar.AddChild(biomeBox);

        var biomeLabel = new Label();
        biomeLabel.Text = "Biome:";
        UITheme.StyleLabel(biomeLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        biomeBox.AddChild(biomeLabel);

        _biomeSelector = new OptionButton();
        UITheme.StyleOptionButton(_biomeSelector);
        _biomeSelector.AddItem("temperate");
        _biomeSelector.AddItem("desert");
        _biomeSelector.AddItem("rocky");
        _biomeSelector.AddItem("coastal");
        _biomeSelector.AddItem("volcanic");
        _biomeSelector.ItemSelected += OnBiomeSelected;
        biomeBox.AddChild(_biomeSelector);
    }

    // ── Properties Panel ───────────────────────────────────────────────────

    private void BuildPropertiesPanel()
    {
        _propertiesPanel = new PanelContainer();
        _propertiesPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        _propertiesPanel.OffsetLeft = -220;
        _propertiesPanel.OffsetTop = 48;
        _propertiesPanel.CustomMinimumSize = new Vector2(200, 180);
        _propertiesPanel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        _propertiesPanel.Visible = false;
        _propertiesPanel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_propertiesPanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _propertiesPanel.AddChild(vbox);

        var propTitle = new Label();
        propTitle.Text = "PROPERTIES";
        UITheme.StyleLabel(propTitle, UITheme.FontSizeSmall, UITheme.Accent);
        vbox.AddChild(propTitle);

        // Rotation
        var rotBox = new HBoxContainer();
        vbox.AddChild(rotBox);
        var rotLabel = new Label();
        rotLabel.Text = "Rotation:";
        UITheme.StyleLabel(rotLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        rotBox.AddChild(rotLabel);
        _propRotation = new SpinBox();
        _propRotation.MinValue = 0;
        _propRotation.MaxValue = 360;
        _propRotation.Step = 15;
        _propRotation.Value = 0;
        _propRotation.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rotBox.AddChild(_propRotation);

        // Scale
        var scaleBox = new HBoxContainer();
        vbox.AddChild(scaleBox);
        var scaleLabel = new Label();
        scaleLabel.Text = "Scale:";
        UITheme.StyleLabel(scaleLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        scaleBox.AddChild(scaleLabel);
        _propScale = new SpinBox();
        _propScale.MinValue = 0.1f;
        _propScale.MaxValue = 5.0f;
        _propScale.Step = 0.1f;
        _propScale.Value = 1.0f;
        _propScale.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scaleBox.AddChild(_propScale);

        // Cordite Amount
        var corditeBox = new HBoxContainer();
        vbox.AddChild(corditeBox);
        var corditeLabel = new Label();
        corditeLabel.Text = "Amount:";
        UITheme.StyleLabel(corditeLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        corditeBox.AddChild(corditeLabel);
        _corditeAmount = new SpinBox();
        _corditeAmount.MinValue = 1000;
        _corditeAmount.MaxValue = 50000;
        _corditeAmount.Step = 1000;
        _corditeAmount.Value = 10000;
        _corditeAmount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        corditeBox.AddChild(_corditeAmount);
    }

    private void BuildStatusBar()
    {
        _statusLabel = new Label();
        _statusLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _statusLabel.OffsetTop = -20;
        _statusLabel.OffsetBottom = 0;
        _statusLabel.Text = "Ready | Click to edit terrain";
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_statusLabel);
    }

    // ── Input Handling ─────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        // Camera movement with WASD
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            float moveSpeed = 2f;
            Vector3 move = Vector3.Zero;

            switch (keyEvent.Keycode)
            {
                case Key.W: move = Vector3.Forward * moveSpeed; break;
                case Key.S: move = Vector3.Back * moveSpeed; break;
                case Key.A: move = Vector3.Left * moveSpeed; break;
                case Key.D: move = Vector3.Right * moveSpeed; break;
                case Key.Q: move = Vector3.Up * moveSpeed; break;
                case Key.E: move = Vector3.Down * moveSpeed; break;

                // Keyboard shortcuts
                case Key.Z when keyEvent.CtrlPressed:
                    if (keyEvent.ShiftPressed) _editor.Redo();
                    else _editor.Undo();
                    return;
                case Key.Y when keyEvent.CtrlPressed:
                    _editor.Redo();
                    return;
            }

            if (move != Vector3.Zero && _camera != null)
            {
                _camera.Position += move;
            }
        }

        // Mouse scroll for camera zoom
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp && _camera != null)
            {
                _camera.Position += Vector3.Down * 3f;
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown && _camera != null)
            {
                _camera.Position += Vector3.Up * 3f;
            }

            // Left click for tool application
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _isDragging = true;
                    ApplyToolAtMouse(mouseButton.Position);
                }
                else
                {
                    _isDragging = false;
                }
            }
        }

        // Mouse motion for dragging
        if (@event is InputEventMouseMotion mouseMotion && _isDragging)
        {
            ApplyToolAtMouse(mouseMotion.Position);
        }
    }

    private void ApplyToolAtMouse(Vector2 screenPos)
    {
        if (_editor == null || _camera == null) return;

        // Raycast from camera through mouse position
        var from = _camera.ProjectRayOrigin(screenPos);
        var dir = _camera.ProjectRayNormal(screenPos);

        // Simple plane intersection at Y=0
        if (MathF.Abs(dir.Y) < 0.001f) return;
        float t = -from.Y / dir.Y;
        if (t < 0) return;

        float worldX = from.X + dir.X * t;
        float worldZ = from.Z + dir.Z * t;
        int gridX = (int)worldX;
        int gridZ = (int)worldZ;

        if (gridX < 0 || gridX >= _editor.MapWidth || gridZ < 0 || gridZ >= _editor.MapHeight)
            return;

        switch (_editor.CurrentTool)
        {
            case EditorTool.TerrainRaise:
            case EditorTool.TerrainLower:
            case EditorTool.TerrainSmooth:
            case EditorTool.TerrainFlatten:
                _editor.ApplyTerrainBrush(gridX, gridZ);
                break;

            case EditorTool.BiomeBrush:
                _editor.ApplyBiomeBrush(gridX, gridZ);
                break;

            case EditorTool.RiverTool:
                if (!_isDragging) return; // Only on click, not drag
                _editor.AddRiverPoint(gridX, gridZ);
                break;

            case EditorTool.BridgeTool:
                _editor.PlaceBridge(gridX, gridZ, 0f);
                break;

            case EditorTool.PropPlacer:
                float rotation = (float)(_propRotation?.Value ?? 0) * Mathf.Pi / 180f;
                float scale = (float)(_propScale?.Value ?? 1.0);
                _editor.PlaceProp(worldX, worldZ, rotation, scale);
                break;

            case EditorTool.StructurePlacer:
                _editor.PlaceStructure(worldX, worldZ,
                    (float)(_propRotation?.Value ?? 0) * Mathf.Pi / 180f,
                    (float)(_propScale?.Value ?? 1.0));
                break;

            case EditorTool.CorditeNodePlacer:
                _editor.PlaceCorditeNode(gridX, gridZ, (int)(_corditeAmount?.Value ?? 10000));
                break;

            case EditorTool.StartingPositionPlacer:
                _editor.PlaceStartingPosition(gridX, gridZ, 0f);
                break;

            case EditorTool.Eraser:
                _editor.EraseAt(worldX, worldZ, _editor.BrushSize);
                break;
        }
    }

    // ── Event Handlers ─────────────────────────────────────────────────────

    private void OnToolButtonPressed(EditorTool tool, Button btn)
    {
        _editor.SetTool(tool);

        // Update button highlighting
        if (_activeToolButton != null)
            UITheme.StyleButton(_activeToolButton);

        UITheme.StyleAccentButton(btn);
        _activeToolButton = btn;

        // Show/hide properties panel based on tool
        bool showProps = tool == EditorTool.PropPlacer || tool == EditorTool.StructurePlacer ||
                         tool == EditorTool.CorditeNodePlacer;
        if (_propertiesPanel != null)
            _propertiesPanel.Visible = showProps;

        UpdateStatus();
    }

    private void OnToolChanged(int tool)
    {
        UpdateStatus();
    }

    private void OnUndoStackChanged(int undoCount, int redoCount)
    {
        if (_undoButton != null) _undoButton.Disabled = undoCount == 0;
        if (_redoButton != null) _redoButton.Disabled = redoCount == 0;
    }

    private void OnMapModified()
    {
        UpdateStatus();
    }

    private void OnBrushSizeChanged(double value)
    {
        _editor.BrushSize = (int)value;
        if (_brushSizeLabel != null)
            _brushSizeLabel.Text = $"Brush Size: {(int)value}";
    }

    private void OnBrushIntensityChanged(double value)
    {
        _editor.BrushIntensity = (float)value;
        if (_brushIntensityLabel != null)
            _brushIntensityLabel.Text = $"Intensity: {value:F2}";
    }

    private void OnBiomeSelected(long index)
    {
        string[] biomes = { "temperate", "desert", "rocky", "coastal", "volcanic" };
        if (index >= 0 && index < biomes.Length)
        {
            _editor.SelectedBiome = biomes[index];
        }
    }

    private void OnPropCategorySelected(long index)
    {
        if (_propList == null) return;
        _propList.Clear();

        var categories = _manifest.GetCategories();
        if (index < 0 || index >= categories.Count) return;

        string category = categories[(int)index];
        _editor.SelectedPropCategory = category;

        // Populate prop list — we need to use FindEntry since we don't have
        // direct access to category entries. We'll iterate the manifest.
        // For now, show the category name as a placeholder approach.
        // The manifest doesn't expose GetEntriesForCategory directly,
        // so we handle this by tracking what the user picks from the ItemList.
        try
        {
            // Use a known approach: the manifest has categories as sorted lists
            // We'll add a small hack: load the JSON directly for category browsing
            PopulatePropListForCategory(category);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MapEditorUI] Failed to populate props for category {category}: {e.Message}");
        }
    }

    private void PopulatePropListForCategory(string category)
    {
        // Read the terrain manifest JSON to get entries per category
        string json;
        using (var file = FileAccess.Open("res://data/terrain_manifest.json", FileAccess.ModeFlags.Read))
        {
            if (file == null) return;
            json = file.GetAsText();
        }

        // Simple parsing: find the category and extract model IDs
        // We use System.Text.Json for structured parsing
        var dict = System.Text.Json.JsonSerializer.Deserialize<
            Dictionary<string, Dictionary<string, object>>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (dict == null || !dict.TryGetValue(category, out var entries)) return;

        foreach (string modelId in entries.Keys)
        {
            _propList.AddItem(modelId);
        }
    }

    private void OnPropSelected(long index)
    {
        if (_propList == null || index < 0 || index >= _propList.ItemCount) return;

        string modelId = _propList.GetItemText((int)index);
        _editor.SelectedPropId = modelId;
        _editor.SelectedStructureId = modelId;
    }

    // ── Top Bar Handlers ───────────────────────────────────────────────────

    private void OnNewMapPressed()
    {
        // Create dialog for new map settings
        _editor.NewMap(128, 128, "temperate");
        _editor.RegenerateTerrain();
        UpdateStatus();
    }

    private void OnSavePressed()
    {
        string filename = _editor.MapId;
        if (_editor.SaveMap(filename))
        {
            if (_statusLabel != null)
                _statusLabel.Text = $"Map saved as {filename}.json";
        }
    }

    private void OnLoadPressed()
    {
        // Open file dialog for loading
        var dialog = new FileDialog();
        dialog.Access = FileDialog.AccessEnum.Resources;
        dialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        dialog.Filters = new[] { "*.json ; Map files" };
        dialog.CurrentDir = "res://data/maps";
        dialog.FileSelected += (path) =>
        {
            _editor.LoadMap(path);
            _editor.RegenerateTerrain();
            UpdateStatus();
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(600, 400));
    }

    private void OnUndoPressed() => _editor.Undo();
    private void OnRedoPressed() => _editor.Redo();

    private void OnTestPlayPressed()
    {
        // Export and save a temp map, then switch to game scene
        _editor.SaveMap("_editor_test");
        if (_statusLabel != null)
            _statusLabel.Text = "Test map saved. Launch from Skirmish to test.";
    }

    private void OnRefreshPressed()
    {
        _editor.RegenerateTerrain();
        if (_statusLabel != null)
            _statusLabel.Text = "Terrain refreshed.";
    }

    private void OnBackToMenuPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
    }

    // ── Status ─────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        if (_statusLabel == null || _editor == null) return;

        string toolName = _editor.CurrentTool.ToString();
        _statusLabel.Text = $"Tool: {toolName} | " +
                            $"Map: {_editor.MapWidth}x{_editor.MapHeight} {_editor.MapBiome} | " +
                            $"Props: {_editor.PropCount} | " +
                            $"Starts: {_editor.StartingPositionCount}";
    }
}
