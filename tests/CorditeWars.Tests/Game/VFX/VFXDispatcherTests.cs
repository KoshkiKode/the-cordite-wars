using System.Linq;
using CorditeWars.Game.Units;
using CorditeWars.Game.VFX;

namespace CorditeWars.Tests.Game.VFX;

/// <summary>
/// Tests for <see cref="VFXDispatcher"/> — the pure-C# routing layer that
/// maps simulation combat events to lists of <see cref="VFXRequest"/> values.
///
/// These tests are deliberately Godot-free and exercise every branch of the
/// three public dispatch methods.
/// </summary>
public class VFXDispatcherTests
{
    // ═══════════════════════════════════════════════════════════════════
    // VFXRequest record helpers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void VFXRequest_At_HasZeroOffset()
    {
        var req = VFXRequest.At(VFXEffectType.Spark);
        Assert.Equal(VFXEffectType.Spark, req.Effect);
        Assert.Equal(0f, req.OffsetX);
        Assert.Equal(0f, req.OffsetY);
        Assert.Equal(0f, req.OffsetZ);
    }

    [Fact]
    public void VFXRequest_ExplicitOffset_StoresCorrectly()
    {
        var req = new VFXRequest(VFXEffectType.ExplosionLarge, 2f, -1f, 0.5f);
        Assert.Equal(2f,   req.OffsetX);
        Assert.Equal(-1f,  req.OffsetY);
        Assert.Equal(0.5f, req.OffsetZ);
    }

    [Fact]
    public void VFXRequest_RecordEquality_Works()
    {
        var a = VFXRequest.At(VFXEffectType.SmokePuff);
        var b = VFXRequest.At(VFXEffectType.SmokePuff);
        Assert.Equal(a, b);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetAttackFiredEffects — weapon-type routing
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(WeaponType.MachineGun)]
    [InlineData(WeaponType.Cannon)]
    [InlineData(WeaponType.Laser)]
    [InlineData(WeaponType.Flak)]
    [InlineData(WeaponType.Bomb)]
    [InlineData(WeaponType.Mortar)]
    [InlineData(WeaponType.Sniper)]
    [InlineData(WeaponType.EMP)]
    [InlineData(WeaponType.GatlingGun)]
    [InlineData(WeaponType.None)]
    public void GetAttackFiredEffects_AnyWeapon_AlwaysContainsMuzzleFlash(WeaponType type)
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(type);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.MuzzleFlash);
    }

    [Theory]
    [InlineData(WeaponType.Missile)]
    [InlineData(WeaponType.Rockets)]
    [InlineData(WeaponType.SAM)]
    [InlineData(WeaponType.Torpedo)]
    public void GetAttackFiredEffects_ProjectileWeapons_AddThrusterTrail(WeaponType type)
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(type);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ThrusterTrail);
    }

    [Theory]
    [InlineData(WeaponType.MachineGun)]
    [InlineData(WeaponType.Cannon)]
    [InlineData(WeaponType.Laser)]
    [InlineData(WeaponType.EMP)]
    public void GetAttackFiredEffects_NonProjectileWeapons_DoNotAddThrusterTrail(WeaponType type)
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(type);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.ThrusterTrail);
    }

    [Theory]
    [InlineData(WeaponType.Flamethrower)]
    [InlineData(WeaponType.ChemicalSpray)]
    public void GetAttackFiredEffects_AreaDenialWeapons_AddSmokePuff(WeaponType type)
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(type);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Theory]
    [InlineData(WeaponType.Cannon)]
    [InlineData(WeaponType.Missile)]
    [InlineData(WeaponType.Laser)]
    public void GetAttackFiredEffects_NonAreaDenialWeapons_DoNotAddSmokePuff(WeaponType type)
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(type);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Fact]
    public void GetAttackFiredEffects_Missile_HasMuzzleFlashAndThrusterTrail_NoSmoke()
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(WeaponType.Missile);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.MuzzleFlash);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ThrusterTrail);
    }

    [Fact]
    public void GetAttackFiredEffects_Flamethrower_HasMuzzleFlashAndSmokePuff_NoTrail()
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(WeaponType.Flamethrower);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.MuzzleFlash);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.ThrusterTrail);
    }

    [Fact]
    public void GetAttackFiredEffects_Cannon_ExactlyOneMuzzleFlash()
    {
        var effects = VFXDispatcher.GetAttackFiredEffects(WeaponType.Cannon);
        Assert.Single(effects);
        Assert.Equal(VFXEffectType.MuzzleFlash, effects[0].Effect);
    }

    [Fact]
    public void GetAttackFiredEffects_AllEffectsHaveZeroOffset()
    {
        foreach (WeaponType type in Enum.GetValues<WeaponType>())
        {
            var effects = VFXDispatcher.GetAttackFiredEffects(type);
            foreach (var req in effects)
            {
                Assert.Equal(0f, req.OffsetX);
                Assert.Equal(0f, req.OffsetY);
                Assert.Equal(0f, req.OffsetZ);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetAttackImpactEffects — hit / miss / AoE routing
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetAttackImpactEffects_CleanMiss_ReturnsEmpty()
    {
        var effects = VFXDispatcher.GetAttackImpactEffects(isHit: false, hasAoe: false);
        Assert.Empty(effects);
    }

    [Fact]
    public void GetAttackImpactEffects_DirectHit_NoAoe_ReturnsSpark()
    {
        var effects = VFXDispatcher.GetAttackImpactEffects(isHit: true, hasAoe: false);
        Assert.Single(effects);
        Assert.Equal(VFXEffectType.Spark, effects[0].Effect);
    }

    [Fact]
    public void GetAttackImpactEffects_AoeHit_ReturnsExplosionAndSmoke()
    {
        var effects = VFXDispatcher.GetAttackImpactEffects(isHit: true, hasAoe: true);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ExplosionMedium);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Fact]
    public void GetAttackImpactEffects_AoeMiss_StillShowsExplosion()
    {
        // Splash weapon misses point-target but still detonates near it
        var effects = VFXDispatcher.GetAttackImpactEffects(isHit: false, hasAoe: true);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ExplosionMedium);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Fact]
    public void GetAttackImpactEffects_AllImpactEffectsHaveZeroOffset()
    {
        var cases = new (bool hit, bool aoe)[]
        {
            (true, false), (true, true), (false, true), (false, false)
        };

        foreach (var (hit, aoe) in cases)
        {
            var effects = VFXDispatcher.GetAttackImpactEffects(hit, aoe);
            foreach (var req in effects)
            {
                Assert.Equal(0f, req.OffsetX);
                Assert.Equal(0f, req.OffsetY);
                Assert.Equal(0f, req.OffsetZ);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetUnitDeathEffects — unit-category routing
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(UnitCategory.Infantry)]
    [InlineData(UnitCategory.Special)]
    public void GetUnitDeathEffects_SoftTargets_SmallExplosionAndDustCloud(UnitCategory cat)
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(cat);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ExplosionSmall);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.DustCloud);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Theory]
    [InlineData(UnitCategory.LightVehicle)]
    [InlineData(UnitCategory.APC)]
    [InlineData(UnitCategory.Support)]
    [InlineData(UnitCategory.Defense)]
    [InlineData(UnitCategory.PatrolBoat)]
    public void GetUnitDeathEffects_LightArmour_MediumExplosionAndSmoke(UnitCategory cat)
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(cat);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ExplosionMedium);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
    }

    [Theory]
    [InlineData(UnitCategory.HeavyVehicle)]
    [InlineData(UnitCategory.Tank)]
    [InlineData(UnitCategory.Artillery)]
    [InlineData(UnitCategory.Helicopter)]
    [InlineData(UnitCategory.Jet)]
    [InlineData(UnitCategory.Destroyer)]
    [InlineData(UnitCategory.Submarine)]
    public void GetUnitDeathEffects_HeavyUnits_LargeExplosionSmokeAndSparks(UnitCategory cat)
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(cat);
        Assert.Equal(3, effects.Count);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.ExplosionLarge);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.Spark);
    }

    [Fact]
    public void GetUnitDeathEffects_CapitalShip_FiveEffectsWithSpatialOffsets()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.CapitalShip);

        Assert.Equal(5, effects.Count);

        // Three explosion effects
        Assert.Equal(3, effects.Count(r =>
            r.Effect is VFXEffectType.ExplosionLarge or VFXEffectType.ExplosionMedium));

        // Smoke and water splash
        Assert.Contains(effects, r => r.Effect == VFXEffectType.SmokePuff);
        Assert.Contains(effects, r => r.Effect == VFXEffectType.WaterSplash);
    }

    [Fact]
    public void GetUnitDeathEffects_CapitalShip_SecondExplosionHasXOffset()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.CapitalShip);

        // One of the ExplosionLarge requests should have a non-zero X offset
        var offsetExplosions = effects
            .Where(r => r.Effect == VFXEffectType.ExplosionLarge && r.OffsetX != 0f)
            .ToList();

        Assert.Single(offsetExplosions);
        Assert.Equal(2f, offsetExplosions[0].OffsetX);
        Assert.Equal(0f, offsetExplosions[0].OffsetY);
        Assert.Equal(0f, offsetExplosions[0].OffsetZ);
    }

    [Fact]
    public void GetUnitDeathEffects_CapitalShip_ThirdExplosionHasXYZOffset()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.CapitalShip);

        var mediumExplosion = effects.Single(r => r.Effect == VFXEffectType.ExplosionMedium);
        Assert.Equal(-1f, mediumExplosion.OffsetX);
        Assert.Equal(1f,  mediumExplosion.OffsetY);
        Assert.Equal(1f,  mediumExplosion.OffsetZ);
    }

    [Fact]
    public void GetUnitDeathEffects_Infantry_NoLargeExplosion()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.Infantry);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.ExplosionLarge);
    }

    [Fact]
    public void GetUnitDeathEffects_Tank_NoSmallExplosion()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.Tank);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.ExplosionSmall);
    }

    [Fact]
    public void GetUnitDeathEffects_CapitalShip_NoSmallExplosion()
    {
        var effects = VFXDispatcher.GetUnitDeathEffects(UnitCategory.CapitalShip);
        Assert.DoesNotContain(effects, r => r.Effect == VFXEffectType.ExplosionSmall);
    }

    [Fact]
    public void GetUnitDeathEffects_AllCategoriesReturnNonEmpty()
    {
        foreach (UnitCategory cat in Enum.GetValues<UnitCategory>())
        {
            var effects = VFXDispatcher.GetUnitDeathEffects(cat);
            Assert.NotEmpty(effects);
        }
    }

    [Fact]
    public void GetUnitDeathEffects_NonCapitalShip_AllEffectsHaveZeroOffset()
    {
        var nonCapital = Enum.GetValues<UnitCategory>()
            .Where(c => c != UnitCategory.CapitalShip);

        foreach (var cat in nonCapital)
        {
            var effects = VFXDispatcher.GetUnitDeathEffects(cat);
            foreach (var req in effects)
            {
                Assert.True(
                    req.OffsetX == 0f && req.OffsetY == 0f && req.OffsetZ == 0f,
                    $"Category {cat}: expected zero offset, got ({req.OffsetX},{req.OffsetY},{req.OffsetZ})");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cross-cutting: return-value contracts
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetAttackFiredEffects_NeverReturnsNull()
    {
        foreach (WeaponType type in Enum.GetValues<WeaponType>())
            Assert.NotNull(VFXDispatcher.GetAttackFiredEffects(type));
    }

    [Fact]
    public void GetAttackImpactEffects_NeverReturnsNull()
    {
        Assert.NotNull(VFXDispatcher.GetAttackImpactEffects(true,  true));
        Assert.NotNull(VFXDispatcher.GetAttackImpactEffects(true,  false));
        Assert.NotNull(VFXDispatcher.GetAttackImpactEffects(false, true));
        Assert.NotNull(VFXDispatcher.GetAttackImpactEffects(false, false));
    }

    [Fact]
    public void GetUnitDeathEffects_NeverReturnsNull()
    {
        foreach (UnitCategory cat in Enum.GetValues<UnitCategory>())
            Assert.NotNull(VFXDispatcher.GetUnitDeathEffects(cat));
    }

    // ═══════════════════════════════════════════════════════════════════
    // VFXEffectType enum coverage — all values are handled
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllVFXEffectTypes_AppearInAtLeastOneDispatch()
    {
        // Collect every effect type that any dispatch path can produce.
        var reachable = new System.Collections.Generic.HashSet<VFXEffectType>();

        foreach (WeaponType wt in Enum.GetValues<WeaponType>())
            foreach (var r in VFXDispatcher.GetAttackFiredEffects(wt))
                reachable.Add(r.Effect);

        foreach (var (hit, aoe) in new (bool, bool)[] { (true, true), (true, false), (false, true) })
            foreach (var r in VFXDispatcher.GetAttackImpactEffects(hit, aoe))
                reachable.Add(r.Effect);

        foreach (UnitCategory cat in Enum.GetValues<UnitCategory>())
            foreach (var r in VFXDispatcher.GetUnitDeathEffects(cat))
                reachable.Add(r.Effect);

        // Every VFXEffectType value must be reachable by at least one path
        foreach (VFXEffectType effect in Enum.GetValues<VFXEffectType>())
        {
            Assert.True(reachable.Contains(effect),
                $"{effect} is defined in VFXEffectType but is never returned by any dispatch method.");
        }
    }
}
