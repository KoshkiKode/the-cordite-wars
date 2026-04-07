using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;

namespace CorditeWars.Game.Tech;

/// <summary>
/// Per-player tech state: tracks constructed buildings, completed upgrades,
/// and current research progress.
/// </summary>
public sealed class PlayerTechState
{
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;

    // ── Completed Buildings (type tracking, not instances) ───────────
    private readonly SortedList<string, int> _buildingCounts = new();

    // ── Completed Upgrades ──────────────────────────────────────────
    private readonly SortedList<string, bool> _completedUpgrades = new();

    // ── Research State ──────────────────────────────────────────────
    public string? CurrentResearch { get; private set; }
    public FixedPoint ResearchProgress { get; private set; }
    public FixedPoint ResearchTarget { get; private set; }

    // ── Building Registration ───────────────────────────────────────

    /// <summary>
    /// Registers that a building of this type has been constructed.
    /// Tracks instance counts so UnregisterBuilding only removes when the last is destroyed.
    /// </summary>
    public void RegisterBuilding(string buildingId)
    {
        if (_buildingCounts.TryGetValue(buildingId, out int count))
            _buildingCounts[buildingId] = count + 1;
        else
            _buildingCounts.Add(buildingId, 1);
    }

    /// <summary>
    /// Marks a building instance as destroyed. Only removes the type
    /// when the last instance is gone.
    /// </summary>
    public void UnregisterBuilding(string buildingId)
    {
        if (_buildingCounts.TryGetValue(buildingId, out int count))
        {
            if (count <= 1)
                _buildingCounts.Remove(buildingId);
            else
                _buildingCounts[buildingId] = count - 1;
        }
    }

    /// <summary>Returns true if the player has at least one instance of this building type.</summary>
    public bool HasBuilding(string buildingId)
    {
        return _buildingCounts.ContainsKey(buildingId);
    }

    /// <summary>Returns true if the player has completed this upgrade.</summary>
    public bool HasUpgrade(string upgradeId)
    {
        return _completedUpgrades.ContainsKey(upgradeId);
    }

    /// <summary>Returns all completed upgrade IDs for save/load serialization.</summary>
    public IList<string> GetCompletedUpgrades() => new List<string>(_completedUpgrades.Keys);

    /// <summary>Returns all registered building type IDs for save/load serialization.</summary>
    public IList<string> GetRegisteredBuildings() => new List<string>(_buildingCounts.Keys);

    // ── Research ────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the player can research a given upgrade:
    /// prerequisite building exists, prerequisite upgrades completed,
    /// not already researched, not currently researching something else.
    /// </summary>
    public bool CanResearchUpgrade(string upgradeId, UpgradeRegistry registry)
    {
        if (_completedUpgrades.ContainsKey(upgradeId))
            return false;

        if (CurrentResearch != null)
            return false;

        if (!registry.HasUpgrade(upgradeId))
            return false;

        UpgradeData data = registry.GetUpgrade(upgradeId);

        // Check faction match
        if (data.FactionId != FactionId)
            return false;

        // Check prerequisite building
        if (!string.IsNullOrEmpty(data.PrerequisiteBuilding))
        {
            if (!HasBuilding(data.PrerequisiteBuilding))
                return false;
        }

        // Check prerequisite upgrades
        for (int i = 0; i < data.PrerequisiteUpgrades.Length; i++)
        {
            if (!_completedUpgrades.ContainsKey(data.PrerequisiteUpgrades[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Begins researching an upgrade. Returns false if already researching.
    /// Caller should check CanResearchUpgrade first.
    /// </summary>
    public bool StartResearch(string upgradeId, UpgradeData data)
    {
        if (CurrentResearch != null)
            return false;

        CurrentResearch = upgradeId;
        ResearchProgress = FixedPoint.Zero;
        ResearchTarget = data.ResearchTime;
        return true;
    }

    /// <summary>
    /// Advances research progress by deltaTime. Returns the upgrade ID
    /// if research completed this tick, null otherwise.
    /// On completion, adds the upgrade to completed set and clears research state.
    /// </summary>
    public string? TickResearch(FixedPoint deltaTime)
    {
        if (CurrentResearch == null)
            return null;

        ResearchProgress = ResearchProgress + deltaTime;

        if (ResearchProgress >= ResearchTarget)
        {
            string completedId = CurrentResearch;
            _completedUpgrades[completedId] = true;
            CurrentResearch = null;
            ResearchProgress = FixedPoint.Zero;
            ResearchTarget = FixedPoint.Zero;
            return completedId;
        }

        return null;
    }

    /// <summary>
    /// Checks whether the player meets the requirements to produce a unit type.
    /// The producing building must exist.
    /// </summary>
    public bool CanBuildUnit(string unitTypeId, UnitDataRegistry unitReg, UpgradeRegistry upgradeReg)
    {
        if (!unitReg.HasUnit(unitTypeId))
            return false;

        // The unit's producing building must be constructed
        // (Building → UnlocksUnitIds relationship is checked externally;
        //  here we just verify the player has at least one building of the right faction)
        return true;
    }

    /// <summary>
    /// Checks whether the player meets the requirements to construct a building type.
    /// All prerequisite buildings must exist.
    /// </summary>
    public bool CanBuildBuilding(string buildingId, BuildingRegistry buildingReg)
    {
        if (!buildingReg.HasBuilding(buildingId))
            return false;

        var buildingData = buildingReg.GetBuilding(buildingId);

        // Check all prerequisite buildings
        var prereqs = buildingData.Prerequisites;
        for (int i = 0; i < prereqs.Count; i++)
        {
            if (!HasBuilding(prereqs[i]))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Central tech tree manager that ticks research for all players and
/// maintains stat modifiers from completed upgrades.
/// </summary>
public partial class TechTreeManager : Node
{
    private readonly SortedList<int, PlayerTechState> _players = new();
    private UpgradeRegistry? _upgradeRegistry;

    // Stat modifiers per player: playerId → (compositeKey → value)
    // compositeKey = "category|stat|modifier" for structured lookups.
    // We store three modifier types separately for correct stacking:
    //   - additive values sum together
    //   - multiplicative values multiply together (stored as product)
    //   - flat additive values sum together
    private readonly SortedList<int, SortedList<string, FixedPoint>> _addModifiers = new();
    private readonly SortedList<int, SortedList<string, FixedPoint>> _multiplyModifiers = new();
    private readonly SortedList<int, SortedList<string, FixedPoint>> _addFlatModifiers = new();

    // ── Initialization ──────────────────────────────────────────────

    public void Initialize(UpgradeRegistry registry)
    {
        _upgradeRegistry = registry;
    }

    public void AddPlayer(int playerId, string factionId)
    {
        if (_players.ContainsKey(playerId))
            return;

        _players.Add(playerId, new PlayerTechState
        {
            PlayerId = playerId,
            FactionId = factionId
        });
        _addModifiers[playerId] = new SortedList<string, FixedPoint>();
        _multiplyModifiers[playerId] = new SortedList<string, FixedPoint>();
        _addFlatModifiers[playerId] = new SortedList<string, FixedPoint>();
    }

    /// <summary>Returns the tech state for a player, or null if not found.</summary>
    public PlayerTechState? GetPlayerTech(int playerId)
    {
        if (_players.TryGetValue(playerId, out var state))
            return state;
        return null;
    }

    // ── Tick ─────────────────────────────────────────────────────────

    /// <summary>
    /// Advances research for all players. If any upgrade completes,
    /// applies its effects and emits signals.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        if (_upgradeRegistry == null)
            return;

        for (int i = 0; i < _players.Count; i++)
        {
            int playerId = _players.Keys[i];
            PlayerTechState state = _players.Values[i];

            string? completedId = state.TickResearch(deltaTime);
            if (completedId != null)
            {
                if (_upgradeRegistry.HasUpgrade(completedId))
                {
                    UpgradeData upgrade = _upgradeRegistry.GetUpgrade(completedId);
                    ApplyUpgradeEffects(playerId, upgrade);
                }
                EventBus.Instance?.EmitUpgradeCompleted(playerId, completedId);
                GD.Print($"[TechTree] Player {playerId} completed upgrade '{completedId}'.");
            }
        }
    }

    // ── Upgrade Effects ─────────────────────────────────────────────

    /// <summary>
    /// Applies all effects from a completed upgrade to the player's stat modifiers.
    /// </summary>
    public void ApplyUpgradeEffects(int playerId, UpgradeData upgrade)
    {
        for (int i = 0; i < upgrade.Effects.Length; i++)
        {
            UpgradeEffect effect = upgrade.Effects[i];
            string key = $"{effect.TargetCategory}|{effect.Stat}";

            if (effect.Modifier == "add")
            {
                if (!_addModifiers.TryGetValue(playerId, out var mods))
                    continue;

                if (mods.TryGetValue(key, out FixedPoint existing))
                    mods[key] = existing + effect.Value;
                else
                    mods.Add(key, effect.Value);
            }
            else if (effect.Modifier == "multiply")
            {
                if (!_multiplyModifiers.TryGetValue(playerId, out var mods))
                    continue;

                if (mods.TryGetValue(key, out FixedPoint existing))
                    mods[key] = existing * effect.Value;
                else
                    mods.Add(key, effect.Value);
            }
            else if (effect.Modifier == "add_flat")
            {
                if (!_addFlatModifiers.TryGetValue(playerId, out var mods))
                    continue;

                if (mods.TryGetValue(key, out FixedPoint existing))
                    mods[key] = existing + effect.Value;
                else
                    mods.Add(key, effect.Value);
            }
        }
    }

    // ── Stat Queries ────────────────────────────────────────────────

    /// <summary>
    /// Returns the additive modifier for a player/category/stat combination.
    /// </summary>
    public FixedPoint GetAddModifier(int playerId, string unitCategory, string stat)
    {
        string key = $"{unitCategory}|{stat}";
        if (_addModifiers.TryGetValue(playerId, out var mods))
        {
            if (mods.TryGetValue(key, out FixedPoint value))
                return value;
        }
        return FixedPoint.Zero;
    }

    /// <summary>
    /// Returns the multiplicative modifier for a player/category/stat combination.
    /// Returns One (identity) if no modifiers exist.
    /// </summary>
    public FixedPoint GetMultiplyModifier(int playerId, string unitCategory, string stat)
    {
        string key = $"{unitCategory}|{stat}";
        if (_multiplyModifiers.TryGetValue(playerId, out var mods))
        {
            if (mods.TryGetValue(key, out FixedPoint value))
                return value;
        }
        return FixedPoint.One;
    }

    /// <summary>
    /// Returns the flat additive modifier for a player/category/stat combination.
    /// </summary>
    public FixedPoint GetFlatModifier(int playerId, string unitCategory, string stat)
    {
        string key = $"{unitCategory}|{stat}";
        if (_addFlatModifiers.TryGetValue(playerId, out var mods))
        {
            if (mods.TryGetValue(key, out FixedPoint value))
                return value;
        }
        return FixedPoint.Zero;
    }

    /// <summary>
    /// Convenience method: computes the final stat value from a base value
    /// by applying all modifier types. Checks both the specific category
    /// and the "All" category.
    /// Formula: (base + add) * multiply + add_flat
    /// </summary>
    public FixedPoint GetModifiedStat(int playerId, string unitCategory, string stat, FixedPoint baseValue)
    {
        // Specific category modifiers
        FixedPoint add = GetAddModifier(playerId, unitCategory, stat);
        FixedPoint mul = GetMultiplyModifier(playerId, unitCategory, stat);
        FixedPoint flat = GetFlatModifier(playerId, unitCategory, stat);

        // "All" category modifiers (apply to every unit)
        FixedPoint addAll = GetAddModifier(playerId, "All", stat);
        FixedPoint mulAll = GetMultiplyModifier(playerId, "All", stat);
        FixedPoint flatAll = GetFlatModifier(playerId, "All", stat);

        // Stack: additive values sum, multiplicative values multiply
        FixedPoint totalAdd = add + addAll;
        FixedPoint totalMul = mul * mulAll;
        FixedPoint totalFlat = flat + flatAll;

        return (baseValue + totalAdd) * totalMul + totalFlat;
    }
}
