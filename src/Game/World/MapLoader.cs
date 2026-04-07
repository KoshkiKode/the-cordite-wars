using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Factions;

namespace UnnamedRTS.Game.World;

/// <summary>
/// Loads all map JSON files from a directory into a deterministic SortedList lookup.
/// Uses Godot's FileAccess for compatibility with exported projects.
/// </summary>
public sealed class MapLoader
{
    private readonly SortedList<string, MapData> _maps = new();

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
    /// Loads all .json files from the given directory into MapData objects.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="mapsDirectory">
    /// Godot resource path to the maps directory (e.g., "res://data/maps").
    /// </param>
    public void LoadAllMaps(string mapsDirectory)
    {
        _maps.Clear();

        using var dir = DirAccess.Open(mapsDirectory);
        if (dir is null)
        {
            GD.PushWarning(
                $"[MapLoader] Could not open directory '{mapsDirectory}' " +
                $"(error: {DirAccess.GetOpenError()}).");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = $"{mapsDirectory}/{fileName}";
                try
                {
                    string json = ReadGodotFile(filePath);
                    MapData? map = JsonSerializer.Deserialize<MapData>(json, JsonOptions);

                    if (map != null)
                    {
                        if (!_maps.ContainsKey(map.Id))
                        {
                            _maps.Add(map.Id, map);
                            GD.Print($"[MapLoader] Loaded map '{map.Id}'.");
                        }
                        else
                        {
                            GD.PushWarning($"[MapLoader] Duplicate map ID '{map.Id}' — skipped.");
                        }
                    }
                    else
                    {
                        GD.PushWarning($"[MapLoader] Deserialized null from '{filePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PushError($"[MapLoader] Failed to load '{filePath}': {ex.Message}");
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"[MapLoader] Load complete — {_maps.Count} maps.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the MapData for the given ID or throws.</summary>
    public MapData GetMap(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var data))
            return data;
        throw new KeyNotFoundException($"Map '{mapId}' not found in MapLoader.");
    }

    /// <summary>Returns true if a map with the given ID is loaded.</summary>
    public bool HasMap(string mapId)
    {
        return _maps.ContainsKey(mapId);
    }

    /// <summary>Returns a sorted list of available map IDs.</summary>
    public IList<string> GetMapIds()
    {
        return _maps.Keys;
    }

    /// <summary>Returns the number of loaded maps.</summary>
    public int MapCount => _maps.Count;

    /// <summary>
    /// Registers a dynamically generated (or otherwise non-file-based) map.
    /// If a map with the same ID already exists it is replaced.
    /// </summary>
    public void RegisterMap(MapData map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));

        if (_maps.ContainsKey(map.Id))
        {
            _maps[map.Id] = map;
            GD.Print($"[MapLoader] Replaced map '{map.Id}'.");
        }
        else
        {
            _maps.Add(map.Id, map);
            GD.Print($"[MapLoader] Registered map '{map.Id}'.");
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
