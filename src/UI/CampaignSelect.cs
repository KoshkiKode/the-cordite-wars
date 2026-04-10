using Godot;
using System.Collections.Generic;
using System.Text.Json;
using CorditeWars.Game;
using CorditeWars.Game.Campaign;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Persistence;

namespace CorditeWars.UI;

/// <summary>
/// Campaign faction selection: 6 faction cards in 3x2 grid.
/// Selecting a faction shows description panel below with Start/Continue buttons.
/// Campaign data is loaded from <c>data/campaign/{faction}.json</c>.
/// Progress (completed missions, stars) is loaded from
/// <c>user://campaign_progress.json</c> via <see cref="CampaignProgressManager"/>.
/// </summary>
public partial class CampaignSelect : Control
{
    // ── Faction data loaded from JSON ─────────────────────────────────

    private readonly FactionCampaign?[] _campaigns = new FactionCampaign?[6];

    // ── UI state ──────────────────────────────────────────────────────

    private int _selectedFaction = -1;
    private Panel[] _factionCards = new Panel[6];
    private Label _campaignTitle = null!;
    private Label _campaignDesc = null!;
    private Label _campaignCommander = null!;
    private Label _campaignMissions = null!;
    private Label _campaignMissionList = null!;
    private Button _startBtn = null!;
    private Button _continueBtn = null!;
    private VBoxContainer _detailPanel = null!;

    private AudioManager? _audioManager;

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
        LoadCampaignData();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
        SelectFaction(0);
    }

    // ── Data loading ──────────────────────────────────────────────────

    private void LoadCampaignData()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        for (int i = 0; i < UITheme.FactionIds.Length; i++)
        {
            string path = $"res://data/campaign/{UITheme.FactionIds[i]}.json";
            if (!FileAccess.FileExists(path)) continue;

            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file is null) continue;

            try
            {
                _campaigns[i] = JsonSerializer.Deserialize<FactionCampaign>(file.GetAsText(), options);
            }
            catch (JsonException ex)
            {
                GD.PushWarning($"[CampaignSelect] Failed to parse {path}: {ex.Message}");
            }
        }
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
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
        outerVBox.AddThemeConstantOverride("separation", 24);
        margin.AddChild(outerVBox);

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        outerVBox.AddChild(header);

        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () =>
        {
            _audioManager?.PlayUiSoundById("ui_cancel");
            GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
        };
        header.AddChild(backBtn);

        var headerSpacer = new Control();
        headerSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(headerSpacer);

        var title = new Label();
        title.Text = Tr("CAMPAIGN_SELECT_TITLE");
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // Faction grid: 3x2
        var gridContainer = new GridContainer();
        gridContainer.Columns = 3;
        gridContainer.AddThemeConstantOverride("h_separation", 16);
        gridContainer.AddThemeConstantOverride("v_separation", 16);
        outerVBox.AddChild(gridContainer);

        for (int i = 0; i < 6; i++)
        {
            var card = BuildFactionCard(i);
            _factionCards[i] = card;
            gridContainer.AddChild(card);
        }

        // Detail panel (hidden until selection)
        _detailPanel = new VBoxContainer();
        _detailPanel.AddThemeConstantOverride("separation", 8);
        _detailPanel.Visible = false;
        outerVBox.AddChild(_detailPanel);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separator", UITheme.Border);
        _detailPanel.AddChild(sep);

        // Campaign info row
        var infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 24);
        _detailPanel.AddChild(infoRow);

        var infoLeft = new VBoxContainer();
        infoLeft.AddThemeConstantOverride("separation", 4);
        infoLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        infoRow.AddChild(infoLeft);

        _campaignTitle = new Label();
        UITheme.StyleLabel(_campaignTitle, UITheme.FontSizeLarge, UITheme.Accent);
        infoLeft.AddChild(_campaignTitle);

        _campaignMissions = new Label();
        UITheme.StyleLabel(_campaignMissions, UITheme.FontSizeNormal, UITheme.TextSecondary);
        infoLeft.AddChild(_campaignMissions);

        _campaignDesc = new Label();
        _campaignDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_campaignDesc, UITheme.FontSizeNormal, UITheme.TextPrimary);
        infoLeft.AddChild(_campaignDesc);

        _campaignCommander = new Label();
        UITheme.StyleLabel(_campaignCommander, UITheme.FontSizeSmall, UITheme.TextMuted);
        infoLeft.AddChild(_campaignCommander);

        // Mission list preview (first 3 missions)
        _campaignMissionList = new Label();
        _campaignMissionList.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_campaignMissionList, UITheme.FontSizeSmall, UITheme.TextMuted);
        infoLeft.AddChild(_campaignMissionList);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 16);
        _detailPanel.AddChild(btnRow);

        _startBtn = new Button();
        _startBtn.Text = Tr("CAMPAIGN_START");
        _startBtn.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleAccentButton(_startBtn);
        _startBtn.Pressed += OnStartPressed;
        btnRow.AddChild(_startBtn);

        _continueBtn = new Button();
        _continueBtn.Text = string.Format(Tr("CAMPAIGN_CONTINUE"), 1);
        _continueBtn.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleButton(_continueBtn);
        _continueBtn.Pressed += OnContinuePressed;
        btnRow.AddChild(_continueBtn);
    }

    private Panel BuildFactionCard(int index)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(200, 120);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakeFactionCard(UITheme.GetFactionColor(index), false));

        var innerMargin = new MarginContainer();
        innerMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        innerMargin.AddThemeConstantOverride("margin_left", 12);
        innerMargin.AddThemeConstantOverride("margin_right", 12);
        innerMargin.AddThemeConstantOverride("margin_top", 12);
        innerMargin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(innerMargin);

        var innerVBox = new VBoxContainer();
        innerVBox.AddThemeConstantOverride("separation", 4);
        innerMargin.AddChild(innerVBox);

        var nameLabel = new Label();
        nameLabel.Text = UITheme.FactionNames[index];
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(nameLabel, UITheme.FontSizeLarge, UITheme.GetFactionColor(index));
        innerVBox.AddChild(nameLabel);

        int missionCount = _campaigns[index]?.Missions.Count ?? 0;
        var missionLabel = new Label();
        missionLabel.Text = missionCount > 0 ? $"({missionCount} missions)" : "(coming soon)";
        missionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(missionLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        innerVBox.AddChild(missionLabel);

        // Progress stars — filled based on completed missions
        var starsLabel = new Label();
        starsLabel.Name = $"StarsLabel_{index}";
        starsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UpdateStarsLabel(starsLabel, index);
        UITheme.StyleLabel(starsLabel, UITheme.FontSizeNormal, UITheme.Accent);
        innerVBox.AddChild(starsLabel);

        // Transparent click overlay
        var clickBtn = new Button();
        clickBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        clickBtn.Flat = true;
        clickBtn.MouseDefaultCursorShape = CursorShape.PointingHand;
        int idx = index;
        clickBtn.Pressed += () =>
        {
            _audioManager?.PlayUiSoundById("ui_click");
            SelectFaction(idx);
        };
        panel.AddChild(clickBtn);

        return panel;
    }

    // ── Progress helpers ──────────────────────────────────────────────

    /// <summary>
    /// Writes filled/empty stars into <paramref name="label"/> based on the
    /// player's total stars earned across the faction's campaign.
    /// Each filled star represents one star earned; shows up to 5.
    /// Total stars = sum of best-star-run for each completed mission.
    /// </summary>
    private static void UpdateStarsLabel(Label label, int factionIndex)
    {
        string factionId = UITheme.FactionIds[factionIndex];
        var progress = CampaignProgressManager.Load();
        var faction = progress.Factions.TryGetValue(factionId, out var fp) ? fp : null;

        // Sum all per-mission star ratings, capped at MaxStars for display
        int totalStars = 0;
        if (faction is not null)
        {
            foreach (var kvp in faction.MissionStars)
                totalStars += kvp.Value;
        }

        // Show up to 5 stars
        const int MaxStars = 5;
        int filled = System.Math.Min(totalStars, MaxStars);

        var sb = new System.Text.StringBuilder(MaxStars);
        for (int i = 0; i < MaxStars; i++)
            sb.Append(i < filled ? '\u2605' : '\u2606'); // ★ or ☆
        label.Text = sb.ToString();

        // Tint: gold when any progress, muted when none
        Color col = filled > 0 ? UITheme.Accent : UITheme.TextMuted;
        label.AddThemeColorOverride("font_color", col);
    }

    /// <summary>
    /// Returns the first uncompleted mission in <paramref name="campaign"/>,
    /// or the last mission if all are completed (allows replaying the finale).
    /// Returns the first mission if no progress exists.
    /// </summary>
    private static CampaignMission GetContinueMission(FactionCampaign campaign)
    {
        var progress = CampaignProgressManager.Load();
        var faction = progress.Factions.TryGetValue(campaign.FactionId, out var fp) ? fp : null;

        if (faction is null || faction.CompletedMissions.Count == 0)
            return campaign.Missions[0];

        foreach (var mission in campaign.Missions)
        {
            if (!faction.IsCompleted(mission.Id))
                return mission;
        }

        // All completed — let them replay the final mission
        return campaign.Missions[^1];
    }

    // ── Selection ─────────────────────────────────────────────────────

    private void SelectFaction(int index)
    {
        _selectedFaction = index;

        for (int i = 0; i < 6; i++)
        {
            _factionCards[i].AddThemeStyleboxOverride("panel",
                UITheme.MakeFactionCard(UITheme.GetFactionColor(i), i == index));
        }

        _detailPanel.Visible = true;

        var campaign = _campaigns[index];
        if (campaign is not null)
        {
            _campaignTitle.Text = campaign.CampaignName;
            int count = campaign.Missions.Count;
            _campaignMissions.Text = string.Format(Tr("CAMPAIGN_MISSIONS_FMT"), count);
            _campaignDesc.Text = campaign.Description;
            _campaignCommander.Text = string.Format(Tr("CAMPAIGN_COMMANDER_FMT"), campaign.Commander);

            // Show first 3 mission names as preview
            var preview = new System.Text.StringBuilder();
            int shown = System.Math.Min(3, campaign.Missions.Count);
            for (int m = 0; m < shown; m++)
            {
                var mission = campaign.Missions[m];
                preview.Append($"M{mission.Number}: {mission.Name}");
                if (m < shown - 1) preview.Append("  •  ");
            }
            if (campaign.Missions.Count > 3)
                preview.Append($"  •  +{campaign.Missions.Count - 3} more...");
            _campaignMissionList.Text = preview.ToString();
        }
        else
        {
            _campaignTitle.Text = UITheme.FactionNames[index];
            _campaignMissions.Text = string.Empty;
            _campaignDesc.Text = Tr(GetFactionDescKey(index));
            _campaignCommander.Text = string.Empty;
            _campaignMissionList.Text = string.Empty;
        }

        // Show the mission number the Continue button will resume on
        var selectedCampaign = _campaigns[index];
        int continueNumber = 1;
        if (selectedCampaign is not null && selectedCampaign.Missions.Count > 0)
        {
            var continueMission = GetContinueMission(selectedCampaign);
            continueNumber = continueMission.Number;
        }
        _continueBtn.Text = string.Format(Tr("CAMPAIGN_CONTINUE"), continueNumber);
    }

    private static string GetFactionDescKey(int index) => index switch
    {
        0 => "FACTION_DESC_VALKYR",
        1 => "FACTION_DESC_KRAGMORE",
        2 => "FACTION_DESC_BASTION",
        3 => "FACTION_DESC_ARCLOFT",
        4 => "FACTION_DESC_IRONMARCH",
        5 => "FACTION_DESC_STORMREND",
        _ => "GAME_TITLE"
    };

    // ── Button handlers ───────────────────────────────────────────────

    private void OnStartPressed()
    {
        if (_selectedFaction < 0) return;
        _audioManager?.PlayUiSoundById("ui_confirm");

        var campaign = _campaigns[_selectedFaction];
        if (campaign == null || campaign.Missions.Count == 0)
        {
            ShowComingSoonDialog();
            return;
        }

        // Always start from mission 1 when pressing "Start"
        LaunchMission(campaign, campaign.Missions[0]);
    }

    private void OnContinuePressed()
    {
        if (_selectedFaction < 0) return;
        _audioManager?.PlayUiSoundById("ui_confirm");

        var campaign = _campaigns[_selectedFaction];
        if (campaign == null || campaign.Missions.Count == 0)
        {
            ShowComingSoonDialog();
            return;
        }

        var mission = GetContinueMission(campaign);
        LaunchMission(campaign, mission);
    }

    /// <summary>
    /// Builds a <see cref="MatchConfig"/> from <paramref name="mission"/> and
    /// transitions to the main gameplay scene.
    /// </summary>
    private void LaunchMission(FactionCampaign campaign, CampaignMission mission)
    {
        var playerConfigs = new List<PlayerConfig>();

        // Slot 1 — human player
        playerConfigs.Add(new PlayerConfig
        {
            PlayerId   = 1,
            FactionId  = mission.PlayerFaction,
            IsAI       = false,
            PlayerName = campaign.Commander
        });

        // Slots 2+ — AI enemies
        for (int i = 0; i < mission.EnemyFactions.Count; i++)
        {
            playerConfigs.Add(new PlayerConfig
            {
                PlayerId    = i + 2,
                FactionId   = mission.EnemyFactions[i],
                IsAI        = true,
                AIDifficulty = mission.AiDifficulty,
                PlayerName  = $"AI {mission.EnemyFactions[i]}"
            });
        }

        var config = new MatchConfig
        {
            MapId            = mission.MapId,
            MatchSeed        = (ulong)System.DateTime.Now.Ticks,
            GameSpeed        = 1,
            FogOfWar         = true,
            StartingCordite  = mission.StartingCordite,
            WinCondition     = mission.WinCondition,
            PlayerConfigs    = playerConfigs.ToArray(),
            Campaign         = new CampaignMatchContext
            {
                FactionId     = campaign.FactionId,
                MissionId     = mission.Id,
                MissionNumber = mission.Number,
                MissionName   = mission.Name,
                Objectives    = mission.Objectives.ToArray()
            }
        };

        GD.Print($"[CampaignSelect] Launching mission '{mission.Name}' " +
                 $"({mission.PlayerFaction} vs {string.Join(", ", mission.EnemyFactions)}) " +
                 $"WinCondition={mission.WinCondition}");

        CorditeWars.Game.Main.PendingConfig = config;
        SceneTransition.TransitionTo(GetTree(), "res://scenes/Game/Main.tscn");
    }

    private void ShowComingSoonDialog()
    {
        var dialog = new AcceptDialog();
        dialog.Title = Tr("CAMPAIGN_COMING_SOON_TITLE");
        dialog.DialogText = Tr("CAMPAIGN_COMING_SOON_BODY");
        dialog.OkButtonText = Tr("MENU_OK");
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += () => dialog.QueueFree();
        dialog.Canceled += () => dialog.QueueFree();
    }
}
