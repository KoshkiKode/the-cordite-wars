using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for MovementProfile factory methods and With* overrides.
/// Validates that each preset has the expected domain, class, footprint,
/// and terrain rules. Also verifies that With* methods produce modified copies
/// without mutating the original.
/// </summary>
public class MovementProfileTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Infantry
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Infantry_HasGroundDomain()
    {
        Assert.Equal(MovementDomain.Ground, MovementProfile.Infantry().Domain);
    }

    [Fact]
    public void Infantry_HasInfantryClass()
    {
        Assert.Equal(MovementClass.Infantry, MovementProfile.Infantry().Class);
    }

    [Fact]
    public void Infantry_HasOneByOneFootprint()
    {
        var p = MovementProfile.Infantry();
        Assert.Equal(1, p.FootprintWidth);
        Assert.Equal(1, p.FootprintHeight);
    }

    [Fact]
    public void Infantry_CannotCrossWater()
    {
        var p = MovementProfile.Infantry();
        Assert.True(p.ImpassableTerrain.Contains(TerrainType.Water));
        Assert.True(p.ImpassableTerrain.Contains(TerrainType.DeepWater));
    }

    [Fact]
    public void Infantry_CannotCrossLava()
    {
        Assert.True(MovementProfile.Infantry().ImpassableTerrain.Contains(TerrainType.Lava));
    }

    [Fact]
    public void Infantry_HasPositiveMaxSpeed()
    {
        Assert.True(MovementProfile.Infantry().MaxSpeed > FixedPoint.Zero);
    }

    [Fact]
    public void Infantry_HasHighSlopeToleranceForGroundUnit()
    {
        // Infantry max slope ~0.87 rad (~50°) — highest among ground units
        var p = MovementProfile.Infantry();
        Assert.True(p.MaxSlopeAngle > FixedPoint.FromFloat(0.7f),
            $"Infantry should have high slope tolerance, got {p.MaxSlopeAngle.ToFloat()}");
    }

    [Fact]
    public void Infantry_RoadBonusModifier()
    {
        var p = MovementProfile.Infantry();
        Assert.True(p.TerrainSpeedModifiers.ContainsKey(TerrainType.Road));
        Assert.True(p.TerrainSpeedModifiers[TerrainType.Road] > FixedPoint.One,
            "Road should give infantry a speed bonus");
    }

    [Fact]
    public void Infantry_MudSlowsMovement()
    {
        var p = MovementProfile.Infantry();
        Assert.True(p.TerrainSpeedModifiers.ContainsKey(TerrainType.Mud));
        Assert.True(p.TerrainSpeedModifiers[TerrainType.Mud] < FixedPoint.One,
            "Mud should slow infantry");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LightVehicle
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LightVehicle_HasGroundDomain()
    {
        Assert.Equal(MovementDomain.Ground, MovementProfile.LightVehicle().Domain);
    }

    [Fact]
    public void LightVehicle_HasTwoByTwoFootprint()
    {
        var p = MovementProfile.LightVehicle();
        Assert.Equal(2, p.FootprintWidth);
        Assert.Equal(2, p.FootprintHeight);
    }

    [Fact]
    public void LightVehicle_FasterThanInfantry()
    {
        Assert.True(MovementProfile.LightVehicle().MaxSpeed > MovementProfile.Infantry().MaxSpeed);
    }

    [Fact]
    public void LightVehicle_RoadBonusHigherThanInfantry()
    {
        var lv = MovementProfile.LightVehicle();
        var inf = MovementProfile.Infantry();
        // Light vehicles benefit more from roads than infantry
        Assert.True(lv.TerrainSpeedModifiers[TerrainType.Road] > inf.TerrainSpeedModifiers[TerrainType.Road]);
    }

    [Fact]
    public void LightVehicle_MudModifierLowerThanInfantry()
    {
        var lv = MovementProfile.LightVehicle();
        var inf = MovementProfile.Infantry();
        // Vehicles are slowed more by mud than infantry
        Assert.True(lv.TerrainSpeedModifiers[TerrainType.Mud] < inf.TerrainSpeedModifiers[TerrainType.Mud]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HeavyVehicle
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HeavyVehicle_SlowerThanLightVehicle()
    {
        Assert.True(MovementProfile.HeavyVehicle().MaxSpeed < MovementProfile.LightVehicle().MaxSpeed);
    }

    [Fact]
    public void HeavyVehicle_HasThreeByThreeFootprint()
    {
        var p = MovementProfile.HeavyVehicle();
        Assert.Equal(3, p.FootprintWidth);
        Assert.Equal(3, p.FootprintHeight);
    }

    [Fact]
    public void HeavyVehicle_CannotCrossIce()
    {
        Assert.True(MovementProfile.HeavyVehicle().ImpassableTerrain.Contains(TerrainType.Ice));
    }

    [Fact]
    public void HeavyVehicle_LowerSlopeLimitThanInfantry()
    {
        Assert.True(MovementProfile.HeavyVehicle().MaxSlopeAngle < MovementProfile.Infantry().MaxSlopeAngle);
    }

    [Fact]
    public void HeavyVehicle_HeavierThanInfantry()
    {
        Assert.True(MovementProfile.HeavyVehicle().Mass > MovementProfile.Infantry().Mass);
    }

    [Fact]
    public void HeavyVehicle_HasCrushStrength()
    {
        Assert.True(MovementProfile.HeavyVehicle().CrushStrength > FixedPoint.Zero);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helicopter
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Helicopter_HasAirDomain()
    {
        Assert.Equal(MovementDomain.Air, MovementProfile.Helicopter().Domain);
    }

    [Fact]
    public void Helicopter_HasLowAirClass()
    {
        Assert.Equal(MovementClass.LowAir, MovementProfile.Helicopter().Class);
    }

    [Fact]
    public void Helicopter_OnlyVoidIsImpassable()
    {
        var p = MovementProfile.Helicopter();
        Assert.Single(p.ImpassableTerrain);
        Assert.True(p.ImpassableTerrain.Contains(TerrainType.Void));
    }

    [Fact]
    public void Helicopter_HasZeroGravityMultiplier()
    {
        Assert.Equal(FixedPoint.Zero, MovementProfile.Helicopter().GravityMultiplier);
    }

    [Fact]
    public void Helicopter_HasTwoByTwoFootprint()
    {
        var p = MovementProfile.Helicopter();
        Assert.Equal(2, p.FootprintWidth);
        Assert.Equal(2, p.FootprintHeight);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Jet
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Jet_HasAirDomain()
    {
        Assert.Equal(MovementDomain.Air, MovementProfile.Jet().Domain);
    }

    [Fact]
    public void Jet_FasterThanHelicopter()
    {
        Assert.True(MovementProfile.Jet().MaxSpeed > MovementProfile.Helicopter().MaxSpeed);
    }

    [Fact]
    public void Jet_HasOneByOneFootprint()
    {
        var p = MovementProfile.Jet();
        Assert.Equal(1, p.FootprintWidth);
        Assert.Equal(1, p.FootprintHeight);
    }

    [Fact]
    public void Jet_HasSlowerTurnRateThanHelicopter()
    {
        // Jets make wide banking turns
        Assert.True(MovementProfile.Jet().TurnRate < MovementProfile.Helicopter().TurnRate);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Naval
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Naval_HasWaterDomain()
    {
        Assert.Equal(MovementDomain.Water, MovementProfile.Naval().Domain);
    }

    [Fact]
    public void Naval_HasNavalClass()
    {
        Assert.Equal(MovementClass.Naval, MovementProfile.Naval().Class);
    }

    [Fact]
    public void Naval_AllLandTerrainIsImpassable()
    {
        var p = MovementProfile.Naval();
        // Ground terrain types must all be in the impassable set
        foreach (TerrainType landType in new[] { TerrainType.Grass, TerrainType.Dirt, TerrainType.Road, TerrainType.Mud })
        {
            Assert.True(p.ImpassableTerrain.Contains(landType),
                $"Naval unit must not be able to traverse {landType}");
        }
    }

    [Fact]
    public void Naval_WaterIsNotImpassable()
    {
        var p = MovementProfile.Naval();
        Assert.False(p.ImpassableTerrain.Contains(TerrainType.Water));
        Assert.False(p.ImpassableTerrain.Contains(TerrainType.DeepWater));
    }

    [Fact]
    public void Naval_HasZeroGravityMultiplier()
    {
        Assert.Equal(FixedPoint.Zero, MovementProfile.Naval().GravityMultiplier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Artillery
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Artillery_SlowestGroundUnit()
    {
        // Artillery should be slower than infantry
        Assert.True(MovementProfile.Artillery().MaxSpeed < MovementProfile.Infantry().MaxSpeed);
    }

    [Fact]
    public void Artillery_HasThreeByThreeFootprint()
    {
        var p = MovementProfile.Artillery();
        Assert.Equal(3, p.FootprintWidth);
        Assert.Equal(3, p.FootprintHeight);
    }

    [Fact]
    public void Artillery_HasVeryLowSlopeLimit()
    {
        // Artillery needs nearly flat terrain (~15°)
        Assert.True(MovementProfile.Artillery().MaxSlopeAngle < MovementProfile.Infantry().MaxSlopeAngle);
        Assert.True(MovementProfile.Artillery().MaxSlopeAngle < MovementProfile.LightVehicle().MaxSlopeAngle);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Building
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Building_HasZeroMaxSpeed()
    {
        Assert.Equal(FixedPoint.Zero, MovementProfile.Building().MaxSpeed);
    }

    [Fact]
    public void Building_DefaultFootprint_ThreeByThree()
    {
        var p = MovementProfile.Building();
        Assert.Equal(3, p.FootprintWidth);
        Assert.Equal(3, p.FootprintHeight);
    }

    [Fact]
    public void Building_CustomFootprint_SetsCorrectly()
    {
        var p = MovementProfile.Building(5, 4);
        Assert.Equal(5, p.FootprintWidth);
        Assert.Equal(4, p.FootprintHeight);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // With* override methods
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithSpeed_ChangesMaxSpeed_DoesNotMutateOriginal()
    {
        var original = MovementProfile.Infantry();
        var originalSpeed = original.MaxSpeed;

        var modified = original.WithSpeed(FixedPoint.FromFloat(0.99f));

        // Modified has new speed
        Assert.Equal(FixedPoint.FromFloat(0.99f), modified.MaxSpeed);
        // Original is unchanged
        Assert.Equal(originalSpeed, original.MaxSpeed);
    }

    [Fact]
    public void WithSpeed_PreservesOtherProperties()
    {
        var original = MovementProfile.LightVehicle();
        var modified = original.WithSpeed(FixedPoint.FromFloat(0.5f));

        Assert.Equal(original.Domain, modified.Domain);
        Assert.Equal(original.Class, modified.Class);
        Assert.Equal(original.FootprintWidth, modified.FootprintWidth);
        Assert.Equal(original.FootprintHeight, modified.FootprintHeight);
        Assert.Equal(original.TurnRate, modified.TurnRate);
        Assert.Equal(original.MaxSlopeAngle, modified.MaxSlopeAngle);
    }

    [Fact]
    public void WithTurnRate_ChangesTurnRate_DoesNotMutateOriginal()
    {
        var original = MovementProfile.Tank();
        var originalRate = original.TurnRate;
        var newRate = FixedPoint.FromFloat(0.25f);

        var modified = original.WithTurnRate(newRate);

        Assert.Equal(newRate, modified.TurnRate);
        Assert.Equal(originalRate, original.TurnRate);
    }

    [Fact]
    public void WithMass_ChangesMass_DoesNotMutateOriginal()
    {
        var original = MovementProfile.Infantry();
        var originalMass = original.Mass;
        var newMass = FixedPoint.FromFloat(99.0f);

        var modified = original.WithMass(newMass);

        Assert.Equal(newMass, modified.Mass);
        Assert.Equal(originalMass, original.Mass);
    }

    [Fact]
    public void WithSpeed_ChainedCalls_EachIndependent()
    {
        var base_ = MovementProfile.Infantry();
        var fast = base_.WithSpeed(FixedPoint.FromFloat(0.5f));
        var faster = base_.WithSpeed(FixedPoint.FromFloat(0.8f));

        // Each call off the original base produces independent values
        Assert.Equal(FixedPoint.FromFloat(0.5f), fast.MaxSpeed);
        Assert.Equal(FixedPoint.FromFloat(0.8f), faster.MaxSpeed);
        Assert.Equal(base_.MaxSpeed, MovementProfile.Infantry().MaxSpeed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cross-profile ordering guarantees
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpeedOrdering_Jet_Fastest()
    {
        // Jet is the fastest unit overall; Artillery the slowest ground unit
        Assert.True(MovementProfile.Jet().MaxSpeed > MovementProfile.Infantry().MaxSpeed);
        Assert.True(MovementProfile.LightVehicle().MaxSpeed > MovementProfile.Infantry().MaxSpeed);
        Assert.True(MovementProfile.Infantry().MaxSpeed > MovementProfile.HeavyVehicle().MaxSpeed);
        Assert.True(MovementProfile.HeavyVehicle().MaxSpeed > MovementProfile.Artillery().MaxSpeed);
        // Jet is faster than helicopters
        Assert.True(MovementProfile.Jet().MaxSpeed > MovementProfile.Helicopter().MaxSpeed);
    }

    [Fact]
    public void SlopeLimit_Infantry_HighestAmongGroundUnits()
    {
        Assert.True(MovementProfile.Infantry().MaxSlopeAngle > MovementProfile.LightVehicle().MaxSlopeAngle);
        Assert.True(MovementProfile.LightVehicle().MaxSlopeAngle > MovementProfile.HeavyVehicle().MaxSlopeAngle);
        Assert.True(MovementProfile.HeavyVehicle().MaxSlopeAngle > MovementProfile.Artillery().MaxSlopeAngle);
    }
}
