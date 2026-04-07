namespace CorditeWars.Game;

/// <summary>
/// Configuration for a single player slot in a match.
/// </summary>
public sealed class PlayerConfig
{
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;
    public bool IsAI { get; init; }
    public int AIDifficulty { get; init; }
    public string PlayerName { get; init; } = string.Empty;
}

/// <summary>
/// Configuration for starting a match. Passed to <see cref="GameSession.StartMatch"/>.
/// </summary>
public sealed class MatchConfig
{
    public string MapId { get; init; } = string.Empty;
    public PlayerConfig[] PlayerConfigs { get; init; } = [];
    public ulong MatchSeed { get; init; }
    public int GameSpeed { get; init; } = 1;
    public bool FogOfWar { get; init; } = true;
    public int StartingCordite { get; init; } = 5000;
}
