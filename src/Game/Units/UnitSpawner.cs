using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Assets;

namespace CorditeWars.Game.Units;

/// <summary>
/// Factory that creates <see cref="UnitNode3D"/> instances from registry data.
/// Manages the lifecycle of all active units and emits EventBus signals.
/// Units are stored in a <see cref="SortedList{TKey,TValue}"/> keyed by ascending
/// integer ID for deterministic iteration.
/// </summary>
public partial class UnitSpawner : Node
{
    private readonly AssetRegistry _assetRegistry;
    private readonly UnitDataRegistry _unitDataRegistry;
    private readonly SortedList<string, Color> _factionColors;

    private int _nextUnitId = 1;
    private readonly SortedList<int, UnitNode3D> _activeUnits = new();
    private Node3D? _unitsParent;

    public UnitSpawner(
        AssetRegistry assetRegistry,
        UnitDataRegistry unitDataRegistry,
        SortedList<string, Color> factionColors)
    {
        _assetRegistry = assetRegistry;
        _unitDataRegistry = unitDataRegistry;
        _factionColors = factionColors;
    }

    public override void _Ready()
    {
        _unitsParent = new Node3D();
        _unitsParent.Name = "Units";
        AddChild(_unitsParent);
    }

    /// <summary>
    /// Creates a new unit at the given simulation position and facing.
    /// Looks up data from registries, creates the visual node, and emits UnitSpawned.
    /// </summary>
    public UnitNode3D? SpawnUnit(
        string unitTypeId,
        string factionId,
        int playerId,
        FixedVector2 position,
        FixedPoint facing)
    {
        if (!_unitDataRegistry.HasUnit(unitTypeId))
        {
            GD.PushWarning($"[UnitSpawner] Unit type '{unitTypeId}' not found in UnitDataRegistry.");
            return null;
        }

        if (!_assetRegistry.HasEntry(unitTypeId))
        {
            GD.PushWarning($"[UnitSpawner] Asset entry '{unitTypeId}' not found in AssetRegistry.");
            return null;
        }

        UnitData data = _unitDataRegistry.GetUnitData(unitTypeId);
        AssetEntry asset = _assetRegistry.GetEntry(unitTypeId);

        Color teamColor = Colors.White;
        if (_factionColors.TryGetValue(factionId, out Color color))
        {
            teamColor = color;
        }

        int unitId = _nextUnitId++;

        var unitNode = new UnitNode3D();
        unitNode.Initialize(unitId, unitTypeId, data, asset, teamColor, playerId);
        unitNode.SyncFromSimulation(position, facing, data.MaxHealth);

        if (_unitsParent is not null)
        {
            _unitsParent.AddChild(unitNode);
        }

        _activeUnits.Add(unitId, unitNode);

        EventBus? bus = EventBus.Instance;
        if (bus is not null)
        {
            bus.EmitUnitSpawned(unitNode);
        }

        GD.Print($"[UnitSpawner] Spawned unit #{unitId} '{unitTypeId}' for faction '{factionId}'.");
        return unitNode;
    }

    /// <summary>
    /// Removes and destroys a unit by its instance ID.
    /// </summary>
    public void DespawnUnit(int unitId)
    {
        if (!_activeUnits.TryGetValue(unitId, out UnitNode3D? unitNode))
        {
            GD.PushWarning($"[UnitSpawner] Cannot despawn unit #{unitId} — not found.");
            return;
        }

        _activeUnits.Remove(unitId);

        EventBus? bus = EventBus.Instance;
        if (bus is not null)
        {
            bus.EmitUnitDestroyed(unitNode);
        }

        unitNode.Die();
    }

    /// <summary>
    /// Returns the unit node for the given ID, or null if not found.
    /// </summary>
    public UnitNode3D? GetUnit(int unitId)
    {
        if (_activeUnits.TryGetValue(unitId, out UnitNode3D? node))
            return node;
        return null;
    }

    /// <summary>
    /// Returns all active units in ascending ID order.
    /// </summary>
    public IList<UnitNode3D> GetAllUnits()
    {
        return _activeUnits.Values;
    }

    /// <summary>Number of active units.</summary>
    public int ActiveCount => _activeUnits.Count;
}
