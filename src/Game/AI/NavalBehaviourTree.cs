using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Assets;

namespace CorditeWars.Game.AI;

/// <summary>
/// Naval sub-controller for the Skirmish AI.  Evaluates whether the current
/// map is water-heavy enough to invest in a Shipyard and naval fleet, then
/// prioritises patrol-boat scouting, destroyer control, and capital-ship
/// late-game pushes.
///
/// <para><b>Design:</b> this controller is intentionally lightweight.  It runs
/// at the same <see cref="SkirmishAI.TickInterval"/> cadence as the parent AI
/// and communicates via simple boolean flags and priority scores rather than
/// complex shared state.  All arithmetic uses <see cref="FixedPoint"/> to
/// maintain determinism.</para>
///
/// <para><b>Activation threshold:</b> the controller activates only when the
/// map has at least <see cref="WaterBodyThresholdPercent"/> percent of its
/// cells marked as water.  On mostly-land maps the naval pipeline is skipped
/// entirely, saving resources for the ground build order.</para>
/// </summary>
public partial class NavalBehaviourTree : Node
{
    // ── Constants ────────────────────────────────────────────────────

    /// <summary>
    /// Minimum percentage of water cells required before the AI invests
    /// in a Shipyard.  A 15% threshold ensures naval investment only
    /// happens on genuinely water-heavy maps like Archipelago.
    /// </summary>
    private const int WaterBodyThresholdPercent = 15;

    /// <summary>Target number of patrol boats before escalating to destroyers.</summary>
    private const int PatrolBoatTarget = 2;

    /// <summary>Target number of destroyers before building capital ships.</summary>
    private const int DestroyerTarget = 3;

    /// <summary>
    /// Tick threshold before Hard AI attempts to build a capital ship.
    /// 18000 ticks = 600 seconds (10 minutes) at 30 ticks per second.
    /// </summary>
    private const int CapitalShipDelayTicks = 18000;

    // ── State ────────────────────────────────────────────────────────

    private int _playerId;
    private string _factionId = string.Empty;
    private AIDifficulty _difficulty;

    private bool _isNavalMapDetected;
    private bool _shipyardBuilt;
    private int _navalUnitCount;

    // Tick sub-sampling: naval evaluation runs every 3× the parent interval
    private int _tickCounter;
    private const int TickDivisor = 3;

    // ── Initialization ───────────────────────────────────────────────

    /// <summary>
    /// Initializes the naval behaviour tree.
    /// <paramref name="waterCellPercent"/> is the fraction of map cells that
    /// are Water or DeepWater, expressed as an integer percentage (0–100).
    /// </summary>
    public void Initialize(
        int playerId,
        string factionId,
        AIDifficulty difficulty,
        int waterCellPercent)
    {
        _playerId = playerId;
        _factionId = factionId;
        _difficulty = difficulty;
        _isNavalMapDetected = waterCellPercent >= WaterBodyThresholdPercent;

        Name = $"NavalBehaviourTree_P{playerId}";

        if (_isNavalMapDetected)
        {
            GD.Print($"[NavalBehaviourTree P{playerId}] Water map detected " +
                     $"({waterCellPercent}% water) — naval pipeline active.");
        }
        else
        {
            GD.Print($"[NavalBehaviourTree P{playerId}] Insufficient water " +
                     $"({waterCellPercent}% < {WaterBodyThresholdPercent}%) — " +
                     $"naval pipeline inactive.");
        }
    }

    /// <summary>Whether this controller is active on the current map.</summary>
    public bool IsActive => _isNavalMapDetected;

    // ── Tick ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called every parent AI tick.  Sub-samples internally via
    /// <see cref="TickDivisor"/> to reduce cost.
    /// </summary>
    public void ProcessTick(
        FixedPoint deltaTime,
        int totalTicksElapsed,
        PlayerEconomy economy,
        EconomyManager economyManager,
        BuildingPlacer buildingPlacer,
        BuildingRegistry buildingRegistry,
        UnitDataRegistry unitDataRegistry,
        UnitSpawner unitSpawner)
    {
        if (!_isNavalMapDetected) return;

        _tickCounter++;
        if (_tickCounter < TickDivisor) return;
        _tickCounter = 0;

        // ── Stage 1: Build Shipyard ──────────────────────────────────
        if (!_shipyardBuilt)
        {
            TryBuildShipyard(economy, economyManager, buildingPlacer, buildingRegistry);
            return;
        }

        // ── Stage 2: Scout with patrol boats ─────────────────────────
        UpdateNavalUnitCount(unitSpawner);

        int patrolBoatCount = CountNavalUnitsByCategory(unitSpawner, UnitCategory.PatrolBoat);
        if (patrolBoatCount < PatrolBoatTarget)
        {
            TryProduceNavalUnit(
                GetNavalUnitId("patrol_boat"),
                economy, economyManager, unitDataRegistry);
            return;
        }

        // ── Stage 3: Build destroyers for sea control ─────────────────
        int destroyerCount = CountNavalUnitsByCategory(unitSpawner, UnitCategory.Destroyer);
        if (destroyerCount < DestroyerTarget)
        {
            TryProduceNavalUnit(
                GetNavalUnitId("destroyer"),
                economy, economyManager, unitDataRegistry);
            return;
        }

        // ── Stage 4: Late game — capital ship (Hard only) ────────────
        if (_difficulty == AIDifficulty.Hard && totalTicksElapsed > CapitalShipDelayTicks)
        {
            int capitalCount = CountNavalUnitsByCategory(unitSpawner, UnitCategory.CapitalShip);
            if (capitalCount < 1)
            {
                TryProduceNavalUnit(
                    GetNavalUnitId("capital"),
                    economy, economyManager, unitDataRegistry);
            }
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private void TryBuildShipyard(
        PlayerEconomy economy,
        EconomyManager economyManager,
        BuildingPlacer buildingPlacer,
        BuildingRegistry buildingRegistry)
    {
        string shipyardId = $"{_factionId}_shipyard";
        if (!buildingRegistry.HasBuilding(shipyardId)) return;

        BuildingData data = buildingRegistry.GetBuilding(shipyardId);

        if (!economy.CanAfford(data.Cost, data.SecondaryCost)) return;

        if (economyManager.TryBuildBuilding(_playerId, data))
        {
            _shipyardBuilt = true;
            GD.Print($"[NavalBehaviourTree P{_playerId}] Shipyard built.");
        }
    }

    private void TryProduceNavalUnit(
        string unitTypeId,
        PlayerEconomy economy,
        EconomyManager economyManager,
        UnitDataRegistry unitDataRegistry)
    {
        if (string.IsNullOrEmpty(unitTypeId)) return;
        if (!unitDataRegistry.HasUnit(unitTypeId)) return;

        UnitData unitData = unitDataRegistry.GetUnitData(unitTypeId);

        if (!economy.CanAfford(unitData.Cost, unitData.SecondaryCost)) return;
        if (economy.CurrentSupply + unitData.PopulationCost > economy.MaxSupply) return;

        if (economyManager.TryBuildUnit(_playerId, unitData))
        {
            GD.Print($"[NavalBehaviourTree P{_playerId}] Producing {unitTypeId}.");
        }
    }

    private void UpdateNavalUnitCount(UnitSpawner unitSpawner)
    {
        var allUnits = unitSpawner.GetAllUnits();
        int count = 0;
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].PlayerId == _playerId && IsNavalCategory(allUnits[i].Category))
                count++;
        }
        _navalUnitCount = count;
    }

    private int CountNavalUnitsByCategory(UnitSpawner unitSpawner, UnitCategory category)
    {
        var allUnits = unitSpawner.GetAllUnits();
        int count = 0;
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].PlayerId == _playerId && allUnits[i].Category == category)
                count++;
        }
        return count;
    }

    private static bool IsNavalCategory(UnitCategory? category)
    {
        return category == UnitCategory.PatrolBoat ||
               category == UnitCategory.Destroyer  ||
               category == UnitCategory.Submarine  ||
               category == UnitCategory.CapitalShip;
    }

    /// <summary>
    /// Returns the faction-qualified unit type ID for a generic naval role.
    /// Falls back to an empty string if the unit is not registered.
    /// </summary>
    private string GetNavalUnitId(string role)
    {
        return role switch
        {
            "patrol_boat" => _factionId switch
            {
                "arcloft"   => "arcloft_aurora_skimmer",
                "bastion"   => "bastion_fortress_boat",
                "ironmarch" => "ironmarch_march_barge",
                "kragmore"  => "kragmore_dredger",
                "stormrend" => "stormrend_blitz_boat",
                "valkyr"    => "valkyr_recon_skiff",
                _           => string.Empty
            },
            "destroyer" => _factionId switch
            {
                "arcloft"   => "arcloft_tempest_frigate",
                "bastion"   => "bastion_aegis_destroyer",
                "ironmarch" => "ironmarch_bulwark_destroyer",
                "kragmore"  => "kragmore_crusher_gunship",
                "stormrend" => "stormrend_tempest_destroyer",
                "valkyr"    => "valkyr_interceptor_cruiser",
                _           => string.Empty
            },
            "capital" => _factionId switch
            {
                "arcloft"   => "arcloft_stormcrest_carrier",
                "bastion"   => "bastion_citadel_ship",
                "ironmarch" => "ironmarch_battleship",
                "kragmore"  => "kragmore_titan_dreadnought",
                "stormrend" => "stormrend_stormfront_flagship",
                "valkyr"    => "valkyr_skyborne_carrier",
                _           => string.Empty
            },
            _ => string.Empty
        };
    }
}
