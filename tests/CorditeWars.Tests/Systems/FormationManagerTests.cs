using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for FormationManager — deterministic formation slot computation.
/// Covers all formation types, auto-selection heuristic, edge cases, and
/// the mass-based sorting that puts heavy units at the front.
/// </summary>
public class FormationManagerTests
{
    private readonly FormationManager _manager = new();

    // ── Helpers ─────────────────────────────────────────────────────────

    private static FormationUnit MakeUnit(
        int id,
        FixedPoint? mass = null,
        FixedPoint? radius = null,
        bool isAir = false)
    {
        return new FormationUnit
        {
            UnitId = id,
            Mass   = mass   ?? FixedPoint.One,
            Radius = radius ?? FixedPoint.One,
            Speed  = FixedPoint.FromInt(5),
            IsAir  = isAir
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeFormation_EmptyList_ReturnsEmpty()
    {
        var result = _manager.ComputeFormation(
            new List<FormationUnit>(),
            FixedVector2.Zero,
            FixedVector2.Zero,
            FormationType.Line);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeFormation_SingleUnit_GoesDirectlyToDestination()
    {
        var destination = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(5));
        var units = new List<FormationUnit> { MakeUnit(1) };

        var result = _manager.ComputeFormation(
            units,
            destination,
            FixedVector2.Zero,
            FormationType.Line);

        Assert.Single(result);
        Assert.Equal(1, result[0].UnitId);
        Assert.Equal(destination, result[0].WorldTarget);
    }

    [Fact]
    public void ComputeFormation_None_AllUnitsGoToDestination()
    {
        var destination = new FixedVector2(FixedPoint.FromInt(20), FixedPoint.Zero);
        var units = new List<FormationUnit> { MakeUnit(1), MakeUnit(2), MakeUnit(3) };

        var result = _manager.ComputeFormation(
            units,
            destination,
            FixedVector2.Zero,
            FormationType.None);

        Assert.Equal(3, result.Count);
        foreach (var slot in result)
            Assert.Equal(destination, slot.WorldTarget);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Slot count correctness for every formation type
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FormationType.Line,   2)]
    [InlineData(FormationType.Column, 2)]
    [InlineData(FormationType.Wedge,  2)]
    [InlineData(FormationType.Box,    2)]
    [InlineData(FormationType.Spread, 2)]
    [InlineData(FormationType.Line,   5)]
    [InlineData(FormationType.Column, 5)]
    [InlineData(FormationType.Wedge,  5)]
    [InlineData(FormationType.Box,    5)]
    [InlineData(FormationType.Spread, 5)]
    [InlineData(FormationType.Line,   10)]
    [InlineData(FormationType.Column, 10)]
    [InlineData(FormationType.Wedge,  10)]
    [InlineData(FormationType.Box,    10)]
    [InlineData(FormationType.Spread, 10)]
    public void ComputeFormation_SlotCountMatchesUnitCount(FormationType type, int unitCount)
    {
        var units = new List<FormationUnit>();
        for (int i = 1; i <= unitCount; i++)
            units.Add(MakeUnit(i, mass: FixedPoint.FromInt(i)));

        var destination = new FixedVector2(FixedPoint.FromInt(50), FixedPoint.FromInt(50));
        var center      = FixedVector2.Zero;

        var result = _manager.ComputeFormation(units, destination, center, type);

        Assert.Equal(unitCount, result.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Every unit gets a unique slot (no two units share the same UnitId)
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FormationType.Line)]
    [InlineData(FormationType.Column)]
    [InlineData(FormationType.Wedge)]
    [InlineData(FormationType.Box)]
    [InlineData(FormationType.Spread)]
    public void ComputeFormation_EachUnitGetsUniqueSlot(FormationType type)
    {
        var units = new List<FormationUnit>();
        for (int i = 1; i <= 6; i++)
            units.Add(MakeUnit(i, mass: FixedPoint.FromInt(i)));

        var result = _manager.ComputeFormation(
            units,
            new FixedVector2(FixedPoint.FromInt(30), FixedPoint.FromInt(30)),
            FixedVector2.Zero,
            type);

        var ids = new HashSet<int>();
        foreach (var slot in result)
            Assert.True(ids.Add(slot.UnitId), $"Duplicate UnitId {slot.UnitId} in {type} formation");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Line formation: units spread laterally, not stacked
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LineFormation_UnitsSpreadLaterally()
    {
        var destination = new FixedVector2(FixedPoint.FromInt(100), FixedPoint.Zero);
        var center      = FixedVector2.Zero;
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.One),
            MakeUnit(2, mass: FixedPoint.One),
            MakeUnit(3, mass: FixedPoint.One)
        };

        var result = _manager.ComputeFormation(units, destination, center, FormationType.Line);

        // In a line formation moving east (+X), units spread along the Y axis.
        // Not all targets should be identical.
        var uniqueY = new HashSet<FixedPoint>();
        foreach (var slot in result)
            uniqueY.Add(slot.WorldTarget.Y);

        Assert.True(uniqueY.Count > 1,
            "Line formation should spread units on the lateral axis");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Column formation: units stacked along depth axis
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ColumnFormation_UnitsStackedInDepth()
    {
        var destination = new FixedVector2(FixedPoint.FromInt(100), FixedPoint.Zero);
        var center      = FixedVector2.Zero;
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.One),
            MakeUnit(2, mass: FixedPoint.One),
            MakeUnit(3, mass: FixedPoint.One)
        };

        var result = _manager.ComputeFormation(units, destination, center, FormationType.Column);

        // In a column formation moving east (+X), units stack along the X axis.
        var uniqueX = new HashSet<FixedPoint>();
        foreach (var slot in result)
            uniqueX.Add(slot.WorldTarget.X);

        Assert.True(uniqueX.Count > 1,
            "Column formation should spread units on the depth axis");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Determinism: same input → same output every call
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FormationType.Line)]
    [InlineData(FormationType.Column)]
    [InlineData(FormationType.Wedge)]
    [InlineData(FormationType.Box)]
    [InlineData(FormationType.Spread)]
    public void ComputeFormation_IsDeterministic(FormationType type)
    {
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.FromInt(5)),
            MakeUnit(2, mass: FixedPoint.FromInt(3)),
            MakeUnit(3, mass: FixedPoint.FromInt(1)),
        };
        var destination = new FixedVector2(FixedPoint.FromInt(40), FixedPoint.FromInt(20));
        var center      = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero);

        var resultA = _manager.ComputeFormation(units, destination, center, type);
        // Snapshot positions
        var positionsA = resultA.ConvertAll(s => s.WorldTarget);

        var resultB = _manager.ComputeFormation(units, destination, center, type);
        var positionsB = resultB.ConvertAll(s => s.WorldTarget);

        Assert.Equal(positionsA, positionsB);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Mass-based sorting: heaviest unit should be unit 1 in slot 0
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeFormation_HeaviestUnitGetsFrontSlot_WedgeFormation()
    {
        // Unit 3 has the highest mass and should occupy the tip (slot 0) in a wedge.
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.FromInt(1)),
            MakeUnit(2, mass: FixedPoint.FromInt(2)),
            MakeUnit(3, mass: FixedPoint.FromInt(10)),
        };
        var destination = new FixedVector2(FixedPoint.FromInt(50), FixedPoint.Zero);
        var center      = FixedVector2.Zero;

        var result = _manager.ComputeFormation(units, destination, center, FormationType.Wedge);

        Assert.Equal(3, result[0].UnitId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AutoSelectFormation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoSelectFormation_NullList_ReturnsNone()
    {
        Assert.Equal(FormationType.None, _manager.AutoSelectFormation(null!));
    }

    [Fact]
    public void AutoSelectFormation_EmptyList_ReturnsNone()
    {
        Assert.Equal(FormationType.None, _manager.AutoSelectFormation(new List<FormationUnit>()));
    }

    [Fact]
    public void AutoSelectFormation_SingleUnit_ReturnsNone()
    {
        var units = new List<FormationUnit> { MakeUnit(1) };
        Assert.Equal(FormationType.None, _manager.AutoSelectFormation(units));
    }

    [Fact]
    public void AutoSelectFormation_AllAir_ReturnsSpread()
    {
        var units = new List<FormationUnit>
        {
            MakeUnit(1, isAir: true),
            MakeUnit(2, isAir: true),
            MakeUnit(3, isAir: true)
        };
        Assert.Equal(FormationType.Spread, _manager.AutoSelectFormation(units));
    }

    [Fact]
    public void AutoSelectFormation_LargeGroup_ReturnsBox()
    {
        // More than 8 units → Box
        var units = new List<FormationUnit>();
        for (int i = 1; i <= 9; i++)
            units.Add(MakeUnit(i, mass: FixedPoint.FromInt(1)));

        Assert.Equal(FormationType.Box, _manager.AutoSelectFormation(units));
    }

    [Fact]
    public void AutoSelectFormation_MixedVehiclesAndInfantry_ReturnsWedge()
    {
        // Heavy vehicle (mass > 2) + infantry (mass <= 2) → Wedge
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.FromInt(10)), // vehicle
            MakeUnit(2, mass: FixedPoint.One),         // infantry
            MakeUnit(3, mass: FixedPoint.One),         // infantry
        };
        Assert.Equal(FormationType.Wedge, _manager.AutoSelectFormation(units));
    }

    [Fact]
    public void AutoSelectFormation_MostlyInfantry_ReturnsLine()
    {
        // >= 70% infantry (mass <= 2) and no vehicles → Line
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.One),
            MakeUnit(2, mass: FixedPoint.One),
            MakeUnit(3, mass: FixedPoint.One),
        };
        Assert.Equal(FormationType.Line, _manager.AutoSelectFormation(units));
    }

    [Fact]
    public void AutoSelectFormation_AllVehicles_ReturnsWedge()
    {
        // All vehicles (mass > 2), no infantry → default Wedge
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.FromInt(5)),
            MakeUnit(2, mass: FixedPoint.FromInt(5)),
            MakeUnit(3, mass: FixedPoint.FromInt(5)),
        };
        Assert.Equal(FormationType.Wedge, _manager.AutoSelectFormation(units));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Same-destination: all slots valid even when center == destination
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeFormation_CenterEqualsDestination_DefaultFacingEast()
    {
        // When delta == zero, facing defaults to +X. Formation should still
        // produce valid (non-NaN) FixedPoint slot positions.
        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.FromInt(2)),
            MakeUnit(2, mass: FixedPoint.One),
        };

        var result = _manager.ComputeFormation(units, pos, pos, FormationType.Line);

        Assert.Equal(2, result.Count);
        // Positions should be finite FixedPoint values (no overflow to MinValue/MaxValue)
        foreach (var slot in result)
        {
            Assert.True(slot.WorldTarget.X > FixedPoint.FromInt(-1000),
                "WorldTarget.X should be finite");
            Assert.True(slot.WorldTarget.X < FixedPoint.FromInt(1000),
                "WorldTarget.X should be finite");
        }
    }

    [Fact]
    public void ComputeFormation_UnrecognizedFormationType_FallsBackToDestination()
    {
        // Cast an out-of-enum value to trigger the default branch.
        var unknownType = (FormationType)999;
        var pos  = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        var units = new List<FormationUnit>
        {
            MakeUnit(1, mass: FixedPoint.One),
            MakeUnit(2, mass: FixedPoint.One),
        };

        var result = _manager.ComputeFormation(units, pos, pos, unknownType);

        // Fallback: all units go directly to destination.
        Assert.Equal(2, result.Count);
        Assert.All(result, slot => Assert.Equal(pos, slot.WorldTarget));
    }
}
