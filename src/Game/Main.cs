using Godot;
using CorditeWars.Core;
using CorditeWars.UI;
using CorditeWars.UI.HUD;
using CorditeWars.Systems.Persistence;
using CorditeWars.Systems.Platform;
using CorditeWars.Game.Campaign;
using System;
using System.Text.Json;

namespace CorditeWars.Game;

/// <summary>
/// The main gameplay scene root.
/// Receives the MatchConfig from the lobby and initializes the GameSession.
/// Also provides the core 3D environment (lighting, skybox), handles ESC-to-pause,
/// and shows the <see cref="VictoryScreen"/> when the match ends.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>
    /// Set this from the lobby before transitioning to this scene.
    /// </summary>
    public static MatchConfig? PendingConfig { get; set; }

    private GameSession? _session;
    private PauseMenu? _pauseMenu;
    private DateTime _matchStartTime;

    public override void _Ready()
    {
        GD.Print("[Main] Initializing main game scene...");

        if (PendingConfig is null)
        {
            GD.PushWarning("[Main] PendingConfig was null. Creating a fallback debug game.");
            PendingConfig = CreateFallbackConfig();
        }

        // Create and start the GameSession first so ActiveMap is available for environment setup
        _session = new GameSession();
        _session.Name = "GameSession";
        AddChild(_session);

        _session.StartMatch(PendingConfig);
        _matchStartTime = DateTime.UtcNow;

        // Setup environment using the loaded map's sun configuration
        SetupEnvironment(_session.ActiveMap?.SunConfig);

        // Wire match-ended signal
        EventBus.Instance?.Connect(
            EventBus.SignalName.MatchEnded,
            Callable.From(OnMatchEnded));

        // Create pause menu (hidden by default)
        _pauseMenu = new PauseMenu(_session);
        _pauseMenu.Name = "PauseMenu";
        AddChild(_pauseMenu);

        // Clear the config so it doesn't accidentally restart later
        PendingConfig = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && _session is not null)
        {
            if (_session.CurrentMatchState == MatchState.Playing)
            {
                _session.PauseMatch();
                _pauseMenu?.Show();
                GetViewport().SetInputAsHandled();
            }
            else if (_session.CurrentMatchState == MatchState.Paused)
            {
                _session.ResumeMatch();
                _pauseMenu?.Hide();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void OnMatchEnded()
    {
        if (_session is null) return;

        double duration = (DateTime.UtcNow - _matchStartTime).TotalSeconds;
        int localPlayerId = 1; // always player 1 for local/skirmish
        bool won = _session.WinnerPlayerId == localPlayerId;
        string factionId = GetLocalPlayerFaction();
        bool isNavalMap = _session.ActiveMap?.Id is "archipelago" or "coral_atoll";
        bool isMultiplayer = IsMultiplayerMatch();
        int aiDifficulty = GetAiDifficulty();

        // ── Campaign progress ─────────────────────────────────────────
        var campaignCtx = _session.ActiveConfig?.Campaign;
        if (won && campaignCtx is not null)
        {
            int stars = GetStars(duration, campaignCtx.MissionNumber);

            CampaignProgressManager.RecordMissionComplete(
                campaignCtx.FactionId,
                campaignCtx.MissionId,
                stars);

            GD.Print($"[Main] Campaign mission complete: {campaignCtx.MissionId} ({stars}★ in {duration:F0}s)");
        }

        // Check for next campaign mission
        bool hasCampaignCtx = campaignCtx is not null;
        bool hasNextMission  = false;
        if (won && hasCampaignCtx)
        {
            var fc = LoadFactionCampaign(campaignCtx!.FactionId);
            if (fc is not null)
                hasNextMission = fc.Missions.FindIndex(m => m.Id == campaignCtx.MissionId) < fc.Missions.Count - 1;
        }

        // Check if this was the last mission (for COMPLETE_CAMPAIGN achievement)
        if (won && campaignCtx is not null)
        {
            var fc2 = LoadFactionCampaign(campaignCtx.FactionId);
            if (fc2 is not null)
            {
                var lastMission = fc2.Missions[^1];
                if (lastMission.Id == campaignCtx.MissionId)
                    SteamManager.Instance?.UnlockAchievement("COMPLETE_CAMPAIGN");
            }
        }

        // Get match stats
        var stats = _session.GetMatchStats();

        VictoryScreen.ShowInScene(this, new VictoryScreen.MatchResult
        {
            Won                  = won,
            PlayerFactionId      = factionId,
            EndReason            = _session.EndReason,
            MatchDurationSeconds = duration,
            IsMultiplayer        = isMultiplayer,
            IsNavalMap           = isNavalMap,
            AiDifficulty         = aiDifficulty,
            IsCampaignMission    = hasCampaignCtx,
            CampaignFactionId    = campaignCtx?.FactionId ?? string.Empty,
            CampaignMissionId    = campaignCtx?.MissionId ?? string.Empty,
            MissionNumber        = campaignCtx?.MissionNumber ?? 0,
            StarsEarned          = won && hasCampaignCtx ? GetStars(duration, campaignCtx!.MissionNumber) : 0,
            HasNextMission       = hasNextMission,
            UnitsKilled          = stats.Kills,
            UnitsLost            = stats.Losses,
            BuildingsConstructed = stats.BuildingsConstructed
        });
    }

    private static int GetStars(double duration, int missionNumber)
    {
        double baseline = missionNumber * 300.0;
        if (duration < baseline * 0.6) return 3;
        if (duration < baseline * 2.0) return 2;
        return 1;
    }

    private static FactionCampaign? LoadFactionCampaign(string factionId)
    {
        string path = $"res://data/campaign/{factionId}.json";
        if (!Godot.FileAccess.FileExists(path)) return null;
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null) return null;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<FactionCampaign>(file.GetAsText(), opts);
        }
        catch { return null; }
    }

    private string GetLocalPlayerFaction()
    {
        return _session?.ActiveConfig?.PlayerConfigs is { Length: > 0 } cfgs
            ? cfgs[0].FactionId
            : "unknown";
    }

    private bool IsMultiplayerMatch()
    {
        return _session?.ActiveConfig?.PlayerConfigs is { Length: > 1 } cfgs
            && !cfgs[1].IsAI;
    }

    private int GetAiDifficulty()
    {
        return _session?.ActiveConfig?.PlayerConfigs is { Length: > 1 } cfgs && cfgs[1].IsAI
            ? cfgs[1].AIDifficulty
            : 0;
    }

    private void SetupEnvironment(CorditeWars.Game.World.MapSunConfig? sunConfig = null)
    {
        // Use map-specific sun settings, falling back to sensible defaults
        sunConfig ??= new CorditeWars.Game.World.MapSunConfig();

        // Directional sun light
        var sun = new DirectionalLight3D();
        sun.Visible = sunConfig.Enabled;
        sun.ShadowEnabled = sunConfig.Enabled;
        sun.RotationDegrees = new Vector3(sunConfig.RotationX, sunConfig.RotationY, 0f);
        sun.LightColor = new Color(sunConfig.Color);
        sun.LightEnergy = sunConfig.Energy;
        AddChild(sun);

        var env = new WorldEnvironment();
        var environment = new Godot.Environment();
        environment.BackgroundMode = Godot.Environment.BGMode.ClearColor;
        environment.BackgroundColor = new Color(sunConfig.SkyColor);

        environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        environment.AmbientLightColor = new Color(sunConfig.AmbientColor);
        environment.AmbientLightEnergy = sunConfig.AmbientEnergy;

        environment.TonemapMode = Godot.Environment.ToneMapper.Aces;
        env.Environment = environment;
        AddChild(env);
    }

    private static MatchConfig CreateFallbackConfig()
    {
        GD.Print("[Main] Generating fallback 1v1 on Crossroads.");
        return new MatchConfig
        {
            MapId = "crossroads",
            MatchSeed = (ulong)System.DateTime.Now.Ticks,
            GameSpeed = 1,
            FogOfWar = true,
            StartingCordite = 5000,
            PlayerConfigs = new PlayerConfig[]
            {
                new PlayerConfig { PlayerId = 1, FactionId = "arcloft", IsAI = false, PlayerName = "Player 1" },
                new PlayerConfig { PlayerId = 2, FactionId = "kragmore", IsAI = true, AIDifficulty = 1, PlayerName = "AI Kragmore" }
            }
        };
    }
}
