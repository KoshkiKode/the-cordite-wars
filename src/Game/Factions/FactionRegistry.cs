using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.Factions;

// ═════════════════════════════════════════════════════════════════════
//  JSON Converters
// ═════════════════════════════════════════════════════════════════════

/// <summary>
/// Serialises <see cref="FixedPoint"/> as a JSON float and deserialises
/// back via <see cref="FixedPoint.FromFloat"/>.  This keeps the data
/// files human-readable while preserving deterministic conversion at load.
/// </summary>
public sealed class FixedPointJsonConverter : JsonConverter<FixedPoint>
{
    public override FixedPoint Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Accept both integer and floating-point JSON tokens.
        if (reader.TokenType == JsonTokenType.Number)
        {
            return FixedPoint.FromFloat((float)reader.GetDouble());
        }

        throw new JsonException(
            $"Expected a number for FixedPoint but got {reader.TokenType}.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        FixedPoint value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToFloat());
    }
}

/// <summary>
/// Serialises nullable <see cref="FixedPoint"/>? — writes null when absent,
/// otherwise delegates to <see cref="FixedPointJsonConverter"/>.
/// </summary>
public sealed class NullableFixedPointJsonConverter : JsonConverter<FixedPoint?>
{
    private readonly FixedPointJsonConverter _inner = new();

    public override FixedPoint? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return _inner.Read(ref reader, typeof(FixedPoint), options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FixedPoint? value,
        JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Faction Registry
// ═════════════════════════════════════════════════════════════════════

/// <summary>
/// Central registry that loads and caches all faction, unit, and building
/// data from JSON files on disk.  Uses Godot's <see cref="FileAccess"/>
/// for reading so it works with exported PCK / resource paths.
/// </summary>
public sealed class FactionRegistry
{
    // ── Public Data Stores ───────────────────────────────────────────

    /// <summary>All factions keyed by ID.</summary>
    public SortedList<string, FactionData> Factions { get; } = new();

    /// <summary>All units keyed by ID.</summary>
    public SortedList<string, UnitData> Units { get; } = new();

    /// <summary>All buildings keyed by ID.</summary>
    public SortedList<string, BuildingData> Buildings { get; } = new();

    // ── Shared Serialiser Options ────────────────────────────────────

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

    // ── Loading ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads all faction, unit, and building JSON files from the given
    /// directories and performs cross-reference validation.
    /// </summary>
    /// <param name="factionsPath">
    /// Godot resource/user path to the factions directory
    /// (e.g., "res://data/factions").
    /// </param>
    /// <param name="unitsPath">
    /// Godot resource/user path to the units directory.
    /// </param>
    /// <param name="buildingsPath">
    /// Godot resource/user path to the buildings directory.
    /// </param>
    public void LoadAll(string factionsPath, string unitsPath, string buildingsPath)
    {
        Factions.Clear();
        Units.Clear();
        Buildings.Clear();

        LoadDirectory<FactionData>(factionsPath, entry =>
        {
            if (!Factions.ContainsKey(entry.Id))
            {
                Factions.Add(entry.Id, entry);
                GD.Print($"[FactionRegistry] Loaded faction '{entry.Id}'.");
            }
            else
                GD.PushWarning($"[FactionRegistry] Duplicate faction ID '{entry.Id}' — skipped.");
        });

        LoadDirectory<UnitData>(unitsPath, entry =>
        {
            if (!Units.ContainsKey(entry.Id))
            {
                Units.Add(entry.Id, entry);
                GD.Print($"[FactionRegistry] Loaded unit '{entry.Id}'.");
            }
            else
                GD.PushWarning($"[FactionRegistry] Duplicate unit ID '{entry.Id}' — skipped.");
        });

        LoadDirectory<BuildingData>(buildingsPath, entry =>
        {
            if (!Buildings.ContainsKey(entry.Id))
            {
                Buildings.Add(entry.Id, entry);
                GD.Print($"[FactionRegistry] Loaded building '{entry.Id}'.");
            }
            else
                GD.PushWarning($"[FactionRegistry] Duplicate building ID '{entry.Id}' — skipped.");
        });

        Validate();

        GD.Print($"[FactionRegistry] Load complete — " +
                 $"{Factions.Count} factions, {Units.Count} units, {Buildings.Count} buildings.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the <see cref="FactionData"/> for the given ID or throws.</summary>
    public FactionData GetFaction(string id)
    {
        if (Factions.TryGetValue(id, out var data))
            return data;
        throw new KeyNotFoundException($"Faction '{id}' not found in registry.");
    }

    /// <summary>Returns the <see cref="UnitData"/> for the given ID or throws.</summary>
    public UnitData GetUnit(string id)
    {
        if (Units.TryGetValue(id, out var data))
            return data;
        throw new KeyNotFoundException($"Unit '{id}' not found in registry.");
    }

    /// <summary>Returns the <see cref="BuildingData"/> for the given ID or throws.</summary>
    public BuildingData GetBuilding(string id)
    {
        if (Buildings.TryGetValue(id, out var data))
            return data;
        throw new KeyNotFoundException($"Building '{id}' not found in registry.");
    }

    /// <summary>Returns all units belonging to the specified faction.</summary>
    public List<UnitData> GetUnitsForFaction(string factionId)
    {
        var result = new List<UnitData>();
        for (int i = 0; i < Units.Count; i++)
        {
            UnitData unit = Units.Values[i];
            if (unit.FactionId == factionId)
                result.Add(unit);
        }
        return result;
    }

    /// <summary>Returns all buildings belonging to the specified faction.</summary>
    public List<BuildingData> GetBuildingsForFaction(string factionId)
    {
        var result = new List<BuildingData>();
        for (int i = 0; i < Buildings.Count; i++)
        {
            BuildingData building = Buildings.Values[i];
            if (building.FactionId == factionId)
                result.Add(building);
        }
        return result;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Reads every <c>.json</c> file inside <paramref name="directoryPath"/>
    /// using Godot's <see cref="FileAccess"/> and deserialises each one
    /// to <typeparamref name="T"/>, invoking <paramref name="onLoaded"/>
    /// for each successful parse.
    /// </summary>
    private static void LoadDirectory<T>(string directoryPath, Action<T> onLoaded)
    {
        using var dir = DirAccess.Open(directoryPath);
        if (dir is null)
        {
            GD.PushWarning(
                $"[FactionRegistry] Could not open directory '{directoryPath}' " +
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
                    T? entry = JsonSerializer.Deserialize<T>(json, JsonOptions);

                    if (entry is not null)
                        onLoaded(entry);
                    else
                        GD.PushWarning($"[FactionRegistry] Deserialized null from '{filePath}'.");
                }
                catch (Exception ex)
                {
                    GD.PushError($"[FactionRegistry] Failed to load '{filePath}': {ex.Message}");
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();
    }

    /// <summary>
    /// Reads an entire text file via Godot's <see cref="FileAccess"/>.
    /// Works with both <c>res://</c> and <c>user://</c> paths as well
    /// as exported projects.
    /// </summary>
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

    // ── Validation ───────────────────────────────────────────────────

    /// <summary>
    /// Cross-references all loaded data and logs warnings for
    /// dangling references, empty rosters, etc.
    /// </summary>
    private void Validate()
    {
        // Faction → unit references
        foreach (var faction in Factions.Values)
        {
            foreach (string unitId in faction.AvailableUnitIds)
            {
                if (!Units.ContainsKey(unitId))
                    GD.PushWarning(
                        $"[FactionRegistry] Faction '{faction.Id}' references " +
                        $"unknown unit '{unitId}'.");
            }

            foreach (string buildingId in faction.AvailableBuildingIds)
            {
                if (!Buildings.ContainsKey(buildingId))
                    GD.PushWarning(
                        $"[FactionRegistry] Faction '{faction.Id}' references " +
                        $"unknown building '{buildingId}'.");
            }

            if (faction.AvailableUnitIds.Count == 0)
                GD.PushWarning(
                    $"[FactionRegistry] Faction '{faction.Id}' has no available units.");

            if (faction.AvailableBuildingIds.Count == 0)
                GD.PushWarning(
                    $"[FactionRegistry] Faction '{faction.Id}' has no available buildings.");
        }

        // Unit → faction back-references
        foreach (var unit in Units.Values)
        {
            if (!string.IsNullOrEmpty(unit.FactionId) && !Factions.ContainsKey(unit.FactionId))
                GD.PushWarning(
                    $"[FactionRegistry] Unit '{unit.Id}' references " +
                    $"unknown faction '{unit.FactionId}'.");

            // Validate MovementClassId is recognized.
            string[] validClasses =
                { "Infantry", "LightVehicle", "HeavyVehicle", "APC",
                  "Tank", "Artillery", "Helicopter", "Jet" };

            bool validClass = false;
            for (int i = 0; i < validClasses.Length; i++)
            {
                if (validClasses[i] == unit.MovementClassId)
                {
                    validClass = true;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(unit.MovementClassId) && !validClass)
            {
                GD.PushWarning(
                    $"[FactionRegistry] Unit '{unit.Id}' has unknown " +
                    $"MovementClassId '{unit.MovementClassId}'.");
            }
        }

        // Building → prerequisite / unlock references
        foreach (var building in Buildings.Values)
        {
            if (!string.IsNullOrEmpty(building.FactionId) && !Factions.ContainsKey(building.FactionId))
                GD.PushWarning(
                    $"[FactionRegistry] Building '{building.Id}' references " +
                    $"unknown faction '{building.FactionId}'.");

            foreach (string prereq in building.Prerequisites)
            {
                if (!Buildings.ContainsKey(prereq))
                    GD.PushWarning(
                        $"[FactionRegistry] Building '{building.Id}' has " +
                        $"unknown prerequisite '{prereq}'.");
            }

            foreach (string unlockUnit in building.UnlocksUnitIds)
            {
                if (!Units.ContainsKey(unlockUnit))
                    GD.PushWarning(
                        $"[FactionRegistry] Building '{building.Id}' unlocks " +
                        $"unknown unit '{unlockUnit}'.");
            }

            foreach (string unlockBuilding in building.UnlocksBuildingIds)
            {
                if (!Buildings.ContainsKey(unlockBuilding))
                    GD.PushWarning(
                        $"[FactionRegistry] Building '{building.Id}' unlocks " +
                        $"unknown building '{unlockBuilding}'.");
            }
        }
    }
}
