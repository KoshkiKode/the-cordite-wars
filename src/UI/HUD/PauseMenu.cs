using Godot;
using CorditeWars.Core;
using CorditeWars.Game;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Persistence;

namespace CorditeWars.UI.HUD;

/// <summary>
/// In-game pause menu overlay. Toggle via ESC (handled in Main.cs).
/// Provides Resume, Save Game, Load Game, and Quit to Main Menu actions.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    // ── References ───────────────────────────────────────────────────

    private readonly GameSession _session;
    private readonly SaveManager _saveManager = new();
    private AudioManager? _audioManager;

    // ── Children ─────────────────────────────────────────────────────

    private Control _mainPanel = null!;
    private Control _savePanel = null!;
    private Control _loadPanel = null!;
    private Label _statusLabel = null!;

    // ── Constructor ──────────────────────────────────────────────────

    public PauseMenu(GameSession session)
    {
        _session = session;
    }

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
        Layer = 30;
        Visible = false;

        BuildUI();
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Shows the pause menu over the current game view.</summary>
    public new void Show()
    {
        Visible = true;
        ShowMain();
    }

    /// <summary>Hides the pause menu and resumes the simulation.</summary>
    public new void Hide()
    {
        Visible = false;
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
        // Semi-transparent dark backdrop
        var bg = new ColorRect();
        bg.Color = new Color(0.0f, 0.0f, 0.0f, 0.65f);
        bg.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        AddChild(bg);

        // Click-through background to prevent passing clicks to game world
        bg.MouseFilter = Control.MouseFilterEnum.Stop;

        // ── Main pause panel ──────────────────────────────────────────
        _mainPanel = BuildMainPanel();
        AddChild(_mainPanel);

        // ── Save panel ────────────────────────────────────────────────
        _savePanel = BuildSavePanel();
        _savePanel.Visible = false;
        AddChild(_savePanel);

        // ── Load panel ────────────────────────────────────────────────
        _loadPanel = BuildLoadPanel();
        _loadPanel.Visible = false;
        AddChild(_loadPanel);
    }

    private Control BuildMainPanel()
    {
        var center = new CenterContainer();
        center.AnchorsPreset = (int)Control.LayoutPreset.FullRect;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.CustomMinimumSize = new Vector2(340, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = Tr("PAUSE_TITLE");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(title);

        // Separator
        vbox.AddChild(new HSeparator());

        // Resume
        var resumeBtn = new Button();
        resumeBtn.Text = Tr("PAUSE_RESUME");
        resumeBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleAccentButton(resumeBtn);
        resumeBtn.Pressed += OnResumePressed;
        vbox.AddChild(resumeBtn);

        // Save Game
        var saveBtn = new Button();
        saveBtn.Text = Tr("PAUSE_SAVE");
        saveBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleButton(saveBtn);
        saveBtn.Pressed += OnSavePressed;
        vbox.AddChild(saveBtn);

        // Load Game
        var loadBtn = new Button();
        loadBtn.Text = Tr("PAUSE_LOAD");
        loadBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleButton(loadBtn);
        loadBtn.Pressed += OnLoadPressed;
        vbox.AddChild(loadBtn);

        // Separator
        vbox.AddChild(new HSeparator());

        // Quit to Menu
        var quitBtn = new Button();
        quitBtn.Text = Tr("PAUSE_QUIT_TO_MENU");
        quitBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleButton(quitBtn);
        quitBtn.Pressed += OnQuitPressed;
        vbox.AddChild(quitBtn);

        return center;
    }

    private Control BuildSavePanel()
    {
        var center = new CenterContainer();
        center.AnchorsPreset = (int)Control.LayoutPreset.FullRect;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.CustomMinimumSize = new Vector2(400, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = new Label();
        title.Text = Tr("PAUSE_SAVE");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // Save slots 1–3
        for (int i = 1; i <= 3; i++)
        {
            int slotIndex = i;
            var slotBtn = new Button();
            slotBtn.Text = string.Format(Tr("SAVE_SLOT_FMT"), slotIndex);
            slotBtn.CustomMinimumSize = new Vector2(0, 44);
            UITheme.StyleButton(slotBtn);
            slotBtn.Pressed += () => DoSave($"save_{slotIndex}");
            vbox.AddChild(slotBtn);
        }

        vbox.AddChild(new HSeparator());

        // Status label
        _statusLabel = new Label();
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
        vbox.AddChild(_statusLabel);

        // Back
        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        backBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += ShowMain;
        vbox.AddChild(backBtn);

        return center;
    }

    private Control BuildLoadPanel()
    {
        var center = new CenterContainer();
        center.AnchorsPreset = (int)Control.LayoutPreset.FullRect;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.CustomMinimumSize = new Vector2(460, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = new Label();
        title.Text = Tr("PAUSE_LOAD");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // List of existing save slots
        var slots = _saveManager.GetSaveSlots();

        const int maxSlotsShown = 6;
        int shown = 0;

        // Manual saves
        for (int i = 1; i <= 3 && shown < maxSlotsShown; i++, shown++)
        {
            string slotName = $"save_{i}";
            string labelText;
            if (slots.TryGetValue(slotName, out var info))
                labelText = $"Slot {i} — {info.MapDisplayName}  [{info.SaveTimestamp}]";
            else
                labelText = $"Slot {i} — {Tr("LOAD_EMPTY_SLOT")}";

            bool hasData = slots.ContainsKey(slotName);
            string capturedSlot = slotName;

            var slotBtn = new Button();
            slotBtn.Text = labelText;
            slotBtn.CustomMinimumSize = new Vector2(0, 44);
            UITheme.StyleButton(slotBtn);
            slotBtn.Disabled = !hasData;
            slotBtn.Pressed += () => DoLoad(capturedSlot);
            vbox.AddChild(slotBtn);
        }

        // Autosaves
        for (int i = 0; i < 3 && shown < maxSlotsShown; i++, shown++)
        {
            string slotName = $"autosave_{i}";
            string labelText;
            if (slots.TryGetValue(slotName, out var info))
                labelText = $"Autosave {i + 1} — {info.MapDisplayName}  [{info.SaveTimestamp}]";
            else
                labelText = $"Autosave {i + 1} — {Tr("LOAD_EMPTY_SLOT")}";

            bool hasData = slots.ContainsKey(slotName);
            string capturedSlot = slotName;

            var slotBtn = new Button();
            slotBtn.Text = labelText;
            slotBtn.CustomMinimumSize = new Vector2(0, 44);
            UITheme.StyleButton(slotBtn);
            slotBtn.Disabled = !hasData;
            slotBtn.Pressed += () => DoLoad(capturedSlot);
            vbox.AddChild(slotBtn);
        }

        vbox.AddChild(new HSeparator());

        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        backBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += ShowMain;
        vbox.AddChild(backBtn);

        return center;
    }

    // ── Panel switchers ───────────────────────────────────────────────

    private void ShowMain()
    {
        _mainPanel.Visible = true;
        _savePanel.Visible = false;
        _loadPanel.Visible = false;
    }

    private void ShowSave()
    {
        _mainPanel.Visible = false;
        _savePanel.Visible = true;
        _loadPanel.Visible = false;
        if (_statusLabel is not null) _statusLabel.Text = string.Empty;
    }

    private void ShowLoad()
    {
        // Rebuild load panel to refresh slot list each time it's opened
        _loadPanel.QueueFree();
        _loadPanel = BuildLoadPanel();
        AddChild(_loadPanel);

        _mainPanel.Visible = false;
        _savePanel.Visible = false;
        _loadPanel.Visible = true;
    }

    // ── Button handlers ───────────────────────────────────────────────

    private void OnResumePressed()
    {
        _audioManager?.PlayUiSoundById("ui_confirm");
        _session.ResumeMatch();
        Hide();
    }

    private void OnSavePressed()
    {
        _audioManager?.PlayUiSoundById("ui_click");
        ShowSave();
    }

    private void OnLoadPressed()
    {
        _audioManager?.PlayUiSoundById("ui_click");
        ShowLoad();
    }

    private void OnQuitPressed()
    {
        _audioManager?.PlayUiSoundById("ui_cancel");
        // Quit directly to main menu without confirmation dialog
        _session.EndMatch(-1, "Player quit to menu.");
        SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/MainMenu.tscn");
    }

    // ── Save / Load actions ───────────────────────────────────────────

    private void DoSave(string slotName)
    {
        bool ok = _session.SaveCurrentState(slotName);
        if (_statusLabel is not null)
            _statusLabel.Text = ok ? Tr("SAVE_SUCCESS") : Tr("SAVE_FAILED");
        _audioManager?.PlayUiSoundById(ok ? "ui_confirm" : "ui_error");
    }

    private void DoLoad(string slotName)
    {
        Hide();
        // Loading restores full session state; the session handles the transition
        GD.Print($"[PauseMenu] Loading slot: {slotName}");
        var data = _saveManager.LoadGame(slotName);
        if (data is not null)
        {
            _session.LoadFromSave(data);
            _session.ResumeMatch();
        }
        else
        {
            GD.PushWarning($"[PauseMenu] Failed to load slot: {slotName}");
        }
    }
}
