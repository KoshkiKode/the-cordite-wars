using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for TerrainCostCalculator — the single source of truth for
/// "can this unit go there?" and "how expensive is it?".
///
/// All functions use FixedPoint arithmetic, so results are deterministic.
/// </summary>
public class TerrainCostCalculatorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a flat grid where every cell is the given terrain type.</summary>
    private static TerrainGrid MakeGrid(TerrainType type = TerrainType.Grass)
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        if (type != TerrainType.Grass) // Grass is the default — only loop when needed
        {
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    grid.GetCell(x, y).Type = type;
                }
            }
        }
        return grid;
    }

    /// <summary>Creates a flat grid and marks one cell as a different type.</summary>
    private static TerrainGrid MakeGridWithCell(int x, int y, TerrainType type, bool blocked = false)
    {
        var grid = new TerrainGrid(16, 16, FixedPoint.One);
        ref var cell = ref grid.GetCell(x, y);
        cell.Type = type;
        cell.IsBlocked = blocked;
        return grid;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetSlopePenalty
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSlopePenalty_FlatTerrain_ReturnsOne()
    {
        FixedPoint penalty = TerrainCostCalculator.GetSlopePenalty(FixedPoint.Zero, FixedPoint.FromFloat(0.87f));
        Assert.Equal(FixedPoint.One, penalty);
    }

    [Fact]
    public void GetSlopePenalty_ZeroMaxSlope_ReturnsOne()
    {
        // If maxSlope is zero, slope penalty is 1 (no slope model applies)
        FixedPoint penalty = TerrainCostCalculator.GetSlopePenalty(FixedPoint.FromFloat(0.3f), FixedPoint.Zero);
        Assert.Equal(FixedPoint.One, penalty);
    }

    [Fact]
    public void GetSlopePenalty_SlopeExceedsMax_ReturnsMaxValue()
    {
        FixedPoint maxSlope = FixedPoint.FromFloat(0.87f);
        FixedPoint tooSteep = FixedPoint.FromFloat(1.0f); // > maxSlope
        FixedPoint penalty = TerrainCostCalculator.GetSlopePenalty(tooSteep, maxSlope);
        Assert.Equal(FixedPoint.MaxValue, penalty);
    }

    [Fact]
    public void GetSlopePenalty_HalfMaxSlope_ReturnsBetweenOneAndFour()
    {
        // At ratio = 0.5: penalty = 1 + 3 * 0.5² = 1 + 0.75 = 1.75
        FixedPoint maxSlope = FixedPoint.FromFloat(1.0f);
        FixedPoint halfSlope = FixedPoint.FromFloat(0.5f);
        FixedPoint penalty = TerrainCostCalculator.GetSlopePenalty(halfSlope, maxSlope);

        float value = penalty.ToFloat();
        Assert.True(value > 1.0f, $"Penalty should be > 1.0 for non-zero slope, got {value}");
        Assert.True(value < 4.0f, $"Penalty should be < 4.0 (max), got {value}");
        Assert.True(Math.Abs(value - 1.75f) < 0.05f,
            $"Expected ~1.75, got {value}");
    }

    [Fact]
    public void GetSlopePenalty_AtMaxSlope_ReturnsApproximatelyFour()
    {
        // At ratio = 1.0: penalty = 1 + 3 * 1² = 4.0
        FixedPoint maxSlope = FixedPoint.FromFloat(1.0f);
        FixedPoint slope = FixedPoint.FromFloat(1.0f); // equal to max (not exceeding)
        FixedPoint penalty = TerrainCostCalculator.GetSlopePenalty(slope, maxSlope);

        float value = penalty.ToFloat();
        Assert.True(Math.Abs(value - 4.0f) < 0.1f,
            $"Expected ~4.0 at ratio=1.0, got {value}");
    }

    [Fact]
    public void GetSlopePenalty_Deterministic_SameInputSameOutput()
    {
        FixedPoint slope = FixedPoint.FromFloat(0.4f);
        FixedPoint maxSlope = FixedPoint.FromFloat(0.87f);

        FixedPoint p1 = TerrainCostCalculator.GetSlopePenalty(slope, maxSlope);
        FixedPoint p2 = TerrainCostCalculator.GetSlopePenalty(slope, maxSlope);

        Assert.Equal(p1.Raw, p2.Raw);
    }

    [Fact]
    public void GetSlopePenalty_IncreaseMonotonically()
    {
        FixedPoint maxSlope = FixedPoint.FromFloat(1.0f);

        FixedPoint p25 = TerrainCostCalculator.GetSlopePenalty(FixedPoint.FromFloat(0.25f), maxSlope);
        FixedPoint p50 = TerrainCostCalculator.GetSlopePenalty(FixedPoint.FromFloat(0.50f), maxSlope);
        FixedPoint p75 = TerrainCostCalculator.GetSlopePenalty(FixedPoint.FromFloat(0.75f), maxSlope);

        Assert.True(p25 < p50, "Penalty should increase with slope");
        Assert.True(p50 < p75, "Penalty should increase with slope");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetElevationCost
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetElevationCost_FlatTerrain_ReturnsZero()
    {
        FixedPoint cost = TerrainCostCalculator.GetElevationCost(FixedPoint.Zero);
        Assert.Equal(FixedPoint.Zero, cost);
    }

    [Fact]
    public void GetElevationCost_Uphill_ReturnsPositive()
    {
        // 2.0 height gain × 0.5 factor = 1.0 extra cost
        FixedPoint cost = TerrainCostCalculator.GetElevationCost(FixedPoint.FromInt(2));
        float value = cost.ToFloat();
        Assert.True(value > 0f, $"Uphill cost must be positive, got {value}");
        Assert.True(Math.Abs(value - 1.0f) < 0.05f, $"Expected ~1.0, got {value}");
    }

    [Fact]
    public void GetElevationCost_Downhill_ReturnsNegative()
    {
        // Height drop of -2.0 → discount = 2.0 × 0.2 = 0.4 → cost = -0.4
        FixedPoint cost = TerrainCostCalculator.GetElevationCost(FixedPoint.FromInt(-2));
        float value = cost.ToFloat();
        Assert.True(value < 0f, $"Downhill cost must be negative (a discount), got {value}");
        Assert.True(Math.Abs(value - (-0.4f)) < 0.05f, $"Expected ~-0.4, got {value}");
    }

    [Fact]
    public void GetElevationCost_UphillCostIsLargerThanDownhillDiscount()
    {
        // Round-trip (uphill then downhill same distance) should have net positive cost
        FixedPoint uphill = TerrainCostCalculator.GetElevationCost(FixedPoint.FromInt(3));
        FixedPoint downhill = TerrainCostCalculator.GetElevationCost(FixedPoint.FromInt(-3));
        // uphill + downhill > 0 (asymmetric by design)
        Assert.True(uphill + downhill > FixedPoint.Zero,
            "Net round-trip elevation cost must be positive");
    }

    [Fact]
    public void GetElevationCost_LargeDownhill_CappedAtMinusOne()
    {
        // Very large downhill — discount should not be worse than -BaseCost = -1.0
        FixedPoint cost = TerrainCostCalculator.GetElevationCost(FixedPoint.FromInt(-100));
        float value = cost.ToFloat();
        Assert.True(value >= -1.0f, $"Downhill discount must not exceed BaseCost, got {value}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CanTraverse
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CanTraverse_Infantry_OnGrass_True()
    {
        var grid = MakeGrid(TerrainType.Grass);
        var profile = MovementProfile.Infantry();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnVoid_False()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Void);
        var profile = MovementProfile.Infantry();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnWater_False()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Water);
        var profile = MovementProfile.Infantry();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnLava_False()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Lava);
        var profile = MovementProfile.Infantry();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnRoad_True()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Road);
        var profile = MovementProfile.Infantry();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnMud_True()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Mud);
        var profile = MovementProfile.Infantry();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_AirUnit_OnAnyTerrain_True()
    {
        var profile = MovementProfile.Helicopter();
        // Air can traverse any non-Void terrain
        foreach (TerrainType type in new[] { TerrainType.Grass, TerrainType.Water, TerrainType.Mud, TerrainType.Lava })
        {
            var grid = MakeGridWithCell(5, 5, type);
            Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5),
                $"Air unit should traverse {type}");
        }
    }

    [Fact]
    public void CanTraverse_AirUnit_OnVoid_False()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Void);
        var profile = MovementProfile.Helicopter();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Naval_OnWater_True()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Water);
        var profile = MovementProfile.Naval();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Naval_OnDeepWater_True()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.DeepWater);
        var profile = MovementProfile.Naval();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Naval_OnGrass_False()
    {
        var grid = MakeGrid(TerrainType.Grass);
        var profile = MovementProfile.Naval();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Naval_OnBridge_False()
    {
        // Bridges are land crossings, not navigable water
        var grid = MakeGridWithCell(5, 5, TerrainType.Bridge);
        var profile = MovementProfile.Naval();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_BlockedCell_False()
    {
        var grid = MakeGridWithCell(5, 5, TerrainType.Grass, blocked: true);
        var profile = MovementProfile.Infantry();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_OutOfBounds_False()
    {
        var grid = MakeGrid();
        var profile = MovementProfile.Infantry();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, -1, 0));
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 0, -1));
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 16, 0));
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 0, 16));
    }

    [Fact]
    public void CanTraverse_HeavyVehicle_OnIce_False()
    {
        // Heavy vehicles cannot traverse ice (too heavy, breaks through)
        var grid = MakeGridWithCell(5, 5, TerrainType.Ice);
        var profile = MovementProfile.HeavyVehicle();
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_Infantry_OnIce_True()
    {
        // Infantry can traverse ice (they just slip)
        var grid = MakeGridWithCell(5, 5, TerrainType.Ice);
        var profile = MovementProfile.Infantry();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_SlopeTooSteepForProfile_False()
    {
        var grid = MakeGrid(TerrainType.Grass);
        // Set a slope angle exceeding heavy vehicle max (~20°)
        ref var cell = ref grid.GetCell(5, 5);
        cell.SlopeAngle = FixedPoint.FromFloat(0.50f); // ~28.6° — above 20° limit for HeavyVehicle

        var profile = MovementProfile.HeavyVehicle(); // maxSlopeAngle ~0.35
        Assert.False(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    [Fact]
    public void CanTraverse_SlopeWithinLimit_True()
    {
        var grid = MakeGrid(TerrainType.Grass);
        // Set slope just within infantry limit (~50°)
        ref var cell = ref grid.GetCell(5, 5);
        cell.SlopeAngle = FixedPoint.FromFloat(0.80f); // < 0.87 (infantry max)

        var profile = MovementProfile.Infantry();
        Assert.True(TerrainCostCalculator.CanTraverse(grid, profile, 5, 5));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetMovementCost
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementCost_FlatGrass_ReturnsNearBaseCost()
    {
        // Infantry on flat grass: modifier = 1.0, no slope, no elevation
        // Cost = 1.0 / 1.0 * 1.0 + 0.0 = 1.0
        var grid = MakeGrid(TerrainType.Grass);
        var profile = MovementProfile.Infantry();
        FixedPoint cost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);

        Assert.NotEqual(FixedPoint.MaxValue, cost);
        float value = cost.ToFloat();
        Assert.True(Math.Abs(value - 1.0f) < 0.05f, $"Expected ~1.0 on flat grass, got {value}");
    }

    [Fact]
    public void GetMovementCost_Road_LowerThanGrass_ForGroundUnit()
    {
        // Road has speed modifier > 1.0 → lower cost than grass
        var grassGrid = MakeGrid(TerrainType.Grass);
        var roadGrid = MakeGrid(TerrainType.Road);
        var profile = MovementProfile.Infantry();

        FixedPoint grassCost = TerrainCostCalculator.GetMovementCost(grassGrid, profile, 4, 4, 5, 4);
        FixedPoint roadCost = TerrainCostCalculator.GetMovementCost(roadGrid, profile, 4, 4, 5, 4);

        Assert.True(roadCost < grassCost,
            $"Road (modifier>1) should be cheaper than grass: road={roadCost.ToFloat()}, grass={grassCost.ToFloat()}");
    }

    [Fact]
    public void GetMovementCost_Mud_HigherThanGrass_ForGroundUnit()
    {
        // Mud has speed modifier < 1.0 → higher cost than grass
        var grassGrid = MakeGrid(TerrainType.Grass);
        var mudGrid = MakeGrid(TerrainType.Mud);
        var profile = MovementProfile.Infantry();

        FixedPoint grassCost = TerrainCostCalculator.GetMovementCost(grassGrid, profile, 4, 4, 5, 4);
        FixedPoint mudCost = TerrainCostCalculator.GetMovementCost(mudGrid, profile, 4, 4, 5, 4);

        Assert.True(mudCost > grassCost,
            $"Mud (modifier<1) should be more expensive than grass: mud={mudCost.ToFloat()}, grass={grassCost.ToFloat()}");
    }

    [Fact]
    public void GetMovementCost_ImpassableCell_ReturnsMaxValue()
    {
        var grid = MakeGridWithCell(5, 4, TerrainType.Water);
        var profile = MovementProfile.Infantry(); // infantry can't cross water
        FixedPoint cost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);
        Assert.Equal(FixedPoint.MaxValue, cost);
    }

    [Fact]
    public void GetMovementCost_AirUnit_ReturnsFlatBaseCost()
    {
        // Air units get the same flat base cost regardless of terrain type
        var grassGrid = MakeGrid(TerrainType.Grass);
        var mudGrid = MakeGrid(TerrainType.Mud);
        var profile = MovementProfile.Helicopter();

        FixedPoint grassCost = TerrainCostCalculator.GetMovementCost(grassGrid, profile, 4, 4, 5, 4);
        FixedPoint mudCost = TerrainCostCalculator.GetMovementCost(mudGrid, profile, 4, 4, 5, 4);

        // Same cost regardless of terrain
        Assert.Equal(grassCost, mudCost);
        Assert.Equal(FixedPoint.One, grassCost);
    }

    [Fact]
    public void GetMovementCost_NavalUnit_ReturnsFlatBaseCost()
    {
        var shallowGrid = MakeGrid(TerrainType.Water);
        var deepGrid = MakeGrid(TerrainType.DeepWater);
        var profile = MovementProfile.Naval();

        FixedPoint shallowCost = TerrainCostCalculator.GetMovementCost(shallowGrid, profile, 4, 4, 5, 4);
        FixedPoint deepCost = TerrainCostCalculator.GetMovementCost(deepGrid, profile, 4, 4, 5, 4);

        // Same flat cost on both water types
        Assert.Equal(shallowCost, deepCost);
        Assert.Equal(FixedPoint.One, shallowCost);
    }

    [Fact]
    public void GetMovementCost_UphillMove_MoreExpensiveThanFlat()
    {
        var grid = MakeGrid(TerrainType.Grass);
        // Set cell (5,4) higher than (4,4)
        grid.GetCell(4, 4).Height = FixedPoint.Zero;
        grid.GetCell(5, 4).Height = FixedPoint.FromInt(2); // 2 units higher

        var profile = MovementProfile.Infantry();
        FixedPoint uphillCost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);
        FixedPoint flatCost = FixedPoint.One; // baseline

        Assert.True(uphillCost > flatCost,
            $"Uphill move should cost more than flat: {uphillCost.ToFloat()} vs {flatCost.ToFloat()}");
    }

    [Fact]
    public void GetMovementCost_DownhillMove_LessExpensiveThanFlat()
    {
        var grid = MakeGrid(TerrainType.Grass);
        // Move FROM a higher cell down to a lower cell
        grid.GetCell(4, 4).Height = FixedPoint.FromInt(2); // source is high
        grid.GetCell(5, 4).Height = FixedPoint.Zero;        // dest is low

        var profile = MovementProfile.Infantry();
        FixedPoint downhillCost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);
        FixedPoint flatCost = FixedPoint.One;

        Assert.True(downhillCost < flatCost,
            $"Downhill move should cost less than flat: {downhillCost.ToFloat()} vs {flatCost.ToFloat()}");
    }

    [Fact]
    public void GetMovementCost_SteepSlope_ReturnsMaxValue()
    {
        var grid = MakeGrid(TerrainType.Grass);
        // Set the destination cell to a slope angle exceeding infantry max (0.87)
        ref var cell = ref grid.GetCell(5, 4);
        cell.SlopeAngle = FixedPoint.FromFloat(1.0f); // > 0.87

        var profile = MovementProfile.Infantry();
        FixedPoint cost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);

        Assert.Equal(FixedPoint.MaxValue, cost);
    }

    [Fact]
    public void GetMovementCost_MinCostEnforced_NeverBelowMinimum()
    {
        // Even with great road + steep downhill, cost should be at least ~0.1
        var grid = MakeGrid(TerrainType.Road);
        grid.GetCell(4, 4).Height = FixedPoint.FromInt(10); // high source
        grid.GetCell(5, 4).Height = FixedPoint.Zero;          // low dest

        var profile = MovementProfile.Infantry();
        FixedPoint cost = TerrainCostCalculator.GetMovementCost(grid, profile, 4, 4, 5, 4);

        Assert.True(cost > FixedPoint.Zero, "Movement cost must always be positive");
    }

    [Fact]
    public void GetMovementCost_Deterministic_SameInputSameOutput()
    {
        var grid = MakeGrid(TerrainType.Mud);
        var profile = MovementProfile.LightVehicle();

        FixedPoint cost1 = TerrainCostCalculator.GetMovementCost(grid, profile, 3, 3, 4, 3);
        FixedPoint cost2 = TerrainCostCalculator.GetMovementCost(grid, profile, 3, 3, 4, 3);

        Assert.Equal(cost1.Raw, cost2.Raw);
    }
}
