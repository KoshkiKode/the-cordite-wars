using Godot;
using CorditeWars.Game;
using CorditeWars.Game.Campaign;

namespace CorditeWars.UI;

/// <summary>
/// Full-screen mission briefing overlay shown between the campaign mission
/// list and gameplay. Displays mission name, briefing text, objectives,
/// difficulty, and twist/intel text.
///
/// Usage:
///   Set <see cref="PendingMission"/> and <see cref="PendingConfig"/>, then
///   call <see cref="ShowInScene"/> to inject it into the current scene tree.
///   The "Begin Mission" button applies the config and transitions to gameplay.
/// </summary>
public partial class MissionBriefingScreen : CanvasLayer
{
    // ── Static carriers ──────────────────────────────────────────────

    public static CampaignMission? PendingMission { get; set; }
    public static MatchConfig?     PendingConfig  { get; set; }

    // ── State ────────────────────────────────────────────────────────

    private CampaignMission? _mission;
    private MatchConfig?     _config;

    // ── Factory ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a MissionBriefingScreen overlay as a child of <paramref name="parent"/>.
    /// The pending mission data is read from the static carriers.
    /// </summary>
    public static MissionBriefingScreen ShowInScene(Node parent, CampaignMission mission, MatchConfig config)
    {
        PendingMission = mission;
        PendingConfig  = config;
        var screen = new MissionBriefingScreen();
        parent.AddChild(screen);
        return screen;
    }

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        _mission = PendingMission;
        _config  = PendingConfig;
        PendingMission = null;
        PendingConfig  = null;

        Layer = 40;
        BuildUI();
    }

    public override void _Input(InputEvent @event)
    {
        // Escape goes back (same as Back button)
        if (@event.IsActionPressed("ui_cancel"))
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
        // Dark backdrop
        var bg = new ColorRect();
        bg.Color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
        bg.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        bg.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(bg);

        // Content container (centered column)
        var outer = new CenterContainer();
        outer.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        AddChild(outer);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.CustomMinimumSize = new Vector2(680, 0);
        outer.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   24);
        margin.AddThemeConstantOverride("margin_right",  24);
        margin.AddThemeConstantOverride("margin_top",    24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        // ── Header row: mission number + difficulty ──────────────────
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(headerRow);

        string missionNum = _mission != null
            ? string.Format(Tr("MISSION_NUMBER_FMT"), _mission.Number)
            : Tr("MISSION_NUMBER_FMT").Replace("{0}", string.Empty).Trim();
        var numLabel = new Label();
        numLabel.Text = missionNum;
        UITheme.StyleLabel(numLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        headerRow.AddChild(numLabel);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        headerRow.AddChild(spacer);

        string difficulty = _mission?.DifficultyLabel ?? string.Empty;
        var diffLabel = new Label();
        diffLabel.Text = string.Format(Tr("MISSION_DIFFICULTY_FMT"), difficulty);
        UITheme.StyleLabel(diffLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        headerRow.AddChild(diffLabel);

        // ── Mission name ─────────────────────────────────────────────
        var nameLabel = new Label();
        nameLabel.Text = _mission?.Name ?? string.Empty;
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(nameLabel, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(nameLabel);

        vbox.AddChild(new HSeparator());

        // ── Briefing text (scrollable) ───────────────────────────────
        var briefingScroll = new ScrollContainer();
        briefingScroll.CustomMinimumSize = new Vector2(0, 160);
        vbox.AddChild(briefingScroll);

        var briefingLabel = new Label();
        briefingLabel.Text = _mission?.Briefing ?? string.Empty;
        briefingLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(briefingLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        briefingScroll.AddChild(briefingLabel);

        // ── Objectives ───────────────────────────────────────────────
        if (_mission?.Objectives != null && _mission.Objectives.Count > 0)
        {
            var objHeader = new Label();
            objHeader.Text = Tr("MISSION_OBJECTIVES");
            UITheme.StyleLabel(objHeader, UITheme.FontSizeSmall, UITheme.Accent);
            vbox.AddChild(objHeader);

            for (int i = 0; i < _mission.Objectives.Count; i++)
            {
                var objLabel = new Label();
                objLabel.Text = $"▸ {_mission.Objectives[i]}";
                objLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                UITheme.StyleLabel(objLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
                vbox.AddChild(objLabel);
            }
        }

        // ── Twist / Intel ────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_mission?.Twist))
        {
            vbox.AddChild(new HSeparator());
            var intelHeader = new Label();
            intelHeader.Text = Tr("MISSION_INTELLIGENCE");
            UITheme.StyleLabel(intelHeader, UITheme.FontSizeSmall, new Color(0.9f, 0.7f, 0.1f));
            vbox.AddChild(intelHeader);

            var twistLabel = new Label();
            twistLabel.Text = _mission.Twist;
            twistLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            UITheme.StyleLabel(twistLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
            vbox.AddChild(twistLabel);
        }

        vbox.AddChild(new HSeparator());

        // ── Button row ───────────────────────────────────────────────
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(btnRow);

        var backBtn = new Button();
        backBtn.Text = Tr("MENU_BACK");
        backBtn.CustomMinimumSize = new Vector2(120, 44);
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () => QueueFree();
        btnRow.AddChild(backBtn);

        var spacer2 = new Control();
        spacer2.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        btnRow.AddChild(spacer2);

        var beginBtn = new Button();
        beginBtn.Text = Tr("MISSION_BEGIN");
        beginBtn.CustomMinimumSize = new Vector2(160, 44);
        UITheme.StyleAccentButton(beginBtn);
        beginBtn.Pressed += OnBeginMission;
        btnRow.AddChild(beginBtn);
    }

    private void OnBeginMission()
    {
        if (_config is null) return;
        Main.PendingConfig = _config;
        SceneTransition.TransitionTo(GetTree(), "res://scenes/Game/Main.tscn");
    }
}
