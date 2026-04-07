using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.World;

/// <summary>
/// Debug test scene that spawns 18 sample units (3 per faction) in a grid,
/// sets up lighting, a ground plane, and the RTS camera.
/// Used to verify that all unit rendering, collision shapes, and selection
/// circles are working correctly.
/// </summary>
public partial class DebugTestScene : Node3D
{
    private AssetRegistry? _assetRegistry;
    private UnitDataRegistry? _unitDataRegistry;
    private AssetFactionRegistry? _factionRegistry;
    private UnitSpawner? _spawner;

    // Faction ID → team color
    private static readonly SortedList<string, Color> FactionColors = BuildFactionColors();

    // 3 sample units per faction (sorted alphabetically by faction ID)
    private static readonly string[][] FactionSamples = new string[][]
    {
        // arcloft
        new[] { "arcloft_apex", "arcloft_cirrus_runner", "arcloft_templar" },
        // bastion
        new[] { "bastion_sentinel", "bastion_phalanx", "bastion_warden" },
        // ironmarch
        new[] { "ironmarch_basalt", "ironmarch_breacher", "ironmarch_juggernaut" },
        // kragmore
        new[] { "kragmore_anvil", "kragmore_dust_runner", "kragmore_ironclad" },
        // stormrend
        new[] { "stormrend_bolt", "stormrend_cyclone", "stormrend_thunderclap" },
        // valkyr
        new[] { "valkyr_gale_trooper", "valkyr_zephyr_buggy", "valkyr_windrunner" },
    };

    private static readonly string[] FactionIds = new[]
    {
        "arcloft", "bastion", "ironmarch", "kragmore", "stormrend", "valkyr"
    };

    public override void _Ready()
    {
        GD.Print("[DebugTestScene] Initializing...");

        // 1. Load registries
        _assetRegistry = new AssetRegistry();
        _assetRegistry.Load("res://data/asset_manifest.json");

        _unitDataRegistry = new UnitDataRegistry();
        _unitDataRegistry.Load("res://data/units");

        _factionRegistry = new AssetFactionRegistry();
        _factionRegistry.Load("res://data/factions");

        // 2. Ground plane
        CreateGroundPlane();

        // 3. Directional light (sun)
        CreateSunLight();

        // 4. WorldEnvironment for ambient lighting
        CreateEnvironment();

        // 5. RTS Camera
        var camera = new RTSCamera();
        camera.Name = "RTSCamera";
        AddChild(camera);

        // 6. Create UnitSpawner
        _spawner = new UnitSpawner(_assetRegistry, _unitDataRegistry, FactionColors);
        _spawner.Name = "UnitSpawner";
        AddChild(_spawner);

        // 7. Spawn sample units in a grid (deferred so spawner's _Ready has run)
        CallDeferred(MethodName.SpawnSampleUnits);

        GD.Print("[DebugTestScene] Setup complete.");
    }

    private void SpawnSampleUnits()
    {
        if (_spawner is null)
            return;

        // 6 factions x 3 units arranged in a grid
        // Each faction gets a row, units spaced along X
        float factionSpacing = 8.0f;
        float unitSpacing = 5.0f;
        float startX = -(FactionIds.Length - 1) * factionSpacing * 0.5f;
        float startZ = -(2) * unitSpacing * 0.5f;

        for (int factionIdx = 0; factionIdx < FactionIds.Length; factionIdx++)
        {
            string factionId = FactionIds[factionIdx];
            string[] samples = FactionSamples[factionIdx];

            for (int unitIdx = 0; unitIdx < samples.Length; unitIdx++)
            {
                string unitTypeId = samples[unitIdx];
                float x = startX + factionIdx * factionSpacing;
                float z = startZ + unitIdx * unitSpacing;

                FixedVector2 pos = new FixedVector2(
                    FixedPoint.FromFloat(x),
                    FixedPoint.FromFloat(z));

                _spawner.SpawnUnit(unitTypeId, factionId, factionIdx + 1, pos, FixedPoint.Zero);
            }
        }

        GD.Print($"[DebugTestScene] Spawned {_spawner.ActiveCount} sample units.");
    }

    private void CreateGroundPlane()
    {
        var meshInstance = new MeshInstance3D();
        meshInstance.Name = "GroundPlane";

        var plane = new PlaneMesh();
        plane.Size = new Vector2(200.0f, 200.0f);
        plane.SubdivideWidth = 4;
        plane.SubdivideDepth = 4;
        meshInstance.Mesh = plane;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.35f, 0.45f, 0.3f, 1.0f); // Muted green-gray
        mat.Roughness = 1.0f;
        meshInstance.MaterialOverride = mat;

        AddChild(meshInstance);
    }

    private void CreateSunLight()
    {
        var light = new DirectionalLight3D();
        light.Name = "Sun";

        // Angled for good cel-shading shadows
        light.RotationDegrees = new Vector3(-45.0f, -30.0f, 0.0f);
        light.LightColor = new Color(1.0f, 0.97f, 0.9f, 1.0f);
        light.LightEnergy = 1.2f;
        light.ShadowEnabled = true;
        light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;

        AddChild(light);
    }

    private void CreateEnvironment()
    {
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.6f, 0.7f, 0.85f, 1.0f); // Sky blue
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
        env.AmbientLightEnergy = 0.5f;
        env.TonemapMode = Godot.Environment.ToneMapper.Filmic;

        var worldEnv = new WorldEnvironment();
        worldEnv.Environment = env;
        worldEnv.Name = "WorldEnvironment";
        AddChild(worldEnv);
    }

    private static SortedList<string, Color> BuildFactionColors()
    {
        var colors = new SortedList<string, Color>();
        colors.Add("arcloft", new Color(0.0f, 0.737f, 0.831f, 1.0f));    // Cyan #00BCD4
        colors.Add("bastion", new Color(1.0f, 0.757f, 0.027f, 1.0f));    // Gold #FFC107
        colors.Add("ironmarch", new Color(0.298f, 0.686f, 0.314f, 1.0f)); // Green #4CAF50
        colors.Add("kragmore", new Color(0.957f, 0.263f, 0.212f, 1.0f)); // Red #F44336
        colors.Add("stormrend", new Color(0.612f, 0.153f, 0.69f, 1.0f)); // Purple #9C27B0
        colors.Add("valkyr", new Color(0.129f, 0.588f, 0.953f, 1.0f));   // Blue #2196F3
        return colors;
    }
}
