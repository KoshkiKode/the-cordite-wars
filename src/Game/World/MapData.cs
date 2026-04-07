using CorditeWars.Core;

namespace CorditeWars.Game.World;

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
    public string NodeId { get; init; } = string.Empty;
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
}
