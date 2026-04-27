using System;
using System.Numerics;

namespace CorditeWars.Core;

/// <summary>
/// Fixed-point number type using Q16.16 format (32-bit integer with 16 fractional bits).
/// CRITICAL FOR DETERMINISM: Floating-point arithmetic produces different results
/// on different CPUs (x86 vs ARM, different FPU settings, etc.).
/// All gameplay simulation math (positions, health, damage, speeds) MUST use
/// FixedPoint instead of float/double to guarantee identical results across
/// all platforms (PC, Linux, Android) in lockstep multiplayer.
///
/// Rendering can still use floats — only the simulation must be deterministic.
/// </summary>
public readonly struct FixedPoint : IEquatable<FixedPoint>, IComparable<FixedPoint>
{
    public const int FractionalBits = 16;
    public const int Scale = 1 << FractionalBits; // 65536

    /// <summary>
    /// The raw integer representation. Public for serialization.
    /// </summary>
    public readonly int Raw;

    // ── Constructors ─────────────────────────────────────────────────

    private FixedPoint(int raw) => Raw = raw;

    public static FixedPoint FromRaw(int raw) => new(raw);
    public static FixedPoint FromInt(int value) => new(value * Scale);
    public static FixedPoint FromFloat(float value) => new((int)(value * Scale));

    // ── Constants ────────────────────────────────────────────────────

    public static readonly FixedPoint Zero = new(0);
    public static readonly FixedPoint One = new(Scale);
    public static readonly FixedPoint Half = new(Scale / 2);
    public static readonly FixedPoint MinValue = new(int.MinValue);
    public static readonly FixedPoint MaxValue = new(int.MaxValue);

    // ── Conversion ──────────────────────────────────────────────────

    public int ToInt() => Raw >> FractionalBits;
    public float ToFloat() => (float)Raw / Scale;
    public double ToDouble() => (double)Raw / Scale;

    // ── Arithmetic Operators ────────────────────────────────────────

    public static FixedPoint operator +(FixedPoint a, FixedPoint b) => new(a.Raw + b.Raw);
    public static FixedPoint operator -(FixedPoint a, FixedPoint b) => new(a.Raw - b.Raw);
    public static FixedPoint operator -(FixedPoint a) => new(-a.Raw);

    public static FixedPoint operator *(FixedPoint a, FixedPoint b) =>
        new((int)(((long)a.Raw * b.Raw) >> FractionalBits));

    public static FixedPoint operator /(FixedPoint a, FixedPoint b) =>
        new((int)(((long)a.Raw << FractionalBits) / b.Raw));

    public static FixedPoint operator *(FixedPoint a, int b) => new(a.Raw * b);
    public static FixedPoint operator *(int a, FixedPoint b) => new(a * b.Raw);

    // ── Comparison Operators ────────────────────────────────────────

    public static bool operator ==(FixedPoint a, FixedPoint b) => a.Raw == b.Raw;
    public static bool operator !=(FixedPoint a, FixedPoint b) => a.Raw != b.Raw;
    public static bool operator <(FixedPoint a, FixedPoint b) => a.Raw < b.Raw;
    public static bool operator >(FixedPoint a, FixedPoint b) => a.Raw > b.Raw;
    public static bool operator <=(FixedPoint a, FixedPoint b) => a.Raw <= b.Raw;
    public static bool operator >=(FixedPoint a, FixedPoint b) => a.Raw >= b.Raw;

    // ── Math Functions ──────────────────────────────────────────────

    public static FixedPoint Abs(FixedPoint a) => new(Math.Abs(a.Raw));
    public static FixedPoint Min(FixedPoint a, FixedPoint b) => a.Raw < b.Raw ? a : b;
    public static FixedPoint Max(FixedPoint a, FixedPoint b) => a.Raw > b.Raw ? a : b;

    public static FixedPoint Clamp(FixedPoint value, FixedPoint min, FixedPoint max)
    {
        if (value.Raw < min.Raw) return min;
        if (value.Raw > max.Raw) return max;
        return value;
    }

    /// <summary>
    /// Integer square root using Newton's method. Deterministic.
    /// Uses a bit-based initial guess to converge in ~2–3 iterations instead
    /// of the ~17 iterations that would result from starting at <c>val</c>.
    /// </summary>
    public static FixedPoint Sqrt(FixedPoint a)
    {
        if (a.Raw <= 0) return Zero;

        // Scale up to maintain precision, then Newton's method
        long val = (long)a.Raw << FractionalBits;

        // Start from 2^(ceil(bits/2)) — always within 2× of the true sqrt,
        // so Newton's method converges in 2–3 iterations rather than ~17.
        int bits = 63 - BitOperations.LeadingZeroCount((ulong)val);
        long guess = 1L << ((bits + 1) >> 1);

        long prev;

        do
        {
            prev = guess;
            guess = (guess + val / guess) >> 1;
        } while (Math.Abs(guess - prev) > 1);

        return new FixedPoint((int)guess);
    }

    // ── Interface Implementations ───────────────────────────────────

    public bool Equals(FixedPoint other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is FixedPoint fp && Equals(fp);
    public override int GetHashCode() => Raw;
    public int CompareTo(FixedPoint other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToFloat().ToString("F4");
}

/// <summary>
/// 2D vector using fixed-point arithmetic for deterministic simulation.
/// </summary>
public readonly struct FixedVector2 : IEquatable<FixedVector2>
{
    public readonly FixedPoint X;
    public readonly FixedPoint Y;

    public FixedVector2(FixedPoint x, FixedPoint y) { X = x; Y = y; }

    public static readonly FixedVector2 Zero = new(FixedPoint.Zero, FixedPoint.Zero);

    public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static FixedVector2 operator *(FixedVector2 a, FixedPoint scalar) =>
        new(a.X * scalar, a.Y * scalar);

    /// <summary>
    /// Squared length — avoids the sqrt for distance comparisons.
    /// </summary>
    public FixedPoint LengthSquared => X * X + Y * Y;

    /// <summary>
    /// Euclidean length using deterministic fixed-point sqrt.
    /// </summary>
    public FixedPoint Length => FixedPoint.Sqrt(LengthSquared);

    /// <summary>
    /// Returns a normalized (unit length) vector. Returns Zero if length is zero.
    /// </summary>
    public FixedVector2 Normalized()
    {
        var len = Length;
        if (len == FixedPoint.Zero) return Zero;
        return new FixedVector2(X / len, Y / len);
    }

    /// <summary>
    /// Squared distance to another point. Use for range checks to avoid sqrt.
    /// </summary>
    public FixedPoint DistanceSquaredTo(FixedVector2 other) => (this - other).LengthSquared;

    public bool Equals(FixedVector2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FixedVector2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);
    public static bool operator ==(FixedVector2 a, FixedVector2 b) => a.Equals(b);
    public static bool operator !=(FixedVector2 a, FixedVector2 b) => !a.Equals(b);

    public override string ToString() => $"({X}, {Y})";

    /// <summary>
    /// Converts to a Godot Vector3 for rendering (Y is up in Godot 3D).
    /// Simulation X → Render X, Simulation Y → Render Z, Render Y = 0.
    /// </summary>
    public Godot.Vector3 ToVector3(float height = 0f) =>
        new(X.ToFloat(), height, Y.ToFloat());
}
