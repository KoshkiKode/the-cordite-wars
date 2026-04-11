using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Factions;

namespace CorditeWars.Game.Tech;

/// <summary>
/// Loads and caches all upgrade definitions from JSON files on disk.
/// Uses Godot's <see cref="FileAccess"/> for exported project compatibility.
/// </summary>
public sealed class UpgradeRegistry
{
    private readonly SortedList<string, UpgradeData> _upgrades = new();

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
    /// Loads all .json files from the given directory into UpgradeData objects.
    /// </summary>
    public void Load(string directoryPath)
    {
        _upgrades.Clear();

        using var dir = DirAccess.Open(directoryPath);
        if (dir is null)
        {
            GD.PushWarning(
                $"[UpgradeRegistry] Could not open directory '{directoryPath}' " +
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
                    UpgradeData? upgrade = JsonSerializer.Deserialize<UpgradeData>(json, JsonOptions);

                    if (upgrade != null)
                    {
                        if (!_upgrades.ContainsKey(upgrade.Id))
                        {
                            _upgrades.Add(upgrade.Id, upgrade);
                            GD.Print($"[UpgradeRegistry] Loaded upgrade '{upgrade.Id}'.");
                        }
                        else
                        {
                            GD.PushWarning($"[UpgradeRegistry] Duplicate upgrade ID '{upgrade.Id}' — skipped.");
                        }
                    }
                    else
                    {
                        GD.PushWarning($"[UpgradeRegistry] Deserialized null from '{filePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PushError($"[UpgradeRegistry] Failed to load '{filePath}': {ex.Message}");
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"[UpgradeRegistry] Load complete — {_upgrades.Count} upgrades.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the UpgradeData for the given ID or throws.</summary>
    public UpgradeData GetUpgrade(string upgradeId)
    {
        if (_upgrades.TryGetValue(upgradeId, out var data))
            return data;
        throw new KeyNotFoundException($"Upgrade '{upgradeId}' not found in UpgradeRegistry.");
    }

    /// <summary>Returns true if an upgrade with the given ID is loaded.</summary>
    public bool HasUpgrade(string upgradeId)
    {
        return _upgrades.ContainsKey(upgradeId);
    }

    /// <summary>
    /// Returns all upgrades belonging to the specified faction, sorted by tier then ID.
    /// </summary>
    public List<UpgradeData> GetFactionUpgrades(string factionId)
    {
        var result = new List<UpgradeData>();
        for (int i = 0; i < _upgrades.Count; i++)
        {
            UpgradeData upgrade = _upgrades.Values[i];
            if (upgrade.FactionId == factionId)
                result.Add(upgrade);
        }

        // Sort by tier, then by ID for deterministic ordering.
        // Simple insertion sort to avoid LINQ.
        for (int i = 1; i < result.Count; i++)
        {
            UpgradeData key = result[i];
            int j = i - 1;
            while (j >= 0 && CompareTierThenId(result[j], key) > 0)
            {
                result[j + 1] = result[j];
                j--;
            }
            result[j + 1] = key;
        }

        return result;
    }

    /// <summary>Returns the number of loaded upgrades.</summary>
    public int Count => _upgrades.Count;

    /// <summary>
    /// Registers an upgrade programmatically. Intended for testing without
    /// requiring Godot's file-system APIs.
    /// </summary>
    public void Register(UpgradeData data)
    {
        if (!_upgrades.ContainsKey(data.Id))
            _upgrades.Add(data.Id, data);
        else
            _upgrades[data.Id] = data;
    }

    // ── Private Helpers ─────────────────────────────────────────────

    private static int CompareTierThenId(UpgradeData a, UpgradeData b)
    {
        int tierCmp = a.Tier.CompareTo(b.Tier);
        if (tierCmp != 0) return tierCmp;
        return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
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
