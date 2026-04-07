using Godot;
using CorditeWars.Core;
using CorditeWars.UI;
using CorditeWars.UI.HUD;
using System;

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

        // Setup environment
        SetupEnvironment();

        // Create and start the GameSession
        _session = new GameSession();
        _session.Name = "GameSession";
        AddChild(_session);

        _session.StartMatch(PendingConfig);
        _matchStartTime = DateTime.UtcNow;

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
        bool isNavalMap = _session.ActiveMap?.Id == "archipelago";
        bool isMultiplayer = IsMultiplayerMatch();
        int aiDifficulty = GetAiDifficulty();

        VictoryScreen.ShowInScene(this, new VictoryScreen.MatchResult
        {
            Won = won,
            PlayerFactionId = factionId,
            EndReason = _session.EndReason,
            MatchDurationSeconds = duration,
            IsMultiplayer = isMultiplayer,
            IsNavalMap = isNavalMap,
            AiDifficulty = aiDifficulty
        });
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

    private void SetupEnvironment()
    {
        // Add basic lighting if not present in the map
        var sun = new DirectionalLight3D();
        sun.ShadowEnabled = true;
        sun.RotationDegrees = new Vector3(-45, 45, 0); // Angled down
        sun.LightEnergy = 1.0f;
        AddChild(sun);

        var env = new WorldEnvironment();
        var environment = new Godot.Environment();
        environment.BackgroundMode = Godot.Environment.BGMode.ClearColor;
        environment.BackgroundColor = new Color(0.1f, 0.1f, 0.12f, 1.0f); // Dark space/abyss color

        // Settings to integrate well with shaders and effects
        environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        environment.AmbientLightColor = new Color(0.8f, 0.8f, 0.9f);
        environment.AmbientLightEnergy = 0.5f;

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
