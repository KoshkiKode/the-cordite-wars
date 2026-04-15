using System;
using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.AI;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Campaign;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Tech;
using CorditeWars.Game.Units;
using CorditeWars.Game.World;
using CorditeWars.Game.VFX;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Graphics;
using CorditeWars.Systems.Networking;
using CorditeWars.Systems.Pathfinding;
using CorditeWars.Systems.FogOfWar;
using CorditeWars.Systems.Garrison;
using CorditeWars.Systems.Persistence;
using CorditeWars.Systems.Platform;
using CorditeWars.Systems.Superweapon;
using CorditeWars.UI.HUD;
using CorditeWars.UI.Input;

namespace CorditeWars.Game;

/// <summary>
/// Match state for tracking the session lifecycle.
/// </summary>
public enum MatchState
{
    Setup,
    Playing,
    Paused,
    Ended
}

/// <summary>
/// Master class that wires ALL systems together for a game session.
/// This is the single entry point for starting a game from lobby/campaign.
/// It initializes all registries and systems, connects signals between
/// systems, and manages the match lifecycle.
/// </summary>
public partial class GameSession : Node
{
    // ── Registries (data, loaded once) ───────────────────────────────

    private MapLoader _mapLoader = new();
    private UnitDataRegistry _unitDataRegistry = new();
    private BuildingRegistry _buildingRegistry = new();
    private UpgradeRegistry _upgradeRegistry = new();
    private AssetRegistry _assetRegistry = new();

    // ── Managers (per-match state) ───────────────────────────────────

    private GameManager? _gameManager;
    private EconomyManager? _economyManager;
    private TechTreeManager? _techTreeManager;
    private UnitSpawner? _unitSpawner;
    private HarvesterSystem? _harvesterSystem;
    private SaveManager? _saveManager;
    private ReplayManager? _replayManager;

    // ── Networking (multiplayer only) ────────────────────────────────

    private LockstepManager? _lockstepManager;
    private NetworkTransport? _networkTransport;

    // ── Physics / Simulation Systems ─────────────────────────────────

    private TerrainGrid? _terrainGrid;
    private SpatialHash? _spatialHash;
    private OccupancyGrid? _occupancyGrid;
    private CollisionResolver? _collisionResolver;
    private PathRequestManager? _pathRequestManager;
    private FormationManager? _formationManager;
    private CombatResolver? _combatResolver;
    private UnitInteractionSystem? _unitInteractionSystem;
    private GarrisonSystem _garrisonSystem = new();
    private readonly SuperweaponSystem _superweaponSystem = new();

    // ── Audio ───────────────────────────────────────────────────────

    private CombatAudioBridge? _combatAudioBridge;
    private CombatVFXBridge?   _combatVFXBridge;
    private CorditeWars.Systems.Audio.AudioManager? _audioManager;

    // ── Fog of War / Vision ─────────────────────────────────────────

    private VisionSystem? _visionSystem;
    private FogGrid[]? _playerFogs;
    private FogSnapshot[]? _playerFogSnapshots;
    private readonly List<VisionComponent> _visionComponents = new();

    // ── Terrain Rendering ───────────────────────────────────────────

    private TerrainRenderer? _terrainRenderer;
    private WaterRenderer? _waterRenderer;
    private PropPlacer? _propPlacer;

    // ── Camera ──────────────────────────────────────────────────────

    private RTSCamera? _camera;

    // ── Gameplay Systems (Systems 1-5) ───────────────────────────────

    private SelectionManager? _selectionManager;
    private CommandInput? _commandInput;
    private BuildingPlacer? _buildingPlacer;
    private BuildingManifest _buildingManifest = new();
    private GameHUD? _gameHUD;
    private readonly List<SkirmishAI> _skirmishAIs = new();

    // ── Session State ───────────────────────────────────────────────

    public MatchState CurrentMatchState { get; private set; } = MatchState.Setup;
    public MatchConfig? ActiveConfig { get; private set; }
    public MapData? ActiveMap { get; private set; }
    public int WinnerPlayerId { get; private set; } = -1;
    public string EndReason { get; private set; } = string.Empty;

    /// <summary>
    /// The local (human) player's ID. Used to determine stealth visual treatment:
    /// own stealthed units are rendered as ghosts; enemy stealthed units are hidden.
    /// Set during <c>SetupGameplaySystems</c>.
    /// </summary>
    private int _localPlayerId = -1;

    // ── Simulation — persistent state across ticks ──────────────────

    /// <summary>
    /// Authoritative SimUnit state for mobile units, keyed by UnitId.
    /// Persisted across ticks so that movement paths, weapon cooldowns,
    /// and target locks survive the tick rebuild.
    /// Mobile units occupy IDs 1..99_999 (UnitSpawner starts at 1).
    /// </summary>
    private readonly Dictionary<int, SimUnit> _persistentSimUnits = new();

    /// <summary>Per-building weapon cooldown lists, keyed by BuildingId.</summary>
    private readonly Dictionary<int, List<FixedPoint>> _buildingWeaponCooldowns = new();

    /// <summary>Per-building current target ID, keyed by BuildingId.</summary>
    private readonly Dictionary<int, int?> _buildingCurrentTargets = new();

    // ── Win Condition ───────────────────────────────────────────────

    private WinCondition _winCondition = WinCondition.DestroyHQ;

    // ── Surrendered players ──────────────────────────────────────────

    private readonly HashSet<int> _surrenderedPlayers = new();

    // ── Mission Objective Tracker ────────────────────────────────────

    private CorditeWars.Game.Campaign.MissionObjectiveTracker? _objectiveTracker;

    // ── Tutorial ─────────────────────────────────────────────────────

    private CorditeWars.Game.Tutorial.TutorialManager?  _tutorialManager;
    private CorditeWars.UI.HUD.TutorialOverlay?         _tutorialOverlay;

    // ── Post-Match Stats ─────────────────────────────────────────────

    private int   _playerKills;
    private int   _playerLosses;
    private int   _buildingsConstructed;
    private int   _buildingsDestroyed;
    private int   _unitsProduced;
    private int   _corditeHarvested;
    private ulong _lastAutosaveTick;
    private const ulong AutosaveIntervalTicks = 1800;
    private const float TickDeltaSeconds = 1f / 30f; // 30 Hz simulation rate
    private const int DefaultDepotSupplyCapacity = 20; // Used when registry lookup fails during save restore

    /// <summary>
    /// HQ BuildingInstance nodes for each player (keyed by PlayerId).
    /// Populated during PlaceStartingBuildings; entries removed when
    /// the HQ is destroyed so CheckWinCondition can detect elimination.
    /// </summary>
    private readonly Dictionary<int, BuildingInstance> _playerHQNodes = new();

    /// <summary>
    /// Set of player IDs that had an HQ at match start.
    /// Used to distinguish "HQ never created" (data missing) from "HQ destroyed".
    /// </summary>
    private readonly HashSet<int> _playersWithInitialHQ = new();

    // ── Faction economy configs (static, created once) ──────────────

    private SortedList<string, FactionEconomyConfig> _factionEconomyConfigs =
        FactionEconomyConfigs.CreateAll();

    // ── Faction colors for unit rendering ───────────────────────────

    private static readonly SortedList<string, Color> FactionColors = CreateFactionColors();
    private static readonly SortedList<string, Color> FactionBaseColors = CreateFactionBaseColors();

    private static SortedList<string, Color> CreateFactionColors()
    {
        var colors = new SortedList<string, Color>();
        colors.Add("arcloft",   CorditeWars.UI.UITheme.FactionArcloft);
        colors.Add("bastion",   CorditeWars.UI.UITheme.FactionBastion);
        colors.Add("ironmarch", CorditeWars.UI.UITheme.FactionIronmarch);
        colors.Add("kragmore",  CorditeWars.UI.UITheme.FactionKragmore);
        colors.Add("stormrend", CorditeWars.UI.UITheme.FactionStormrend);
        colors.Add("valkyr",    CorditeWars.UI.UITheme.FactionValkyr);
        return colors;
    }

    private static SortedList<string, Color> CreateFactionBaseColors()
    {
        // Secondary colors used as base tints on 3D models to give each faction
        // a distinctive look regardless of player-assigned team color.
        var colors = new SortedList<string, Color>();
        colors.Add("arcloft",   CorditeWars.UI.UITheme.FactionArcloftSecondary);
        colors.Add("bastion",   CorditeWars.UI.UITheme.FactionBastionSecondary);
        colors.Add("ironmarch", CorditeWars.UI.UITheme.FactionIronmarchSecondary);
        colors.Add("kragmore",  CorditeWars.UI.UITheme.FactionKragmoreSecondary);
        colors.Add("stormrend", CorditeWars.UI.UITheme.FactionStormrendSecondary);
        colors.Add("valkyr",    CorditeWars.UI.UITheme.FactionValkyrSecondary);
        return colors;
    }

    // ═════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts a new match from lobby or campaign configuration.
    /// This is the primary entry point that initializes everything.
    /// </summary>
    public void StartMatch(MatchConfig config)
    {
        ActiveConfig = config;
        CurrentMatchState = MatchState.Setup;
        _winCondition = config.WinCondition;

        // Reset per-match stats
        _playerKills          = 0;
        _playerLosses         = 0;
        _buildingsConstructed = 0;
        _buildingsDestroyed   = 0;
        _unitsProduced        = 0;
        _corditeHarvested     = 0;
        _lastAutosaveTick     = 0;
        _surrenderedPlayers.Clear();

        // Initialize mission objective tracker
        var typedObjs = config.Campaign?.TypedObjectives;
        if (typedObjs != null && typedObjs.Length > 0)
        {
            _objectiveTracker = new CorditeWars.Game.Campaign.MissionObjectiveTracker();
            var list = new List<CorditeWars.Game.Campaign.TypedObjective>(typedObjs.Length);
            for (int i = 0; i < typedObjs.Length; i++)
            {
                var d = typedObjs[i];
                list.Add(new CorditeWars.Game.Campaign.TypedObjective
                {
                    Type     = d.Type switch
                    {
                        "build_building"        => CorditeWars.Game.Campaign.ObjectiveType.BuildBuilding,
                        "maintain_unit_type"    => CorditeWars.Game.Campaign.ObjectiveType.MaintainUnitType,
                        "survive_timer"         => CorditeWars.Game.Campaign.ObjectiveType.SurviveTimer,
                        "destroy_building_type" => CorditeWars.Game.Campaign.ObjectiveType.DestroyBuildingType,
                        "destroy_unit_type"     => CorditeWars.Game.Campaign.ObjectiveType.DestroyUnitType,
                        "accumulate_cordite"    => CorditeWars.Game.Campaign.ObjectiveType.AccumulateCordite,
                        "escort_unit"           => CorditeWars.Game.Campaign.ObjectiveType.EscortUnit,
                        "defend_position"       => CorditeWars.Game.Campaign.ObjectiveType.DefendPosition,
                        "reach_location"        => CorditeWars.Game.Campaign.ObjectiveType.ReachLocation,
                        _                       => CorditeWars.Game.Campaign.ObjectiveType.SurviveTimer
                    },
                    Label    = d.Label,
                    TargetId = d.TargetId,
                    Count    = d.Count,
                    Ticks    = d.Ticks,
                    Required = d.Required
                });
            }
            _objectiveTracker.Initialize(list, 0);
        }
        else
        {
            _objectiveTracker = null;
        }

        // Resolve AudioManager once; used throughout match lifecycle.
        _audioManager ??= GetNodeOrNull<CorditeWars.Systems.Audio.AudioManager>("/root/AudioManager");

        GD.Print($"[GameSession] Starting match — map: {config.MapId}, " +
                 $"players: {config.PlayerConfigs.Length}, seed: {config.MatchSeed}");

        // a. Load the map
        LoadRegistries();
        if (config.MapGeneration is not null)
        {
            var generator = new MapGenerator();
            var generated = generator.Generate(config.MapGeneration);
            _mapLoader.RegisterMap(generated);
            ActiveMap = generated;
            GD.Print($"[GameSession] Using generated map '{generated.Id}'.");
        }
        else
        {
            // Guard: if the map isn't loaded (e.g. ID typo or load error), fall back to first available
            string mapId = config.MapId;
            if (!_mapLoader.HasMap(mapId))
            {
                var available = _mapLoader.GetMapIds();
                if (available.Count > 0)
                {
                    mapId = available[0];
                    GD.PushWarning($"[GameSession] Map '{config.MapId}' not found — falling back to '{mapId}'.");
                }
            }
            ActiveMap = _mapLoader.GetMap(mapId);
        }
        EventBus.Instance?.EmitMapLoaded(ActiveMap.Id);

        // b. Create EconomyManager
        _economyManager = new EconomyManager();
        AddChild(_economyManager);
        _economyManager.Initialize(
            _factionEconomyConfigs,
            _buildingRegistry);

        // c. Create TechTreeManager
        _techTreeManager = new TechTreeManager();
        AddChild(_techTreeManager);
        _techTreeManager.Initialize(_upgradeRegistry);

        // d. Create UnitSpawner
        _unitSpawner = new UnitSpawner(
            _assetRegistry,
            _unitDataRegistry,
            FactionColors,
            FactionBaseColors);
        AddChild(_unitSpawner);

        // d2. Create deterministic simulation tick pipeline
        _terrainGrid = new TerrainGrid(ActiveMap.Width, ActiveMap.Height, FixedPoint.One);
        // Populate terrain grid with water body cells from map data so that
        // naval pathfinding works correctly on maps with defined water bodies.
        BuildTerrainGridFromMapData(ActiveMap, _terrainGrid);
        // Share the populated terrain grid with UnitSpawner so naval units
        // can be relocated to the nearest water cell on production/spawn.
        _unitSpawner.SetTerrainGrid(_terrainGrid);
        _spatialHash = new SpatialHash(ActiveMap.Width, ActiveMap.Height);
        _occupancyGrid = new OccupancyGrid(ActiveMap.Width, ActiveMap.Height);
        
        // These stateless resolvers don't require external references in constructor, 
        // they get passed state during the tick.
        _collisionResolver = new CollisionResolver();
        _pathRequestManager = new PathRequestManager();
        _formationManager = new FormationManager();
        _combatResolver = new CombatResolver();

        _unitInteractionSystem = new UnitInteractionSystem(
            _spatialHash,
            _occupancyGrid,
            _collisionResolver,
            _pathRequestManager,
            _formationManager,
            _combatResolver,
            new DeterministicRng(config.MatchSeed),
            8);

        // e. Create HarvesterSystem
        _harvesterSystem = new HarvesterSystem();
        AddChild(_harvesterSystem);
        _harvesterSystem.Initialize(_factionEconomyConfigs, _economyManager);

        // e2. Create CombatAudioBridge (wires combat events → audio playback)
        _combatAudioBridge = new CombatAudioBridge();
        AddChild(_combatAudioBridge);
        _combatAudioBridge.Initialize();

        // e3. Create CombatVFXBridge (wires combat events → GPU particle effects)
        _combatVFXBridge = new CombatVFXBridge();
        AddChild(_combatVFXBridge);
        _combatVFXBridge.Initialize();

        // f. Create SaveManager
        _saveManager = new SaveManager();
        AddChild(_saveManager);

        // f2. Create ReplayManager and begin recording
        _replayManager = new ReplayManager();
        var replayPlayers = new ReplayPlayerInfo[config.PlayerConfigs.Length];
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            var pc = config.PlayerConfigs[i];
            replayPlayers[i] = new ReplayPlayerInfo
            {
                PlayerId   = pc.PlayerId,
                FactionId  = pc.FactionId,
                PlayerName = pc.PlayerName,
                IsAI       = pc.IsAI
            };
        }
        _replayManager.BeginRecording(
            config.MapId,
            config.MatchSeed,
            replayPlayers,
            config.Campaign?.MissionId);

        // g. Set up players
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];
            _economyManager.AddPlayer(pc.PlayerId, pc.FactionId);
            _techTreeManager.AddPlayer(pc.PlayerId, pc.FactionId);

            // Register all superweapon abilities for this player (one BuildingSuperweapon +
            // one ActivatedAbility). All catalogue entries for the faction are registered so
            // each gets its own independent cooldown.
            foreach (var weaponData in SuperweaponSystem.GetFactionWeapons(pc.FactionId))
                _superweaponSystem.RegisterPlayer(pc.PlayerId, weaponData.Id);
        }

        // g2. Initialize fog of war (after terrain and players are set up)
        if (config.FogOfWar && _terrainGrid != null)
        {
            _visionSystem = new VisionSystem();
            _playerFogs = new FogGrid[config.PlayerConfigs.Length];
            _playerFogSnapshots = new FogSnapshot[config.PlayerConfigs.Length];

            for (int i = 0; i < config.PlayerConfigs.Length; i++)
            {
                _playerFogs[i] = new FogGrid(
                    _terrainGrid.Width,
                    _terrainGrid.Height,
                    config.PlayerConfigs[i].PlayerId,
                    FogMode.Skirmish);
                _playerFogSnapshots[i] = new FogSnapshot();
            }

            GD.Print($"[GameSession] Fog of war initialized for {config.PlayerConfigs.Length} players " +
                     $"({_terrainGrid.Width}x{_terrainGrid.Height} grid).");
        }

        // g3. Render terrain mesh, water, and map props
        SetupTerrainRendering();

        // h. Place starting buildings (HQ per player at starting positions)
        PlaceStartingBuildings(config);

        // i. Spawn starting units (1 harvester per player)
        SpawnStartingUnits(config);

        // j. Register Cordite nodes from map data
        RegisterCorditeNodes();

        // k. Set up RTS camera
        SetupCamera();

        // k2. Set up gameplay systems (Selection, Commands, Building, HUD, AI)
        SetupGameplaySystems(config);

        // k3. Wire minimap to live terrain/camera data now that all systems are ready
        SetupMinimapData();

        // k4. Spawn visual cordite node markers on the map
        SpawnCorditeNodeMarkers();

        // l. Wire up GameManager
        _gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (_gameManager is not null)
        {
            _gameManager.EconomyManager = _economyManager;
            _gameManager.HarvesterSystem = _harvesterSystem;
            _gameManager.TechTreeManager = _techTreeManager;
            _gameManager.MapLoader = _mapLoader;
            _gameManager.CommandBuffer = new CommandBuffer();
            _gameManager.OnSimulationTick += HandleSimulationTick;

            // m. If multiplayer: initialize LockstepManager + NetworkTransport
            bool isMultiplayer = false;
            for (int i = 0; i < config.PlayerConfigs.Length; i++)
            {
                if (!config.PlayerConfigs[i].IsAI && i > 0)
                {
                    isMultiplayer = true;
                    break;
                }
            }

            if (isMultiplayer)
            {
                SetupMultiplayer(config);
            }
            else
            {
                _gameManager.StartMatch(config.MatchSeed);
            }
        }

        CurrentMatchState = MatchState.Playing;

        // Play match-start stinger and start battle music
        _audioManager?.PlayUiSoundById("ui_match_start");
        _audioManager?.PlayMusicById("music_battle_calm");

        // Steam match-start notification — disabled until Steam integration is re-enabled.
        // if (SteamManager.Instance is { } steam && config.PlayerConfigs.Length > 0)
        // {
        //     bool isMultiplayerSteam = config.PlayerConfigs.Length > 1 && !config.PlayerConfigs[1].IsAI;
        //     bool hasAiOpponent     = config.PlayerConfigs.Length > 1 && config.PlayerConfigs[1].IsAI;
        //     int  aiDiff            = hasAiOpponent ? config.PlayerConfigs[1].AIDifficulty : 0;
        //     steam.OnMatchStarted(config.PlayerConfigs[0].FactionId, isMultiplayerSteam, hasAiOpponent, aiDiff);
        // }

        GD.Print("[GameSession] Match started successfully.");

        // Initialize tutorial if requested
        if (config.IsTutorial)
        {
            _tutorialManager = new CorditeWars.Game.Tutorial.TutorialManager();
            var steps = BuildTutorialSteps(config.TutorialMission);
            _tutorialManager.Start(steps);

            _tutorialOverlay = new CorditeWars.UI.HUD.TutorialOverlay();
            _tutorialOverlay.Name = "TutorialOverlay";
            AddChild(_tutorialOverlay);
            _tutorialOverlay.Attach(_tutorialManager);
        }
        else
        {
            _tutorialManager = null;
            _tutorialOverlay = null;
        }
    }

    /// <summary>
    /// Pauses the simulation. Input is still processed.
    /// </summary>
    public void PauseMatch()
    {
        if (CurrentMatchState != MatchState.Playing) return;

        CurrentMatchState = MatchState.Paused;
        _gameManager?.PauseMatch();
        GD.Print("[GameSession] Match paused.");
    }

    /// <summary>
    /// Resumes a paused match.
    /// </summary>
    public void ResumeMatch()
    {
        if (CurrentMatchState != MatchState.Paused) return;

        CurrentMatchState = MatchState.Playing;
        _gameManager?.ResumeMatch();
        GD.Print("[GameSession] Match resumed.");
    }

    /// <summary>
    /// Ends the match with a winner and reason.
    /// </summary>
    public void EndMatch(int winnerPlayerId, string reason)
    {
        CurrentMatchState = MatchState.Ended;
        WinnerPlayerId = winnerPlayerId;
        EndReason = reason;

        ulong finalTick = _gameManager?.CurrentTick ?? 0;

        if (_gameManager is not null)
        {
            _gameManager.OnSimulationTick -= HandleSimulationTick;
            _gameManager.EndMatch();
        }

        // Shutdown multiplayer if active
        _lockstepManager?.Shutdown();
        _networkTransport?.Disconnect();

        // Stop battle music — VictoryScreen will start victory/defeat music
        _audioManager?.StopMusic();

        // Finalize and save replay (respects the auto-save setting)
        bool autoSaveReplays = LoadAutoSaveReplaySetting();
        _replayManager?.FinalizeAndSave(finalTick, winnerPlayerId, autoSaveReplays);

        // Steam hard-AI defeat achievement — disabled until Steam integration is re-enabled.
        // if (SteamManager.Instance is { } steam && ActiveConfig is not null)
        // {
        //     bool hasHardAI = ActiveConfig.PlayerConfigs.Length > 1
        //         && ActiveConfig.PlayerConfigs[1].IsAI
        //         && ActiveConfig.PlayerConfigs[1].AIDifficulty >= 2;
        //     if (hasHardAI && winnerPlayerId == 1)
        //         steam.OnHardAIDefeated();
        // }

        // Signal through EventBus so listeners (Main.cs, VictoryScreen, etc.) can react
        EventBus.Instance?.EmitMatchEnded();

        GD.Print($"[GameSession] Match ended — winner: {winnerPlayerId}, reason: {reason}");
    }

    /// <summary>
    /// Records a player surrender, emits the event, and re-evaluates the win condition.
    /// In lockstep multiplayer this should be called after processing a SurrenderCommand.
    /// </summary>
    public void PlayerSurrender(int playerId)
    {
        if (CurrentMatchState != MatchState.Playing) return;
        if (_surrenderedPlayers.Contains(playerId)) return;

        _surrenderedPlayers.Add(playerId);
        GD.Print($"[GameSession] Player {playerId} surrendered.");
        EventBus.Instance?.EmitPlayerSurrendered(playerId);
        CheckWinCondition();
    }

    /// <summary>
    /// Attempts to garrison the given infantry unit into a nearby building.
    /// The building must be owned by <paramref name="playerId"/>, have garrison
    /// capacity, and the unit must be within 6 grid cells of the building center.
    /// </summary>
    public bool TryGarrisonUnit(int unitId, int buildingId, int playerId)
    {
        if (CurrentMatchState != MatchState.Playing) return false;

        // Validate building ownership
        var slot = _garrisonSystem.GetGarrisonForBuilding(buildingId);
        if (slot is null || slot.OwnerId != playerId) return false;

        // Validate unit ownership and category (infantry only)
        if (!_persistentSimUnits.TryGetValue(unitId, out SimUnit unit)) return false;
        if (unit.PlayerId != playerId) return false;
        if (unit.Category != UnitCategory.Infantry) return false;

        bool success = _garrisonSystem.TryGarrison(unitId, buildingId);
        if (success)
        {
            EventBus.Instance?.EmitUnitGarrisoned(unitId, buildingId);
            GD.Print($"[Garrison] Unit {unitId} garrisoned in building {buildingId}.");
        }
        return success;
    }

    /// <summary>Ejects a unit from whatever garrison it is in.</summary>
    public bool TryEjectUnit(int unitId, int playerId)
    {
        if (CurrentMatchState != MatchState.Playing) return false;

        int buildingId = _garrisonSystem.GetGarrisonBuilding(unitId);
        if (buildingId < 0) return false;

        bool success = _garrisonSystem.TryEject(unitId);
        if (success)
            EventBus.Instance?.EmitUnitEjected(unitId, buildingId);
        return success;
    }

    /// <summary>Returns the garrison system for HUD queries.</summary>
    public GarrisonSystem GarrisonSystem => _garrisonSystem;

    /// <summary>Returns the superweapon system for HUD queries and ability activation.</summary>
    public SuperweaponSystem SuperweaponSystem => _superweaponSystem;

    /// <summary>
    /// Attempts to fire a specific superweapon for the local player targeting a world position.
    /// Returns the result (which may contain hit unit IDs to which damage must be applied).
    /// </summary>
    public SuperweaponResult ActivateSuperweapon(string weaponId, FixedVector2 targetPosition)
    {
        if (CurrentMatchState != MatchState.Playing)
            return new SuperweaponResult { TargetPosition = targetPosition, DidFire = false, WeaponId = weaponId };

        // Build a snapshot of all alive units for targeting
        var allAlive = new List<CorditeWars.Systems.Pathfinding.SimUnit>(_persistentSimUnits.Count);
        foreach (var kv in _persistentSimUnits)
            if (kv.Value.IsAlive)
                allAlive.Add(kv.Value);

        var result = _superweaponSystem.TryActivate(_localPlayerId, weaponId, targetPosition, allAlive);

        if (result.DidFire)
        {
            EventBus.Instance?.EmitSuperweaponFired(_localPlayerId, weaponId,
                new Vector3(targetPosition.X.ToFloat(), 0, targetPosition.Y.ToFloat()));

            // Apply damage from result
            for (int i = 0; i < result.HitUnitIds.Count; i++)
            {
                int uid = result.HitUnitIds[i];
                if (_persistentSimUnits.TryGetValue(uid, out SimUnit su))
                {
                    su.Health = FixedPoint.Max(FixedPoint.Zero, su.Health - result.DamagePerUnit[i]);
                    if (su.Health <= FixedPoint.Zero)
                        su.IsAlive = false;
                    _persistentSimUnits[uid] = su;
                }
            }

            // Handle EMP — suppress weapon cooldown refresh so enemies can't fire
            if (result.IsEMP)
            {
                foreach (var kv in _persistentSimUnits)
                {
                    SimUnit su = kv.Value;
                    if (su.PlayerId == _localPlayerId || !su.IsAlive) continue;

                    if (su.WeaponCooldowns != null)
                    {
                        for (int w = 0; w < su.WeaponCooldowns.Count; w++)
                            su.WeaponCooldowns[w] = FixedPoint.FromInt(result.EMPDurationTicks);
                    }
                    _persistentSimUnits[su.UnitId] = su;
                }
            }

            // Handle ReinforcementDrop — spawn units at the target position
            if (result.SpawnedUnitTypeIds.Count > 0 && _unitSpawner is not null)
            {
                string factionId = GetPlayerFactionId(_localPlayerId);

                // Scatter spawn positions in a small ring around the target
                FixedPoint spawnRadius = FixedPoint.FromInt(3);
                // Offsets at cardinal directions: N, E, S, W
                FixedPoint[] offX = [FixedPoint.Zero, spawnRadius,  FixedPoint.Zero, -spawnRadius];
                FixedPoint[] offY = [spawnRadius,  FixedPoint.Zero, -spawnRadius, FixedPoint.Zero];

                for (int i = 0; i < result.SpawnedUnitTypeIds.Count; i++)
                {
                    int idx = i % 4;
                    FixedVector2 spawnPos = new FixedVector2(
                        targetPosition.X + offX[idx],
                        targetPosition.Y + offY[idx]);
                    _unitSpawner.SpawnUnit(
                        result.SpawnedUnitTypeIds[i],
                        factionId,
                        _localPlayerId,
                        spawnPos,
                        facing: FixedPoint.Zero);
                }
            }
        }

        return result;
    }

    /// <summary>Returns the faction ID for a player, or an empty string if not found.</summary>
    private string GetPlayerFactionId(int playerId)
    {
        if (ActiveConfig is null) return string.Empty;
        foreach (var pc in ActiveConfig.PlayerConfigs)
            if (pc.PlayerId == playerId)
                return pc.FactionId;
        return string.Empty;
    }

    /// <summary>
    /// Reads the auto-save-replays setting from user://settings.cfg.
    /// Defaults to true if the file or key is absent.
    /// </summary>
    private static bool LoadAutoSaveReplaySetting()
    {
        const string Path = "user://settings.cfg";
        if (!FileAccess.FileExists(Path)) return true;

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return true;

        return (bool)cfg.GetValue("Game", "auto_save_replays", Variant.CreateFrom(true));
    }

    // ─────────────────────────────────────────────────────────────────
    // SIMULATION TICK
    // ─────────────────────────────────────────────────────────────────

    private void HandleSimulationTick(ulong currentTick)
    {
        if (_unitInteractionSystem == null || _unitSpawner == null || _terrainGrid == null)
            return;

        // ── 0. Execute queued commands from this tick ─────────────────────
        // Process commands from the CommandBuffer that are scheduled for
        // this tick. SetStance commands update _persistentSimUnits before
        // the sim units list is built below so the change takes effect immediately.
        // Move/Stop/HoldPosition/Patrol commands are also processed here via
        // path requests and direct state changes.
        var gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (gameManager?.CommandBuffer != null)
        {
            var cmds = gameManager.CommandBuffer.GetCommandsForTick(currentTick);
            for (int c = 0; c < cmds.Count; c++)
            {
                var cmd = cmds[c];
                switch (cmd)
                {
                    case CorditeWars.Systems.Networking.SetStanceCommand stanceCmd:
                    {
                        for (int k = 0; k < stanceCmd.UnitIds.Count; k++)
                        {
                            int uid = stanceCmd.UnitIds[k];
                            if (_persistentSimUnits.TryGetValue(uid, out SimUnit su))
                            {
                                su.Stance = stanceCmd.Stance;
                                _persistentSimUnits[uid] = su;
                            }
                        }
                        break;
                    }
                    case CorditeWars.Systems.Networking.MoveCommand moveCmd:
                    {
                        for (int k = 0; k < moveCmd.UnitIds.Count; k++)
                        {
                            int uid = moveCmd.UnitIds[k];
                            if (_persistentSimUnits.TryGetValue(uid, out SimUnit su))
                            {
                                int capturedId = uid;
                                MovementProfile profile = su.Profile;
                                FixedVector2 from = su.Movement.Position;
                                FixedVector2 to   = moveCmd.TargetPosition;
                                _pathRequestManager?.RequestPath(capturedId, profile, from, to,
                                    path =>
                                    {
                                        if (_persistentSimUnits.TryGetValue(capturedId, out SimUnit s2))
                                        {
                                            s2.CurrentPath = path;
                                            s2.CurrentWaypointIndex = 0;
                                            s2.ActiveFlowField = null;
                                            _persistentSimUnits[capturedId] = s2;
                                        }
                                    });
                            }
                        }
                        break;
                    }
                    case CorditeWars.Systems.Networking.StopCommand stopCmd:
                    {
                        for (int k = 0; k < stopCmd.UnitIds.Count; k++)
                        {
                            int uid = stopCmd.UnitIds[k];
                            if (_persistentSimUnits.TryGetValue(uid, out SimUnit su))
                            {
                                su.CurrentPath = null;
                                su.ActiveFlowField = null;
                                su.CurrentTargetId = null;
                                _persistentSimUnits[uid] = su;
                            }
                        }
                        break;
                    }
                    case CorditeWars.Systems.Networking.HoldPositionCommand holdCmd:
                    {
                        for (int k = 0; k < holdCmd.UnitIds.Count; k++)
                        {
                            int uid = holdCmd.UnitIds[k];
                            if (_persistentSimUnits.TryGetValue(uid, out SimUnit su))
                            {
                                su.CurrentPath = null;
                                su.ActiveFlowField = null;
                                _persistentSimUnits[uid] = su;
                            }
                        }
                        break;
                    }
                }
            }
        }

        // ── 0b. Tick superweapon cooldowns ─────────────────────────────────
        // Track which weapons were ready before ticking so we can fire "ready" events
        var readyBefore = new System.Collections.Generic.HashSet<string>();
        foreach (var ws in _superweaponSystem.GetPlayerWeapons(_localPlayerId))
            if (ws.IsReady) readyBefore.Add(ws.Data.Id);

        _superweaponSystem.Tick();

        // Emit SuperweaponReady for each weapon that just became ready this tick
        foreach (var ws in _superweaponSystem.GetPlayerWeapons(_localPlayerId))
        {
            if (ws.IsReady && !readyBefore.Contains(ws.Data.Id))
                EventBus.Instance?.EmitSuperweaponReady(_localPlayerId, ws.Data.Id);
        }

        // ── 1. Build combined SimUnit list (mobile units + buildings) ──────

        var allNodes = _unitSpawner.GetAllUnits();
        var simUnits = new List<SimUnit>(allNodes.Count + 16);

        // 1a. Mobile units — use or initialise persistent state
        for (int i = 0; i < allNodes.Count; i++)
        {
            var node = allNodes[i];
            if (!node.IsAlive) continue;

            if (!_persistentSimUnits.TryGetValue(node.UnitId, out SimUnit sim))
                sim = InitSimUnitFromNode(node);

            simUnits.Add(sim);
        }

        // 1b. Pre-placed HQ buildings (direct children of GameSession)
        foreach (var kvp in _playerHQNodes)
        {
            var b = kvp.Value;
            if (b == null || !GodotObject.IsInstanceValid(b)) continue;
            simUnits.Add(BuildSimUnitFromBuilding(b));
        }

        // 1c. Player-placed buildings (managed by BuildingPlacer)
        if (_buildingPlacer != null)
        {
            var allBuildings = _buildingPlacer.GetAllBuildings();
            for (int i = 0; i < allBuildings.Count; i++)
            {
                var b = allBuildings[i];
                if (b == null || !GodotObject.IsInstanceValid(b)) continue;
                simUnits.Add(BuildSimUnitFromBuilding(b));
            }
        }

        // Sort ascending by UnitId — required by UnitInteractionSystem
        simUnits.Sort((a, b) => a.UnitId.CompareTo(b.UnitId));

        // Snapshot building health before the tick so we can compute deltas after
        var buildingHealthBefore = new Dictionary<int, FixedPoint>();
        foreach (var kvp in _playerHQNodes)
        {
            if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
                buildingHealthBefore[kvp.Value.BuildingId] = kvp.Value.Health;
        }
        if (_buildingPlacer != null)
        {
            var allBuildings = _buildingPlacer.GetAllBuildings();
            for (int i = 0; i < allBuildings.Count; i++)
            {
                var b = allBuildings[i];
                if (b != null && GodotObject.IsInstanceValid(b))
                    buildingHealthBefore[b.BuildingId] = b.Health;
            }
        }

        // ── 2. Process Tick ────────────────────────────────────────────────
        TickResult tickResult = _unitInteractionSystem.ProcessTick(simUnits, _terrainGrid, currentTick);

        // ── 3. Emit combat audio (before despawn so nodes are still alive) ──
        EmitCombatAudioEvents(tickResult, simUnits);

        // ── 4a. Persist updated mobile unit state and sync visual nodes ─────
        // ProcessTick has removed dead units from simUnits (phase 8b).
        // Rebuild _persistentSimUnits from the surviving mobile entries.
        _persistentSimUnits.Clear();
        for (int i = 0; i < simUnits.Count; i++)
        {
            SimUnit sim = simUnits[i];
            if (!IsBuildingId(sim.UnitId))
            {
                _persistentSimUnits[sim.UnitId] = sim;
                var node = _unitSpawner.GetUnit(sim.UnitId);
                if (node != null && node.IsAlive)
                {
                    node.SyncFromSimulation(sim.Movement.Position, sim.Movement.Facing, sim.Health,
                        sim.Stance, sim.XP, sim.Veterancy,
                        _terrainRenderer?.GetElevationAtWorld(
                            sim.Movement.Position.X.ToFloat(),
                            sim.Movement.Position.Y.ToFloat()) ?? 0f);

                    // Sync stealth visual: own units appear as ghosts, enemy stealthed
                    // units are fully hidden until detected or they fire.
                    if (sim.IsStealthUnit)
                    {
                        bool isOwnUnit = sim.PlayerId == _localPlayerId;
                        node.SetStealthed(sim.IsCurrentlyStealthed, isOwnUnit);
                    }
                }
            }
            else
            {
                // Persist building weapon state so cooldowns survive across ticks
                _buildingWeaponCooldowns[sim.UnitId] = sim.WeaponCooldowns ?? new List<FixedPoint>();
                _buildingCurrentTargets[sim.UnitId] = sim.CurrentTargetId;
            }
        }

        // ── 4b. Apply building health changes ─────────────────────────────
        // Take a snapshot of the buildings list to avoid modifying it while
        // TakeDamage might trigger OnBuildingDestroyed (which modifies _buildings).
        var buildingSnapshot = new List<BuildingInstance>();
        foreach (var kvp in _playerHQNodes)
        {
            if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
                buildingSnapshot.Add(kvp.Value);
        }
        if (_buildingPlacer != null)
        {
            var allBuildings = _buildingPlacer.GetAllBuildings();
            for (int i = 0; i < allBuildings.Count; i++)
            {
                if (allBuildings[i] != null && GodotObject.IsInstanceValid(allBuildings[i]))
                    buildingSnapshot.Add(allBuildings[i]);
            }
        }

        // Apply lethal damage to buildings that were destroyed this tick
        for (int d = 0; d < tickResult.DestroyedUnitIds.Count; d++)
        {
            int destroyedId = tickResult.DestroyedUnitIds[d];
            if (!IsBuildingId(destroyedId)) continue;

            for (int bi = 0; bi < buildingSnapshot.Count; bi++)
            {
                var b = buildingSnapshot[bi];
                if (b.BuildingId == destroyedId && GodotObject.IsInstanceValid(b) &&
                    b.Health > FixedPoint.Zero)
                {
                    b.TakeDamage(b.Health); // brings to 0 and triggers Destroy()
                    break;
                }
            }
        }

        // Sync partial damage to buildings that survived but lost health
        for (int i = 0; i < simUnits.Count; i++)
        {
            SimUnit sim = simUnits[i];
            if (!IsBuildingId(sim.UnitId)) continue;

            for (int bi = 0; bi < buildingSnapshot.Count; bi++)
            {
                var b = buildingSnapshot[bi];
                if (b.BuildingId != sim.UnitId || !GodotObject.IsInstanceValid(b)) continue;

                if (buildingHealthBefore.TryGetValue(sim.UnitId, out FixedPoint prevHealth) &&
                    sim.Health < prevHealth)
                {
                    FixedPoint delta = prevHealth - sim.Health;
                    b.TakeDamage(delta);
                }
                break;
            }
        }

        // ── 4c. Despawn mobile unit visual nodes ───────────────────────────
        // Track kills/losses and despawn in a single loop
        for (int i = 0; i < tickResult.DestroyedUnitIds.Count; i++)
        {
            int destroyedId = tickResult.DestroyedUnitIds[i];
            if (!IsBuildingId(destroyedId))
            {
                // Track kill/loss before despawning
                if (_persistentSimUnits.TryGetValue(destroyedId, out SimUnit deadSim))
                {
                    if (deadSim.PlayerId == _localPlayerId)
                        _playerLosses++;
                    else
                    {
                        _playerKills++;
                        // Advance DestroyUnitType campaign objectives for enemy unit deaths
                        if (_objectiveTracker is not null)
                        {
                            string? unitTypeId = _unitSpawner?.GetUnit(destroyedId)?.UnitTypeId;
                            if (!string.IsNullOrEmpty(unitTypeId))
                                _objectiveTracker.NotifyUnitDestroyed(unitTypeId);
                        }
                        // SteamManager.Instance?.RecordUnitsDestroyed(1); // disabled until Steam integration is re-enabled
                    }
                }
                _unitSpawner?.DespawnUnit(destroyedId);
            }
        }

        // ── 5. Update fog of war / vision ──────────────────────────────────
        UpdateFogOfWar(simUnits, currentTick);

        // ── 6. Check win condition ─────────────────────────────────────────
        CheckWinCondition();

        // ── 7. Objective tracker ──────────────────────────────────────────
        if (_objectiveTracker is not null && _economyManager is not null)
        {
            var ctx = new CorditeWars.Game.Campaign.MissionSessionContext();
            ctx.AllBuildings  = _buildingPlacer?.GetAllBuildings() ?? (System.Collections.Generic.IList<BuildingInstance>)System.Array.Empty<BuildingInstance>();
            ctx.AliveUnits    = _unitSpawner?.GetAllUnits() ?? (System.Collections.Generic.IList<UnitNode3D>)System.Array.Empty<UnitNode3D>();
            ctx.PlayerCordite = _economyManager.GetPlayer(_localPlayerId)?.Cordite ?? FixedPoint.Zero;
            _objectiveTracker.Tick(_localPlayerId, ctx, currentTick);
            if (_objectiveTracker.AllPrimaryObjectivesComplete)
                EndMatch(_localPlayerId, "All objectives complete.");
            else if (_objectiveTracker.AnyObjectiveFailed)
                EndMatch(-1, "Mission failed: objective failed.");
        }

        // ── 8. Tutorial tick ──────────────────────────────────────────────
        if (_tutorialManager is not null && _economyManager is not null)
        {
            int cordite = _economyManager.GetPlayer(_localPlayerId)?.Cordite.ToInt() ?? 0;
            _tutorialManager.NotifyCordite(cordite);
            _tutorialManager.Tick(TickDeltaSeconds);
        }

        // ── 9. Autosave ───────────────────────────────────────────────────
        if (currentTick >= AutosaveIntervalTicks && currentTick - _lastAutosaveTick >= AutosaveIntervalTicks)
        {
            _lastAutosaveTick = currentTick;
            SaveCurrentState("autosave_0");
        }
    }

    /// <summary>
    /// Returns true if <paramref name="unitId"/> belongs to a building rather
    /// than a mobile unit.  Pre-placed HQ buildings use negative IDs; player-
    /// placed buildings use IDs ≥ 100_001 (<c>BuildingPlacer._nextBuildingId</c>
    /// starts at 100_001).  Mobile units occupy 1..100_000 (<c>UnitSpawner</c>
    /// starts at 1 and a match will not reach 100_000 live units simultaneously).
    /// </summary>
    private static bool IsBuildingId(int unitId) => unitId < 0 || unitId >= 100_001;

    /// <summary>
    /// Returns the ordered list of <see cref="TutorialStep"/>s for the given tutorial mission number.
    /// </summary>
    private static List<CorditeWars.Game.Tutorial.TutorialStep> BuildTutorialSteps(int mission)
    {
        const CorditeWars.Game.Tutorial.TriggerCondition T  = CorditeWars.Game.Tutorial.TriggerCondition.TimerSeconds;
        const CorditeWars.Game.Tutorial.TriggerCondition US = CorditeWars.Game.Tutorial.TriggerCondition.UnitSelected;
        const CorditeWars.Game.Tutorial.TriggerCondition BP = CorditeWars.Game.Tutorial.TriggerCondition.BuildingPlaced;
        const CorditeWars.Game.Tutorial.TriggerCondition CA = CorditeWars.Game.Tutorial.TriggerCondition.CorditeAbove;

        return mission switch
        {
            1 => new List<CorditeWars.Game.Tutorial.TutorialStep>
            {
                new() { Id="m1_01", Title="Mission 1 \u2014 Movement & Camera",
                        Body="Welcome, Commander! This mission teaches you how to move around the battlefield and navigate the interface.",
                        TriggerCondition=T, TriggerValue=6f },
                new() { Id="m1_02", Title="Camera \u2014 Zoom",
                        Body="Scroll the mouse wheel to zoom in and out. Zoom in to see unit details; zoom out for a strategic overview.",
                        TriggerCondition=T, TriggerValue=8f },
                new() { Id="m1_03", Title="Camera \u2014 Pan",
                        Body="Hold Middle Mouse Button (or move to screen edges) to pan the camera across the map. Try panning now.",
                        TriggerCondition=T, TriggerValue=8f },
                new() { Id="m1_04", Title="Select a Unit",
                        Body="Left-click your harvester unit to select it. Selected units show a green ring underneath them.",
                        TriggerCondition=US, TriggerValue=0f },
                new() { Id="m1_05", Title="Move a Unit",
                        Body="With your harvester selected, right-click anywhere on the ground to move it there. Try moving it now.",
                        TriggerCondition=T, TriggerValue=10f },
                new() { Id="m1_06", Title="Box Selection",
                        Body="Hold left mouse button and drag to draw a selection box. All friendly units inside the box will be selected.",
                        TriggerCondition=T, TriggerValue=8f },
                new() { Id="m1_07", Title="The Minimap",
                        Body="The minimap (bottom-left) shows the full battlefield. Left-click it to jump the camera to that location.",
                        TriggerCondition=T, TriggerValue=7f },
                new() { Id="m1_08", Title="The Build Menu",
                        Body="The Build panel (bottom-right) lists structures you can construct. Click a building icon to start placing it.",
                        TriggerCondition=T, TriggerValue=7f },
                new() { Id="m1_09", Title="Pause Menu",
                        Body="Press Escape at any time to open the Pause menu where you can adjust settings or quit.",
                        TriggerCondition=T, TriggerValue=6f },
                new() { Id="m1_10", Title="Mission 1 Complete!",
                        Body="You've mastered movement and the camera. Proceed to Mission 2 to learn about buildings and units.",
                        TriggerCondition=T, TriggerValue=5f },
            },
            2 => new List<CorditeWars.Game.Tutorial.TutorialStep>
            {
                new() { Id="m2_01", Title="Mission 2 \u2014 Buildings & Units",
                        Body="Welcome back! This mission teaches you how to build structures and train units.",
                        TriggerCondition=T, TriggerValue=6f },
                new() { Id="m2_02", Title="Cordite \u2014 Your Economy",
                        Body="Cordite is your resource. Your harvester collects it from glowing nodes. Right-click a node to send your harvester there.",
                        TriggerCondition=CA, TriggerValue=2000f },
                new() { Id="m2_03", Title="Build a Refinery",
                        Body="Good, Cordite is flowing! A Refinery increases your income rate. Open the Build menu and place a Refinery now.",
                        TriggerCondition=BP, TriggerValue=0f },
                new() { Id="m2_04", Title="Build a Barracks",
                        Body="Excellent! Now build a Barracks to train infantry. Select it from the Build menu and place it near your base.",
                        TriggerCondition=BP, TriggerValue=0f },
                new() { Id="m2_05", Title="Train Infantry",
                        Body="Click your Barracks to select it. A unit-training panel will appear \u2014 click an infantry unit to queue training.",
                        TriggerCondition=US, TriggerValue=0f },
                new() { Id="m2_06", Title="Attack the Enemy",
                        Body="Select your trained units and right-click the enemy base on the minimap to issue an attack order.",
                        TriggerCondition=T, TriggerValue=30f },
                new() { Id="m2_07", Title="Destroy the HQ",
                        Body="Destroy the enemy Command Center to win the match. Focus fire on the HQ \u2014 concentrate your forces!",
                        TriggerCondition=T, TriggerValue=60f },
                new() { Id="m2_08", Title="Mission 2 Complete!",
                        Body="You now understand economy, construction, and unit training. Proceed to Mission 3 for advanced tactics.",
                        TriggerCondition=T, TriggerValue=5f },
            },
            _ => new List<CorditeWars.Game.Tutorial.TutorialStep>
            {
                new() { Id="m3_01", Title="Mission 3 \u2014 Advanced Strategy",
                        Body="Welcome to your final training mission. You will learn tech buildings, advanced units, and multi-front tactics.",
                        TriggerCondition=T, TriggerValue=6f },
                new() { Id="m3_02", Title="Research Lab",
                        Body="Build a Tech Lab from the Build menu. It unlocks advanced units and global upgrades for your faction.",
                        TriggerCondition=BP, TriggerValue=0f },
                new() { Id="m3_03", Title="Upgrades",
                        Body="Select your Tech Lab and browse the Upgrades tab. Researching upgrades permanently improves your entire army.",
                        TriggerCondition=T, TriggerValue=12f },
                new() { Id="m3_04", Title="Advanced Units",
                        Body="Build a Vehicle Factory or Airfield. Advanced units are powerful but expensive \u2014 use them to break stalemates.",
                        TriggerCondition=BP, TriggerValue=0f },
                new() { Id="m3_05", Title="Control Groups",
                        Body="Select a group of units and press Ctrl+1-9 to assign them a control group. Press 1-9 to reselect instantly.",
                        TriggerCondition=T, TriggerValue=10f },
                new() { Id="m3_06", Title="Fog of War",
                        Body="In real missions, Fog of War hides the map. Station detector units near your perimeter to spot stealthed enemies.",
                        TriggerCondition=T, TriggerValue=10f },
                new() { Id="m3_07", Title="Multi-Front Tactics",
                        Body="Use control groups to manage separate attack waves. While one group distracts, send another to flank the enemy base.",
                        TriggerCondition=T, TriggerValue=12f },
                new() { Id="m3_08", Title="Victory!",
                        Body="You are ready for the campaign, Commander. Choose a faction from the Campaign menu and begin the war for Cordite.",
                        TriggerCondition=T, TriggerValue=5f },
            }
        };
    }

    /// <summary>
    /// Creates a fresh <see cref="SimUnit"/> for a mobile unit on its first
    /// appearance in the simulation.  All movement state starts at rest.
    /// </summary>
    private SimUnit InitSimUnitFromNode(UnitNode3D node)
    {
        UnitData? unitData = _unitDataRegistry.HasUnit(node.UnitTypeId)
            ? _unitDataRegistry.GetUnitData(node.UnitTypeId)
            : null;

        var weapons = unitData?.Weapons ?? new List<WeaponData>();
        var cooldowns = new List<FixedPoint>(weapons.Count);
        for (int w = 0; w < weapons.Count; w++)
            cooldowns.Add(FixedPoint.Zero);

        return new SimUnit
        {
            UnitId              = node.UnitId,
            PlayerId            = node.PlayerId,
            Movement            = new MovementState
            {
                Position = node.SimPosition,
                Facing   = node.SimFacing
            },
            Health              = node.Health,
            MaxHealth           = node.MaxHealth,
            ArmorValue          = node.ArmorValue,
            ArmorClass          = node.ArmorClass,
            Category            = node.Category,
            SightRange          = node.SightRange,
            Profile             = node.MovementProfile ?? MovementProfile.Infantry(),
            Radius              = node.Radius,
            IsAlive             = true,
            Weapons             = weapons,
            WeaponCooldowns     = cooldowns,
            CurrentTargetId     = null,
            CurrentPath         = null,
            ActiveFlowField     = null,
            CurrentWaypointIndex = 0,
            IsStealthUnit        = node.IsStealthUnit,
            IsDetector           = node.IsDetector,
            StealthRevealTicks   = 0,
            IsCurrentlyStealthed = node.IsStealthUnit,
            // Stance and veterancy default to Aggressive/Recruit for newly spawned units
            Stance     = UnitStance.Aggressive,
            XP         = 0,
            Veterancy  = VeterancyLevel.Recruit
        };
    }

    /// <summary>
    /// Builds a transient <see cref="SimUnit"/> for a building each tick.
    /// Weapon cooldowns and target IDs are sourced from persistent dictionaries
    /// so they survive between ticks even though the struct is recreated.
    /// </summary>
    private SimUnit BuildSimUnitFromBuilding(BuildingInstance b)
    {
        int fw = b.Data?.FootprintWidth  ?? 3;
        int fh = b.Data?.FootprintHeight ?? 3;

        // Centre of the building footprint in world/grid space
        FixedVector2 center = new FixedVector2(
            FixedPoint.FromInt(b.GridX) + FixedPoint.FromInt(fw) / FixedPoint.FromInt(2),
            FixedPoint.FromInt(b.GridY) + FixedPoint.FromInt(fh) / FixedPoint.FromInt(2));

        // Collision radius = half the diagonal of the footprint
        float diagHalf = (float)Math.Sqrt(fw * fw + fh * fh) * 0.5f;

        var weapons = b.Data?.Weapons ?? new List<WeaponData>();

        if (!_buildingWeaponCooldowns.TryGetValue(b.BuildingId, out var cooldowns))
        {
            cooldowns = new List<FixedPoint>(weapons.Count);
            for (int w = 0; w < weapons.Count; w++)
                cooldowns.Add(FixedPoint.Zero);
        }

        _buildingCurrentTargets.TryGetValue(b.BuildingId, out int? targetId);

        return new SimUnit
        {
            UnitId               = b.BuildingId,
            PlayerId             = b.PlayerId,
            Movement             = new MovementState
            {
                Position = center,
                Facing   = FixedPoint.Zero
            },
            Health               = b.Health,
            MaxHealth            = b.MaxHealth,
            ArmorValue           = b.Data?.ArmorValue ?? FixedPoint.Zero,
            ArmorClass           = b.Data?.ArmorClass ?? ArmorType.Building,
            Category             = UnitCategory.Defense,
            SightRange           = b.Data?.SightRange ?? FixedPoint.FromInt(5),
            Profile              = MovementProfile.Building(fw, fh),
            Radius               = FixedPoint.FromFloat(diagHalf),
            IsAlive              = b.Health > FixedPoint.Zero,
            Weapons              = weapons,
            WeaponCooldowns      = cooldowns,
            CurrentTargetId      = targetId,
            CurrentPath          = null,
            ActiveFlowField      = null,
            CurrentWaypointIndex = 0
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // WIN CONDITION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once per simulation tick and on building destruction.
    /// Routes to the appropriate win-condition checker.
    /// </summary>
    private void CheckWinCondition()
    {
        if (CurrentMatchState != MatchState.Playing || ActiveConfig is null) return;

        switch (_winCondition)
        {
            case WinCondition.DestroyHQ:    CheckHQDestroyedWin();    break;
            case WinCondition.KillAllUnits: CheckAllUnitsKilledWin(); break;
        }
    }

    /// <summary>
    /// DestroyHQ win condition: the match ends when any tracked player's
    /// Command Centre is no longer in <see cref="_playerHQNodes"/>.
    /// In multi-player (3+ factions), the game continues until only one
    /// player retains an HQ.
    /// </summary>
    private void CheckHQDestroyedWin()
    {
        if (ActiveConfig is null) return;

        // Count how many players still have a standing HQ (and haven't surrendered)
        int survivorCount = 0;
        int lastSurvivorPid = -1;
        for (int i = 0; i < ActiveConfig.PlayerConfigs.Length; i++)
        {
            int pid = ActiveConfig.PlayerConfigs[i].PlayerId;
            if (!_playersWithInitialHQ.Contains(pid)) continue; // no HQ data → skip
            if (_surrenderedPlayers.Contains(pid)) continue;    // surrendered → eliminated
            if (_playerHQNodes.ContainsKey(pid))
            {
                survivorCount++;
                lastSurvivorPid = pid;
            }
        }

        // The match ends when at most one HQ-owning player remains
        if (_playersWithInitialHQ.Count > 0 && survivorCount <= 1)
        {
            // Find the eliminated player for the end-reason string
            int eliminatedPid = -1;
            for (int i = 0; i < ActiveConfig.PlayerConfigs.Length; i++)
            {
                int pid = ActiveConfig.PlayerConfigs[i].PlayerId;
                if (_playersWithInitialHQ.Contains(pid) &&
                    (!_playerHQNodes.ContainsKey(pid) || _surrenderedPlayers.Contains(pid)))
                {
                    eliminatedPid = pid;
                    break;
                }
            }

            string reason = eliminatedPid != -1
                ? (_surrenderedPlayers.Contains(eliminatedPid)
                    ? $"Player {eliminatedPid} surrendered."
                    : $"Player {eliminatedPid}'s Command Centre was destroyed.")
                : "All Command Centres have been destroyed.";

            EndMatch(lastSurvivorPid, reason);
        }
    }

    /// <summary>
    /// KillAllUnits win condition: the match ends when any player has no
    /// surviving mobile units.  In multi-player matches the game continues
    /// until only one player retains forces.
    /// </summary>
    private void CheckAllUnitsKilledWin()
    {
        if (ActiveConfig is null || _unitSpawner is null) return;

        var allNodes = _unitSpawner.GetAllUnits();

        // Count players who still have at least one living mobile unit (and haven't surrendered)
        int survivorCount = 0;
        int lastSurvivorPid = -1;
        int eliminatedPid = -1;

        for (int i = 0; i < ActiveConfig.PlayerConfigs.Length; i++)
        {
            int pid = ActiveConfig.PlayerConfigs[i].PlayerId;

            // Surrendered players are treated as having no units
            if (_surrenderedPlayers.Contains(pid))
            {
                if (eliminatedPid == -1) eliminatedPid = pid;
                continue;
            }

            bool hasUnits = false;
            for (int u = 0; u < allNodes.Count; u++)
            {
                if (allNodes[u].PlayerId == pid && allNodes[u].IsAlive)
                {
                    hasUnits = true;
                    break;
                }
            }

            if (hasUnits)
            {
                survivorCount++;
                lastSurvivorPid = pid;
            }
            else if (eliminatedPid == -1)
            {
                eliminatedPid = pid;
            }
        }

        // End only when at most one player still has units
        if (ActiveConfig.PlayerConfigs.Length > 0 && survivorCount <= 1)
        {
            string reason = eliminatedPid != -1
                ? $"All of player {eliminatedPid}'s forces have been eliminated."
                : "All forces have been eliminated.";

            EndMatch(lastSurvivorPid, reason);
        }
    }

    /// <summary>
    /// Handler for <see cref="EventBus.BuildingDestroyed"/> signal.
    /// Notifies BuildingPlacer, updates HQ tracking, cleans up building
    /// weapon state, then checks the win condition immediately.
    /// </summary>
    private void OnBuildingDestroyed(Node building)
    {
        if (building is not BuildingInstance b) return;

        // Track enemy buildings destroyed for post-match stats
        if (b.PlayerId != _localPlayerId)
            _buildingsDestroyed++;

        // Eject all garrisoned units from the destroyed building
        var ejected = _garrisonSystem.OnBuildingDestroyed(b.BuildingId);
        for (int e = 0; e < ejected.Count; e++)
        {
            EventBus.Instance?.EmitUnitEjected(ejected[e], b.BuildingId);
        }

        // Notify BuildingPlacer so it can remove the entry from its dict
        // and vacate the occupancy grid.
        _buildingPlacer?.OnBuildingDestroyed(b);

        // Advance any DestroyBuildingType campaign objectives
        _objectiveTracker?.NotifyBuildingDestroyed(b.BuildingTypeId);

        // Remove the ghost for this building from all players' fog snapshots
        if (_playerFogSnapshots != null)
        {
            for (int p = 0; p < _playerFogSnapshots.Length; p++)
                _playerFogSnapshots[p].OnEntityDestroyed(b.BuildingId);
        }

        // Remove from HQ tracking if this was a player's Command Centre
        if (_playersWithInitialHQ.Contains(b.PlayerId) &&
            _playerHQNodes.TryGetValue(b.PlayerId, out var hqNode) &&
            hqNode?.BuildingId == b.BuildingId)
        {
            _playerHQNodes.Remove(b.PlayerId);
        }

        // Clean up persistent building sim state
        _buildingWeaponCooldowns.Remove(b.BuildingId);
        _buildingCurrentTargets.Remove(b.BuildingId);

        // Evaluate win condition immediately on destruction
        CheckWinCondition();
    }

    /// <summary>
    /// Emits EventBus signals for combat sounds based on tick results.
    /// Called once per tick after simulation state has been applied.
    /// </summary>
    private void EmitCombatAudioEvents(TickResult tickResult, List<SimUnit> simUnits)
    {
        var bus = EventBus.Instance;
        if (bus == null) return;

        // Skip if nothing happened this tick
        if (tickResult.Attacks.Count == 0 && tickResult.DestroyedUnitIds.Count == 0)
            return;

        // Build O(1) lookup from unit ID → SimUnit index
        // (avoids O(n²) linear scans per attack/death)
        var unitLookup = new Dictionary<int, int>(simUnits.Count);
        for (int i = 0; i < simUnits.Count; i++)
        {
            unitLookup[simUnits[i].UnitId] = i;
        }

        // Weapon fire + impact sounds
        for (int i = 0; i < tickResult.Attacks.Count; i++)
        {
            AttackResult attack = tickResult.Attacks[i];

            // Find attacker's weapon type
            WeaponType weaponType = WeaponType.None;
            Vector3 attackerPos = Vector3.Zero;
            if (unitLookup.TryGetValue(attack.AttackerId, out int attackerIdx))
            {
                SimUnit attacker = simUnits[attackerIdx];
                attackerPos = attacker.Movement.Position.ToVector3();

                if (attacker.Weapons != null &&
                    attack.WeaponIndex < attacker.Weapons.Count)
                {
                    weaponType = attacker.Weapons[attack.WeaponIndex].Type;
                }
            }

            // Emit weapon fire event at attacker position
            bus.EmitAttackFired(attack.AttackerId, (int)weaponType, attackerPos);

            // Emit impact event at impact position
            Vector3 impactPos = attack.ImpactPosition.ToVector3();

            bool hasAoe = attack.SplashTargets != null && attack.SplashTargets.Count > 0;
            bus.EmitAttackImpact(attack.TargetId, attack.DidHit, hasAoe, impactPos);
        }

        // Unit death sounds
        for (int i = 0; i < tickResult.DestroyedUnitIds.Count; i++)
        {
            int destroyedId = tickResult.DestroyedUnitIds[i];

            // Try to get position and category from the node before despawn,
            // or from simUnits if still present
            var node = _unitSpawner?.GetUnit(destroyedId);
            if (node != null)
            {
                bus.EmitUnitDeath(destroyedId, (int)node.Category, node.GlobalPosition);
            }
            else if (unitLookup.TryGetValue(destroyedId, out int deadIdx))
            {
                SimUnit dead = simUnits[deadIdx];
                Vector3 pos = dead.Movement.Position.ToVector3();
                bus.EmitUnitDeath(destroyedId, (int)dead.Category, pos);
            }
        }
    }

    /// <summary>
    /// Updates fog of war for all players using current unit positions.
    /// Called at the end of each simulation tick, after combat and cleanup.
    /// </summary>
    private void UpdateFogOfWar(List<SimUnit> simUnits, ulong currentTick)
    {
        if (_visionSystem == null || _playerFogs == null || _terrainGrid == null)
            return;

        // Build VisionComponent list from surviving SimUnits
        _visionComponents.Clear();
        for (int i = 0; i < simUnits.Count; i++)
        {
            SimUnit su = simUnits[i];
            if (!su.IsAlive) continue;

            bool isAir = su.Category == UnitCategory.Helicopter ||
                         su.Category == UnitCategory.Jet;

            _visionComponents.Add(new VisionComponent
            {
                UnitId = su.UnitId,
                PlayerId = su.PlayerId,
                Position = su.Movement.Position,
                SightRange = su.SightRange,
                Height = _terrainGrid.GetHeight(su.Movement.Position),
                IsAirUnit = isAir
            });
        }

        // Update vision for each player's fog grid
        for (int p = 0; p < _playerFogs.Length; p++)
        {
            _visionSystem.UpdateVision(_playerFogs[p], _terrainGrid, _visionComponents);
        }

        // Update FogSnapshot ghost entities for each player based on the refreshed fog grids.
        // After every vision update we reconcile each player's "last-known" building ghosts:
        //   • Visible cell  → remove any stale ghost (the player now sees the real building).
        //   • Explored cell → add/update a ghost (the player has been there but can't see it now).
        //   • Unexplored    → ignore (the player has never seen this area).
        if (_playerFogSnapshots != null && _buildingPlacer != null)
        {
            var allBuildings = _buildingPlacer.GetAllBuildings();

            for (int p = 0; p < _playerFogs.Length; p++)
            {
                FogGrid fog = _playerFogs[p];
                FogSnapshot snapshot = _playerFogSnapshots[p];
                int ownerPlayerId = fog.PlayerId;

                for (int b = 0; b < allBuildings.Count; b++)
                {
                    var bldg = allBuildings[b];
                    if (bldg.PlayerId == ownerPlayerId) continue; // own buildings are never ghosted

                    FogVisibility vis = fog.GetVisibility(bldg.GridX, bldg.GridY);

                    if (vis == FogVisibility.Visible)
                    {
                        // Player currently sees this cell — show the real building, remove ghost
                        snapshot.OnEntityBecameVisible(bldg.BuildingId);
                    }
                    else if (vis == FogVisibility.Explored)
                    {
                        // Previously visited but no longer visible — keep/update the ghost
                        FixedPoint healthPct = bldg.MaxHealth > FixedPoint.Zero
                            ? bldg.Health / bldg.MaxHealth
                            : FixedPoint.Zero;

                        snapshot.OnEntityBecameHidden(
                            bldg.BuildingId,
                            bldg.PlayerId,
                            new FixedVector2(
                                FixedPoint.FromInt(bldg.GridX),
                                FixedPoint.FromInt(bldg.GridY)),
                            bldg.BuildingTypeId,
                            healthPct,
                            isBuilding: true,
                            currentTick);
                    }
                    // Unexplored: player has never seen this area — no ghost
                }
            }
        }
    }

    /// <summary>
    /// Returns the fog grid for the given player, or null if fog of war is disabled.
    /// </summary>
    public FogGrid? GetPlayerFog(int playerId)
    {
        if (_playerFogs == null) return null;

        for (int i = 0; i < _playerFogs.Length; i++)
        {
            if (_playerFogs[i].PlayerId == playerId)
                return _playerFogs[i];
        }
        return null;
    }

    /// <summary>
    /// Returns the fog snapshot for the given player, or null if fog of war is disabled.
    /// </summary>
    public FogSnapshot? GetPlayerFogSnapshot(int playerId)
    {
        if (_playerFogSnapshots == null || _playerFogs == null) return null;

        for (int i = 0; i < _playerFogSnapshots.Length; i++)
        {
            if (_playerFogs[i].PlayerId == playerId)
                return _playerFogSnapshots[i];
        }
        return null;
    }

    /// <summary>
    /// Saves the current match state to the given slot.
    /// Delegates to SaveManager after collecting state from all managers.
    /// </summary>
    public bool SaveCurrentState(string slotName)
    {
        if (_saveManager is null || _gameManager is null)
        {
            GD.PushError("[GameSession] Cannot save — managers not initialized.");
            return false;
        }

        SaveGameData data = GetMatchState();
        return _saveManager.SaveGame(slotName, data);
    }

    /// <summary>Returns accumulated post-match statistics for the local player.</summary>
    public readonly struct MatchStats
    {
        public int Kills                { get; init; }
        public int Losses               { get; init; }
        public int BuildingsConstructed { get; init; }
        public int BuildingsDestroyed   { get; init; }
        public int UnitsProduced        { get; init; }
        public int CorditeHarvested     { get; init; }
    }

    /// <summary>Returns the current match stats (kills, losses, buildings, units, cordite).</summary>
    public MatchStats GetMatchStats() => new MatchStats
    {
        Kills                = _playerKills,
        Losses               = _playerLosses,
        BuildingsConstructed = _buildingsConstructed,
        BuildingsDestroyed   = _buildingsDestroyed,
        UnitsProduced        = _unitsProduced,
        CorditeHarvested     = _economyManager?.GetPlayer(_localPlayerId)?.TotalCorditeIncome ?? 0
    };

    /// <summary>
    /// Restores full game state from a save. Tears down current state
    /// and rebuilds all systems from the save data.
    /// </summary>
    public void LoadFromSave(SaveGameData data)
    {
        GD.Print($"[GameSession] Restoring from save — map: {data.MapId}, tick: {data.CurrentTick}");

        // Tear down any existing match state
        CleanupMatch();

        // Reconstruct a MatchConfig from save data (including AI flags)
        var playerConfigs = new PlayerConfig[data.Players.Length];
        for (int i = 0; i < data.Players.Length; i++)
        {
            PlayerSaveData ps = data.Players[i];
            playerConfigs[i] = new PlayerConfig
            {
                PlayerId = ps.PlayerId,
                FactionId = ps.FactionId,
                IsAI = ps.IsAI,
                AIDifficulty = ps.AIDifficulty,
                PlayerName = string.IsNullOrEmpty(ps.PlayerName) ? $"Player {ps.PlayerId}" : ps.PlayerName
            };
        }

        var config = new MatchConfig
        {
            MapId = data.MapId,
            PlayerConfigs = playerConfigs,
            MatchSeed = data.MatchSeed,
            GameSpeed = data.GameSpeed,
            FogOfWar = data.FogOfWar,
            StartingCordite = data.StartingCordite,
            WinCondition = data.WinCondition == nameof(WinCondition.KillAllUnits)
                ? WinCondition.KillAllUnits
                : WinCondition.DestroyHQ
        };

        ActiveConfig = config;

        // Load registries and map
        LoadRegistries();
        ActiveMap = _mapLoader.GetMap(data.MapId);

        // Recreate managers
        _economyManager = new EconomyManager();
        AddChild(_economyManager);
        _economyManager.Initialize(_factionEconomyConfigs, _buildingRegistry);

        _techTreeManager = new TechTreeManager();
        AddChild(_techTreeManager);
        _techTreeManager.Initialize(_upgradeRegistry);

        _unitSpawner = new UnitSpawner(
            _assetRegistry,
            _unitDataRegistry,
            FactionColors,
            FactionBaseColors);
        AddChild(_unitSpawner);

        _harvesterSystem = new HarvesterSystem();
        AddChild(_harvesterSystem);
        _harvesterSystem.Initialize(_factionEconomyConfigs, _economyManager);

        _saveManager = new SaveManager();
        AddChild(_saveManager);

        // Create simulation systems for tick pipeline
        int mapWidth = ActiveMap?.Width ?? 256;
        int mapHeight = ActiveMap?.Height ?? 256;
        _terrainGrid = new TerrainGrid(mapWidth, mapHeight, FixedPoint.One);
        _spatialHash = new SpatialHash(mapWidth, mapHeight);
        _occupancyGrid = new OccupancyGrid(mapWidth, mapHeight);
        _collisionResolver = new CollisionResolver();
        _pathRequestManager = new PathRequestManager();
        _formationManager = new FormationManager();
        _combatResolver = new CombatResolver();

        _unitInteractionSystem = new UnitInteractionSystem(
            _spatialHash,
            _occupancyGrid,
            _collisionResolver,
            _pathRequestManager,
            _formationManager,
            _combatResolver,
            new DeterministicRng(data.MatchSeed),
            8);

        // Restore player economy state
        for (int i = 0; i < data.Players.Length; i++)
        {
            PlayerSaveData ps = data.Players[i];
            _economyManager.AddPlayer(ps.PlayerId, ps.FactionId);
            _techTreeManager.AddPlayer(ps.PlayerId, ps.FactionId);

            // Restore economy values via the PlayerEconomy
            PlayerEconomy? economy = _economyManager.GetPlayer(ps.PlayerId);
            if (economy is not null)
            {
                // Set cordite to the exact saved value (not an additive diff)
                economy.SetCordite(FixedPoint.FromRaw((int)ps.Cordite));

                // Set VC to the exact saved value
                economy.SetVC(FixedPoint.FromRaw((int)ps.VoltaicCharge));

                // Restore building counts for reactors/refineries/depots
                for (int r = 0; r < ps.ReactorCount; r++)
                    economy.RegisterReactor();
                for (int r = 0; r < ps.RefineryCount; r++)
                    economy.RegisterRefinery();

                // Restore depot supply capacity
                if (ps.DepotCount > 0)
                {
                    string depotBuildingId = $"{ps.FactionId}_supply_depot";
                    int supplyPerDepot = DefaultDepotSupplyCapacity; // sensible default
                    if (_buildingRegistry.HasBuilding(depotBuildingId))
                        supplyPerDepot = _buildingRegistry.GetBuilding(depotBuildingId).SupplyProvided;
                    for (int d = 0; d < ps.DepotCount; d++)
                        economy.RegisterDepot(supplyPerDepot);
                }
            }

            // Restore completed upgrades via tech tree
            PlayerTechState? tech = _techTreeManager.GetPlayerTech(ps.PlayerId);
            if (tech is not null)
            {
                for (int u = 0; u < ps.CompletedUpgrades.Length; u++)
                {
                    string upgradeId = ps.CompletedUpgrades[u];
                    if (_upgradeRegistry.HasUpgrade(upgradeId))
                    {
                        UpgradeData upgradeData = _upgradeRegistry.GetUpgrade(upgradeId);
                        tech.StartResearch(upgradeId, upgradeData);
                        // Force-complete by ticking past the target
                        tech.TickResearch(upgradeData.ResearchTime + FixedPoint.One);
                        _techTreeManager.ApplyUpgradeEffects(ps.PlayerId, upgradeData);
                    }
                }

                // Restore current research if any
                if (!string.IsNullOrEmpty(ps.CurrentResearch) && _upgradeRegistry.HasUpgrade(ps.CurrentResearch))
                {
                    UpgradeData currentUpgrade = _upgradeRegistry.GetUpgrade(ps.CurrentResearch);
                    tech.StartResearch(ps.CurrentResearch, currentUpgrade);
                    // Tick up to saved progress
                    FixedPoint progress = FixedPoint.FromRaw((int)ps.ResearchProgress);
                    if (progress > FixedPoint.Zero)
                        tech.TickResearch(progress);
                }

                // Restore building registrations for tech prerequisites
                for (int b = 0; b < ps.CompletedBuildings.Length; b++)
                {
                    tech.RegisterBuilding(ps.CompletedBuildings[b]);
                }
            }
        }

        // Restore cordite nodes
        for (int i = 0; i < data.CorditeNodes.Length; i++)
        {
            CorditeNodeSaveData cn = data.CorditeNodes[i];
            FixedVector2 nodePos = new FixedVector2(
                FixedPoint.FromInt(cn.PositionX),
                FixedPoint.FromInt(cn.PositionY));
            _harvesterSystem.RegisterCorditeNode(cn.NodeId, nodePos, cn.RemainingCordite);
        }

        // Restore buildings — deferred until after SetupGameplaySystems creates _buildingPlacer
        // (see the "Restore buildings (deferred)" block below)
        var pendingBuildings = data.Buildings;

        // Restore units
        for (int i = 0; i < data.Units.Length; i++)
        {
            UnitSaveData us = data.Units[i];
            if (!us.IsAlive) continue;

            // Find which player config this unit belongs to
            string factionId = string.Empty;
            for (int p = 0; p < data.Players.Length; p++)
            {
                if (data.Players[p].PlayerId == us.PlayerId)
                {
                    factionId = data.Players[p].FactionId;
                    break;
                }
            }

            FixedVector2 unitPos = new FixedVector2(
                FixedPoint.FromRaw((int)us.PositionX),
                FixedPoint.FromRaw((int)us.PositionY));
            FixedPoint facing = FixedPoint.FromRaw((int)us.Facing);
            FixedPoint health = FixedPoint.FromRaw((int)us.Health);

            _unitSpawner.SpawnUnitWithId(us.UnitId, us.UnitTypeId, factionId, us.PlayerId, unitPos, facing, health);
        }

        // Restore harvesters
        for (int i = 0; i < data.Harvesters.Length; i++)
        {
            HarvesterSaveData hs = data.Harvesters[i];
            string hvFaction = string.Empty;
            for (int p = 0; p < data.Players.Length; p++)
            {
                if (data.Players[p].PlayerId == hs.PlayerId)
                {
                    hvFaction = data.Players[p].FactionId;
                    break;
                }
            }

            // Find unit position from units array
            FixedVector2 hvPos = FixedVector2.Zero;
            for (int u = 0; u < data.Units.Length; u++)
            {
                if (data.Units[u].UnitId == hs.UnitId)
                {
                    hvPos = new FixedVector2(
                        FixedPoint.FromRaw((int)data.Units[u].PositionX),
                        FixedPoint.FromRaw((int)data.Units[u].PositionY));
                    break;
                }
            }

            _harvesterSystem.RegisterHarvester(hs.UnitId, hs.PlayerId, hvFaction, hvPos);
        }

        // Render terrain mesh, water, and map props
        SetupTerrainRendering();

        // Set up camera
        SetupCamera();

        // Set up gameplay systems (Selection, Commands, Building, HUD, AI)
        SetupGameplaySystems(config);

        // ── Restore buildings (deferred — requires _buildingPlacer from SetupGameplaySystems) ──
        for (int i = 0; i < pendingBuildings.Length; i++)
        {
            BuildingSaveData bs = pendingBuildings[i];

            // Detect command-center / HQ building type
            bool isHQ = bs.BuildingTypeId.EndsWith("_command_center", StringComparison.OrdinalIgnoreCase);

            if (isHQ)
            {
                // Restore HQ as an external building (mirrors PlaceStartingBuildings logic).
                // We create the node ourselves so we can track it in _playerHQNodes.
                if (_buildingRegistry.HasBuilding(bs.BuildingTypeId))
                {
                    BuildingData hqData = _buildingRegistry.GetBuilding(bs.BuildingTypeId);
                    BuildingModelEntry? hqModelEntry = _buildingManifest.HasEntry(bs.BuildingTypeId)
                        ? _buildingManifest.GetEntry(bs.BuildingTypeId)
                        : null;

                    var hqNode = new BuildingInstance();
                    hqNode.Initialize(bs.BuildingId, bs.BuildingTypeId, hqData, bs.PlayerId,
                        bs.PositionX, bs.PositionY, hqModelEntry);
                    hqNode.RestoreState(
                        FixedPoint.FromRaw((int)bs.Health),
                        bs.IsConstructed,
                        FixedPoint.FromRaw((int)bs.ConstructionProgress));

                    if (_terrainRenderer is not null)
                    {
                        float terrainY = _terrainRenderer.GetElevationAtWorld(bs.PositionX, bs.PositionY);
                        hqNode.Position = new Vector3(bs.PositionX, terrainY, bs.PositionY);
                    }
                    AddChild(hqNode);

                    // Register for win-condition tracking
                    _playerHQNodes[bs.PlayerId] = hqNode;
                    _playersWithInitialHQ.Add(bs.PlayerId);

                    // Make it visible to BuildingPlacer queries (minimap, objectives, simulation)
                    _buildingPlacer?.RegisterExternalBuilding(hqNode);
                    _garrisonSystem.RegisterBuilding(hqNode);

                    // Occupy footprint so units path around it
                    _occupancyGrid?.OccupyFootprint(
                        bs.PositionX, bs.PositionY,
                        hqData.FootprintWidth, hqData.FootprintHeight,
                        OccupancyType.Building, bs.BuildingId, bs.PlayerId);
                }
            }
            else
            {
                _buildingPlacer?.RestoreBuilding(
                    bs.BuildingId,
                    bs.BuildingTypeId,
                    bs.PlayerId,
                    bs.PositionX,
                    bs.PositionY,
                    FixedPoint.FromRaw((int)bs.Health),
                    bs.IsConstructed,
                    FixedPoint.FromRaw((int)bs.ConstructionProgress));
            }

            // Re-register refineries (including HQ refinery) with harvester system
            if (bs.BuildingTypeId.Contains("refinery", StringComparison.OrdinalIgnoreCase) || isHQ)
            {
                FixedVector2 bldgPos = new FixedVector2(
                    FixedPoint.FromInt(bs.PositionX),
                    FixedPoint.FromInt(bs.PositionY));
                _harvesterSystem.RegisterRefinery(bs.BuildingId, bs.PlayerId, bldgPos);
            }
        }

        // Wire minimap to live terrain/camera data
        SetupMinimapData();

        // Spawn visual cordite node markers
        SpawnCorditeNodeMarkers();

        // Wire up GameManager and set tick
        _gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (_gameManager is not null)
        {
            _gameManager.EconomyManager = _economyManager;
            _gameManager.HarvesterSystem = _harvesterSystem;
            _gameManager.TechTreeManager = _techTreeManager;
            _gameManager.MapLoader = _mapLoader;
            _gameManager.CommandBuffer = new CommandBuffer();
            _gameManager.OnSimulationTick += HandleSimulationTick;

            // Initialize RNG with saved seed and restore full state
            _gameManager.StartMatch(data.MatchSeed);
            _gameManager.Rng?.SetState(data.RngState0, data.RngState1, data.RngState2, data.RngState3);
        }

        // Also restore the combat RNG in UnitInteractionSystem to match saved state
        _unitInteractionSystem?.CombatRng.SetState(data.RngState0, data.RngState1, data.RngState2, data.RngState3);

        CurrentMatchState = MatchState.Playing;

        GD.Print($"[GameSession] Restored match at tick {data.CurrentTick}.");
    }

    /// <summary>
    /// Collects the complete match state from all managers for save/checksum.
    /// </summary>
    public SaveGameData GetMatchState()
    {
        ulong currentTick = _gameManager?.CurrentTick ?? 0;
        ulong matchSeed = ActiveConfig?.MatchSeed ?? 0;

        // Collect full RNG state (4 ulongs for xoshiro256**)
        ulong rng0 = 0, rng1 = 0, rng2 = 0, rng3 = 0;
        if (_gameManager?.Rng is not null)
        {
            (rng0, rng1, rng2, rng3) = _gameManager.Rng.GetState();
        }

        // Collect player data
        var players = new List<PlayerSaveData>();
        if (ActiveConfig is not null)
        {
            for (int i = 0; i < ActiveConfig.PlayerConfigs.Length; i++)
            {
                PlayerConfig pc = ActiveConfig.PlayerConfigs[i];
                PlayerEconomy? economy = _economyManager?.GetPlayer(pc.PlayerId);
                PlayerTechState? tech = _techTreeManager?.GetPlayerTech(pc.PlayerId);

                string[] completedUpgrades = [];
                string[] completedBuildings = [];
                string? currentResearch = null;
                long researchProgress = 0;

                if (tech is not null)
                {
                    currentResearch = tech.CurrentResearch;
                    researchProgress = tech.ResearchProgress.Raw;

                    var upgradesList = tech.GetCompletedUpgrades();
                    completedUpgrades = new string[upgradesList.Count];
                    for (int u = 0; u < upgradesList.Count; u++)
                        completedUpgrades[u] = upgradesList[u];

                    var buildingsList = tech.GetRegisteredBuildings();
                    completedBuildings = new string[buildingsList.Count];
                    for (int b = 0; b < buildingsList.Count; b++)
                        completedBuildings[b] = buildingsList[b];
                }

                players.Add(new PlayerSaveData
                {
                    PlayerId = pc.PlayerId,
                    FactionId = pc.FactionId,
                    PlayerName = pc.PlayerName,
                    IsAI = pc.IsAI,
                    AIDifficulty = pc.AIDifficulty,
                    Cordite = economy?.Cordite.Raw ?? 0,
                    VoltaicCharge = economy?.VoltaicCharge.Raw ?? 0,
                    CurrentSupply = economy?.CurrentSupply ?? 0,
                    MaxSupply = economy?.MaxSupply ?? 0,
                    ReactorCount = economy?.ReactorCount ?? 0,
                    RefineryCount = economy?.RefineryCount ?? 0,
                    DepotCount = economy?.DepotCount ?? 0,
                    CompletedUpgrades = completedUpgrades,
                    CurrentResearch = currentResearch,
                    ResearchProgress = researchProgress,
                    CompletedBuildings = completedBuildings
                });
            }
        }

        // Collect cordite nodes from harvester system
        var corditeNodes = new List<CorditeNodeSaveData>();
        if (_harvesterSystem is not null)
        {
            var allNodes = _harvesterSystem.GetAllCorditeNodes();
            for (int i = 0; i < allNodes.Count; i++)
            {
                CorditeNode node = allNodes[i];
                corditeNodes.Add(new CorditeNodeSaveData
                {
                    NodeId = node.NodeId,
                    PositionX = node.Position.X.ToInt(),
                    PositionY = node.Position.Y.ToInt(),
                    RemainingCordite = node.RemainingCordite
                });
            }
        }

        // Collect all units from UnitSpawner
        var units = new List<UnitSaveData>();
        if (_unitSpawner is not null)
        {
            var allUnits = _unitSpawner.GetAllUnits();
            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                units.Add(new UnitSaveData
                {
                    UnitId = unit.UnitId,
                    UnitTypeId = unit.UnitTypeId,
                    PlayerId = unit.PlayerId,
                    PositionX = unit.SimPosition.X.Raw,
                    PositionY = unit.SimPosition.Y.Raw,
                    Facing = unit.SimFacing.Raw,
                    Health = unit.Health.Raw,
                    IsAlive = unit.IsAlive
                });
            }
        }

        // Collect all buildings from BuildingPlacer
        var buildings = new List<BuildingSaveData>();
        if (_buildingPlacer is not null)
        {
            var allBuildings = _buildingPlacer.GetAllBuildings();
            for (int i = 0; i < allBuildings.Count; i++)
            {
                var bldg = allBuildings[i];
                buildings.Add(new BuildingSaveData
                {
                    BuildingId = bldg.BuildingId,
                    BuildingTypeId = bldg.BuildingTypeId,
                    PlayerId = bldg.PlayerId,
                    PositionX = bldg.GridX,
                    PositionY = bldg.GridY,
                    Health = bldg.Health.Raw,
                    IsConstructed = bldg.IsConstructed,
                    ConstructionProgress = bldg.ConstructionProgress.Raw
                });
            }
        }

        // Collect all harvesters
        var harvesters = new List<HarvesterSaveData>();
        if (_harvesterSystem is not null)
        {
            var allHarvesters = _harvesterSystem.GetAllHarvesters();
            for (int i = 0; i < allHarvesters.Count; i++)
            {
                var hv = allHarvesters[i];
                harvesters.Add(new HarvesterSaveData
                {
                    UnitId = hv.UnitId,
                    PlayerId = hv.PlayerId,
                    State = hv.State.ToString(),
                    CorditeCarrying = hv.CorditeCarrying,
                    AssignedNodeId = hv.AssignedNodeId,
                    AssignedRefineryId = hv.AssignedRefineryId
                });
            }
        }

        return new SaveGameData
        {
            Version = "0.1.0",
            ProtocolVersion = 1,
            SaveTimestamp = DateTime.UtcNow.ToString("o"),
            MapId = ActiveConfig?.MapId ?? string.Empty,
            MatchSeed = matchSeed,
            CurrentTick = currentTick,
            GameSpeed = ActiveConfig?.GameSpeed ?? 1,
            FogOfWar = ActiveConfig?.FogOfWar ?? true,
            StartingCordite = ActiveConfig?.StartingCordite ?? 5000,
            WinCondition = _winCondition.ToString(),
            Players = players.ToArray(),
            Units = units.ToArray(),
            Buildings = buildings.ToArray(),
            Harvesters = harvesters.ToArray(),
            CorditeNodes = corditeNodes.ToArray(),
            RngState0 = rng0,
            RngState1 = rng1,
            RngState2 = rng2,
            RngState3 = rng3,
            CommandHistory = []
        };
    }

    // ═════════════════════════════════════════════════════════════════
    // GAMEPLAY SYSTEMS SETUP
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets up SelectionManager, CommandInput, BuildingPlacer, GameHUD,
    /// and SkirmishAI controllers for each AI player.
    /// </summary>
    private void SetupGameplaySystems(MatchConfig config)
    {
        if (_camera is null || _unitSpawner is null || _economyManager is null) return;

        // Find the local (human) player ID.
        // If the MatchConfig already specifies the local player (network multiplayer),
        // use it directly; otherwise auto-detect as the first non-AI player.
        int localPlayerId = config.LocalPlayerId >= 0 ? config.LocalPlayerId : 0;
        if (config.LocalPlayerId < 0)
        {
            for (int i = 0; i < config.PlayerConfigs.Length; i++)
            {
                if (!config.PlayerConfigs[i].IsAI)
                {
                    localPlayerId = config.PlayerConfigs[i].PlayerId;
                    break;
                }
            }
        }
        _localPlayerId = localPlayerId;

        // Load building manifest
        _buildingManifest.Load("res://data/building_manifest.json");

        // a. SelectionManager
        _selectionManager = new SelectionManager();
        _selectionManager.Name = "SelectionManager";
        _selectionManager.Initialize(localPlayerId, _unitSpawner, _camera);
        AddChild(_selectionManager);

        // b. CommandInput — needs CommandBuffer from GameManager
        var commandBuffer = new CommandBuffer();
        // Share this CommandBuffer with GameManager so HandleSimulationTick can consume commands
        if (_gameManager != null)
            _gameManager.CommandBuffer = commandBuffer;
        _commandInput = new CommandInput();
        _commandInput.Name = "CommandInput";
        _commandInput.Initialize(
            localPlayerId,
            _selectionManager,
            commandBuffer,
            _unitSpawner,
            _camera);
        // Wire superweapon targeting: FIRE button → targeting mode → left-click → activate
        _commandInput.SetSuperweaponActivateCallback(ActivateSuperweapon);
        AddChild(_commandInput);

        // c. BuildingPlacer
        var occupancyGrid = new OccupancyGrid(
            ActiveMap?.Width ?? 256,
            ActiveMap?.Height ?? 256);

        _buildingPlacer = new BuildingPlacer();
        _buildingPlacer.Name = "BuildingPlacer";
        _buildingPlacer.Initialize(
            localPlayerId,
            occupancyGrid,
            _economyManager,
            _buildingRegistry,
            _buildingManifest,
            _camera,
            _terrainGrid,
            _terrainRenderer);
        AddChild(_buildingPlacer);

        // Register HQ positions for build radius validation
        if (ActiveMap is not null)
        {
            for (int i = 0; i < config.PlayerConfigs.Length; i++)
            {
                PlayerConfig pc = config.PlayerConfigs[i];
                for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
                {
                    if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId ||
                        (s == i && ActiveMap.StartingPositions.Length > i))
                    {
                        var sp = ActiveMap.StartingPositions[s < ActiveMap.StartingPositions.Length ? s : 0];
                        FixedVector2 hqPos = new FixedVector2(
                            FixedPoint.FromInt(sp.X),
                            FixedPoint.FromInt(sp.Y));
                        _buildingPlacer.RegisterHQPosition(pc.PlayerId, hqPos);
                        break;
                    }
                }
            }
        }

        // Register pre-placed HQ buildings so they appear in GetAllBuildings()
        // queries — used by the minimap, mission-objective context, and simulation tick.
        // PlaceStartingBuildings runs before SetupGameplaySystems, so _playerHQNodes is
        // already populated here.
        foreach (var kvp in _playerHQNodes)
        {
            if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
                _buildingPlacer.RegisterExternalBuilding(kvp.Value);
        }

        // d. GameHUD
        _gameHUD = new GameHUD();
        _gameHUD.Initialize(
            localPlayerId,
            _economyManager,
            _selectionManager,
            _buildingPlacer,
            _unitSpawner,
            _unitDataRegistry,
            _buildingRegistry,
            config.Campaign,
            config.PlayerConfigs.Length > 0 ? config.PlayerConfigs[0].PlayerName : "Commander",
            default,
            _superweaponSystem);
        AddChild(_gameHUD);
        _gameHUD.SetCommandInput(_commandInput);

        // e. Wire minimap click-to-move to camera
        EventBus.Instance?.Connect(EventBus.SignalName.MinimapClick,
            Callable.From<Vector3>((pos) => _camera?.SetFocusPoint(pos)));

        // e1. Wire superweapon FIRE button → targeting mode in CommandInput
        var capturedCommandInput = _commandInput;
        EventBus.Instance?.Connect(EventBus.SignalName.SuperweaponActivateRequested,
            Callable.From<int, string>((pid, weaponId) =>
            {
                if (pid == localPlayerId)
                    capturedCommandInput?.StartSuperweaponTargeting(weaponId);
            }));

        // e2. Wire building-destroyed to keep BuildingPlacer and HQ tracking in sync
        EventBus.Instance?.Connect(EventBus.SignalName.BuildingDestroyed,
            Callable.From<Node>(OnBuildingDestroyed));

        // e2b. Track buildings constructed and unit production for post-match stats
        EventBus.Instance?.Connect(EventBus.SignalName.BuildingCompleted,
            Callable.From<Node>(b =>
            {
                _buildingsConstructed++;
                // Register the completed building with the garrison system
                if (b is BuildingInstance bld)
                    _garrisonSystem.RegisterBuilding(bld);
            }));
        EventBus.Instance?.Connect(EventBus.SignalName.UnitSpawned,
            Callable.From<Node>(u =>
            {
                if (u is UnitNode3D node && node.PlayerId == localPlayerId)
                    _unitsProduced++;
            }));

        // e3. Wire command events to ReplayManager so human commands are recorded
        if (_replayManager is not null)
        {
            var capturedReplay = _replayManager;
            EventBus.Instance?.Connect(EventBus.SignalName.MoveCommandIssued,
                Callable.From<Vector3>((target) =>
                {
                    ulong tick = _gameManager?.CurrentTick ?? 0;
                    var ids = _selectionManager?.GetSelectedUnitIds();
                    capturedReplay.RecordCommand(tick, localPlayerId, "Move",
                        target.X, target.Z,
                        ids?.ToArray() ?? []);
                }));
        }

        // f. Skirmish AI for each AI player
        SetupAIPlayers(config);

        GD.Print("[GameSession] Gameplay systems initialized.");
    }

    /// <summary>
    /// Creates SkirmishAI controller for each AI player.
    /// </summary>
    private void SetupAIPlayers(MatchConfig config)
    {
        if (ActiveMap is null || _economyManager is null || _unitSpawner is null ||
            _buildingPlacer is null || _techTreeManager is null) return;

        // Compute water cell percentage once for all AI players
        int waterCellPercent = ComputeWaterCellPercent(_terrainGrid);

        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];
            if (!pc.IsAI) continue;

            // Find starting position for this AI.
            // Map StartingPosition.PlayerId uses 0-based indices while PlayerConfig.PlayerId
            // is 1-based, so we match against pc.PlayerId - 1.
            FixedVector2 basePos = FixedVector2.Zero;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId - 1)
                {
                    basePos = new FixedVector2(
                        FixedPoint.FromInt(ActiveMap.StartingPositions[s].X),
                        FixedPoint.FromInt(ActiveMap.StartingPositions[s].Y));
                    break;
                }
            }

            if (basePos.X == FixedPoint.Zero && basePos.Y == FixedPoint.Zero && i < ActiveMap.StartingPositions.Length)
            {
                var sp = ActiveMap.StartingPositions[i];
                basePos = new FixedVector2(
                    FixedPoint.FromInt(sp.X),
                    FixedPoint.FromInt(sp.Y));
            }

            AIDifficulty difficulty = pc.AIDifficulty switch
            {
                0 => AIDifficulty.Easy,
                1 => AIDifficulty.Medium,
                2 => AIDifficulty.Hard,
                _ => AIDifficulty.Medium
            };

            var ai = new SkirmishAI();
            ai.Initialize(
                pc.PlayerId,
                pc.FactionId,
                difficulty,
                basePos,
                _economyManager,
                _unitSpawner,
                _buildingPlacer,
                _techTreeManager,
                _unitDataRegistry,
                _buildingRegistry,
                waterCellPercent);
            AddChild(ai);
            _skirmishAIs.Add(ai);

            GD.Print($"[GameSession] Created AI player {pc.PlayerId} ({pc.FactionId}, {difficulty}).");
        }
    }

    /// <summary>
    /// Returns the percentage of TerrainGrid cells that are Water or DeepWater.
    /// Returns 0 if the grid is null.
    /// </summary>
    private static int ComputeWaterCellPercent(TerrainGrid? grid)
    {
        if (grid is null) return 0;

        int total = grid.Width * grid.Height;
        if (total == 0) return 0;

        int waterCount = 0;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            TerrainType t = grid.Cells[i].Type;
            if (t == TerrainType.Water || t == TerrainType.DeepWater)
                waterCount++;
        }

        return waterCount * 100 / total;
    }

    // ═════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates the <see cref="TerrainGrid"/> with terrain types derived from
    /// <see cref="MapData"/> terrain features.  Currently handles:
    /// <list type="bullet">
    ///   <item><c>"water_body"</c> — fills a rectangular or polygonal area with
    ///         <see cref="TerrainType.DeepWater"/> cells so that naval units can
    ///         navigate the water.  If the feature has exactly 2 points it is
    ///         treated as an axis-aligned rectangle (top-left / bottom-right).
    ///         Three or more points define a convex/concave polygon filled via
    ///         a scanline algorithm.</item>
    /// </list>
    /// All other terrain defaults to <see cref="TerrainType.Grass"/> (the grid
    /// is already zero-initialised to Grass).
    /// </summary>
    private static void BuildTerrainGridFromMapData(MapData mapData, TerrainGrid grid)
    {
        if (mapData.TerrainFeatures == null || mapData.TerrainFeatures.Length == 0)
            return;

        for (int f = 0; f < mapData.TerrainFeatures.Length; f++)
        {
            TerrainFeature feature = mapData.TerrainFeatures[f];
            if (feature.Type != "water_body" || feature.Points == null || feature.Points.Length < 2)
                continue;

            if (feature.Points.Length == 2)
            {
                // Rectangle: Points[0] = top-left, Points[1] = bottom-right
                int[] p0 = feature.Points[0];
                int[] p1 = feature.Points[1];
                if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

                int x0 = Math.Min(p0[0], p1[0]);
                int y0 = Math.Min(p0[1], p1[1]);
                int x1 = Math.Max(p0[0], p1[0]);
                int y1 = Math.Max(p0[1], p1[1]);

                x0 = Math.Max(0, x0);
                y0 = Math.Max(0, y0);
                x1 = Math.Min(grid.Width - 1, x1);
                y1 = Math.Min(grid.Height - 1, y1);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        grid.Cells[y * grid.Width + x].Type = TerrainType.DeepWater;
                    }
                }
            }
            else
            {
                // Polygon scanline fill
                FillWaterBodyPolygon(grid, feature.Points);
            }
        }

        GD.Print($"[GameSession] TerrainGrid populated from {mapData.TerrainFeatures.Length} terrain features.");
    }

    /// <summary>
    /// Fills a polygon defined by <paramref name="points"/> with
    /// <see cref="TerrainType.DeepWater"/> using a scanline algorithm.
    /// </summary>
    private static void FillWaterBodyPolygon(TerrainGrid grid, int[][] points)
    {
        // Compute bounding box
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null || points[i].Length < 2) continue;
            if (points[i][1] < minY) minY = points[i][1];
            if (points[i][1] > maxY) maxY = points[i][1];
        }

        minY = Math.Max(0, minY);
        maxY = Math.Min(grid.Height - 1, maxY);

        int n = points.Length;

        for (int scanY = minY; scanY <= maxY; scanY++)
        {
            // Find all X intersections of the polygon edges at this scanline
            var intersections = new System.Collections.Generic.List<int>(8);

            for (int i = 0; i < n; i++)
            {
                int[] pa = points[i];
                int[] pb = points[(i + 1) % n];
                if (pa == null || pa.Length < 2 || pb == null || pb.Length < 2) continue;

                int ay = pa[1];
                int by = pb[1];

                if (ay == by) continue; // horizontal edge — skip

                // Check if the scanline crosses this edge
                if ((scanY >= ay && scanY < by) || (scanY >= by && scanY < ay))
                {
                    // Compute X intersection using rounding to avoid truncation gaps.
                    // Using integer round-half-up: add half the denominator before dividing.
                    int ax = pa[0];
                    int bx = pb[0];
                    int numerator   = (scanY - ay) * (bx - ax);
                    int denominator = by - ay;
                    int xIntersect  = ax + (numerator + denominator / 2) / denominator;
                    intersections.Add(xIntersect);
                }
            }

            if (intersections.Count < 2) continue;

            // Sort intersections and fill between pairs
            intersections.Sort();

            for (int k = 0; k + 1 < intersections.Count; k += 2)
            {
                int xStart = Math.Max(0, intersections[k]);
                int xEnd   = Math.Min(grid.Width - 1, intersections[k + 1]);

                for (int x = xStart; x <= xEnd; x++)
                {
                    grid.Cells[scanY * grid.Width + x].Type = TerrainType.DeepWater;
                }
            }
        }
    }

    /// <summary>
    /// Loads all data registries from res://data/ paths.
    /// </summary>
    private void LoadRegistries()
    {
        _mapLoader.LoadAllMaps("res://data/maps");
        _unitDataRegistry.Load("res://data/units");
        _buildingRegistry.Load("res://data/buildings");
        _upgradeRegistry.Load("res://data/upgrades");
        _assetRegistry.Load("res://data/asset_manifest.json");
    }

    /// <summary>
    /// Places HQ buildings at starting positions for each player.
    /// </summary>
    private void PlaceStartingBuildings(MatchConfig config)
    {
        if (ActiveMap is null) return;

        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];

            // Find matching starting position.
            // Map StartingPosition.PlayerId uses 0-based indices while PlayerConfig.PlayerId
            // is 1-based, so we match against pc.PlayerId - 1.
            StartingPosition? startPos = null;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId - 1)
                {
                    startPos = ActiveMap.StartingPositions[s];
                    break;
                }
            }

            if (startPos is null)
            {
                // Fallback: use index-based assignment
                if (i < ActiveMap.StartingPositions.Length)
                {
                    startPos = ActiveMap.StartingPositions[i];
                }
                else
                {
                    GD.PushWarning($"[GameSession] No starting position for player {pc.PlayerId}.");
                    continue;
                }
            }

            // Register the HQ building with economy and tech
            _techTreeManager?.GetPlayerTech(pc.PlayerId)?.RegisterBuilding("hq");

            // Register a refinery at HQ position so harvesters have a delivery point
            FixedVector2 hqPos = new FixedVector2(
                FixedPoint.FromInt(startPos.X),
                FixedPoint.FromInt(startPos.Y));
            int refineryId = pc.PlayerId * 1000; // Unique ID per player's starting refinery
            _harvesterSystem?.RegisterRefinery(refineryId, pc.PlayerId, hqPos);

            // Spawn a visual HQ building node so the base is visible on screen
            string hqBuildingId = $"{pc.FactionId}_command_center";
            if (_buildingRegistry is not null && _buildingRegistry.HasBuilding(hqBuildingId))
            {
                BuildingData hqData = _buildingRegistry.GetBuilding(hqBuildingId);
                int buildingId = -(pc.PlayerId * 100); // Negative IDs for pre-placed HQ buildings

                BuildingModelEntry? hqModelEntry = _buildingManifest.HasEntry(hqBuildingId)
                    ? _buildingManifest.GetEntry(hqBuildingId)
                    : null;

                var hqNode = new BuildingInstance();
                hqNode.Initialize(buildingId, hqBuildingId, hqData, pc.PlayerId,
                    startPos.X, startPos.Y, hqModelEntry);
                // Mark fully constructed so it doesn't animate in during the game start
                hqNode.RestoreState(hqData.MaxHealth, true, hqData.BuildTime);
                // Snap to terrain surface so the HQ sits on the ground mesh
                if (_terrainRenderer is not null)
                {
                    float terrainY = _terrainRenderer.GetElevationAtWorld(startPos.X, startPos.Y);
                    hqNode.Position = new Vector3(startPos.X, terrainY, startPos.Y);
                }
                AddChild(hqNode);

                // Track for win-condition checking
                _playerHQNodes[pc.PlayerId] = hqNode;
                _playersWithInitialHQ.Add(pc.PlayerId);

                // Register with garrison system if HQ supports garrisoning
                _garrisonSystem.RegisterBuilding(hqNode);

                // Occupy the footprint in the grid so units path around the HQ
                _occupancyGrid?.OccupyFootprint(
                    startPos.X, startPos.Y,
                    hqData.FootprintWidth, hqData.FootprintHeight,
                    OccupancyType.Building, buildingId, pc.PlayerId);
            }

            GD.Print($"[GameSession] Placed HQ for player {pc.PlayerId} at ({startPos.X}, {startPos.Y}).");
        }
    }

    /// <summary>
    /// Spawns one harvester per player at their starting position.
    /// </summary>
    private void SpawnStartingUnits(MatchConfig config)
    {
        if (ActiveMap is null || _unitSpawner is null || _harvesterSystem is null) return;

        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];

            // Find starting position.
            // Map StartingPosition.PlayerId uses 0-based indices while PlayerConfig.PlayerId
            // is 1-based, so we match against pc.PlayerId - 1.
            StartingPosition? startPos = null;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId - 1)
                {
                    startPos = ActiveMap.StartingPositions[s];
                    break;
                }
            }

            if (startPos is null && i < ActiveMap.StartingPositions.Length)
            {
                startPos = ActiveMap.StartingPositions[i];
            }

            if (startPos is null) continue;

            // Offset harvester slightly from HQ position
            FixedVector2 harvesterPos = new FixedVector2(
                FixedPoint.FromInt(startPos.X + 3),
                FixedPoint.FromInt(startPos.Y + 3));

            // Find a harvester unit type for this faction
            string harvesterTypeId = FindHarvesterTypeId(pc.FactionId);
            if (string.IsNullOrEmpty(harvesterTypeId))
            {
                GD.PushWarning($"[GameSession] No harvester unit type found for faction '{pc.FactionId}'.");
                continue;
            }

            UnitNode3D? unit = _unitSpawner.SpawnUnit(
                harvesterTypeId,
                pc.FactionId,
                pc.PlayerId,
                harvesterPos,
                startPos.Facing);

            if (unit is not null)
            {
                _harvesterSystem.RegisterHarvester(
                    unit.UnitId,
                    pc.PlayerId,
                    pc.FactionId,
                    harvesterPos);

                GD.Print($"[GameSession] Spawned harvester for player {pc.PlayerId}.");
            }
        }
    }

    /// <summary>
    /// Registers all Cordite resource nodes from map data.
    /// </summary>
    private void RegisterCorditeNodes()
    {
        if (ActiveMap is null || _harvesterSystem is null) return;

        for (int i = 0; i < ActiveMap.CorditeNodes.Length; i++)
        {
            CorditeNodeData node = ActiveMap.CorditeNodes[i];
            FixedVector2 pos = new FixedVector2(
                FixedPoint.FromFloat(node.X),
                FixedPoint.FromFloat(node.Y));
            _harvesterSystem.RegisterCorditeNode(i, pos, node.Amount);
        }

        GD.Print($"[GameSession] Registered {ActiveMap.CorditeNodes.Length} Cordite nodes.");
    }

    /// <summary>
    /// Creates and configures the RTS camera.
    /// </summary>
    private void SetupCamera()
    {
        _camera = new RTSCamera();
        _camera.Name = "RTSCamera";
        AddChild(_camera);

        // Focus camera on the human player's starting position (first non-AI slot).
        // Map starting positions use 0-based PlayerId; player configs use 1-based PlayerId.
        // We use the index-based fallback: config slot 0 → map starting position index 0.
        if (ActiveMap is not null && ActiveMap.StartingPositions.Length > 0 && ActiveConfig is not null)
        {
            // Find first human player slot
            int humanSlotIndex = 0;
            for (int i = 0; i < ActiveConfig.PlayerConfigs.Length; i++)
            {
                if (!ActiveConfig.PlayerConfigs[i].IsAI)
                {
                    humanSlotIndex = i;
                    break;
                }
            }
            // Use the map starting position at the same index (0-based), clamped to available slots
            int spIndex = Math.Min(humanSlotIndex, ActiveMap.StartingPositions.Length - 1);
            StartingPosition sp = ActiveMap.StartingPositions[spIndex];
            _camera.SetFocusPoint(new Vector3(sp.X, 0f, sp.Y));
        }
    }

    /// <summary>
    /// Generates the terrain mesh, water planes, and map props from <see cref="ActiveMap"/>.
    /// Must be called after <see cref="ActiveMap"/> and <see cref="_occupancyGrid"/> are
    /// initialised.
    /// </summary>
    private void SetupTerrainRendering()
    {
        if (ActiveMap is null) return;

        QualityTier tier = QualityManager.Instance?.CurrentTier ?? QualityTier.Medium;

        // Terrain mesh
        _terrainRenderer = new TerrainRenderer();
        _terrainRenderer.Name = "TerrainRenderer";
        AddChild(_terrainRenderer);
        _terrainRenderer.Generate(ActiveMap, tier);

        // Animated (or static, on Potato/Low) water planes for rivers / water bodies
        _waterRenderer = new WaterRenderer();
        _waterRenderer.Name = "WaterRenderer";
        AddChild(_waterRenderer);
        _waterRenderer.Generate(ActiveMap, _terrainRenderer, tier);

        // Decorative props and structures (trees, rocks, ruins, etc.)
        bool hasProps = (ActiveMap.Props.Length > 0) || (ActiveMap.Structures.Length > 0);
        if (_occupancyGrid is not null && hasProps)
        {
            var terrainManifest = new TerrainManifest();
            try
            {
                terrainManifest.Load("res://data/terrain_manifest.json");
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[GameSession] Could not load terrain_manifest.json — props will be skipped. ({ex.Message})");
                return;
            }

            _propPlacer = new PropPlacer();
            _propPlacer.Name = "PropPlacer";
            AddChild(_propPlacer);
            _propPlacer.PlaceAll(ActiveMap, terrainManifest, _terrainRenderer, _occupancyGrid, tier);
        }
    }

    /// <summary>
    /// Wires the minimap panel to live terrain and game data.
    /// Called after SetupGameplaySystems so the HUD and camera are ready.
    /// </summary>
    private void SetupMinimapData()
    {
        if (_gameHUD is null || _terrainGrid is null || _camera is null ||
            _unitSpawner is null || _buildingPlacer is null || ActiveMap is null) return;

        _gameHUD.SetupMinimapData(
            _terrainGrid,
            ActiveMap.Width,
            ActiveMap.Height,
            _unitSpawner,
            _buildingPlacer,
            _camera);

        GD.Print("[GameSession] Minimap wired to live terrain data.");
    }

    /// <summary>
    /// Places a small glowing sphere marker at each Cordite node position
    /// so players can see resources on the map.
    /// </summary>
    private void SpawnCorditeNodeMarkers()
    {
        if (ActiveMap is null) return;

        var parentNode = new Node3D();
        parentNode.Name = "CorditeMarkers";
        AddChild(parentNode);

        for (int i = 0; i < ActiveMap.CorditeNodes.Length; i++)
        {
            CorditeNodeData cn = ActiveMap.CorditeNodes[i];

            var marker = new MeshInstance3D();
            marker.Name = $"CorditeNode_{i}";
            marker.GlobalPosition = new Vector3(cn.X, 0.3f, cn.Y);

            var sphere = new SphereMesh();
            sphere.Radius = 0.6f;
            sphere.Height = 1.2f;
            marker.Mesh = sphere;

            // Bright golden-yellow material to stand out as a resource node
            var markerMat = new StandardMaterial3D();
            markerMat.AlbedoColor = new Color(0.9f, 0.85f, 0.1f);
            markerMat.EmissionEnabled = true;
            markerMat.Emission = new Color(0.6f, 0.55f, 0.05f);
            markerMat.EmissionEnergyMultiplier = 1.5f;
            marker.MaterialOverride = markerMat;

            parentNode.AddChild(marker);
        }

        GD.Print($"[GameSession] Spawned {ActiveMap.CorditeNodes.Length} Cordite node markers.");
    }

    /// <summary>
    /// Initializes multiplayer networking for the match.
    /// </summary>
    private void SetupMultiplayer(MatchConfig config)
    {
        if (_gameManager is null) return;

        _networkTransport = GetNodeOrNull<NetworkTransport>("/root/NetworkTransport");
        if (_networkTransport is null)
        {
            _networkTransport = new NetworkTransport();
            _networkTransport.Name = "NetworkTransport";
            AddChild(_networkTransport);
        }

        _lockstepManager = new LockstepManager();
        _lockstepManager.Name = "LockstepManager";
        AddChild(_lockstepManager);

        // Resolve local player: use config.LocalPlayerId when set by the lobby,
        // otherwise fall back to the first non-AI player (skirmish compatibility).
        int localPlayerId = config.LocalPlayerId >= 0 ? config.LocalPlayerId : 0;
        if (config.LocalPlayerId < 0)
        {
            for (int i = 0; i < config.PlayerConfigs.Length; i++)
            {
                if (!config.PlayerConfigs[i].IsAI)
                {
                    localPlayerId = config.PlayerConfigs[i].PlayerId;
                    break;
                }
            }
        }

        // LockstepManager only tracks human players — AI runs deterministically on every machine.
        int humanPlayerCount = 0;
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            if (!config.PlayerConfigs[i].IsAI)
                humanPlayerCount++;
        }

        _lockstepManager.Initialize(
            localPlayerId,
            humanPlayerCount,
            _networkTransport.IsHost,
            inputDelay: 6,
            _networkTransport);

        _gameManager.StartMultiplayerMatch(config.MatchSeed, _lockstepManager);
    }

    /// <summary>
    /// Finds the harvester unit type ID for a faction.
    /// Searches the unit data registry for a unit whose ID contains "harvester"
    /// and belongs to the given faction.
    /// </summary>
    private string FindHarvesterTypeId(string factionId)
    {
        var factionUnits = _unitDataRegistry.GetFactionUnits(factionId);

        // 1. Explicit "harvester" in the unit ID (forward-compat when dedicated harvester units are added)
        for (int i = 0; i < factionUnits.Count; i++)
            if (factionUnits[i].Id.Contains("harvester", StringComparison.OrdinalIgnoreCase))
                return factionUnits[i].Id;

        // Cross-faction generic harvester unit
        if (_unitDataRegistry.HasUnit("harvester"))
            return "harvester";

        // 2. Support category, non-air (engineer / worker archetype — best proxy for a harvester)
        for (int i = 0; i < factionUnits.Count; i++)
        {
            var u = factionUnits[i];
            if (u.Category == UnitCategory.Support && IsGroundMovementClass(u.MovementClassId))
                return u.Id;
        }

        // 3. Cheapest LightVehicle — visible on ground, fast, clearly a scout/worker
        UnitData? cheapestLight = null;
        for (int i = 0; i < factionUnits.Count; i++)
        {
            var u = factionUnits[i];
            if (u.Category == UnitCategory.LightVehicle &&
                (cheapestLight == null || u.Cost < cheapestLight.Cost))
                cheapestLight = u;
        }
        if (cheapestLight != null) return cheapestLight.Id;

        // 4. Any non-air, non-naval ground unit
        for (int i = 0; i < factionUnits.Count; i++)
        {
            var u = factionUnits[i];
            if (IsGroundMovementClass(u.MovementClassId))
                return u.Id;
        }

        // 5. Absolute fallback: any faction unit
        return factionUnits.Count > 0 ? factionUnits[0].Id : string.Empty;
    }

    /// <summary>
    /// Returns true when the given MovementClassId belongs to a ground/surface unit
    /// (not a helicopter, jet, or naval vessel).
    /// </summary>
    private static bool IsGroundMovementClass(string movementClassId) =>
        movementClassId != "Helicopter" && movementClassId != "Jet" && movementClassId != "Naval";

    /// <summary>
    /// Tears down all child nodes and managers from a previous match.
    /// </summary>
    // ─────────────────────────────────────────────────────────────────
    // DEBUG SNAPSHOT
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A point-in-time snapshot of key session metrics for the debug overlay.
    /// All fields are value types so no allocation occurs on the hot path.
    /// </summary>
    public struct DebugSnapshot
    {
        // Map / world
        public string MapId;
        public string Biome;
        public int MapWidth;
        public int MapHeight;
        public bool FogOfWar;

        // Simulation
        public ulong SimTick;
        public int UnitCount;
        public int BuildingCount;
        public MatchState MatchState;
        public string WinConditionName;

        // Camera
        public float CameraX;
        public float CameraY;
        public float CameraZ;
        public float CameraZoom;

        // Local player economy (player 1 / first non-AI)
        public int Cordite;
        public int VoltaicCharge;
        public int Supply;
        public int MaxSupply;

        // Match timing
        public int GameSpeed;
        public bool IsMultiplayer;
    }

    /// <summary>
    /// Returns a lightweight snapshot of current game state for the debug overlay.
    /// Safe to call every frame.
    /// </summary>
    public DebugSnapshot GetDebugSnapshot()
    {
        var snap = new DebugSnapshot();

        // Map
        snap.MapId        = ActiveMap?.Id ?? string.Empty;
        snap.Biome        = ActiveMap?.Biome ?? string.Empty;
        snap.MapWidth     = ActiveMap?.Width ?? 0;
        snap.MapHeight    = ActiveMap?.Height ?? 0;
        snap.FogOfWar     = ActiveConfig?.FogOfWar ?? false;
        snap.GameSpeed    = ActiveConfig?.GameSpeed ?? 1;

        // Win condition
        snap.WinConditionName = _winCondition.ToString();

        // Sim
        snap.SimTick      = _gameManager?.CurrentTick ?? 0;
        snap.UnitCount    = _unitSpawner?.ActiveCount ?? 0;
        snap.BuildingCount = _buildingPlacer?.GetAllBuildings().Count ?? 0;
        snap.MatchState   = CurrentMatchState;

        // Camera
        if (_camera is not null)
        {
            var focus = _camera.FocusPoint;
            snap.CameraX = focus.X;
            snap.CameraY = focus.Y;
            snap.CameraZ = focus.Z;
            snap.CameraZoom = _camera.CurrentZoom;
        }

        // Economy
        var economy = _economyManager?.GetPlayer(_localPlayerId);
        if (economy is not null)
        {
            snap.Cordite       = economy.Cordite.ToInt();
            snap.VoltaicCharge = economy.VoltaicCharge.ToInt();
            snap.Supply        = economy.CurrentSupply;
            snap.MaxSupply     = economy.MaxSupply;
        }

        // Multiplayer
        snap.IsMultiplayer = _lockstepManager is not null;

        return snap;
    }

    private void CleanupMatch()
    {
        _lockstepManager?.Shutdown();
        _networkTransport?.Disconnect();

        // Remove all managed child nodes
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            Node child = GetChild(i);
            RemoveChild(child);
            child.QueueFree();
        }

        _economyManager = null;
        _techTreeManager = null;
        _unitSpawner = null;
        _harvesterSystem = null;
        _saveManager = null;
        _replayManager = null;
        _lockstepManager = null;
        _networkTransport = null;
        _camera = null;
        _terrainRenderer = null;
        _waterRenderer = null;
        _propPlacer = null;
        _selectionManager = null;
        _commandInput = null;
        _buildingPlacer = null;
        _gameHUD = null;
        _skirmishAIs.Clear();
        _visionSystem = null;
        _playerFogs = null;
        _playerFogSnapshots = null;
        _visionComponents.Clear();

        CurrentMatchState = MatchState.Setup;
    }
}
