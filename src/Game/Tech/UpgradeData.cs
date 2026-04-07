using CorditeWars.Core;

namespace CorditeWars.Game.Tech;

/// <summary>
/// A single stat modification applied when an upgrade completes.
/// </summary>
public sealed class UpgradeEffect
{
    /// <summary>
    /// Target unit category: "Infantry", "Tank", "Jet", "All", or a specific unit ID.
    /// </summary>
    public string TargetCategory { get; init; } = string.Empty;

    /// <summary>
    /// Stat to modify: "Damage", "Armor", "MaxHealth", "Speed", "Range", "SightRange", "BuildTime", etc.
    /// </summary>
    public string Stat { get; init; } = string.Empty;

    /// <summary>
    /// Modifier type: "add" (additive to base), "multiply" (multiplicative), "add_flat" (flat additive).
    /// </summary>
    public string Modifier { get; init; } = string.Empty;

    /// <summary>
    /// The modification value. For "multiply", 1.1 means +10%. For "add"/"add_flat", the raw amount.
    /// </summary>
    public FixedPoint Value { get; init; }
}

/// <summary>
/// Static data definition for a single tech upgrade loaded from JSON.
/// </summary>
public sealed class UpgradeData
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FactionId { get; init; } = string.Empty;
    public int Tier { get; init; }
    public int Cost { get; init; }
    public int SecondaryCost { get; init; }
    public FixedPoint ResearchTime { get; init; }
    public string PrerequisiteBuilding { get; init; } = string.Empty;
    public string[] PrerequisiteUpgrades { get; init; } = System.Array.Empty<string>();
    public UpgradeEffect[] Effects { get; init; } = System.Array.Empty<UpgradeEffect>();
    public string Description { get; init; } = string.Empty;
}
