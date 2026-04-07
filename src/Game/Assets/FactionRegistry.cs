using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.Assets;

/// <summary>
/// Registry that loads all faction JSON files from <c>data/factions/</c> and provides
/// deterministic lookups by faction ID.
/// All data is stored in <see cref="SortedList{TKey,TValue}"/> for deterministic iteration.
/// </summary>
public sealed class AssetFactionRegistry
{
    private readonly SortedList<string, FactionData> _factions = new();

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
    /// Loads all <c>.json</c> files from the given directory into <see cref="FactionData"/> objects.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="factionsPath">
    /// Godot resource path to the factions directory (e.g., "res://data/factions").
    /// </param>
    public void Load(string factionsPath)
    {
        _factions.Clear();

        using var dir = DirAccess.Open(factionsPath);
        if (dir is null)
        {
            GD.PushWarning(
                $"[AssetFactionRegistry] Could not open directory '{factionsPath}' " +
                $"(error: {DirAccess.GetOpenError()}).");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = $"{factionsPath}/{fileName}";
                try
                {
                    string json = ReadGodotFile(filePath);
                    FactionData? faction = JsonSerializer.Deserialize<FactionData>(json, JsonOptions);
                    if (faction != null)
                    {
                        if (!_factions.ContainsKey(faction.Id))
                        {
                            _factions.Add(faction.Id, faction);
                            GD.Print($"[AssetFactionRegistry] Loaded faction '{faction.Id}'.");
                        }
                        else
                        {
                            GD.PushWarning($"[AssetFactionRegistry] Duplicate faction ID '{faction.Id}' — skipped.");
                        }
                    }
                    else
                    {
                        GD.PushWarning($"[AssetFactionRegistry] Deserialized null from '{filePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PushError($"[AssetFactionRegistry] Failed to load '{filePath}': {ex.Message}");
                }
            }
            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"[AssetFactionRegistry] Load complete — {_factions.Count} factions.");
    }

    /// <summary>Returns the <see cref="FactionData"/> for the given faction ID.</summary>
    public FactionData GetFaction(string factionId)
    {
        if (_factions.TryGetValue(factionId, out var data))
            return data;
        throw new KeyNotFoundException($"Faction '{factionId}' not found in AssetFactionRegistry.");
    }

    /// <summary>Returns true if a faction with the given ID is loaded.</summary>
    public bool HasFaction(string factionId)
    {
        return _factions.ContainsKey(factionId);
    }

    /// <summary>Returns the number of loaded factions.</summary>
    public int Count => _factions.Count;

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
