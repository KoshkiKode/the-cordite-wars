using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Superweapon;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for <see cref="SuperweaponSystem"/>, <see cref="PlayerSuperweaponState"/>,
/// and the superweapon catalogue.
/// </summary>
public class SuperweaponSystemTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static SimUnit MakeUnit(int id, int playerId, FixedVector2 pos, int hp = 100)
    {
        return new SimUnit
        {
            UnitId    = id,
            PlayerId  = playerId,
            Movement  = new MovementState { Position = pos },
            Health    = FixedPoint.FromInt(hp),
            MaxHealth = FixedPoint.FromInt(hp),
            IsAlive   = true,
            Category  = UnitCategory.Infantry,
            Weapons   = new List<WeaponData>(),
            WeaponCooldowns = new List<FixedPoint>(),
            Profile   = MovementProfile.Infantry(),
            Radius    = FixedPoint.One
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Catalogue
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Catalogue_ContainsArcloftWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("arcloft"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("arcloft", w.FactionId));
    }

    [Fact]
    public void Catalogue_ContainsBastionWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("bastion"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("bastion", w.FactionId));
    }

    [Fact]
    public void GetData_ReturnsKnownEntry()
    {
        var data = SuperweaponSystem.GetData("arcloft_orbital_strike");
        Assert.NotNull(data);
        Assert.Equal(SuperweaponType.OrbitalStrike, data!.Type);
    }

    [Fact]
    public void GetData_ReturnsNull_ForUnknownId()
    {
        Assert.Null(SuperweaponSystem.GetData("nonexistent_weapon"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Registration
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterPlayer_AssignsWeapon()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        Assert.NotNull(sys.GetState(1));
    }

    [Fact]
    public void RegisterPlayer_UnknownWeapon_NoState()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "does_not_exist");
        Assert.Null(sys.GetState(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cooldown
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewWeapon_StartsOnCooldown_NotReady()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        Assert.False(sys.IsReady(1));
    }

    [Fact]
    public void Tick_ReducesCooldown()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        var state = sys.GetState(1)!;
        int initial = state.CooldownRemaining;

        sys.Tick();
        Assert.Equal(initial - 1, state.CooldownRemaining);
    }

    [Fact]
    public void Weapon_BecomesReady_AfterFullCooldown()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        var state = sys.GetState(1)!;
        int ticks = state.CooldownRemaining;

        for (int i = 0; i < ticks; i++)
            sys.Tick();

        Assert.True(sys.IsReady(1));
    }

    [Fact]
    public void ChargePercent_IsZero_WhenFreshCooldown()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        Assert.Equal(0f, sys.GetChargePercent(1), precision: 2);
    }

    [Fact]
    public void ChargePercent_IsOne_WhenReady()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        var state = sys.GetState(1)!;
        int ticks = state.CooldownRemaining;
        for (int i = 0; i < ticks; i++)
            sys.Tick();
        Assert.Equal(1f, sys.GetChargePercent(1), precision: 2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TryActivate — not ready
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryActivate_WhenNotReady_DoesNotFire()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");

        var result = sys.TryActivate(1, FixedVector2.Zero, new List<SimUnit>());
        Assert.False(result.DidFire);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TryActivate — ready / hits units
    // ═══════════════════════════════════════════════════════════════════════

    private static void MakeReady(SuperweaponSystem sys, int playerId)
    {
        var state = sys.GetState(playerId)!;
        int initialCooldown = state.CooldownRemaining;
        for (int i = 0; i < initialCooldown; i++)
            sys.Tick();
    }

    [Fact]
    public void TryActivate_WhenReady_Fires()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        MakeReady(sys, 1);

        var result = sys.TryActivate(1, FixedVector2.Zero, new List<SimUnit>());
        Assert.True(result.DidFire);
    }

    [Fact]
    public void TryActivate_ArmsWeapon_AfterFiring()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        MakeReady(sys, 1);

        sys.TryActivate(1, FixedVector2.Zero, new List<SimUnit>());
        Assert.False(sys.IsReady(1)); // back on cooldown
    }

    [Fact]
    public void TryActivate_HitsEnemiesInAoE()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(playerId: 1, "arcloft_orbital_strike");
        MakeReady(sys, 1);

        var target = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        var units = new List<SimUnit>
        {
            // inside AoE (player 2 — enemy)
            MakeUnit(id: 10, playerId: 2,
                pos: new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10))),
            // outside AoE
            MakeUnit(id: 11, playerId: 2,
                pos: new FixedVector2(FixedPoint.FromInt(50), FixedPoint.FromInt(50)))
        };

        var result = sys.TryActivate(1, target, units);
        Assert.True(result.DidFire);
        Assert.Contains(10, result.HitUnitIds);
        Assert.DoesNotContain(11, result.HitUnitIds);
    }

    [Fact]
    public void TryActivate_DoesNotHitFriendlyUnits()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_orbital_strike");
        MakeReady(sys, 1);

        var units = new List<SimUnit>
        {
            // friendly inside AoE
            MakeUnit(id: 5, playerId: 1,
                pos: FixedVector2.Zero)
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.DoesNotContain(5, result.HitUnitIds);
    }

    [Fact]
    public void TryActivate_EMP_SetsFlag()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "arcloft_emp_blast");
        MakeReady(sys, 1);

        var result = sys.TryActivate(1, FixedVector2.Zero, new List<SimUnit>());
        Assert.True(result.IsEMP);
        Assert.True(result.EMPDurationTicks > 0);
    }

    [Fact]
    public void TryActivate_ReinforcementDrop_SpawnsUnits()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "bastion_reinforcement_drop");
        MakeReady(sys, 1);

        var result = sys.TryActivate(1, FixedVector2.Zero, new List<SimUnit>());
        Assert.True(result.DidFire);
        Assert.True(result.SpawnedUnitTypeIds.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ironmarch — Seismic Charge
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Catalogue_ContainsIronmarchWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("ironmarch"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("ironmarch", w.FactionId));
    }

    [Fact]
    public void SeismicCharge_InCatalogue_WithCorrectType()
    {
        var data = SuperweaponSystem.GetData("ironmarch_seismic_charge");
        Assert.NotNull(data);
        Assert.Equal(SuperweaponType.SeismicCharge, data!.Type);
    }

    [Fact]
    public void SeismicCharge_HitsEnemiesInAoE()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "ironmarch_seismic_charge");
        MakeReady(sys, 1);

        var target = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        var units = new List<SimUnit>
        {
            MakeUnit(10, 2, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10))), // inside
            MakeUnit(11, 2, new FixedVector2(FixedPoint.FromInt(100), FixedPoint.FromInt(100))) // far away
        };

        var result = sys.TryActivate(1, target, units);
        Assert.True(result.DidFire);
        Assert.Contains(10, result.HitUnitIds);
        Assert.DoesNotContain(11, result.HitUnitIds);
    }

    [Fact]
    public void SeismicCharge_DamageIs250()
    {
        var data = SuperweaponSystem.GetData("ironmarch_seismic_charge")!;
        Assert.Equal(250, data.Damage.ToInt());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Kragmore — Artillery Salvo
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Catalogue_ContainsKragmoreWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("kragmore"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("kragmore", w.FactionId));
    }

    [Fact]
    public void ArtillerySalvo_InCatalogue_WithCorrectType()
    {
        var data = SuperweaponSystem.GetData("kragmore_artillery_salvo");
        Assert.NotNull(data);
        Assert.Equal(SuperweaponType.ArtillerySalvo, data!.Type);
    }

    [Fact]
    public void ArtillerySalvo_HitsMultipleEnemiesInArea()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "kragmore_artillery_salvo");
        MakeReady(sys, 1);

        // AoE radius = 11 cells; pack 3 enemies inside and 1 clearly outside
        var units = new List<SimUnit>
        {
            MakeUnit(10, 2, new FixedVector2(FixedPoint.FromInt(1), FixedPoint.Zero)),
            MakeUnit(11, 2, new FixedVector2(FixedPoint.Zero,      FixedPoint.FromInt(1))),
            MakeUnit(12, 2, new FixedVector2(FixedPoint.FromInt(2), FixedPoint.FromInt(2))),
            MakeUnit(20, 2, new FixedVector2(FixedPoint.FromInt(50), FixedPoint.Zero)), // outside AoE (radius=11)
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.True(result.DidFire);
        Assert.True(result.HitUnitIds.Count >= 3);
        Assert.DoesNotContain(20, result.HitUnitIds);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stormrend — Lightning Cascade
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Catalogue_ContainsStormrendWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("stormrend"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("stormrend", w.FactionId));
    }

    [Fact]
    public void LightningCascade_InCatalogue_WithCorrectType()
    {
        var data = SuperweaponSystem.GetData("stormrend_lightning_cascade");
        Assert.NotNull(data);
        Assert.Equal(SuperweaponType.LightningCascade, data!.Type);
        Assert.True(data.ChainCount > 0);
    }

    [Fact]
    public void LightningCascade_HitsChainedTargets()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "stormrend_lightning_cascade");
        MakeReady(sys, 1);

        // 4 enemies clustered close together
        var units = new List<SimUnit>
        {
            MakeUnit(10, 2, new FixedVector2(FixedPoint.FromInt(1), FixedPoint.Zero)),
            MakeUnit(11, 2, new FixedVector2(FixedPoint.FromInt(2), FixedPoint.Zero)),
            MakeUnit(12, 2, new FixedVector2(FixedPoint.FromInt(3), FixedPoint.Zero)),
            MakeUnit(13, 2, new FixedVector2(FixedPoint.FromInt(4), FixedPoint.Zero)),
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.True(result.DidFire);
        Assert.True(result.HitUnitIds.Count >= 2, $"Expected chain ≥2 hits, got {result.HitUnitIds.Count}");
    }

    [Fact]
    public void LightningCascade_NeverHitsSameUnitTwice()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "stormrend_lightning_cascade");
        MakeReady(sys, 1);

        var units = new List<SimUnit>
        {
            MakeUnit(10, 2, FixedVector2.Zero),
            MakeUnit(11, 2, new FixedVector2(FixedPoint.FromInt(1), FixedPoint.Zero)),
            MakeUnit(12, 2, new FixedVector2(FixedPoint.FromInt(2), FixedPoint.Zero)),
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        var hitSet = new HashSet<int>(result.HitUnitIds);
        Assert.Equal(result.HitUnitIds.Count, hitSet.Count); // no duplicates
    }

    [Fact]
    public void LightningCascade_DoesNotHitFriendlyUnits()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "stormrend_lightning_cascade");
        MakeReady(sys, 1);

        var units = new List<SimUnit>
        {
            MakeUnit(5, 1, FixedVector2.Zero), // friendly — must not be hit
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.Empty(result.HitUnitIds);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Valkyr — Carpet Bomb Run
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Catalogue_ContainsValkyrWeapons()
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons("valkyr"));
        Assert.True(weapons.Count >= 1);
        Assert.All(weapons, w => Assert.Equal("valkyr", w.FactionId));
    }

    [Fact]
    public void CarpetBombRun_InCatalogue_WithCorrectType()
    {
        var data = SuperweaponSystem.GetData("valkyr_carpet_bomb_run");
        Assert.NotNull(data);
        Assert.Equal(SuperweaponType.CarpetBombRun, data!.Type);
        Assert.True(data.StripLength > FixedPoint.Zero);
        Assert.True(data.StripHalfWidth > FixedPoint.Zero);
    }

    [Fact]
    public void CarpetBombRun_HitsEnemiesInsideStrip()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "valkyr_carpet_bomb_run");
        MakeReady(sys, 1);

        // Strip runs along X; half-width=4, half-length=12, centred at origin
        var units = new List<SimUnit>
        {
            // inside strip (X ≤ 12, Y ≤ 4)
            MakeUnit(10, 2, new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(2))),
            MakeUnit(11, 2, new FixedVector2(FixedPoint.FromInt(-8), FixedPoint.FromInt(-3))),
            // outside strip — too wide
            MakeUnit(20, 2, new FixedVector2(FixedPoint.FromInt(1), FixedPoint.FromInt(10))),
            // outside strip — too long
            MakeUnit(21, 2, new FixedVector2(FixedPoint.FromInt(20), FixedPoint.Zero)),
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.True(result.DidFire);
        Assert.Contains(10, result.HitUnitIds);
        Assert.Contains(11, result.HitUnitIds);
        Assert.DoesNotContain(20, result.HitUnitIds);
        Assert.DoesNotContain(21, result.HitUnitIds);
    }

    [Fact]
    public void CarpetBombRun_DoesNotHitFriendlyUnits()
    {
        var sys = new SuperweaponSystem();
        sys.RegisterPlayer(1, "valkyr_carpet_bomb_run");
        MakeReady(sys, 1);

        var units = new List<SimUnit>
        {
            MakeUnit(5, 1, FixedVector2.Zero), // friendly — inside strip but should not be hit
        };

        var result = sys.TryActivate(1, FixedVector2.Zero, units);
        Assert.Empty(result.HitUnitIds);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // All 6 factions have weapons in the catalogue
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("arcloft")]
    [InlineData("bastion")]
    [InlineData("ironmarch")]
    [InlineData("kragmore")]
    [InlineData("stormrend")]
    [InlineData("valkyr")]
    public void AllFactions_HaveAtLeastOneWeapon(string factionId)
    {
        var weapons = new List<SuperweaponData>(SuperweaponSystem.GetFactionWeapons(factionId));
        Assert.True(weapons.Count >= 1, $"Faction '{factionId}' has no superweapon in the catalogue");
    }
}
