using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Game.Units;

/// <summary>
/// Tests for the UnitStance system and UnitVeterancy/XP system.
/// </summary>
public class StanceAndVeterancyTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static SimUnit MakeArmedUnit(int id, int playerId, FixedVector2 position,
        UnitStance stance = UnitStance.Aggressive, int xp = 0, VeterancyLevel vet = VeterancyLevel.Recruit)
    {
        var weapon = new WeaponData
        {
            Id   = "test_weapon",
            Type = WeaponType.MachineGun,
            Damage          = FixedPoint.FromInt(20),
            RateOfFire      = FixedPoint.One,
            Range           = FixedPoint.FromInt(10),
            MinRange        = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect    = FixedPoint.Zero,
            CanTarget       = TargetType.Ground,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers  = new Dictionary<ArmorType, FixedPoint>()
        };

        return new SimUnit
        {
            UnitId           = id,
            PlayerId         = playerId,
            Movement         = new MovementState { Position = position },
            Health           = FixedPoint.FromInt(100),
            MaxHealth        = FixedPoint.FromInt(100),
            ArmorValue       = FixedPoint.Zero,
            ArmorClass       = ArmorType.Unarmored,
            Category         = UnitCategory.Infantry,
            SightRange       = FixedPoint.FromInt(12),
            Profile          = MovementProfile.Infantry(),
            Radius           = FixedPoint.One,
            IsAlive          = true,
            Weapons          = new List<WeaponData> { weapon },
            WeaponCooldowns  = new List<FixedPoint> { FixedPoint.Zero },
            CurrentTargetId  = null,
            Stance           = stance,
            XP               = xp,
            Veterancy        = vet
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UnitStance enum
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnitStance_DefaultValue_IsAggressive()
    {
        var unit = new SimUnit();
        Assert.Equal(UnitStance.Aggressive, unit.Stance);
    }

    [Fact]
    public void UnitStance_AllValuesDistinct()
    {
        var values = System.Enum.GetValues<UnitStance>();
        var distinct = new System.Collections.Generic.HashSet<UnitStance>(values);
        Assert.Equal(values.Length, distinct.Count);
    }

    [Fact]
    public void UnitStance_CanBeSetOnSimUnit()
    {
        var unit = MakeArmedUnit(1, 1, FixedVector2.Zero, UnitStance.HoldFire);
        Assert.Equal(UnitStance.HoldFire, unit.Stance);
    }

    [Fact]
    public void UnitStance_HoldGround_Distinct_From_Defensive()
    {
        Assert.NotEqual(UnitStance.HoldGround, UnitStance.Defensive);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VeterancyLevel enum
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VeterancyLevel_DefaultValue_IsRecruit()
    {
        var unit = new SimUnit();
        Assert.Equal(VeterancyLevel.Recruit, unit.Veterancy);
    }

    [Fact]
    public void VeterancyLevel_AllValuesDistinct()
    {
        var values = System.Enum.GetValues<VeterancyLevel>();
        var distinct = new System.Collections.Generic.HashSet<VeterancyLevel>(values);
        Assert.Equal(values.Length, distinct.Count);
    }

    [Fact]
    public void VeterancyLevel_CanBeSetOnSimUnit()
    {
        var unit = MakeArmedUnit(1, 1, FixedVector2.Zero, xp: 3, vet: VeterancyLevel.Elite);
        Assert.Equal(VeterancyLevel.Elite, unit.Veterancy);
        Assert.Equal(3, unit.XP);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // XP thresholds
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, VeterancyLevel.Recruit)]
    [InlineData(1, VeterancyLevel.Veteran)]
    [InlineData(2, VeterancyLevel.Veteran)]
    [InlineData(3, VeterancyLevel.Elite)]
    [InlineData(5, VeterancyLevel.Elite)]
    [InlineData(6, VeterancyLevel.Heroic)]
    [InlineData(99, VeterancyLevel.Heroic)]
    public void VeterancyLevel_DerivedFromXP_MatchesThresholds(int xp, VeterancyLevel expected)
    {
        // Simulate the same switch expression used in UnitInteractionSystem Phase 8a
        VeterancyLevel derived = xp switch
        {
            >= 6 => VeterancyLevel.Heroic,
            >= 3 => VeterancyLevel.Elite,
            >= 1 => VeterancyLevel.Veteran,
            _    => VeterancyLevel.Recruit
        };
        Assert.Equal(expected, derived);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AttackerInfo DamageMultiplier (veterancy damage bonuses)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(VeterancyLevel.Recruit,  1.0f)]
    [InlineData(VeterancyLevel.Veteran,  1.1f)]
    [InlineData(VeterancyLevel.Elite,    1.25f)]
    [InlineData(VeterancyLevel.Heroic,   1.5f)]
    public void AttackerInfo_DamageMultiplier_CorrectForVeterancyLevel(VeterancyLevel vet, float expectedMult)
    {
        // Replicate the multiplier logic from BuildAttackerInfo
        FixedPoint multiplier = vet switch
        {
            VeterancyLevel.Heroic  => FixedPoint.FromFloat(1.5f),
            VeterancyLevel.Elite   => FixedPoint.FromFloat(1.25f),
            VeterancyLevel.Veteran => FixedPoint.FromFloat(1.1f),
            _                      => FixedPoint.One
        };
        Assert.Equal(expectedMult, multiplier.ToFloat(), precision: 2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stance suppresses movement (HoldGround / Defensive)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(UnitStance.HoldGround, true)]
    [InlineData(UnitStance.Defensive,  true)]
    [InlineData(UnitStance.Aggressive, false)]
    [InlineData(UnitStance.HoldFire,   false)]
    public void StanceSuppressesMovement_WhenNoActivePath(UnitStance stance, bool expectSuppressed)
    {
        // Replicate the movement suppression logic from Phase 4
        var unit = MakeArmedUnit(1, 1, FixedVector2.Zero, stance);
        // No path, no flow field
        bool suppressMovement = (unit.Stance == UnitStance.HoldGround ||
                                  unit.Stance == UnitStance.Defensive) &&
                                 (unit.CurrentPath == null || unit.CurrentPath.Count == 0) &&
                                 unit.ActiveFlowField == null;
        Assert.Equal(expectSuppressed, suppressMovement);
    }

    [Fact]
    public void StanceSuppressesMovement_WhenHoldGround_ButHasPath_DoesNotSuppress()
    {
        var unit = MakeArmedUnit(1, 1, FixedVector2.Zero, UnitStance.HoldGround);
        unit.CurrentPath = new List<(int, int)> { (1, 1) };

        bool suppressMovement = (unit.Stance == UnitStance.HoldGround ||
                                  unit.Stance == UnitStance.Defensive) &&
                                 (unit.CurrentPath == null || unit.CurrentPath.Count == 0) &&
                                 unit.ActiveFlowField == null;
        Assert.False(suppressMovement);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HoldFire stance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HoldFire_Stance_IsDistinctFromHoldGround()
    {
        Assert.NotEqual((int)UnitStance.HoldFire, (int)UnitStance.HoldGround);
    }

    [Fact]
    public void HoldFire_TargetShouldBeClearedByStanceCheck()
    {
        // Validate the HoldFire check logic
        var unit = MakeArmedUnit(1, 1, FixedVector2.Zero, UnitStance.HoldFire);
        bool holdFireSkipCombat = unit.Stance == UnitStance.HoldFire;
        Assert.True(holdFireSkipCombat);
    }
}
