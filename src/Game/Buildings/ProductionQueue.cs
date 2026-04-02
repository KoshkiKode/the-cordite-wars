using Godot;
using System.Collections.Generic;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Assets;
using UnnamedRTS.Game.Economy;
using UnnamedRTS.Game.Tech;
using UnnamedRTS.Game.Units;

namespace UnnamedRTS.Game.Buildings;

/// <summary>
/// Per-building production queue. Max 5 units. Ticks progress deterministically
/// using FixedPoint. Spawns units via UnitSpawner on completion.
/// Simulation code: FixedPoint, no LINQ, SortedList.
/// </summary>
public partial class ProductionQueue : Node
{
    // ── Constants ────────────────────────────────────────────────────

    private const int MaxQueueSize = 5;

    // ── Dependencies ─────────────────────────────────────────────────

    private BuildingInstance? _building;
    private EconomyManager? _economyManager;
    private UnitSpawner? _unitSpawner;
    private UnitDataRegistry? _unitDataRegistry;
    private TechTreeManager? _techTreeManager;
    private UpgradeRegistry? _upgradeRegistry;

    // ── Queue State (deterministic) ──────────────────────────────────

    private readonly SortedList<int, string> _queue = new(); // position → unitTypeId
    private int _nextSlot;

    // Current production
    private string _currentUnitTypeId = string.Empty;
    private FixedPoint _currentProgress;
    private FixedPoint _currentBuildTime;
    private bool _isProducing;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        BuildingInstance building,
        EconomyManager economyManager,
        UnitSpawner unitSpawner,
        UnitDataRegistry unitDataRegistry,
        TechTreeManager techTreeManager,
        UpgradeRegistry upgradeRegistry)
    {
        _building = building;
        _economyManager = economyManager;
        _unitSpawner = unitSpawner;
        _unitDataRegistry = unitDataRegistry;
        _techTreeManager = techTreeManager;
        _upgradeRegistry = upgradeRegistry;
    }

    // ── Public API ───────────────────────────────────────────────────

    public int QueueCount => _queue.Count;
    public bool IsProducing => _isProducing;
    public string CurrentUnitTypeId => _currentUnitTypeId;

    public FixedPoint CurrentProgress => _currentProgress;
    public FixedPoint CurrentBuildTime => _currentBuildTime;

    public float ProgressPercent
    {
        get
        {
            if (!_isProducing || _currentBuildTime <= FixedPoint.Zero) return 0f;
            return _currentProgress.ToFloat() / _currentBuildTime.ToFloat();
        }
    }

    /// <summary>
    /// Returns the unit type IDs in the queue (not including current production).
    /// </summary>
    public List<string> GetQueuedUnitTypes()
    {
        var result = new List<string>(_queue.Count);
        for (int i = 0; i < _queue.Count; i++)
            result.Add(_queue.Values[i]);
        return result;
    }

    /// <summary>
    /// Adds a unit to the production queue. Checks tech reqs, cost, and queue limit.
    /// Returns true if successfully added.
    /// </summary>
    public bool AddToQueue(string unitTypeId)
    {
        if (_building is null || _economyManager is null || _unitDataRegistry is null) return false;
        if (!_building.IsConstructed) return false;

        // Queue full?
        int totalInQueue = _queue.Count + (_isProducing ? 1 : 0);
        if (totalInQueue >= MaxQueueSize) return false;

        // Unit data exists?
        if (!_unitDataRegistry.HasUnit(unitTypeId)) return false;

        UnitData unitData = _unitDataRegistry.GetUnitData(unitTypeId);

        // Tech requirements met?
        if (_techTreeManager is not null && _upgradeRegistry is not null)
        {
            PlayerTechState? tech = _techTreeManager.GetPlayerTech(_building.PlayerId);
            if (tech is not null && !tech.CanBuildUnit(unitTypeId, _unitDataRegistry, _upgradeRegistry))
                return false;
        }

        // Deduct resources
        if (!_economyManager.TryBuildUnit(_building.PlayerId, unitData))
        {
            EventBus.Instance?.EmitInsufficientFunds(_building.PlayerId);
            return false;
        }

        // Add to queue
        _queue.Add(_nextSlot, unitTypeId);
        _nextSlot++;

        // If nothing is producing, start immediately
        if (!_isProducing)
            StartNextProduction();

        return true;
    }

    /// <summary>
    /// Removes a unit from the queue by position index. Refunds cost.
    /// </summary>
    public void RemoveFromQueue(int queueIndex)
    {
        if (queueIndex < 0 || queueIndex >= _queue.Count) return;
        if (_economyManager is null || _building is null || _unitDataRegistry is null) return;

        string unitTypeId = _queue.Values[queueIndex];
        int key = _queue.Keys[queueIndex];
        _queue.Remove(key);

        // Refund cost
        if (_unitDataRegistry.HasUnit(unitTypeId))
        {
            UnitData unitData = _unitDataRegistry.GetUnitData(unitTypeId);
            PlayerEconomy? economy = _economyManager.GetPlayer(_building.PlayerId);
            if (economy is not null)
            {
                economy.AddCordite(FixedPoint.FromInt(unitData.Cost));
                economy.AddVC(FixedPoint.FromInt(unitData.SecondaryCost));
                if (unitData.PopulationCost > 0)
                    economy.FreeSupply(unitData.PopulationCost);
            }
        }
    }

    /// <summary>
    /// Cancels the current production item. Refunds partial cost.
    /// </summary>
    public void CancelCurrent()
    {
        if (!_isProducing || _building is null || _economyManager is null || _unitDataRegistry is null) return;

        // Refund full cost (cancellation is generous)
        if (_unitDataRegistry.HasUnit(_currentUnitTypeId))
        {
            UnitData unitData = _unitDataRegistry.GetUnitData(_currentUnitTypeId);
            PlayerEconomy? economy = _economyManager.GetPlayer(_building.PlayerId);
            if (economy is not null)
            {
                economy.AddCordite(FixedPoint.FromInt(unitData.Cost));
                economy.AddVC(FixedPoint.FromInt(unitData.SecondaryCost));
                if (unitData.PopulationCost > 0)
                    economy.FreeSupply(unitData.PopulationCost);
            }
        }

        _isProducing = false;
        _currentUnitTypeId = string.Empty;
        _currentProgress = FixedPoint.Zero;
        _currentBuildTime = FixedPoint.Zero;

        // Start next if queue has items
        if (_queue.Count > 0)
            StartNextProduction();
    }

    /// <summary>
    /// Sets the rally point for newly produced units.
    /// </summary>
    public void SetRallyPoint(FixedVector2 position)
    {
        if (_building is not null)
            _building.RallyPoint = position;
    }

    // ── Simulation Tick ──────────────────────────────────────────────

    /// <summary>
    /// Called each sim tick. Advances production progress using FixedPoint.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        if (_building is null || !_building.IsConstructed) return;

        if (!_isProducing)
        {
            if (_queue.Count > 0)
                StartNextProduction();
            return;
        }

        _currentProgress = _currentProgress + deltaTime;

        if (_currentProgress >= _currentBuildTime)
        {
            CompleteProduction();

            // Start next if available
            if (_queue.Count > 0)
                StartNextProduction();
        }
    }

    // ── Internal ─────────────────────────────────────────────────────

    private void StartNextProduction()
    {
        if (_queue.Count == 0 || _unitDataRegistry is null) return;

        // Take first item from queue
        string unitTypeId = _queue.Values[0];
        int key = _queue.Keys[0];
        _queue.Remove(key);

        if (!_unitDataRegistry.HasUnit(unitTypeId))
        {
            // Invalid unit, skip
            if (_queue.Count > 0) StartNextProduction();
            return;
        }

        UnitData unitData = _unitDataRegistry.GetUnitData(unitTypeId);

        _currentUnitTypeId = unitTypeId;
        _currentProgress = FixedPoint.Zero;
        _currentBuildTime = unitData.BuildTime;
        _isProducing = true;

        EventBus.Instance?.EmitProductionStarted(_building!, unitTypeId);
    }

    private void CompleteProduction()
    {
        if (_building is null || _unitSpawner is null) return;

        _isProducing = false;
        string completedType = _currentUnitTypeId;
        _currentUnitTypeId = string.Empty;
        _currentProgress = FixedPoint.Zero;
        _currentBuildTime = FixedPoint.Zero;

        // Determine faction from building's player
        string factionId = string.Empty;
        if (_unitDataRegistry is not null && _unitDataRegistry.HasUnit(completedType))
            factionId = _unitDataRegistry.GetUnitData(completedType).FactionId;

        // Spawn at rally point
        FixedVector2 spawnPos = _building.RallyPoint;
        UnitNode3D? unit = _unitSpawner.SpawnUnit(
            completedType,
            factionId,
            spawnPos,
            FixedPoint.Zero);

        if (unit is not null)
        {
            GD.Print($"[ProductionQueue] Produced {completedType} for player {_building.PlayerId}.");
            EventBus.Instance?.EmitProductionCompleted(_building, completedType);
        }
    }
}
