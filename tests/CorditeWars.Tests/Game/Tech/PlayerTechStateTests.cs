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

    // ── CanResearchUpgrade ──────────────────────────────────────────────────

    [Fact]
    public void CanResearchUpgrade_AlreadyCompleted_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgradeData = new UpgradeData
        {
            Id = "speed_upgrade",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5)
        };
        registry.Register(upgradeData);

        // Complete the upgrade via research
        tech.StartResearch("speed_upgrade", upgradeData);
        tech.TickResearch(FixedPoint.FromInt(10)); // exceeds target

        // Already completed → should return false
        bool result = tech.CanResearchUpgrade("speed_upgrade", registry);
        Assert.False(result, "Should not be able to research an already-completed upgrade");
    }

    [Fact]
    public void CanResearchUpgrade_CurrentlyResearching_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgradeA = new UpgradeData
        {
            Id = "upgrade_a",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(10)
        };
        var upgradeB = new UpgradeData
        {
            Id = "upgrade_b",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(10)
        };
        registry.Register(upgradeA);
        registry.Register(upgradeB);

        tech.StartResearch("upgrade_a", upgradeA);

        // Currently researching upgrade_a → cannot queue upgrade_b
        bool result = tech.CanResearchUpgrade("upgrade_b", registry);
        Assert.False(result, "Should not be able to start research while another is in progress");
    }

    [Fact]
    public void CanResearchUpgrade_NotInRegistry_ReturnsFalse()
    {
        var tech = CreateDefault();
        var emptyRegistry = new CorditeWars.Game.Tech.UpgradeRegistry();

        bool result = tech.CanResearchUpgrade("nonexistent_upgrade", emptyRegistry);
        Assert.False(result, "Should return false when upgrade is not registered");
    }

    [Fact]
    public void CanResearchUpgrade_WrongFaction_ReturnsFalse()
    {
        var tech = CreateDefault(); // FactionId = "bastion"
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgrade = new UpgradeData
        {
            Id = "arcloft_boost",
            FactionId = "arcloft", // different faction
            ResearchTime = FixedPoint.FromInt(5)
        };
        registry.Register(upgrade);

        bool result = tech.CanResearchUpgrade("arcloft_boost", registry);
        Assert.False(result, "Should return false for an upgrade belonging to a different faction");
    }

    [Fact]
    public void CanResearchUpgrade_PrerequisiteBuildingMissing_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgrade = new UpgradeData
        {
            Id = "advanced_weapons",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5),
            PrerequisiteBuilding = "bastion_research_lab"
        };
        registry.Register(upgrade);

        // Player has NOT built the prerequisite building
        bool result = tech.CanResearchUpgrade("advanced_weapons", registry);
        Assert.False(result, "Should return false when prerequisite building is not built");
    }

    [Fact]
    public void CanResearchUpgrade_PrerequisiteBuildingPresent_NoOtherPrereqs_ReturnsTrue()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgrade = new UpgradeData
        {
            Id = "armor_plating",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5),
            PrerequisiteBuilding = "bastion_factory",
            PrerequisiteUpgrades = []
        };
        registry.Register(upgrade);

        tech.RegisterBuilding("bastion_factory");

        bool result = tech.CanResearchUpgrade("armor_plating", registry);
        Assert.True(result, "Should return true when all prerequisites are met");
    }

    [Fact]
    public void CanResearchUpgrade_PrerequisiteUpgradeMissing_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var upgrade = new UpgradeData
        {
            Id = "tier2_weapons",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5),
            PrerequisiteBuilding = string.Empty,
            PrerequisiteUpgrades = ["tier1_weapons"]
        };
        registry.Register(upgrade);

        // tier1_weapons not completed
        bool result = tech.CanResearchUpgrade("tier2_weapons", registry);
        Assert.False(result, "Should return false when prerequisite upgrade is not completed");
    }

    [Fact]
    public void CanResearchUpgrade_AllPrerequisitesMet_ReturnsTrue()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Tech.UpgradeRegistry();
        var tier1 = new UpgradeData
        {
            Id = "tier1_weapons",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(1)
        };
        var tier2 = new UpgradeData
        {
            Id = "tier2_weapons",
            FactionId = "bastion",
            ResearchTime = FixedPoint.FromInt(5),
            PrerequisiteUpgrades = ["tier1_weapons"]
        };
        registry.Register(tier1);
        registry.Register(tier2);

        // Complete tier1
        tech.StartResearch("tier1_weapons", tier1);
        tech.TickResearch(FixedPoint.FromInt(5));

        bool result = tech.CanResearchUpgrade("tier2_weapons", registry);
        Assert.True(result, "Should return true once all prerequisite upgrades are completed");
    }

    // ── CanBuildBuilding ────────────────────────────────────────────────────

    [Fact]
    public void CanBuildBuilding_UnknownBuilding_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();

        bool result = tech.CanBuildBuilding("nonexistent_building", registry);
        Assert.False(result, "Should return false when building is not in registry");
    }

    [Fact]
    public void CanBuildBuilding_NoPrerequisites_ReturnsTrue()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();
        var buildingData = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_barracks",
            FactionId = "bastion",
            Cost = 500,
            Prerequisites = new System.Collections.Generic.List<string>()
        };
        registry.Register(buildingData);

        bool result = tech.CanBuildBuilding("bastion_barracks", registry);
        Assert.True(result, "Should return true when building has no prerequisites");
    }

    [Fact]
    public void CanBuildBuilding_PrerequisiteMissing_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();
        var buildingData = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_tech_lab",
            FactionId = "bastion",
            Cost = 1500,
            Prerequisites = new System.Collections.Generic.List<string> { "bastion_barracks" }
        };
        registry.Register(buildingData);

        // Player has NOT built bastion_barracks
        bool result = tech.CanBuildBuilding("bastion_tech_lab", registry);
        Assert.False(result, "Should return false when prerequisite building is not constructed");
    }

    [Fact]
    public void CanBuildBuilding_PrerequisiteMet_ReturnsTrue()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();
        var prerequisite = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_barracks",
            FactionId = "bastion",
            Cost = 500,
            Prerequisites = new System.Collections.Generic.List<string>()
        };
        var advanced = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_tech_lab",
            FactionId = "bastion",
            Cost = 1500,
            Prerequisites = new System.Collections.Generic.List<string> { "bastion_barracks" }
        };
        registry.Register(prerequisite);
        registry.Register(advanced);

        tech.RegisterBuilding("bastion_barracks");

        bool result = tech.CanBuildBuilding("bastion_tech_lab", registry);
        Assert.True(result, "Should return true when all prerequisite buildings are present");
    }

    [Fact]
    public void CanBuildBuilding_MultiplePrerequisites_AllMet_ReturnsTrue()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();
        var superWeapon = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_superweapon",
            FactionId = "bastion",
            Cost = 5000,
            Prerequisites = new System.Collections.Generic.List<string>
            {
                "bastion_barracks",
                "bastion_factory",
                "bastion_reactor"
            }
        };
        registry.Register(superWeapon);

        tech.RegisterBuilding("bastion_barracks");
        tech.RegisterBuilding("bastion_factory");
        tech.RegisterBuilding("bastion_reactor");

        bool result = tech.CanBuildBuilding("bastion_superweapon", registry);
        Assert.True(result, "Should return true when all multiple prerequisites are met");
    }

    [Fact]
    public void CanBuildBuilding_MultiplePrerequisites_OneMissing_ReturnsFalse()
    {
        var tech = CreateDefault();
        var registry = new CorditeWars.Game.Economy.BuildingRegistry();
        var superWeapon = new CorditeWars.Game.Buildings.BuildingData
        {
            Id = "bastion_superweapon",
            FactionId = "bastion",
            Cost = 5000,
            Prerequisites = new System.Collections.Generic.List<string>
            {
                "bastion_barracks",
                "bastion_factory",
                "bastion_reactor"
            }
        };
        registry.Register(superWeapon);

        tech.RegisterBuilding("bastion_barracks");
        tech.RegisterBuilding("bastion_factory");
        // NOT registering bastion_reactor

        bool result = tech.CanBuildBuilding("bastion_superweapon", registry);
        Assert.False(result, "Should return false when at least one prerequisite is missing");
    }
}
