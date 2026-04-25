using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Game.Units;

/// <summary>
/// Tests for UnitData.GetMovementProfile — dynamic movement profile generation
/// based on MovementClassId and per-unit stat overrides.
/// </summary>
public class UnitDataMovementProfileTests
{
    private static UnitData CreateUnitData(
        string movementClassId = "Infantry",
        FixedPoint? speedOverride = null,
        FixedPoint? turnRateOverride = null,
        FixedPoint? massOverride = null)
    {
        return new UnitData
        {
            Id = "test_unit",
            DisplayName = "Test Unit",
            FactionId = "bastion",
            Category = UnitCategory.Infantry,
            MovementClassId = movementClassId,
            MaxHealth = FixedPoint.FromInt(100),
            Cost = 100,
            SpeedOverride = speedOverride,
            TurnRateOverride = turnRateOverride,
            MassOverride = massOverride
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // MovementClassId → Base Profile
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Infantry")]
    [InlineData("LightVehicle")]
    [InlineData("HeavyVehicle")]
    [InlineData("APC")]
    [InlineData("Tank")]
    [InlineData("Artillery")]
    [InlineData("Helicopter")]
    [InlineData("Jet")]
    [InlineData("Naval")]
    public void GetMovementProfile_AllValidClasses_Succeed(string classId)
    {
        var unit = CreateUnitData(movementClassId: classId);
        var profile = unit.GetMovementProfile();
        Assert.NotNull(profile);
    }

    [Fact]
    public void GetMovementProfile_UnknownClassId_ThrowsArgumentException()
    {
        var unit = CreateUnitData(movementClassId: "InvalidClass");
        Assert.Throws<ArgumentException>(() => unit.GetMovementProfile());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Speed Override
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementProfile_SpeedOverride_AppliesCustomSpeed()
    {
        FixedPoint customSpeed = FixedPoint.FromInt(99);
        var unit = CreateUnitData(speedOverride: customSpeed);
        var profile = unit.GetMovementProfile();

        Assert.Equal(customSpeed, profile.MaxSpeed);
    }

    [Fact]
    public void GetMovementProfile_NoSpeedOverride_UsesBaseSpeed()
    {
        var unit = CreateUnitData(movementClassId: "Infantry");
        var baseProfile = MovementProfile.Infantry();
        var profile = unit.GetMovementProfile();

        Assert.Equal(baseProfile.MaxSpeed, profile.MaxSpeed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Turn Rate Override
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementProfile_TurnRateOverride_AppliesCustomTurnRate()
    {
        FixedPoint customTurnRate = FixedPoint.FromInt(50);
        var unit = CreateUnitData(turnRateOverride: customTurnRate);
        var profile = unit.GetMovementProfile();

        Assert.Equal(customTurnRate, profile.TurnRate);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Mass Override
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementProfile_MassOverride_AppliesCustomMass()
    {
        FixedPoint customMass = FixedPoint.FromInt(25);
        var unit = CreateUnitData(massOverride: customMass);
        var profile = unit.GetMovementProfile();

        Assert.Equal(customMass, profile.Mass);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Multiple Overrides
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementProfile_AllOverrides_AppliesAll()
    {
        FixedPoint speed = FixedPoint.FromInt(42);
        FixedPoint turn = FixedPoint.FromInt(15);
        FixedPoint mass = FixedPoint.FromInt(88);

        var unit = CreateUnitData(
            speedOverride: speed,
            turnRateOverride: turn,
            massOverride: mass);

        var profile = unit.GetMovementProfile();

        Assert.Equal(speed, profile.MaxSpeed);
        Assert.Equal(turn, profile.TurnRate);
        Assert.Equal(mass, profile.Mass);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Different Base Classes Produce Different Profiles
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMovementProfile_InfantryVsTank_DifferentSpeeds()
    {
        var infantry = CreateUnitData(movementClassId: "Infantry");
        var tank = CreateUnitData(movementClassId: "Tank");

        var infProfile = infantry.GetMovementProfile();
        var tankProfile = tank.GetMovementProfile();

        // Infantry and tank should have different speeds
        Assert.NotEqual(infProfile.MaxSpeed, tankProfile.MaxSpeed);
    }

    [Fact]
    public void GetMovementProfile_Helicopter_IsAirDomain()
    {
        var unit = CreateUnitData(movementClassId: "Helicopter");
        var profile = unit.GetMovementProfile();

        Assert.Equal(MovementDomain.Air, profile.Domain);
    }

    [Fact]
    public void GetMovementProfile_Jet_IsAirDomain()
    {
        var unit = CreateUnitData(movementClassId: "Jet");
        var profile = unit.GetMovementProfile();

        Assert.Equal(MovementDomain.Air, profile.Domain);
    }

    [Fact]
    public void GetMovementProfile_Infantry_IsGroundDomain()
    {
        var unit = CreateUnitData(movementClassId: "Infantry");
        var profile = unit.GetMovementProfile();

        Assert.Equal(MovementDomain.Ground, profile.Domain);
        Assert.NotEqual(MovementDomain.Air, profile.Domain);
    }

    [Fact]
    public void GetMovementProfile_Naval_IsWaterDomain()
    {
        var unit = CreateUnitData(movementClassId: "Naval");
        var profile = unit.GetMovementProfile();

        Assert.Equal(MovementDomain.Water, profile.Domain);
        Assert.NotEqual(MovementDomain.Ground, profile.Domain);
    }
}

/// <summary>
/// Tests for UnitData default values and property initialization.
/// </summary>
public class UnitDataDefaultsTests
{
    [Fact]
    public void UnitData_DefaultValues()
    {
        var data = new UnitData();

        Assert.Equal(string.Empty, data.Id);
        Assert.Equal(string.Empty, data.DisplayName);
        Assert.Equal(string.Empty, data.FactionId);
        Assert.Equal(UnitCategory.Infantry, data.Category);
        Assert.Equal(string.Empty, data.MovementClassId);
        Assert.Equal(1, data.FootprintWidth);
        Assert.Equal(1, data.FootprintHeight);
        Assert.False(data.CanGarrison);
        Assert.False(data.CanCrush);
        Assert.False(data.IsStealthed);
        Assert.False(data.IsDetector);
        Assert.Null(data.SpecialAbilityId);
        Assert.NotNull(data.Weapons);
        Assert.Empty(data.Weapons);
    }

    [Fact]
    public void UnitCategory_HasExpectedValues()
    {
        // Verify all expected categories exist
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Infantry));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Tank));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Helicopter));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Jet));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Artillery));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.PatrolBoat));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Destroyer));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Submarine));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.CapitalShip));
        Assert.True(Enum.IsDefined(typeof(UnitCategory), UnitCategory.Defense));
    }

    [Fact]
    public void ArmorType_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Unarmored));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Light));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Medium));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Heavy));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Aircraft));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Building));
        Assert.True(Enum.IsDefined(typeof(ArmorType), ArmorType.Naval));
    }

    [Fact]
    public void WeaponType_HasExpectedValues()
    {
        // Verify torpedo and chemical spray (newer additions)
        Assert.True(Enum.IsDefined(typeof(WeaponType), WeaponType.Torpedo));
        Assert.True(Enum.IsDefined(typeof(WeaponType), WeaponType.ChemicalSpray));
    }

    [Fact]
    public void TargetType_FlagsWorkCorrectly()
    {
        var combined = TargetType.Ground | TargetType.Air;
        Assert.True(combined.HasFlag(TargetType.Ground));
        Assert.True(combined.HasFlag(TargetType.Air));
        Assert.False(combined.HasFlag(TargetType.Building));
        Assert.False(combined.HasFlag(TargetType.Naval));
    }

    [Fact]
    public void TargetType_NavalFlag_Independent()
    {
        var naval = TargetType.Naval;
        Assert.True(naval.HasFlag(TargetType.Naval));
        Assert.False(naval.HasFlag(TargetType.Ground));
        Assert.False(naval.HasFlag(TargetType.Air));
    }

    [Fact]
    public void TargetType_AllFlags_CanBeCombined()
    {
        var all = TargetType.Ground | TargetType.Air | TargetType.Building | TargetType.Naval;
        Assert.True(all.HasFlag(TargetType.Ground));
        Assert.True(all.HasFlag(TargetType.Air));
        Assert.True(all.HasFlag(TargetType.Building));
        Assert.True(all.HasFlag(TargetType.Naval));
    }

    [Fact]
    public void WeaponData_DefaultValues()
    {
        var weapon = new WeaponData();
        Assert.Equal(string.Empty, weapon.Id);
        Assert.Equal(WeaponType.None, weapon.Type);
        Assert.Equal(TargetType.Ground, weapon.CanTarget);
        Assert.Equal(FixedPoint.FromInt(100), weapon.AccuracyPercent);
        Assert.NotNull(weapon.ArmorModifiers);
    }
}
