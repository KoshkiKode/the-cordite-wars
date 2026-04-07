namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Metadata for a save slot displayed in the load-game UI.
/// </summary>
public sealed class SaveSlotInfo
{
    public string SlotName { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string MapDisplayName { get; init; } = string.Empty;
    public ulong CurrentTick { get; init; }
    public string SaveTimestamp { get; init; } = string.Empty;
    public int PlayerCount { get; init; }
    public string Version { get; init; } = string.Empty;
}
