using Godot;

namespace CorditeWars.UI;

/// <summary>
/// Main menu hub: Campaign, Skirmish, Multiplayer, Options, Credits, Quit.
/// Staggered fade-in animation for menu buttons. Title at top.
/// </summary>
public partial class MainMenu : Control
{
    private const string VersionString = "v0.1.0-alpha";

    private static readonly string[] ButtonLabelKeys = { "MENU_CAMPAIGN", "MENU_SKIRMISH", "MENU_MULTIPLAYER", "MENU_OPTIONS", "MENU_CREDITS", "MENU_QUIT" };
    private static readonly string[] ButtonScenes =
    {
        "res://scenes/UI/CampaignSelect.tscn",
        "res://scenes/UI/SkirmishLobby.tscn",
        "res://scenes/UI/MultiplayerLobby.tscn",
        "res://scenes/UI/OptionsMenu.tscn",
        "res://scenes/UI/CreditsScreen.tscn",
        "" // Quit
    };

    private readonly Button[] _menuButtons = new Button[ButtonLabelKeys.Length];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Main layout: left column for title + buttons
        var marginContainer = new MarginContainer();
        marginContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        marginContainer.AddThemeConstantOverride("margin_left", 120);
        marginContainer.AddThemeConstantOverride("margin_top", 100);
        marginContainer.AddThemeConstantOverride("margin_right", 120);
        marginContainer.AddThemeConstantOverride("margin_bottom", 60);
        AddChild(marginContainer);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 40);
        marginContainer.AddChild(outerVBox);

        // Title area
        var titleBox = new VBoxContainer();
        titleBox.AddThemeConstantOverride("separation", 4);
        outerVBox.AddChild(titleBox);

        var title = new Label();
        title.Text = Tr("GAME_TITLE");
        UITheme.StyleLabel(title, UITheme.FontSizeTitle, UITheme.Accent);
        titleBox.AddChild(title);

        var subtitle = new Label();
        subtitle.Text = "\u2500\u2500\u2500  " + Tr("GAME_SUBTITLE") + "  \u2500\u2500\u2500";
        UITheme.StyleLabel(subtitle, UITheme.FontSizeSubtitle, UITheme.TextSecondary);
        titleBox.AddChild(subtitle);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 20);
        outerVBox.AddChild(spacer);

        // Menu buttons
        var buttonBox = new VBoxContainer();
        buttonBox.AddThemeConstantOverride("separation", 8);
        outerVBox.AddChild(buttonBox);

        for (int i = 0; i < ButtonLabelKeys.Length; i++)
        {
            var btn = new Button();
            btn.Text = "\u25BA  " + Tr(ButtonLabelKeys[i]);
            btn.CustomMinimumSize = new Vector2(320, 0);
            btn.Alignment = HorizontalAlignment.Left;
            UITheme.StyleMenuButton(btn);
            buttonBox.AddChild(btn);

            // Start invisible for staggered animation
            btn.Modulate = new Color(1, 1, 1, 0);

            int index = i;
            btn.Pressed += () => OnMenuButtonPressed(index);
            _menuButtons[i] = btn;
        }

        // Footer: version + copyright
        var footer = new HBoxContainer();
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.KeepSize);
        footer.OffsetTop = -40;
        footer.OffsetLeft = 120;
        footer.OffsetRight = -120;
        AddChild(footer);

        var versionLabel = new Label();
        versionLabel.Text = VersionString;
        UITheme.StyleLabel(versionLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        versionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(versionLabel);

        var copyright = new Label();
        copyright.Text = Tr("FOOTER_COPYRIGHT");
        copyright.HorizontalAlignment = HorizontalAlignment.Right;
        UITheme.StyleLabel(copyright, UITheme.FontSizeSmall, UITheme.TextMuted);
        copyright.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(copyright);

        // Play staggered fade-in
        PlayFadeIn();
    }

    private async void PlayFadeIn()
    {
        for (int i = 0; i < _menuButtons.Length; i++)
        {
            var tween = CreateTween();
            tween.TweenProperty(_menuButtons[i], "modulate", new Color(1, 1, 1, 1), 0.2f)
                .SetEase(Tween.EaseType.Out);
            await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
        }
    }

    private void OnMenuButtonPressed(int index)
    {
        if (index == ButtonLabelKeys.Length - 1)
        {
            // Quit
            GetTree().Quit();
            return;
        }

        string scene = ButtonScenes[index];
        if (!string.IsNullOrEmpty(scene))
        {
            GetTree().ChangeSceneToFile(scene);
        }
    }
}
