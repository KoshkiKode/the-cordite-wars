using Godot;
using CorditeWars.Core;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Platform;

namespace CorditeWars.UI;

/// <summary>
/// Full-screen victory/defeat overlay shown at the end of a match.
/// Set the static <see cref="PendingResult"/> before transitioning to the scene,
/// or call <see cref="ShowInScene"/> to inject it directly into an existing scene tree.
/// </summary>
public partial class VictoryScreen : CanvasLayer
{
    // ── Static result carrier (set by Main.cs before showing) ────────

    public sealed class MatchResult
    {
        public bool   Won                  { get; init; }
        public string PlayerFactionId      { get; init; } = string.Empty;
        public string EndReason            { get; init; } = string.Empty;
        public double MatchDurationSeconds { get; init; }
        public bool   IsMultiplayer        { get; init; }
        public bool   IsNavalMap           { get; init; }
        public int    AiDifficulty         { get; init; }

        public bool   IsCampaignMission      { get; init; }
        public string CampaignFactionId      { get; init; } = string.Empty;
        public string CampaignMissionId      { get; init; } = string.Empty;
        public int    MissionNumber          { get; init; }
        public int    StarsEarned            { get; init; }
        public bool   HasNextMission         { get; init; }
        public int    UnitsKilled            { get; init; }
        public int    UnitsLost              { get; init; }
        public int    BuildingsConstructed   { get; init; }
    }

    /// <summary>Set this before transitioning to the victory screen scene.</summary>
    public static MatchResult? PendingResult { get; set; }

    // ── State ────────────────────────────────────────────────────────

    private MatchResult? _result;
    private AudioManager? _audioManager;
    private ColorRect _overlayBg = null!;
    private CenterContainer _overlayContent = null!;

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
        _result = PendingResult;
        PendingResult = null;

        Layer = 50;
        BuildUI();
        PlayFadeIn();

        // Play victory or defeat music + UI stinger
        bool won = _result?.Won ?? false;
        _audioManager?.PlayMusicById(won ? "music_victory" : "music_defeat");
        _audioManager?.PlayUiSoundById(won ? "ui_match_victory" : "ui_match_defeat");

        // Report result to Steam
        if (_result is not null && SteamManager.Instance is { } steam)
        {
            if (_result.Won)
                steam.OnMatchWon(_result.PlayerFactionId, _result.IsMultiplayer,
                    _result.IsNavalMap, _result.MatchDurationSeconds);
            else
                steam.OnMatchLost();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Allow pressing Escape as shortcut to main menu
        if (@event.IsActionPressed("ui_cancel"))
        {
            GoToMainMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── Factory / injection helper ────────────────────────────────────

    /// <summary>
    /// Creates and adds the VictoryScreen as a child of <paramref name="parent"/>
    /// without a scene transition. Useful when called from Main.cs.
    /// </summary>
    public static VictoryScreen ShowInScene(Node parent, MatchResult result)
    {
        PendingResult = result;
        var screen = new VictoryScreen();
        parent.AddChild(screen);
        return screen;
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
        bool won = _result?.Won ?? false;
        string faction = _result?.PlayerFactionId ?? "unknown";
        string reason = _result?.EndReason ?? string.Empty;
        double duration = _result?.MatchDurationSeconds ?? 0.0;

        // Dark semi-transparent background — fades in
        _overlayBg = new ColorRect();
        _overlayBg.Color = won
            ? new Color(0.05f, 0.1f, 0.05f, 0.92f)
            : new Color(0.1f, 0.05f, 0.05f, 0.92f);
        _overlayBg.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _overlayBg.Modulate = new Color(1, 1, 1, 0);
        AddChild(_overlayBg);

        // Center container — fades in with a slight delay
        _overlayContent = new CenterContainer();
        _overlayContent.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _overlayContent.Modulate = new Color(1, 1, 1, 0);
        AddChild(_overlayContent);

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(600, 0);
        vbox.AddThemeConstantOverride("separation", 24);
        _overlayContent.AddChild(vbox);

        // Result title
        var titleLabel = new Label();
        titleLabel.Text = won ? Tr("VICTORY_TITLE") : Tr("DEFEAT_TITLE");
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Color titleColor = won ? UITheme.SuccessColor : UITheme.ErrorColor;
        UITheme.StyleLabel(titleLabel, UITheme.FontSizeTitle, titleColor);
        vbox.AddChild(titleLabel);

        // Faction name
        var factionLabel = new Label();
        string factionDisplayName = GetFactionDisplayName(faction);
        factionLabel.Text = won
            ? string.Format(Tr("VICTORY_FACTION_FMT"), factionDisplayName)
            : string.Format(Tr("DEFEAT_FACTION_FMT"), factionDisplayName);
        factionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(factionLabel, UITheme.FontSizeSubtitle, UITheme.GetFactionColorById(faction));
        vbox.AddChild(factionLabel);

        // Match duration
        var durationLabel = new Label();
        int minutes = (int)(duration / 60);
        int seconds = (int)(duration % 60);
        durationLabel.Text = string.Format(Tr("MATCH_DURATION_FMT"), minutes, seconds);
        durationLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(durationLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        vbox.AddChild(durationLabel);

        // Stats row
        var statsLabel = new Label();
        int kills  = _result?.UnitsKilled          ?? 0;
        int losses = _result?.UnitsLost            ?? 0;
        int bldgs  = _result?.BuildingsConstructed ?? 0;
        statsLabel.Text = $"Units Killed: {kills}   Units Lost: {losses}   Buildings: {bldgs}";
        statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(statsLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        vbox.AddChild(statsLabel);

        // Campaign stars
        if (won && (_result?.IsCampaignMission ?? false))
        {
            int stars = _result!.StarsEarned;
            var starsLabel = new Label();
            var sb = new System.Text.StringBuilder(3);
            for (int si = 0; si < 3; si++)
                sb.Append(si < stars ? '\u2605' : '\u2606');
            starsLabel.Text = $"Mission Complete!  {sb}";
            starsLabel.HorizontalAlignment = HorizontalAlignment.Center;
            UITheme.StyleLabel(starsLabel, UITheme.FontSizeLarge, UITheme.Accent);
            vbox.AddChild(starsLabel);
        }

        // End reason (if present)
        if (!string.IsNullOrEmpty(reason))
        {
            var reasonLabel = new Label();
            reasonLabel.Text = reason;
            reasonLabel.HorizontalAlignment = HorizontalAlignment.Center;
            UITheme.StyleLabel(reasonLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
            vbox.AddChild(reasonLabel);
        }

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 16);
        vbox.AddChild(spacer);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        btnRow.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(btnRow);

        var mainMenuBtn = new Button();
        mainMenuBtn.Text = Tr("MENU_MAIN_MENU");
        mainMenuBtn.CustomMinimumSize = new Vector2(200, 50);
        UITheme.StyleButton(mainMenuBtn);
        mainMenuBtn.Pressed += GoToMainMenu;
        btnRow.AddChild(mainMenuBtn);

        var playAgainBtn = new Button();
        playAgainBtn.Text = Tr("VICTORY_PLAY_AGAIN");
        playAgainBtn.CustomMinimumSize = new Vector2(200, 50);
        UITheme.StyleAccentButton(playAgainBtn);
        playAgainBtn.Pressed += GoToSkirmishLobby;
        btnRow.AddChild(playAgainBtn);

        // Next mission button (campaign only)
        if (won && (_result?.IsCampaignMission ?? false) && (_result?.HasNextMission ?? false))
        {
            var nextBtn = new Button();
            nextBtn.Text = "Next Mission \u25BA";
            nextBtn.CustomMinimumSize = new Vector2(200, 50);
            UITheme.StyleAccentButton(nextBtn);
            nextBtn.Pressed += () =>
            {
                _audioManager?.PlayUiSoundById("ui_confirm");
                SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/CampaignSelect.tscn");
            };
            btnRow.AddChild(nextBtn);
        }
    }

    // ── Animations ───────────────────────────────────────────────────

    private async void PlayFadeIn()
    {
        // Background fades in first
        var bgTween = CreateTween();
        bgTween.TweenProperty(_overlayBg, "modulate", new Color(1, 1, 1, 1), 0.3f)
            .SetEase(Tween.EaseType.Out);
        await ToSignal(bgTween, Tween.SignalName.Finished);

        // Content fades in with a slight delay for layered feel
        var contentTween = CreateTween();
        contentTween.TweenProperty(_overlayContent, "modulate", new Color(1, 1, 1, 1), 0.4f)
            .SetEase(Tween.EaseType.Out);
    }

    // ── Navigation ────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        _audioManager?.PlayUiSoundById("ui_cancel");
        SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/MainMenu.tscn");
    }

    private void GoToSkirmishLobby()
    {
        _audioManager?.PlayUiSoundById("ui_confirm");
        SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/SkirmishLobby.tscn");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string GetFactionDisplayName(string factionId)
    {
        for (int i = 0; i < UITheme.FactionIds.Length; i++)
        {
            if (UITheme.FactionIds[i] == factionId)
                return UITheme.FactionNames[i];
        }
        return factionId;
    }
}
