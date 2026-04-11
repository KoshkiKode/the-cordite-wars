using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Tech;

namespace CorditeWars.Tests.Game.Tech;

/// <summary>
/// Tests for UpgradeRegistry — programmatic registration and query logic,
/// including the tier-then-ID insertion sort in GetFactionUpgrades.
/// Uses the Register() overload designed for testing without Godot file-system APIs.
/// </summary>
public class UpgradeRegistryTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static UpgradeData MakeUpgrade(
        string id,
        string factionId,
        int tier = 1,
        int cost = 500,
        string prerequisiteBuilding = "",
        string[]? prerequisiteUpgrades = null,
        UpgradeEffect[]? effects = null)
    {
        return new UpgradeData
        {
            Id = id,
            DisplayName = id,
            FactionId = factionId,
            Tier = tier,
            Cost = cost,
            ResearchTime = FixedPoint.FromInt(30),
            PrerequisiteBuilding = prerequisiteBuilding,
            PrerequisiteUpgrades = prerequisiteUpgrades ?? System.Array.Empty<string>(),
            Effects = effects ?? System.Array.Empty<UpgradeEffect>()
        };
    }

    // ── Count / Empty state ──────────────────────────────────────────────

    [Fact]
    public void Count_EmptyRegistry_ReturnsZero()
    {
        var registry = new UpgradeRegistry();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void HasUpgrade_EmptyRegistry_ReturnsFalse()
    {
        var registry = new UpgradeRegistry();
        Assert.False(registry.HasUpgrade("speed_boost"));
    }

    // ── Register + HasUpgrade ────────────────────────────────────────────

    [Fact]
    public void Register_Single_IncreasesCount()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Register_Single_HasUpgradeReturnsTrue()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion"));
        Assert.True(registry.HasUpgrade("speed_boost"));
    }

    [Fact]
    public void Register_Multiple_AllReturnTrue()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion"));
        registry.Register(MakeUpgrade("armor_plating", "bastion"));
        registry.Register(MakeUpgrade("air_superiority", "valkyr"));

        Assert.True(registry.HasUpgrade("speed_boost"));
        Assert.True(registry.HasUpgrade("armor_plating"));
        Assert.True(registry.HasUpgrade("air_superiority"));
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    public void Register_DuplicateId_OverwritesExisting()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion", cost: 500));
        registry.Register(MakeUpgrade("speed_boost", "bastion", cost: 750));

        Assert.Equal(1, registry.Count);
        Assert.Equal(750, registry.GetUpgrade("speed_boost").Cost);
    }

    // ── GetUpgrade ───────────────────────────────────────────────────────

    [Fact]
    public void GetUpgrade_Registered_ReturnsCorrectData()
    {
        var registry = new UpgradeRegistry();
        var data = MakeUpgrade("speed_boost", "bastion", tier: 2, cost: 600);
        registry.Register(data);

        UpgradeData retrieved = registry.GetUpgrade("speed_boost");
        Assert.Equal("speed_boost", retrieved.Id);
        Assert.Equal("bastion", retrieved.FactionId);
        Assert.Equal(2, retrieved.Tier);
        Assert.Equal(600, retrieved.Cost);
    }

    [Fact]
    public void GetUpgrade_Unknown_ThrowsKeyNotFoundException()
    {
        var registry = new UpgradeRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetUpgrade("nonexistent"));
    }

    [Fact]
    public void GetUpgrade_AfterOverwrite_ReturnsUpdatedData()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion", cost: 500));
        registry.Register(MakeUpgrade("speed_boost", "bastion", cost: 900));
        Assert.Equal(900, registry.GetUpgrade("speed_boost").Cost);
    }

    // ── GetFactionUpgrades — faction filtering ───────────────────────────

    [Fact]
    public void GetFactionUpgrades_NoMatchingFaction_ReturnsEmpty()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion"));

        var result = registry.GetFactionUpgrades("valkyr");
        Assert.Empty(result);
    }

    [Fact]
    public void GetFactionUpgrades_ReturnsOnlyMatchingFaction()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("speed_boost", "bastion"));
        registry.Register(MakeUpgrade("armor_plating", "bastion"));
        registry.Register(MakeUpgrade("air_superiority", "valkyr"));

        var bastionUpgrades = registry.GetFactionUpgrades("bastion");
        Assert.Equal(2, bastionUpgrades.Count);
        Assert.All(bastionUpgrades, u => Assert.Equal("bastion", u.FactionId));
    }

    [Fact]
    public void GetFactionUpgrades_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new UpgradeRegistry();
        Assert.Empty(registry.GetFactionUpgrades("bastion"));
    }

    // ── GetFactionUpgrades — sort order (tier then ID) ───────────────────

    [Fact]
    public void GetFactionUpgrades_SortedByTierAscending()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("tier3_upgrade", "bastion", tier: 3));
        registry.Register(MakeUpgrade("tier1_upgrade", "bastion", tier: 1));
        registry.Register(MakeUpgrade("tier2_upgrade", "bastion", tier: 2));

        var result = registry.GetFactionUpgrades("bastion");
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Tier);
        Assert.Equal(2, result[1].Tier);
        Assert.Equal(3, result[2].Tier);
    }

    [Fact]
    public void GetFactionUpgrades_SameTier_SortedByIdAscending()
    {
        var registry = new UpgradeRegistry();
        // All tier 1 — should sort alphabetically by ID
        registry.Register(MakeUpgrade("c_upgrade", "bastion", tier: 1));
        registry.Register(MakeUpgrade("a_upgrade", "bastion", tier: 1));
        registry.Register(MakeUpgrade("b_upgrade", "bastion", tier: 1));

        var result = registry.GetFactionUpgrades("bastion");
        Assert.Equal(3, result.Count);
        Assert.Equal("a_upgrade", result[0].Id);
        Assert.Equal("b_upgrade", result[1].Id);
        Assert.Equal("c_upgrade", result[2].Id);
    }

    [Fact]
    public void GetFactionUpgrades_MixedTierAndId_SortsTierFirst()
    {
        var registry = new UpgradeRegistry();
        // z_upgrade tier 1 should come before a_upgrade tier 2
        registry.Register(MakeUpgrade("a_upgrade", "bastion", tier: 2));
        registry.Register(MakeUpgrade("z_upgrade", "bastion", tier: 1));

        var result = registry.GetFactionUpgrades("bastion");
        Assert.Equal("z_upgrade", result[0].Id); // tier 1 first
        Assert.Equal("a_upgrade", result[1].Id); // tier 2 second
    }

    [Fact]
    public void GetFactionUpgrades_SingleUpgrade_ReturnsSingleElement()
    {
        var registry = new UpgradeRegistry();
        registry.Register(MakeUpgrade("lone_upgrade", "arcloft", tier: 1));

        var result = registry.GetFactionUpgrades("arcloft");
        Assert.Single(result);
        Assert.Equal("lone_upgrade", result[0].Id);
    }

    // ── Effects stored correctly ─────────────────────────────────────────

    [Fact]
    public void Register_EffectsStoredCorrectly()
    {
        var registry = new UpgradeRegistry();
        var effects = new[]
        {
            new UpgradeEffect
            {
                TargetCategory = "Infantry",
                Stat = "Damage",
                Modifier = "add",
                Value = FixedPoint.FromInt(5)
            }
        };
        var upgrade = MakeUpgrade("damage_upgrade", "bastion", effects: effects);
        registry.Register(upgrade);

        var retrieved = registry.GetUpgrade("damage_upgrade");
        Assert.Single(retrieved.Effects);
        Assert.Equal("Infantry", retrieved.Effects[0].TargetCategory);
        Assert.Equal("Damage", retrieved.Effects[0].Stat);
        Assert.Equal("add", retrieved.Effects[0].Modifier);
        Assert.Equal(FixedPoint.FromInt(5), retrieved.Effects[0].Value);
    }

    // ── Prerequisites stored correctly ───────────────────────────────────

    [Fact]
    public void Register_PrerequisiteBuildingStoredCorrectly()
    {
        var registry = new UpgradeRegistry();
        var upgrade = MakeUpgrade(
            "advanced_weapons", "bastion",
            prerequisiteBuilding: "bastion_lab");
        registry.Register(upgrade);

        Assert.Equal("bastion_lab", registry.GetUpgrade("advanced_weapons").PrerequisiteBuilding);
    }

    [Fact]
    public void Register_PrerequisiteUpgradesStoredCorrectly()
    {
        var registry = new UpgradeRegistry();
        var upgrade = MakeUpgrade(
            "tier2_weapons", "bastion",
            prerequisiteUpgrades: new[] { "tier1_weapons", "basic_armor" });
        registry.Register(upgrade);

        var retrieved = registry.GetUpgrade("tier2_weapons");
        Assert.Equal(2, retrieved.PrerequisiteUpgrades.Length);
        Assert.Contains("tier1_weapons", retrieved.PrerequisiteUpgrades);
        Assert.Contains("basic_armor", retrieved.PrerequisiteUpgrades);
    }
}
