using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.World;

/// <summary>
/// Data for a single terrain model entry from the terrain manifest.
/// </summary>
public sealed class TerrainModelEntry
{
    public string ModelPath { get; init; } = string.Empty;
    public FixedPoint CollisionRadius { get; init; }
    public bool Passable { get; init; }
    public bool BlocksVision { get; init; }
    public bool Destructible { get; init; }
    public int Health { get; init; }
    public FixedPoint ModelScale { get; init; } = FixedPoint.One;
}

/// <summary>
/// Loads the terrain_manifest.json with model/collision data for terrain props.
/// Entries are organized by category (trees, rocks, etc.) and keyed by model ID.
/// Uses nested SortedLists for deterministic iteration order.
/// </summary>
public sealed class TerrainManifest
{
    private readonly SortedList<string, SortedList<string, TerrainModelEntry>> _entries = new();

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        opts.Converters.Add(new FixedPointJsonConverter());
        opts.Converters.Add(new NullableFixedPointJsonConverter());
        return opts;
    }

    /// <summary>
    /// Loads the terrain manifest from the given Godot resource path.
    /// Expects a JSON object keyed by category, each containing an object keyed by model ID.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="manifestPath">
    /// Godot resource path to the manifest JSON file
    /// (e.g., "res://data/terrain_manifest.json").
    /// </param>
    public void Load(string manifestPath)
    {
        _entries.Clear();

        string json = ReadGodotFile(manifestPath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, TerrainModelEntry>>>(json, JsonOptions);
        if (dict == null)
        {
            GD.PushWarning("[TerrainManifest] Deserialized null from manifest.");
            return;
        }

        int totalCount = 0;

        // Insert into nested SortedLists for deterministic iteration order.
        foreach (var categoryKvp in dict)
        {
            var sorted = new SortedList<string, TerrainModelEntry>();

            foreach (var entryKvp in categoryKvp.Value)
            {
                if (!sorted.ContainsKey(entryKvp.Key))
                {
                    sorted.Add(entryKvp.Key, entryKvp.Value);
                    totalCount++;
                }
                else
                {
                    GD.PushWarning(
                        $"[TerrainManifest] Duplicate entry '{entryKvp.Key}' in category '{categoryKvp.Key}' — skipped.");
                }
            }

            if (!_entries.ContainsKey(categoryKvp.Key))
            {
                _entries.Add(categoryKvp.Key, sorted);
            }
            else
            {
                GD.PushWarning($"[TerrainManifest] Duplicate category '{categoryKvp.Key}' — skipped.");
            }
        }

        GD.Print($"[TerrainManifest] Load complete — {totalCount} entries across {_entries.Count} categories.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the terrain model entry for the given category and model ID, or throws.</summary>
    public TerrainModelEntry GetEntry(string category, string modelId)
    {
        if (_entries.TryGetValue(category, out var categoryEntries))
        {
            if (categoryEntries.TryGetValue(modelId, out var entry))
                return entry;
        }
        throw new KeyNotFoundException(
            $"Terrain entry '{modelId}' in category '{category}' not found in TerrainManifest.");
    }

    /// <summary>
    /// Searches all categories for the given model ID.
    /// Returns the first match found (categories are searched in sorted order).
    /// </summary>
    public TerrainModelEntry FindEntry(string modelId)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            SortedList<string, TerrainModelEntry> categoryEntries = _entries.Values[i];
            if (categoryEntries.TryGetValue(modelId, out var entry))
                return entry;
        }
        throw new KeyNotFoundException(
            $"Terrain entry '{modelId}' not found in any category of TerrainManifest.");
    }

    /// <summary>Returns the sorted list of category names.</summary>
    public IList<string> GetCategories()
    {
        return _entries.Keys;
    }

    /// <summary>Returns the total number of terrain model entries across all categories.</summary>
    public int TotalEntries
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                count += _entries.Values[i].Count;
            }
            return count;
        }
    }

    // ── Private Helpers ─────────────────────────────────────────────

    private static string ReadGodotFile(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new System.IO.FileNotFoundException(
                $"Godot FileAccess could not open '{path}' " +
                $"(error: {FileAccess.GetOpenError()}).");
        }
        return file.GetAsText();
    }
}
