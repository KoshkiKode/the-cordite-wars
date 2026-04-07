using Godot;
using UnnamedRTS.Systems.Graphics;
using UnnamedRTS.UI.Input;

namespace UnnamedRTS.UI;

/// <summary>
/// Options menu with 5 tabs: Display, Audio, Controls, Game, Accessibility.
/// All settings persist to user://settings.cfg via ConfigFile.
/// Integrates with QualityManager, AudioManager, KeybindManager, and AccessibilitySettings.
/// </summary>
public partial class OptionsMenu : Control
{
    private const string SettingsPath = "user://settings.cfg";

    // ── Display controls ──────────────────────────────────────────────
    private OptionButton _qualityPreset = null!;
    private OptionButton _resolution = null!;
    private CheckBox _fullscreen = null!;
    private CheckBox _vsync = null!;
    private HSlider _shadowQuality = null!;
    private Label _shadowLabel = null!;
    private OptionButton _antiAliasing = null!;
    private HSlider _drawDistance = null!;
    private Label _drawDistanceLabel = null!;

    // ── Audio controls ────────────────────────────────────────────────
    private HSlider _masterVolume = null!;
    private Label _masterVolumeLabel = null!;
    private HSlider _musicVolume = null!;
    private Label _musicVolumeLabel = null!;
    private HSlider _sfxVolume = null!;
    private Label _sfxVolumeLabel = null!;

    // ── Controls controls ─────────────────────────────────────────────
    private HSlider _panSpeed = null!;
    private Label _panSpeedLabel = null!;
    private CheckBox _edgeScroll = null!;
    private HSlider _edgeScrollSpeed = null!;
    private Label _edgeScrollSpeedLabel = null!;
    private CheckBox _mouseZoom = null!;
    private HSlider _zoomSpeed = null!;
    private Label _zoomSpeedLabel = null!;

    // ── Game controls ─────────────────────────────────────────────────
    private CheckBox _showFps = null!;
    private CheckBox _showHealthBars = null!;
    private OptionButton _minimapSize = null!;
    private CheckBox _autoSaveReplays = null!;

    // ── Accessibility controls ────────────────────────────────────────
    private OptionButton _contrastMode = null!;
    private readonly System.Collections.Generic.Dictionary<KeybindManager.GameAction, Button> _keybindButtons = new();
    private KeybindManager.GameAction? _waitingForKey;
    private Button? _waitingButton;

    private static readonly string[] QualityNames = { "Potato", "Low", "Medium", "High", "Custom" };
    private static readonly string[] ResolutionLabels = { "1280x720", "1920x1080", "2560x1440", "3840x2160" };
    private static readonly Vector2I[] Resolutions = { new(1280, 720), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    private static readonly string[] AANames = { "Off", "FXAA", "MSAA 2x", "MSAA 4x" };
    private static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High" };
    private static readonly string[] MinimapNames = { "Small", "Medium", "Large" };

    private bool _suppressPresetChange;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Outer margin
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 80);
        margin.AddThemeConstantOverride("margin_right", 80);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(outerVBox);

        // Header row: Back + Title
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        outerVBox.AddChild(header);

        var backBtn = new Button();
        backBtn.Text = "\u25C4 BACK";
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += OnBackPressed;
        header.AddChild(backBtn);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(spacer);

        var title = new Label();
        title.Text = "OPTIONS";
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // Tab container
        var tabs = new TabContainer();
        tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
        UITheme.StyleTabContainer(tabs);
        outerVBox.AddChild(tabs);

        // Build each tab
        tabs.AddChild(BuildDisplayTab());
        tabs.AddChild(BuildAudioTab());
        tabs.AddChild(BuildControlsTab());
        tabs.AddChild(BuildGameTab());
        tabs.AddChild(BuildAccessibilityTab());

        // Bottom buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 16);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        outerVBox.AddChild(btnRow);

        var applyBtn = new Button();
        applyBtn.Text = "APPLY";
        applyBtn.CustomMinimumSize = new Vector2(160, 0);
        UITheme.StyleAccentButton(applyBtn);
        applyBtn.Pressed += OnApplyPressed;
        btnRow.AddChild(applyBtn);

        var resetBtn = new Button();
        resetBtn.Text = "RESET TO DEFAULTS";
        resetBtn.CustomMinimumSize = new Vector2(220, 0);
        UITheme.StyleButton(resetBtn);
        resetBtn.Pressed += OnResetPressed;
        btnRow.AddChild(resetBtn);

        // Load saved settings
        LoadSettings();
    }

    // ── Tab Builders ──────────────────────────────────────────────────

    private Control BuildDisplayTab()
    {
        var scroll = new ScrollContainer();
        scroll.Name = "Display";
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        // Quality Preset
        _qualityPreset = new OptionButton();
        for (int i = 0; i < QualityNames.Length; i++)
            _qualityPreset.AddItem(QualityNames[i], i);
        _qualityPreset.Selected = 2; // Medium
        UITheme.StyleOptionButton(_qualityPreset);
        _qualityPreset.ItemSelected += OnQualityPresetChanged;
        vbox.AddChild(MakeSettingRow("Quality Preset:", _qualityPreset));

        // Resolution
        _resolution = new OptionButton();
        for (int i = 0; i < ResolutionLabels.Length; i++)
            _resolution.AddItem(ResolutionLabels[i], i);
        _resolution.Selected = 1; // 1920x1080
        UITheme.StyleOptionButton(_resolution);
        _resolution.ItemSelected += _ => MarkCustomPreset();
        vbox.AddChild(MakeSettingRow("Resolution:", _resolution));

        // Fullscreen
        _fullscreen = new CheckBox();
        _fullscreen.Text = "Enabled";
        _fullscreen.ButtonPressed = true;
        UITheme.StyleCheckBox(_fullscreen);
        _fullscreen.Toggled += _ => MarkCustomPreset();
        vbox.AddChild(MakeSettingRow("Fullscreen:", _fullscreen));

        // VSync
        _vsync = new CheckBox();
        _vsync.Text = "Enabled";
        _vsync.ButtonPressed = true;
        UITheme.StyleCheckBox(_vsync);
        _vsync.Toggled += _ => MarkCustomPreset();
        vbox.AddChild(MakeSettingRow("VSync:", _vsync));

        // Shadow Quality
        var shadowRow = new HBoxContainer();
        shadowRow.AddThemeConstantOverride("separation", 12);
        _shadowQuality = new HSlider();
        _shadowQuality.MinValue = 0;
        _shadowQuality.MaxValue = 3;
        _shadowQuality.Step = 1;
        _shadowQuality.Value = 2;
        _shadowQuality.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleSlider(_shadowQuality);
        _shadowLabel = new Label();
        _shadowLabel.Text = "Medium";
        UITheme.StyleLabel(_shadowLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        _shadowQuality.ValueChanged += v =>
        {
            _shadowLabel.Text = ShadowNames[(int)v];
            MarkCustomPreset();
        };
        shadowRow.AddChild(_shadowQuality);
        shadowRow.AddChild(_shadowLabel);
        vbox.AddChild(MakeSettingRow("Shadow Quality:", shadowRow));

        // Anti-Aliasing
        _antiAliasing = new OptionButton();
        for (int i = 0; i < AANames.Length; i++)
            _antiAliasing.AddItem(AANames[i], i);
        _antiAliasing.Selected = 1; // FXAA
        UITheme.StyleOptionButton(_antiAliasing);
        _antiAliasing.ItemSelected += _ => MarkCustomPreset();
        vbox.AddChild(MakeSettingRow("Anti-Aliasing:", _antiAliasing));

        // Draw Distance
        var ddRow = new HBoxContainer();
        ddRow.AddThemeConstantOverride("separation", 12);
        _drawDistance = new HSlider();
        _drawDistance.MinValue = 0.4f;
        _drawDistance.MaxValue = 1.2f;
        _drawDistance.Step = 0.1f;
        _drawDistance.Value = 1.0f;
        _drawDistance.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleSlider(_drawDistance);
        _drawDistanceLabel = new Label();
        _drawDistanceLabel.Text = "100%";
        UITheme.StyleLabel(_drawDistanceLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        _drawDistance.ValueChanged += v =>
        {
            _drawDistanceLabel.Text = $"{(int)(v * 100)}%";
            MarkCustomPreset();
        };
        ddRow.AddChild(_drawDistance);
        ddRow.AddChild(_drawDistanceLabel);
        vbox.AddChild(MakeSettingRow("Draw Distance:", ddRow));

        return scroll;
    }

    private Control BuildAudioTab()
    {
        var scroll = new ScrollContainer();
        scroll.Name = "Audio";
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        // Master Volume
        _masterVolume = MakeVolumeSlider(out _masterVolumeLabel, 1.0f);
        vbox.AddChild(MakeSettingRow("Master Volume:", MakeSliderRow(_masterVolume, _masterVolumeLabel)));

        // Music Volume
        _musicVolume = MakeVolumeSlider(out _musicVolumeLabel, 0.7f);
        vbox.AddChild(MakeSettingRow("Music Volume:", MakeSliderRow(_musicVolume, _musicVolumeLabel)));

        // SFX Volume
        _sfxVolume = MakeVolumeSlider(out _sfxVolumeLabel, 1.0f);
        vbox.AddChild(MakeSettingRow("SFX Volume:", MakeSliderRow(_sfxVolume, _sfxVolumeLabel)));

        return scroll;
    }

    private Control BuildControlsTab()
    {
        var scroll = new ScrollContainer();
        scroll.Name = "Controls";
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        // Camera Pan Speed
        _panSpeed = MakeSpeedSlider(out _panSpeedLabel, 0.5f);
        vbox.AddChild(MakeSettingRow("Camera Pan Speed:", MakeSliderRow(_panSpeed, _panSpeedLabel)));

        // Edge Scroll
        _edgeScroll = new CheckBox();
        _edgeScroll.Text = "Enabled";
        _edgeScroll.ButtonPressed = true;
        UITheme.StyleCheckBox(_edgeScroll);
        vbox.AddChild(MakeSettingRow("Edge Scroll:", _edgeScroll));

        // Edge Scroll Speed
        _edgeScrollSpeed = MakeSpeedSlider(out _edgeScrollSpeedLabel, 0.5f);
        vbox.AddChild(MakeSettingRow("Edge Scroll Speed:", MakeSliderRow(_edgeScrollSpeed, _edgeScrollSpeedLabel)));

        // Mouse Scroll Zoom
        _mouseZoom = new CheckBox();
        _mouseZoom.Text = "Enabled";
        _mouseZoom.ButtonPressed = true;
        UITheme.StyleCheckBox(_mouseZoom);
        vbox.AddChild(MakeSettingRow("Mouse Scroll Zoom:", _mouseZoom));

        // Zoom Speed
        _zoomSpeed = MakeSpeedSlider(out _zoomSpeedLabel, 0.5f);
        vbox.AddChild(MakeSettingRow("Zoom Speed:", MakeSliderRow(_zoomSpeed, _zoomSpeedLabel)));

        return scroll;
    }

    private Control BuildGameTab()
    {
        var scroll = new ScrollContainer();
        scroll.Name = "Game";
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        // Show FPS Counter
        _showFps = new CheckBox();
        _showFps.Text = "Show";
        _showFps.ButtonPressed = false;
        UITheme.StyleCheckBox(_showFps);
        vbox.AddChild(MakeSettingRow("FPS Counter:", _showFps));

        // Show Unit Health Bars
        _showHealthBars = new CheckBox();
        _showHealthBars.Text = "Show";
        _showHealthBars.ButtonPressed = true;
        UITheme.StyleCheckBox(_showHealthBars);
        vbox.AddChild(MakeSettingRow("Unit Health Bars:", _showHealthBars));

        // Mini-map Size
        _minimapSize = new OptionButton();
        for (int i = 0; i < MinimapNames.Length; i++)
            _minimapSize.AddItem(MinimapNames[i], i);
        _minimapSize.Selected = 1; // Medium
        UITheme.StyleOptionButton(_minimapSize);
        vbox.AddChild(MakeSettingRow("Mini-map Size:", _minimapSize));

        // Auto-save Replays
        _autoSaveReplays = new CheckBox();
        _autoSaveReplays.Text = "Enabled";
        _autoSaveReplays.ButtonPressed = true;
        UITheme.StyleCheckBox(_autoSaveReplays);
        vbox.AddChild(MakeSettingRow("Auto-save Replays:", _autoSaveReplays));

        return scroll;
    }

    private Control BuildAccessibilityTab()
    {
        var scroll = new ScrollContainer();
        scroll.Name = "Accessibility";
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        // ── High Contrast Mode ──────────────────────────────────────
        var contrastLabel = new Label();
        contrastLabel.Text = "HIGH CONTRAST";
        UITheme.StyleLabel(contrastLabel, UITheme.FontSizeHeading, UITheme.Accent);
        vbox.AddChild(contrastLabel);

        _contrastMode = new OptionButton();
        for (int i = 0; i < AccessibilitySettings.ContrastModeNames.Length; i++)
            _contrastMode.AddItem(AccessibilitySettings.ContrastModeNames[i], i);
        _contrastMode.Selected = (int)(AccessibilitySettings.Instance?.CurrentContrastMode
            ?? AccessibilitySettings.ContrastMode.Normal);
        UITheme.StyleOptionButton(_contrastMode);
        vbox.AddChild(MakeSettingRow("Contrast Mode:", _contrastMode));

        // Separator
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 20);
        vbox.AddChild(sep);

        // ── Keybind Remapping ───────────────────────────────────────
        var keybindLabel = new Label();
        keybindLabel.Text = "KEYBIND REMAPPING";
        UITheme.StyleLabel(keybindLabel, UITheme.FontSizeHeading, UITheme.Accent);
        vbox.AddChild(keybindLabel);

        var hint = new Label();
        hint.Text = "Click a key button to rebind, then press the new key. Conflicts are resolved automatically.";
        UITheme.StyleLabel(hint, UITheme.FontSizeSmall, UITheme.TextSecondary);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        var km = KeybindManager.Instance;
        _keybindButtons.Clear();

        // Unit commands
        AddKeybindSection(vbox, "Unit Commands");
        AddKeybindRow(vbox, km, KeybindManager.GameAction.AttackMove);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.Stop);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.HoldPosition);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.Patrol);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.CancelMode);

        // Control groups
        AddKeybindSection(vbox, "Control Groups");
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup1);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup2);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup3);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup4);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup5);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup6);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup7);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup8);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup9);
        AddKeybindRow(vbox, km, KeybindManager.GameAction.ControlGroup0);

        // Reset keybinds button
        var resetKeybindsBtn = new Button();
        resetKeybindsBtn.Text = "RESET KEYBINDS TO DEFAULTS";
        resetKeybindsBtn.CustomMinimumSize = new Vector2(280, 0);
        UITheme.StyleButton(resetKeybindsBtn);
        resetKeybindsBtn.Pressed += OnResetKeybindsPressed;
        vbox.AddChild(resetKeybindsBtn);

        return scroll;
    }

    private static void AddKeybindSection(VBoxContainer vbox, string title)
    {
        var label = new Label();
        label.Text = title;
        UITheme.StyleLabel(label, UITheme.FontSizeLarge, UITheme.TextSecondary);
        vbox.AddChild(label);
    }

    private void AddKeybindRow(VBoxContainer vbox, KeybindManager? km, KeybindManager.GameAction action)
    {
        Key currentKey = km?.GetKey(action) ?? KeybindManager.GetDefaultKey(action);

        var btn = new Button();
        btn.Text = KeybindManager.GetKeyName(currentKey);
        btn.CustomMinimumSize = new Vector2(120, 0);
        UITheme.StyleButton(btn);
        btn.Pressed += () => OnKeybindButtonPressed(action, btn);

        _keybindButtons[action] = btn;
        vbox.AddChild(MakeSettingRow(KeybindManager.GetActionLabel(action) + ":", btn));
    }

    private void OnKeybindButtonPressed(KeybindManager.GameAction action, Button btn)
    {
        // Cancel previous wait if any
        if (_waitingButton is not null)
            _waitingButton.Text = KeybindManager.GetKeyName(
                KeybindManager.Instance?.GetKey(_waitingForKey!.Value) ?? Key.None);

        _waitingForKey = action;
        _waitingButton = btn;
        btn.Text = "[ Press a key... ]";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_waitingForKey is null || @event is not InputEventKey keyEvent || !keyEvent.Pressed)
            return;

        var km = KeybindManager.Instance;
        if (km is null) return;

        Key newKey = keyEvent.Keycode;
        var action = _waitingForKey.Value;

        // Allow unbinding with Delete/Backspace
        if (newKey == Key.Delete || newKey == Key.Backspace)
            newKey = Key.None;

        var displaced = km.SetKey(action, newKey);

        // Update the button we just set
        _waitingButton!.Text = KeybindManager.GetKeyName(newKey);

        // If a conflict was resolved, update that button too
        if (displaced.HasValue && _keybindButtons.TryGetValue(displaced.Value, out Button? conflictBtn))
            conflictBtn.Text = KeybindManager.GetKeyName(Key.None);

        _waitingForKey = null;
        _waitingButton = null;
        GetViewport().SetInputAsHandled();
    }

    private void OnResetKeybindsPressed()
    {
        var km = KeybindManager.Instance;
        if (km is null) return;

        km.ResetToDefaults();

        // Refresh all buttons
        foreach (var kvp in _keybindButtons)
            kvp.Value.Text = KeybindManager.GetKeyName(km.GetKey(kvp.Key));
    }

    // ── UI Helpers ────────────────────────────────────────────────────

    private static HBoxContainer MakeSettingRow(string labelText, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var label = new Label();
        label.Text = labelText;
        label.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleLabel(label, UITheme.FontSizeNormal, UITheme.TextPrimary);
        row.AddChild(label);

        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);

        return row;
    }

    private static HBoxContainer MakeSliderRow(HSlider slider, Label valueLabel)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(slider);
        valueLabel.CustomMinimumSize = new Vector2(50, 0);
        row.AddChild(valueLabel);
        return row;
    }

    private HSlider MakeVolumeSlider(out Label valueLabel, float defaultValue)
    {
        var slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 1;
        slider.Step = 0.05f;
        slider.Value = defaultValue;
        slider.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleSlider(slider);

        valueLabel = new Label();
        valueLabel.Text = $"{(int)(defaultValue * 100)}%";
        UITheme.StyleLabel(valueLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);

        var lbl = valueLabel;
        slider.ValueChanged += v => lbl.Text = $"{(int)(v * 100)}%";

        return slider;
    }

    private HSlider MakeSpeedSlider(out Label valueLabel, float defaultValue)
    {
        var slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 1;
        slider.Step = 0.05f;
        slider.Value = defaultValue;
        slider.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleSlider(slider);

        valueLabel = new Label();
        UITheme.StyleLabel(valueLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);

        var lbl = valueLabel;
        slider.ValueChanged += v =>
        {
            string name = v < 0.33f ? "Slow" : v < 0.66f ? "Medium" : "Fast";
            lbl.Text = name;
        };
        // Trigger initial label
        valueLabel.Text = defaultValue < 0.33f ? "Slow" : defaultValue < 0.66f ? "Medium" : "Fast";

        return slider;
    }

    // ── Preset Logic ──────────────────────────────────────────────────

    private void MarkCustomPreset()
    {
        if (_suppressPresetChange) return;
        _qualityPreset.Selected = 4; // Custom
    }

    private void OnQualityPresetChanged(long index)
    {
        if (index == 4) return; // Custom — no auto-apply

        _suppressPresetChange = true;

        switch ((int)index)
        {
            case 0: // Potato
                _shadowQuality.Value = 0;
                _antiAliasing.Selected = 0;
                _drawDistance.Value = 0.6f;
                break;
            case 1: // Low
                _shadowQuality.Value = 1;
                _antiAliasing.Selected = 0;
                _drawDistance.Value = 0.8f;
                break;
            case 2: // Medium
                _shadowQuality.Value = 2;
                _antiAliasing.Selected = 1;
                _drawDistance.Value = 1.0f;
                break;
            case 3: // High
                _shadowQuality.Value = 3;
                _antiAliasing.Selected = 3;
                _drawDistance.Value = 1.2f;
                break;
        }

        _suppressPresetChange = false;
    }

    // ── Actions ───────────────────────────────────────────────────────

    private void OnApplyPressed()
    {
        ApplyDisplaySettings();
        ApplyAudioSettings();
        ApplyAccessibilitySettings();
        SaveSettings();
        GD.Print("[OptionsMenu] Settings applied and saved.");
    }

    private void OnResetPressed()
    {
        _suppressPresetChange = true;

        // Display defaults
        _qualityPreset.Selected = 2;
        _resolution.Selected = 1;
        _fullscreen.ButtonPressed = true;
        _vsync.ButtonPressed = true;
        _shadowQuality.Value = 2;
        _antiAliasing.Selected = 1;
        _drawDistance.Value = 1.0f;

        // Audio defaults
        _masterVolume.Value = 1.0f;
        _musicVolume.Value = 0.7f;
        _sfxVolume.Value = 1.0f;

        // Controls defaults
        _panSpeed.Value = 0.5f;
        _edgeScroll.ButtonPressed = true;
        _edgeScrollSpeed.Value = 0.5f;
        _mouseZoom.ButtonPressed = true;
        _zoomSpeed.Value = 0.5f;

        // Game defaults
        _showFps.ButtonPressed = false;
        _showHealthBars.ButtonPressed = true;
        _minimapSize.Selected = 1;
        _autoSaveReplays.ButtonPressed = true;

        // Accessibility defaults
        _contrastMode.Selected = 0;
        OnResetKeybindsPressed();

        _suppressPresetChange = false;

        ApplyDisplaySettings();
        ApplyAudioSettings();
        ApplyAccessibilitySettings();
        SaveSettings();
        GD.Print("[OptionsMenu] Settings reset to defaults.");
    }

    private void OnBackPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
    }

    // ── Apply to Systems ──────────────────────────────────────────────

    private void ApplyDisplaySettings()
    {
        // Quality tier
        int presetIdx = _qualityPreset.Selected;
        if (presetIdx >= 0 && presetIdx < 4)
        {
            var qm = QualityManager.Instance;
            if (qm != null)
                qm.ApplyTier((QualityTier)presetIdx);
        }

        // Resolution + Fullscreen
        int resIdx = _resolution.Selected;
        if (resIdx >= 0 && resIdx < Resolutions.Length)
        {
            if (_fullscreen.ButtonPressed)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
            }
            else
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(Resolutions[resIdx]);
            }
        }

        // VSync
        DisplayServer.WindowSetVsyncMode(
            _vsync.ButtonPressed ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled
        );

        // Individual overrides
        var qmInst = QualityManager.Instance;
        if (qmInst != null)
        {
            qmInst.SetShadowQuality((int)_shadowQuality.Value);
            qmInst.SetAntiAliasing(_antiAliasing.Selected);
            qmInst.SetDrawDistance((float)_drawDistance.Value);
        }
    }

    private void ApplyAudioSettings()
    {
        var audio = GetNodeOrNull<Node>("/root/AudioManager");
        if (audio == null) return;

        // Call via reflection-free approach — cast to AudioManager
        if (audio is Systems.Audio.AudioManager am)
        {
            am.SetMasterVolume((float)_masterVolume.Value);
            am.SetMusicVolume((float)_musicVolume.Value);
            am.SetSfxVolume((float)_sfxVolume.Value);
        }
    }

    private void ApplyAccessibilitySettings()
    {
        // Apply contrast mode
        var accessibility = AccessibilitySettings.Instance;
        if (accessibility is not null)
        {
            accessibility.CurrentContrastMode =
                (AccessibilitySettings.ContrastMode)_contrastMode.Selected;
            accessibility.Save();

            Core.EventBus.Instance?.EmitHighContrastChanged(_contrastMode.Selected);
        }

        // Save keybinds
        KeybindManager.Instance?.Save();
        Core.EventBus.Instance?.EmitKeybindsChanged();
    }

    // ── Persistence ───────────────────────────────────────────────────

    private void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath); // Load existing to avoid clobbering

        // Display
        cfg.SetValue("Display", "quality_preset", _qualityPreset.Selected);
        cfg.SetValue("Display", "resolution", _resolution.Selected);
        cfg.SetValue("Display", "fullscreen", _fullscreen.ButtonPressed);
        cfg.SetValue("Display", "vsync", _vsync.ButtonPressed);
        cfg.SetValue("Display", "shadow_quality", (int)_shadowQuality.Value);
        cfg.SetValue("Display", "anti_aliasing", _antiAliasing.Selected);
        cfg.SetValue("Display", "draw_distance", (float)_drawDistance.Value);

        // Audio
        cfg.SetValue("Audio", "master_volume", (float)_masterVolume.Value);
        cfg.SetValue("Audio", "music_volume", (float)_musicVolume.Value);
        cfg.SetValue("Audio", "sfx_volume", (float)_sfxVolume.Value);

        // Controls
        cfg.SetValue("Controls", "pan_speed", (float)_panSpeed.Value);
        cfg.SetValue("Controls", "edge_scroll", _edgeScroll.ButtonPressed);
        cfg.SetValue("Controls", "edge_scroll_speed", (float)_edgeScrollSpeed.Value);
        cfg.SetValue("Controls", "mouse_zoom", _mouseZoom.ButtonPressed);
        cfg.SetValue("Controls", "zoom_speed", (float)_zoomSpeed.Value);

        // Game
        cfg.SetValue("Game", "show_fps", _showFps.ButtonPressed);
        cfg.SetValue("Game", "show_health_bars", _showHealthBars.ButtonPressed);
        cfg.SetValue("Game", "minimap_size", _minimapSize.Selected);
        cfg.SetValue("Game", "auto_save_replays", _autoSaveReplays.ButtonPressed);

        var err = cfg.Save(SettingsPath);
        if (err != Error.Ok)
            GD.PrintErr($"[OptionsMenu] Failed to save settings: {err}");
    }

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return; // No saved settings, use defaults

        _suppressPresetChange = true;

        // Display
        _qualityPreset.Selected = (int)cfg.GetValue("Display", "quality_preset", 2);
        _resolution.Selected = (int)cfg.GetValue("Display", "resolution", 1);
        _fullscreen.ButtonPressed = (bool)cfg.GetValue("Display", "fullscreen", true);
        _vsync.ButtonPressed = (bool)cfg.GetValue("Display", "vsync", true);
        _shadowQuality.Value = (int)cfg.GetValue("Display", "shadow_quality", 2);
        _antiAliasing.Selected = (int)cfg.GetValue("Display", "anti_aliasing", 1);
        _drawDistance.Value = (float)cfg.GetValue("Display", "draw_distance", 1.0f);

        // Audio
        _masterVolume.Value = (float)cfg.GetValue("Audio", "master_volume", 1.0f);
        _musicVolume.Value = (float)cfg.GetValue("Audio", "music_volume", 0.7f);
        _sfxVolume.Value = (float)cfg.GetValue("Audio", "sfx_volume", 1.0f);

        // Controls
        _panSpeed.Value = (float)cfg.GetValue("Controls", "pan_speed", 0.5f);
        _edgeScroll.ButtonPressed = (bool)cfg.GetValue("Controls", "edge_scroll", true);
        _edgeScrollSpeed.Value = (float)cfg.GetValue("Controls", "edge_scroll_speed", 0.5f);
        _mouseZoom.ButtonPressed = (bool)cfg.GetValue("Controls", "mouse_zoom", true);
        _zoomSpeed.Value = (float)cfg.GetValue("Controls", "zoom_speed", 0.5f);

        // Game
        _showFps.ButtonPressed = (bool)cfg.GetValue("Game", "show_fps", false);
        _showHealthBars.ButtonPressed = (bool)cfg.GetValue("Game", "show_health_bars", true);
        _minimapSize.Selected = (int)cfg.GetValue("Game", "minimap_size", 1);
        _autoSaveReplays.ButtonPressed = (bool)cfg.GetValue("Game", "auto_save_replays", true);

        _suppressPresetChange = false;
    }
}
