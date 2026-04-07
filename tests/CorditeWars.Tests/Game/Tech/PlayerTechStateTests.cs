using CorditeWars.Core;
using CorditeWars.Game.Tech;

namespace CorditeWars.Tests.Game.Tech;

/// <summary>
/// Tests for PlayerTechState — building tracking, upgrade tracking,
/// and research state machine.
/// </summary>
public class PlayerTechStateTests
{
    private static PlayerTechState CreateDefault()
    {
        return new PlayerTechState
        {
            PlayerId = 1,
            FactionId = "bastion"
        };
    }

    // ── Building Registration ───────────────────────────────────────────

    [Fact]
    public void RegisterBuilding_TracksType()
    {
        var tech = CreateDefault();
        tech.RegisterBuilding("bastion_barracks");
        Assert.True(tech.HasBuilding("bastion_barracks"));
    }

    [Fact]
    public void RegisterBuilding_MultipleInstances()
    {
        var tech = CreateDefault();
        tech.RegisterBuilding("bastion_barracks");
        tech.RegisterBuilding("bastion_barracks");
        Assert.True(tech.HasBuilding("bastion_barracks"));
    }

    [Fact]
    public void UnregisterBuilding_RemovesOnlyWhenLastDestroyed()
    {
        var tech = CreateDefault();
        tech.RegisterBuilding("bastion_barracks");
        tech.RegisterBuilding("bastion_barracks");

        tech.UnregisterBuilding("bastion_barracks");
        Assert.True(tech.HasBuilding("bastion_barracks"),
            "Should still have building after destroying one of two");

        tech.UnregisterBuilding("bastion_barracks");
        Assert.False(tech.HasBuilding("bastion_barracks"),
            "Should not have building after destroying both");
    }

    [Fact]
    public void UnregisterBuilding_NonExistent_DoesNothing()
    {
        var tech = CreateDefault();
        tech.UnregisterBuilding("nonexistent");
        Assert.False(tech.HasBuilding("nonexistent"));
    }

    [Fact]
    public void HasBuilding_ReturnsFalseForUnregistered()
    {
        var tech = CreateDefault();
        Assert.False(tech.HasBuilding("bastion_factory"));
    }

    [Fact]
    public void GetRegisteredBuildings_ReturnsAllTypes()
    {
        var tech = CreateDefault();
        tech.RegisterBuilding("bastion_barracks");
        tech.RegisterBuilding("bastion_factory");
        tech.RegisterBuilding("bastion_barracks"); // duplicate instance

        var buildings = tech.GetRegisteredBuildings();
        Assert.Equal(2, buildings.Count);
        Assert.Contains("bastion_barracks", buildings);
        Assert.Contains("bastion_factory", buildings);
    }

    // ── Upgrade Tracking ────────────────────────────────────────────────

    [Fact]
    public void HasUpgrade_ReturnsFalse_Initially()
    {
        var tech = CreateDefault();
        Assert.False(tech.HasUpgrade("some_upgrade"));
    }

    [Fact]
    public void GetCompletedUpgrades_InitiallyEmpty()
    {
        var tech = CreateDefault();
        Assert.Empty(tech.GetCompletedUpgrades());
    }

    // ── Research State Machine ──────────────────────────────────────────

    [Fact]
    public void StartResearch_SetsState()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "bastion_armor_plating",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(30)
        };

        bool started = tech.StartResearch("bastion_armor_plating", upgradeData);
        Assert.True(started);
        Assert.Equal("bastion_armor_plating", tech.CurrentResearch);
        Assert.Equal(FixedPoint.Zero, tech.ResearchProgress);
        Assert.Equal(FixedPoint.FromInt(30), tech.ResearchTarget);
    }

    [Fact]
    public void StartResearch_FailsWhenAlreadyResearching()
    {
        var tech = CreateDefault();
        var upgrade1 = new UpgradeData
        {
            Id = "upgrade_1",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(30)
        };
        var upgrade2 = new UpgradeData
        {
            Id = "upgrade_2",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(20)
        };

        tech.StartResearch("upgrade_1", upgrade1);
        bool started = tech.StartResearch("upgrade_2", upgrade2);
        Assert.False(started);
        Assert.Equal("upgrade_1", tech.CurrentResearch);
    }

    [Fact]
    public void TickResearch_IncreasesProgress()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "test_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(10)
        };

        tech.StartResearch("test_upgrade", upgradeData);
        string? completed = tech.TickResearch(FixedPoint.FromInt(5));

        Assert.Null(completed);
        Assert.Equal(FixedPoint.FromInt(5), tech.ResearchProgress);
    }

    [Fact]
    public void TickResearch_CompletesWhenTargetReached()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "test_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(10)
        };

        tech.StartResearch("test_upgrade", upgradeData);
        string? completed = tech.TickResearch(FixedPoint.FromInt(10));

        Assert.Equal("test_upgrade", completed);
        Assert.True(tech.HasUpgrade("test_upgrade"));
        Assert.Null(tech.CurrentResearch);
        Assert.Equal(FixedPoint.Zero, tech.ResearchProgress);
    }

    [Fact]
    public void TickResearch_CompletesWhenTargetExceeded()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "test_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(10)
        };

        tech.StartResearch("test_upgrade", upgradeData);
        string? completed = tech.TickResearch(FixedPoint.FromInt(15));

        Assert.Equal("test_upgrade", completed);
        Assert.True(tech.HasUpgrade("test_upgrade"));
    }

    [Fact]
    public void TickResearch_NoResearch_ReturnsNull()
    {
        var tech = CreateDefault();
        string? completed = tech.TickResearch(FixedPoint.FromInt(1));
        Assert.Null(completed);
    }

    [Fact]
    public void TickResearch_MultipleIncrements()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "slow_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(30)
        };

        tech.StartResearch("slow_upgrade", upgradeData);

        // Tick 10 at a time
        Assert.Null(tech.TickResearch(FixedPoint.FromInt(10)));
        Assert.Null(tech.TickResearch(FixedPoint.FromInt(10)));
        string? completed = tech.TickResearch(FixedPoint.FromInt(10));

        Assert.Equal("slow_upgrade", completed);
        Assert.True(tech.HasUpgrade("slow_upgrade"));
    }

    [Fact]
    public void CompletedUpgrade_AppearsInList()
    {
        var tech = CreateDefault();
        var upgradeData = new UpgradeData
        {
            Id = "finished_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(1)
        };

        tech.StartResearch("finished_upgrade", upgradeData);
        tech.TickResearch(FixedPoint.FromInt(1));

        var completed = tech.GetCompletedUpgrades();
        Assert.Single(completed);
        Assert.Contains("finished_upgrade", completed);
    }

    [Fact]
    public void AfterResearchCompletes_CanStartNew()
    {
        var tech = CreateDefault();
        var upgrade1 = new UpgradeData
        {
            Id = "upgrade_1",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5)
        };
        var upgrade2 = new UpgradeData
        {
            Id = "upgrade_2",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5)
        };

        tech.StartResearch("upgrade_1", upgrade1);
        tech.TickResearch(FixedPoint.FromInt(5));

        bool started = tech.StartResearch("upgrade_2", upgrade2);
        Assert.True(started);
        Assert.Equal("upgrade_2", tech.CurrentResearch);
    }
}
