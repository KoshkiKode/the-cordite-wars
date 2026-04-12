using Godot;
using CorditeWars.Systems.Audio;

namespace CorditeWars.UI;

/// <summary>
/// Tutorial mission-selection screen: three mission cards the player can choose from.
/// Each card launches a <see cref="Game.MatchConfig"/> with <c>IsTutorial = true</c>
/// and the appropriate <c>TutorialMission</c> number (1–3).
/// </summary>
public partial class TutorialSelect : Control
{
    private static readonly (string Title, string Subtitle, string Desc, string Map)[] MissionDefs =
    {
        (
            "Mission 1",
            "Movement & Camera",
            "Learn how to pan and zoom the camera, select units, move them around the battlefield, and navigate the build menu and minimap.",
            "crossroads"
        ),
        (
            "Mission 2",
            "Buildings & Units",
            "Collect Cordite, build a Refinery and Barracks, train infantry, and lead your first attack against an enemy base.",
            "crossroads"
        ),
        (
            "Mission 3",
            "Advanced Strategy",
            "Unlock the Tech Lab, research upgrades, field advanced units, assign control groups, and practise multi-front assault tactics.",
            "iron_ridge"
        ),
    };

    private AudioManager? _audioManager;

    public override void _Ready()
    {
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
    }

    private void BuildUI()
    {
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   80);
        margin.AddThemeConstantOverride("margin_right",  80);
        margin.AddThemeConstantOverride("margin_top",    50);
        margin.AddThemeConstantOverride("margin_bottom", 50);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 28);
        margin.AddChild(root);

        // Header
        var headerRow = new HBoxContainer();
        root.AddChild(headerRow);

        var backBtn = new Button();
        backBtn.Text = "\u25C0 Back";
        UITheme.StyleMenuButton(backBtn);
        backBtn.CustomMinimumSize = new Vector2(110, 0);
        backBtn.Pressed += OnBack;
        backBtn.MouseEntered += OnHover;
        headerRow.AddChild(backBtn);

        var spacerH = new Control();
        spacerH.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(spacerH);

        var heading = new Label();
        heading.Text = Tr("MENU_TUTORIAL");
        UITheme.StyleLabel(heading, UITheme.FontSizeTitle, UITheme.Accent);
        heading.HorizontalAlignment = HorizontalAlignment.Right;
        headerRow.AddChild(heading);

        var subHeading = new Label();
        subHeading.Text = "Choose a training mission";
        UITheme.StyleLabel(subHeading, UITheme.FontSizeSubtitle, UITheme.TextSecondary);
        subHeading.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(subHeading);

        // Mission cards
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", 24);
        cardRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(cardRow);

        for (int i = 0; i < MissionDefs.Length; i++)
        {
            var (title, subtitle, desc, _) = MissionDefs[i];
            int missionNumber = i + 1;
            cardRow.AddChild(BuildMissionCard(missionNumber, title, subtitle, desc));
        }
    }

    private Panel BuildMissionCard(int missionNumber, string title, string subtitle, string desc)
    {
        var card = new Panel();
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical   = SizeFlags.ExpandFill;
        card.AddThemeStyleboxOverride("panel", UITheme.MakePanel());

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   20);
        margin.AddThemeConstantOverride("margin_right",  20);
        margin.AddThemeConstantOverride("margin_top",    20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        card.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        margin.AddChild(vbox);

        var titleLabel = new Label();
        titleLabel.Text = title;
        UITheme.StyleLabel(titleLabel, UITheme.FontSizeTitle, UITheme.Accent);
        vbox.AddChild(titleLabel);

        var subLabel = new Label();
        subLabel.Text = subtitle;
        UITheme.StyleLabel(subLabel, UITheme.FontSizeSubtitle, UITheme.TextPrimary);
        vbox.AddChild(subLabel);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var descLabel = new Label();
        descLabel.Text = desc;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        UITheme.StyleLabel(descLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        vbox.AddChild(descLabel);

        var startBtn = new Button();
        startBtn.Text = $"Start Mission {missionNumber}";
        startBtn.CustomMinimumSize = new Vector2(0, 44);
        UITheme.StyleMenuButton(startBtn);
        startBtn.Pressed      += () => LaunchMission(missionNumber);
        startBtn.MouseEntered += OnHover;
        vbox.AddChild(startBtn);

        return card;
    }

    private void LaunchMission(int missionNumber)
    {
        _audioManager?.PlayUiSoundById("ui_confirm");

        var (_, _, _, mapId) = MissionDefs[missionNumber - 1];

        Game.Main.PendingConfig = new Game.MatchConfig
        {
            MapId           = mapId,
            MatchSeed       = (ulong)System.DateTime.Now.Ticks,
            GameSpeed       = 1,
            FogOfWar        = false,
            StartingCordite = 5000,
            IsTutorial      = true,
            TutorialMission = missionNumber,
            PlayerConfigs   = new Game.PlayerConfig[]
            {
                new() { PlayerId = 1, FactionId = "arcloft",  IsAI = false, PlayerName = "Commander" },
                new() { PlayerId = 2, FactionId = "kragmore", IsAI = true,  AIDifficulty = 0, PlayerName = "Training AI" },
            }
        };

        SceneTransition.TransitionTo(GetTree(), "res://scenes/Game/Main.tscn");
    }

    private void OnBack()
    {
        _audioManager?.PlayUiSoundById("ui_click");
        SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/MainMenu.tscn");
    }

    private void OnHover()
    {
        _audioManager?.PlayUiSoundById("ui_hover");
    }
}
