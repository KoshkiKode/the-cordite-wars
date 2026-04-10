using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for TerrainGrid — the map's terrain data store.
/// Covers construction, bounds checks, cell accessors, coordinate conversions,
/// height sampling, and slope computation.
/// </summary>
public class TerrainGridTests
{
    // ── Construction ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsWidthHeightAndCellSize()
    {
        var grid = new TerrainGrid(32, 64, FixedPoint.One);
        Assert.Equal(32, grid.Width);
        Assert.Equal(64, grid.Height);
        Assert.Equal(FixedPoint.One, grid.CellSize);
    }

    [Fact]
    public void Constructor_DefaultsToGrassAtZeroHeight()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                ref var cell = ref grid.GetCell(x, y);
                Assert.Equal(TerrainType.Grass, cell.Type);
                Assert.Equal(FixedPoint.Zero, cell.Height);
                Assert.False(cell.IsBlocked);
            }
        }
    }

    [Fact]
    public void Constructor_SmallDimensions_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TerrainGrid(1, 8, FixedPoint.One));
        Assert.ThrowsAny<ArgumentException>(() => new TerrainGrid(8, 1, FixedPoint.One));
        Assert.ThrowsAny<ArgumentException>(() => new TerrainGrid(0, 8, FixedPoint.One));
    }

    [Fact]
    public void Constructor_ZeroCellSize_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TerrainGrid(8, 8, FixedPoint.Zero));
    }

    // ── IsInBounds ──────────────────────────────────────────────────────────

    [Fact]
    public void IsInBounds_CornerCells_True()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        Assert.True(grid.IsInBounds(0, 0));
        Assert.True(grid.IsInBounds(7, 0));
        Assert.True(grid.IsInBounds(0, 7));
        Assert.True(grid.IsInBounds(7, 7));
    }

    [Fact]
    public void IsInBounds_OutsideGrid_False()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        Assert.False(grid.IsInBounds(-1, 0));
        Assert.False(grid.IsInBounds(0, -1));
        Assert.False(grid.IsInBounds(8, 0));
        Assert.False(grid.IsInBounds(0, 8));
    }

    // ── GetCell / GetCellSafe ────────────────────────────────────────────────

    [Fact]
    public void GetCell_ReturnsRefAllowingMutation()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        ref var cell = ref grid.GetCell(3, 3);
        cell.Type = TerrainType.Road;
        cell.Height = FixedPoint.FromInt(5);

        // Read it back through the same accessor
        Assert.Equal(TerrainType.Road, grid.GetCell(3, 3).Type);
        Assert.Equal(FixedPoint.FromInt(5), grid.GetCell(3, 3).Height);
    }

    [Fact]
    public void GetCell_OutOfBounds_Throws()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        Assert.ThrowsAny<Exception>(() => { ref var _ = ref grid.GetCell(-1, 0); });
        Assert.ThrowsAny<Exception>(() => { ref var _ = ref grid.GetCell(0, 8); });
    }

    [Fact]
    public void GetCellSafe_ValidCoord_ReturnsData()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        ref var cell = ref grid.GetCell(2, 2);
        cell.Type = TerrainType.Mud;

        var safe = grid.GetCellSafe(2, 2);
        Assert.Equal(TerrainType.Mud, safe.Type);
    }

    [Fact]
    public void GetCellSafe_OutOfBounds_ReturnsVoidBlockedCell()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        var safe = grid.GetCellSafe(-1, 0);

        Assert.Equal(TerrainType.Void, safe.Type);
        Assert.True(safe.IsBlocked);
    }

    // ── WorldToGrid / GridToWorld ────────────────────────────────────────────

    [Fact]
    public void WorldToGrid_CellSizeOne_MapsCorrectly()
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        var (gx, gy) = grid.WorldToGrid(new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(5)));
        Assert.Equal(3, gx);
        Assert.Equal(5, gy);
    }

    [Fact]
    public void WorldToGrid_FractionalPosition_TruncatesToCell()
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        // Position 4.7, 2.9 should map to cell (4, 2) by floor/truncation
        var (gx, gy) = grid.WorldToGrid(new FixedVector2(
            FixedPoint.FromFloat(4.7f), FixedPoint.FromFloat(2.9f)));
        Assert.Equal(4, gx);
        Assert.Equal(2, gy);
    }

    [Fact]
    public void GridToWorld_ReturnsCellCentre()
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        // Cell (3, 5) should have world centre at (3.5, 5.5) with CellSize=1
        var centre = grid.GridToWorld(3, 5);
        Assert.True(Math.Abs(centre.X.ToFloat() - 3.5f) < 0.01f,
            $"Expected X=3.5, got {centre.X.ToFloat()}");
        Assert.True(Math.Abs(centre.Y.ToFloat() - 5.5f) < 0.01f,
            $"Expected Y=5.5, got {centre.Y.ToFloat()}");
    }

    [Fact]
    public void WorldToGrid_ThenGridToWorld_RoundTripsWithinHalfCell()
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        var worldPos = new FixedVector2(FixedPoint.FromFloat(6.3f), FixedPoint.FromFloat(9.1f));

        var (gx, gy) = grid.WorldToGrid(worldPos);
        var backToWorld = grid.GridToWorld(gx, gy);

        // The round-trip should land within ±0.5 of the original position
        Assert.True(Math.Abs(backToWorld.X.ToFloat() - worldPos.X.ToFloat()) < 0.5f,
            "Round-trip X should be within half a cell");
        Assert.True(Math.Abs(backToWorld.Y.ToFloat() - worldPos.Y.ToFloat()) < 0.5f,
            "Round-trip Y should be within half a cell");
    }

    // ── GetTerrainType ───────────────────────────────────────────────────────

    [Fact]
    public void GetTerrainType_ReturnsCorrectType()
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        ref var cell = ref grid.GetCell(4, 4);
        cell.Type = TerrainType.Sand;

        var worldPos = new FixedVector2(FixedPoint.FromFloat(4.5f), FixedPoint.FromFloat(4.5f));
        Assert.Equal(TerrainType.Sand, grid.GetTerrainType(worldPos));
    }

    [Fact]
    public void GetTerrainType_OutOfBounds_ClampsToEdge()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        // Off the left edge — should clamp to cell (0, 4)
        var worldPos = new FixedVector2(FixedPoint.FromInt(-5), FixedPoint.FromFloat(4.5f));
        // Should not throw
        var type = grid.GetTerrainType(worldPos);
        Assert.True(Enum.IsDefined(type));
    }

    // ── GetHeight (bilinear) ─────────────────────────────────────────────────

    [Fact]
    public void GetHeight_FlatTerrain_ReturnsZero()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        var height = grid.GetHeight(new FixedVector2(FixedPoint.FromFloat(3.5f), FixedPoint.FromFloat(3.5f)));
        Assert.Equal(FixedPoint.Zero, height);
    }

    [Fact]
    public void GetHeight_SingleElevatedCell_InterpolatesCorrectly()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        // Elevate cell (4, 4) to height 4
        grid.GetCell(4, 4).Height = FixedPoint.FromInt(4);

        // Query at the cell centre — should return exactly 4
        var centre = grid.GridToWorld(4, 4);
        float h = grid.GetHeight(centre).ToFloat();
        Assert.True(Math.Abs(h - 4.0f) < 0.1f,
            $"Expected ~4.0 at elevated cell centre, got {h}");
    }

    [Fact]
    public void GetHeight_BetweenFlatAndElevated_Interpolates()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        grid.GetCell(4, 4).Height = FixedPoint.Zero;
        grid.GetCell(5, 4).Height = FixedPoint.FromInt(2);

        // Midpoint between the two cells should have height ~1.0
        var midpoint = new FixedVector2(FixedPoint.FromFloat(5.0f), FixedPoint.FromFloat(4.5f));
        float h = grid.GetHeight(midpoint).ToFloat();
        Assert.True(h > 0.0f && h < 2.0f,
            $"Interpolated height should be between 0 and 2, got {h}");
    }

    // ── ComputeSlopes ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeSlopes_FlatTerrain_AllSlopesZero()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        grid.ComputeSlopes();

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                var cell = grid.GetCellSafe(x, y);
                Assert.Equal(FixedPoint.Zero, cell.SlopeAngle);
                Assert.Equal(FixedPoint.Zero, cell.SlopeX);
                Assert.Equal(FixedPoint.Zero, cell.SlopeY);
            }
        }
    }

    [Fact]
    public void ComputeSlopes_SlopeInXDirection_SlopeXNonZero()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        // Create a slope: column 0 = height 0, column 1 = height 1, etc.
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                grid.GetCell(x, y).Height = FixedPoint.FromInt(x);
            }
        }
        grid.ComputeSlopes();

        // Interior cells should have SlopeX ≈ 1.0 (rise/run = 1/1)
        var cell = grid.GetCellSafe(4, 4); // interior cell
        Assert.True(cell.SlopeX > FixedPoint.Zero,
            $"SlopeX should be positive for an increasing-x gradient, got {cell.SlopeX.ToFloat()}");
    }

    [Fact]
    public void ComputeSlopes_SlopeInYDirection_SlopeYNonZero()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                grid.GetCell(x, y).Height = FixedPoint.FromInt(y);
            }
        }
        grid.ComputeSlopes();

        var cell = grid.GetCellSafe(4, 4);
        Assert.True(cell.SlopeY > FixedPoint.Zero,
            $"SlopeY should be positive for an increasing-y gradient, got {cell.SlopeY.ToFloat()}");
    }

    [Fact]
    public void ComputeSlopes_SteepSlope_SlopeAnglePositive()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        // Big height jump between adjacent cells → large slope angle
        grid.GetCell(3, 4).Height = FixedPoint.Zero;
        grid.GetCell(4, 4).Height = FixedPoint.FromInt(5);
        grid.GetCell(5, 4).Height = FixedPoint.FromInt(10);
        grid.ComputeSlopes();

        var cell = grid.GetCellSafe(4, 4);
        Assert.True(cell.SlopeAngle > FixedPoint.Zero,
            $"Steep terrain should have positive SlopeAngle, got {cell.SlopeAngle.ToFloat()}");
    }

    [Fact]
    public void ComputeSlopes_Deterministic_SameInputSameOutput()
    {
        var grid1 = new TerrainGrid(8, 8, FixedPoint.One);
        var grid2 = new TerrainGrid(8, 8, FixedPoint.One);

        // Set the same height on both grids
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                var h = FixedPoint.FromInt(x + y);
                grid1.GetCell(x, y).Height = h;
                grid2.GetCell(x, y).Height = h;
            }
        }

        grid1.ComputeSlopes();
        grid2.ComputeSlopes();

        // Interior cell should have identical slopes
        var c1 = grid1.GetCellSafe(4, 4);
        var c2 = grid2.GetCellSafe(4, 4);
        Assert.Equal(c1.SlopeX.Raw, c2.SlopeX.Raw);
        Assert.Equal(c1.SlopeY.Raw, c2.SlopeY.Raw);
        Assert.Equal(c1.SlopeAngle.Raw, c2.SlopeAngle.Raw);
    }

    // ── GetSlope (bilinear) ───────────────────────────────────────────────────

    [Fact]
    public void GetSlope_FlatTerrain_ReturnsZeroVector()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        grid.ComputeSlopes();

        var slope = grid.GetSlope(new FixedVector2(FixedPoint.FromFloat(3.5f), FixedPoint.FromFloat(3.5f)));
        Assert.Equal(FixedVector2.Zero, slope);
    }

    [Fact]
    public void GetSlope_SlopedTerrain_ReturnsNonZeroVector()
    {
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                grid.GetCell(x, y).Height = FixedPoint.FromInt(x * 2);
            }
        }
        grid.ComputeSlopes();

        var slope = grid.GetSlope(new FixedVector2(FixedPoint.FromFloat(4.5f), FixedPoint.FromFloat(4.5f)));
        // Should have a non-zero X component since terrain rises in X
        Assert.True(slope.X != FixedPoint.Zero,
            "Sloped terrain should produce non-zero gradient");
    }
}
