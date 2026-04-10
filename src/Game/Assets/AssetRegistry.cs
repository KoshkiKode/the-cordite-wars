using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.Assets;

/// <summary>
/// Data for a single unit type from the asset manifest.
/// Holds model path, collision radius, footprint, mass, crush strength, and rendering hints.
/// </summary>
public sealed class AssetEntry
{
    public string ModelPath { get; init; } = string.Empty;
    public FixedPoint CollisionRadius { get; init; }
    /// <summary>
    /// Height of the 3D collision cylinder for ground units (metres).
    /// Air units use a sphere whose radius equals <see cref="CollisionRadius"/> and ignore this value.
    /// Defaults to 1.0 if not specified in the manifest.
    /// </summary>
    public FixedPoint CollisionHeight { get; init; } = FixedPoint.One;
    public int FootprintWidth { get; init; } = 1;
    public int FootprintHeight { get; init; } = 1;
    public FixedPoint Mass { get; init; }
    public FixedPoint CrushStrength { get; init; }
    public FixedPoint Speed { get; init; }
    public string Domain { get; init; } = string.Empty;
    public FixedPoint ModelScale { get; init; } = FixedPoint.One;
    public FixedPoint ModelRotation { get; init; }
}

/// <summary>
/// Registry that loads <c>data/asset_manifest.json</c> and provides
/// deterministic lookups for per-unit-type physics and rendering properties.
/// All data is stored in a <see cref="SortedList{TKey,TValue}"/> for
/// deterministic iteration order.
/// </summary>
public sealed class AssetRegistry
{
    private readonly SortedList<string, AssetEntry> _entries = new();

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
    /// Loads the asset manifest from the given Godot resource path.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="manifestPath">
    /// Godot resource path to the manifest JSON file
    /// (e.g., "res://data/asset_manifest.json").
    /// </param>
    public void Load(string manifestPath)
    {
        _entries.Clear();

        string json = ReadGodotFile(manifestPath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, AssetEntry>>(json, JsonOptions);
        if (dict == null)
        {
            GD.PushWarning("[AssetRegistry] Deserialized null from manifest.");
            return;
        }

        // Insert into SortedList for deterministic iteration order.
        foreach (var kvp in dict)
        {
            if (!_entries.ContainsKey(kvp.Key))
            {
                _entries.Add(kvp.Key, kvp.Value);
                GD.Print($"[AssetRegistry] Loaded asset entry '{kvp.Key}'.");
            }
            else
            {
                GD.PushWarning($"[AssetRegistry] Duplicate asset entry '{kvp.Key}' — skipped.");
            }
        }

        GD.Print($"[AssetRegistry] Load complete — {_entries.Count} entries.");
    }

    /// <summary>Returns the GLB model path for the given unit type.</summary>
    public string GetModelPath(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.ModelPath;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the collision radius for the given unit type.</summary>
    public FixedPoint GetCollisionRadius(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.CollisionRadius;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the footprint (width, height) in grid cells for the given unit type.</summary>
    public (int Width, int Height) GetFootprint(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return (entry.FootprintWidth, entry.FootprintHeight);
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the mass for the given unit type.</summary>
    public FixedPoint GetMass(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.Mass;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the crush strength for the given unit type.</summary>
    public FixedPoint GetCrushStrength(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.CrushStrength;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the model scale for the given unit type.</summary>
    public FixedPoint GetModelScale(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.ModelScale;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the model rotation in degrees for the given unit type.</summary>
    public FixedPoint GetModelRotation(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry.ModelRotation;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns true if an entry exists for the given unit type.</summary>
    public bool HasEntry(string unitId)
    {
        return _entries.ContainsKey(unitId);
    }

    /// <summary>Returns the full <see cref="AssetEntry"/> for the given unit type.</summary>
    public AssetEntry GetEntry(string unitId)
    {
        if (_entries.TryGetValue(unitId, out var entry))
            return entry;
        throw new KeyNotFoundException($"Asset entry '{unitId}' not found in registry.");
    }

    /// <summary>Returns the number of loaded entries.</summary>
    public int Count => _entries.Count;

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
