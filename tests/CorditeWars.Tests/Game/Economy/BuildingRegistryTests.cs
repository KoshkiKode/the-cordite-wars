using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;

namespace CorditeWars.Tests.Game.Economy;

/// <summary>
/// Tests for BuildingRegistry — programmatic registration and query logic.
/// Uses the Register() overload specifically designed for testing without
/// Godot's file-system APIs.
/// </summary>
public class BuildingRegistryTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static BuildingData MakeBuilding(
        string id,
        string factionId,
        int cost = 500,
        IEnumerable<string>? prerequisites = null)
    {
        return new BuildingData
        {
            Id = id,
            DisplayName = id,
            FactionId = factionId,
            Cost = cost,
            MaxHealth = FixedPoint.FromInt(1000),
            BuildTime = FixedPoint.FromInt(30),
            Prerequisites = prerequisites != null
                ? new List<string>(prerequisites)
                : new List<string>()
        };
    }

    // ── Count / Empty state ──────────────────────────────────────────────

    [Fact]
    public void Count_EmptyRegistry_ReturnsZero()
    {
        var registry = new BuildingRegistry();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void HasBuilding_EmptyRegistry_ReturnsFalse()
    {
        var registry = new BuildingRegistry();
        Assert.False(registry.HasBuilding("bastion_barracks"));
    }

    // ── Register + HasBuilding ───────────────────────────────────────────

    [Fact]
    public void Register_Single_IncreasesCount()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Register_Single_HasBuildingReturnsTrue()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));
        Assert.True(registry.HasBuilding("bastion_barracks"));
    }

    [Fact]
    public void Register_Multiple_AllReturnTrue()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));
        registry.Register(MakeBuilding("bastion_factory", "bastion"));
        registry.Register(MakeBuilding("valkyr_airfield", "valkyr"));

        Assert.True(registry.HasBuilding("bastion_barracks"));
        Assert.True(registry.HasBuilding("bastion_factory"));
        Assert.True(registry.HasBuilding("valkyr_airfield"));
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    public void Register_DuplicateId_OverwritesExisting()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion", cost: 500));
        registry.Register(MakeBuilding("bastion_barracks", "bastion", cost: 750));

        // Count stays the same (no duplicate entry)
        Assert.Equal(1, registry.Count);
        // Updated data is retrieved
        Assert.Equal(750, registry.GetBuilding("bastion_barracks").Cost);
    }

    // ── GetBuilding ──────────────────────────────────────────────────────

    [Fact]
    public void GetBuilding_Registered_ReturnsCorrectData()
    {
        var registry = new BuildingRegistry();
        var data = MakeBuilding("bastion_barracks", "bastion", cost: 600);
        registry.Register(data);

        BuildingData retrieved = registry.GetBuilding("bastion_barracks");
        Assert.Equal("bastion_barracks", retrieved.Id);
        Assert.Equal("bastion", retrieved.FactionId);
        Assert.Equal(600, retrieved.Cost);
    }

    [Fact]
    public void GetBuilding_Unknown_ThrowsKeyNotFoundException()
    {
        var registry = new BuildingRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetBuilding("nonexistent"));
    }

    [Fact]
    public void GetBuilding_AfterOverwrite_ReturnsUpdatedData()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion", cost: 500));
        registry.Register(MakeBuilding("bastion_barracks", "bastion", cost: 800));

        Assert.Equal(800, registry.GetBuilding("bastion_barracks").Cost);
    }

    // ── GetFactionBuildings ──────────────────────────────────────────────

    [Fact]
    public void GetFactionBuildings_NoMatchingFaction_ReturnsEmpty()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));

        var result = registry.GetFactionBuildings("valkyr");
        Assert.Empty(result);
    }

    [Fact]
    public void GetFactionBuildings_ReturnsOnlyMatchingFaction()
    {
        var registry = new BuildingRegistry();
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));
        registry.Register(MakeBuilding("bastion_factory", "bastion"));
        registry.Register(MakeBuilding("valkyr_airfield", "valkyr"));
        registry.Register(MakeBuilding("arcloft_lab", "arcloft"));

        var bastionBuildings = registry.GetFactionBuildings("bastion");
        Assert.Equal(2, bastionBuildings.Count);
        Assert.All(bastionBuildings, b => Assert.Equal("bastion", b.FactionId));
    }

    [Fact]
    public void GetFactionBuildings_IsSortedById()
    {
        var registry = new BuildingRegistry();
        // Register in reverse alphabetical order
        registry.Register(MakeBuilding("bastion_factory", "bastion"));
        registry.Register(MakeBuilding("bastion_barracks", "bastion"));
        registry.Register(MakeBuilding("bastion_command", "bastion"));

        var result = registry.GetFactionBuildings("bastion");
        Assert.Equal(3, result.Count);
        // SortedList<string,…> keeps keys sorted alphabetically
        Assert.Equal("bastion_barracks", result[0].Id);
        Assert.Equal("bastion_command", result[1].Id);
        Assert.Equal("bastion_factory", result[2].Id);
    }

    [Fact]
    public void GetFactionBuildings_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new BuildingRegistry();
        Assert.Empty(registry.GetFactionBuildings("bastion"));
    }

    // ── Prerequisites stored correctly ──────────────────────────────────

    [Fact]
    public void Register_PrerequisitesStoredCorrectly()
    {
        var registry = new BuildingRegistry();
        var advanced = MakeBuilding(
            "bastion_tech_lab", "bastion",
            prerequisites: new[] { "bastion_barracks", "bastion_factory" });
        registry.Register(advanced);

        var data = registry.GetBuilding("bastion_tech_lab");
        Assert.Equal(2, data.Prerequisites.Count);
        Assert.Contains("bastion_barracks", data.Prerequisites);
        Assert.Contains("bastion_factory", data.Prerequisites);
    }
}
