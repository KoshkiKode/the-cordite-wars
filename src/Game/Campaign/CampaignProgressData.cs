using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CorditeWars.Game.Campaign;

/// <summary>
/// Stores progress for a single faction's campaign: which missions have been
/// completed and how many stars were earned on each.
/// </summary>
public sealed class FactionCampaignProgress
{
    /// <summary>Faction identifier (e.g. "arcloft").</summary>
    [JsonPropertyName("faction_id")]
    public string FactionId { get; set; } = string.Empty;

    /// <summary>
    /// Set of mission IDs the player has completed at least once
    /// (e.g. "arcloft_01", "arcloft_02").
    /// </summary>
    [JsonPropertyName("completed_missions")]
    public List<string> CompletedMissions { get; set; } = new();

    /// <summary>
    /// Number of stars earned per mission (1–3). Key = mission ID.
    /// A mission not yet completed will not appear here.
    /// </summary>
    [JsonPropertyName("mission_stars")]
    public Dictionary<string, int> MissionStars { get; set; } = new();

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Returns true if the given mission has been completed.</summary>
    public bool IsCompleted(string missionId) => CompletedMissions.Contains(missionId);

    /// <summary>Returns the star count for a mission (0 if not completed).</summary>
    public int GetStars(string missionId) =>
        MissionStars.TryGetValue(missionId, out int s) ? s : 0;

    /// <summary>
    /// Records a mission completion. Updates stars if the new result is better.
    /// Returns true if this is the first time this mission was completed.
    /// </summary>
    public bool RecordCompletion(string missionId, int stars)
    {
        bool firstTime = !CompletedMissions.Contains(missionId);
        if (firstTime)
            CompletedMissions.Add(missionId);

        int current = GetStars(missionId);
        if (stars > current)
            MissionStars[missionId] = stars;

        return firstTime;
    }
}

/// <summary>
/// Root document for the campaign progress save file
/// (<c>user://campaign_progress.json</c>). Holds one
/// <see cref="FactionCampaignProgress"/> record per faction.
/// </summary>
public sealed class AllCampaignProgress
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Key = faction ID (e.g. "arcloft").</summary>
    [JsonPropertyName("factions")]
    public Dictionary<string, FactionCampaignProgress> Factions { get; set; } = new();

    /// <summary>Gets (or creates) the progress object for a faction.</summary>
    public FactionCampaignProgress GetOrCreate(string factionId)
    {
        if (!Factions.TryGetValue(factionId, out var progress))
        {
            progress = new FactionCampaignProgress { FactionId = factionId };
            Factions[factionId] = progress;
        }
        return progress;
    }
}
