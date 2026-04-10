using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for OccupancyGrid — dynamic blocking layer for units and buildings.
/// Covers cell occupy/vacate operations, footprint operations, passability rules,
/// reservation, and boundary handling.
/// </summary>
public class OccupancyGridTests
{
    // ── Construction ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_AllCellsStartEmpty()
    {
        var grid = new OccupancyGrid(16, 16);

        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                Assert.Equal(OccupancyType.Empty, grid.Cells[x, y].Type);
                Assert.Equal(-1, grid.Cells[x, y].OccupantId);
                Assert.Equal(-1, grid.Cells[x, y].PlayerId);
            }
        }
    }

    [Fact]
    public void Constructor_SetsWidthAndHeight()
    {
        var grid = new OccupancyGrid(32, 64);
        Assert.Equal(32, grid.Width);
        Assert.Equal(64, grid.Height);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Constructor_InvalidDimensions_Throws(int width, int height)
    {
        Assert.ThrowsAny<ArgumentException>(() => new OccupancyGrid(width, height));
    }

    // ── Clear ───────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_ResetsAllCellsToEmpty()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Unit, 42, 1);
        grid.OccupyCell(5, 5, OccupancyType.Building, 99, 2);

        grid.Clear();

        Assert.Equal(OccupancyType.Empty, grid.Cells[3, 3].Type);
        Assert.Equal(-1, grid.Cells[3, 3].OccupantId);
        Assert.Equal(-1, grid.Cells[3, 3].PlayerId);

        Assert.Equal(OccupancyType.Empty, grid.Cells[5, 5].Type);
    }

    [Fact]
    public void Clear_IsSafeToCallMultipleTimes()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.Clear();
        grid.Clear(); // should not throw
        Assert.Equal(OccupancyType.Empty, grid.Cells[0, 0].Type);
    }

    // ── OccupyCell ──────────────────────────────────────────────────────────

    [Fact]
    public void OccupyCell_SetsTypeOccupantAndPlayer()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(5, 7, OccupancyType.Unit, 10, 1);

        Assert.Equal(OccupancyType.Unit, grid.Cells[5, 7].Type);
        Assert.Equal(10, grid.Cells[5, 7].OccupantId);
        Assert.Equal(1, grid.Cells[5, 7].PlayerId);
    }

    [Fact]
    public void OccupyCell_OutOfBounds_Ignored()
    {
        var grid = new OccupancyGrid(8, 8);
        // Should not throw — silently ignored
        grid.OccupyCell(-1, 0, OccupancyType.Unit, 1, 1);
        grid.OccupyCell(0, -1, OccupancyType.Unit, 1, 1);
        grid.OccupyCell(8, 0, OccupancyType.Unit, 1, 1);
        grid.OccupyCell(0, 8, OccupancyType.Unit, 1, 1);
        // All cells should still be empty
        Assert.Equal(OccupancyType.Empty, grid.Cells[0, 0].Type);
    }

    [Fact]
    public void OccupyCell_Building_StoresCorrectType()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(2, 3, OccupancyType.Building, 200, 2);
        Assert.Equal(OccupancyType.Building, grid.Cells[2, 3].Type);
        Assert.Equal(200, grid.Cells[2, 3].OccupantId);
        Assert.Equal(2, grid.Cells[2, 3].PlayerId);
    }

    // ── VacateCell ──────────────────────────────────────────────────────────

    [Fact]
    public void VacateCell_ClearsOccupiedCell()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(4, 4, OccupancyType.Unit, 5, 1);
        grid.VacateCell(4, 4);

        Assert.Equal(OccupancyType.Empty, grid.Cells[4, 4].Type);
        Assert.Equal(-1, grid.Cells[4, 4].OccupantId);
        Assert.Equal(-1, grid.Cells[4, 4].PlayerId);
    }

    [Fact]
    public void VacateCell_OutOfBounds_Ignored()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.VacateCell(-1, 0); // no throw
        grid.VacateCell(0, -1); // no throw
        grid.VacateCell(8, 0);  // no throw
        grid.VacateCell(0, 8);  // no throw
    }

    // ── OccupyFootprint / VacateFootprint ───────────────────────────────────

    [Fact]
    public void OccupyFootprint_FillsRectangle()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyFootprint(2, 3, 3, 2, OccupancyType.Building, 7, 2);

        // All 6 cells in the 3×2 footprint should be occupied
        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                var cell = grid.Cells[2 + dx, 3 + dy];
                Assert.Equal(OccupancyType.Building, cell.Type);
                Assert.Equal(7, cell.OccupantId);
                Assert.Equal(2, cell.PlayerId);
            }
        }
    }

    [Fact]
    public void OccupyFootprint_DoesNotAffectAdjacentCells()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyFootprint(4, 4, 2, 2, OccupancyType.Building, 1, 1);

        // Cells just outside the footprint should be empty
        Assert.Equal(OccupancyType.Empty, grid.Cells[3, 4].Type);
        Assert.Equal(OccupancyType.Empty, grid.Cells[6, 4].Type);
        Assert.Equal(OccupancyType.Empty, grid.Cells[4, 3].Type);
        Assert.Equal(OccupancyType.Empty, grid.Cells[4, 6].Type);
    }

    [Fact]
    public void VacateFootprint_ClearsRectangle()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyFootprint(2, 2, 3, 3, OccupancyType.Building, 8, 1);
        grid.VacateFootprint(2, 2, 3, 3);

        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                Assert.Equal(OccupancyType.Empty, grid.Cells[2 + dx, 2 + dy].Type);
            }
        }
    }

    [Fact]
    public void OccupyFootprint_PartiallyOutOfBounds_ClampsGracefully()
    {
        var grid = new OccupancyGrid(8, 8);
        // A 3×3 footprint starting at (6,6) — only cells within bounds are set
        grid.OccupyFootprint(6, 6, 3, 3, OccupancyType.Building, 1, 1);

        // Cells at (6,6) and (7,6) are within bounds
        Assert.Equal(OccupancyType.Building, grid.Cells[6, 6].Type);
        Assert.Equal(OccupancyType.Building, grid.Cells[7, 6].Type);
        // (8, 6) and beyond are out of bounds — skipped silently
    }

    // ── ReserveCell ─────────────────────────────────────────────────────────

    [Fact]
    public void ReserveCell_SetsReservedType()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.ReserveCell(3, 5, 17, 1);

        Assert.Equal(OccupancyType.Reserved, grid.Cells[3, 5].Type);
        Assert.Equal(17, grid.Cells[3, 5].OccupantId);
        Assert.Equal(1, grid.Cells[3, 5].PlayerId);
    }

    // ── IsCellFree ──────────────────────────────────────────────────────────

    [Fact]
    public void IsCellFree_EmptyCell_True()
    {
        var grid = new OccupancyGrid(8, 8);
        Assert.True(grid.IsCellFree(3, 3));
    }

    [Fact]
    public void IsCellFree_UnitOccupied_False()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Unit, 1, 1);
        Assert.False(grid.IsCellFree(3, 3));
    }

    [Fact]
    public void IsCellFree_BuildingOccupied_False()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Building, 1, 1);
        Assert.False(grid.IsCellFree(3, 3));
    }

    [Fact]
    public void IsCellFree_Reserved_False()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.ReserveCell(3, 3, 1, 1);
        Assert.False(grid.IsCellFree(3, 3));
    }

    [Fact]
    public void IsCellFree_OutOfBounds_False()
    {
        var grid = new OccupancyGrid(8, 8);
        Assert.False(grid.IsCellFree(-1, 0));
        Assert.False(grid.IsCellFree(0, -1));
        Assert.False(grid.IsCellFree(8, 0));
        Assert.False(grid.IsCellFree(0, 8));
    }

    // ── IsCellPassable ──────────────────────────────────────────────────────

    [Fact]
    public void IsCellPassable_EmptyCell_AlwaysTrue()
    {
        var grid = new OccupancyGrid(8, 8);
        Assert.True(grid.IsCellPassable(3, 3, 1));
        Assert.True(grid.IsCellPassable(3, 3, 2));
    }

    [Fact]
    public void IsCellPassable_FriendlyUnit_True()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Unit, 5, 1);
        // Player 1 can path through their own unit's cell
        Assert.True(grid.IsCellPassable(3, 3, 1));
    }

    [Fact]
    public void IsCellPassable_EnemyUnit_False()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Unit, 5, 2);
        // Player 1 cannot path through player 2's unit
        Assert.False(grid.IsCellPassable(3, 3, 1));
    }

    [Fact]
    public void IsCellPassable_Building_AlwaysFalse()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.OccupyCell(3, 3, OccupancyType.Building, 100, 1);
        // Even friendly buildings block movement
        Assert.False(grid.IsCellPassable(3, 3, 1));
        Assert.False(grid.IsCellPassable(3, 3, 2));
    }

    [Fact]
    public void IsCellPassable_Reserved_False()
    {
        var grid = new OccupancyGrid(8, 8);
        grid.ReserveCell(3, 3, 5, 1);
        // Reserved cells block movement even for the reserving player
        Assert.False(grid.IsCellPassable(3, 3, 1));
        Assert.False(grid.IsCellPassable(3, 3, 2));
    }

    [Fact]
    public void IsCellPassable_OutOfBounds_False()
    {
        var grid = new OccupancyGrid(8, 8);
        Assert.False(grid.IsCellPassable(-1, 0, 1));
        Assert.False(grid.IsCellPassable(0, -1, 1));
        Assert.False(grid.IsCellPassable(8, 0, 1));
        Assert.False(grid.IsCellPassable(0, 8, 1));
    }

    // ── IsFootprintFree ─────────────────────────────────────────────────────

    [Fact]
    public void IsFootprintFree_AllEmpty_True()
    {
        var grid = new OccupancyGrid(16, 16);
        Assert.True(grid.IsFootprintFree(3, 3, 3, 3));
    }

    [Fact]
    public void IsFootprintFree_FullyOccupied_False()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyFootprint(3, 3, 3, 3, OccupancyType.Building, 1, 1);
        Assert.False(grid.IsFootprintFree(3, 3, 3, 3));
    }

    [Fact]
    public void IsFootprintFree_OneOccupiedCell_False()
    {
        var grid = new OccupancyGrid(16, 16);
        // Occupy a cell within the footprint
        grid.OccupyCell(3, 3, OccupancyType.Unit, 1, 1);
        Assert.False(grid.IsFootprintFree(3, 3, 3, 3));
    }

    [Fact]
    public void IsFootprintFree_AdjacentOccupied_True()
    {
        var grid = new OccupancyGrid(16, 16);
        // Occupy a cell just outside the footprint (3..5, 3..5)
        grid.OccupyCell(6, 3, OccupancyType.Unit, 1, 1);
        Assert.True(grid.IsFootprintFree(3, 3, 3, 3));
    }

    // ── GetCell ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetCell_ValidCoord_ReturnsCorrectData()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(7, 7, OccupancyType.Building, 55, 3);

        var cell = grid.GetCell(7, 7);
        Assert.Equal(OccupancyType.Building, cell.Type);
        Assert.Equal(55, cell.OccupantId);
        Assert.Equal(3, cell.PlayerId);
    }

    [Fact]
    public void GetCell_OutOfBounds_ReturnsEmptyCell()
    {
        var grid = new OccupancyGrid(8, 8);
        var cell = grid.GetCell(-1, 0);

        Assert.Equal(OccupancyType.Empty, cell.Type);
        Assert.Equal(-1, cell.OccupantId);
        Assert.Equal(-1, cell.PlayerId);
    }

    // ── GetOccupantAt ───────────────────────────────────────────────────────

    [Fact]
    public void GetOccupantAt_OccupiedCell_ReturnsId()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(4, 4, OccupancyType.Unit, 77, 1);
        Assert.Equal(77, grid.GetOccupantAt(4, 4));
    }

    [Fact]
    public void GetOccupantAt_EmptyCell_ReturnsMinusOne()
    {
        var grid = new OccupancyGrid(16, 16);
        Assert.Equal(-1, grid.GetOccupantAt(4, 4));
    }

    [Fact]
    public void GetOccupantAt_OutOfBounds_ReturnsMinusOne()
    {
        var grid = new OccupancyGrid(8, 8);
        Assert.Equal(-1, grid.GetOccupantAt(-1, 0));
        Assert.Equal(-1, grid.GetOccupantAt(0, 8));
    }

    // ── Interaction sequences ───────────────────────────────────────────────

    [Fact]
    public void Occupy_ThenVacate_CellIsEmpty()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(5, 5, OccupancyType.Unit, 3, 1);
        Assert.False(grid.IsCellFree(5, 5));

        grid.VacateCell(5, 5);
        Assert.True(grid.IsCellFree(5, 5));
    }

    [Fact]
    public void OccupyFootprint_ThenClear_AllCellsEmpty()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyFootprint(0, 0, 4, 4, OccupancyType.Building, 1, 1);
        grid.Clear();

        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                Assert.True(grid.IsCellFree(x, y));
            }
        }
    }

    [Fact]
    public void MultipleUnits_IndependentCells()
    {
        var grid = new OccupancyGrid(16, 16);
        grid.OccupyCell(1, 1, OccupancyType.Unit, 1, 1);
        grid.OccupyCell(5, 5, OccupancyType.Unit, 2, 2);

        Assert.Equal(1, grid.GetOccupantAt(1, 1));
        Assert.Equal(2, grid.GetOccupantAt(5, 5));
        Assert.True(grid.IsCellFree(3, 3)); // unaffected cell
    }
}
