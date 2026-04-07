using CorditeWars.Core;

namespace CorditeWars.Tests.Core;

/// <summary>
/// Tests for FixedVector2 deterministic 2D vector math.
/// </summary>
public class FixedVector2Tests
{
    // ── Construction ────────────────────────────────────────────────────

    [Fact]
    public void Zero_HasZeroComponents()
    {
        Assert.Equal(FixedPoint.Zero, FixedVector2.Zero.X);
        Assert.Equal(FixedPoint.Zero, FixedVector2.Zero.Y);
    }

    [Fact]
    public void Constructor_SetsComponents()
    {
        var x = FixedPoint.FromInt(3);
        var y = FixedPoint.FromInt(7);
        var v = new FixedVector2(x, y);
        Assert.Equal(x, v.X);
        Assert.Equal(y, v.Y);
    }

    // ── Arithmetic ─────────────────────────────────────────────────────

    [Fact]
    public void Addition()
    {
        var a = new FixedVector2(FixedPoint.FromInt(1), FixedPoint.FromInt(2));
        var b = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        var result = a + b;
        Assert.Equal(FixedPoint.FromInt(4), result.X);
        Assert.Equal(FixedPoint.FromInt(6), result.Y);
    }

    [Fact]
    public void Subtraction()
    {
        var a = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(10));
        var b = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        var result = a - b;
        Assert.Equal(FixedPoint.FromInt(2), result.X);
        Assert.Equal(FixedPoint.FromInt(6), result.Y);
    }

    [Fact]
    public void ScalarMultiplication()
    {
        var v = new FixedVector2(FixedPoint.FromInt(2), FixedPoint.FromInt(3));
        var scalar = FixedPoint.FromInt(4);
        var result = v * scalar;
        Assert.Equal(FixedPoint.FromInt(8), result.X);
        Assert.Equal(FixedPoint.FromInt(12), result.Y);
    }

    // ── Length ──────────────────────────────────────────────────────────

    [Fact]
    public void LengthSquared_3_4_Triangle()
    {
        // 3² + 4² = 25
        var v = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        Assert.Equal(FixedPoint.FromInt(25), v.LengthSquared);
    }

    [Fact]
    public void Length_3_4_Triangle()
    {
        // √25 = 5
        var v = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        float length = v.Length.ToFloat();
        Assert.True(
            Math.Abs(length - 5.0f) < 0.02f,
            $"Expected ~5.0, got {length}");
    }

    [Fact]
    public void LengthSquared_Zero()
    {
        Assert.Equal(FixedPoint.Zero, FixedVector2.Zero.LengthSquared);
    }

    // ── Normalized ─────────────────────────────────────────────────────

    [Fact]
    public void Normalized_UnitVector()
    {
        var v = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        var norm = v.Normalized();
        float length = norm.Length.ToFloat();
        Assert.True(
            Math.Abs(length - 1.0f) < 0.02f,
            $"Normalized length should be ~1.0, got {length}");
    }

    [Fact]
    public void Normalized_ZeroVector_ReturnsZero()
    {
        var norm = FixedVector2.Zero.Normalized();
        Assert.Equal(FixedVector2.Zero, norm);
    }

    [Fact]
    public void Normalized_Direction_Preserved()
    {
        var v = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero);
        var norm = v.Normalized();
        Assert.True(
            Math.Abs(norm.X.ToFloat() - 1.0f) < 0.02f,
            $"Expected X ~1.0, got {norm.X.ToFloat()}");
        Assert.True(
            Math.Abs(norm.Y.ToFloat()) < 0.02f,
            $"Expected Y ~0.0, got {norm.Y.ToFloat()}");
    }

    // ── Distance ───────────────────────────────────────────────────────

    [Fact]
    public void DistanceSquaredTo_SamePoint_IsZero()
    {
        var v = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(3));
        Assert.Equal(FixedPoint.Zero, v.DistanceSquaredTo(v));
    }

    [Fact]
    public void DistanceSquaredTo_KnownDistance()
    {
        var a = new FixedVector2(FixedPoint.FromInt(0), FixedPoint.FromInt(0));
        var b = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(4));
        // distance² = 9 + 16 = 25
        Assert.Equal(FixedPoint.FromInt(25), a.DistanceSquaredTo(b));
    }

    [Fact]
    public void DistanceSquaredTo_IsSymmetric()
    {
        var a = new FixedVector2(FixedPoint.FromInt(1), FixedPoint.FromInt(2));
        var b = new FixedVector2(FixedPoint.FromInt(4), FixedPoint.FromInt(6));
        Assert.Equal(a.DistanceSquaredTo(b), b.DistanceSquaredTo(a));
    }

    // ── Equality ───────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameComponents()
    {
        var a = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(7));
        var b = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(7));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inequality_DifferentComponents()
    {
        var a = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(7));
        var b = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(8));
        Assert.True(a != b);
        Assert.False(a == b);
    }
}
