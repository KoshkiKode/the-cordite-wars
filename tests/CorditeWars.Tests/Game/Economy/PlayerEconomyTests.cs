using UnnamedRTS.Core;
using UnnamedRTS.Game.Economy;

namespace CorditeWars.Tests.Game.Economy;

/// <summary>
/// Tests for PlayerEconomy — per-player resource state management.
/// </summary>
public class PlayerEconomyTests
{
    private static PlayerEconomy CreateDefault()
    {
        var economy = new PlayerEconomy
        {
            PlayerId = 1,
            FactionId = "bastion"
        };
        economy.Initialize(
            FixedPoint.FromInt(5000),
            startingSupply: 10,
            maxSupply: 200,
            vcCap: 500
        );
        return economy;
    }

    // ── Initialization ──────────────────────────────────────────────────

    [Fact]
    public void Initialize_SetsStartingResources()
    {
        var economy = CreateDefault();
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
        Assert.Equal(FixedPoint.Zero, economy.VoltaicCharge);
        Assert.Equal(0, economy.CurrentSupply);
        Assert.Equal(200, economy.MaxSupply);
        Assert.Equal(FixedPoint.FromInt(500), economy.VCCap);
    }

    // ── Cordite ─────────────────────────────────────────────────────────

    [Fact]
    public void AddCordite_IncreasesBalance()
    {
        var economy = CreateDefault();
        economy.AddCordite(FixedPoint.FromInt(1000));
        Assert.Equal(FixedPoint.FromInt(6000), economy.Cordite);
    }

    [Fact]
    public void SpendCordite_Success()
    {
        var economy = CreateDefault();
        bool result = economy.SpendCordite(FixedPoint.FromInt(3000));
        Assert.True(result);
        Assert.Equal(FixedPoint.FromInt(2000), economy.Cordite);
    }

    [Fact]
    public void SpendCordite_InsufficientFunds_ReturnsFalse()
    {
        var economy = CreateDefault();
        bool result = economy.SpendCordite(FixedPoint.FromInt(6000));
        Assert.False(result);
        // Balance unchanged
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
    }

    [Fact]
    public void SpendCordite_ExactAmount_Succeeds()
    {
        var economy = CreateDefault();
        bool result = economy.SpendCordite(FixedPoint.FromInt(5000));
        Assert.True(result);
        Assert.Equal(FixedPoint.Zero, economy.Cordite);
    }

    // ── Voltaic Charge ──────────────────────────────────────────────────

    [Fact]
    public void AddVC_IncreasesBalance()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(100));
        Assert.Equal(FixedPoint.FromInt(100), economy.VoltaicCharge);
    }

    [Fact]
    public void AddVC_CappedAtVCCap()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(600));
        Assert.Equal(FixedPoint.FromInt(500), economy.VoltaicCharge);
    }

    [Fact]
    public void SpendVC_Success()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        bool result = economy.SpendVC(FixedPoint.FromInt(150));
        Assert.True(result);
        Assert.Equal(FixedPoint.FromInt(50), economy.VoltaicCharge);
    }

    [Fact]
    public void SpendVC_InsufficientFunds_ReturnsFalse()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(100));
        bool result = economy.SpendVC(FixedPoint.FromInt(200));
        Assert.False(result);
        Assert.Equal(FixedPoint.FromInt(100), economy.VoltaicCharge);
    }

    // ── CanAfford ───────────────────────────────────────────────────────

    [Fact]
    public void CanAfford_True_WhenSufficient()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        Assert.True(economy.CanAfford(1000, 100));
    }

    [Fact]
    public void CanAfford_False_InsufficientCordite()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        Assert.False(economy.CanAfford(6000, 100));
    }

    [Fact]
    public void CanAfford_False_InsufficientVC()
    {
        var economy = CreateDefault();
        Assert.False(economy.CanAfford(1000, 100));
    }

    // ── TryPurchase (atomic transaction) ────────────────────────────────

    [Fact]
    public void TryPurchase_Success_DeductsAll()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        bool result = economy.TryPurchase(1000, 50, 2);
        Assert.True(result);
        Assert.Equal(FixedPoint.FromInt(4000), economy.Cordite);
        Assert.Equal(FixedPoint.FromInt(150), economy.VoltaicCharge);
        Assert.Equal(2, economy.CurrentSupply);
    }

    [Fact]
    public void TryPurchase_InsufficientCordite_Atomic()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        bool result = economy.TryPurchase(6000, 50, 2);
        Assert.False(result);
        // Nothing changed
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
        Assert.Equal(FixedPoint.FromInt(200), economy.VoltaicCharge);
        Assert.Equal(0, economy.CurrentSupply);
    }

    [Fact]
    public void TryPurchase_InsufficientVC_Atomic()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(50));
        bool result = economy.TryPurchase(1000, 100, 2);
        Assert.False(result);
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
        Assert.Equal(FixedPoint.FromInt(50), economy.VoltaicCharge);
        Assert.Equal(0, economy.CurrentSupply);
    }

    [Fact]
    public void TryPurchase_InsufficientSupply_Atomic()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(200));
        // Max supply is 200, try to buy supply of 201
        bool result = economy.TryPurchase(100, 50, 201);
        Assert.False(result);
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
        Assert.Equal(FixedPoint.FromInt(200), economy.VoltaicCharge);
        Assert.Equal(0, economy.CurrentSupply);
    }

    [Fact]
    public void TryPurchase_ZeroSupply_SkipsSupplyCheck()
    {
        var economy = CreateDefault();
        bool result = economy.TryPurchase(100, 0, 0);
        Assert.True(result);
        Assert.Equal(FixedPoint.FromInt(4900), economy.Cordite);
        Assert.Equal(0, economy.CurrentSupply);
    }

    // ── Supply Management ───────────────────────────────────────────────

    [Fact]
    public void ConsumeSupply_Success()
    {
        var economy = CreateDefault();
        bool result = economy.ConsumeSupply(5);
        Assert.True(result);
        Assert.Equal(5, economy.CurrentSupply);
    }

    [Fact]
    public void ConsumeSupply_ExceedsMax_ReturnsFalse()
    {
        var economy = CreateDefault();
        bool result = economy.ConsumeSupply(201);
        Assert.False(result);
        Assert.Equal(0, economy.CurrentSupply);
    }

    [Fact]
    public void FreeSupply_Decreases()
    {
        var economy = CreateDefault();
        economy.ConsumeSupply(10);
        economy.FreeSupply(3);
        Assert.Equal(7, economy.CurrentSupply);
    }

    [Fact]
    public void FreeSupply_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.ConsumeSupply(5);
        economy.FreeSupply(100);
        Assert.Equal(0, economy.CurrentSupply);
    }

    [Fact]
    public void AddSupply_IncreasesMax()
    {
        var economy = CreateDefault();
        economy.AddSupply(20);
        Assert.Equal(220, economy.MaxSupply);
    }

    [Fact]
    public void RemoveSupply_DecreasesMax()
    {
        var economy = CreateDefault();
        economy.RemoveSupply(50);
        Assert.Equal(150, economy.MaxSupply);
    }

    [Fact]
    public void RemoveSupply_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.RemoveSupply(300);
        Assert.Equal(0, economy.MaxSupply);
    }

    // ── Building Registration ───────────────────────────────────────────

    [Fact]
    public void RegisterReactor_IncrementsCount()
    {
        var economy = CreateDefault();
        economy.RegisterReactor();
        Assert.Equal(1, economy.ReactorCount);
        economy.RegisterReactor();
        Assert.Equal(2, economy.ReactorCount);
    }

    [Fact]
    public void UnregisterReactor_DecrementsCount()
    {
        var economy = CreateDefault();
        economy.RegisterReactor();
        economy.RegisterReactor();
        economy.UnregisterReactor();
        Assert.Equal(1, economy.ReactorCount);
    }

    [Fact]
    public void UnregisterReactor_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.UnregisterReactor();
        Assert.Equal(0, economy.ReactorCount);
    }

    [Fact]
    public void RegisterDepot_AddsSupply()
    {
        var economy = CreateDefault();
        economy.RegisterDepot(20);
        Assert.Equal(1, economy.DepotCount);
        Assert.Equal(220, economy.MaxSupply);
    }

    [Fact]
    public void UnregisterDepot_RemovesSupply()
    {
        var economy = CreateDefault();
        economy.RegisterDepot(20);
        economy.UnregisterDepot(20);
        Assert.Equal(0, economy.DepotCount);
        Assert.Equal(200, economy.MaxSupply);
    }

    // ── Passive Income ──────────────────────────────────────────────────

    [Fact]
    public void UpdatePassiveIncome_ReactorsGenerateVC()
    {
        var economy = CreateDefault();
        economy.RegisterReactor();
        economy.RegisterReactor();

        var config = new FactionEconomyConfig
        {
            FactionId = "bastion",
            ReactorVCRate = FixedPoint.FromInt(7),
            RefineryPassiveIncome = FixedPoint.Zero,
            HarvesterSpeed = FixedPoint.FromFloat(0.35f),
            HarvesterCapacity = 500,
            HarvesterMovementClass = "LightVehicle",
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1000,
            MaxSupply = 200,
            MaxDepots = 10,
            VCCap = 500
        };

        // 2 reactors × 7 VC/sec × 1 second = 14 VC
        economy.UpdatePassiveIncome(FixedPoint.One, config);

        float vc = economy.VoltaicCharge.ToFloat();
        Assert.True(
            Math.Abs(vc - 14.0f) < 0.1f,
            $"Expected ~14 VC, got {vc}");
    }

    [Fact]
    public void UpdatePassiveIncome_RefineryGeneratesCordite_Bastion()
    {
        var economy = CreateDefault();
        economy.RegisterRefinery();

        var config = new FactionEconomyConfig
        {
            FactionId = "bastion",
            ReactorVCRate = FixedPoint.FromInt(7),
            RefineryPassiveIncome = FixedPoint.FromInt(15),
            HarvesterSpeed = FixedPoint.FromFloat(0.35f),
            HarvesterCapacity = 500,
            HarvesterMovementClass = "LightVehicle",
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 1000,
            MaxSupply = 200,
            MaxDepots = 10,
            VCCap = 500
        };

        // 1 refinery × 15 Cordite/sec × 1 second = 15 Cordite
        economy.UpdatePassiveIncome(FixedPoint.One, config);

        float cordite = economy.Cordite.ToFloat();
        Assert.True(
            Math.Abs(cordite - 5015.0f) < 0.1f,
            $"Expected ~5015 Cordite, got {cordite}");
    }
}
