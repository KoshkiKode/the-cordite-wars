namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Complete save game state. All FixedPoint simulation values are stored
/// as raw long values for lossless round-trip serialization.
/// </summary>
public sealed class SaveGameData
{
    public string Version { get; init; } = "0.1.0";
    public int ProtocolVersion { get; init; } = 1;
    public string SaveTimestamp { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public ulong MatchSeed { get; init; }
    public ulong CurrentTick { get; init; }

    // Match settings for full config reconstruction
    public int GameSpeed { get; init; } = 1;
    public bool FogOfWar { get; init; } = true;
    public int StartingCordite { get; init; } = 5000;

    public PlayerSaveData[] Players { get; init; } = [];
    public UnitSaveData[] Units { get; init; } = [];
    public BuildingSaveData[] Buildings { get; init; } = [];
    public HarvesterSaveData[] Harvesters { get; init; } = [];
    public CorditeNodeSaveData[] CorditeNodes { get; init; } = [];

    // Full xoshiro256** state (4 ulongs) for lossless RNG restoration
    public ulong RngState0 { get; init; }
    public ulong RngState1 { get; init; }
    public ulong RngState2 { get; init; }
    public ulong RngState3 { get; init; }

    public SavedCommand[] CommandHistory { get; init; } = [];
}

/// <summary>
/// Per-player snapshot for save/load.
/// </summary>
public sealed class PlayerSaveData
{
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public bool IsAI { get; init; }
    public int AIDifficulty { get; init; }
    public long Cordite { get; init; }
    public long VoltaicCharge { get; init; }
    public int CurrentSupply { get; init; }
    public int MaxSupply { get; init; }
    public int ReactorCount { get; init; }
    public int RefineryCount { get; init; }
    public int DepotCount { get; init; }
    public string[] CompletedUpgrades { get; init; } = [];
    public string? CurrentResearch { get; init; }
    public long ResearchProgress { get; init; }
    public string[] CompletedBuildings { get; init; } = [];
}

/// <summary>
/// Per-unit snapshot. Positions and stats stored as raw FixedPoint longs.
/// </summary>
public sealed class UnitSaveData
{
    public int UnitId { get; init; }
    public string UnitTypeId { get; init; } = string.Empty;
    public int PlayerId { get; init; }
    public long PositionX { get; init; }
    public long PositionY { get; init; }
    public long Facing { get; init; }
    public long Health { get; init; }
    public bool IsAlive { get; init; }
    public string CurrentOrderType { get; init; } = string.Empty;
    public long TargetX { get; init; }
    public long TargetY { get; init; }
    public int TargetUnitId { get; init; } = -1;
}

/// <summary>
/// Per-building snapshot. Grid positions are ints; health is raw FixedPoint long.
/// </summary>
public sealed class BuildingSaveData
{
    public int BuildingId { get; init; }
    public string BuildingTypeId { get; init; } = string.Empty;
    public int PlayerId { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public long Health { get; init; }
    public bool IsConstructed { get; init; }
    public long ConstructionProgress { get; init; }
    public ProductionQueueSaveData? ProductionQueue { get; init; }
}

/// <summary>
/// Snapshot of an in-progress production queue for a building.
/// </summary>
public sealed class ProductionQueueSaveData
{
    public string? CurrentUnitTypeId { get; init; }
    public long CurrentProgress { get; init; }
    public long CurrentBuildTime { get; init; }
    public string[] QueuedUnitTypeIds { get; init; } = [];
}

/// <summary>
/// Per-harvester snapshot layered on top of its UnitSaveData.
/// </summary>
public sealed class HarvesterSaveData
{
    public int UnitId { get; init; }
    public int PlayerId { get; init; }
    public string State { get; init; } = string.Empty;
    public int CorditeCarrying { get; init; }
    public int AssignedNodeId { get; init; }
    public int AssignedRefineryId { get; init; }
}

/// <summary>
/// Cordite resource node snapshot.
/// </summary>
public sealed class CorditeNodeSaveData
{
    public int NodeId { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public int RemainingCordite { get; init; }
}

/// <summary>
/// Lightweight command representation for the last N ticks of replay verification.
/// </summary>
public sealed class SavedCommand
{
    public ulong Tick { get; init; }
    public int PlayerId { get; init; }
    public string CommandType { get; init; } = string.Empty;
}
