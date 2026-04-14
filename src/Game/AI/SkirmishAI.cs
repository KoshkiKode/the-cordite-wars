using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Tech;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.AI;

/// <summary>
/// AI difficulty levels.
/// </summary>
public enum AIDifficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

/// <summary>
/// AI state machine phases.
/// </summary>
public enum AIState
{
    Opening,
    Expanding,
    Aggression,
    Crisis
}

/// <summary>
/// Main AI controller. One per AI player. Runs every 30 ticks (1 second).
/// State machine: Opening -> Expanding -> Aggression -> Crisis.
/// Simulation code: FixedPoint, no LINQ, SortedList only.
/// </summary>
public partial class SkirmishAI : Node
{
    // ── Configuration ────────────────────────────────────────────────

    private const int TickInterval = 30; // Run every 30 ticks (1 second at 30 TPS)

    // Building placement offset cycling constants
    private const int BuildingPlacementStepInterval = 500; // ticks between offset index changes
    private const int OffsetPatternSize = 12;              // number of offset positions in the pattern

    public int PlayerId { get; private set; }
    public string FactionId { get; private set; } = string.Empty;
    public AIDifficulty Difficulty { get; private set; }
    public AIState CurrentState { get; private set; } = AIState.Opening;

    // ── Dependencies ─────────────────────────────────────────────────

    private EconomyManager? _economyManager;
    private UnitSpawner? _unitSpawner;
    private BuildingPlacer? _buildingPlacer;
    private TechTreeManager? _techTreeManager;
    private UnitDataRegistry? _unitDataRegistry;
    private BuildingRegistry? _buildingRegistry;

    // ── Sub-controllers ──────────────────────────────────────────────

    private AIBuildOrder? _buildOrder;
    private AICommander? _commander;
    private NavalBehaviourTree? _navalTree;

    // ── Timing ───────────────────────────────────────────────────────

    private int _tickCounter;
    private int _totalTicksElapsed;

    // Opening duration (in ticks): Easy=6000(~3.3min), Medium=5400(~3min), Hard=4500(~2.5min)
    private int _openingEndTick;
    // Expanding->Aggression transition: Easy=never, Medium=14400(~8min), Hard=12600(~7min)
    private int _aggressionStartTick;

    // ── Production tracking ──────────────────────────────────────────

    // Maps building ID → AI ticks remaining until the in-progress unit is ready.
    // One unit per building; key absent means the building is idle.
    private readonly SortedList<int, int> _productionTimers = new();

    // Maps building ID → unit type ID currently being produced.
    private readonly SortedList<int, string> _productionUnitType = new();

    // ── State ────────────────────────────────────────────────────────

    private FixedVector2 _basePosition;
    private bool _isUnderAttack;
    private int _crisisRecoverTicks;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int playerId,
        string factionId,
        AIDifficulty difficulty,
        FixedVector2 basePosition,
        EconomyManager economyManager,
        UnitSpawner unitSpawner,
        BuildingPlacer buildingPlacer,
        TechTreeManager techTreeManager,
        UnitDataRegistry unitDataRegistry,
        BuildingRegistry buildingRegistry,
        int waterCellPercent = 0)
    {
        PlayerId = playerId;
        FactionId = factionId;
        Difficulty = difficulty;
        _basePosition = basePosition;
        _economyManager = economyManager;
        _unitSpawner = unitSpawner;
        _buildingPlacer = buildingPlacer;
        _techTreeManager = techTreeManager;
        _unitDataRegistry = unitDataRegistry;
        _buildingRegistry = buildingRegistry;

        Name = $"SkirmishAI_P{playerId}";

        // Set phase transition timings based on difficulty
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                _openingEndTick = 6000;
                _aggressionStartTick = int.MaxValue; // Easy never attacks aggressively
                break;
            case AIDifficulty.Medium:
                _openingEndTick = 5400;
                _aggressionStartTick = 14400;
                break;
            case AIDifficulty.Hard:
                _openingEndTick = 4500;
                _aggressionStartTick = 12600;
                break;
        }

        // Create sub-controllers
        _buildOrder = new AIBuildOrder();
        _buildOrder.Initialize(factionId, difficulty);
        AddChild(_buildOrder);

        _commander = new AICommander();
        _commander.Initialize(playerId, factionId, difficulty, _basePosition, unitSpawner);
        AddChild(_commander);

        // Create naval behaviour tree — activates automatically on water-heavy maps
        _navalTree = new NavalBehaviourTree();
        _navalTree.Initialize(playerId, factionId, difficulty, waterCellPercent);
        AddChild(_navalTree);

        // If naval is active, append Shipyard build steps to the build order
        if (_navalTree.IsActive)
        {
            _buildOrder.AppendNavalBuildSteps(factionId);
        }

        // Listen for attacks on our base
        EventBus.Instance?.Connect(EventBus.SignalName.BaseUnderAttack,
            Callable.From<int, Vector3>(OnBaseUnderAttack));
    }

    // ── Simulation Tick ──────────────────────────────────────────────

    /// <summary>
    /// Called every simulation tick. Only does work every TickInterval ticks.
    /// All logic uses FixedPoint. No LINQ.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        _totalTicksElapsed++;
        _tickCounter++;

        if (_tickCounter < TickInterval)
            return;
        _tickCounter = 0;

        UpdateState();
        ExecuteState();
    }

    // ── State Machine ────────────────────────────────────────────────

    private void UpdateState()
    {
        // Check crisis: under attack and losing buildings
        if (_isUnderAttack)
        {
            if (CurrentState != AIState.Crisis)
            {
                CurrentState = AIState.Crisis;
                GD.Print($"[SkirmishAI P{PlayerId}] Entering CRISIS state.");
            }
            return;
        }

        // Recover from crisis
        if (CurrentState == AIState.Crisis)
        {
            _crisisRecoverTicks++;
            if (_crisisRecoverTicks > 90) // ~3 seconds recovery
            {
                _crisisRecoverTicks = 0;
                // Return to previous state based on timing
                CurrentState = _totalTicksElapsed < _openingEndTick
                    ? AIState.Opening
                    : _totalTicksElapsed < _aggressionStartTick
                        ? AIState.Expanding
                        : AIState.Aggression;
            }
            return;
        }

        // Phase transitions
        if (CurrentState == AIState.Opening && _totalTicksElapsed >= _openingEndTick)
        {
            CurrentState = AIState.Expanding;
            GD.Print($"[SkirmishAI P{PlayerId}] Entering EXPANDING state.");
        }
        else if (CurrentState == AIState.Expanding && _totalTicksElapsed >= _aggressionStartTick)
        {
            CurrentState = AIState.Aggression;
            GD.Print($"[SkirmishAI P{PlayerId}] Entering AGGRESSION state.");
        }
    }

    private void ExecuteState()
    {
        if (_economyManager is null || _buildOrder is null || _commander is null) return;

        PlayerEconomy? economy = _economyManager.GetPlayer(PlayerId);
        if (economy is null) return;

        switch (CurrentState)
        {
            case AIState.Opening:
                ExecuteOpening(economy);
                break;
            case AIState.Expanding:
                ExecuteExpanding(economy);
                break;
            case AIState.Aggression:
                ExecuteAggression(economy);
                break;
            case AIState.Crisis:
                ExecuteCrisis(economy);
                break;
        }

        // Commander always manages squads
        _commander.Update(CurrentState);

        // Naval behaviour tree ticks in parallel with the ground pipeline
        if (_navalTree is not null && _navalTree.IsActive && _unitSpawner is not null &&
            _economyManager is not null && _buildingPlacer is not null &&
            _buildingRegistry is not null && _unitDataRegistry is not null)
        {
            _navalTree.ProcessTick(
                FixedPoint.One,   // deltaTime placeholder — naval tree uses tick counts internally
                _totalTicksElapsed,
                economy,
                _economyManager,
                _buildingPlacer,
                _buildingRegistry,
                _unitDataRegistry,
                _unitSpawner);
        }
    }

    // ── State Execution ──────────────────────────────────────────────

    private void ExecuteOpening(PlayerEconomy economy)
    {
        // Follow scripted build order
        _buildOrder?.ExecuteNextStep(
            PlayerId, economy, _basePosition,
            _economyManager!, _buildingPlacer!, _buildingRegistry!,
            _unitDataRegistry!, _unitSpawner!, FactionId);
    }

    private void ExecuteExpanding(PlayerEconomy economy)
    {
        // Continue build order if not complete
        if (_buildOrder is not null && !_buildOrder.IsComplete)
        {
            _buildOrder.ExecuteNextStep(
                PlayerId, economy, _basePosition,
                _economyManager!, _buildingPlacer!, _buildingRegistry!,
                _unitDataRegistry!, _unitSpawner!, FactionId);
        }

        // Build additional economy buildings
        if (economy.Cordite > FixedPoint.FromInt(500))
        {
            TryBuildIfAffordable("refinery", economy);
        }

        // Build supply depots if near cap
        if (economy.CurrentSupply > economy.MaxSupply - 5)
        {
            TryBuildIfAffordable("supply_depot", economy);
        }

        // Continue producing units
        ProduceUnits(economy);
    }

    private void ExecuteAggression(PlayerEconomy economy)
    {
        // Keep building economy
        if (economy.CurrentSupply > economy.MaxSupply - 5)
        {
            TryBuildIfAffordable("supply_depot", economy);
        }

        // Max unit production
        ProduceUnits(economy);
    }

    private void ExecuteCrisis(PlayerEconomy economy)
    {
        // Produce defensive units
        ProduceUnits(economy);

        // Commander handles pulling army back
        _isUnderAttack = false; // Reset after one cycle, re-evaluate next tick
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void ProduceUnits(PlayerEconomy economy)
    {
        // AI produces units from all production buildings, respecting each unit's BuildTime.
        // Each building queues at most one unit at a time. The cost is deducted when
        // production starts; the unit spawns only when the build-time timer expires.
        if (_buildingPlacer is null || _unitDataRegistry is null) return;

        var buildings = _buildingPlacer.GetAllBuildings();

        // ── Step 1: Tick down in-progress production and spawn completed units ──
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            if (building.PlayerId != PlayerId) continue;
            if (!_productionTimers.ContainsKey(building.BuildingId)) continue;

            _productionTimers[building.BuildingId]--;

            if (_productionTimers[building.BuildingId] <= 0)
            {
                // Production complete — spawn unit
                _productionTimers.Remove(building.BuildingId);
                if (_productionUnitType.TryGetValue(building.BuildingId, out string? completedUnitId) &&
                    !string.IsNullOrEmpty(completedUnitId))
                {
                    _productionUnitType.Remove(building.BuildingId);
                    _unitSpawner?.SpawnUnit(completedUnitId, FactionId, PlayerId,
                        building.RallyPoint, FixedPoint.Zero);
                }
            }
        }

        // ── Step 2: Start new production in idle buildings ────────────────────
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            if (building.PlayerId != PlayerId) continue;
            if (!building.IsConstructed) continue;
            if (building.Data is null) continue;
            if (building.Data.UnlocksUnitIds.Count == 0) continue;
            if (_productionTimers.ContainsKey(building.BuildingId)) continue; // already producing

            // Find the first affordable unit this building can make
            for (int u = 0; u < building.Data.UnlocksUnitIds.Count; u++)
            {
                string unitId = building.Data.UnlocksUnitIds[u];
                if (!_unitDataRegistry.HasUnit(unitId)) continue;

                UnitData unitData = _unitDataRegistry.GetUnitData(unitId);
                if (!economy.CanAfford(unitData.Cost, unitData.SecondaryCost)) continue;
                if (economy.CurrentSupply + unitData.PopulationCost > economy.MaxSupply) continue;

                if (_economyManager?.TryBuildUnit(PlayerId, unitData) == true)
                {
                    // Deducted cost; now schedule the build timer.
                    // BuildTime is in seconds; TickInterval ticks = 1 second → timer in AI ticks.
                    int buildTimeAITicks = Mathf.Max(1, unitData.BuildTime.ToInt());
                    _productionTimers[building.BuildingId] = buildTimeAITicks;
                    _productionUnitType[building.BuildingId] = unitId;
                }
                break; // One attempt per building per AI tick
            }
        }
    }

    private void TryBuildIfAffordable(string buildingId, PlayerEconomy economy)
    {
        if (_buildingRegistry is null || _buildingPlacer is null) return;

        // Try faction-specific variant first
        string factionBuildingId = $"{FactionId}_{buildingId}";
        if (!_buildingRegistry.HasBuilding(factionBuildingId))
            factionBuildingId = buildingId;
        if (!_buildingRegistry.HasBuilding(factionBuildingId))
            return;

        BuildingData data = _buildingRegistry.GetBuilding(factionBuildingId);
        if (!economy.CanAfford(data.Cost, data.SecondaryCost))
            return;

        // Try a small grid of candidate positions near the base and pick the first free cell
        int baseX = _basePosition.X.ToInt();
        int baseY = _basePosition.Y.ToInt();

        // Offset pattern: ring outward from base so buildings don't stack
        int step = (int)(_totalTicksElapsed / BuildingPlacementStepInterval) % OffsetPatternSize;
        int[] offsets = [-6, -4, -2, 0, 2, 4, 6, -8, 8, -10, 10, 0];
        int dx = offsets[step % offsets.Length];
        int dy = offsets[(step + 3) % offsets.Length];

        int gridX = baseX + dx;
        int gridY = baseY + dy;

        // PlaceBuildingForAI deducts cost and places the building atomically
        _buildingPlacer.PlaceBuildingForAI(factionBuildingId, PlayerId, gridX, gridY);
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private void OnBaseUnderAttack(int playerId, Vector3 position)
    {
        if (playerId != PlayerId) return;
        _isUnderAttack = true;
        _crisisRecoverTicks = 0;
    }
}
