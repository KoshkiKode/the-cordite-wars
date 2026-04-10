using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Player summary stored in a replay header.
/// </summary>
public sealed class ReplayPlayerInfo
{
    [JsonPropertyName("player_id")]   public int    PlayerId   { get; init; }
    [JsonPropertyName("faction_id")]  public string FactionId  { get; init; } = string.Empty;
    [JsonPropertyName("player_name")] public string PlayerName { get; init; } = string.Empty;
    [JsonPropertyName("is_ai")]       public bool   IsAI       { get; init; }
}

/// <summary>
/// A single recorded command entry inside a replay file.
/// Sufficient to reconstruct the full command stream for playback.
/// </summary>
public sealed class ReplayCommandEntry
{
    /// <summary>Simulation tick the command was issued on.</summary>
    [JsonPropertyName("tick")]       public ulong  Tick       { get; init; }

    /// <summary>Player who issued the command.</summary>
    [JsonPropertyName("player_id")]  public int    PlayerId   { get; init; }

    /// <summary>String name of the <c>CommandType</c> enum value.</summary>
    [JsonPropertyName("type")]       public string Type       { get; init; } = string.Empty;

    /// <summary>Target world X position (float; not used for simulation replay — informational only).</summary>
    [JsonPropertyName("target_x")]   public float  TargetX    { get; init; }

    /// <summary>Target world Z position mapped to Y in 2D grid space.</summary>
    [JsonPropertyName("target_z")]   public float  TargetZ    { get; init; }

    /// <summary>Unit IDs involved in the command.</summary>
    [JsonPropertyName("unit_ids")]   public int[]  UnitIds    { get; init; } = [];

    /// <summary>Target unit ID (for attack commands). -1 if none.</summary>
    [JsonPropertyName("target_unit_id")] public int TargetUnitId { get; init; } = -1;
}

/// <summary>
/// Full replay document saved to <c>user://replays/{timestamp}.json</c>.
///
/// <para>
/// A replay is a complete record of the command stream for a match.
/// Because the simulation is deterministic (FixedPoint arithmetic, seeded RNG,
/// sorted iteration), replaying all commands from the same seed reproduces
/// the identical match state on any client.
/// </para>
///
/// <para>
/// Use <see cref="ReplayManager"/> to record and save replays automatically.
/// </para>
/// </summary>
public sealed class ReplayData
{
    [JsonPropertyName("version")]         public string Version        { get; init; } = "0.1.0";
    [JsonPropertyName("save_timestamp")]  public string SaveTimestamp  { get; init; } = string.Empty;
    [JsonPropertyName("map_id")]          public string MapId          { get; init; } = string.Empty;
    [JsonPropertyName("match_seed")]      public ulong  MatchSeed      { get; init; }
    [JsonPropertyName("total_ticks")]     public ulong  TotalTicks     { get; init; }
    [JsonPropertyName("duration_seconds")] public double DurationSeconds { get; init; }
    [JsonPropertyName("winner_player_id")] public int   WinnerPlayerId { get; init; } = -1;

    /// <summary>Optional campaign context (null for skirmish / multiplayer).</summary>
    [JsonPropertyName("mission_id")]      public string? MissionId     { get; init; }

    [JsonPropertyName("players")]         public ReplayPlayerInfo[]   Players  { get; init; } = [];
    [JsonPropertyName("commands")]        public List<ReplayCommandEntry> Commands { get; init; } = new();
}
