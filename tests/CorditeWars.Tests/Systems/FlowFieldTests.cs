using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for FlowField — Dijkstra-based flow field generation for group movement.
/// Covers generation on open and obstacle grids, direction queries, edge cases,
/// and determinism.
/// </summary>
public class FlowFieldTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static TerrainGrid OpenGrid(int w = 32, int h = 32) =>
        new TerrainGrid(w, h, FixedPoint.One);

    // ═══════════════════════════════════════════════════════════════════
    // IsValid flag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_OpenGrid_IsValidAfterGeneration()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 16, goalY: 16,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        Assert.True(ff.IsValid);
    }

    [Fact]
    public void New_FlowField_IsNotValid()
    {
        var ff = new FlowField();
        Assert.False(ff.IsValid);
    }

    [Fact]
    public void Generate_GoalOutsideRegion_IsNotValid()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(),
            goalX: 40, goalY: 40,          // outside 0..31 × 0..31 region
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        Assert.False(ff.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Goal cell has direction None (it IS the goal)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_GoalCell_HasDirectionNone()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 10, goalY: 10,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        Assert.Equal(FlowDirection.None, ff.GetDirection(10, 10));
    }

    [Fact]
    public void Generate_GoalCell_HasZeroDirectionVector()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 10, goalY: 10,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        Assert.Equal(FixedVector2.Zero, ff.GetDirectionVector(10, 10));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cells to the left of the goal point East
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_CellDirectlyWestOfGoal_PointsEast()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        // Goal at (15, 15). Cell at (14, 15) should point East.
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 15, goalY: 15,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        FlowDirection dir = ff.GetDirection(14, 15);
        Assert.Equal(FlowDirection.E, dir);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Direction vector magnitude should be ~1 (normalized)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_NonGoalCell_DirectionVectorHasUnitLength()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 20, goalY: 20,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        // Check a few cells that are clearly not the goal
        foreach (var (x, y) in new[] { (0, 0), (5, 5), (10, 20), (20, 5) })
        {
            var vec = ff.GetDirectionVector(x, y);
            // Skip if somehow unreachable (shouldn't be on open grid)
            if (vec == FixedVector2.Zero) continue;

            FixedPoint lenSq = vec.LengthSquared;
            // Allow a small tolerance: |lenSq - 1| < 0.02
            FixedPoint diff = FixedPoint.Abs(lenSq - FixedPoint.One);
            Assert.True(diff < FixedPoint.FromFloat(0.02f),
                $"Direction vector at ({x},{y}) is not normalized: lenSq={lenSq}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cells outside the region return None
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetDirection_OutsideRegion_ReturnsNone()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 10, goalY: 10,
            regionMinX: 5, regionMinY: 5, regionMaxX: 20, regionMaxY: 20);

        // Query outside the region
        Assert.Equal(FlowDirection.None, ff.GetDirection(0, 0));
        Assert.Equal(FlowDirection.None, ff.GetDirection(30, 30));
    }

    [Fact]
    public void GetDirection_InvalidField_ReturnsNone()
    {
        var ff = new FlowField(); // never generated
        Assert.Equal(FlowDirection.None, ff.GetDirection(5, 5));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Blocked cells produce no flow
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_FullyEnclosedGoal_CellsOutsideHaveNoDirection()
    {
        // Surround the goal with blocked cells — no path from the outside.
        var grid = OpenGrid();
        int gx = 15, gy = 15;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                ref var c = ref grid.GetCell(gx + dx, gy + dy);
                c.IsBlocked = true;
            }

        var ff = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: gx, goalY: gy,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        // The goal itself is reachable (IsValid may be true since the goal IS the cell).
        // But cells at distance > 1 should be unreachable.
        Assert.Equal(FlowDirection.None, ff.GetDirection(5, 5));
        Assert.Equal(FlowDirection.None, ff.GetDirection(0, 0));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region properties after Generate
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_RegionPropertiesMatchInput()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 10, goalY: 10,
            regionMinX: 5, regionMinY: 5, regionMaxX: 25, regionMaxY: 25);

        Assert.Equal(5,  ff.RegionMinX);
        Assert.Equal(5,  ff.RegionMinY);
        Assert.Equal(25, ff.RegionMaxX);
        Assert.Equal(25, ff.RegionMaxY);
        Assert.Equal(10, ff.GoalX);
        Assert.Equal(10, ff.GoalY);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Determinism
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_IsDeterministic_SameGridSameResult()
    {
        var grid = OpenGrid();
        var ffA  = new FlowField();
        var ffB  = new FlowField();

        ffA.Generate(grid, MovementProfile.Infantry(), goalX: 20, goalY: 20,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);
        ffB.Generate(grid, MovementProfile.Infantry(), goalX: 20, goalY: 20,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);

        // Compare a sample of directions
        for (int y = 0; y < 32; y += 4)
        {
            for (int x = 0; x < 32; x += 4)
            {
                Assert.Equal(ffA.GetDirection(x, y), ffB.GetDirection(x, y));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Regeneration: new goal → new valid field
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_CalledTwice_ProducesUpdatedField()
    {
        var grid = OpenGrid();
        var ff   = new FlowField();

        // Goal at (5, 5) — from (0, 0) direction should be SE
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 5, goalY: 5,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);
        FlowDirection firstDir = ff.GetDirection(0, 0);

        // Goal at (30, 0) — from (0, 0) direction should be E, not SE
        ff.Generate(grid, MovementProfile.Infantry(), goalX: 30, goalY: 0,
            regionMinX: 0, regionMinY: 0, regionMaxX: 31, regionMaxY: 31);
        FlowDirection secondDir = ff.GetDirection(0, 0);

        // With different goals, the direction at (0,0) should differ.
        Assert.NotEqual(firstDir, secondDir);
    }
}
