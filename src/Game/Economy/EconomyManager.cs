using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.Economy;

/// <summary>
/// Per-player resource state: Cordite, Voltaic Charge, and supply.
/// All simulation values use FixedPoint for determinism.
/// </summary>
public sealed class PlayerEconomy
{
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;

    // ── Resources ───────────────────────────────────────────────────

    public FixedPoint Cordite { get; private set; }
    public FixedPoint VoltaicCharge { get; private set; }
    public int CurrentSupply { get; private set; }

    // ── Caps ────────────────────────────────────────────────────────

    public int MaxSupply { get; private set; }
    public FixedPoint VCCap { get; private set; }

    // ── Income Tracking ─────────────────────────────────────────────

    public FixedPoint CorditePerSecond { get; private set; }
    public FixedPoint VCPerSecond { get; private set; }
    public int ReactorCount { get; private set; }
    public int DepotCount { get; private set; }
    public int RefineryCount { get; private set; }

    // ── Initialization ──────────────────────────────────────────────

    /// <summary>
    /// Sets up starting resources for a new match.
    /// </summary>
    public void Initialize(FixedPoint startingCordite, int startingSupply, int maxSupply, int vcCap)
    {
        Cordite = startingCordite;
        VoltaicCharge = FixedPoint.Zero;
        CurrentSupply = 0;
        MaxSupply = maxSupply;
        VCCap = FixedPoint.FromInt(vcCap);
        ReactorCount = 0;
        DepotCount = 0;
        RefineryCount = 0;
        CorditePerSecond = FixedPoint.Zero;
        VCPerSecond = FixedPoint.Zero;
    }

    // ── Resource Mutations ──────────────────────────────────────────

    public void AddCordite(FixedPoint amount)
    {
        Cordite = Cordite + amount;
    }

    public void AddVC(FixedPoint amount)
    {
        VoltaicCharge = VoltaicCharge + amount;
        if (VoltaicCharge > VCCap)
            VoltaicCharge = VCCap;
    }

    public bool SpendCordite(FixedPoint amount)
    {
        if (Cordite < amount)
            return false;
        Cordite = Cordite - amount;
        return true;
    }

    public bool SpendVC(FixedPoint amount)
    {
        if (VoltaicCharge < amount)
            return false;
        VoltaicCharge = VoltaicCharge - amount;
        return true;
    }

    public bool CanAfford(int cordite, int vc)
    {
        return Cordite >= FixedPoint.FromInt(cordite) && VoltaicCharge >= FixedPoint.FromInt(vc);
    }

    /// <summary>
    /// Atomically deducts cordite, VC, and supply — or rejects if any is insufficient.
    /// </summary>
    public bool TryPurchase(int cordite, int vc, int supply)
    {
        FixedPoint corditeCost = FixedPoint.FromInt(cordite);
        FixedPoint vcCost = FixedPoint.FromInt(vc);

        if (Cordite < corditeCost)
            return false;
        if (VoltaicCharge < vcCost)
            return false;
        if (supply > 0 && CurrentSupply + supply > MaxSupply)
            return false;

        Cordite = Cordite - corditeCost;
        VoltaicCharge = VoltaicCharge - vcCost;
        if (supply > 0)
            CurrentSupply = CurrentSupply + supply;
        return true;
    }

    // ── Supply Management ───────────────────────────────────────────

    public void AddSupply(int amount)
    {
        MaxSupply = MaxSupply + amount;
    }

    public void RemoveSupply(int amount)
    {
        MaxSupply = MaxSupply - amount;
        if (MaxSupply < 0)
            MaxSupply = 0;
    }

    public bool ConsumeSupply(int amount)
    {
        if (CurrentSupply + amount > MaxSupply)
            return false;
        CurrentSupply = CurrentSupply + amount;
        return true;
    }

    public void FreeSupply(int amount)
    {
        CurrentSupply = CurrentSupply - amount;
        if (CurrentSupply < 0)
            CurrentSupply = 0;
    }

    // ── Passive Income Tick ─────────────────────────────────────────

    /// <summary>
    /// Called each simulation tick to generate passive resources.
    /// VC from reactors (all factions), Cordite from refineries (Bastion).
    /// </summary>
    public void UpdatePassiveIncome(FixedPoint deltaTime, FactionEconomyConfig config)
    {
        // VC income: ReactorCount * ReactorVCRate * deltaTime
        if (ReactorCount > 0)
        {
            FixedPoint vcIncome = config.ReactorVCRate * ReactorCount * deltaTime;
            AddVC(vcIncome);
        }

        // Cordite passive income from refineries (Bastion bonus)
        if (RefineryCount > 0 && config.RefineryPassiveIncome > FixedPoint.Zero)
        {
            FixedPoint corditeIncome = config.RefineryPassiveIncome * RefineryCount * deltaTime;
            AddCordite(corditeIncome);
        }

        // Update display rates
        VCPerSecond = config.ReactorVCRate * ReactorCount;
        CorditePerSecond = config.RefineryPassiveIncome * RefineryCount;
    }

    // ── Building Registration ───────────────────────────────────────

    public void RegisterReactor() { ReactorCount++; }
    public void UnregisterReactor() { if (ReactorCount > 0) ReactorCount--; }

    public void RegisterRefinery() { RefineryCount++; }
    public void UnregisterRefinery() { if (RefineryCount > 0) RefineryCount--; }

    public void RegisterDepot(int supplyPerDepot)
    {
        DepotCount++;
        AddSupply(supplyPerDepot);
    }

    public void UnregisterDepot(int supplyPerDepot)
    {
        if (DepotCount > 0)
        {
            DepotCount--;
            RemoveSupply(supplyPerDepot);
        }
    }
}

/// <summary>
/// Central economy manager. Owns per-player economy state and processes
/// passive income each tick. Provides transaction APIs for building/unit purchases.
/// </summary>
public sealed partial class EconomyManager : Node
{
    private SortedList<int, PlayerEconomy> _players = new();
    private SortedList<string, FactionEconomyConfig> _configs = new();

    // External references set during initialization
    private BuildingRegistry? _buildingRegistry;
    private SortedList<string, UnitData>? _unitLookup;

    private static readonly FixedPoint StartingCordite = FixedPoint.FromInt(5000);
    private static readonly int StartingSupply = 10;

    // ── Initialization ──────────────────────────────────────────────

    public void Initialize(
        SortedList<string, FactionEconomyConfig> configs,
        BuildingRegistry? buildingRegistry = null,
        SortedList<string, UnitData>? unitLookup = null)
    {
        _configs = configs;
        _buildingRegistry = buildingRegistry;
        _unitLookup = unitLookup;
    }

    public void AddPlayer(int playerId, string factionId)
    {
        if (_players.ContainsKey(playerId))
        {
            GD.PushWarning($"[EconomyManager] Player {playerId} already registered.");
            return;
        }

        if (!_configs.TryGetValue(factionId, out var config))
        {
            GD.PushError($"[EconomyManager] Unknown faction '{factionId}' for player {playerId}.");
            return;
        }

        var economy = new PlayerEconomy
        {
            PlayerId = playerId,
            FactionId = factionId
        };
        economy.Initialize(StartingCordite, StartingSupply, config.MaxSupply, config.VCCap);

        _players.Add(playerId, economy);
        GD.Print($"[EconomyManager] Player {playerId} ({factionId}) added — " +
                 $"Cordite: {StartingCordite}, Supply: {StartingSupply}/{config.MaxSupply}.");
    }

    // ── Queries ──────────────────────────────────────────────────────

    public PlayerEconomy? GetPlayer(int playerId)
    {
        if (_players.TryGetValue(playerId, out var economy))
            return economy;
        return null;
    }

    public FactionEconomyConfig? GetFactionConfig(string factionId)
    {
        if (_configs.TryGetValue(factionId, out var config))
            return config;
        return null;
    }

    // ── Tick Processing ─────────────────────────────────────────────

    /// <summary>
    /// Called each simulation tick. Updates passive income for all players.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            PlayerEconomy player = _players.Values[i];
            if (_configs.TryGetValue(player.FactionId, out var config))
            {
                player.UpdatePassiveIncome(deltaTime, config);
            }
        }
    }

    // ── Harvest Delivery ────────────────────────────────────────────

    /// <summary>
    /// Called when a harvester returns to a refinery and delivers Cordite.
    /// </summary>
    public void DeliverHarvest(int playerId, int corditeAmount)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return;

        player.AddCordite(FixedPoint.FromInt(corditeAmount));
        EventBus.Instance?.EmitHarvesterDelivered(playerId, corditeAmount);
    }

    // ── Purchase Transactions ───────────────────────────────────────

    /// <summary>
    /// Attempts to purchase a unit. Checks cost + supply atomically.
    /// Returns false and emits InsufficientFunds if the player cannot afford it.
    /// </summary>
    public bool TryBuildUnit(int playerId, UnitData unitData)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return false;

        if (!player.TryPurchase(unitData.Cost, unitData.SecondaryCost, unitData.PopulationCost))
        {
            EventBus.Instance?.EmitInsufficientFunds(playerId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to purchase a unit by type ID lookup.
    /// </summary>
    public bool TryBuildUnit(int playerId, string unitTypeId)
    {
        if (_unitLookup == null || !_unitLookup.TryGetValue(unitTypeId, out var unitData))
        {
            GD.PushWarning($"[EconomyManager] Unit '{unitTypeId}' not found in lookup.");
            return false;
        }

        return TryBuildUnit(playerId, unitData);
    }

    /// <summary>
    /// Attempts to purchase a building. Checks cost atomically (buildings use 0 supply).
    /// Returns false and emits InsufficientFunds if the player cannot afford it.
    /// </summary>
    public bool TryBuildBuilding(int playerId, BuildingData buildingData)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return false;

        if (!player.TryPurchase(buildingData.Cost, buildingData.SecondaryCost, 0))
        {
            EventBus.Instance?.EmitInsufficientFunds(playerId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to purchase a building by type ID lookup.
    /// </summary>
    public bool TryBuildBuilding(int playerId, string buildingTypeId)
    {
        if (_buildingRegistry == null || !_buildingRegistry.HasBuilding(buildingTypeId))
        {
            GD.PushWarning($"[EconomyManager] Building '{buildingTypeId}' not found in registry.");
            return false;
        }

        BuildingData data = _buildingRegistry.GetBuilding(buildingTypeId);
        return TryBuildBuilding(playerId, data);
    }

    // ── Building Completion Hooks ───────────────────────────────────

    /// <summary>
    /// Called when a building finishes construction. Registers it with the
    /// player economy for reactors, refineries, and depots.
    /// </summary>
    public void OnBuildingCompleted(int playerId, BuildingData buildingData)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return;

        // Register economy-relevant buildings
        if (buildingData.VCGeneration > FixedPoint.Zero)
            player.RegisterReactor();

        if (buildingData.PassiveIncome > FixedPoint.Zero)
            player.RegisterRefinery();

        if (buildingData.SupplyProvided > 0)
            player.RegisterDepot(buildingData.SupplyProvided);

        EventBus.Instance?.EmitEconomyBuildingCompleted(playerId, buildingData.Id);
    }

    /// <summary>
    /// Called when a building is destroyed. Unregisters it from the player economy.
    /// </summary>
    public void OnBuildingDestroyed(int playerId, BuildingData buildingData)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return;

        if (buildingData.VCGeneration > FixedPoint.Zero)
            player.UnregisterReactor();

        if (buildingData.PassiveIncome > FixedPoint.Zero)
            player.UnregisterRefinery();

        if (buildingData.SupplyProvided > 0)
            player.UnregisterDepot(buildingData.SupplyProvided);
    }

    /// <summary>
    /// Called when a unit is destroyed to free its supply cost.
    /// </summary>
    public void OnUnitDestroyed(int playerId, int populationCost)
    {
        if (!_players.TryGetValue(playerId, out var player))
            return;

        player.FreeSupply(populationCost);
    }
}
