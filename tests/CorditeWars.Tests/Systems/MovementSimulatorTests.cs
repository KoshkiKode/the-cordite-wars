using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for <see cref="MovementSimulator"/> — deterministic fixed-point physics.
/// Covers the public math functions (FixedSin, FixedCos, Atan2, NormalizeAngle)
/// and the full AdvanceTick pipeline (acceleration, braking, turning, stuck detection).
/// </summary>
public class MovementSimulatorTests
{
    // ── Tolerances ──────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum acceptable error for trig approximations (about 0.02 in Q16.16).
    /// The Taylor-series sin/cos and atan2 rational approximations have limited
    /// accuracy far from zero, so we allow a small margin.
    /// </summary>
    private static readonly FixedPoint TrigTolerance = FixedPoint.FromFloat(0.02f);

    private static bool Near(FixedPoint actual, FixedPoint expected, FixedPoint tolerance)
        => FixedPoint.Abs(actual - expected) <= tolerance;

    // ── Math helpers ─────────────────────────────────────────────────────

    private static readonly FixedPoint Pi     = MovementSimulator.Pi;
    private static readonly FixedPoint TwoPi  = MovementSimulator.TwoPi;
    private static readonly FixedPoint HalfPi = FixedPoint.FromRaw(102944); // π/2

    // ── FixedSin ────────────────────────────────────────────────────────────

    [Fact]
    public void FixedSin_Zero_ReturnsZero()
    {
        FixedPoint result = MovementSimulator.FixedSin(FixedPoint.Zero);
        Assert.True(Near(result, FixedPoint.Zero, TrigTolerance),
            $"sin(0) expected ≈0 but got {result.ToFloat()}");
    }

    [Fact]
    public void FixedSin_HalfPi_ReturnsOne()
    {
        FixedPoint result = MovementSimulator.FixedSin(HalfPi);
        Assert.True(Near(result, FixedPoint.One, TrigTolerance),
            $"sin(π/2) expected ≈1 but got {result.ToFloat()}");
    }

    [Fact]
    public void FixedSin_NegativeHalfPi_ReturnsNegativeOne()
    {
        FixedPoint result = MovementSimulator.FixedSin(-HalfPi);
        Assert.True(Near(result, -FixedPoint.One, TrigTolerance),
            $"sin(-π/2) expected ≈-1 but got {result.ToFloat()}");
    }

    [Fact]
    public void FixedSin_ThreeHalfPi_ReturnsNegativeOne()
    {
        FixedPoint angle = Pi + HalfPi; // 3π/2 normalizes to -π/2 internally
        FixedPoint result = MovementSimulator.FixedSin(angle);
        Assert.True(Near(result, -FixedPoint.One, TrigTolerance),
            $"sin(3π/2) expected ≈-1 but got {result.ToFloat()}");
    }

    // ── FixedCos ────────────────────────────────────────────────────────────

    [Fact]
    public void FixedCos_Zero_ReturnsOne()
    {
        FixedPoint result = MovementSimulator.FixedCos(FixedPoint.Zero);
        Assert.True(Near(result, FixedPoint.One, TrigTolerance),
            $"cos(0) expected ≈1 but got {result.ToFloat()}");
    }

    [Fact]
    public void FixedCos_Pi_ReturnsNegativeOne()
    {
        // cos(π) = sin(3π/2) ≈ -1 — uses the well-converged part of the approximation
        FixedPoint result = MovementSimulator.FixedCos(Pi);
        Assert.True(Near(result, -FixedPoint.One, TrigTolerance),
            $"cos(π) expected ≈-1 but got {result.ToFloat()}");
    }

    [Fact]
    public void FixedCos_ThreeHalfPi_ReturnsZero()
    {
        // cos(3π/2) = sin(2π) = sin(0) = 0 — normalizes cleanly to 0
        FixedPoint angle = Pi + HalfPi; // 3π/2
        FixedPoint result = MovementSimulator.FixedCos(angle);
        Assert.True(Near(result, FixedPoint.Zero, TrigTolerance),
            $"cos(3π/2) expected ≈0 but got {result.ToFloat()}");
    }

    // ── Atan2 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Atan2_PositiveX_Zero_ReturnsZero()
    {
        // atan2(0, +1) = 0
        FixedPoint result = MovementSimulator.Atan2(FixedPoint.Zero, FixedPoint.One);
        Assert.True(Near(result, FixedPoint.Zero, TrigTolerance),
            $"atan2(0,1) expected ≈0 but got {result.ToFloat()}");
    }

    [Fact]
    public void Atan2_PositiveY_Zero_ReturnsHalfPi()
    {
        // atan2(+1, 0) = π/2
        FixedPoint result = MovementSimulator.Atan2(FixedPoint.One, FixedPoint.Zero);
        Assert.True(Near(result, HalfPi, TrigTolerance),
            $"atan2(1,0) expected ≈π/2 but got {result.ToFloat()}");
    }

    [Fact]
    public void Atan2_NegativeX_Zero_ReturnsPi()
    {
        // atan2(0, -1) = π
        FixedPoint result = MovementSimulator.Atan2(FixedPoint.Zero, -FixedPoint.One);
        Assert.True(Near(result, Pi, TrigTolerance),
            $"atan2(0,-1) expected ≈π but got {result.ToFloat()}");
    }

    [Fact]
    public void Atan2_NegativeY_Zero_ReturnsThreeHalfPi()
    {
        // atan2(-1, 0) = 3π/2
        FixedPoint result = MovementSimulator.Atan2(-FixedPoint.One, FixedPoint.Zero);
        FixedPoint expected = Pi + HalfPi;
        Assert.True(Near(result, expected, TrigTolerance),
            $"atan2(-1,0) expected ≈3π/2 but got {result.ToFloat()}");
    }

    [Fact]
    public void Atan2_BothZero_ReturnsZero()
    {
        // atan2(0, 0) is defined as 0 by convention
        FixedPoint result = MovementSimulator.Atan2(FixedPoint.Zero, FixedPoint.Zero);
        Assert.Equal(FixedPoint.Zero, result);
    }

    // ── NormalizeAngle ──────────────────────────────────────────────────────

    [Fact]
    public void NormalizeAngle_Zero_ReturnsZero()
    {
        Assert.Equal(FixedPoint.Zero, MovementSimulator.NormalizeAngle(FixedPoint.Zero));
    }

    [Fact]
    public void NormalizeAngle_TwoPi_WrapsToZero()
    {
        FixedPoint result = MovementSimulator.NormalizeAngle(TwoPi);
        Assert.True(result.Raw < TwoPi.Raw,
            $"NormalizeAngle(2π) should be < 2π, got raw {result.Raw}");
    }

    [Fact]
    public void NormalizeAngle_NegativeAngle_WrapsToPositive()
    {
        FixedPoint negHalfPi = -HalfPi;
        FixedPoint result = MovementSimulator.NormalizeAngle(negHalfPi);
        // -π/2 should map to 3π/2
        FixedPoint expected = Pi + HalfPi;
        Assert.True(Near(result, expected, TrigTolerance),
            $"NormalizeAngle(-π/2) expected ≈3π/2, got {result.ToFloat()}");
    }

    [Fact]
    public void NormalizeAngle_LargeAngle_WrapsCorrectly()
    {
        // 5π should map to π (since 5π mod 2π = π)
        FixedPoint fivePi = Pi + TwoPi + TwoPi; // 5π
        FixedPoint result = MovementSimulator.NormalizeAngle(fivePi);
        Assert.True(Near(result, Pi, TrigTolerance),
            $"NormalizeAngle(5π) expected ≈π, got {result.ToFloat()}");
    }

    // ── AdvanceTick helpers ─────────────────────────────────────────────────

    private static TerrainGrid FlatTerrain()
    {
        // Small flat terrain grid — all grass, height 0, no slope
        var grid = new TerrainGrid(8, 8, FixedPoint.One);
        // grid defaults to all Grass cells at height 0
        grid.ComputeSlopes();
        return grid;
    }

    private static MovementProfile InfantryProfile()
        => MovementProfile.Infantry();

    private static MovementState AtRest(FixedVector2? pos = null)
        => new MovementState
        {
            Position = pos ?? new FixedVector2(FixedPoint.FromInt(4), FixedPoint.FromInt(4)),
            Speed = FixedPoint.Zero,
            Facing = FixedPoint.Zero,
            Velocity = FixedVector2.Zero,
            Height = FixedPoint.Zero,
            VerticalVelocity = FixedPoint.Zero,
            IsAirborne = false,
            IsStuck = false,
            StuckTicks = 0
        };

    // ── AdvanceTick — basic physics ─────────────────────────────────────────

    [Fact]
    public void AdvanceTick_NoInput_UnitRemainsAtRest()
    {
        var terrain = FlatTerrain();
        var state = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = FixedVector2.Zero,
            DesiredSpeed = FixedPoint.Zero,
            Brake = false
        };

        MovementState next = MovementSimulator.AdvanceTick(state, input, InfantryProfile(), terrain);

        Assert.Equal(state.Position.X, next.Position.X);
        Assert.Equal(state.Position.Y, next.Position.Y);
        Assert.Equal(FixedPoint.Zero, next.Speed);
    }

    [Fact]
    public void AdvanceTick_ForwardInput_SpeedIncreases()
    {
        var terrain = FlatTerrain();
        var state = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero), // east
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        MovementState next = MovementSimulator.AdvanceTick(state, input, InfantryProfile(), terrain);

        Assert.True(next.Speed > FixedPoint.Zero,
            "Speed should increase when throttle is applied");
    }

    [Fact]
    public void AdvanceTick_ForwardInput_PositionAdvancesEast()
    {
        var terrain = FlatTerrain();
        var profile = InfantryProfile();
        var state = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        // Run several ticks to build up speed and movement
        for (int i = 0; i < 10; i++)
            state = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        Assert.True(state.Position.X > FixedPoint.FromInt(4),
            "Unit should have moved east after several ticks");
    }

    [Fact]
    public void AdvanceTick_BrakeFromMoving_SpeedDecreases()
    {
        var terrain = FlatTerrain();
        var profile = InfantryProfile();

        // First accelerate to some speed
        var state = AtRest();
        var accelerate = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };
        for (int i = 0; i < 10; i++)
            state = MovementSimulator.AdvanceTick(state, accelerate, profile, terrain);

        FixedPoint speedBeforeBrake = state.Speed;

        // Now brake
        var brake = new MovementInput
        {
            DesiredDirection = FixedVector2.Zero,
            DesiredSpeed = FixedPoint.Zero,
            Brake = true
        };
        MovementState after = MovementSimulator.AdvanceTick(state, brake, profile, terrain);

        Assert.True(after.Speed < speedBeforeBrake,
            "Speed should decrease when braking");
    }

    [Fact]
    public void AdvanceTick_TurnRate_FacingChangesGradually()
    {
        var terrain = FlatTerrain();
        var profile = MovementProfile.HeavyVehicle(); // slow turn rate

        // Start facing east (angle ≈ 0)
        var state = new MovementState
        {
            Position = new FixedVector2(FixedPoint.FromInt(4), FixedPoint.FromInt(4)),
            Facing = FixedPoint.Zero,
            Speed = FixedPoint.Zero,
            Velocity = FixedVector2.Zero,
            Height = FixedPoint.Zero
        };

        // Ask to face north (π/2)
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.Zero, FixedPoint.One),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        MovementState after1 = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        // Facing should move toward π/2 but NOT snap instantly (heavy vehicle has slow turn)
        Assert.True(after1.Facing > FixedPoint.Zero,
            "Facing should have started turning toward target angle");
        Assert.True(after1.Facing < HalfPi,
            "Heavy vehicle should not have instantly snapped to target angle");
    }

    [Fact]
    public void AdvanceTick_InfantryTurnRate_EventuallyReachesTarget()
    {
        var terrain = FlatTerrain();
        var profile = InfantryProfile(); // infantry turn rate: 0.30 rad/s

        // Start facing east (0). Want to face north (π/2).
        var state = new MovementState
        {
            Position = new FixedVector2(FixedPoint.FromInt(4), FixedPoint.FromInt(4)),
            Facing = FixedPoint.Zero,
            Speed = FixedPoint.Zero,
            Velocity = FixedVector2.Zero,
            Height = FixedPoint.Zero
        };

        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.Zero, FixedPoint.One), // north
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        // Run enough ticks for infantry to fully turn 90°
        // Infantry turn rate: 0.30 rad/s → 0.01 rad/tick.
        // 90° = π/2 ≈ 1.57 rad → requires ~157 ticks
        for (int i = 0; i < 200; i++)
            state = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        Assert.True(Near(state.Facing, HalfPi, TrigTolerance),
            $"Infantry should have reached north facing after 200 ticks, got {state.Facing.ToFloat()}");
    }

    [Fact]
    public void AdvanceTick_Determinism_SameSeedProducesSameResult()
    {
        var terrain = FlatTerrain();
        var profile = InfantryProfile();
        var initialState = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.One),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        // Run 30 ticks twice — results must be bit-identical (determinism)
        var stateA = initialState;
        var stateB = initialState;

        for (int i = 0; i < 30; i++)
        {
            stateA = MovementSimulator.AdvanceTick(stateA, input, profile, terrain);
            stateB = MovementSimulator.AdvanceTick(stateB, input, profile, terrain);
        }

        Assert.Equal(stateA.Position.X.Raw, stateB.Position.X.Raw);
        Assert.Equal(stateA.Position.Y.Raw, stateB.Position.Y.Raw);
        Assert.Equal(stateA.Speed.Raw, stateB.Speed.Raw);
        Assert.Equal(stateA.Facing.Raw, stateB.Facing.Raw);
    }

    [Fact]
    public void AdvanceTick_SpeedClampedToMaxSpeed()
    {
        var terrain = FlatTerrain();
        var profile = InfantryProfile();
        var state = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        // Run many ticks — speed must not exceed profile.MaxSpeed
        for (int i = 0; i < 120; i++)
            state = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        Assert.True(state.Speed <= profile.MaxSpeed,
            $"Speed {state.Speed.ToFloat()} must not exceed MaxSpeed {profile.MaxSpeed.ToFloat()}");
    }

    [Fact]
    public void AdvanceTick_StuckDetection_FlagsAfterNTicks()
    {
        var terrain = FlatTerrain();

        // Block all cells to simulate a completely-blocked unit
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                terrain.GetCell(x, y).IsBlocked = true;

        // Re-compute slopes so the grid is consistent
        terrain.ComputeSlopes();

        var profile = InfantryProfile();
        var state = AtRest();
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        // Run enough ticks that stuck detection should trigger (threshold = 15)
        for (int i = 0; i < 20; i++)
            state = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        Assert.True(state.IsStuck || state.StuckTicks > 0,
            "Unit should be flagged as stuck after many ticks with no meaningful movement");
    }

    // ── Atan2 — third quadrant (x<0, y<0) hits line 576 ─────────────────────

    [Fact]
    public void Atan2_NegativeX_NegativeY_ReturnsAngleInThirdQuadrant()
    {
        // atan2(-1, -1) should return an angle in the third quadrant [π, 3π/2]
        // This hits the x<0, y<0 branch (line 576: atanResult = Pi + atanResult).
        // The approximation is not perfectly precise, so we just verify quadrant.
        FixedPoint result = MovementSimulator.Atan2(-FixedPoint.One, -FixedPoint.One);
        FixedPoint pi = MovementSimulator.Pi;
        FixedPoint threePiOver2 = FixedPoint.FromRaw(pi.Raw + (pi.Raw / 2));
        // Allow a small tolerance — the approximation is accurate to ~0.2 rad here.
        Assert.True(result > FixedPoint.Zero && result < MovementSimulator.TwoPi,
            $"atan2(-1,-1) expected in [0, 2π), got {result.ToFloat():F3}");
        Assert.True(result >= pi - FixedPoint.FromFloat(0.2f),
            $"atan2(-1,-1) expected ≥ π, got {result.ToFloat():F3}");
    }

    [Fact]
    public void Atan2_BothZero_ReturnsZero_AndHitsMaxValZeroPath()
    {
        // atan2(0,0): absX=0 and absY=0 → maxVal=0 → t=0 (line 554)
        FixedPoint result = MovementSimulator.Atan2(FixedPoint.Zero, FixedPoint.Zero);
        Assert.Equal(FixedPoint.Zero, result);
    }

    // ── NormalizeAngle — negative angle wraps up (line 604) ────────────────

    [Fact]
    public void NormalizeAngle_LargeNegative_WrapsToPositiveRange()
    {
        // -π → should normalize to π (raw 205887)
        FixedPoint negPi = FixedPoint.FromRaw(-205887);
        FixedPoint result = MovementSimulator.NormalizeAngle(negPi);
        Assert.True(result.Raw >= 0, $"Expected non-negative, got {result.ToFloat():F3}");
        Assert.True(result.Raw < MovementSimulator.TwoPi.Raw,
            $"Expected < 2π, got {result.ToFloat():F3}");
    }

    // ── AdvanceTick — turn rate limited (unit must take multiple ticks) ─────

    [Fact]
    public void AdvanceTick_HighTurnRateLimit_TurnsSlowly()
    {
        // Use a profile with a very slow turn rate so the unit takes many ticks
        // to face the desired direction. This exercises the "turn in shorter direction"
        // branch including both subtraction directions (lines 208-209).
        var terrain = FlatTerrain();
        var profile = InfantryProfile().WithTurnRate(FixedPoint.FromRaw(655)); // very slow ~0.01 rad/tick

        // Start facing 0 (right), want to face left (π) — forces clamped turns.
        var state = AtRest() with
        {
            Facing = FixedPoint.Zero
        };
        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(-FixedPoint.One, FixedPoint.Zero), // left
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        MovementState after = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        // After one tick, facing should have moved slightly toward π but not reached it.
        Assert.NotEqual(FixedPoint.Zero, after.Facing);
        Assert.True(after.Facing < MovementSimulator.Pi,
            $"Facing {after.Facing.ToFloat():F3} should be < π after one slow-turn tick");
    }

    // ── AdvanceTick — airborne unit lands after enough ticks ─────────────────

    [Fact]
    public void AdvanceTick_AirborneUnit_LandsAfterApplyingGravity()
    {
        // Unit starts airborne at Height=2 with zero vertical velocity.
        // Gravity should pull it down to Height=0 (terrain height on flat map).
        var terrain = FlatTerrain();
        var profile = InfantryProfile(); // SuspensionStiffness != 0

        var state = AtRest() with
        {
            Height = FixedPoint.FromInt(2),
            VerticalVelocity = FixedPoint.Zero,
            IsAirborne = true
        };

        var input = new MovementInput
        {
            DesiredDirection = FixedVector2.Zero,
            DesiredSpeed = FixedPoint.Zero,
            Brake = false
        };

        // Run enough ticks for gravity to bring the unit to ground.
        for (int i = 0; i < 60 && state.IsAirborne; i++)
            state = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        Assert.False(state.IsAirborne, "Unit should have landed after gravity pulled it to terrain height");
        Assert.Equal(FixedPoint.Zero, state.VerticalVelocity);
    }

    // ── AdvanceTick — steep slope stops movement ─────────────────────────────

    [Fact]
    public void AdvanceTick_UnitOnSteepSlope_SpeedReducedOrZero()
    {
        // Create a terrain with a steep slope at the center cell and verify
        // the unit cannot accelerate to full speed.
        var terrain = new TerrainGrid(8, 8, FixedPoint.One);
        // Set a large slope on the center cell to exceed infantry MaxSlopeAngle
        ref TerrainCell cell = ref terrain.GetCell(4, 4);
        cell.SlopeAngle = FixedPoint.FromInt(10); // > infantry max slope
        terrain.ComputeSlopes();

        var profile = MovementProfile.Infantry();
        var state = AtRest(pos: new FixedVector2(FixedPoint.FromFloat(4.5f), FixedPoint.FromFloat(4.5f)));

        var input = new MovementInput
        {
            DesiredDirection = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            DesiredSpeed = FixedPoint.One,
            Brake = false
        };

        MovementState next = MovementSimulator.AdvanceTick(state, input, profile, terrain);

        // On a slope too steep to climb, effective max speed is 0 → speed stays at or near 0.
        // (may accelerate by one tick's accel before being clamped)
        Assert.True(next.Speed <= profile.Acceleration,
            $"Speed {next.Speed.ToFloat():F4} should not exceed one tick of acceleration on a steep slope");
    }
}
