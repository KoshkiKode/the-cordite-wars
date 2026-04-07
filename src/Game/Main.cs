using Godot;
using CorditeWars.Core;

namespace CorditeWars.Game;

/// <summary>
/// The main gameplay scene root.
/// Receives the MatchConfig from the lobby and initializes the GameSession.
/// Also provides the core 3D environment (lighting, skybox).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>
    /// Set this from the lobby before transitioning to this scene.
    /// </summary>
    public static MatchConfig? PendingConfig { get; set; }

    private GameSession? _session;

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

        // Clear the config so it doesn't accidentally restart later
        PendingConfig = null;
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
