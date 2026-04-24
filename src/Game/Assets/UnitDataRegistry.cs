using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.Assets;

/// <summary>
/// Registry that loads all unit JSON files from <c>data/units/</c> and provides
/// deterministic lookups by unit ID and faction.
/// All data is stored in <see cref="SortedList{TKey,TValue}"/> for deterministic iteration.
/// </summary>
public sealed class UnitDataRegistry
{
    private readonly SortedList<string, UnitData> _units = new();

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
    /// Loads all <c>.json</c> files from the given directory into <see cref="UnitData"/> objects.
    /// Call once during game initialization.
    /// </summary>
    /// <param name="unitsPath">
    /// Godot resource path to the units directory (e.g., "res://data/units").
    /// </param>
    public void Load(string unitsPath)
    {
        _units.Clear();

        using var dir = DirAccess.Open(unitsPath);
        if (dir is null)
        {
            GD.PushWarning(
                $"[UnitDataRegistry] Could not open directory '{unitsPath}' " +
                $"(error: {DirAccess.GetOpenError()}).");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = $"{unitsPath}/{fileName}";
                try
                {
                    string json = ReadGodotFile(filePath);
                    UnitData? unit = JsonSerializer.Deserialize<UnitData>(json, JsonOptions);
                    if (unit != null)
                    {
                        if (!_units.ContainsKey(unit.Id))
                        {
                            _units.Add(unit.Id, unit);
                            GD.Print($"[UnitDataRegistry] Loaded unit '{unit.Id}'.");
                        }
                        else
                        {
                            GD.PushWarning($"[UnitDataRegistry] Duplicate unit ID '{unit.Id}' — skipped.");
                        }
                    }
                    else
                    {
                        GD.PushWarning($"[UnitDataRegistry] Deserialized null from '{filePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PushError($"[UnitDataRegistry] Failed to load '{filePath}': {ex.Message}");
                }
            }
            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"[UnitDataRegistry] Load complete — {_units.Count} units.");
    }

    /// <summary>Returns the <see cref="UnitData"/> for the given unit ID.</summary>
    public UnitData GetUnitData(string unitId)
    {
        if (_units.TryGetValue(unitId, out var data))
            return data;
        throw new KeyNotFoundException($"Unit '{unitId}' not found in UnitDataRegistry.");
    }

    /// <summary>Returns true if a unit with the given ID is loaded.</summary>
    public bool HasUnit(string unitId)
    {
        return _units.ContainsKey(unitId);
    }

    /// <summary>
    /// Returns all units belonging to the specified faction.
    /// The returned list is in deterministic order (sorted by unit ID).
    /// </summary>
    public List<UnitData> GetFactionUnits(string factionId)
    {
        var result = new List<UnitData>();
        for (int i = 0; i < _units.Count; i++)
        {
            UnitData unit = _units.Values[i];
            if (unit.FactionId == factionId)
            {
                result.Add(unit);
            }
        }
        return result;
    }

    /// <summary>Returns the number of loaded units.</summary>
    public int Count => _units.Count;

    /// <summary>
    /// Registers a unit programmatically. Intended for testing without
    /// requiring Godot's file-system APIs.
    /// </summary>
    public void Register(UnitData data)
    {
        if (!_units.ContainsKey(data.Id))
            _units.Add(data.Id, data);
        else
            _units[data.Id] = data;
    }

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
