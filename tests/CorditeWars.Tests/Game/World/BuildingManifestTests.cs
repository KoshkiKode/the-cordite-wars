using System.Collections.Generic;
using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

/// <summary>
/// Tests for <see cref="BuildingManifest"/>'s public query API.
/// The <c>Load</c> path requires Godot's <c>FileAccess</c> and is exercised
/// only by integration runs; here we cover the empty-state queries and the
/// throw-on-missing behavior of <c>GetEntry</c>.
/// </summary>
public class BuildingManifestTests
{
    [Fact]
    public void Count_NewManifest_ReturnsZero()
    {
        var manifest = new BuildingManifest();
        Assert.Equal(0, manifest.Count);
    }

    [Fact]
    public void HasEntry_NewManifest_ReturnsFalse()
    {
        var manifest = new BuildingManifest();
        Assert.False(manifest.HasEntry("bastion_barracks"));
    }

    [Fact]
    public void HasEntry_ArbitraryId_ReturnsFalseOnEmpty()
    {
        var manifest = new BuildingManifest();
        Assert.False(manifest.HasEntry("anything_at_all"));
        Assert.False(manifest.HasEntry(string.Empty));
    }

    [Fact]
    public void GetEntry_UnknownId_ThrowsKeyNotFoundException()
    {
        var manifest = new BuildingManifest();
        Assert.Throws<KeyNotFoundException>(() => manifest.GetEntry("missing_building"));
    }

    [Fact]
    public void GetEntry_EmptyString_ThrowsKeyNotFoundException()
    {
        var manifest = new BuildingManifest();
        Assert.Throws<KeyNotFoundException>(() => manifest.GetEntry(string.Empty));
    }

    // ── BuildingModelEntry POCO ─────────────────────────────────────────

    [Fact]
    public void BuildingModelEntry_DefaultValues_AreSensible()
    {
        var entry = new BuildingModelEntry();
        Assert.Equal(string.Empty, entry.ModelPath);
        Assert.Equal(0, entry.CollisionWidth);
        Assert.Equal(0, entry.CollisionHeight);
        Assert.False(entry.Passable);
        Assert.Equal(string.Empty, entry.Category);
        Assert.Equal(CorditeWars.Core.FixedPoint.One, entry.ModelScale);
    }

    [Fact]
    public void BuildingModelEntry_InitProperties_SetCorrectly()
    {
        var entry = new BuildingModelEntry
        {
            ModelPath = "res://models/barracks.glb",
            CollisionWidth = 4,
            CollisionHeight = 4,
            CollisionRadius = CorditeWars.Core.FixedPoint.FromInt(2),
            Mass = CorditeWars.Core.FixedPoint.FromInt(50),
            Passable = false,
            ModelScale = CorditeWars.Core.FixedPoint.FromFloat(1.5f),
            ModelRotation = CorditeWars.Core.FixedPoint.FromInt(90),
            Category = "infantry"
        };

        Assert.Equal("res://models/barracks.glb", entry.ModelPath);
        Assert.Equal(4, entry.CollisionWidth);
        Assert.Equal(4, entry.CollisionHeight);
        Assert.Equal(CorditeWars.Core.FixedPoint.FromInt(2), entry.CollisionRadius);
        Assert.Equal(CorditeWars.Core.FixedPoint.FromInt(50), entry.Mass);
        Assert.False(entry.Passable);
        Assert.Equal("infantry", entry.Category);
    }
}
