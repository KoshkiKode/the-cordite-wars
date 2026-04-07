using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorditeWars.Systems.Localization;

/// <summary>
/// Manages versioned subtitles for voice lines and audio events.
/// Audio stays in English; subtitles are translated per-language
/// and tracked with a version number for update management.
///
/// Subtitle files live in data/subtitles/ with the structure:
/// <code>
/// {
///   "version": 1,
///   "locale": "en",
///   "entries": {
///     "vo_commander_greeting": { "text": "Commander on deck.", "duration": 2.0 }
///   }
/// }
/// </code>
/// </summary>
public partial class SubtitleManager : Node
{
    private const string SubtitleDir = "res://data/subtitles";
    private const string ManifestPath = "res://data/subtitles/manifest.json";

    /// <summary>Singleton set by Godot autoload.</summary>
    public static SubtitleManager? Instance { get; private set; }

    /// <summary>Fired when a subtitle should be displayed.</summary>
    [Signal]
    public delegate void SubtitleShowRequestedEventHandler(string text, float duration);

    /// <summary>Fired when the active subtitle should be hidden.</summary>
    [Signal]
    public delegate void SubtitleHideRequestedEventHandler();

    // Current locale's subtitle entries
    private Dictionary<string, SubtitleEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // Version info per locale
    private SubtitleManifest? _manifest;

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[SubtitleManager] Initialized.");
    }

    /// <summary>
    /// Loads the subtitle manifest and the subtitle file for the current locale.
    /// Call after <see cref="LocalizationManager.LoadAllTranslations"/>.
    /// </summary>
    public void LoadSubtitles()
    {
        LoadManifest();

        string locale = LocalizationManager.Instance?.GetCurrentLocale() ?? "en";
        LoadLocaleSubtitles(locale);

        // Re-load when language changes
        if (LocalizationManager.Instance != null)
        {
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }
    }

    /// <summary>
    /// Shows a subtitle for a given sound/voice line ID.
    /// Only works if subtitles are enabled.
    /// </summary>
    /// <param name="subtitleId">The subtitle key, e.g. "vo_commander_greeting".</param>
    public void ShowSubtitle(string subtitleId)
    {
        if (LocalizationManager.Instance != null && !LocalizationManager.Instance.SubtitlesEnabled)
            return;

        if (_entries.TryGetValue(subtitleId, out var entry))
        {
            EmitSignal(SignalName.SubtitleShowRequested, entry.Text, entry.Duration);
        }
    }

    /// <summary>
    /// Hides the current subtitle.
    /// </summary>
    public void HideSubtitle()
    {
        EmitSignal(SignalName.SubtitleHideRequested);
    }

    /// <summary>
    /// Returns the subtitle version for a given locale, or -1 if unknown.
    /// Used by the auto-subtitle versioning system to detect stale translations.
    /// </summary>
    public int GetLocaleVersion(string locale)
    {
        if (_manifest?.Versions != null &&
            _manifest.Versions.TryGetValue(locale, out int version))
        {
            return version;
        }
        return -1;
    }

    /// <summary>
    /// Returns the master (English) subtitle version, or -1 if unknown.
    /// </summary>
    public int GetMasterVersion()
    {
        return _manifest?.MasterVersion ?? -1;
    }

    /// <summary>
    /// Checks whether a locale's subtitles are up to date with the master.
    /// Returns true if versions match, false if the locale needs updating.
    /// </summary>
    public bool IsLocaleUpToDate(string locale)
    {
        int master = GetMasterVersion();
        int localeVer = GetLocaleVersion(locale);
        return master >= 0 && localeVer >= 0 && localeVer >= master;
    }

    /// <summary>
    /// Returns all subtitle IDs that exist in the master but are
    /// missing from a given locale's file. Useful for translation tooling.
    /// </summary>
    public List<string> GetMissingKeys(string locale)
    {
        var missing = new List<string>();

        // Load master entries for comparison
        var masterEntries = LoadEntriesFromFile("en");
        var localeEntries = LoadEntriesFromFile(locale);

        foreach (var key in masterEntries.Keys)
        {
            if (!localeEntries.ContainsKey(key))
                missing.Add(key);
        }

        return missing;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void OnLanguageChanged(string localeCode)
    {
        LoadLocaleSubtitles(localeCode);
    }

    private void LoadManifest()
    {
        try
        {
            string json = ReadFile(ManifestPath);
            _manifest = JsonSerializer.Deserialize<SubtitleManifest>(json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            if (_manifest != null)
            {
                GD.Print($"[SubtitleManager] Manifest loaded. Master version: {_manifest.MasterVersion}, " +
                         $"{_manifest.Versions?.Count ?? 0} locale versions tracked.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SubtitleManager] Failed to load manifest: {ex.Message}");
            _manifest = new SubtitleManifest();
        }
    }

    private void LoadLocaleSubtitles(string locale)
    {
        _entries = LoadEntriesFromFile(locale);

        if (_entries.Count == 0 && locale != "en")
        {
            // Fall back to English subtitles
            GD.PushWarning($"[SubtitleManager] No subtitles for '{locale}', falling back to English.");
            _entries = LoadEntriesFromFile("en");
        }

        if (!IsLocaleUpToDate(locale) && locale != "en")
        {
            GD.PushWarning($"[SubtitleManager] Subtitles for '{locale}' are outdated " +
                           $"(v{GetLocaleVersion(locale)} vs master v{GetMasterVersion()}).");
        }

        GD.Print($"[SubtitleManager] Loaded {_entries.Count} subtitle entries for '{locale}'.");
    }

    private Dictionary<string, SubtitleEntry> LoadEntriesFromFile(string locale)
    {
        string path = $"{SubtitleDir}/{locale}.json";
        var result = new Dictionary<string, SubtitleEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = ReadFile(path);
            var file = JsonSerializer.Deserialize<SubtitleFile>(json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            if (file?.Entries != null)
            {
                foreach (var (key, entry) in file.Entries)
                {
                    result[key] = entry;
                }
            }
        }
        catch
        {
            // Silently ignore missing subtitle files — they're optional
        }

        return result;
    }

    private static string ReadFile(string path)
    {
        using var fa = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (fa != null)
            return fa.GetAsText();

        if (System.IO.File.Exists(path))
            return System.IO.File.ReadAllText(path);

        throw new InvalidOperationException(
            $"[SubtitleManager] Cannot open file: '{path}' " +
            $"(Godot error: {FileAccess.GetOpenError()})");
    }
}

// ── Data Models ─────────────────────────────────────────────────────────

/// <summary>
/// A single subtitle entry with text and display duration.
/// </summary>
public sealed class SubtitleEntry
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Duration in seconds to display the subtitle.</summary>
    [JsonPropertyName("duration")]
    public float Duration { get; init; } = 3.0f;
}

/// <summary>
/// Root object of a per-locale subtitle file.
/// </summary>
internal sealed class SubtitleFile
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en";

    [JsonPropertyName("entries")]
    public Dictionary<string, SubtitleEntry>? Entries { get; init; }
}

/// <summary>
/// Root object of the subtitle manifest that tracks versions
/// across all locales. Used by the auto-versioning system.
///
/// Structure:
/// <code>
/// {
///   "master_version": 1,
///   "versions": { "en": 1, "fr": 1, "de": 1, ... },
///   "last_updated": "2026-04-07"
/// }
/// </code>
/// </summary>
internal sealed class SubtitleManifest
{
    [JsonPropertyName("master_version")]
    public int MasterVersion { get; init; } = 1;

    [JsonPropertyName("versions")]
    public Dictionary<string, int>? Versions { get; init; }

    [JsonPropertyName("last_updated")]
    public string? LastUpdated { get; init; }
}
