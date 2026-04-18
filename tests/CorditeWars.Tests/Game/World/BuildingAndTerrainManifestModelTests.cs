using CorditeWars.Core;
using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

/// <summary>
/// Tests for <see cref="BuildingModelEntry"/> and <see cref="TerrainModelEntry"/> —
/// the pure data models loaded from the building/terrain manifests.
/// No Godot file I/O required.
/// </summary>
public class BuildingAndTerrainManifestModelTests
{
    // ══════════════════════════════════════════════════════════════════
    // BuildingModelEntry — defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildingModelEntry_Defaults_AreExpected()
    {
        var entry = new BuildingModelEntry();

        Assert.Equal(string.Empty, entry.ModelPath);
        Assert.Equal(0, entry.CollisionWidth);
        Assert.Equal(0, entry.CollisionHeight);
        Assert.Equal(default(FixedPoint), entry.CollisionRadius);
        Assert.Equal(default(FixedPoint), entry.Mass);
        Assert.False(entry.Passable);
        Assert.Equal(FixedPoint.One, entry.ModelScale);
        Assert.Equal(default(FixedPoint), entry.ModelRotation);
        Assert.Equal(string.Empty, entry.Category);
    }

    [Fact]
    public void BuildingModelEntry_AssignedValues_ArePreserved()
    {
        var entry = new BuildingModelEntry
        {
            ModelPath = "res://models/buildings/barracks.glb",
            CollisionWidth = 3,
            CollisionHeight = 3,
            CollisionRadius = FixedPoint.FromFloat(1.5f),
            Mass = FixedPoint.FromFloat(500f),
            Passable = false,
            ModelScale = FixedPoint.FromFloat(1.0f),
            ModelRotation = FixedPoint.FromFloat(45f),
            Category = "production"
        };

        Assert.Equal("res://models/buildings/barracks.glb", entry.ModelPath);
        Assert.Equal(3, entry.CollisionWidth);
        Assert.Equal(3, entry.CollisionHeight);
        Assert.InRange(entry.CollisionRadius.ToFloat(), 1.4f, 1.6f);
        Assert.False(entry.Passable);
        Assert.Equal("production", entry.Category);
    }

    [Fact]
    public void BuildingModelEntry_ModelScale_DefaultIsOne()
    {
        var entry = new BuildingModelEntry();
        Assert.Equal(FixedPoint.One, entry.ModelScale);
    }

    // ══════════════════════════════════════════════════════════════════
    // TerrainModelEntry — defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void TerrainModelEntry_Defaults_AreExpected()
    {
        var entry = new TerrainModelEntry();

        Assert.Equal(string.Empty, entry.ModelPath);
        Assert.Equal(default(FixedPoint), entry.CollisionRadius);
        Assert.False(entry.Passable);
        Assert.False(entry.BlocksVision);
        Assert.False(entry.Destructible);
        Assert.Equal(0, entry.Health);
        Assert.Equal(FixedPoint.One, entry.ModelScale);
    }

    [Fact]
    public void TerrainModelEntry_AssignedValues_ArePreserved()
    {
        var entry = new TerrainModelEntry
        {
            ModelPath = "res://models/terrain/oak_tree.glb",
            CollisionRadius = FixedPoint.FromFloat(0.8f),
            Passable = false,
            BlocksVision = true,
            Destructible = true,
            Health = 200,
            ModelScale = FixedPoint.FromFloat(1.2f)
        };

        Assert.Equal("res://models/terrain/oak_tree.glb", entry.ModelPath);
        Assert.InRange(entry.CollisionRadius.ToFloat(), 0.7f, 0.9f);
        Assert.False(entry.Passable);
        Assert.True(entry.BlocksVision);
        Assert.True(entry.Destructible);
        Assert.Equal(200, entry.Health);
        Assert.InRange(entry.ModelScale.ToFloat(), 1.15f, 1.25f);
    }

    [Fact]
    public void TerrainModelEntry_ModelScale_DefaultIsOne()
    {
        var entry = new TerrainModelEntry();
        Assert.Equal(FixedPoint.One, entry.ModelScale);
    }

    [Fact]
    public void TerrainModelEntry_Passable_CanBeSetTrue()
    {
        var entry = new TerrainModelEntry { Passable = true };
        Assert.True(entry.Passable);
    }

    [Fact]
    public void TerrainModelEntry_NonDestructible_HasZeroHealth()
    {
        var entry = new TerrainModelEntry { Destructible = false, Health = 0 };
        Assert.False(entry.Destructible);
        Assert.Equal(0, entry.Health);
    }
}
