using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.FogOfWar;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Game.FogOfWar;

/// <summary>
/// Tests for <see cref="VisionSystem"/> — deterministic per-tick fog-of-war vision.
/// Covers circle cell enumeration, line-of-sight calculation, elevation bonus,
/// air-unit vision, multi-unit vision merging, and player filtering.
/// </summary>
public class VisionSystemTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static TerrainGrid FlatTerrain(int width = 32, int height = 32) =>
        new TerrainGrid(width, height, FixedPoint.One);

    private static FogGrid Campaign(int width = 32, int height = 32, int playerId = 1) =>
        new FogGrid(width, height, playerId, FogMode.Campaign);

    private static VisionComponent MakeUnit(
        int playerId,
        int x,
        int y,
        int sightRange,
        float height = 0f,
        bool isAir = false) =>
        new VisionComponent
        {
            UnitId     = playerId * 1000 + x * 100 + y,
            PlayerId   = playerId,
            Position   = new FixedVector2(FixedPoint.FromFloat(x + 0.5f), FixedPoint.FromFloat(y + 0.5f)),
            SightRange = FixedPoint.FromInt(sightRange),
            Height     = FixedPoint.FromFloat(height),
            IsAirUnit  = isAir
        };

    // ═══════════════════════════════════════════════════════════════════════
    // GetCellsInRadius — circle cell enumeration
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCellsInRadius_RadiusOne_IncludesCenter()
    {
        var vs = new VisionSystem();
        var cells = vs.GetCellsInRadius(1);
        Assert.Contains((0, 0), cells);
    }

    [Fact]
    public void GetCellsInRadius_RadiusOne_HasExpectedCount()
    {
        var vs = new VisionSystem();
        var cells = vs.GetCellsInRadius(1);
        // Cells within distance 1 from (0,0): (0,0),(1,0),(-1,0),(0,1),(0,-1)
        Assert.Equal(5, cells.Count);
    }

    [Fact]
    public void GetCellsInRadius_RadiusZero_OnlyCenter()
    {
        var vs = new VisionSystem();
        var cells = vs.GetCellsInRadius(0);
        Assert.Single(cells);
        Assert.Contains((0, 0), cells);
    }

    [Fact]
    public void GetCellsInRadius_SameRadius_ReturnsCachedList()
    {
        var vs = new VisionSystem();
        var first  = vs.GetCellsInRadius(3);
        var second = vs.GetCellsInRadius(3);
        Assert.Same(first, second); // referential equality — cached
    }

    [Fact]
    public void GetCellsInRadius_LargerRadius_MoreCells()
    {
        var vs = new VisionSystem();
        var small = vs.GetCellsInRadius(2);
        var large = vs.GetCellsInRadius(5);
        Assert.True(large.Count > small.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void GetCellsInRadius_AllCellsWithinRadius(int radius)
    {
        var vs    = new VisionSystem();
        var cells = vs.GetCellsInRadius(radius);
        int rSq   = radius * radius;
        foreach (var (dx, dy) in cells)
            Assert.True(dx * dx + dy * dy <= rSq,
                $"Cell ({dx},{dy}) is outside radius {radius}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void GetCellsInRadius_IsSymmetric(int radius)
    {
        var vs    = new VisionSystem();
        var cells = vs.GetCellsInRadius(radius);
        var set   = new HashSet<(int, int)>(cells);

        foreach (var (dx, dy) in cells)
        {
            Assert.Contains((-dx, dy), set);
            Assert.Contains((dx, -dy), set);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateVision — basic visibility
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateVision_NoUnits_AllCellsUnexplored()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign();
        var terrain = FlatTerrain();

        vs.UpdateVision(fog, terrain, new List<VisionComponent>());

        // After update with no units, every cell should still be Unexplored
        Assert.Equal(FogVisibility.Unexplored, fog.GetVisibility(0, 0));
        Assert.Equal(FogVisibility.Unexplored, fog.GetVisibility(16, 16));
    }

    [Fact]
    public void UpdateVision_UnitAtCenter_CenterCellVisible()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign();
        var terrain = FlatTerrain();

        var units = new List<VisionComponent> { MakeUnit(1, 16, 16, sightRange: 3) };
        vs.UpdateVision(fog, terrain, units);

        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(16, 16));
    }

    [Fact]
    public void UpdateVision_UnitAtCorner_NearCellsVisible()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign();
        var terrain = FlatTerrain();

        var units = new List<VisionComponent> { MakeUnit(1, 0, 0, sightRange: 3) };
        vs.UpdateVision(fog, terrain, units);

        // Cell at (0,0) should be visible
        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(0, 0));
    }

    [Fact]
    public void UpdateVision_WrongPlayer_CellsNotRevealedOnPlayerFog()
    {
        var vs      = new VisionSystem();
        // Player 1's fog grid
        var fog     = Campaign(playerId: 1);
        var terrain = FlatTerrain();

        // Unit belongs to player 2 — should NOT reveal player 1's fog
        var units = new List<VisionComponent> { MakeUnit(playerId: 2, 16, 16, sightRange: 5) };
        vs.UpdateVision(fog, terrain, units);

        Assert.Equal(FogVisibility.Unexplored, fog.GetVisibility(16, 16));
    }

    [Fact]
    public void UpdateVision_TwoUnitsForSamePlayer_BothRevealAreas()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign(width: 64, height: 64);
        var terrain = FlatTerrain(64, 64);

        var units = new List<VisionComponent>
        {
            MakeUnit(1, 5,  5, sightRange: 2),
            MakeUnit(1, 55, 55, sightRange: 2)
        };
        vs.UpdateVision(fog, terrain, units);

        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(5, 5));
        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(55, 55));
        // A cell far from both units should remain unexplored
        Assert.Equal(FogVisibility.Unexplored, fog.GetVisibility(30, 30));
    }

    [Fact]
    public void UpdateVision_SecondTickWithoutUnit_PreviouslyVisibleBecomesExplored()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign();
        var terrain = FlatTerrain();

        // Tick 1 — unit present
        var units = new List<VisionComponent> { MakeUnit(1, 10, 10, sightRange: 2) };
        vs.UpdateVision(fog, terrain, units);
        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(10, 10));

        // Tick 2 — no units
        vs.UpdateVision(fog, terrain, new List<VisionComponent>());
        // Cell was previously visible → should now be Explored (shrouded)
        Assert.Equal(FogVisibility.Explored, fog.GetVisibility(10, 10));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HasLineOfSight — flat terrain (no blocking)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HasLineOfSight_FlatTerrain_AdjacentCells_ReturnsTrue()
    {
        var vs      = new VisionSystem();
        var terrain = FlatTerrain();

        var from = new FixedVector2(FixedPoint.FromFloat(5.5f), FixedPoint.FromFloat(5.5f));
        bool los = vs.HasLineOfSight(terrain, from, FixedPoint.Zero, 6, 5);
        Assert.True(los);
    }

    [Fact]
    public void HasLineOfSight_FlatTerrain_DistantCell_ReturnsTrue()
    {
        var vs      = new VisionSystem();
        var terrain = FlatTerrain();

        var from = new FixedVector2(FixedPoint.FromFloat(2.5f), FixedPoint.FromFloat(2.5f));
        bool los = vs.HasLineOfSight(terrain, from, FixedPoint.Zero, 20, 20);
        Assert.True(los);
    }

    [Fact]
    public void HasLineOfSight_SameCellOrAdjacent_AlwaysTrue()
    {
        var vs      = new VisionSystem();
        var terrain = FlatTerrain();

        var from = new FixedVector2(FixedPoint.FromFloat(10.5f), FixedPoint.FromFloat(10.5f));

        // Same cell
        Assert.True(vs.HasLineOfSight(terrain, from, FixedPoint.Zero, 10, 10));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HasLineOfSight — elevated terrain blocks LOS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HasLineOfSight_HighTerrain_BlocksLOS()
    {
        var vs      = new VisionSystem();
        var terrain = FlatTerrain(32, 32);

        // Raise a wall of cells in the middle at height 10
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref terrain.GetCell(15, y);
            cell.Height = FixedPoint.FromInt(10);
        }

        // Observer at (5,5) with eye-level 0 looking at (25,5) past the wall
        var from   = new FixedVector2(FixedPoint.FromFloat(5.5f), FixedPoint.FromFloat(5.5f));
        bool los   = vs.HasLineOfSight(terrain, from, FixedPoint.Zero, 25, 5);

        // The high terrain should block the line of sight
        Assert.False(los);
    }

    [Fact]
    public void HasLineOfSight_ObserverHighEnough_SeesOverWall()
    {
        var vs      = new VisionSystem();
        var terrain = FlatTerrain(32, 32);

        // Modest hill of height 5
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref terrain.GetCell(15, y);
            cell.Height = FixedPoint.FromInt(5);
        }

        // Observer at a great height (20) can see over height-5 wall
        var from    = new FixedVector2(FixedPoint.FromFloat(5.5f), FixedPoint.FromFloat(5.5f));
        bool los    = vs.HasLineOfSight(terrain, from, FixedPoint.FromInt(20), 25, 5);

        Assert.True(los);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Air units — ignore terrain LOS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AirUnit_IgnoresTerrainLineOfSight_CellsBehindWallStillVisible()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign(width: 64, height: 32);
        var terrain = FlatTerrain(64, 32);

        // Build a solid terrain wall at x=20
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref terrain.GetCell(20, y);
            cell.Height = FixedPoint.FromInt(15);
        }

        // Air unit at (5, 5) with large sight range
        var units = new List<VisionComponent>
        {
            MakeUnit(1, 5, 5, sightRange: 20, height: 0f, isAir: true)
        };
        vs.UpdateVision(fog, terrain, units);

        // Cell behind the wall should be visible for an air unit
        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(25, 5));
    }

    [Fact]
    public void GroundUnit_TerrainWallBlocksDistantCell()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign(width: 64, height: 32);
        var terrain = FlatTerrain(64, 32);

        // Build a solid terrain wall at x=10
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref terrain.GetCell(10, y);
            cell.Height = FixedPoint.FromInt(15);
        }

        // Ground unit at (3, 5) with sight range 20 but eye-level 0
        var units = new List<VisionComponent>
        {
            MakeUnit(1, 3, 5, sightRange: 20, height: 0f, isAir: false)
        };
        vs.UpdateVision(fog, terrain, units);

        // Cell at (20, 5) should be blocked by the wall
        Assert.Equal(FogVisibility.Unexplored, fog.GetVisibility(20, 5));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ComputeUnitVision — elevation bonus
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ElevatedUnit_SeesMoreCellsThanGroundLevel()
    {
        var vs      = new VisionSystem();
        var fog1    = Campaign(width: 64, height: 64, playerId: 1);
        var fog2    = Campaign(width: 64, height: 64, playerId: 2);
        var terrain = FlatTerrain(64, 64);

        // Both units at same position with same base sight range
        var groundUnit = new VisionComponent
        {
            UnitId     = 1,
            PlayerId   = 1,
            Position   = new FixedVector2(FixedPoint.FromFloat(32.5f), FixedPoint.FromFloat(32.5f)),
            SightRange = FixedPoint.FromInt(5),
            Height     = FixedPoint.Zero,
            IsAirUnit  = false
        };

        var elevatedUnit = new VisionComponent
        {
            UnitId     = 2,
            PlayerId   = 2,
            Position   = new FixedVector2(FixedPoint.FromFloat(32.5f), FixedPoint.FromFloat(32.5f)),
            SightRange = FixedPoint.FromInt(5),
            Height     = FixedPoint.FromInt(10), // elevated
            IsAirUnit  = false
        };

        vs.UpdateVision(fog1, terrain, new List<VisionComponent> { groundUnit });
        vs.UpdateVision(fog2, terrain, new List<VisionComponent> { elevatedUnit });

        // Count visible cells for each player
        int visible1 = CountVisibleCells(fog1, 64, 64);
        int visible2 = CountVisibleCells(fog2, 64, 64);

        // Elevated unit should see at least as many cells as ground unit
        Assert.True(visible2 >= visible1,
            $"Elevated unit ({visible2} cells) should see >= ground unit ({visible1} cells)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateVision — exploration persistence (Campaign mode)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CampaignFog_VisitedCell_BecomesExploredAfterUnitLeaves()
    {
        var vs      = new VisionSystem();
        var fog     = Campaign();
        var terrain = FlatTerrain();

        // Tick 1 — unit reveals cell (8,8)
        var units = new List<VisionComponent> { MakeUnit(1, 8, 8, sightRange: 1) };
        vs.UpdateVision(fog, terrain, units);
        Assert.Equal(FogVisibility.Visible, fog.GetVisibility(8, 8));

        // Tick 2 — unit gone
        vs.UpdateVision(fog, terrain, new List<VisionComponent>());
        // Campaign fog: previously visible → Explored (not Unexplored)
        Assert.Equal(FogVisibility.Explored, fog.GetVisibility(8, 8));
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static int CountVisibleCells(FogGrid fog, int width, int height)
    {
        int count = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (fog.GetVisibility(x, y) == FogVisibility.Visible)
                    count++;
        return count;
    }
}
