using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Game.Factions;

/// <summary>
/// High-level play-style archetype for a faction.
/// Determines AI behavior hints and matchmaking balance tags.
/// </summary>
public enum FactionArchetype
{
    AirPrimary,
    GroundPrimary,
    DefensePrimary,
    AirDefense,
    GroundDefense,
    AirGroundOffense
}

/// <summary>
/// Immutable data object describing a faction, loaded from JSON at runtime.
/// Contains economic modifiers, tech tree info, and references to the
/// units/buildings the faction can produce.
/// </summary>
public sealed class FactionData
{
    // ── Identity ─────────────────────────────────────────────────────

    /// <summary>Unique identifier (e.g., "valkyr", "ironpact").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Play-style archetype for this faction.</summary>
    public FactionArchetype Archetype { get; init; }

    /// <summary>Lore / flavor text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Visual style tag (e.g., "sleek_hightech", "heavy_industrial").</summary>
    public string AestheticTheme { get; init; } = string.Empty;

    /// <summary>Player color slot index (0–7).</summary>
    public int ColorIndex { get; init; }

    /// <summary>
    /// Faction primary color as a hex string (e.g., "#2196F3").
    /// Used as the base tint on faction unit models and UI elements.
    /// </summary>
    public string PrimaryColor { get; init; } = "#FFFFFF";

    /// <summary>
    /// Faction secondary/shadow color as a hex string.
    /// Used for panel borders, darker model accents, and UI highlights.
    /// </summary>
    public string SecondaryColor { get; init; } = "#AAAAAA";

    /// <summary>
    /// Faction accent/highlight color as a hex string.
    /// Used for emissive details, UI accent elements, and bright model trim.
    /// </summary>
    public string AccentColor { get; init; } = "#FFFFFF";

    // ── Economic Modifiers ───────────────────────────────────────────

    /// <summary>Multiplier on harvester speed (1.0 = normal).</summary>
    public FixedPoint HarvesterSpeedMod { get; init; } = FixedPoint.One;

    /// <summary>Building construction speed multiplier.</summary>
    public FixedPoint BuildSpeedMod { get; init; } = FixedPoint.One;

    /// <summary>Unit training speed multiplier.</summary>
    public FixedPoint UnitBuildSpeedMod { get; init; } = FixedPoint.One;

    /// <summary>Resource income multiplier.</summary>
    public FixedPoint IncomeMod { get; init; } = FixedPoint.One;

    /// <summary>Cost multiplier for defensive structures (&lt; 1.0 = cheaper).</summary>
    public FixedPoint DefenseCostMod { get; init; } = FixedPoint.One;

    /// <summary>Cost multiplier for air units.</summary>
    public FixedPoint AirUnitCostMod { get; init; } = FixedPoint.One;

    /// <summary>Cost multiplier for ground units.</summary>
    public FixedPoint GroundUnitCostMod { get; init; } = FixedPoint.One;

    /// <summary>How fast buildings/units auto-repair.</summary>
    public FixedPoint RepairRateMod { get; init; } = FixedPoint.One;

    // ── Faction Mechanic ─────────────────────────────────────────────

    /// <summary>Unique mechanic identifier (loaded separately).</summary>
    public string FactionMechanicId { get; init; } = string.Empty;

    // ── Available Roster ─────────────────────────────────────────────

    /// <summary>Unit IDs this faction can build.</summary>
    public List<string> AvailableUnitIds { get; init; } = new();

    /// <summary>Building IDs this faction can construct.</summary>
    public List<string> AvailableBuildingIds { get; init; } = new();

    // ── Tech Tree ────────────────────────────────────────────────────

    /// <summary>Tech research costs keyed by upgrade ID.</summary>
    public Dictionary<string, FixedPoint> TechTreeUnlocks { get; init; } = new();
}
