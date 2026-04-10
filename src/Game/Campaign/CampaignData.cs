using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CorditeWars.Game.Campaign;

/// <summary>
/// Victory condition variants for a match or campaign mission.
/// </summary>
public enum WinCondition
{
    /// <summary>Default: destroy all enemy Command Centres (HQ buildings).</summary>
    DestroyHQ,

    /// <summary>Eliminate all enemy mobile units (no buildings required).</summary>
    KillAllUnits
}

/// <summary>
/// A single campaign mission definition, loaded from <c>data/campaign/{faction}.json</c>.
/// </summary>
public sealed class CampaignMission
{
    [JsonPropertyName("id")]          public string Id            { get; set; } = string.Empty;
    [JsonPropertyName("number")]      public int    Number        { get; set; }
    [JsonPropertyName("name")]        public string Name          { get; set; } = string.Empty;
    [JsonPropertyName("briefing")]    public string Briefing      { get; set; } = string.Empty;
    [JsonPropertyName("map")]         public string MapId         { get; set; } = string.Empty;
    [JsonPropertyName("player_faction")] public string PlayerFaction { get; set; } = string.Empty;
    [JsonPropertyName("enemy_factions")] public List<string> EnemyFactions { get; set; } = new();
    [JsonPropertyName("objectives")]  public List<string> Objectives { get; set; } = new();
    [JsonPropertyName("starting_cordite")] public int StartingCordite { get; set; } = 3000;
    [JsonPropertyName("ai_difficulty")]    public int AiDifficulty    { get; set; } = 1;
    [JsonPropertyName("difficulty_label")] public string DifficultyLabel { get; set; } = "Normal";
    [JsonPropertyName("twist")]            public string Twist          { get; set; } = string.Empty;
    [JsonPropertyName("unlocks_mission")]  public int?   UnlocksMission { get; set; }

    /// <summary>
    /// Serialised win-condition identifier.
    /// <c>"destroy_hq"</c> (default) or <c>"kill_all_units"</c>.
    /// </summary>
    [JsonPropertyName("win_condition")] public string WinConditionId { get; set; } = "destroy_hq";

    /// <summary>Resolved <see cref="WinCondition"/> from <see cref="WinConditionId"/>.</summary>
    [JsonIgnore]
    public WinCondition WinCondition => WinConditionId switch
    {
        "kill_all_units" => WinCondition.KillAllUnits,
        _                => WinCondition.DestroyHQ
    };
}

/// <summary>
/// A faction campaign — a named sequence of missions.
/// Loaded from <c>data/campaign/{faction}.json</c>.
/// </summary>
public sealed class FactionCampaign
{
    [JsonPropertyName("faction")]     public string FactionId     { get; set; } = string.Empty;
    [JsonPropertyName("name")]        public string CampaignName  { get; set; } = string.Empty;
    [JsonPropertyName("commander")]   public string Commander     { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description   { get; set; } = string.Empty;
    [JsonPropertyName("missions")]    public List<CampaignMission> Missions { get; set; } = new();
}
