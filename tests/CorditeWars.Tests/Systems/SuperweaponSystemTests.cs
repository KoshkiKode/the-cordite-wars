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
}
