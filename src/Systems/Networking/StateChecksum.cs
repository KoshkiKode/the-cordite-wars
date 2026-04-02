using System.Collections.Generic;
using UnnamedRTS.Core;
using UnnamedRTS.Systems.Pathfinding;

namespace UnnamedRTS.Systems.Networking;

/// <summary>
/// Computes deterministic FNV-1a checksums of game state for desync detection.
/// All clients compute the checksum at the same tick; mismatches indicate desync.
/// Only hashes simulation-critical state: tick, unit positions, health, RNG state.
/// </summary>
public static class StateChecksum
{
    private const uint FnvOffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    /// <summary>
    /// Computes a 32-bit FNV-1a hash over the deterministic game state.
    /// Units must be provided sorted by UnitId ascending (SimUnit ordering).
    /// </summary>
    public static uint ComputeChecksum(
        ulong currentTick,
        List<SimUnit> units,
        DeterministicRng rng)
    {
        uint hash = FnvOffsetBasis;

        // Hash the current tick
        hash = FnvHashUlong(hash, currentTick);

        // Hash all unit positions and health (sorted by UnitId — caller's responsibility)
        for (int i = 0; i < units.Count; i++)
        {
            SimUnit u = units[i];
            if (!u.IsAlive) continue;

            hash = FnvHashInt(hash, u.UnitId);
            hash = FnvHashInt(hash, u.PlayerId);
            hash = FnvHashInt(hash, u.Movement.Position.X.Raw);
            hash = FnvHashInt(hash, u.Movement.Position.Y.Raw);
            hash = FnvHashInt(hash, u.Health.Raw);
        }

        // Hash RNG state to catch divergence even without visible unit state differences
        var rngState = rng.GetState();
        hash = FnvHashUlong(hash, rngState.s0);
        hash = FnvHashUlong(hash, rngState.s1);
        hash = FnvHashUlong(hash, rngState.s2);
        hash = FnvHashUlong(hash, rngState.s3);

        return hash;
    }

    /// <summary>
    /// FNV-1a step for a 32-bit integer value.
    /// Feeds 4 bytes into the hash one at a time.
    /// </summary>
    public static uint FnvHashInt(uint hash, int value)
    {
        hash ^= (uint)(value & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 8) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 16) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 24) & 0xFF);
        hash *= FnvPrime;
        return hash;
    }

    /// <summary>
    /// FNV-1a step for a 64-bit unsigned integer value.
    /// Feeds 8 bytes into the hash one at a time.
    /// </summary>
    public static uint FnvHashUlong(uint hash, ulong value)
    {
        hash ^= (uint)(value & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 8) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 16) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 24) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 32) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 40) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 48) & 0xFF);
        hash *= FnvPrime;
        hash ^= (uint)((value >> 56) & 0xFF);
        hash *= FnvPrime;
        return hash;
    }
}
