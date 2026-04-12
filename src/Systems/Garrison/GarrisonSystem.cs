using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Units;

namespace CorditeWars.Systems.Garrison;

/// <summary>
/// Per-building garrison state: which units are inside and their metadata.
/// </summary>
public sealed class GarrisonSlot
{
    public int BuildingId { get; init; }
    public int OwnerId   { get; init; }
    public int Capacity  { get; init; }
    public int DefenseBonus { get; init; }

    private readonly List<int> _occupants = new();

    /// <summary>IDs of units currently garrisoned in this building.</summary>
    public IReadOnlyList<int> Occupants => _occupants;

    /// <summary>Number of units currently inside.</summary>
    public int Count => _occupants.Count;

    /// <summary>True if the building can accept more units.</summary>
    public bool HasSpace => _occupants.Count < Capacity;

    /// <summary>Adds a unit to this garrison. Returns true on success.</summary>
    public bool Add(int unitId)
    {
        if (!HasSpace || _occupants.Contains(unitId)) return false;
        _occupants.Add(unitId);
        return true;
    }

    /// <summary>Removes a unit from the garrison. Returns true if it was there.</summary>
    public bool Remove(int unitId) => _occupants.Remove(unitId);

    /// <summary>Removes all units (e.g., building destroyed).</summary>
    public void Clear() => _occupants.Clear();
}

/// <summary>
/// Manages all garrisons in the current match. Tracks which buildings accept
/// infantry, which units are inside, and applies defense bonuses.
///
/// Integration:
///   - <see cref="RegisterBuilding"/> adds a garrison slot for buildings with
///     <see cref="BuildingData.GarrisonCapacity"/> &gt; 0.
///   - <see cref="TryGarrison"/> moves a unit into a building.
///   - <see cref="TryEject"/> removes a unit from a building.
///   - <see cref="OnBuildingDestroyed"/> ejects all units from a destroyed building.
///   - The simulation tick calls <see cref="GetDefenseMultiplier"/> to reduce
///     incoming damage for garrisoned units.
///
/// This system is intentionally side-effect-free (no scene mutations).
/// The HUD reads <see cref="GetGarrisonForBuilding"/> to render occupancy.
/// </summary>
public sealed class GarrisonSystem
{
    // Building ID → garrison state
    private readonly Dictionary<int, GarrisonSlot> _garrisons = new();

    // Unit ID → building they are garrisoned in
    private readonly Dictionary<int, int> _unitToBuilding = new();

    // ── Registration ─────────────────────────────────────────────────

    /// <summary>
    /// Registers a building as a garrison point if its data specifies capacity.
    /// </summary>
    public void RegisterBuilding(BuildingInstance building)
    {
        if (building.Data is null || building.Data.GarrisonCapacity <= 0) return;

        RegisterBuilding(building.BuildingId, building.PlayerId,
            building.Data.GarrisonCapacity, building.Data.GarrisonDefenseBonus);
    }

    /// <summary>
    /// Registers a building using raw parameters. Used when a
    /// <see cref="BuildingInstance"/> node is not available (e.g., in tests).
    /// </summary>
    public void RegisterBuilding(int buildingId, int ownerId, int capacity, int defenseBonus)
    {
        if (capacity <= 0) return;

        _garrisons[buildingId] = new GarrisonSlot
        {
            BuildingId   = buildingId,
            OwnerId      = ownerId,
            Capacity     = capacity,
            DefenseBonus = defenseBonus
        };
    }

    /// <summary>
    /// Removes garrison state for a destroyed building and ejects all occupants.
    /// Ejected unit IDs are returned so the simulation can re-spawn them adjacent
    /// to the building's position.
    /// </summary>
    public List<int> OnBuildingDestroyed(int buildingId)
    {
        var ejected = new List<int>();
        if (!_garrisons.TryGetValue(buildingId, out var slot)) return ejected;

        for (int i = 0; i < slot.Occupants.Count; i++)
        {
            int uid = slot.Occupants[i];
            ejected.Add(uid);
            _unitToBuilding.Remove(uid);
        }
        slot.Clear();
        _garrisons.Remove(buildingId);
        return ejected;
    }

    // ── Garrison / Eject ─────────────────────────────────────────────

    /// <summary>
    /// Attempts to garrison <paramref name="unitId"/> in <paramref name="buildingId"/>.
    /// Returns <c>true</c> on success.
    ///
    /// Prerequisites (not checked here — caller must validate):
    ///   - Unit is infantry (Category == Infantry).
    ///   - Unit and building belong to the same player.
    ///   - Unit is close enough to the building.
    /// </summary>
    public bool TryGarrison(int unitId, int buildingId)
    {
        if (_unitToBuilding.ContainsKey(unitId)) return false; // already garrisoned
        if (!_garrisons.TryGetValue(buildingId, out var slot)) return false;
        if (!slot.HasSpace) return false;

        slot.Add(unitId);
        _unitToBuilding[unitId] = buildingId;
        return true;
    }

    /// <summary>
    /// Ejects <paramref name="unitId"/> from its current garrison.
    /// Returns <c>true</c> if the unit was garrisoned.
    /// </summary>
    public bool TryEject(int unitId)
    {
        if (!_unitToBuilding.TryGetValue(unitId, out int buildingId)) return false;
        _unitToBuilding.Remove(unitId);

        if (_garrisons.TryGetValue(buildingId, out var slot))
            slot.Remove(unitId);

        return true;
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <c>GarrisonSlot</c> for a building, or null if none.
    /// </summary>
    public GarrisonSlot? GetGarrisonForBuilding(int buildingId)
        => _garrisons.TryGetValue(buildingId, out var slot) ? slot : null;

    /// <summary>Returns true if the unit is currently garrisoned.</summary>
    public bool IsGarrisoned(int unitId) => _unitToBuilding.ContainsKey(unitId);

    /// <summary>Returns the building ID the unit is in, or -1.</summary>
    public int GetGarrisonBuilding(int unitId)
        => _unitToBuilding.TryGetValue(unitId, out int bid) ? bid : -1;

    /// <summary>
    /// Returns the damage multiplier for a garrisoned unit.
    /// 1.0 = full damage (not garrisoned).
    /// E.g., 0.5 = 50% damage reduction (DefenseBonus == 50).
    /// </summary>
    public FixedPoint GetDefenseMultiplier(int unitId)
    {
        if (!_unitToBuilding.TryGetValue(unitId, out int buildingId)) return FixedPoint.One;
        if (!_garrisons.TryGetValue(buildingId, out var slot)) return FixedPoint.One;

        // multiplier = 1.0 - (DefenseBonus / 100)
        int bonus = System.Math.Clamp(slot.DefenseBonus, 0, 100);
        return FixedPoint.One - FixedPoint.FromFloat(bonus / 100f);
    }

    /// <summary>Returns all registered garrison buildings.</summary>
    public IReadOnlyDictionary<int, GarrisonSlot> AllGarrisons => _garrisons;

    /// <summary>Returns all unit-to-building mappings (garrisoned units).</summary>
    public IReadOnlyDictionary<int, int> GarrisonedUnits => _unitToBuilding;
}
