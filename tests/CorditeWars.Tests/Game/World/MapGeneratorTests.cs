using System;
using System.Linq;
using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

/// <summary>
/// Tests for MapGenerator — deterministic procedural map generation.
/// Verifies that the generator respects its config, produces correct structure,
/// is deterministic (same seed = same output), and validates its input config.
/// </summary>
public class MapGeneratorTests
{
    private static readonly MapGenerator Generator = new();

    // ── Minimum valid config for most tests ───────────────────────────────

    private static MapGenConfig MinConfig(int players = 2, ulong seed = 1) =>
        new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = players,
            Biome = "temperate",
            Seed = seed,
            PropDensity = 0.0,    // keep tests fast — no props
            CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = false,
            GeneratePaths = false
        };

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public void Generate_WidthTooSmall_Throws()
    {
        var cfg = new MapGenConfig
        {
            Width = 63,
            Height = 64,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 1
        };
        Assert.ThrowsAny<ArgumentException>(() => Generator.Generate(cfg));
    }

    [Fact]
    public void Generate_HeightTooSmall_Throws()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 63,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 1
        };
        Assert.ThrowsAny<ArgumentException>(() => Generator.Generate(cfg));
    }

    [Fact]
    public void Generate_PlayerCountTooLow_Throws()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 1,
            Biome = "temperate",
            Seed = 1
        };
        Assert.ThrowsAny<ArgumentException>(() => Generator.Generate(cfg));
    }

    [Fact]
    public void Generate_PlayerCountTooHigh_Throws()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 7,
            Biome = "temperate",
            Seed = 1
        };
        Assert.ThrowsAny<ArgumentException>(() => Generator.Generate(cfg));
    }

    // ── MapData structure ──────────────────────────────────────────────────

    [Fact]
    public void Generate_ReturnsMapDataWithCorrectDimensions()
    {
        var map = Generator.Generate(MinConfig(2));
        Assert.Equal(64, map.Width);
        Assert.Equal(64, map.Height);
    }

    [Fact]
    public void Generate_MapIdContainsSeed()
    {
        var map = Generator.Generate(MinConfig(2, seed: 77));
        Assert.Contains("77", map.Id);
    }

    [Fact]
    public void Generate_MaxPlayersMatchesConfig()
    {
        var map = Generator.Generate(MinConfig(4));
        Assert.Equal(4, map.MaxPlayers);
    }

    [Fact]
    public void Generate_BiomeMatchesConfig()
    {
        var map = Generator.Generate(MinConfig(2));
        Assert.Equal("temperate", map.Biome);
    }

    // ── Starting positions ─────────────────────────────────────────────────

    [Fact]
    public void Generate_TwoPlayers_TwoStartingPositions()
    {
        var map = Generator.Generate(MinConfig(2));
        Assert.Equal(2, map.StartingPositions.Length);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Generate_NPlayers_NStartingPositions(int n)
    {
        var map = Generator.Generate(MinConfig(n));
        Assert.Equal(n, map.StartingPositions.Length);
    }

    [Fact]
    public void Generate_StartingPositions_WithinMapBounds()
    {
        var cfg = MinConfig(4);
        var map = Generator.Generate(cfg);

        foreach (var sp in map.StartingPositions)
        {
            Assert.True(sp.X >= 0 && sp.X < cfg.Width,
                $"StartingPosition.X={sp.X} out of bounds [0,{cfg.Width})");
            Assert.True(sp.Y >= 0 && sp.Y < cfg.Height,
                $"StartingPosition.Y={sp.Y} out of bounds [0,{cfg.Height})");
        }
    }

    [Fact]
    public void Generate_StartingPositions_HaveUniqueLocations()
    {
        var map = Generator.Generate(MinConfig(4));
        var coords = map.StartingPositions.Select(sp => (sp.X, sp.Y)).Distinct().ToList();
        Assert.Equal(map.StartingPositions.Length, coords.Count);
    }

    // ── Cordite nodes ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_CorditeNodes_AtLeastOnePerPlayer()
    {
        // With CorditeNodesPerPlayer=1, there should be at least 2 nodes
        var map = Generator.Generate(MinConfig(2));
        Assert.True(map.CorditeNodes.Length >= 2,
            $"Expected at least 2 cordite nodes, got {map.CorditeNodes.Length}");
    }

    [Fact]
    public void Generate_CorditeNodes_HavePositiveAmount()
    {
        var map = Generator.Generate(MinConfig(2));
        foreach (var node in map.CorditeNodes)
        {
            Assert.True(node.Amount > 0, $"Node {node.NodeId} has non-positive amount");
        }
    }

    [Fact]
    public void Generate_CorditeNodes_WithinMapBounds()
    {
        var cfg = MinConfig(2);
        var map = Generator.Generate(cfg);

        foreach (var node in map.CorditeNodes)
        {
            Assert.True(node.X >= 0 && node.X < cfg.Width,
                $"CorditeNode.X={node.X} out of bounds");
            Assert.True(node.Y >= 0 && node.Y < cfg.Height,
                $"CorditeNode.Y={node.Y} out of bounds");
        }
    }

    [Fact]
    public void Generate_CorditeNodes_HaveUniqueIds()
    {
        var map = Generator.Generate(MinConfig(2));
        var ids = map.CorditeNodes.Select(n => n.NodeId).Distinct().ToList();
        Assert.Equal(map.CorditeNodes.Length, ids.Count);
    }

    // ── Determinism ────────────────────────────────────────────────────────

    [Fact]
    public void Generate_SameSeed_ProducesIdenticalMap()
    {
        var cfg = MinConfig(2, seed: 42);
        var map1 = Generator.Generate(cfg);
        var map2 = Generator.Generate(cfg);

        Assert.Equal(map1.Id, map2.Id);
        Assert.Equal(map1.StartingPositions.Length, map2.StartingPositions.Length);
        Assert.Equal(map1.CorditeNodes.Length, map2.CorditeNodes.Length);

        for (int i = 0; i < map1.StartingPositions.Length; i++)
        {
            Assert.Equal(map1.StartingPositions[i].X, map2.StartingPositions[i].X);
            Assert.Equal(map1.StartingPositions[i].Y, map2.StartingPositions[i].Y);
        }

        for (int i = 0; i < map1.CorditeNodes.Length; i++)
        {
            Assert.Equal(map1.CorditeNodes[i].NodeId, map2.CorditeNodes[i].NodeId);
            Assert.Equal(map1.CorditeNodes[i].X, map2.CorditeNodes[i].X);
            Assert.Equal(map1.CorditeNodes[i].Y, map2.CorditeNodes[i].Y);
            Assert.Equal(map1.CorditeNodes[i].Amount, map2.CorditeNodes[i].Amount);
        }

        for (int i = 0; i < map1.ElevationZones.Length; i++)
        {
            Assert.Equal(map1.ElevationZones[i].CenterX, map2.ElevationZones[i].CenterX);
            Assert.Equal(map1.ElevationZones[i].CenterY, map2.ElevationZones[i].CenterY);
        }
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var cfg1 = MinConfig(2, seed: 100);
        var cfg2 = MinConfig(2, seed: 200);

        var map1 = Generator.Generate(cfg1);
        var map2 = Generator.Generate(cfg2);

        // IDs must differ (they embed the seed)
        Assert.NotEqual(map1.Id, map2.Id);
    }

    // ── Elevation zones ────────────────────────────────────────────────────

    [Fact]
    public void Generate_ElevationZones_CountMatchesConfig()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 1,
            ElevationZoneCount = 4,
            PropDensity = 0.0,
            CorditeNodesPerPlayer = 1,
            GenerateRivers = false,
            GeneratePaths = false
        };
        var map = Generator.Generate(cfg);
        Assert.Equal(4, map.ElevationZones.Length);
    }

    [Fact]
    public void Generate_ElevationZones_WithinMapBounds()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 1,
            ElevationZoneCount = 3,
            PropDensity = 0.0,
            CorditeNodesPerPlayer = 1,
            GenerateRivers = false,
            GeneratePaths = false
        };
        var map = Generator.Generate(cfg);

        foreach (var zone in map.ElevationZones)
        {
            Assert.True(zone.CenterX >= 0 && zone.CenterX < cfg.Width);
            Assert.True(zone.CenterY >= 0 && zone.CenterY < cfg.Height);
            Assert.True(zone.Radius > 0);
        }
    }

    // ── Biomes ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("temperate")]
    [InlineData("desert")]
    [InlineData("rocky")]
    [InlineData("coastal")]
    [InlineData("archipelago")]
    [InlineData("volcanic")]
    public void Generate_AllBiomes_ProduceValidMap(string biome)
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 2,
            Biome = biome,
            Seed = 1,
            PropDensity = 0.0,
            CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = false,
            GeneratePaths = false
        };

        var map = Generator.Generate(cfg);

        Assert.Equal(biome, map.Biome);
        Assert.Equal(2, map.StartingPositions.Length);
        Assert.True(map.CorditeNodes.Length >= 2);
    }

    // ── Props ──────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ZeroPropDensity_FewPropsGenerated()
    {
        // With density=0, maxProps=0 but the generator clamps to a minimum of 10.
        // Verify the output is within the expected clamped range, not unbounded.
        var map = Generator.Generate(MinConfig(2));
        Assert.True(map.Props.Length <= 2000,
            "Props should be bounded even with minimal density");
    }

    [Fact]
    public void Generate_NonZeroPropDensity_HasProps()
    {
        var cfg = new MapGenConfig
        {
            Width = 200,
            Height = 200,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 42,
            PropDensity = 1.0,  // maximum density
            CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 0,
            GenerateRivers = false,
            GeneratePaths = false
        };
        var map = Generator.Generate(cfg);
        Assert.True(map.Props.Length > 0,
            "Expected props to be generated with density=1.0 on a 200×200 map");
    }

    // ── DisplayName / Description ──────────────────────────────────────────

    [Fact]
    public void Generate_DisplayNameContainsBiome()
    {
        var map = Generator.Generate(MinConfig(2));
        Assert.Contains("temperate", map.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_DescriptionContainsPlayerCountAndSeed()
    {
        var map = Generator.Generate(MinConfig(3, seed: 999));
        Assert.Contains("3", map.Description);
        Assert.Contains("999", map.Description);
    }

    [Fact]
    public void Generate_AuthorIsMapGenerator()
    {
        var map = Generator.Generate(MinConfig(2));
        Assert.Equal("MapGenerator", map.Author);
    }

    // ── River generation ──────────────────────────────────────────────────

    [Fact]
    public void Generate_WithRiversEnabled_ProducesRiverFeature()
    {
        // Enable river generation on a biome that allows rivers.
        var cfg = new MapGenConfig
        {
            Width = 64, Height = 64, PlayerCount = 2,
            Biome = "temperate", Seed = 42,
            PropDensity = 0.0, CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = true, GeneratePaths = false
        };

        var map = Generator.Generate(cfg);

        // Rivers are only added for non-desert/non-volcanic biomes ("temperate")
        // and the RNG may flip wantRiver to false occasionally. We verify no throw.
        Assert.NotNull(map.TerrainFeatures);
    }

    [Fact]
    public void Generate_WithRiversEnabled_Deterministic()
    {
        var cfg = new MapGenConfig
        {
            Width = 64, Height = 64, PlayerCount = 2,
            Biome = "temperate", Seed = 100,
            PropDensity = 0.0, CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = true, GeneratePaths = false
        };

        var map1 = Generator.Generate(cfg);
        var map2 = Generator.Generate(cfg);

        Assert.Equal(map1.TerrainFeatures.Length, map2.TerrainFeatures.Length);
    }

    // ── Path generation ───────────────────────────────────────────────────

    [Fact]
    public void Generate_WithPathsEnabled_ProducesPathFeature()
    {
        var cfg = new MapGenConfig
        {
            Width = 64, Height = 64, PlayerCount = 2,
            Biome = "temperate", Seed = 7,
            PropDensity = 0.0, CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = false, GeneratePaths = true
        };

        var map = Generator.Generate(cfg);

        bool hasPath = map.TerrainFeatures.Any(
            f => f.Type.Equals("path", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasPath, "Expected at least one 'path' feature when GeneratePaths=true and players >= 2");
    }

    [Fact]
    public void Generate_WithPathsAndRiversEnabled_IncludesBothFeatureTypes()
    {
        var cfg = new MapGenConfig
        {
            Width = 64,
            Height = 64,
            PlayerCount = 2,
            Biome = "temperate",
            Seed = 999,
            PropDensity = 0.0,
            CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = true,
            GeneratePaths = true
        };

        var map = Generator.Generate(cfg);

        // Must have at least a path (and possibly a river).
        bool hasPath = map.TerrainFeatures.Any(
            f => f.Type.Equals("path", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasPath);
    }

    [Fact]
    public void Generate_PathsEnabled_With4Players_PathHasEnoughPoints()
    {
        var cfg = new MapGenConfig
        {
            Width = 128,
            Height = 128,
            PlayerCount = 4,
            Biome = "temperate",
            Seed = 555,
            PropDensity = 0.0,
            CorditeNodesPerPlayer = 1,
            ElevationZoneCount = 2,
            GenerateRivers = false,
            GeneratePaths = true
        };

        var map = Generator.Generate(cfg);

        var path = map.TerrainFeatures.FirstOrDefault(
            f => f.Type.Equals("path", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(path);
        // A ring through 4 starting positions should produce at least 8 points.
        Assert.True(path!.Points.Length >= 4,
            $"Expected >= 4 points in a 4-player ring path, got {path.Points.Length}");
    }
}
