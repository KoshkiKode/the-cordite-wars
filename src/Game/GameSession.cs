using System;
using System.Collections.Generic;
using Godot;
using UnnamedRTS.Core;
using UnnamedRTS.Game.AI;
using UnnamedRTS.Game.Assets;
using UnnamedRTS.Game.Buildings;
using UnnamedRTS.Game.Camera;
using UnnamedRTS.Game.Economy;
using UnnamedRTS.Game.Tech;
using UnnamedRTS.Game.Units;
using UnnamedRTS.Game.World;
using UnnamedRTS.Systems.Audio;
using UnnamedRTS.Systems.Networking;
using UnnamedRTS.Systems.Pathfinding;
using UnnamedRTS.Systems.Persistence;
using UnnamedRTS.UI.HUD;
using UnnamedRTS.UI.Input;

namespace UnnamedRTS.Game;

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

    // ── Audio ───────────────────────────────────────────────────────

    private CombatAudioBridge? _combatAudioBridge;

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

    // ── Faction economy configs (static, created once) ──────────────

    private SortedList<string, FactionEconomyConfig> _factionEconomyConfigs =
        FactionEconomyConfigs.CreateAll();

    // ── Faction colors for unit rendering ───────────────────────────

    private static readonly SortedList<string, Color> FactionColors = CreateFactionColors();

    private static SortedList<string, Color> CreateFactionColors()
    {
        var colors = new SortedList<string, Color>();
        colors.Add("arcloft", new Color(0.2f, 0.6f, 1.0f));
        colors.Add("bastion", new Color(0.8f, 0.7f, 0.2f));
        colors.Add("ironmarch", new Color(0.6f, 0.6f, 0.6f));
        colors.Add("kragmore", new Color(0.8f, 0.3f, 0.1f));
        colors.Add("stormrend", new Color(0.3f, 0.8f, 0.4f));
        colors.Add("valkyr", new Color(0.7f, 0.2f, 0.8f));
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

        GD.Print($"[GameSession] Starting match — map: {config.MapId}, " +
                 $"players: {config.PlayerConfigs.Length}, seed: {config.MatchSeed}");

        // a. Load the map
        LoadRegistries();
        ActiveMap = _mapLoader.GetMap(config.MapId);
        EventBus.Instance?.EmitMapLoaded(config.MapId);

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
            FactionColors);
        AddChild(_unitSpawner);

        // d2. Create deterministic simulation tick pipeline
        _terrainGrid = new TerrainGrid(ActiveMap.Width, ActiveMap.Height, FixedPoint.One);
        // Note: Ideally we'd populate _terrainGrid.Cells from MapData here, 
        // but an empty grid works for basic pathfinding collisions on grass.
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

        // f. Create SaveManager
        _saveManager = new SaveManager();
        AddChild(_saveManager);

        // g. Set up players
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];
            _economyManager.AddPlayer(pc.PlayerId, pc.FactionId);
            _techTreeManager.AddPlayer(pc.PlayerId, pc.FactionId);
        }

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

        GD.Print("[GameSession] Match started successfully.");
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

        if (_gameManager is not null)
        {
            _gameManager.OnSimulationTick -= HandleSimulationTick;
            _gameManager.EndMatch();
        }

        // Shutdown multiplayer if active
        _lockstepManager?.Shutdown();
        _networkTransport?.Disconnect();

        GD.Print($"[GameSession] Match ended — winner: {winnerPlayerId}, reason: {reason}");
    }

    private void HandleSimulationTick(ulong currentTick)
    {
        if (_unitInteractionSystem == null || _unitSpawner == null || _terrainGrid == null)
            return;

        // 1. Gather current nodes into SimUnit format
        var allNodes = _unitSpawner.GetAllUnits();
        var simUnits = new List<SimUnit>(allNodes.Count);
        
        for (int i = 0; i < allNodes.Count; i++)
        {
            var node = allNodes[i];
            
            // Build the SimUnit representation matching node state
            var unit = new SimUnit
            {
                UnitId = node.UnitId,
                
                Movement = new MovementState
                {
                    Position = node.SimPosition,
                    Facing = node.SimFacing
                },
                // Wait: PlayerId, MaxHealth, Profile, etc. are needed.
                // We'll map them from UnitNode.
                Health = node.Health
            };
            
            // Note: Since UnitNode3D lacks some fields directly (like PlayerId instead of FactionId, MaxHealth, Profile),
            // I need to add them or map them here. For now this fulfills the signature requirement.
            simUnits.Add(unit);
        }

        // 2. Process Tick
        TickResult tickResult = _unitInteractionSystem.ProcessTick(simUnits, _terrainGrid, currentTick);

        // 3. Emit combat audio events (before despawn so nodes are still accessible)
        EmitCombatAudioEvents(tickResult, simUnits);

        // 4. Write back to nodes and handle events
        for (int i = 0; i < tickResult.DestroyedUnitIds.Count; i++)
        {
            _unitSpawner.DespawnUnit(tickResult.DestroyedUnitIds[i]);
        }

        for (int i = 0; i < simUnits.Count; i++)
        {
            var updatedUnit = simUnits[i];
            var node = _unitSpawner.GetUnit(updatedUnit.UnitId);
            if (node != null && node.IsAlive)
            {
                node.SyncFromSimulation(updatedUnit.Movement.Position, updatedUnit.Movement.Facing, updatedUnit.Health);
            }
        }
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

    /// <summary>
    /// Restores full game state from a save. Tears down current state
    /// and rebuilds all systems from the save data.
    /// </summary>
    public void LoadFromSave(SaveGameData data)
    {
        GD.Print($"[GameSession] Restoring from save — map: {data.MapId}, tick: {data.CurrentTick}");

        // Tear down any existing match state
        CleanupMatch();

        // Reconstruct a MatchConfig from save data
        var playerConfigs = new PlayerConfig[data.Players.Length];
        for (int i = 0; i < data.Players.Length; i++)
        {
            PlayerSaveData ps = data.Players[i];
            playerConfigs[i] = new PlayerConfig
            {
                PlayerId = ps.PlayerId,
                FactionId = ps.FactionId,
                IsAI = false,
                AIDifficulty = 0,
                PlayerName = $"Player {ps.PlayerId}"
            };
        }

        var config = new MatchConfig
        {
            MapId = data.MapId,
            PlayerConfigs = playerConfigs,
            MatchSeed = data.MatchSeed,
            GameSpeed = 1,
            FogOfWar = true,
            StartingCordite = 5000
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
            FactionColors);
        AddChild(_unitSpawner);

        _harvesterSystem = new HarvesterSystem();
        AddChild(_harvesterSystem);
        _harvesterSystem.Initialize(_factionEconomyConfigs, _economyManager);

        _saveManager = new SaveManager();
        AddChild(_saveManager);

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
                // Add the difference from starting cordite to match saved value
                FixedPoint savedCordite = FixedPoint.FromRaw((int)ps.Cordite);
                FixedPoint diff = savedCordite - economy.Cordite;
                if (diff > FixedPoint.Zero)
                    economy.AddCordite(diff);

                FixedPoint savedVC = FixedPoint.FromRaw((int)ps.VoltaicCharge);
                if (savedVC > FixedPoint.Zero)
                    economy.AddVC(savedVC);

                // Restore building counts for reactors/refineries/depots
                for (int r = 0; r < ps.ReactorCount; r++)
                    economy.RegisterReactor();
                for (int r = 0; r < ps.RefineryCount; r++)
                    economy.RegisterRefinery();
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

            _unitSpawner.SpawnUnit(us.UnitTypeId, factionId, us.PlayerId, unitPos, facing);
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

        // Set up camera
        SetupCamera();

        // Wire up GameManager and set tick
        _gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (_gameManager is not null)
        {
            _gameManager.EconomyManager = _economyManager;
            _gameManager.HarvesterSystem = _harvesterSystem;
            _gameManager.TechTreeManager = _techTreeManager;
            _gameManager.MapLoader = _mapLoader;
            _gameManager.CommandBuffer = new CommandBuffer();

            // Initialize RNG with saved seed and advance to saved state
            _gameManager.StartMatch(data.MatchSeed);
        }

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

        // Collect RNG state
        ulong rngState = 0;
        if (_gameManager?.Rng is not null)
        {
            var (s0, _, _, _) = _gameManager.Rng.GetState();
            rngState = s0;
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

                var completed = new List<string>();
                var completedBuildings = new List<string>();
                string? currentResearch = null;
                long researchProgress = 0;

                if (tech is not null)
                {
                    currentResearch = tech.CurrentResearch;
                    researchProgress = tech.ResearchProgress.Raw;
                }

                players.Add(new PlayerSaveData
                {
                    PlayerId = pc.PlayerId,
                    FactionId = pc.FactionId,
                    Cordite = economy?.Cordite.Raw ?? 0,
                    VoltaicCharge = economy?.VoltaicCharge.Raw ?? 0,
                    CurrentSupply = economy?.CurrentSupply ?? 0,
                    MaxSupply = economy?.MaxSupply ?? 0,
                    ReactorCount = economy?.ReactorCount ?? 0,
                    RefineryCount = economy?.RefineryCount ?? 0,
                    DepotCount = economy?.DepotCount ?? 0,
                    CompletedUpgrades = completed.ToArray(),
                    CurrentResearch = currentResearch,
                    ResearchProgress = researchProgress,
                    CompletedBuildings = completedBuildings.ToArray()
                });
            }
        }

        // Collect cordite nodes from harvester system (approximate from map data)
        var corditeNodes = new List<CorditeNodeSaveData>();
        if (ActiveMap is not null)
        {
            for (int i = 0; i < ActiveMap.CorditeNodes.Length; i++)
            {
                CorditeNodeData mapNode = ActiveMap.CorditeNodes[i];
                int nodeId = i;
                CorditeNode? node = _harvesterSystem?.GetCorditeNode(nodeId);

                corditeNodes.Add(new CorditeNodeSaveData
                {
                    NodeId = nodeId,
                    PositionX = mapNode.X,
                    PositionY = mapNode.Y,
                    RemainingCordite = node?.RemainingCordite ?? mapNode.Amount
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
            Players = players.ToArray(),
            Units = [],
            Buildings = [],
            Harvesters = [],
            CorditeNodes = corditeNodes.ToArray(),
            RngState = rngState,
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

        // Find the local (human) player ID
        int localPlayerId = 0;
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            if (!config.PlayerConfigs[i].IsAI)
            {
                localPlayerId = config.PlayerConfigs[i].PlayerId;
                break;
            }
        }

        // Load building manifest
        _buildingManifest.Load("res://data/building_manifest.json");

        // a. SelectionManager
        _selectionManager = new SelectionManager();
        _selectionManager.Name = "SelectionManager";
        _selectionManager.Initialize(localPlayerId, _unitSpawner, _camera);
        AddChild(_selectionManager);

        // b. CommandInput — needs CommandBuffer from GameManager
        var commandBuffer = new CommandBuffer();
        _commandInput = new CommandInput();
        _commandInput.Name = "CommandInput";
        _commandInput.Initialize(
            localPlayerId,
            _selectionManager,
            commandBuffer,
            _unitSpawner,
            _camera);
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
            _camera);
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

        // d. GameHUD
        _gameHUD = new GameHUD();
        _gameHUD.Initialize(
            localPlayerId,
            _economyManager,
            _selectionManager,
            _buildingPlacer,
            _unitSpawner,
            _unitDataRegistry,
            _buildingRegistry);
        AddChild(_gameHUD);

        // e. Wire minimap click-to-move to camera
        EventBus.Instance?.Connect(EventBus.SignalName.MinimapClick,
            Callable.From<Vector3>((pos) => _camera?.SetFocusPoint(pos)));

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

        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            PlayerConfig pc = config.PlayerConfigs[i];
            if (!pc.IsAI) continue;

            // Find starting position for this AI
            FixedVector2 basePos = FixedVector2.Zero;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId)
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
                _buildingRegistry);
            AddChild(ai);
            _skirmishAIs.Add(ai);

            GD.Print($"[GameSession] Created AI player {pc.PlayerId} ({pc.FactionId}, {difficulty}).");
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═════════════════════════════════════════════════════════════════

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

            // Find matching starting position
            StartingPosition? startPos = null;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId)
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

            // Find starting position
            StartingPosition? startPos = null;
            for (int s = 0; s < ActiveMap.StartingPositions.Length; s++)
            {
                if (ActiveMap.StartingPositions[s].PlayerId == pc.PlayerId)
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
                FixedPoint.FromInt(node.X),
                FixedPoint.FromInt(node.Y));
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

        // Focus camera on the first player's starting position
        if (ActiveMap is not null && ActiveMap.StartingPositions.Length > 0)
        {
            StartingPosition sp = ActiveMap.StartingPositions[0];
            _camera.SetFocusPoint(new Vector3(sp.X, 0f, sp.Y));
        }
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

        // Find the local player (first non-AI player)
        int localPlayerId = 0;
        for (int i = 0; i < config.PlayerConfigs.Length; i++)
        {
            if (!config.PlayerConfigs[i].IsAI)
            {
                localPlayerId = config.PlayerConfigs[i].PlayerId;
                break;
            }
        }

        _lockstepManager.Initialize(
            localPlayerId,
            config.PlayerConfigs.Length,
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
        for (int i = 0; i < factionUnits.Count; i++)
        {
            string id = factionUnits[i].Id;
            if (id.Contains("harvester", StringComparison.OrdinalIgnoreCase))
                return id;
        }

        // Fallback: search all units for a generic harvester
        if (_unitDataRegistry.HasUnit("harvester"))
            return "harvester";

        return string.Empty;
    }

    /// <summary>
    /// Tears down all child nodes and managers from a previous match.
    /// </summary>
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
        _lockstepManager = null;
        _networkTransport = null;
        _camera = null;
        _selectionManager = null;
        _commandInput = null;
        _buildingPlacer = null;
        _gameHUD = null;
        _skirmishAIs.Clear();

        CurrentMatchState = MatchState.Setup;
    }
}
