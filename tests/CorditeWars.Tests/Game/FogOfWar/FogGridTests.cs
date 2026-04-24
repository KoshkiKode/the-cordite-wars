using CorditeWars.Systems.FogOfWar;

namespace CorditeWars.Tests.Game.FogOfWar;

/// <summary>
/// Tests for FogGrid — per-player fog-of-war state grid.
/// Covers construction modes, visibility queries, ref-count manipulation,
/// bulk reset, and reveal-all.
/// </summary>
public class FogGridTests
{
    // Helper: flat-index accessor matching the row-major layout (y * Width + x).
    private static FogCell Cell(FogGrid g, int x, int y)
        => g.Cells[y * g.Width + x];

    // ── Construction ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Campaign_AllCellsUnexplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(x, y));
                Assert.False(Cell(grid, x, y).WasEverVisible);
                Assert.Equal(0, Cell(grid, x, y).VisibilityRefCount);
            }
        }
    }

    [Fact]
    public void Constructor_Skirmish_AllCellsExplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Skirmish);

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                Assert.Equal(FogVisibility.Explored, grid.GetVisibility(x, y));
                Assert.True(Cell(grid, x, y).WasEverVisible);
            }
        }
    }

    [Fact]
    public void Constructor_SetsWidthHeightPlayerId()
    {
        var grid = new FogGrid(16, 32, 3, FogMode.Campaign);
        Assert.Equal(16, grid.Width);
        Assert.Equal(32, grid.Height);
        Assert.Equal(3, grid.PlayerId);
    }

    // ── IsInBounds ─────────────────────────────────────────────────────────

    [Fact]
    public void IsInBounds_CornerCells_True()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.True(grid.IsInBounds(0, 0));
        Assert.True(grid.IsInBounds(7, 7));
        Assert.True(grid.IsInBounds(0, 7));
        Assert.True(grid.IsInBounds(7, 0));
    }

    [Fact]
    public void IsInBounds_OutsideCells_False()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.False(grid.IsInBounds(-1, 0));
        Assert.False(grid.IsInBounds(0, -1));
        Assert.False(grid.IsInBounds(8, 0));
        Assert.False(grid.IsInBounds(0, 8));
    }

    // ── GetVisibility ──────────────────────────────────────────────────────

    [Fact]
    public void GetVisibility_OutOfBounds_ReturnsUnexplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(-1, 0));
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(0, -1));
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(8, 0));
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(0, 8));
    }

    // ── IsVisible / IsExplored ─────────────────────────────────────────────

    [Fact]
    public void IsVisible_CampaignStart_False()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.False(grid.IsVisible(3, 3));
    }

    [Fact]
    public void IsExplored_CampaignStart_False()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.False(grid.IsExplored(3, 3));
    }

    [Fact]
    public void IsExplored_SkirmishStart_True()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Skirmish);
        Assert.True(grid.IsExplored(3, 3));
    }

    [Fact]
    public void IsVisible_OutOfBounds_False()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.False(grid.IsVisible(-1, 0));
        Assert.False(grid.IsVisible(0, 8));
    }

    [Fact]
    public void IsExplored_OutOfBounds_False()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        Assert.False(grid.IsExplored(-1, 0));
        Assert.False(grid.IsExplored(0, 8));
    }

    // ── AddVisibility ──────────────────────────────────────────────────────

    [Fact]
    public void AddVisibility_SetsVisibleAndWasEverVisible()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(3, 3);

        Assert.Equal(FogVisibility.Visible, grid.GetVisibility(3, 3));
        Assert.True(Cell(grid, 3, 3).WasEverVisible);
        Assert.True(grid.IsVisible(3, 3));
        Assert.True(grid.IsExplored(3, 3));
    }

    [Fact]
    public void AddVisibility_IncrementsRefCount()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(2, 2);
        Assert.Equal(1, Cell(grid, 2, 2).VisibilityRefCount);
        grid.AddVisibility(2, 2);
        Assert.Equal(2, Cell(grid, 2, 2).VisibilityRefCount);
    }

    [Fact]
    public void AddVisibility_OutOfBounds_Ignored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        // Must not throw
        grid.AddVisibility(-1, 0);
        grid.AddVisibility(0, 8);
        // Grid unchanged
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(0, 0));
    }

    // ── RemoveVisibility ───────────────────────────────────────────────────

    [Fact]
    public void RemoveVisibility_RefCountDropsToZero_TransitionsToExplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(4, 4);
        grid.RemoveVisibility(4, 4);

        Assert.Equal(FogVisibility.Explored, grid.GetVisibility(4, 4));
        Assert.Equal(0, Cell(grid, 4, 4).VisibilityRefCount);
        Assert.True(Cell(grid, 4, 4).WasEverVisible);
    }

    [Fact]
    public void RemoveVisibility_MultipleViewers_StaysVisible()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(5, 5);
        grid.AddVisibility(5, 5); // 2 viewers
        grid.RemoveVisibility(5, 5); // still 1 viewer

        Assert.Equal(FogVisibility.Visible, grid.GetVisibility(5, 5));
        Assert.Equal(1, Cell(grid, 5, 5).VisibilityRefCount);
    }

    [Fact]
    public void RemoveVisibility_BelowZero_ClampsToZero()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(1, 1);
        grid.RemoveVisibility(1, 1); // now 0
        grid.RemoveVisibility(1, 1); // should not underflow

        Assert.Equal(0, Cell(grid, 1, 1).VisibilityRefCount);
    }

    [Fact]
    public void RemoveVisibility_OutOfBounds_Ignored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        // Must not throw
        grid.RemoveVisibility(-1, 0);
        grid.RemoveVisibility(0, 8);
    }

    [Fact]
    public void RemoveVisibility_UnexploredCell_StaysUnexplored()
    {
        // An unexplored cell that somehow loses visibility (edge case) should
        // not transition to Explored because it was never made Visible.
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        // Add and remove without the cell ever transitioning to Visible first
        // (the implementation guards on cell.Visibility == Visible before transitioning)
        grid.RemoveVisibility(3, 3);
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(3, 3));
    }

    // ── ResetVisibility ────────────────────────────────────────────────────

    [Fact]
    public void ResetVisibility_ClearsAllRefCounts()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(1, 1);
        grid.AddVisibility(2, 2);
        grid.AddVisibility(2, 2); // 2 refs

        grid.ResetVisibility();

        Assert.Equal(0, Cell(grid, 1, 1).VisibilityRefCount);
        Assert.Equal(0, Cell(grid, 2, 2).VisibilityRefCount);
    }

    [Fact]
    public void ResetVisibility_VisibleCellsTransitionToExplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(3, 3);
        Assert.Equal(FogVisibility.Visible, grid.GetVisibility(3, 3));

        grid.ResetVisibility();

        Assert.Equal(FogVisibility.Explored, grid.GetVisibility(3, 3));
    }

    [Fact]
    public void ResetVisibility_UnexploredCellsStayUnexplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        // Never add visibility to (0,0)
        grid.ResetVisibility();
        Assert.Equal(FogVisibility.Unexplored, grid.GetVisibility(0, 0));
    }

    [Fact]
    public void ResetVisibility_ExploredCellsStayExplored()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(4, 4); // becomes Visible
        grid.RemoveVisibility(4, 4); // becomes Explored

        grid.ResetVisibility(); // already Explored, stays Explored
        Assert.Equal(FogVisibility.Explored, grid.GetVisibility(4, 4));
    }

    [Fact]
    public void ResetVisibility_WasEverVisibleRetained()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(5, 5);
        grid.ResetVisibility();
        Assert.True(Cell(grid, 5, 5).WasEverVisible);
    }

    // ── RevealAll ──────────────────────────────────────────────────────────

    [Fact]
    public void RevealAll_AllCellsVisible()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.RevealAll();

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                Assert.Equal(FogVisibility.Visible, grid.GetVisibility(x, y));
                Assert.True(Cell(grid, x, y).WasEverVisible);
                Assert.Equal(1, Cell(grid, x, y).VisibilityRefCount);
            }
        }
    }

    [Fact]
    public void RevealAll_OnSkirmishGrid_AllBecomesVisible()
    {
        var grid = new FogGrid(4, 4, 1, FogMode.Skirmish);
        grid.RevealAll();

        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                Assert.True(grid.IsVisible(x, y));
            }
        }
    }

    // ── Interaction sequences ──────────────────────────────────────────────

    [Fact]
    public void AddRemoveAdd_ReturnsToVisible()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(2, 2);
        grid.RemoveVisibility(2, 2);
        Assert.Equal(FogVisibility.Explored, grid.GetVisibility(2, 2));

        grid.AddVisibility(2, 2);
        Assert.Equal(FogVisibility.Visible, grid.GetVisibility(2, 2));
    }

    [Fact]
    public void ResetThenAddVisibility_WorksCorrectly()
    {
        var grid = new FogGrid(8, 8, 1, FogMode.Campaign);
        grid.AddVisibility(3, 3);
        grid.ResetVisibility(); // → Explored

        // Re-add after reset should restore Visible
        grid.AddVisibility(3, 3);
        Assert.Equal(FogVisibility.Visible, grid.GetVisibility(3, 3));
        Assert.Equal(1, Cell(grid, 3, 3).VisibilityRefCount);
    }
}
