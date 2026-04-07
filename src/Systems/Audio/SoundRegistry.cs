using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorditeWars.Systems.Audio
{
    // ─── Data model ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes a single sound event as defined in sound_manifest.json.
    /// Float fields are intentional — audio is rendering-only, not simulation logic.
    /// </summary>
    public sealed class SoundEntry
    {
        /// <summary>
        /// One or more file paths relative to the Godot project root (res://).
        /// Multiple variants are chosen at random by AudioManager to add natural variety.
        /// </summary>
        [JsonPropertyName("Files")]
        public string[] Files { get; init; } = Array.Empty<string>();

        /// <summary>Master volume multiplier (0.0 – 1.0).</summary>
        [JsonPropertyName("Volume")]
        public float Volume { get; init; } = 0.8f;

        /// <summary>
        /// Maximum random pitch shift applied per-play (±half this value, centred on 1.0).
        /// 0.1 means pitch can range from 0.95 to 1.05.
        /// </summary>
        [JsonPropertyName("PitchVariation")]
        public float PitchVariation { get; init; } = 0.0f;

        /// <summary>
        /// Spatial audio attenuation distance in world units.
        /// 0 = non-positional (UI, music, global voice).
        /// </summary>
        [JsonPropertyName("MaxDistance")]
        public float MaxDistance { get; init; } = 50.0f;

        /// <summary>
        /// Playback priority (1–10). When the audio pool is full, lower-priority
        /// sounds are dropped in favour of higher-priority ones.
        /// </summary>
        [JsonPropertyName("Priority")]
        public int Priority { get; init; } = 5;

        /// <summary>
        /// Minimum seconds that must elapse before this sound can play again
        /// from the same source. Prevents audio spam during rapid events.
        /// </summary>
        [JsonPropertyName("Cooldown")]
        public float Cooldown { get; init; } = 0.0f;

        /// <summary>
        /// Optional subtitle strings that map 1:1 with Files for localised captions.
        /// Only populated on voice lines.
        /// </summary>
        [JsonPropertyName("Subtitles")]
        public string[]? Subtitles { get; init; }
    }

    // ─── Manifest shape ──────────────────────────────────────────────────────────

    /// <summary>
    /// Root object of sound_manifest.json.
    /// Structure: { "categories": { "category_name": { "sound_id": SoundEntry } } }
    /// </summary>
    internal sealed class SoundManifest
    {
        [JsonPropertyName("categories")]
        public Dictionary<string, Dictionary<string, SoundEntry>> Categories { get; init; }
            = new Dictionary<string, Dictionary<string, SoundEntry>>(StringComparer.OrdinalIgnoreCase);
    }

    // ─── Registry ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and provides lookup access to the sound manifest.
    ///
    /// The registry is a thin data layer — it does not play sounds.
    /// AudioManager owns playback; it calls GetSound / FindSound to resolve
    /// a <see cref="SoundEntry"/> and then instantiates AudioStreamPlayer nodes.
    ///
    /// Typical usage:
    /// <code>
    ///   SoundRegistry.Instance.Load("res://data/sound_manifest.json");
    ///   SoundEntry? entry = SoundRegistry.Instance.GetSound("combat", "weapon_cannon_heavy");
    ///   if (entry != null)
    ///       AudioManager.Instance.PlayAt(entry, worldPosition);
    /// </code>
    /// </summary>
    public sealed class SoundRegistry
    {
        // ─── Singleton ────────────────────────────────────────────────────────

        private static readonly Lazy<SoundRegistry> _lazy =
            new Lazy<SoundRegistry>(() => new SoundRegistry());

        /// <summary>Global singleton — thread-safe lazy initialisation.</summary>
        public static SoundRegistry Instance => _lazy.Value;

        private SoundRegistry() { }

        // ─── Internal storage ─────────────────────────────────────────────────

        // SortedList gives O(log n) lookup and keeps categories/ids in alphabetical
        // order, which makes serialisation round-trips deterministic and debuggable.

        private SortedList<string, SortedList<string, SoundEntry>> _data =
            new SortedList<string, SortedList<string, SoundEntry>>(StringComparer.OrdinalIgnoreCase);

        private bool _loaded = false;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Whether the manifest has been successfully loaded.</summary>
        public bool IsLoaded => _loaded;

        /// <summary>
        /// Loads the sound manifest from a file path.
        /// Accepts both <c>res://</c> and absolute OS paths.
        ///
        /// Calling Load() again replaces the existing data; this allows hot-reload
        /// during development without restarting the game.
        /// </summary>
        /// <param name="manifestPath">
        /// Path to sound_manifest.json, e.g. <c>"res://data/sound_manifest.json"</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the file cannot be read or parsed.
        /// </exception>
        public void Load(string manifestPath)
        {
            GD.Print($"[SoundRegistry] Loading manifest: {manifestPath}");

            string json = ReadFile(manifestPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive   = true,
                ReadCommentHandling           = JsonCommentHandling.Skip,
                AllowTrailingCommas           = true
            };

            SoundManifest? manifest = JsonSerializer.Deserialize<SoundManifest>(json, options)
                ?? throw new InvalidOperationException(
                    $"[SoundRegistry] Failed to deserialise manifest at '{manifestPath}'.");

            // Rebuild internal lookup tables.
            var newData = new SortedList<string, SortedList<string, SoundEntry>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var (category, sounds) in manifest.Categories)
            {
                var categoryList = new SortedList<string, SoundEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var (soundId, entry) in sounds)
                {
                    ValidateEntry(soundId, entry);
                    categoryList.Add(soundId, entry);
                }
                newData.Add(category, categoryList);
            }

            _data  = newData;
            _loaded = true;

            int totalSounds = 0;
            foreach (var cat in _data.Values)
                totalSounds += cat.Count;

            GD.Print($"[SoundRegistry] Loaded {_data.Count} categories, {totalSounds} sound entries.");
        }

        /// <summary>
        /// Returns the <see cref="SoundEntry"/> for a specific category + sound ID.
        /// Returns <c>null</c> if not found (caller should handle gracefully).
        /// </summary>
        /// <param name="category">Category name, e.g. <c>"combat"</c>.</param>
        /// <param name="soundId">Sound identifier, e.g. <c>"weapon_cannon_heavy"</c>.</param>
        public SoundEntry? GetSound(string category, string soundId)
        {
            EnsureLoaded();

            if (!_data.TryGetValue(category, out var categoryList))
            {
                GD.PrintErr($"[SoundRegistry] Category not found: '{category}'");
                return null;
            }

            if (!categoryList.TryGetValue(soundId, out SoundEntry? entry))
            {
                GD.PrintErr($"[SoundRegistry] Sound not found: '{category}/{soundId}'");
                return null;
            }

            return entry;
        }

        /// <summary>
        /// Searches all categories for a sound ID and returns the first match.
        /// Useful for convenience calls where the category is unknown.
        ///
        /// Prefer <see cref="GetSound"/> when the category is known — it is faster
        /// (O(log n) vs O(c × log n) where c = number of categories).
        /// </summary>
        /// <param name="soundId">Sound identifier to search for, e.g. <c>"ui_click"</c>.</param>
        public SoundEntry? FindSound(string soundId)
        {
            EnsureLoaded();

            foreach (var categoryList in _data.Values)
            {
                if (categoryList.TryGetValue(soundId, out SoundEntry? entry))
                    return entry;
            }

            GD.PrintErr($"[SoundRegistry] Sound not found in any category: '{soundId}'");
            return null;
        }

        /// <summary>
        /// Returns all category names present in the manifest.
        /// </summary>
        public IEnumerable<string> GetCategories()
        {
            EnsureLoaded();
            return _data.Keys;
        }

        /// <summary>
        /// Returns all sound IDs within a given category.
        /// Returns an empty enumerable if the category does not exist.
        /// </summary>
        public IEnumerable<string> GetSoundsInCategory(string category)
        {
            EnsureLoaded();
            return _data.TryGetValue(category, out var list)
                ? list.Keys
                : Array.Empty<string>();
        }

        /// <summary>
        /// Returns a random file path from a <see cref="SoundEntry"/>'s Files array.
        /// Randomises uniformly if more than one variant exists.
        /// Returns <c>null</c> if the entry has no files.
        /// </summary>
        public static string? PickVariant(SoundEntry entry)
        {
            if (entry.Files == null || entry.Files.Length == 0)
                return null;

            if (entry.Files.Length == 1)
                return entry.Files[0];

            int index = GD.RandRange(0, entry.Files.Length - 1);
            return entry.Files[index];
        }

        /// <summary>
        /// Computes a randomised pitch scale value based on a SoundEntry's
        /// PitchVariation field. Centred on 1.0, range is ±(PitchVariation / 2).
        /// </summary>
        public static float RandomisedPitch(SoundEntry entry)
        {
            if (entry.PitchVariation <= 0f)
                return 1.0f;

            float halfRange = entry.PitchVariation / 2f;
            // GD.RandRange returns a double; cast to float is intentional (audio only).
            return 1.0f + (float)GD.RandRange(-halfRange, halfRange);
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        private void EnsureLoaded()
        {
            if (!_loaded)
                throw new InvalidOperationException(
                    "[SoundRegistry] Registry not loaded. Call Load() before accessing sounds.");
        }

        /// <summary>
        /// Reads a file via Godot's FileAccess (supports res:// and user://) or,
        /// if that fails, falls back to System.IO for absolute OS paths during
        /// editor/unit-test contexts.
        /// </summary>
        private static string ReadFile(string path)
        {
            // Try Godot's VFS first (works for res:// and user://).
            using var fa = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (fa != null)
                return fa.GetAsText();

            // Fallback: absolute OS path (e.g. during headless unit tests)
            if (System.IO.File.Exists(path))
                return System.IO.File.ReadAllText(path);

            throw new InvalidOperationException(
                $"[SoundRegistry] Cannot open file: '{path}' " +
                $"(Godot error: {FileAccess.GetOpenError()})");
        }

        /// <summary>
        /// Validates a SoundEntry and logs warnings for suspicious values.
        /// Does not throw — missing files are allowed at authoring time (placeholder paths).
        /// </summary>
        private static void ValidateEntry(string soundId, SoundEntry entry)
        {
            if (entry.Files == null || entry.Files.Length == 0)
            {
                GD.PrintErr($"[SoundRegistry] '{soundId}' has no Files defined.");
                return;
            }

            if (entry.Volume is < 0f or > 1f)
                GD.PrintErr($"[SoundRegistry] '{soundId}' Volume {entry.Volume} outside [0, 1].");

            if (entry.PitchVariation < 0f)
                GD.PrintErr($"[SoundRegistry] '{soundId}' PitchVariation {entry.PitchVariation} is negative.");

            if (entry.Priority is < 1 or > 10)
                GD.PrintErr($"[SoundRegistry] '{soundId}' Priority {entry.Priority} outside [1, 10].");

            if (entry.Cooldown < 0f)
                GD.PrintErr($"[SoundRegistry] '{soundId}' Cooldown {entry.Cooldown} is negative.");
        }
    }
}
