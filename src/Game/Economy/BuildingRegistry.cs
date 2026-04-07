using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.Economy;

/// <summary>
/// Loads all building JSONs from data/buildings/ into a deterministic SortedList lookup.
/// Uses Godot's FileAccess for compatibility with exported projects.
/// </summary>
public sealed class BuildingRegistry
{
    private readonly SortedList<string, BuildingData> _buildings = new();

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
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    /// <summary>
    /// Loads all .json files from the given directory into BuildingData objects.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="directoryPath">
    /// Godot resource path to the buildings directory (e.g., "res://data/buildings").
    /// </param>
    public void Load(string directoryPath)
    {
        _buildings.Clear();

        using var dir = DirAccess.Open(directoryPath);
        if (dir is null)
        {
            GD.PushWarning(
                $"[BuildingRegistry] Could not open directory '{directoryPath}' " +
                $"(error: {DirAccess.GetOpenError()}).");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = $"{directoryPath}/{fileName}";
                try
                {
                    string json = ReadGodotFile(filePath);
                    BuildingData? building = JsonSerializer.Deserialize<BuildingData>(json, JsonOptions);

                    if (building != null)
                    {
                        if (!_buildings.ContainsKey(building.Id))
                        {
                            _buildings.Add(building.Id, building);
                            GD.Print($"[BuildingRegistry] Loaded building '{building.Id}'.");
                        }
                        else
                        {
                            GD.PushWarning($"[BuildingRegistry] Duplicate building ID '{building.Id}' — skipped.");
                        }
                    }
                    else
                    {
                        GD.PushWarning($"[BuildingRegistry] Deserialized null from '{filePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PushError($"[BuildingRegistry] Failed to load '{filePath}': {ex.Message}");
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"[BuildingRegistry] Load complete — {_buildings.Count} buildings.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the BuildingData for the given ID or throws.</summary>
    public BuildingData GetBuilding(string buildingId)
    {
        if (_buildings.TryGetValue(buildingId, out var data))
            return data;
        throw new KeyNotFoundException($"Building '{buildingId}' not found in BuildingRegistry.");
    }

    /// <summary>Returns true if a building with the given ID is loaded.</summary>
    public bool HasBuilding(string buildingId)
    {
        return _buildings.ContainsKey(buildingId);
    }

    /// <summary>
    /// Returns all buildings belonging to the specified faction, sorted by ID.
    /// </summary>
    public List<BuildingData> GetFactionBuildings(string factionId)
    {
        var result = new List<BuildingData>();
        for (int i = 0; i < _buildings.Count; i++)
        {
            BuildingData building = _buildings.Values[i];
            if (building.FactionId == factionId)
            {
                result.Add(building);
            }
        }
        return result;
    }

    /// <summary>Returns the number of loaded buildings.</summary>
    public int Count => _buildings.Count;

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
