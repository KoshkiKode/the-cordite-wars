using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using System.Linq;
using Steamworks;

namespace CorditeWars.Systems.Platform;

// ──────────────────────────────────────────────────────────────────────────────
// Data model that mirrors data/achievements.json
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Data record for a single Steam achievement definition, loaded from
/// <c>data/achievements.json</c>.
/// </summary>
public sealed class AchievementDefinition
{
    [JsonPropertyName("id")]          public string Id          { get; set; } = string.Empty;
    [JsonPropertyName("name")]        public string Name        { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("hidden")]      public bool   Hidden      { get; set; }
    [JsonPropertyName("icon")]        public string Icon        { get; set; } = string.Empty;
}

file sealed class AchievementFile
{
    [JsonPropertyName("achievements")]
    public List<AchievementDefinition> Achievements { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// SteamManager
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages all Steamworks integration for Cordite Wars.
///
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>Initialise / shut down the Steamworks API.</item>
///   <item>Gate Steam overlay activation (Shift+Tab, in-game screenshots).</item>
///   <item>Unlock Steam achievements and track stat increments.</item>
///   <item>Trigger Steam Cloud save synchronisation after every save.</item>
///   <item>Set Rich Presence strings shown in the Steam Friends list.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Design note</b>: Steamworks.NET is loaded at runtime via a thin
/// reflection shim so that the project compiles and runs on platforms where
/// <c>steam_api64.dll</c> / <c>libsteam_api.so</c> is absent (CI, macOS
/// notarisation runners, Android, iOS).  All public methods are safe to call
/// even when Steam is unavailable — they simply no-op and return <c>false</c>.
/// </para>
///
/// <para>
/// <b>⚠ Pre-release action required</b>: <c>steam_appid.txt</c> currently
/// contains <c>480</c> (the Valve "Space War" test app).  Replace it with the
/// real Steamworks App ID before shipping, and update the depot IDs in
/// <c>steam/app-build.vdf</c> and the individual depot VDF files.
/// </para>
/// </summary>
public sealed partial class SteamManager : Node
{
    // ── Singleton ────────────────────────────────────────────────────

    public static SteamManager? Instance { get; private set; }

    // ── State ────────────────────────────────────────────────────────

    /// <summary>True only when the Steamworks API initialised successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>True while the Steam overlay is open (e.g. Shift+Tab).</summary>
    public bool IsOverlayActive { get; private set; }

    private readonly Dictionary<string, AchievementDefinition> _achievements = new();

    // Stat counters persisted each time they change
    private int _totalMatchesPlayed;
    private int _totalUnitsDestroyed;

    // Steam callbacks — kept alive for the lifetime of this node
    private Callback<UserStatsReceived_t>?    _userStatsReceivedCallback;
    private Callback<GameOverlayActivated_t>? _overlayActivatedCallback;

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        LoadAchievementDefinitions();
        TryInitSteam();
    }

    public override void _Process(double delta)
    {
        if (!IsAvailable) return;
        RunCallbacks();
    }

    public override void _ExitTree()
    {
        _userStatsReceivedCallback?.Dispose();
        _userStatsReceivedCallback = null;
        _overlayActivatedCallback?.Dispose();
        _overlayActivatedCallback = null;

        if (IsAvailable)
            ShutdownSteam();

        if (Instance == this)
            Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Unlocks a Steam achievement by its API name (e.g. <c>"FIRST_VICTORY"</c>).
    /// Safe to call multiple times — Steam ignores re-unlocks.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the achievement was set successfully (or was already set);
    /// <c>false</c> if Steam is unavailable or the achievement ID is unknown.
    /// </returns>
    public bool UnlockAchievement(string achievementId)
    {
        if (!IsAvailable)
        {
            GD.Print($"[Steam] Achievement '{achievementId}' skipped (Steam unavailable).");
            return false;
        }

        if (!_achievements.ContainsKey(achievementId))
        {
            GD.PushWarning($"[Steam] Unknown achievement ID: '{achievementId}'");
            return false;
        }

        bool ok = SetAchievementNative(achievementId);
        if (ok)
        {
            StoreStatsNative();
            GD.Print($"[Steam] Achievement unlocked: {achievementId}");
        }
        return ok;
    }

    /// <summary>
    /// Increments the <c>MATCHES_PLAYED</c> counter and triggers related milestone
    /// achievements.
    /// </summary>
    public void RecordMatchPlayed()
    {
        _totalMatchesPlayed++;
        SetStatNative("MATCHES_PLAYED", _totalMatchesPlayed);
        StoreStatsNative();

        if (_totalMatchesPlayed >= 10)
            UnlockAchievement("PLAY_10_MATCHES");
    }

    /// <summary>
    /// Increments the <c>UNITS_DESTROYED</c> counter and triggers the
    /// <c>DESTROY_100_UNITS</c> achievement milestone.
    /// </summary>
    public void RecordUnitsDestroyed(int count)
    {
        if (count <= 0) return;
        _totalUnitsDestroyed += count;
        SetStatNative("UNITS_DESTROYED", _totalUnitsDestroyed);
        StoreStatsNative();

        if (_totalUnitsDestroyed >= 100)
            UnlockAchievement("DESTROY_100_UNITS");
    }

    /// <summary>
    /// Sets the Steam Rich Presence status string shown in the Friends list.
    /// </summary>
    /// <param name="status">
    /// A short human-readable string, e.g. <c>"In Skirmish vs Hard AI"</c>.
    /// Maximum 256 bytes (Steam limit).
    /// </param>
    public void SetRichPresence(string status)
    {
        if (!IsAvailable) return;
        SetRichPresenceNative("status", status);
    }

    /// <summary>
    /// Clears Rich Presence (shown as "In Menus" by default in Steam Friends).
    /// </summary>
    public void ClearRichPresence()
    {
        if (!IsAvailable) return;
        ClearRichPresenceNative();
    }

    /// <summary>
    /// Notifies Steam Cloud that the save directory has changed.
    /// Steam automatically syncs <c>user://</c> paths that are registered in
    /// the Steamworks app configuration; this call is a best-effort hint.
    /// </summary>
    public void NotifySaveChanged()
    {
        if (!IsAvailable) return;
        GD.Print("[Steam] Cloud save sync triggered.");
        // Steamworks auto-sync handles the actual upload; no explicit call needed
        // beyond ensuring ISteamRemoteStorage is enabled in the app settings.
    }

    // ── Achievement data loading ─────────────────────────────────────

    private void LoadAchievementDefinitions()
    {
        const string path = "res://data/achievements.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning("[Steam] achievements.json not found — no achievements will be tracked.");
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushWarning("[Steam] Could not open achievements.json.");
            return;
        }

        try
        {
            string json = file.GetAsText();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var root = JsonSerializer.Deserialize<AchievementFile>(json, options);
            if (root is null) return;

            foreach (var def in root.Achievements.Where(def => !string.IsNullOrEmpty(def.Id)))
            {
                _achievements[def.Id] = def;
            }

            GD.Print($"[Steam] Loaded {_achievements.Count} achievement definitions.");
        }
        catch (JsonException ex)
        {
            GD.PushWarning($"[Steam] Failed to parse achievements.json: {ex.Message}");
        }
    }

    // ── Native shim layer ────────────────────────────────────────────
    //
    // All calls are wrapped in try/catch so the game runs cleanly on platforms
    // where steam_api64.dll / libsteam_api.so is absent (CI, mobile, etc.).

    private void TryInitSteam()
    {
        try
        {
            IsAvailable = SteamAPI.Init();
            if (!IsAvailable)
            {
                GD.Print("[Steam] SteamAPI.Init() returned false — Steam not running or steam_appid.txt missing.");
                return;
            }

            GD.Print($"[Steam] Initialized. AppId: {SteamUtils.GetAppID()}");

            // Register persistent callbacks before requesting stats so the
            // UserStatsReceived_t handler fires as soon as Steam responds.
            RegisterCallbacks();
            RequestCurrentStatsNative();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            GD.Print($"[Steam] Steamworks unavailable on this platform: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers persistent broadcast callbacks.  Must be called once, after
    /// <see cref="SteamAPI.Init"/> succeeds, and before the first
    /// <see cref="SteamAPI.RunCallbacks"/> tick.
    /// </summary>
    private void RegisterCallbacks()
    {
        try
        {
            _userStatsReceivedCallback = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
            _overlayActivatedCallback  = Callback<GameOverlayActivated_t>.Create(OnGameOverlayActivated);
            GD.Print("[Steam] Callbacks registered.");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] Failed to register callbacks: {ex.Message}");
        }
    }

    /// <summary>
    /// Fires when Steam has delivered the current stats and achievement
    /// state to the client.  We restore the in-memory counters here so
    /// subsequent stat increments start from the correct baseline.
    /// </summary>
    private void OnUserStatsReceived(UserStatsReceived_t result)
    {
        if (result.m_eResult != EResult.k_EResultOK)
        {
            GD.PushWarning($"[Steam] UserStatsReceived failed: {result.m_eResult}");
            return;
        }

        try
        {
            SteamUserStats.GetStat("MATCHES_PLAYED",  out _totalMatchesPlayed);
            SteamUserStats.GetStat("UNITS_DESTROYED", out _totalUnitsDestroyed);
            GD.Print($"[Steam] Stats loaded — Matches: {_totalMatchesPlayed}, Units destroyed: {_totalUnitsDestroyed}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] Failed to load stats from Steam: {ex.Message}");
        }
    }

    /// <summary>
    /// Fires whenever the Steam overlay is opened or closed (e.g. Shift+Tab).
    /// Callers can read <see cref="IsOverlayActive"/> to pause input handling.
    /// </summary>
    private void OnGameOverlayActivated(GameOverlayActivated_t result)
    {
        IsOverlayActive = result.m_bActive != 0;
        GD.Print($"[Steam] Overlay {(IsOverlayActive ? "opened" : "closed")}.");
    }

    private static void RunCallbacks()
    {
        try
        {
            SteamAPI.RunCallbacks();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] RunCallbacks error: {ex.Message}");
        }
    }

    private static void ShutdownSteam()
    {
        try
        {
            SteamAPI.Shutdown();
            GD.Print("[Steam] Steamworks API shut down.");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] Shutdown error: {ex.Message}");
        }
    }

    private static bool SetAchievementNative(string id)
    {
        try
        {
            return SteamUserStats.SetAchievement(id);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] SetAchievement error: {ex.Message}");
            return false;
        }
    }

    private static void StoreStatsNative()
    {
        try
        {
            SteamUserStats.StoreStats();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] StoreStats error: {ex.Message}");
        }
    }

    private static void SetStatNative(string name, int value)
    {
        try
        {
            SteamUserStats.SetStat(name, value);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] SetStat error: {ex.Message}");
        }
    }

    private static void SetRichPresenceNative(string key, string value)
    {
        try
        {
            SteamFriends.SetRichPresence(key, value);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] SetRichPresence error: {ex.Message}");
        }
    }

    private static void ClearRichPresenceNative()
    {
        try
        {
            SteamFriends.ClearRichPresence();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] ClearRichPresence error: {ex.Message}");
        }
    }

    private static void RequestCurrentStatsNative()
    {
        try
        {
            bool ok = SteamUserStats.RequestCurrentStats();
            GD.Print($"[Steam] RequestCurrentStats sent: {ok}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] RequestCurrentStats error: {ex.Message}");
        }
    }

    // ── Convenience helpers used by GameSession ──────────────────────

    /// <summary>
    /// Called by GameSession at the start of a match to set Rich Presence
    /// and update the matches-played stat.
    /// </summary>
    public void OnMatchStarted(string factionId, bool isMultiplayer, bool isAiOpponent, int aiDifficulty)
    {
        string opponentDesc = isMultiplayer ? "Multiplayer"
            : aiDifficulty switch
            {
                2 => "Hard AI",
                1 => "Medium AI",
                _ => "Easy AI"
            };

        SetRichPresence($"Playing {factionId} vs {opponentDesc}");
        RecordMatchPlayed();
    }

    /// <summary>
    /// Called by GameSession when the local player wins a match.
    /// Handles per-faction and general victory achievements.
    /// </summary>
    public void OnMatchWon(string factionId, bool isMultiplayer, bool isNavalMap, double matchDurationSeconds)
    {
        UnlockAchievement("FIRST_VICTORY");

        string factionAchievement = factionId.ToLowerInvariant() switch
        {
            "arcloft"   => "WIN_AS_ARCLOFT",
            "valkyr"    => "WIN_AS_VALKYR",
            "kragmore"  => "WIN_AS_KRAGMORE",
            "bastion"   => "WIN_AS_BASTION",
            "ironmarch" => "WIN_AS_IRONMARCH",
            "stormrend" => "WIN_AS_STORMREND",
            _           => string.Empty
        };

        if (!string.IsNullOrEmpty(factionAchievement))
        {
            UnlockAchievement(factionAchievement);
            CheckWinAllFactions();
        }

        if (isMultiplayer)
            UnlockAchievement("WIN_MULTIPLAYER");

        if (isNavalMap)
            UnlockAchievement("FIRST_NAVAL_VICTORY");

        if (matchDurationSeconds < 600.0)
            UnlockAchievement("MATCH_UNDER_10_MIN");

        ClearRichPresence();
    }

    /// <summary>
    /// Unlocks <c>WIN_ALL_FACTIONS</c> once the player has won at least one
    /// match with each of the six factions.  Uses the per-faction achievements
    /// as the source of truth so no extra stat is needed.
    /// </summary>
    private void CheckWinAllFactions()
    {
        if (!IsAvailable) return;

        try
        {
            string[] perFactionIds =
            {
                "WIN_AS_ARCLOFT", "WIN_AS_VALKYR", "WIN_AS_KRAGMORE",
                "WIN_AS_BASTION", "WIN_AS_IRONMARCH", "WIN_AS_STORMREND"
            };

            foreach (string id in perFactionIds)
            {
                if (!SteamUserStats.GetAchievement(id, out bool achieved) || !achieved)
                    return;
            }

            UnlockAchievement("WIN_ALL_FACTIONS");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Steam] CheckWinAllFactions error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by GameSession when the local player loses a match.
    /// </summary>
    public void OnMatchLost()
    {
        UnlockAchievement("LOSE_A_MATCH");
        ClearRichPresence();
    }

    /// <summary>
    /// Called by GameSession when a Hard AI opponent is defeated.
    /// </summary>
    public void OnHardAIDefeated()
    {
        UnlockAchievement("DEFEAT_HARD_AI");
    }
}
