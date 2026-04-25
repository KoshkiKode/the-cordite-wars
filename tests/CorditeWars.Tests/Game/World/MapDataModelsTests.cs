using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

public class MapDataModelsTests
{
    [Fact]
    public void MapSunConfig_Defaults_AreExpected()
    {
        var sun = new MapSunConfig();

        Assert.True(sun.Enabled);
        Assert.Equal(-55f, sun.RotationX);
        Assert.Equal(30f, sun.RotationY);
        Assert.Equal("#FFFFFF", sun.Color);
        Assert.Equal(1.2f, sun.Energy);
        Assert.Equal("#8899BB", sun.AmbientColor);
        Assert.Equal(0.45f, sun.AmbientEnergy);
        Assert.Equal("#1A2040", sun.SkyColor);
    }

    [Fact]
    public void TerrainFeature_Defaults_AreExpected()
    {
        var feature = new TerrainFeature();

        Assert.Equal(string.Empty, feature.Type);
        Assert.Empty(feature.Points);
    }

    [Fact]
    public void MapData_Defaults_AreExpected()
    {
        var map = new MapData();

        Assert.Equal(string.Empty, map.Id);
        Assert.Equal(string.Empty, map.DisplayName);
        Assert.Equal(string.Empty, map.Description);
        Assert.Equal(string.Empty, map.Author);
        Assert.Equal(0, map.MaxPlayers);
        Assert.Equal(0, map.Width);
        Assert.Equal(0, map.Height);
        Assert.Equal(string.Empty, map.Biome);
        Assert.Empty(map.StartingPositions);
        Assert.Empty(map.CorditeNodes);
        Assert.Empty(map.TerrainFeatures);
        Assert.Empty(map.Props);
        Assert.Empty(map.Structures);
        Assert.Empty(map.ElevationZones);
        Assert.Empty(map.NeutralCapturableUnits);
        Assert.Null(map.SunConfig);
    }

    [Fact]
    public void CapturableUnitPlacement_Defaults_AreExpected()
    {
        var p = new CapturableUnitPlacement();
        Assert.Equal(string.Empty, p.UnitTypeId);
        Assert.Equal(0, p.X);
        Assert.Equal(0, p.Y);
        Assert.Equal(CorditeWars.Core.FixedPoint.Zero, p.Facing);
    }

    [Fact]
    public void CapturableUnitPlacement_AssignedValues_ArePreserved()
    {
        var p = new CapturableUnitPlacement
        {
            UnitTypeId = "ancient_gun",
            X = 160,
            Y = 135,
            Facing = CorditeWars.Core.FixedPoint.FromFloat(1.5708f)
        };
        Assert.Equal("ancient_gun", p.UnitTypeId);
        Assert.Equal(160, p.X);
        Assert.Equal(135, p.Y);
        Assert.Equal(CorditeWars.Core.FixedPoint.FromFloat(1.5708f), p.Facing);
    }

    [Fact]
    public void MapData_AssignedValues_ArePreserved()
    {
        var map = new MapData
        {
            Id = "crossroads",
            DisplayName = "Crossroads",
            Description = "A mid-size tactical map",
            Author = "QA",
            MaxPlayers = 4,
            Width = 200,
            Height = 160,
            Biome = "temperate",
            TerrainFeatures = [new TerrainFeature { Type = "river", Points = [[0, 0], [10, 10]] }],
            SunConfig = new MapSunConfig { Enabled = false, SkyColor = "#223344" }
        };

        Assert.Equal("crossroads", map.Id);
        Assert.Equal("Crossroads", map.DisplayName);
        Assert.Equal("A mid-size tactical map", map.Description);
        Assert.Equal("QA", map.Author);
        Assert.Equal(4, map.MaxPlayers);
        Assert.Equal(200, map.Width);
        Assert.Equal(160, map.Height);
        Assert.Equal("temperate", map.Biome);
        Assert.Single(map.TerrainFeatures);
        Assert.Equal("river", map.TerrainFeatures[0].Type);
        Assert.Equal(2, map.TerrainFeatures[0].Points.Length);
        Assert.NotNull(map.SunConfig);
        Assert.False(map.SunConfig!.Enabled);
        Assert.Equal("#223344", map.SunConfig.SkyColor);
    }
}
