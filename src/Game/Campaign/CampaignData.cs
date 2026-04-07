using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CorditeWars.Game.Campaign;

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
