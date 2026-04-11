using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CorditeWars.Game.Campaign;

/// <summary>
/// Structured (typed) objective that drives the <see cref="MissionObjectiveTracker"/>.
/// Loaded from <c>typed_objectives</c> in a campaign mission JSON.
/// </summary>
public sealed class TypedObjectiveData
{
    [JsonPropertyName("type")]      public string Type     { get; set; } = string.Empty;
    [JsonPropertyName("label")]     public string Label    { get; set; } = string.Empty;
    [JsonPropertyName("target_id")] public string TargetId { get; set; } = string.Empty;
    [JsonPropertyName("count")]     public int    Count    { get; set; } = 1;
    [JsonPropertyName("ticks")]     public int    Ticks    { get; set; }
    [JsonPropertyName("required")]  public bool   Required { get; set; } = true;
}

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

    /// <summary>Typed objectives for <see cref="MissionObjectiveTracker"/>.</summary>
    [JsonPropertyName("typed_objectives")] public List<TypedObjectiveData> TypedObjectives { get; set; } = new();

    /// <summary>Mission that must be completed before this one is unlocked.</summary>
    [JsonPropertyName("requires_mission")] public string RequiresMission { get; set; } = string.Empty;

    /// <summary>
    /// Building IDs newly unlocked for construction when this mission is started.
    /// These accumulate across the mission chain — later missions include all prior unlocks.
    /// An empty list means no new buildings are added beyond what was already available.
    /// </summary>
    [JsonPropertyName("unlocks_buildings")] public List<string> UnlocksBuildings { get; set; } = new();

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
