using UnnamedRTS.Core;
using UnnamedRTS.Game.Economy;

namespace CorditeWars.Tests.Game.Economy;

/// <summary>
/// Tests for FactionEconomyConfig factory and validation.
/// Ensures all six factions have valid, sensible configurations.
/// </summary>
public class FactionEconomyConfigTests
{
    private readonly SortedList<string, FactionEconomyConfig> _configs
        = FactionEconomyConfigs.CreateAll();

    private static readonly string[] ExpectedFactions =
    {
        "arcloft", "bastion", "ironmarch", "kragmore", "stormrend", "valkyr"
    };

    [Fact]
    public void CreateAll_ReturnsAllSixFactions()
    {
        Assert.Equal(6, _configs.Count);
        foreach (string factionId in ExpectedFactions)
        {
            Assert.True(_configs.ContainsKey(factionId),
                $"Missing faction config: {factionId}");
        }
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void FactionId_MatchesKey(string factionId)
    {
        Assert.Equal(factionId, _configs[factionId].FactionId);
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void HarvesterSpeed_IsPositive(string factionId)
    {
        Assert.True(_configs[factionId].HarvesterSpeed > FixedPoint.Zero,
            $"{factionId} harvester speed should be positive");
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void HarvesterCapacity_IsPositive(string factionId)
    {
        Assert.True(_configs[factionId].HarvesterCapacity > 0,
            $"{factionId} harvester capacity should be positive");
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void MaxSupply_IsPositive(string factionId)
    {
        Assert.True(_configs[factionId].MaxSupply > 0,
            $"{factionId} max supply should be positive");
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void ReactorCost_IsPositive(string factionId)
    {
        Assert.True(_configs[factionId].ReactorCost > 0,
            $"{factionId} reactor cost should be positive");
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void VCCap_IsPositive(string factionId)
    {
        Assert.True(_configs[factionId].VCCap > 0,
            $"{factionId} VC cap should be positive");
    }

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void HarvesterMovementClass_IsNotEmpty(string factionId)
    {
        Assert.False(
            string.IsNullOrEmpty(_configs[factionId].HarvesterMovementClass),
            $"{factionId} harvester movement class should not be empty");
    }

    // ── Faction-specific balance assertions ─────────────────────────────

    [Fact]
    public void Bastion_HasRefineryPassiveIncome()
    {
        Assert.True(_configs["bastion"].RefineryPassiveIncome > FixedPoint.Zero,
            "Bastion should have passive refinery income");
    }

    [Fact]
    public void NonBastion_NoRefineryPassiveIncome()
    {
        foreach (string factionId in ExpectedFactions)
        {
            if (factionId == "bastion") continue;
            Assert.True(_configs[factionId].RefineryPassiveIncome == FixedPoint.Zero,
                $"{factionId} should have zero refinery passive income");
        }
    }

    [Fact]
    public void Ironmarch_HasRefineryTurret()
    {
        Assert.True(_configs["ironmarch"].RefineryHasTurret);
    }

    [Fact]
    public void Ironmarch_HasHigherRefineryHP()
    {
        Assert.True(_configs["ironmarch"].RefineryHPMultiplier > FixedPoint.One);
    }

    [Fact]
    public void Kragmore_HasHighCapacitySlowHarvester()
    {
        // Kragmore harvesters are slow (0.1) but carry double (1000)
        Assert.Equal(1000, _configs["kragmore"].HarvesterCapacity);
        Assert.True(_configs["kragmore"].HarvesterSpeed < _configs["bastion"].HarvesterSpeed);
    }

    [Fact]
    public void AirFactions_UseHelicopterHarvesters()
    {
        Assert.Equal("Helicopter", _configs["arcloft"].HarvesterMovementClass);
        Assert.Equal("Helicopter", _configs["valkyr"].HarvesterMovementClass);
    }

    [Fact]
    public void CreateAll_IsDeterministic()
    {
        var configs1 = FactionEconomyConfigs.CreateAll();
        var configs2 = FactionEconomyConfigs.CreateAll();

        Assert.Equal(configs1.Count, configs2.Count);
        for (int i = 0; i < configs1.Count; i++)
        {
            Assert.Equal(configs1.Keys[i], configs2.Keys[i]);
            Assert.Equal(configs1.Values[i].MaxSupply, configs2.Values[i].MaxSupply);
        }
    }
}
