using CorditeWars.Core;

namespace CorditeWars.Tests.Core;

/// <summary>
/// Tests for FixedPoint Q16.16 deterministic arithmetic.
/// These tests are critical: any FixedPoint bug causes multiplayer desync.
/// </summary>
public class FixedPointTests
{
    // ── Construction & Conversion ───────────────────────────────────────

    [Fact]
    public void FromInt_ReturnsCorrectRaw()
    {
        var fp = FixedPoint.FromInt(5);
        Assert.Equal(5 * FixedPoint.Scale, fp.Raw);
    }

    [Fact]
    public void FromInt_Zero()
    {
        var fp = FixedPoint.FromInt(0);
        Assert.Equal(0, fp.Raw);
    }

    [Fact]
    public void FromInt_Negative()
    {
        var fp = FixedPoint.FromInt(-3);
        Assert.Equal(-3 * FixedPoint.Scale, fp.Raw);
    }

    [Fact]
    public void ToInt_TruncatesTowardNegativeInfinity()
    {
        // Right-shift arithmetic: truncates toward negative infinity
        Assert.Equal(5, FixedPoint.FromInt(5).ToInt());
        Assert.Equal(0, FixedPoint.FromFloat(0.9f).ToInt());
        // -1.5 >> 16 = -2 (arithmetic right shift truncates toward -inf)
        Assert.Equal(-2, FixedPoint.FromFloat(-1.5f).ToInt());
        Assert.Equal(-1, FixedPoint.FromInt(-1).ToInt());
    }

    [Fact]
    public void FromFloat_ToFloat_RoundTrips()
    {
        float[] values = { 0f, 1f, -1f, 0.5f, -0.5f, 100.25f, -99.75f };
        foreach (float v in values)
        {
            float result = FixedPoint.FromFloat(v).ToFloat();
            Assert.True(
                Math.Abs(result - v) < 0.001f,
                $"Round-trip failed for {v}: got {result}");
        }
    }

    [Fact]
    public void FromRaw_PreservesExactValue()
    {
        var fp = FixedPoint.FromRaw(12345);
        Assert.Equal(12345, fp.Raw);
    }

    // ── Constants ───────────────────────────────────────────────────────

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(0, FixedPoint.Zero.Raw);
        Assert.Equal(FixedPoint.Scale, FixedPoint.One.Raw);
        Assert.Equal(FixedPoint.Scale / 2, FixedPoint.Half.Raw);
        Assert.Equal(int.MinValue, FixedPoint.MinValue.Raw);
        Assert.Equal(int.MaxValue, FixedPoint.MaxValue.Raw);
    }

    // ── Addition ────────────────────────────────────────────────────────

    [Fact]
    public void Addition_BasicIntegers()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(4);
        Assert.Equal(FixedPoint.FromInt(7), a + b);
    }

    [Fact]
    public void Addition_WithFractions()
    {
        var a = FixedPoint.FromFloat(1.5f);
        var b = FixedPoint.FromFloat(2.25f);
        var result = a + b;
        Assert.True(
            Math.Abs(result.ToFloat() - 3.75f) < 0.001f,
            $"Expected ~3.75, got {result.ToFloat()}");
    }

    [Fact]
    public void Addition_IsCommutative()
    {
        var a = FixedPoint.FromFloat(3.7f);
        var b = FixedPoint.FromFloat(8.2f);
        Assert.Equal(a + b, b + a);
    }

    [Fact]
    public void Addition_ZeroIdentity()
    {
        var a = FixedPoint.FromFloat(42.5f);
        Assert.Equal(a, a + FixedPoint.Zero);
    }

    // ── Subtraction ─────────────────────────────────────────────────────

    [Fact]
    public void Subtraction_BasicIntegers()
    {
        var a = FixedPoint.FromInt(10);
        var b = FixedPoint.FromInt(3);
        Assert.Equal(FixedPoint.FromInt(7), a - b);
    }

    [Fact]
    public void Subtraction_ResultCanBeNegative()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(10);
        Assert.Equal(FixedPoint.FromInt(-7), a - b);
    }

    [Fact]
    public void UnaryNegation()
    {
        var a = FixedPoint.FromInt(5);
        Assert.Equal(FixedPoint.FromInt(-5), -a);
        Assert.Equal(a, -(-a));
    }

    // ── Multiplication ──────────────────────────────────────────────────

    [Fact]
    public void Multiplication_Integers()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(4);
        Assert.Equal(FixedPoint.FromInt(12), a * b);
    }

    [Fact]
    public void Multiplication_ByZero()
    {
        var a = FixedPoint.FromInt(99);
        Assert.Equal(FixedPoint.Zero, a * FixedPoint.Zero);
    }

    [Fact]
    public void Multiplication_ByOne()
    {
        var a = FixedPoint.FromFloat(42.5f);
        Assert.Equal(a, a * FixedPoint.One);
    }

    [Fact]
    public void Multiplication_Fractional()
    {
        var a = FixedPoint.FromFloat(2.5f);
        var b = FixedPoint.FromFloat(4.0f);
        var result = a * b;
        Assert.True(
            Math.Abs(result.ToFloat() - 10.0f) < 0.01f,
            $"Expected ~10.0, got {result.ToFloat()}");
    }

    [Fact]
    public void Multiplication_IntOverload()
    {
        var a = FixedPoint.FromFloat(3.5f);
        Assert.Equal((a * FixedPoint.FromInt(2)).Raw, (a * 2).Raw);
    }

    [Fact]
    public void Multiplication_IsCommutative()
    {
        var a = FixedPoint.FromFloat(3.7f);
        var b = FixedPoint.FromFloat(8.2f);
        Assert.Equal(a * b, b * a);
    }

    // ── Division ────────────────────────────────────────────────────────

    [Fact]
    public void Division_Integers()
    {
        var a = FixedPoint.FromInt(12);
        var b = FixedPoint.FromInt(4);
        Assert.Equal(FixedPoint.FromInt(3), a / b);
    }

    [Fact]
    public void Division_FractionalResult()
    {
        var a = FixedPoint.FromInt(1);
        var b = FixedPoint.FromInt(2);
        var result = a / b;
        Assert.True(
            Math.Abs(result.ToFloat() - 0.5f) < 0.001f,
            $"Expected ~0.5, got {result.ToFloat()}");
    }

    [Fact]
    public void Division_SelfEqualsOne()
    {
        var a = FixedPoint.FromFloat(7.3f);
        var result = a / a;
        Assert.True(
            Math.Abs(result.ToFloat() - 1.0f) < 0.001f,
            $"Expected ~1.0, got {result.ToFloat()}");
    }

    // ── Comparison ──────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameValues()
    {
        var a = FixedPoint.FromInt(5);
        var b = FixedPoint.FromInt(5);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Comparison_Ordering()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(5);
        Assert.True(a < b);
        Assert.True(a <= b);
        Assert.True(b > a);
        Assert.True(b >= a);
        Assert.False(a > b);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectOrdering()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(5);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(a));
    }

    // ── Math Functions ──────────────────────────────────────────────────

    [Fact]
    public void Abs_PositiveUnchanged()
    {
        var a = FixedPoint.FromInt(5);
        Assert.Equal(a, FixedPoint.Abs(a));
    }

    [Fact]
    public void Abs_NegativeBecomesPositive()
    {
        var a = FixedPoint.FromInt(-5);
        Assert.Equal(FixedPoint.FromInt(5), FixedPoint.Abs(a));
    }

    [Fact]
    public void Min_ReturnsSmaller()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(7);
        Assert.Equal(a, FixedPoint.Min(a, b));
        Assert.Equal(a, FixedPoint.Min(b, a));
    }

    [Fact]
    public void Max_ReturnsLarger()
    {
        var a = FixedPoint.FromInt(3);
        var b = FixedPoint.FromInt(7);
        Assert.Equal(b, FixedPoint.Max(a, b));
        Assert.Equal(b, FixedPoint.Max(b, a));
    }

    [Fact]
    public void Clamp_ValueWithinRange()
    {
        var val = FixedPoint.FromInt(5);
        var min = FixedPoint.FromInt(1);
        var max = FixedPoint.FromInt(10);
        Assert.Equal(val, FixedPoint.Clamp(val, min, max));
    }

    [Fact]
    public void Clamp_ValueBelowRange()
    {
        var val = FixedPoint.FromInt(-5);
        var min = FixedPoint.FromInt(1);
        var max = FixedPoint.FromInt(10);
        Assert.Equal(min, FixedPoint.Clamp(val, min, max));
    }

    [Fact]
    public void Clamp_ValueAboveRange()
    {
        var val = FixedPoint.FromInt(15);
        var min = FixedPoint.FromInt(1);
        var max = FixedPoint.FromInt(10);
        Assert.Equal(max, FixedPoint.Clamp(val, min, max));
    }

    [Fact]
    public void Sqrt_PerfectSquares()
    {
        float[] values = { 1, 4, 9, 16, 25, 100 };
        foreach (float v in values)
        {
            float result = FixedPoint.Sqrt(FixedPoint.FromFloat(v)).ToFloat();
            float expected = (float)Math.Sqrt(v);
            Assert.True(
                Math.Abs(result - expected) < 0.02f,
                $"Sqrt({v}): expected ~{expected}, got {result}");
        }
    }

    [Fact]
    public void Sqrt_Zero()
    {
        Assert.Equal(FixedPoint.Zero, FixedPoint.Sqrt(FixedPoint.Zero));
    }

    [Fact]
    public void Sqrt_Negative_ReturnsZero()
    {
        Assert.Equal(FixedPoint.Zero, FixedPoint.Sqrt(FixedPoint.FromInt(-1)));
    }

    [Fact]
    public void Sqrt_FractionalValues()
    {
        var fp = FixedPoint.FromFloat(2.0f);
        float result = FixedPoint.Sqrt(fp).ToFloat();
        Assert.True(
            Math.Abs(result - 1.414f) < 0.02f,
            $"Sqrt(2): expected ~1.414, got {result}");
    }

    // ── Determinism (critical for multiplayer) ──────────────────────────

    [Fact]
    public void Arithmetic_IsDeterministic_SameInputSameOutput()
    {
        // Run the same sequence twice and verify identical results
        var a = FixedPoint.FromFloat(3.14f);
        var b = FixedPoint.FromFloat(2.71f);

        var sum1 = a + b;
        var diff1 = a - b;
        var prod1 = a * b;
        var quot1 = a / b;

        var sum2 = a + b;
        var diff2 = a - b;
        var prod2 = a * b;
        var quot2 = a / b;

        Assert.Equal(sum1.Raw, sum2.Raw);
        Assert.Equal(diff1.Raw, diff2.Raw);
        Assert.Equal(prod1.Raw, prod2.Raw);
        Assert.Equal(quot1.Raw, quot2.Raw);
    }

    // ── GetHashCode & Equals ────────────────────────────────────────────

    [Fact]
    public void GetHashCode_SameForEqualValues()
    {
        var a = FixedPoint.FromInt(42);
        var b = FixedPoint.FromInt(42);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_Object_WorksForBoxed()
    {
        var a = FixedPoint.FromInt(5);
        object b = FixedPoint.FromInt(5);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_Object_FalseForNonFixedPoint()
    {
        var a = FixedPoint.FromInt(5);
        Assert.False(a.Equals("not a FixedPoint"));
        Assert.False(a.Equals(null));
    }
}
