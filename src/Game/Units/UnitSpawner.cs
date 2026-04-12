using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Systems.Pathfinding;

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
    private readonly SortedList<string, Color> _factionBaseColors;
    private TerrainGrid? _terrainGrid;

    private int _nextUnitId = 1;
    private readonly SortedList<int, UnitNode3D> _activeUnits = new();
    private Node3D? _unitsParent;

    public UnitSpawner(
        AssetRegistry assetRegistry,
        UnitDataRegistry unitDataRegistry,
        SortedList<string, Color> factionColors,
        SortedList<string, Color> factionBaseColors)
    {
        _assetRegistry = assetRegistry;
        _unitDataRegistry = unitDataRegistry;
        _factionColors = factionColors;
        _factionBaseColors = factionBaseColors;
    }

    public override void _Ready()
    {
        _unitsParent = new Node3D();
        _unitsParent.Name = "Units";
        AddChild(_unitsParent);
    }

    /// <summary>
    /// Provides the terrain grid so naval units can be relocated to a valid
    /// water spawn point if the requested spawn position is on land.
    /// </summary>
    public void SetTerrainGrid(TerrainGrid terrainGrid)
    {
        _terrainGrid = terrainGrid;
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
        int unitId = _nextUnitId++;
        return SpawnUnitCore(unitId, unitTypeId, factionId, playerId, position, facing, health: null);
    }

    /// <summary>
    /// Restores a unit from a save using its original ID and health value.
    /// Advances <see cref="_nextUnitId"/> past <paramref name="unitId"/> so that
    /// future auto-assigned IDs never collide with saved IDs.
    /// </summary>
    public UnitNode3D? SpawnUnitWithId(
        int unitId,
        string unitTypeId,
        string factionId,
        int playerId,
        FixedVector2 position,
        FixedPoint facing,
        FixedPoint health)
    {
        if (unitId >= _nextUnitId)
            _nextUnitId = unitId + 1;

        return SpawnUnitCore(unitId, unitTypeId, factionId, playerId, position, facing, health);
    }

    private UnitNode3D? SpawnUnitCore(
        int unitId,
        string unitTypeId,
        string factionId,
        int playerId,
        FixedVector2 position,
        FixedPoint facing,
        FixedPoint? health)
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

        Color factionBaseColor = Colors.White;
        if (_factionBaseColors.TryGetValue(factionId, out Color baseColor))
        {
            factionBaseColor = baseColor;
        }

        // For naval units, relocate the spawn position to the nearest water cell
        // if the requested position is not already on Water/DeepWater.
        if (data.MovementClassId == "Naval" && _terrainGrid != null)
        {
            position = FindNearestWaterSpawn(position);
        }

        FixedPoint spawnHealth = health ?? data.MaxHealth;

        var unitNode = new UnitNode3D();
        unitNode.Initialize(unitId, unitTypeId, data, asset, teamColor, factionBaseColor, playerId);
        unitNode.SyncFromSimulation(position, facing, spawnHealth);

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

    /// <summary>
    /// Finds the nearest Water or DeepWater cell to <paramref name="origin"/>.
    /// Uses a BFS-like expanding search up to 30 cells radius.
    /// Returns the original position if no water cell is found or terrain is unavailable.
    /// </summary>
    private FixedVector2 FindNearestWaterSpawn(FixedVector2 origin)
    {
        if (_terrainGrid is null) return origin;

        (int gx, int gy) = _terrainGrid.WorldToGrid(origin);

        // Check origin cell first
        if (_terrainGrid.IsInBounds(gx, gy))
        {
            TerrainCell c = _terrainGrid.GetCellSafe(gx, gy);
            if (c.Type == TerrainType.Water || c.Type == TerrainType.DeepWater)
                return origin;
        }

        // Expanding ring search
        const int MaxRadius = 30;
        for (int r = 1; r <= MaxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue; // ring only

                    int nx = gx + dx;
                    int ny = gy + dy;
                    if (!_terrainGrid.IsInBounds(nx, ny)) continue;

                    TerrainCell nc = _terrainGrid.GetCellSafe(nx, ny);
                    if (nc.Type == TerrainType.Water || nc.Type == TerrainType.DeepWater)
                        return _terrainGrid.GridToWorld(nx, ny);
                }
            }
        }

        GD.PushWarning($"[UnitSpawner] No water cell found within {MaxRadius} cells of {origin} — spawning at original position.");
        return origin;
    }
}
