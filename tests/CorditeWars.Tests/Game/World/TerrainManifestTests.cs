using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

/// <summary>
/// Tests for <see cref="TerrainManifest"/>'s public query API.
/// The <c>Load</c> path requires Godot's <c>FileAccess</c> and is exercised
/// only by integration runs; here we cover empty-state queries and the
/// throw-on-missing behavior of <c>GetEntry</c> / <c>FindEntry</c>.
/// </summary>
public class TerrainManifestTests
{
    [Fact]
    public void TotalEntries_NewManifest_ReturnsZero()
    {
        var manifest = new TerrainManifest();
        Assert.Equal(0, manifest.TotalEntries);
    }

    [Fact]
    public void GetCategories_NewManifest_ReturnsEmpty()
    {
        var manifest = new TerrainManifest();
        Assert.Empty(manifest.GetCategories());
    }

    [Fact]
    public void GetEntry_UnknownCategory_ThrowsKeyNotFoundException()
    {
        var manifest = new TerrainManifest();
        Assert.Throws<KeyNotFoundException>(
            () => manifest.GetEntry("trees", "oak_01"));
    }

    [Fact]
    public void GetEntry_EmptyArguments_ThrowsKeyNotFoundException()
    {
        var manifest = new TerrainManifest();
        Assert.Throws<KeyNotFoundException>(
            () => manifest.GetEntry(string.Empty, string.Empty));
    }

    [Fact]
    public void FindEntry_UnknownId_ThrowsKeyNotFoundException()
    {
        var manifest = new TerrainManifest();
        Assert.Throws<KeyNotFoundException>(
            () => manifest.FindEntry("missing_model"));
    }

    [Fact]
    public void FindEntry_EmptyId_ThrowsKeyNotFoundException()
    {
        var manifest = new TerrainManifest();
        Assert.Throws<KeyNotFoundException>(
            () => manifest.FindEntry(string.Empty));
    }

    // ── TerrainModelEntry POCO ──────────────────────────────────────────

    [Fact]
    public void TerrainModelEntry_DefaultValues_AreSensible()
    {
        var entry = new TerrainModelEntry();
        Assert.Equal(string.Empty, entry.ModelPath);
        Assert.False(entry.Passable);
        Assert.False(entry.BlocksVision);
        Assert.False(entry.Destructible);
        Assert.Equal(0, entry.Health);
        Assert.Equal(FixedPoint.One, entry.ModelScale);
    }

    [Fact]
    public void TerrainModelEntry_InitProperties_SetCorrectly()
    {
        var entry = new TerrainModelEntry
        {
            ModelPath = "res://models/oak.glb",
            CollisionRadius = FixedPoint.FromFloat(0.75f),
            Passable = false,
            BlocksVision = true,
            Destructible = true,
            Health = 100,
            ModelScale = FixedPoint.FromFloat(1.25f)
        };

        Assert.Equal("res://models/oak.glb", entry.ModelPath);
        Assert.Equal(FixedPoint.FromFloat(0.75f), entry.CollisionRadius);
        Assert.False(entry.Passable);
        Assert.True(entry.BlocksVision);
        Assert.True(entry.Destructible);
        Assert.Equal(100, entry.Health);
        Assert.Equal(FixedPoint.FromFloat(1.25f), entry.ModelScale);
    }
}
