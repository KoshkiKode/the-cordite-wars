using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using CorditeWars.Game.World;

namespace CorditeWars.UI.ModelDesigner;

/// <summary>
/// In-game Model Designer — a lightweight tool for authoring procedural 3D models
/// composed of coloured primitives (Box, Sphere, Cylinder, Cone, Capsule).
///
/// Models are saved to <c>data/props/models/{id}.model.json</c> and loaded at
/// runtime by <see cref="ProceduralModelLoader"/>, replacing the generic placeholder
/// cubes that PropPlacer uses when a model is absent from the TerrainManifest.
///
/// Layout
/// ───────
///   ┌────────────────────────────────────────────────────────┐
///   │  Top bar: model ID field  [New] [Load] [Save]          │
///   ├────────────────┬───────────────────────────────────────┤
///   │  Sidebar       │  3-D Preview  (SubViewportContainer)  │
///   │  ─────────     │                                       │
///   │  [Add…▼]       │                                       │
///   │  primitive list│                                       │
///   │  ─────────     │                                       │
///   │  Property panel│                                       │
///   │  (sel. prim.)  │                                       │
///   └────────────────┴───────────────────────────────────────┘
///
/// To open the designer call <see cref="Open"/> and add the returned node to the scene
/// tree.  Closing it (X button or ESC) removes it automatically.
/// </summary>
public partial class ModelDesignerUI : Control
{
    // ── Layout constants ─────────────────────────────────────────────

    private const int SidebarWidth  = 300;
    private const int TopBarHeight  = 48;

    // ── State ────────────────────────────────────────────────────────

    private ProceduralModelData _model = NewModel();
    private int                 _selectedIndex = -1;   // index into _model.Primitives

    // ── UI nodes ─────────────────────────────────────────────────────

    private LineEdit            _idField        = null!;
    private LineEdit            _nameField      = null!;
    private VBoxContainer       _primList       = null!;
    private VBoxContainer       _propPanel      = null!;
    private Label               _statusLabel    = null!;
    private SubViewportContainer _viewportContainer = null!;
    private SubViewport         _subViewport    = null!;
    private Node3D              _previewRoot    = null!;
    private Node3D?             _previewModel;
    private Camera3D            _camera         = null!;

    // Orbit camera state
    private float _camYaw   = 45f;
    private float _camPitch = -30f;
    private float _camDist  = 4f;
    private bool  _orbiting;
    private Vector2 _orbitLastPos;

    // ── Factory ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="ModelDesignerUI"/> node, fullscreen over its parent.
    /// </summary>
    public static ModelDesignerUI Open()
    {
        var ui = new ModelDesignerUI();
        return ui;
    }

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Dimmed backdrop
        var backdrop = new ColorRect();
        backdrop.Color = new Color(0f, 0f, 0f, 0.6f);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // Main panel
        var panel = new Panel();
        panel.AddThemeStyleboxOverride("panel", MakePanel(new Color(0.12f, 0.12f, 0.14f)));
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 40);
        AddChild(panel);

        // Close button (top-right)
        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize);
        closeBtn.OffsetLeft  = -36;
        closeBtn.OffsetTop   = 44;
        closeBtn.OffsetRight = -4;
        closeBtn.OffsetBottom = 76;
        StylePlainButton(closeBtn);
        closeBtn.Pressed += () => QueueFree();
        AddChild(closeBtn);

        // Outer vertical layout inside the panel
        var outerVBox = new VBoxContainer();
        outerVBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 4);
        panel.AddChild(outerVBox);

        BuildTopBar(outerVBox);

        // Horizontal split: sidebar | preview
        var hSplit = new HSplitContainer();
        hSplit.SizeFlagsVertical = SizeFlags.ExpandFill;
        hSplit.SplitOffsets = [SidebarWidth];
        outerVBox.AddChild(hSplit);

        BuildSidebar(hSplit);
        Build3DPreview(hSplit);

        // Status bar
        _statusLabel = new Label();
        _statusLabel.Text = "New model — add primitives on the left, preview on the right.";
        StyleLabel(_statusLabel, 11, new Color(0.6f, 0.6f, 0.6f));
        outerVBox.AddChild(_statusLabel);

        RefreshPreview();
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }

        if (_viewportContainer == null) return;

        // Orbit with left-drag inside preview
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                var localPos = _viewportContainer.GetLocalMousePosition();
                bool inView = new Rect2(Vector2.Zero, _viewportContainer.Size).HasPoint(localPos);
                if (mb.Pressed && inView)
                {
                    _orbiting   = true;
                    _orbitLastPos = mb.GlobalPosition;
                }
                else
                {
                    _orbiting = false;
                }
            }
            // Scroll to zoom
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var localPos = _viewportContainer.GetLocalMousePosition();
                bool inView = new Rect2(Vector2.Zero, _viewportContainer.Size).HasPoint(localPos);
                if (inView)
                {
                    _camDist = Math.Clamp(
                        _camDist + (mb.ButtonIndex == MouseButton.WheelDown ? 0.4f : -0.4f),
                        1f, 20f);
                    UpdateCamera();
                }
            }
        }
        if (ev is InputEventMouseMotion motion && _orbiting)
        {
            Vector2 delta = motion.GlobalPosition - _orbitLastPos;
            _orbitLastPos = motion.GlobalPosition;
            _camYaw   += delta.X * 0.4f;
            _camPitch  = Math.Clamp(_camPitch - delta.Y * 0.3f, -89f, 89f);
            UpdateCamera();
        }
    }

    // ── UI builders ──────────────────────────────────────────────────

    private void BuildTopBar(VBoxContainer parent)
    {
        var topBar = new HBoxContainer();
        topBar.CustomMinimumSize = new Vector2(0, TopBarHeight);
        topBar.AddThemeConstantOverride("separation", 8);
        parent.AddChild(topBar);

        AddLabel(topBar, "ID:", 12);

        _idField = new LineEdit();
        _idField.PlaceholderText = "my_rock_01";
        _idField.CustomMinimumSize = new Vector2(160, 0);
        _idField.Text = _model.Id;
        _idField.TextChanged += t => _model.Id = t;
        topBar.AddChild(_idField);

        AddLabel(topBar, "Name:", 12);

        _nameField = new LineEdit();
        _nameField.PlaceholderText = "My Rock";
        _nameField.CustomMinimumSize = new Vector2(160, 0);
        _nameField.Text = _model.DisplayName;
        _nameField.TextChanged += t => _model.DisplayName = t;
        topBar.AddChild(_nameField);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topBar.AddChild(spacer);

        var newBtn = MakeButton("NEW", OnNew);
        topBar.AddChild(newBtn);

        var loadBtn = MakeButton("LOAD", OnLoad);
        topBar.AddChild(loadBtn);

        var saveBtn = MakeButton("SAVE", OnSave);
        topBar.AddChild(saveBtn);
    }

    private void BuildSidebar(HSplitContainer parent)
    {
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(SidebarWidth, 0);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        parent.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        // ── Add primitive row ──
        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(addRow);

        AddLabel(addRow, "Add:", 12);

        foreach (PrimitiveShape shape in Enum.GetValues<PrimitiveShape>())
        {
            PrimitiveShape captured = shape;
            var btn = MakeButton(shape.ToString(), () => OnAddPrimitive(captured), minWidth: 72);
            addRow.AddChild(btn);
        }

        // ── Primitives list ──
        AddLabel(vbox, "Primitives", 12, new Color(0.5f, 0.8f, 1f));

        _primList = new VBoxContainer();
        _primList.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(_primList);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        // ── Property panel ──
        AddLabel(vbox, "Properties", 12, new Color(0.5f, 0.8f, 1f));

        _propPanel = new VBoxContainer();
        _propPanel.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_propPanel);

        RefreshPrimList();
    }

    private void Build3DPreview(HSplitContainer parent)
    {
        _viewportContainer = new SubViewportContainer();
        _viewportContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _viewportContainer.SizeFlagsVertical   = SizeFlags.ExpandFill;
        _viewportContainer.Stretch = true;
        parent.AddChild(_viewportContainer);

        _subViewport = new SubViewport();
        _subViewport.TransparentBg = false;
        _subViewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
        _viewportContainer.AddChild(_subViewport);

        // Sky / background colour
        var env = new WorldEnvironment();
        var godotEnv = new Godot.Environment();
        godotEnv.BackgroundMode = Godot.Environment.BGMode.Color;
        godotEnv.BackgroundColor = new Color(0.08f, 0.08f, 0.10f);
        env.Environment = godotEnv;
        _subViewport.AddChild(env);

        // Directional light
        var light = new DirectionalLight3D();
        light.LightEnergy = 1.2f;
        light.RotationDegrees = new Vector3(-45f, 30f, 0f);
        _subViewport.AddChild(light);

        // Ambient fill light (opposite direction, lower energy)
        var fill = new DirectionalLight3D();
        fill.LightEnergy = 0.4f;
        fill.RotationDegrees = new Vector3(45f, -150f, 0f);
        _subViewport.AddChild(fill);

        // Grid helper (Y=0 plane)
        var grid = BuildGridMesh();
        _subViewport.AddChild(grid);

        // Preview model root
        _previewRoot = new Node3D();
        _subViewport.AddChild(_previewRoot);

        // Camera
        _camera = new Camera3D();
        _subViewport.AddChild(_camera);
        UpdateCamera();
    }

    // ── Primitive operations ─────────────────────────────────────────

    private void OnAddPrimitive(PrimitiveShape shape)
    {
        var prim = new ProceduralPrimitive
        {
            Shape    = shape,
            Position = [0f, 0.5f, 0f],
            Scale    = [1f, 1f, 1f],
            Color    = DefaultColorForShape(shape)
        };

        var list = new List<ProceduralPrimitive>(_model.Primitives) { prim };
        _model.Primitives = [.. list];
        _selectedIndex = _model.Primitives.Length - 1;

        RefreshPrimList();
        RefreshPropPanel();
        RefreshPreview();
        SetStatus($"Added {shape} primitive.");
    }

    private void OnRemovePrimitive(int index)
    {
        if (index < 0 || index >= _model.Primitives.Length) return;
        var list = new List<ProceduralPrimitive>(_model.Primitives);
        list.RemoveAt(index);
        _model.Primitives = [.. list];
        _selectedIndex = Math.Clamp(_selectedIndex, -1, _model.Primitives.Length - 1);
        RefreshPrimList();
        RefreshPropPanel();
        RefreshPreview();
    }

    private void OnSelectPrimitive(int index)
    {
        _selectedIndex = index;
        RefreshPrimList();
        RefreshPropPanel();
    }

    private void OnMovePrimitive(int index, int direction)
    {
        int target = index + direction;
        if (target < 0 || target >= _model.Primitives.Length) return;
        var list = new List<ProceduralPrimitive>(_model.Primitives);
        (list[index], list[target]) = (list[target], list[index]);
        _model.Primitives = [.. list];
        _selectedIndex = target;
        RefreshPrimList();
        RefreshPreview();
    }

    // ── File operations ──────────────────────────────────────────────

    private void OnNew()
    {
        _model = NewModel();
        _selectedIndex = -1;
        _idField.Text = _model.Id;
        _nameField.Text = _model.DisplayName;
        RefreshPrimList();
        RefreshPropPanel();
        RefreshPreview();
        SetStatus("New model created.");
    }

    private void OnLoad()
    {
        string id = _idField.Text.Trim();
        if (string.IsNullOrEmpty(id)) { SetStatus("Enter a model ID first."); return; }

        var loaded = ProceduralModelLoader.LoadData(id);
        if (loaded is null) { SetStatus($"Model '{id}' not found in data/props/models/."); return; }

        _model = loaded;
        _selectedIndex = -1;
        _idField.Text   = _model.Id;
        _nameField.Text = _model.DisplayName;
        RefreshPrimList();
        RefreshPropPanel();
        RefreshPreview();
        SetStatus($"Loaded model '{id}' ({_model.Primitives.Length} primitives).");
    }

    private void OnSave()
    {
        _model.Id          = _idField.Text.Trim();
        _model.DisplayName = _nameField.Text.Trim();

        if (string.IsNullOrEmpty(_model.Id)) { SetStatus("Model ID cannot be empty."); return; }

        ProceduralModelLoader.Save(_model);
        SetStatus($"Saved '{_model.Id}' → data/props/models/{_model.Id}.model.json");
    }

    // ── Refresh helpers ──────────────────────────────────────────────

    private void RefreshPrimList()
    {
        foreach (Node child in _primList.GetChildren()) child.QueueFree();

        for (int i = 0; i < _model.Primitives.Length; i++)
        {
            int idx = i;
            var prim = _model.Primitives[i];

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            bool selected = i == _selectedIndex;
            var bg = new Panel();
            bg.AddThemeStyleboxOverride("panel", MakePanel(
                selected ? new Color(0.25f, 0.4f, 0.6f) : new Color(0.18f, 0.18f, 0.2f)));
            bg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bg.CustomMinimumSize = new Vector2(0, 28);
            row.AddChild(bg);

            var colorSwatch = new ColorRect();
            colorSwatch.CustomMinimumSize = new Vector2(16, 16);
            colorSwatch.Color = Color.FromHtml(prim.Color.StartsWith('#') ? prim.Color : "#" + prim.Color);
            colorSwatch.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft, LayoutPresetMode.KeepSize);
            bg.AddChild(colorSwatch);

            var lbl = new Label();
            lbl.Text = $"{i + 1}. {prim.Shape}";
            StyleLabel(lbl, 11, selected ? Colors.White : new Color(0.8f, 0.8f, 0.8f));
            lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            lbl.OffsetLeft = 22;
            bg.AddChild(lbl);

            bg.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    OnSelectPrimitive(idx);
            };

            var upBtn   = MakeSmallButton("▲", () => OnMovePrimitive(idx, -1));
            var downBtn = MakeSmallButton("▼", () => OnMovePrimitive(idx, +1));
            var delBtn  = MakeSmallButton("✕", () => OnRemovePrimitive(idx));
            row.AddChild(upBtn);
            row.AddChild(downBtn);
            row.AddChild(delBtn);

            _primList.AddChild(row);
        }
    }

    private void RefreshPropPanel()
    {
        foreach (Node child in _propPanel.GetChildren()) child.QueueFree();

        if (_selectedIndex < 0 || _selectedIndex >= _model.Primitives.Length)
        {
            AddLabel(_propPanel, "No primitive selected.", 11, new Color(0.5f, 0.5f, 0.5f));
            return;
        }

        var prim = _model.Primitives[_selectedIndex];

        AddVec3Row(_propPanel, "Position", prim.Position,
            v => { prim.Position = v; DirtyPreview(); });
        AddVec3Row(_propPanel, "Rotation °", prim.RotationDeg,
            v => { prim.RotationDeg = v; DirtyPreview(); });
        AddVec3Row(_propPanel, "Scale", prim.Scale,
            v => { prim.Scale = v; DirtyPreview(); });
        AddColorRow(_propPanel, prim.Color,
            c => { prim.Color = c; DirtyPreview(); });
    }

    private bool _previewDirty;
    private void DirtyPreview() => _previewDirty = true;

    public override void _Process(double delta)
    {
        if (_previewDirty) { RefreshPreview(); _previewDirty = false; }
    }

    private void RefreshPreview()
    {
        _previewModel?.QueueFree();
        _previewModel = null;

        if (_model.Primitives.Length > 0)
        {
            _previewModel = ProceduralModelLoader.Build(_model);
            _previewRoot.AddChild(_previewModel);
        }
    }

    private void UpdateCamera()
    {
        float yawRad   = Mathf.DegToRad(_camYaw);
        float pitchRad = Mathf.DegToRad(_camPitch);
        var offset = new Vector3(
            _camDist * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            _camDist * Mathf.Sin(-pitchRad),
            _camDist * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));
        _camera.Position = new Vector3(0f, 0.5f, 0f) + offset;
        _camera.LookAt(new Vector3(0f, 0.5f, 0f), Vector3.Up);
    }

    // ── Property row builders ────────────────────────────────────────

    private void AddVec3Row(VBoxContainer parent, string label, float[]? values, Action<float[]> onChange)
    {
        values ??= [0f, 0f, 0f];
        if (values.Length < 3) values = [0f, 0f, 0f];

        AddLabel(parent, label, 10, new Color(0.7f, 0.7f, 0.7f));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        parent.AddChild(row);

        string[] axes = ["X", "Y", "Z"];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            AddLabel(row, axes[i] + ":", 10);
            var spin = new SpinBox();
            spin.Step = 0.05;
            spin.MinValue = -100;
            spin.MaxValue = 100;
            spin.Value = values[axis];
            spin.CustomMinimumSize = new Vector2(72, 0);
            spin.ValueChanged += v =>
            {
                values[axis] = (float)v;
                onChange(values);
            };
            row.AddChild(spin);
        }
    }

    private static void AddColorRow(VBoxContainer parent, string current, Action<string> onChange)
    {
        AddLabel(parent, "Color (HTML)", 10, new Color(0.7f, 0.7f, 0.7f));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        Color parsed = Color.FromHtml(current.StartsWith('#') ? current : "#" + current);

        var picker = new ColorPickerButton();
        picker.Color = parsed;
        picker.CustomMinimumSize = new Vector2(140, 28);
        picker.ColorChanged += c => onChange(c.ToHtml(false));
        row.AddChild(picker);

        var field = new LineEdit();
        field.Text = current;
        field.CustomMinimumSize = new Vector2(90, 0);
        field.TextSubmitted += t =>
        {
            onChange(t.StartsWith('#') ? t : "#" + t);
            picker.Color = Color.FromHtml(t.StartsWith('#') ? t : "#" + t);
        };
        row.AddChild(field);

        picker.ColorChanged += c =>
        {
            field.Text = "#" + c.ToHtml(false);
        };
    }

    private void SetStatus(string msg) => _statusLabel.Text = msg;

    // ── Grid helper mesh ─────────────────────────────────────────────

    private static MeshInstance3D BuildGridMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        st.SetColor(new Color(0.3f, 0.3f, 0.3f));

        for (int i = -5; i <= 5; i++)
        {
            st.AddVertex(new Vector3(i, 0, -5));
            st.AddVertex(new Vector3(i, 0,  5));
            st.AddVertex(new Vector3(-5, 0, i));
            st.AddVertex(new Vector3( 5, 0, i));
        }

        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.VertexColorUseAsAlbedo = true;

        var mi = new MeshInstance3D();
        mi.Mesh = st.Commit();
        mi.MaterialOverride = mat;
        return mi;
    }

    // ── Styling helpers ──────────────────────────────────────────────

    private static StyleBoxFlat MakePanel(Color bg)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = bg;
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight =
        sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 4;
        return sb;
    }

    private static void StyleLabel(Label lbl, int fontSize, Color color)
    {
        lbl.AddThemeColorOverride("font_color", color);
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private static void StylePlainButton(Button btn)
    {
        var flat = new StyleBoxFlat();
        flat.BgColor = new Color(0.3f, 0.3f, 0.3f, 0.7f);
        flat.CornerRadiusTopLeft = flat.CornerRadiusTopRight =
        flat.CornerRadiusBottomLeft = flat.CornerRadiusBottomRight = 4;
        btn.AddThemeStyleboxOverride("normal", flat);
    }

    private static Button MakeButton(string text, Action onPressed, int minWidth = 70)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(minWidth, 32);
        StylePlainButton(btn);
        btn.Pressed += onPressed;
        return btn;
    }

    private static Button MakeSmallButton(string text, Action onPressed)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(24, 24);
        StylePlainButton(btn);
        btn.Pressed += onPressed;
        return btn;
    }

    private static void AddLabel(Control parent, string text, int fontSize,
        Color? color = null)
    {
        var lbl = new Label();
        lbl.Text = text;
        StyleLabel(lbl, fontSize, color ?? new Color(0.85f, 0.85f, 0.85f));
        parent.AddChild(lbl);
    }

    // ── Misc helpers ─────────────────────────────────────────────────

    private static ProceduralModelData NewModel() => new()
    {
        Id           = "new_model",
        DisplayName  = "New Model",
        Category     = "misc",
        Primitives   = []
    };

    private static string DefaultColorForShape(PrimitiveShape shape) => shape switch
    {
        PrimitiveShape.Sphere   => "#7A9F6A",
        PrimitiveShape.Cylinder => "#9F8A6A",
        PrimitiveShape.Cone     => "#9F8A6A",
        PrimitiveShape.Capsule  => "#8A9FAF",
        _                       => "#808080"
    };
}
