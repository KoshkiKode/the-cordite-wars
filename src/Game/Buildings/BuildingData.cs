using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.Buildings;

/// <summary>
/// Broad building classification. Drives build-menu grouping,
/// AI placement logic, and targeting priority.
/// </summary>
public enum BuildingCategory
{
    Production,
    Economy,
    Defense,
    Tech,
    Utility,
    Superweapon
}

/// <summary>
/// Immutable data object describing a building template, loaded from JSON.
/// Like <see cref="UnitData"/>, instances are shared across all buildings
/// of the same type; per-instance runtime state lives elsewhere.
/// </summary>
public sealed class BuildingData
{
    // ── Identity ─────────────────────────────────────────────────────

    /// <summary>Unique identifier (e.g., "valkyr_airfield").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Owning faction ID.</summary>
    public string FactionId { get; init; } = string.Empty;

    /// <summary>Broad building classification.</summary>
    public BuildingCategory Category { get; init; }

    // ── Core Stats ───────────────────────────────────────────────────

    /// <summary>Maximum hit points.</summary>
    public FixedPoint MaxHealth { get; init; }

    /// <summary>Armor classification for incoming-damage modifiers.</summary>
    public ArmorType ArmorClass { get; init; } = ArmorType.Building;

    /// <summary>Flat damage reduction per hit.</summary>
    public FixedPoint ArmorValue { get; init; }

    /// <summary>Primary resource cost.</summary>
    public int Cost { get; init; }

    /// <summary>Secondary resource cost.</summary>
    public int SecondaryCost { get; init; }

    /// <summary>Seconds to construct.</summary>
    public FixedPoint BuildTime { get; init; }

    // ── Footprint ────────────────────────────────────────────────────

    /// <summary>Grid footprint width (most buildings are 3×3 or 4×4).</summary>
    public int FootprintWidth { get; init; } = 3;

    /// <summary>Grid footprint height.</summary>
    public int FootprintHeight { get; init; } = 3;

    // ── Combat ───────────────────────────────────────────────────────

    /// <summary>
    /// Weapon hardpoints for turrets / defenses.
    /// Null for non-combat buildings.
    /// </summary>
    public List<WeaponData>? Weapons { get; init; }

    /// <summary>Vision range in grid cells.</summary>
    public FixedPoint SightRange { get; init; }

    // ── Economy ───────────────────────────────────────────────────────

    /// <summary>Passive Cordite income per second (e.g., Bastion refinery bonus).</summary>
    public FixedPoint PassiveIncome { get; init; }

    /// <summary>Voltaic Charge generated per second (reactors).</summary>
    public FixedPoint VCGeneration { get; init; }

    /// <summary>Supply cap increase when this building is active.</summary>
    public int SupplyProvided { get; init; }

    // ── Power ────────────────────────────────────────────────────────

    /// <summary>
    /// Positive = generates power, negative = consumes power.
    /// </summary>
    public FixedPoint PowerGenerated { get; init; }

    // ── Tech / Unlock Graph ──────────────────────────────────────────

    /// <summary>Unit IDs that become available when this building is built.</summary>
    public List<string> UnlocksUnitIds { get; init; } = new();

    /// <summary>Building IDs that become available when this building is built.</summary>
    public List<string> UnlocksBuildingIds { get; init; } = new();

    /// <summary>Building IDs required before this building can be placed.</summary>
    public List<string> Prerequisites { get; init; } = new();

    // ── Flags ────────────────────────────────────────────────────────

    /// <summary>Enables minimap radar vision when constructed.</summary>
    public bool ProvidesRadar { get; init; }

    /// <summary>
    /// Shipyard and coastal buildings require placement adjacent to at least
    /// one Water or DeepWater cell.  The <see cref="BuildingPlacer"/> enforces
    /// this constraint at placement time.
    /// </summary>
    public bool RequiresWaterAccess { get; init; }

    // ── Flavor ───────────────────────────────────────────────────────

    /// <summary>Lore / flavor text.</summary>
    public string Description { get; init; } = string.Empty;
}
