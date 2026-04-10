using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Networking;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for StateChecksum — deterministic FNV-1a game-state hashing.
/// Verifies that the low-level hash primitives are correct and that
/// ComputeChecksum is deterministic, order-sensitive, and excludes dead units.
/// </summary>
public class StateChecksumTests
{
    // ── FnvHashInt ─────────────────────────────────────────────────────────

    [Fact]
    public void FnvHashInt_DifferentValues_ProduceDifferentHashes()
    {
        uint h1 = StateChecksum.FnvHashInt(2166136261u, 42);
        uint h2 = StateChecksum.FnvHashInt(2166136261u, 43);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void FnvHashInt_SameInputs_ProduceSameHash()
    {
        uint h1 = StateChecksum.FnvHashInt(2166136261u, 12345);
        uint h2 = StateChecksum.FnvHashInt(2166136261u, 12345);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void FnvHashInt_DifferentStartHash_ProducesDifferentResult()
    {
        uint h1 = StateChecksum.FnvHashInt(2166136261u, 0);
        uint h2 = StateChecksum.FnvHashInt(99999u, 0);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void FnvHashInt_Zero_DifferentFromOne()
    {
        uint basis = 2166136261u;
        uint h0 = StateChecksum.FnvHashInt(basis, 0);
        uint h1 = StateChecksum.FnvHashInt(basis, 1);
        Assert.NotEqual(h0, h1);
    }

    [Fact]
    public void FnvHashInt_NegativeValue_Deterministic()
    {
        uint h1 = StateChecksum.FnvHashInt(2166136261u, -1);
        uint h2 = StateChecksum.FnvHashInt(2166136261u, -1);
        Assert.Equal(h1, h2);
    }

    // ── FnvHashUlong ───────────────────────────────────────────────────────

    [Fact]
    public void FnvHashUlong_DifferentValues_ProduceDifferentHashes()
    {
        uint h1 = StateChecksum.FnvHashUlong(2166136261u, 1UL);
        uint h2 = StateChecksum.FnvHashUlong(2166136261u, 2UL);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void FnvHashUlong_SameInputs_ProduceSameHash()
    {
        uint h1 = StateChecksum.FnvHashUlong(2166136261u, 9999999999UL);
        uint h2 = StateChecksum.FnvHashUlong(2166136261u, 9999999999UL);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void FnvHashUlong_Zero_DifferentFromOne()
    {
        uint basis = 2166136261u;
        Assert.NotEqual(
            StateChecksum.FnvHashUlong(basis, 0UL),
            StateChecksum.FnvHashUlong(basis, 1UL));
    }

    [Fact]
    public void FnvHashUlong_LargeValue_Deterministic()
    {
        ulong big = ulong.MaxValue;
        uint h1 = StateChecksum.FnvHashUlong(2166136261u, big);
        uint h2 = StateChecksum.FnvHashUlong(2166136261u, big);
        Assert.Equal(h1, h2);
    }

    // ── ComputeChecksum — determinism ──────────────────────────────────────

    private static SimUnit MakeAliveUnit(int id, int playerId, int x, int y, int hp)
    {
        return new SimUnit
        {
            UnitId = id,
            PlayerId = playerId,
            Movement = new MovementState
            {
                Position = new FixedVector2(FixedPoint.FromInt(x), FixedPoint.FromInt(y))
            },
            Health = FixedPoint.FromInt(hp),
            MaxHealth = FixedPoint.FromInt(hp),
            IsAlive = true,
            Weapons = new List<WeaponData>(),
            WeaponCooldowns = new List<FixedPoint>(),
            Profile = MovementProfile.Infantry(),
        };
    }

    private static SimUnit MakeDeadUnit(int id) =>
        MakeAliveUnit(id, 1, 0, 0, 100) with { IsAlive = false };

    [Fact]
    public void ComputeChecksum_SameState_ProducesSameHash()
    {
        var rng1 = new DeterministicRng(42);
        var rng2 = new DeterministicRng(42);

        var units1 = new List<SimUnit> { MakeAliveUnit(1, 1, 10, 10, 100) };
        var units2 = new List<SimUnit> { MakeAliveUnit(1, 1, 10, 10, 100) };

        uint c1 = StateChecksum.ComputeChecksum(5UL, units1, rng1);
        uint c2 = StateChecksum.ComputeChecksum(5UL, units2, rng2);

        Assert.Equal(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_DifferentTick_ProducesDifferentHash()
    {
        var rng1 = new DeterministicRng(0);
        var rng2 = new DeterministicRng(0);
        var units = new List<SimUnit> { MakeAliveUnit(1, 1, 5, 5, 100) };

        uint c1 = StateChecksum.ComputeChecksum(1UL, units, rng1);
        uint c2 = StateChecksum.ComputeChecksum(2UL, units, rng2);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_DifferentPosition_ProducesDifferentHash()
    {
        var rng1 = new DeterministicRng(0);
        var rng2 = new DeterministicRng(0);

        var units1 = new List<SimUnit> { MakeAliveUnit(1, 1, 5, 5, 100) };
        var units2 = new List<SimUnit> { MakeAliveUnit(1, 1, 6, 5, 100) };

        uint c1 = StateChecksum.ComputeChecksum(1UL, units1, rng1);
        uint c2 = StateChecksum.ComputeChecksum(1UL, units2, rng2);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_DifferentHealth_ProducesDifferentHash()
    {
        var rng1 = new DeterministicRng(0);
        var rng2 = new DeterministicRng(0);

        var units1 = new List<SimUnit> { MakeAliveUnit(1, 1, 5, 5, 100) };
        var units2 = new List<SimUnit> { MakeAliveUnit(1, 1, 5, 5, 50) };

        uint c1 = StateChecksum.ComputeChecksum(1UL, units1, rng1);
        uint c2 = StateChecksum.ComputeChecksum(1UL, units2, rng2);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_DeadUnitsExcluded()
    {
        // One hash with no units, another with a dead unit — should be equal
        // because dead units are skipped in the checksum loop.
        var rng1 = new DeterministicRng(0);
        var rng2 = new DeterministicRng(0);

        var noUnits = new List<SimUnit>();
        var deadUnit = new List<SimUnit> { MakeDeadUnit(1) };

        uint c1 = StateChecksum.ComputeChecksum(1UL, noUnits, rng1);
        uint c2 = StateChecksum.ComputeChecksum(1UL, deadUnit, rng2);

        Assert.Equal(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_EmptyUnits_StillHashesTick()
    {
        var rng1 = new DeterministicRng(0);
        var rng2 = new DeterministicRng(0);
        var empty = new List<SimUnit>();

        uint c1 = StateChecksum.ComputeChecksum(10UL, empty, rng1);
        uint c2 = StateChecksum.ComputeChecksum(11UL, empty, rng2);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_MultipleUnits_AllContribute()
    {
        var rng1 = new DeterministicRng(7);
        var rng2 = new DeterministicRng(7);

        var twoUnits = new List<SimUnit>
        {
            MakeAliveUnit(1, 1, 3, 3, 100),
            MakeAliveUnit(2, 2, 7, 7, 80)
        };
        var oneUnit = new List<SimUnit>
        {
            MakeAliveUnit(1, 1, 3, 3, 100)
        };

        uint c1 = StateChecksum.ComputeChecksum(1UL, twoUnits, rng1);
        uint c2 = StateChecksum.ComputeChecksum(1UL, oneUnit, rng2);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void ComputeChecksum_DifferentRngState_ProducesDifferentHash()
    {
        // Same units + tick, but RNG has advanced differently on each client
        var rng1 = new DeterministicRng(1);
        var rng2 = new DeterministicRng(2); // different seed → different state

        var units = new List<SimUnit> { MakeAliveUnit(1, 1, 0, 0, 100) };

        uint c1 = StateChecksum.ComputeChecksum(1UL, units, rng1);
        uint c2 = StateChecksum.ComputeChecksum(1UL, units, rng2);

        Assert.NotEqual(c1, c2);
    }
}
