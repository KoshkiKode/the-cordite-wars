namespace CorditeWars.Core;

/// <summary>
/// Deterministic random number generator using xoshiro256** algorithm.
/// CRITICAL: All gameplay randomness must flow through this RNG.
/// Never use System.Random, GD.Randf(), or any other non-deterministic
/// source during simulation. This ensures lockstep sync across all clients.
///
/// The xoshiro256** algorithm is:
/// - Deterministic given the same seed
/// - Fast (no division, no modulus in core loop)
/// - High quality (passes all BigCrush tests)
/// - 256-bit state (period of 2^256 - 1)
/// </summary>
public class DeterministicRng
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>
    /// Returns the full internal state for deterministic checksum comparison.
    /// Used by StateChecksum to verify lockstep sync.
    /// </summary>
    public (ulong s0, ulong s1, ulong s2, ulong s3) GetState() => (_s0, _s1, _s2, _s3);

    /// <summary>
    /// Restores the full internal state from a save file.
    /// Used by save/load to resume the exact RNG sequence.
    /// </summary>
    public void SetState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    public DeterministicRng(ulong seed)
    {
        // Use SplitMix64 to initialize state from a single seed.
        // This ensures even simple seeds (like 1, 2, 3) produce
        // well-distributed initial states.
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    /// <summary>
    /// Returns the next random ulong in the sequence.
    /// </summary>
    public ulong NextUlong()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>
    /// Returns a random integer in [0, max) range.
    /// Uses rejection sampling to avoid modulo bias.
    /// </summary>
    public int NextInt(int max)
    {
        if (max <= 0) return 0;
        ulong threshold = (ulong)(-(long)max) % (ulong)max;
        ulong r;
        do { r = NextUlong(); } while (r < threshold);
        return (int)(r % (ulong)max);
    }

    /// <summary>
    /// Returns a random integer in [min, max) range.
    /// </summary>
    public int NextInt(int min, int max)
    {
        return min + NextInt(max - min);
    }

    /// <summary>
    /// Returns a random float in [0.0, 1.0) range.
    /// Uses the upper 53 bits for maximum precision in a double.
    /// </summary>
    public double NextDouble()
    {
        return (NextUlong() >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Returns a random float in [0.0f, 1.0f) range.
    /// </summary>
    public float NextFloat()
    {
        return (float)NextDouble();
    }

    /// <summary>
    /// Returns true with the given probability [0.0, 1.0].
    /// </summary>
    public bool NextBool(double probability = 0.5)
    {
        return NextDouble() < probability;
    }

    private static ulong RotateLeft(ulong x, int k)
    {
        return (x << k) | (x >> (64 - k));
    }

    private static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
