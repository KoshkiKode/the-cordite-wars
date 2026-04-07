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

    // ── Timing ───────────────────────────────────────────────────────

    private int _tickCounter;
    private int _totalTicksElapsed;

    // Opening duration (in ticks): Easy=6000(~3.3min), Medium=5400(~3min), Hard=4500(~2.5min)
    private int _openingEndTick;
    // Expanding->Aggression transition: Easy=never, Medium=14400(~8min), Hard=12600(~7min)
    private int _aggressionStartTick;

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
        BuildingRegistry buildingRegistry)
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
    }

    // ── State Execution ──────────────────────────────────────────────

    private void ExecuteOpening(PlayerEconomy economy)
    {
        // Follow scripted build order
        _buildOrder?.ExecuteNextStep(
            PlayerId, economy, _basePosition,
            _economyManager!, _buildingPlacer!, _buildingRegistry!,
            _unitDataRegistry!);
    }

    private void ExecuteExpanding(PlayerEconomy economy)
    {
        // Continue build order if not complete
        if (_buildOrder is not null && !_buildOrder.IsComplete)
        {
            _buildOrder.ExecuteNextStep(
                PlayerId, economy, _basePosition,
                _economyManager!, _buildingPlacer!, _buildingRegistry!,
                _unitDataRegistry!);
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
        // AI produces units from all production buildings
        if (_buildingPlacer is null || _unitDataRegistry is null) return;

        var buildings = _buildingPlacer.GetAllBuildings();
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            if (building.PlayerId != PlayerId) continue;
            if (!building.IsConstructed) continue;
            if (building.Data is null) continue;

            // Check if building can produce units
            if (building.Data.UnlocksUnitIds.Count == 0) continue;

            // Find a unit this building can make that we can afford
            for (int u = 0; u < building.Data.UnlocksUnitIds.Count; u++)
            {
                string unitId = building.Data.UnlocksUnitIds[u];
                if (!_unitDataRegistry.HasUnit(unitId)) continue;

                UnitData unitData = _unitDataRegistry.GetUnitData(unitId);
                if (economy.CanAfford(unitData.Cost, unitData.SecondaryCost))
                {
                    // Check supply
                    if (economy.CurrentSupply + unitData.PopulationCost <= economy.MaxSupply)
                    {
                        _economyManager?.TryBuildUnit(PlayerId, unitData);
                        break; // One unit per building per AI tick
                    }
                }
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

        // Place near base with offset
        FixedVector2 buildPos = new FixedVector2(
            _basePosition.X + FixedPoint.FromInt((_totalTicksElapsed / 1000) % 10 - 5),
            _basePosition.Y + FixedPoint.FromInt((_totalTicksElapsed / 500) % 10 - 5));

        _economyManager?.TryBuildBuilding(PlayerId, data);
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private void OnBaseUnderAttack(int playerId, Vector3 position)
    {
        if (playerId != PlayerId) return;
        _isUnderAttack = true;
        _crisisRecoverTicks = 0;
    }
}
