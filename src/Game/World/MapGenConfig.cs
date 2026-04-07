namespace CorditeWars.Game.World;

/// <summary>
/// Configuration parameters for procedural map generation.
/// All values have sane defaults so callers only need to set what they want to customize.
/// </summary>
public sealed class MapGenConfig
{
    /// <summary>Map width in grid cells. Must be ≥ 64.</summary>
    public int Width { get; init; } = 200;

    /// <summary>Map height in grid cells. Must be ≥ 64.</summary>
    public int Height { get; init; } = 200;

    /// <summary>Number of player starting positions (2–6).</summary>
    public int PlayerCount { get; init; } = 2;

    /// <summary>
    /// Biome theme. Supported: "temperate", "desert", "rocky", "coastal", "archipelago", "volcanic".
    /// </summary>
    public string Biome { get; init; } = "temperate";

    /// <summary>
    /// Deterministic seed. The same seed + config always produces the same map.
    /// </summary>
    public ulong Seed { get; init; } = 42;

    /// <summary>
    /// Relative density of decorative props (trees, rocks). Range [0.0, 1.0].
    /// 0 = barren, 1 = heavily forested/covered.
    /// </summary>
    public double PropDensity { get; init; } = 0.5;

    /// <summary>
    /// Number of cordite nodes per player. Each player's base area gets this many nodes,
    /// plus additional contested nodes in the center.
    /// </summary>
    public int CorditeNodesPerPlayer { get; init; } = 3;

    /// <summary>
    /// Number of elevation zones to place. Higher = more terrain variation.
    /// </summary>
    public int ElevationZoneCount { get; init; } = 6;

    /// <summary>
    /// Whether to generate rivers. Forced off for desert/volcanic biomes if false.
    /// </summary>
    public bool GenerateRivers { get; init; } = true;

    /// <summary>
    /// Whether to generate connecting paths between starting positions.
    /// </summary>
    public bool GeneratePaths { get; init; } = true;
}
