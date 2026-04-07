using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.World;

/// <summary>
/// Data for a single building model entry from the building manifest.
/// Holds collision, physics, and rendering data for map-placed buildings.
/// </summary>
public sealed class BuildingModelEntry
{
    public string ModelPath { get; init; } = string.Empty;
    public int CollisionWidth { get; init; }
    public int CollisionHeight { get; init; }
    public FixedPoint CollisionRadius { get; init; }
    public FixedPoint Mass { get; init; }
    public bool Passable { get; init; }
    public FixedPoint ModelScale { get; init; } = FixedPoint.One;
    public FixedPoint ModelRotation { get; init; }
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// Loads building_manifest.json with collision/model data for all buildings.
/// Uses Godot's FileAccess for compatibility with exported projects.
/// </summary>
public sealed class BuildingManifest
{
    private readonly SortedList<string, BuildingModelEntry> _entries = new();

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
    /// Loads the building manifest from the given Godot resource path.
    /// Expects a JSON object keyed by building ID.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="manifestPath">
    /// Godot resource path to the manifest JSON file
    /// (e.g., "res://data/building_manifest.json").
    /// </param>
    public void Load(string manifestPath)
    {
        _entries.Clear();

        string json = ReadGodotFile(manifestPath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, BuildingModelEntry>>(json, JsonOptions);
        if (dict == null)
        {
            GD.PushWarning("[BuildingManifest] Deserialized null from manifest.");
            return;
        }

        // Insert into SortedList for deterministic iteration order.
        foreach (var kvp in dict)
        {
            if (!_entries.ContainsKey(kvp.Key))
            {
                _entries.Add(kvp.Key, kvp.Value);
                GD.Print($"[BuildingManifest] Loaded building entry '{kvp.Key}'.");
            }
            else
            {
                GD.PushWarning($"[BuildingManifest] Duplicate building entry '{kvp.Key}' — skipped.");
            }
        }

        GD.Print($"[BuildingManifest] Load complete — {_entries.Count} entries.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the building model entry for the given ID or throws.</summary>
    public BuildingModelEntry GetEntry(string buildingId)
    {
        if (_entries.TryGetValue(buildingId, out var entry))
            return entry;
        throw new KeyNotFoundException($"Building entry '{buildingId}' not found in BuildingManifest.");
    }

    /// <summary>Returns true if an entry exists for the given building ID.</summary>
    public bool HasEntry(string buildingId)
    {
        return _entries.ContainsKey(buildingId);
    }

    /// <summary>Returns the number of loaded building entries.</summary>
    public int Count => _entries.Count;

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
