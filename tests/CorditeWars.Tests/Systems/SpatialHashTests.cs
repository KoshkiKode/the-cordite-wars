using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for SpatialHash — uniform-grid spatial index for fast proximity queries.
/// Verifies insertion, clearing, and radius-based lookup behaviour.
/// </summary>
public class SpatialHashTests
{
    // ── Constructor validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, 16)]
    [InlineData(-1, 16)]
    [InlineData(16, 0)]
    [InlineData(16, -1)]
    public void Constructor_InvalidDimensions_Throws(int width, int height)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SpatialHash(width, height));
    }

    [Fact]
    public void Constructor_InvalidCellSize_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SpatialHash(64, 64, 0));
        Assert.ThrowsAny<ArgumentException>(() => new SpatialHash(64, 64, -1));
    }

    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var hash = new SpatialHash(64, 64, 8);
        Assert.Equal(8, hash.CellSize);
    }

    // ── QueryRadius after construction ──────────────────────────────────────

    [Fact]
    public void QueryRadius_EmptyHash_ReturnsNoResults()
    {
        var hash = new SpatialHash(64, 64);
        var results = new List<int>();
        hash.QueryRadius(FixedVector2.Zero, FixedPoint.FromInt(10), results);
        Assert.Empty(results);
    }

    // ── Insert + QueryRadius ────────────────────────────────────────────────

    [Fact]
    public void QueryRadius_SingleInsertedUnit_FoundWithinRange()
    {
        var hash = new SpatialHash(64, 64);
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        hash.Insert(1, pos, FixedPoint.One);

        var results = new List<int>();
        // Query from same position with large radius — should find unit 1
        hash.QueryRadius(pos, FixedPoint.FromInt(5), results);

        Assert.Contains(1, results);
    }

    [Fact]
    public void QueryRadius_UnitFarAway_NotFoundInSmallRadius()
    {
        var hash = new SpatialHash(64, 64);
        // Insert at (50, 50)
        var unitPos = new FixedVector2(FixedPoint.FromInt(50), FixedPoint.FromInt(50));
        hash.Insert(1, unitPos, FixedPoint.One);

        var results = new List<int>();
        // Query at (5, 5) with radius 5 — unit at (50,50) is 63+ units away
        var queryCenter = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        hash.QueryRadius(queryCenter, FixedPoint.FromInt(5), results);

        Assert.DoesNotContain(1, results);
    }

    [Fact]
    public void QueryRadius_MultipleUnits_FindsAllInRange()
    {
        var hash = new SpatialHash(64, 64);
        var center = new FixedVector2(FixedPoint.FromInt(20), FixedPoint.FromInt(20));

        // Insert 4 units within radius 5 of center
        for (int i = 1; i <= 4; i++)
        {
            var pos = new FixedVector2(
                FixedPoint.FromInt(20 + i),
                FixedPoint.FromInt(20));
            hash.Insert(i, pos, FixedPoint.One);
        }

        // Insert one unit far away
        hash.Insert(99, new FixedVector2(FixedPoint.FromInt(60), FixedPoint.FromInt(60)), FixedPoint.One);

        var results = new List<int>();
        hash.QueryRadius(center, FixedPoint.FromInt(10), results);

        for (int i = 1; i <= 4; i++)
        {
            Assert.Contains(i, results);
        }
        Assert.DoesNotContain(99, results);
    }

    // ── Clear ───────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllInsertedUnits()
    {
        var hash = new SpatialHash(64, 64);
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        hash.Insert(1, pos, FixedPoint.One);
        hash.Insert(2, pos, FixedPoint.One);

        hash.Clear();

        var results = new List<int>();
        hash.QueryRadius(pos, FixedPoint.FromInt(20), results);
        Assert.Empty(results);
    }

    [Fact]
    public void Clear_ThenReinsert_FindsNewUnits()
    {
        var hash = new SpatialHash(64, 64);
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        hash.Insert(1, pos, FixedPoint.One);
        hash.Clear();
        hash.Insert(2, pos, FixedPoint.One);

        var results = new List<int>();
        hash.QueryRadius(pos, FixedPoint.FromInt(5), results);

        Assert.DoesNotContain(1, results);
        Assert.Contains(2, results);
    }

    // ── Edge / boundary cases ───────────────────────────────────────────────

    [Fact]
    public void Insert_AtOrigin_FoundByQuery()
    {
        var hash = new SpatialHash(64, 64);
        hash.Insert(1, FixedVector2.Zero, FixedPoint.One);

        var results = new List<int>();
        hash.QueryRadius(FixedVector2.Zero, FixedPoint.FromInt(5), results);
        Assert.Contains(1, results);
    }

    [Fact]
    public void Insert_AtWorldEdge_DoesNotThrow()
    {
        var hash = new SpatialHash(64, 64);
        // Near the boundary — should not crash
        var edgePos = new FixedVector2(FixedPoint.FromInt(63), FixedPoint.FromInt(63));
        hash.Insert(1, edgePos, FixedPoint.One); // no throw
    }

    [Fact]
    public void Insert_LargeRadius_StillInsertsWithoutThrow()
    {
        var hash = new SpatialHash(64, 64);
        var pos = new FixedVector2(FixedPoint.FromInt(32), FixedPoint.FromInt(32));
        // Radius larger than a single cell but within world bounds
        hash.Insert(1, pos, FixedPoint.FromInt(16)); // no throw
    }

    [Fact]
    public void QueryRadius_ZeroRadius_CanReturnUnitsAtSameCell()
    {
        var hash = new SpatialHash(64, 64, 4);
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        hash.Insert(1, pos, FixedPoint.Zero);

        var results = new List<int>();
        hash.QueryRadius(pos, FixedPoint.Zero, results);

        // The unit inserted at the exact query position should be in the same cell
        Assert.Contains(1, results);
    }

    [Fact]
    public void QueryRadius_DoesNotClearResultsList()
    {
        var hash = new SpatialHash(64, 64);
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        hash.Insert(1, pos, FixedPoint.One);
        hash.Insert(2, pos, FixedPoint.One);

        var results = new List<int> { 999 }; // pre-existing entry
        hash.QueryRadius(pos, FixedPoint.FromInt(5), results);

        // The pre-existing entry should still be present (QueryRadius appends, not replaces)
        Assert.Contains(999, results);
        Assert.Contains(1, results);
        Assert.Contains(2, results);
    }

    // ── Determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void QueryRadius_Deterministic_SameInputSameOrder()
    {
        var hash1 = new SpatialHash(64, 64);
        var hash2 = new SpatialHash(64, 64);

        // Insert units in the same order
        for (int i = 1; i <= 5; i++)
        {
            var pos = new FixedVector2(FixedPoint.FromInt(i * 3), FixedPoint.FromInt(10));
            hash1.Insert(i, pos, FixedPoint.One);
            hash2.Insert(i, pos, FixedPoint.One);
        }

        var center = new FixedVector2(FixedPoint.FromInt(8), FixedPoint.FromInt(10));
        var results1 = new List<int>();
        var results2 = new List<int>();

        hash1.QueryRadius(center, FixedPoint.FromInt(20), results1);
        hash2.QueryRadius(center, FixedPoint.FromInt(20), results2);

        Assert.Equal(results1.Count, results2.Count);
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i], results2[i]);
        }
    }
}
