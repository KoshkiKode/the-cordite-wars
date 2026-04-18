using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Game.Units;

/// <summary>
/// Tests for <see cref="UnitData"/>, <see cref="BuildingData"/>, and
/// <see cref="WeaponData"/>, including the <c>GetMovementProfile()</c>
/// factory on <see cref="UnitData"/> and all override paths.
/// All tests are Godot-free.
/// </summary>
public class UnitDataAndBuildingDataTests
{
    // ══════════════════════════════════════════════════════════════════
    // UnitData – defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void UnitData_Defaults_AreExpected()
    {
        var data = new UnitData();

        Assert.Equal(string.Empty, data.Id);
        Assert.Equal(string.Empty, data.DisplayName);
        Assert.Equal(string.Empty, data.FactionId);
        Assert.Equal(default(UnitCategory), data.Category);
        Assert.Equal(string.Empty, data.MovementClassId);
        Assert.Equal(default(FixedPoint), data.MaxHealth);
        Assert.Equal(default(FixedPoint), data.ArmorValue);
        Assert.Equal(default(ArmorType), data.ArmorClass);
        Assert.Equal(default(FixedPoint), data.SightRange);
        Assert.Equal(default(FixedPoint), data.BuildTime);
        Assert.Equal(0, data.Cost);
        Assert.Equal(0, data.SecondaryCost);
        Assert.Equal(0, data.PopulationCost);
        Assert.Empty(data.Weapons);
        Assert.Null(data.SpecialAbilityId);
        Assert.Equal(string.Empty, data.Description);
        Assert.Null(data.SpeedOverride);
        Assert.Null(data.TurnRateOverride);
        Assert.Null(data.MassOverride);
        Assert.Equal(1, data.FootprintWidth);
        Assert.Equal(1, data.FootprintHeight);
        Assert.False(data.CanGarrison);
        Assert.False(data.CanCrush);
        Assert.False(data.IsStealthed);
        Assert.False(data.IsDetector);
    }

    [Fact]
    public void UnitData_AssignedValues_ArePreserved()
    {
        var weapon = new WeaponData
        {
            Id = "cannon",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(50),
            Range = FixedPoint.FromInt(6),
            CanTarget = TargetType.Ground | TargetType.Building
        };

        var data = new UnitData
        {
            Id = "arcloft_tank",
            DisplayName = "Arcloft Battle Tank",
            FactionId = "arcloft",
            Category = UnitCategory.Tank,
            MovementClassId = "Tank",
            MaxHealth = FixedPoint.FromInt(800),
            ArmorValue = FixedPoint.FromInt(10),
            ArmorClass = ArmorType.Heavy,
            SightRange = FixedPoint.FromInt(7),
            BuildTime = FixedPoint.FromFloat(12.5f),
            Cost = 900,
            SecondaryCost = 50,
            PopulationCost = 3,
            Weapons = [weapon],
            SpecialAbilityId = "siege_mode",
            Description = "Heavy tank.",
            SpeedOverride = FixedPoint.FromFloat(2.5f),
            TurnRateOverride = FixedPoint.FromFloat(60f),
            MassOverride = FixedPoint.FromFloat(15f),
            FootprintWidth = 2,
            FootprintHeight = 2,
            CanGarrison = false,
            CanCrush = true,
            IsStealthed = false,
            IsDetector = false
        };

        Assert.Equal("arcloft_tank", data.Id);
        Assert.Equal(UnitCategory.Tank, data.Category);
        Assert.Equal("Tank", data.MovementClassId);
        Assert.Equal(800, data.MaxHealth.ToInt());
        Assert.Equal(900, data.Cost);
        Assert.Single(data.Weapons);
        Assert.Equal("cannon", data.Weapons[0].Id);
        Assert.Equal(WeaponType.Cannon, data.Weapons[0].Type);
        Assert.True(data.CanCrush);
        Assert.NotNull(data.SpeedOverride);
        Assert.Equal(2f, data.FootprintWidth);
    }

    // ══════════════════════════════════════════════════════════════════
    // UnitData.GetMovementProfile() – all base movement classes
    // ══════════════════════════════════════════════════════════════════

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
    public void GetMovementProfile_KnownClass_ReturnsProfile(string classId)
    {
        var unit = new UnitData { Id = "test", MovementClassId = classId };
        MovementProfile profile = unit.GetMovementProfile();
        Assert.NotNull(profile);
        // All profiles have a positive max speed
        Assert.True(profile.MaxSpeed > FixedPoint.Zero,
            $"Expected MaxSpeed > 0 for class '{classId}'");
    }

    [Fact]
    public void GetMovementProfile_UnknownClass_ThrowsArgumentException()
    {
        var unit = new UnitData { Id = "test", MovementClassId = "Hover" };
        Assert.Throws<ArgumentException>(() => unit.GetMovementProfile());
    }

    [Fact]
    public void GetMovementProfile_SpeedOverride_ChangesSpeed()
    {
        FixedPoint overrideSpeed = FixedPoint.FromFloat(1.5f);
        var unit = new UnitData
        {
            Id = "test",
            MovementClassId = "Infantry",
            SpeedOverride = overrideSpeed
        };

        MovementProfile profile = unit.GetMovementProfile();
        Assert.Equal(overrideSpeed, profile.MaxSpeed);
    }

    [Fact]
    public void GetMovementProfile_TurnRateOverride_ChangesTurnRate()
    {
        FixedPoint overrideTurn = FixedPoint.FromFloat(90f);
        var unit = new UnitData
        {
            Id = "test",
            MovementClassId = "Tank",
            TurnRateOverride = overrideTurn
        };

        MovementProfile profile = unit.GetMovementProfile();
        Assert.Equal(overrideTurn, profile.TurnRate);
    }

    [Fact]
    public void GetMovementProfile_MassOverride_ChangesMass()
    {
        FixedPoint overrideMass = FixedPoint.FromFloat(25f);
        var unit = new UnitData
        {
            Id = "test",
            MovementClassId = "HeavyVehicle",
            MassOverride = overrideMass
        };

        MovementProfile profile = unit.GetMovementProfile();
        Assert.Equal(overrideMass, profile.Mass);
    }

    [Fact]
    public void GetMovementProfile_NoOverrides_DoesNotChangeBaseProfile()
    {
        var unitNoOverride = new UnitData { Id = "a", MovementClassId = "Infantry" };
        var unitWithOverride = new UnitData
        {
            Id = "b",
            MovementClassId = "Infantry",
            SpeedOverride = FixedPoint.FromFloat(999f)
        };

        MovementProfile baseProfile = unitNoOverride.GetMovementProfile();
        MovementProfile overriddenProfile = unitWithOverride.GetMovementProfile();

        Assert.NotEqual(baseProfile.MaxSpeed, overriddenProfile.MaxSpeed);
    }

    // ══════════════════════════════════════════════════════════════════
    // WeaponData – defaults and TargetType flags
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void WeaponData_Defaults_AreExpected()
    {
        var weapon = new WeaponData();

        Assert.Equal(string.Empty, weapon.Id);
        Assert.Equal(WeaponType.None, weapon.Type);
        Assert.Equal(default(FixedPoint), weapon.Damage);
        Assert.Equal(default(FixedPoint), weapon.RateOfFire);
        Assert.Equal(default(FixedPoint), weapon.Range);
        Assert.Equal(default(FixedPoint), weapon.MinRange);
        Assert.Equal(default(FixedPoint), weapon.ProjectileSpeed);
        Assert.Equal(default(FixedPoint), weapon.AreaOfEffect);
        Assert.Equal(TargetType.Ground, weapon.CanTarget);
        Assert.Equal(FixedPoint.FromInt(100), weapon.AccuracyPercent);
        Assert.Empty(weapon.ArmorModifiers);
    }

    [Fact]
    public void TargetType_FlagsComposition_WorksCorrectly()
    {
        TargetType antiAll = TargetType.Ground | TargetType.Air | TargetType.Building | TargetType.Naval;
        Assert.True((antiAll & TargetType.Ground) != 0);
        Assert.True((antiAll & TargetType.Air) != 0);
        Assert.True((antiAll & TargetType.Building) != 0);
        Assert.True((antiAll & TargetType.Naval) != 0);
    }

    [Fact]
    public void WeaponData_ArmorModifiers_CanBePopulated()
    {
        var weapon = new WeaponData
        {
            Id = "cannon",
            ArmorModifiers = new Dictionary<ArmorType, FixedPoint>
            {
                { ArmorType.Light, FixedPoint.FromFloat(1.5f) },
                { ArmorType.Heavy, FixedPoint.FromFloat(0.5f) }
            }
        };

        Assert.Equal(2, weapon.ArmorModifiers.Count);
        Assert.InRange(weapon.ArmorModifiers[ArmorType.Light].ToFloat(), 1.4f, 1.6f);
        Assert.InRange(weapon.ArmorModifiers[ArmorType.Heavy].ToFloat(), 0.4f, 0.6f);
    }

    // ══════════════════════════════════════════════════════════════════
    // BuildingData – defaults and full field coverage
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildingData_Defaults_AreExpected()
    {
        var data = new BuildingData();

        Assert.Equal(string.Empty, data.Id);
        Assert.Equal(string.Empty, data.DisplayName);
        Assert.Equal(string.Empty, data.FactionId);
        Assert.Equal(default(BuildingCategory), data.Category);
        Assert.Equal(default(FixedPoint), data.MaxHealth);
        Assert.Equal(ArmorType.Building, data.ArmorClass);
        Assert.Equal(default(FixedPoint), data.ArmorValue);
        Assert.Equal(0, data.Cost);
        Assert.Equal(0, data.SecondaryCost);
        Assert.Equal(default(FixedPoint), data.BuildTime);
        Assert.Equal(3, data.FootprintWidth);
        Assert.Equal(3, data.FootprintHeight);
        Assert.Null(data.Weapons);
        Assert.Equal(default(FixedPoint), data.SightRange);
        Assert.Equal(default(FixedPoint), data.PassiveIncome);
        Assert.Equal(default(FixedPoint), data.VCGeneration);
        Assert.Equal(0, data.SupplyProvided);
        Assert.Equal(default(FixedPoint), data.PowerGenerated);
        Assert.Empty(data.UnlocksUnitIds);
        Assert.Empty(data.UnlocksBuildingIds);
        Assert.Empty(data.Prerequisites);
        Assert.False(data.ProvidesRadar);
        Assert.False(data.RequiresWaterAccess);
        Assert.Equal(0, data.GarrisonCapacity);
        Assert.Equal(50, data.GarrisonDefenseBonus);
        Assert.Equal(string.Empty, data.Description);
    }

    [Fact]
    public void BuildingData_AssignedValues_ArePreserved()
    {
        var data = new BuildingData
        {
            Id = "bastion_barracks",
            DisplayName = "Bastion Barracks",
            FactionId = "bastion",
            Category = BuildingCategory.Production,
            MaxHealth = FixedPoint.FromInt(1500),
            ArmorClass = ArmorType.Medium,
            ArmorValue = FixedPoint.FromInt(5),
            Cost = 400,
            SecondaryCost = 0,
            BuildTime = FixedPoint.FromFloat(25.0f),
            FootprintWidth = 3,
            FootprintHeight = 3,
            Weapons = [new WeaponData { Id = "auto_turret", Type = WeaponType.MachineGun }],
            SightRange = FixedPoint.FromInt(6),
            PassiveIncome = FixedPoint.FromFloat(0f),
            VCGeneration = FixedPoint.FromFloat(0f),
            SupplyProvided = 10,
            PowerGenerated = FixedPoint.FromInt(-5),
            UnlocksUnitIds = ["bastion_rifleman", "bastion_medic"],
            UnlocksBuildingIds = ["bastion_armory"],
            Prerequisites = ["bastion_hq"],
            ProvidesRadar = false,
            RequiresWaterAccess = false,
            GarrisonCapacity = 5,
            GarrisonDefenseBonus = 40,
            Description = "Produces infantry units."
        };

        Assert.Equal("bastion_barracks", data.Id);
        Assert.Equal(BuildingCategory.Production, data.Category);
        Assert.Equal(1500, data.MaxHealth.ToInt());
        Assert.Equal(400, data.Cost);
        Assert.Single(data.Weapons);
        Assert.Equal("auto_turret", data.Weapons![0].Id);
        Assert.Equal(10, data.SupplyProvided);
        Assert.Equal(2, data.UnlocksUnitIds.Count);
        Assert.Equal(40, data.GarrisonDefenseBonus);
        Assert.Single(data.Prerequisites);
        Assert.Equal("bastion_hq", data.Prerequisites[0]);
    }

    [Fact]
    public void BuildingCategory_AllValues_CanBeCast()
    {
        foreach (BuildingCategory cat in Enum.GetValues<BuildingCategory>())
        {
            Assert.True(Enum.IsDefined(cat), $"{cat} should be defined");
        }
    }
}
