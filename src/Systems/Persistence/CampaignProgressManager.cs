using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CorditeWars.Game.Campaign;
using Godot;

namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Static helper that reads and writes campaign progress to
/// <c>user://campaign_progress.json</c>.
///
/// <para>
/// Progress is loaded lazily on first access and cached for the session.
/// Call <see cref="Save"/> after any modification to persist changes.
/// </para>
/// </summary>
public static class CampaignProgressManager
{
    private const string ProgressFilePath = "user://campaign_progress.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static AllCampaignProgress? _cached;

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full progress document, loading from disk on first call.
    /// Never returns null — returns an empty document if no file exists yet.
    /// </summary>
    public static AllCampaignProgress Load()
    {
        if (_cached is not null)
            return _cached;

        if (!FileAccess.FileExists(ProgressFilePath))
        {
            _cached = new AllCampaignProgress();
            return _cached;
        }

        try
        {
            using var file = FileAccess.Open(ProgressFilePath, FileAccess.ModeFlags.Read);
            if (file is null)
            {
                GD.PushWarning("[CampaignProgressManager] Cannot open progress file for reading.");
                _cached = new AllCampaignProgress();
                return _cached;
            }

            string json = file.GetAsText();
            _cached = JsonSerializer.Deserialize<AllCampaignProgress>(json, JsonOptions)
                      ?? new AllCampaignProgress();

            GD.Print("[CampaignProgressManager] Loaded campaign progress from disk.");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[CampaignProgressManager] Failed to load progress: {ex.Message}. Starting fresh.");
            _cached = new AllCampaignProgress();
        }

        return _cached;
    }

    /// <summary>
    /// Records the completion of a campaign mission for the given faction,
    /// then immediately persists to disk.
    /// </summary>
    /// <param name="factionId">The playing faction (e.g. "arcloft").</param>
    /// <param name="missionId">The completed mission ID (e.g. "arcloft_03").</param>
    /// <param name="stars">Stars earned (1–3). Clamped to valid range.</param>
    /// <returns>True if this was the first completion of the mission.</returns>
    public static bool RecordMissionComplete(string factionId, string missionId, int stars)
    {
        stars = Math.Clamp(stars, 1, 3);
        var progress = Load();
        var faction = progress.GetOrCreate(factionId);
        bool firstTime = faction.RecordCompletion(missionId, stars);
        Save(progress);
        GD.Print($"[CampaignProgressManager] Recorded {factionId}/{missionId} ({stars}★)" +
                 (firstTime ? " [first completion]" : " [improved]"));
        return firstTime;
    }

    /// <summary>
    /// Writes the current progress cache to disk.
    /// </summary>
    public static void Save(AllCampaignProgress progress)
    {
        _cached = progress;

        try
        {
            string json = JsonSerializer.Serialize(progress, JsonOptions);

            using var file = FileAccess.Open(ProgressFilePath, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                GD.PushError($"[CampaignProgressManager] Cannot open progress file for writing " +
                             $"(error: {FileAccess.GetOpenError()}).");
                return;
            }

            file.StoreString(json);
            file.Flush();
            GD.Print("[CampaignProgressManager] Campaign progress saved.");
        }
        catch (Exception ex)
        {
            GD.PushError($"[CampaignProgressManager] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the in-memory cache, forcing the next <see cref="Load"/> call
    /// to re-read from disk. Useful after external modifications.
    /// </summary>
    public static void InvalidateCache() => _cached = null;

    // ── Private helpers ───────────────────────────────────────────────

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
