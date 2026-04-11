using CorditeWars.Core;

namespace CorditeWars.Game.World;

/// <summary>
/// Per-map sun/lighting configuration loaded from JSON.
/// When <see cref="Enabled"/> is false the map uses a flat overcast ambient light.
/// </summary>
public sealed class MapSunConfig
{
    /// <summary>Whether a directional sun light is active on this map.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Rotation around the X axis in degrees (controls altitude).</summary>
    public float RotationX { get; init; } = -55f;

    /// <summary>Rotation around the Y axis in degrees (controls compass bearing).</summary>
    public float RotationY { get; init; } = 30f;

    /// <summary>Hex colour string for the sun light (e.g. "#FFF4E0").</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>Sun light energy multiplier.</summary>
    public float Energy { get; init; } = 1.2f;

    /// <summary>Hex colour string for ambient fill light.</summary>
    public string AmbientColor { get; init; } = "#8899BB";

    /// <summary>Ambient light energy multiplier.</summary>
    public float AmbientEnergy { get; init; } = 0.45f;

    /// <summary>Hex colour string for the sky / background clear colour.</summary>
    public string SkyColor { get; init; } = "#1A2040";
}

/// <summary>
/// Starting position for a player on the map.
/// </summary>
public sealed class StartingPosition
{
    public int PlayerId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public FixedPoint Facing { get; init; }
}

/// <summary>
/// A cordite resource node placement on the map.
/// </summary>
public sealed class CorditeNodeData
{
    public int NodeId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Amount { get; init; }
}

/// <summary>
/// A terrain feature defined by a type and a set of polygon points.
/// </summary>
public sealed class TerrainFeature
{
    public string Type { get; init; } = string.Empty;
    public int[][] Points { get; init; } = [];
}

/// <summary>
/// Placement of a decorative or interactive prop on the map.
/// </summary>
public sealed class PropPlacement
{
    public string ModelId { get; init; } = string.Empty;
    public FixedPoint X { get; init; }
    public FixedPoint Y { get; init; }
    public FixedPoint Rotation { get; init; }
    public FixedPoint Scale { get; init; } = FixedPoint.One;
}

/// <summary>
/// Placement of a pre-built structure on the map.
/// </summary>
public sealed class StructurePlacement
{
    public string ModelId { get; init; } = string.Empty;
    public FixedPoint X { get; init; }
    public FixedPoint Y { get; init; }
    public FixedPoint Rotation { get; init; }
    public FixedPoint Scale { get; init; } = FixedPoint.One;
}

/// <summary>
/// An elevation zone that modifies terrain height in a circular area.
/// </summary>
public sealed class ElevationZone
{
    public string Type { get; init; } = string.Empty;
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public int Radius { get; init; }
    public FixedPoint Height { get; init; }
}

/// <summary>
/// Complete data for a single map loaded from JSON.
/// </summary>
public sealed class MapData
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public int MaxPlayers { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Biome { get; init; } = string.Empty;
    public StartingPosition[] StartingPositions { get; init; } = [];
    public CorditeNodeData[] CorditeNodes { get; init; } = [];
    public TerrainFeature[] TerrainFeatures { get; init; } = [];
    public PropPlacement[] Props { get; init; } = [];
    public StructurePlacement[] Structures { get; init; } = [];
    public ElevationZone[] ElevationZones { get; init; } = [];

    /// <summary>
    /// Optional per-map sun / ambient lighting config.
    /// When null, <see cref="MapSunConfig"/> defaults are used.
    /// </summary>
    public MapSunConfig? SunConfig { get; init; }
}
