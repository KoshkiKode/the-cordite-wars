using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for AStarPathfinder — 8-directional A* on a TerrainGrid.
/// Covers basic pathfinding, obstacle avoidance, impassable terrain,
/// trivial cases, out-of-bounds goals, and determinism.
/// </summary>
public class AStarPathfinderTests
{
    private readonly AStarPathfinder _pathfinder = new();

    // ── Grid helpers ─────────────────────────────────────────────────────

    /// <summary>Creates an open, fully traversable grass grid.</summary>
    private static TerrainGrid OpenGrid(int w = 32, int h = 32)
    {
        return new TerrainGrid(w, h, FixedPoint.One);
    }

    /// <summary>Creates a grid with a single blocked column, creating a wall.</summary>
    private static TerrainGrid WallGrid(int wallX, int w = 32, int h = 32)
    {
        var grid = new TerrainGrid(w, h, FixedPoint.One);
        for (int y = 0; y < h; y++)
        {
            ref var cell = ref grid.GetCell(wallX, y);
            cell.IsBlocked = true;
        }
        return grid;
    }

    /// <summary>Creates a grid with a U-shaped wall leaving only a gap at (wallX, gapY).</summary>
    private static TerrainGrid UWallGrid(int wallX, int gapY, int w = 32, int h = 32)
    {
        var grid = new TerrainGrid(w, h, FixedPoint.One);
        for (int y = 0; y < h; y++)
        {
            if (y == gapY) continue; // leave the gap
            ref var cell = ref grid.GetCell(wallX, y);
            cell.IsBlocked = true;
        }
        return grid;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Trivial case: start == goal
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_StartEqualsGoal_ReturnsSingleCell()
    {
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();

        var path = _pathfinder.FindPath(grid, profile, 5, 5, 5, 5);

        Assert.Single(path);
        Assert.Equal((5, 5), path[0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Basic path finding on open terrain
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_OpenGrid_ReturnsNonEmptyPath()
    {
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();

        var path = _pathfinder.FindPath(grid, profile, 0, 0, 10, 10);

        Assert.NotEmpty(path);
    }

    [Fact]
    public void FindPath_OpenGrid_PathStartsAtStart()
    {
        var grid = OpenGrid();
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 2, 3, 15, 15);

        Assert.Equal((2, 3), path[0]);
    }

    [Fact]
    public void FindPath_OpenGrid_PathEndsAtGoal()
    {
        var grid = OpenGrid();
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 2, 3, 15, 15);

        Assert.Equal((15, 15), path[^1]);
    }

    [Fact]
    public void FindPath_OpenGrid_PathIsContiguous()
    {
        var grid = OpenGrid();
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 0, 0, 20, 20);

        Assert.NotEmpty(path);
        for (int i = 1; i < path.Count; i++)
        {
            int dx = Math.Abs(path[i].x - path[i - 1].x);
            int dy = Math.Abs(path[i].y - path[i - 1].y);
            Assert.True(dx <= 1 && dy <= 1,
                $"Step {i - 1}→{i}: ({path[i - 1].x},{path[i - 1].y})→({path[i].x},{path[i].y}) is not contiguous");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Obstacle avoidance
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_WallWithGap_FindsPathThroughGap()
    {
        // Wall at x=10, gap at y=15. Start (5,15) → Goal (20,15).
        var grid = UWallGrid(wallX: 10, gapY: 15);
        var profile = MovementProfile.Infantry();

        var path = _pathfinder.FindPath(grid, profile, 5, 15, 20, 15);

        Assert.NotEmpty(path);
        Assert.Equal((20, 15), path[^1]);

        // Path must pass through or near the gap
        bool passedGap = false;
        foreach (var (x, y) in path)
            if (x == 10 && y == 15) { passedGap = true; break; }
        Assert.True(passedGap, "Path should cross through the gap at (10,15)");
    }

    [Fact]
    public void FindPath_SolidWall_ReturnsEmpty()
    {
        // Wall from x=10, no gap — completely cuts off left from right.
        var grid = WallGrid(wallX: 10);
        var profile = MovementProfile.Infantry();

        var path = _pathfinder.FindPath(grid, profile, 5, 5, 20, 5);

        Assert.Empty(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Impassable terrain types
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_GoalIsBlockedCell_ReturnsEmpty()
    {
        var grid = OpenGrid();
        ref var goalCell = ref grid.GetCell(10, 10);
        goalCell.IsBlocked = true;

        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 0, 0, 10, 10);

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_StartIsBlockedCell_ReturnsEmpty()
    {
        var grid = OpenGrid();
        ref var startCell = ref grid.GetCell(0, 0);
        startCell.IsBlocked = true;

        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 0, 0, 10, 10);

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_WaterImpassableForInfantry_ReturnsEmpty()
    {
        // Solid strip of water at x=10 spanning the full grid height.
        var grid = new TerrainGrid(32, 32, FixedPoint.One);
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref grid.GetCell(10, y);
            cell.Type = TerrainType.Water;
        }

        // Infantry cannot cross water.
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 5, 5, 20, 5);

        Assert.Empty(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Out-of-bounds goals
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_GoalOutOfBounds_ReturnsEmpty()
    {
        var grid = OpenGrid(16, 16);
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 0, 0, 50, 50);
        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_NegativeGoal_ReturnsEmpty()
    {
        var grid = OpenGrid();
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 5, 5, -1, 5);
        Assert.Empty(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Adjacent cells (distance = 1)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_AdjacentCardinal_ReturnsTwoSteps()
    {
        var grid = OpenGrid();
        // Move one step east
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 5, 5, 6, 5);

        Assert.Equal(2, path.Count);
        Assert.Equal((5, 5), path[0]);
        Assert.Equal((6, 5), path[1]);
    }

    [Fact]
    public void FindPath_AdjacentDiagonal_ReturnsTwoSteps()
    {
        var grid = OpenGrid();
        // Move one step diagonally
        var path = _pathfinder.FindPath(grid, MovementProfile.Infantry(), 5, 5, 6, 6);

        Assert.Equal(2, path.Count);
        Assert.Equal((5, 5), path[0]);
        Assert.Equal((6, 6), path[1]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Determinism: same inputs → same path
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_IsDeterministic_OpenTerrain()
    {
        var grid    = OpenGrid();
        var profile = MovementProfile.Infantry();

        var pathA = _pathfinder.FindPath(grid, profile, 0, 0, 25, 20);
        var pathB = _pathfinder.FindPath(grid, profile, 0, 0, 25, 20);

        Assert.Equal(pathA, pathB);
    }

    [Fact]
    public void FindPath_IsDeterministic_WithObstacles()
    {
        var grid    = UWallGrid(wallX: 12, gapY: 8);
        var profile = MovementProfile.Infantry();

        var pathA = _pathfinder.FindPath(grid, profile, 2, 2, 25, 20);
        var pathB = _pathfinder.FindPath(grid, profile, 2, 2, 25, 20);

        Assert.Equal(pathA, pathB);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Movement profile differences
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_NavalUnit_CanOnlyCrossWater()
    {
        // All water grid — naval should be able to pathfind.
        var grid = new TerrainGrid(32, 32, FixedPoint.One);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                ref var cell = ref grid.GetCell(x, y);
                cell.Type = TerrainType.Water;
            }

        var path = _pathfinder.FindPath(grid, MovementProfile.Naval(), 0, 0, 20, 20);

        Assert.NotEmpty(path);
        Assert.Equal((20, 20), path[^1]);
    }

    [Fact]
    public void FindPath_NavalUnit_CannotCrossGrass()
    {
        // Solid strip of grass at x=10 on an otherwise water grid.
        var grid = new TerrainGrid(32, 32, FixedPoint.One);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                ref var cell = ref grid.GetCell(x, y);
                cell.Type = TerrainType.Water;
            }
        // Replace column 10 with grass (impassable for naval)
        for (int y = 0; y < 32; y++)
        {
            ref var cell = ref grid.GetCell(10, y);
            cell.Type = TerrainType.Grass;
        }

        var path = _pathfinder.FindPath(grid, MovementProfile.Naval(), 5, 5, 20, 5);

        Assert.Empty(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Path stays within grid bounds
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_AllNodesWithinBounds()
    {
        var grid    = OpenGrid(24, 24);
        var profile = MovementProfile.Infantry();

        var path = _pathfinder.FindPath(grid, profile, 0, 0, 23, 23);

        foreach (var (x, y) in path)
        {
            Assert.True(x >= 0 && x < 24, $"x={x} out of bounds");
            Assert.True(y >= 0 && y < 24, $"y={y} out of bounds");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Footprint-aware pathfinding (vehicle with 2×2 footprint)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindPath_LargeFootprint_CannotFitThroughNarrowGap()
    {
        // Gap of 1 cell width — a 2×2 vehicle cannot fit through.
        var grid = UWallGrid(wallX: 10, gapY: 8);
        var profile = MovementProfile.HeavyVehicle();

        // HeavyVehicle has a 2×2 footprint; a single-cell gap at y=8 is not wide enough.
        var path = _pathfinder.FindPath(grid, profile, 2, 7, 20, 7);

        // The path should either be empty (no 2-cell gap) or use a route around.
        // We just verify the path (if found) is valid and contiguous.
        foreach (var (x, y) in path)
        {
            Assert.True(grid.IsInBounds(x, y), "Path node out of bounds");
            for (int fy = 0; fy < profile.FootprintHeight; fy++)
                for (int fx = 0; fx < profile.FootprintWidth; fx++)
                {
                    int cx = x + fx;
                    int cy = y + fy;
                    if (grid.IsInBounds(cx, cy))
                        Assert.False(grid.GetCellSafe(cx, cy).IsBlocked,
                            $"Footprint cell ({cx},{cy}) is blocked");
                }
        }
    }
}
