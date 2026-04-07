using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Game.Economy;

/// <summary>
/// Static per-faction economic modifiers. All simulation values use FixedPoint.
/// </summary>
public sealed class FactionEconomyConfig
{
    public string FactionId { get; init; } = string.Empty;

    // ── Harvester Modifiers ─────────────────────────────────────────

    /// <summary>Harvester movement speed (FixedPoint units/tick).</summary>
    public FixedPoint HarvesterSpeed { get; init; }

    /// <summary>Cordite carried per trip before returning to refinery.</summary>
    public int HarvesterCapacity { get; init; }

    /// <summary>Movement class: "Helicopter", "HeavyVehicle", "LightVehicle".</summary>
    public string HarvesterMovementClass { get; init; } = string.Empty;

    // ── Refinery Modifiers ──────────────────────────────────────────

    /// <summary>Passive Cordite/sec per refinery (Bastion: 15, others: 0).</summary>
    public FixedPoint RefineryPassiveIncome { get; init; }

    /// <summary>HP multiplier for refineries (Ironmarch: 1.5, others: 1.0).</summary>
    public FixedPoint RefineryHPMultiplier { get; init; }

    /// <summary>Whether the refinery has a defensive turret (Ironmarch only).</summary>
    public bool RefineryHasTurret { get; init; }

    // ── Reactor Modifiers ───────────────────────────────────────────

    /// <summary>Cordite cost to build a reactor.</summary>
    public int ReactorCost { get; init; }

    /// <summary>Voltaic Charge generated per second per reactor.</summary>
    public FixedPoint ReactorVCRate { get; init; }

    // ── Supply Modifiers ────────────────────────────────────────────

    /// <summary>Absolute maximum supply cap for this faction.</summary>
    public int MaxSupply { get; init; }

    /// <summary>Maximum number of supply depots allowed.</summary>
    public int MaxDepots { get; init; }

    // ── VC Stockpile ────────────────────────────────────────────────

    /// <summary>Maximum Voltaic Charge a player can stockpile.</summary>
    public int VCCap { get; init; }
}

/// <summary>
/// Factory for all six faction economy configurations.
/// </summary>
public static class FactionEconomyConfigs
{
    /// <summary>
    /// Creates a deterministic SortedList of all faction economy configs.
    /// </summary>
    public static SortedList<string, FactionEconomyConfig> CreateAll()
    {
        var configs = new SortedList<string, FactionEconomyConfig>();

        configs.Add("arcloft", new FactionEconomyConfig
        {
            FactionId = "arcloft",
            HarvesterSpeed = FixedPoint.FromFloat(0.30f),
            HarvesterCapacity = 500,
            HarvesterMovementClass = "Helicopter",
            RefineryPassiveIncome = FixedPoint.Zero,
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1000,
            ReactorVCRate = FixedPoint.FromInt(6),
            MaxSupply = 180,
            MaxDepots = 9,
            VCCap = 500
        });

        configs.Add("bastion", new FactionEconomyConfig
        {
            FactionId = "bastion",
            HarvesterSpeed = FixedPoint.FromFloat(0.35f),
            HarvesterCapacity = 500,
            HarvesterMovementClass = "LightVehicle",
            RefineryPassiveIncome = FixedPoint.FromInt(15),
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1000,
            ReactorVCRate = FixedPoint.FromInt(7),
            MaxSupply = 200,
            MaxDepots = 10,
            VCCap = 500
        });

        configs.Add("ironmarch", new FactionEconomyConfig
        {
            FactionId = "ironmarch",
            HarvesterSpeed = FixedPoint.FromFloat(0.35f),
            HarvesterCapacity = 500,
            HarvesterMovementClass = "LightVehicle",
            RefineryPassiveIncome = FixedPoint.Zero,
            RefineryHPMultiplier = FixedPoint.FromFloat(1.5f),
            RefineryHasTurret = true,
            ReactorCost = 1000,
            ReactorVCRate = FixedPoint.FromInt(5),
            MaxSupply = 210,
            MaxDepots = 10,
            VCCap = 500
        });

        configs.Add("kragmore", new FactionEconomyConfig
        {
            FactionId = "kragmore",
            HarvesterSpeed = FixedPoint.FromFloat(0.10f),
            HarvesterCapacity = 1000,
            HarvesterMovementClass = "HeavyVehicle",
            RefineryPassiveIncome = FixedPoint.Zero,
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1000,
            ReactorVCRate = FixedPoint.FromInt(5),
            MaxSupply = 220,
            MaxDepots = 10,
            VCCap = 500
        });

        configs.Add("stormrend", new FactionEconomyConfig
        {
            FactionId = "stormrend",
            HarvesterSpeed = FixedPoint.FromFloat(0.35f),
            HarvesterCapacity = 400,
            HarvesterMovementClass = "LightVehicle",
            RefineryPassiveIncome = FixedPoint.Zero,
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1250,
            ReactorVCRate = FixedPoint.FromInt(5),
            MaxSupply = 200,
            MaxDepots = 10,
            VCCap = 500
        });

        configs.Add("valkyr", new FactionEconomyConfig
        {
            FactionId = "valkyr",
            HarvesterSpeed = FixedPoint.FromFloat(0.30f),
            HarvesterCapacity = 350,
            HarvesterMovementClass = "Helicopter",
            RefineryPassiveIncome = FixedPoint.Zero,
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 800,
            ReactorVCRate = FixedPoint.FromInt(5),
            MaxSupply = 200,
            MaxDepots = 10,
            VCCap = 500
        });

        return configs;
    }
}
