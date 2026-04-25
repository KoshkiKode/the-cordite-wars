using CorditeWars.Core;
using CorditeWars.Game.Economy;

namespace CorditeWars.Tests.Game.Economy;

/// <summary>
/// Extended tests for PlayerEconomy covering SetCordite/SetVC save-state restore,
/// refinery/reactor registration edge cases, passive income generation, and
/// income tracking.
/// </summary>
public class PlayerEconomyExtendedTests
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

    // ═══════════════════════════════════════════════════════════════════
    // SetCordite (saved-state restore)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SetCordite_PositiveAmount_SetsExactValue()
    {
        var economy = CreateDefault();
        economy.SetCordite(FixedPoint.FromInt(1234));
        Assert.Equal(FixedPoint.FromInt(1234), economy.Cordite);
    }

    [Fact]
    public void SetCordite_NegativeAmount_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.SetCordite(FixedPoint.FromInt(-100));
        Assert.Equal(FixedPoint.Zero, economy.Cordite);
    }

    [Fact]
    public void SetCordite_Zero_SetsZero()
    {
        var economy = CreateDefault();
        economy.SetCordite(FixedPoint.Zero);
        Assert.Equal(FixedPoint.Zero, economy.Cordite);
    }

    [Fact]
    public void SetCordite_DoesNotUpdateTotalCorditeIncome()
    {
        var economy = CreateDefault();
        int incomeBefore = economy.TotalCorditeIncome;
        economy.SetCordite(FixedPoint.FromInt(9999));
        Assert.Equal(incomeBefore, economy.TotalCorditeIncome);
    }

    [Fact]
    public void SetCordite_LargeValue_Sets()
    {
        var economy = CreateDefault();
        economy.SetCordite(FixedPoint.FromInt(999999));
        Assert.Equal(FixedPoint.FromInt(999999), economy.Cordite);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SetVC (saved-state restore)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SetVC_ValidAmount_SetsExactValue()
    {
        var economy = CreateDefault();
        economy.SetVC(FixedPoint.FromInt(200));
        Assert.Equal(FixedPoint.FromInt(200), economy.VoltaicCharge);
    }

    [Fact]
    public void SetVC_NegativeAmount_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.SetVC(FixedPoint.FromInt(-50));
        Assert.Equal(FixedPoint.Zero, economy.VoltaicCharge);
    }

    [Fact]
    public void SetVC_AboveCap_ClampsToVCCap()
    {
        var economy = CreateDefault(); // VCCap = 500
        economy.SetVC(FixedPoint.FromInt(800));
        Assert.Equal(FixedPoint.FromInt(500), economy.VoltaicCharge);
    }

    [Fact]
    public void SetVC_ExactlyCap_SetsCapValue()
    {
        var economy = CreateDefault();
        economy.SetVC(FixedPoint.FromInt(500));
        Assert.Equal(FixedPoint.FromInt(500), economy.VoltaicCharge);
    }

    [Fact]
    public void SetVC_Zero_SetsZero()
    {
        var economy = CreateDefault();
        economy.SetVC(FixedPoint.Zero);
        Assert.Equal(FixedPoint.Zero, economy.VoltaicCharge);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Refinery Registration
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterRefinery_IncrementsCount()
    {
        var economy = CreateDefault();
        economy.RegisterRefinery();
        Assert.Equal(1, economy.RefineryCount);
        economy.RegisterRefinery();
        Assert.Equal(2, economy.RefineryCount);
    }

    [Fact]
    public void UnregisterRefinery_DecrementsCount()
    {
        var economy = CreateDefault();
        economy.RegisterRefinery();
        economy.RegisterRefinery();
        economy.UnregisterRefinery();
        Assert.Equal(1, economy.RefineryCount);
    }

    [Fact]
    public void UnregisterRefinery_ClampsToZero()
    {
        var economy = CreateDefault();
        economy.UnregisterRefinery();
        Assert.Equal(0, economy.RefineryCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Passive Income
    // ═══════════════════════════════════════════════════════════════════

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
    public void UpdatePassiveIncome_NoReactors_NoVC()
    {
        var economy = CreateDefault();

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

        economy.UpdatePassiveIncome(FixedPoint.One, config);

        Assert.Equal(FixedPoint.Zero, economy.VoltaicCharge);
    }

    [Fact]
    public void UpdatePassiveIncome_RefineryGeneratesCordite()
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

    [Fact]
    public void UpdatePassiveIncome_NoPassiveIncome_CorditeUnchanged()
    {
        var economy = CreateDefault();
        economy.RegisterRefinery();

        var config = new FactionEconomyConfig
        {
            FactionId = "valkyr",
            ReactorVCRate = FixedPoint.FromInt(3),
            RefineryPassiveIncome = FixedPoint.Zero,
            HarvesterSpeed = FixedPoint.FromFloat(0.4f),
            HarvesterCapacity = 600,
            HarvesterMovementClass = "LightVehicle",
            RefineryHPMultiplier = FixedPoint.One,
            RefineryHasTurret = false,
            ReactorCost = 800,
            MaxSupply = 180,
            MaxDepots = 10,
            VCCap = 600
        };

        FixedPoint corditeBefore = economy.Cordite;
        economy.UpdatePassiveIncome(FixedPoint.One, config);

        Assert.Equal(corditeBefore, economy.Cordite);
    }

    [Fact]
    public void UpdatePassiveIncome_UpdatesDisplayRates()
    {
        var economy = CreateDefault();
        economy.RegisterReactor();
        economy.RegisterRefinery();

        var config = new FactionEconomyConfig
        {
            FactionId = "bastion",
            ReactorVCRate = FixedPoint.FromInt(5),
            RefineryPassiveIncome = FixedPoint.FromInt(10),
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

        economy.UpdatePassiveIncome(FixedPoint.One, config);

        // VCPerSecond = ReactorVCRate * ReactorCount = 5 * 1
        Assert.Equal(FixedPoint.FromInt(5), economy.VCPerSecond);
        // CorditePerSecond = RefineryPassiveIncome * RefineryCount = 10 * 1
        Assert.Equal(FixedPoint.FromInt(10), economy.CorditePerSecond);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Income Tracking
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddCordite_TracksIncome()
    {
        var economy = CreateDefault();
        int incomeBefore = economy.TotalCorditeIncome;
        economy.AddCordite(FixedPoint.FromInt(300));
        Assert.Equal(incomeBefore + 300, economy.TotalCorditeIncome);
    }

    [Fact]
    public void AddCordite_MultipleAdds_AccumulatesIncome()
    {
        var economy = CreateDefault();
        economy.AddCordite(FixedPoint.FromInt(100));
        economy.AddCordite(FixedPoint.FromInt(200));
        Assert.Equal(300, economy.TotalCorditeIncome);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Depot Registration Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UnregisterDepot_WhenNoDepots_DoesNothing()
    {
        var economy = CreateDefault();
        int maxBefore = economy.MaxSupply;
        economy.UnregisterDepot(20);
        Assert.Equal(0, economy.DepotCount);
        Assert.Equal(maxBefore, economy.MaxSupply); // Unchanged — no depot to remove
    }

    [Fact]
    public void RegisterDepot_Multiple_AccumulatesSupply()
    {
        var economy = CreateDefault();
        economy.RegisterDepot(20);
        economy.RegisterDepot(20);
        Assert.Equal(2, economy.DepotCount);
        Assert.Equal(240, economy.MaxSupply); // 200 + 20 + 20
    }

    // ═══════════════════════════════════════════════════════════════════
    // TryPurchase — Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TryPurchase_ExactAmounts_Succeeds()
    {
        var economy = CreateDefault();
        economy.AddVC(FixedPoint.FromInt(500));

        // Use exactly all cordite and VC
        bool result = economy.TryPurchase(5000, 500, 0);
        Assert.True(result);
        Assert.Equal(FixedPoint.Zero, economy.Cordite);
        Assert.Equal(FixedPoint.Zero, economy.VoltaicCharge);
    }

    [Fact]
    public void TryPurchase_ZeroCost_Succeeds()
    {
        var economy = CreateDefault();
        bool result = economy.TryPurchase(0, 0, 0);
        Assert.True(result);
        Assert.Equal(FixedPoint.FromInt(5000), economy.Cordite);
    }

    [Fact]
    public void TryPurchase_SupplyAtExactCap_Fails()
    {
        var economy = CreateDefault(); // Max supply = 200
        // Fill supply to cap
        economy.ConsumeSupply(200);
        Assert.Equal(200, economy.CurrentSupply);

        bool result = economy.TryPurchase(100, 0, 1);
        Assert.False(result); // Would exceed cap
    }

    [Fact]
    public void TryPurchase_SupplyNegativeCheck_SkippedWhenZero()
    {
        // When supply cost is 0, supply check is skipped even at cap
        var economy = CreateDefault();
        economy.ConsumeSupply(200);

        bool result = economy.TryPurchase(100, 0, 0);
        Assert.True(result);
    }
}
