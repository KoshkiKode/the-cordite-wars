using System;
using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════
// MOVEMENT SIMULATOR — Deterministic per-tick movement physics
// ═══════════════════════════════════════════════════════════════════════════
//
// DESIGN PHILOSOPHY (inspired by C&C Generals / Zero Hour):
//
//   Every unit in Generals had distinctive movement "feel":
//   - Rocket Buggy: light, fast, bouncy — catches air over hills, drifts on turns
//   - SCUD Launcher: massive, slow, needs flat terrain — crawls up slopes
//   - Ranger (infantry): walks over almost anything, ignores small slopes
//   - Comanche (helicopter): ignores terrain entirely, flies at fixed altitude
//
//   All of these emerge from ONE physics system with different MovementProfile
//   values. The key insight: mass, suspension, speed, and terrain modifiers
//   create emergent vehicle personalities without special-casing.
//
//   This simulator advances a unit by exactly one simulation tick (1/30s at
//   SimTickRate=30). It is fully deterministic — all math uses FixedPoint
//   and FixedVector2, never float or double. This is CRITICAL for lockstep
//   networking: every client must produce bit-identical results.
//
// PHYSICS PIPELINE (per tick):
//   1. Turn facing toward desired direction (bounded by TurnRate)
//   2. Sample terrain at current position → slope, terrain type
//   3. Compute effective max speed (terrain modifier × slope penalty)
//   4. Accelerate or decelerate toward target speed (mass-adjusted)
//   5. Apply velocity in facing direction → new position
//   6. Track height: terrain-following + airborne detection for vehicles
//   7. Stuck detection: flag units that haven't moved in N ticks
//
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Snapshot of a unit's movement state at a given tick. Immutable struct —
/// the simulator returns a NEW state each tick rather than mutating in place,
/// which simplifies rollback/replay for lockstep networking.
/// </summary>
public struct MovementState
{
    /// <summary>Current 2D world position (X, Y on the simulation plane).</summary>
    public FixedVector2 Position;

    /// <summary>
    /// Current Z height. Normally equals terrain height at Position, but
    /// diverges when the unit is airborne (jumped off a crest).
    /// </summary>
    public FixedPoint Height;

    /// <summary>
    /// Vertical velocity component for airborne physics.
    /// Positive = rising, negative = falling.
    /// </summary>
    public FixedPoint VerticalVelocity;

    /// <summary>Current 2D movement velocity vector.</summary>
    public FixedVector2 Velocity;

    /// <summary>
    /// Current heading angle in fixed-point radians [0, 2π).
    /// 0 = +X axis, π/2 = +Y axis.
    /// </summary>
    public FixedPoint Facing;

    /// <summary>Current scalar speed (magnitude of Velocity).</summary>
    public FixedPoint Speed;

    /// <summary>
    /// True if the unit has left the ground. Light vehicles with high
    /// suspension catch air over crests; heavy vehicles stay planted.
    /// </summary>
    public bool IsAirborne;

    /// <summary>True if the unit cannot make meaningful progress.</summary>
    public bool IsStuck;

    /// <summary>
    /// Consecutive ticks without meaningful movement. Resets when the unit
    /// moves more than Epsilon. Used for stuck detection and AI fallback.
    /// </summary>
    public int StuckTicks;
}

/// <summary>
/// Input to the movement simulator for one tick. Produced by the steering
/// system or AI controller.
/// </summary>
public struct MovementInput
{
    /// <summary>
    /// Normalized direction the unit wants to move. Comes from pathfinding
    /// or steering. Zero vector means "no desired direction" (coast/stop).
    /// </summary>
    public FixedVector2 DesiredDirection;

    /// <summary>
    /// Throttle from 0 (idle) to 1 (full speed). Intermediate values let
    /// units approach destinations smoothly (arrival behavior).
    /// </summary>
    public FixedPoint DesiredSpeed;

    /// <summary>
    /// Emergency stop — overrides throttle and applies maximum deceleration.
    /// Used by StopCommand and collision avoidance.
    /// </summary>
    public bool Brake;
}

/// <summary>
/// Pure-static deterministic movement physics. No state — all context flows
/// through parameters. Thread-safe and side-effect-free.
/// </summary>
public static class MovementSimulator
{
    // ── Fixed-point math constants ──────────────────────────────────────

    /// <summary>π in Q16.16 fixed-point (raw ≈ 205887).</summary>
    public static readonly FixedPoint Pi = FixedPoint.FromRaw(205887);

    /// <summary>2π in Q16.16 fixed-point.</summary>
    public static readonly FixedPoint TwoPi = FixedPoint.FromRaw(411775);

    /// <summary>π/2 in Q16.16 fixed-point.</summary>
    private static readonly FixedPoint HalfPi = FixedPoint.FromRaw(102944);

    /// <summary>
    /// Minimum movement per tick to not be considered "stuck."
    /// ~0.001 world units in Q16.16.
    /// </summary>
    private static readonly FixedPoint StuckEpsilonSq = FixedPoint.FromRaw(4); // (0.0001)^2-ish in raw squared

    /// <summary>Number of consecutive stuck ticks before flagging IsStuck.</summary>
    private const int StuckThresholdTicks = 15; // half a second at 30 Hz

    /// <summary>
    /// Default gravity in world units per second². Divided by SimTickRate
    /// each tick. ~9.8 m/s² equivalent.
    /// </summary>
    private static readonly FixedPoint Gravity = FixedPoint.FromRaw(642252); // ≈ 9.8 in Q16.16

    /// <summary>Sim tick rate cached as FixedPoint for per-tick division.</summary>
    private static readonly FixedPoint TickRate = FixedPoint.FromInt(GameManager.SimTickRate);

    /// <summary>Max downhill speed bonus: 20% over base max speed.</summary>
    private static readonly FixedPoint DownhillSpeedCap = FixedPoint.FromRaw(78643); // 1.2

    /// <summary>Minimum slope penalty — don't let uphill reduce speed below 20%.</summary>
    private static readonly FixedPoint MinSlopePenalty = FixedPoint.FromRaw(13107); // 0.2

    /// <summary>
    /// Slope effect strength. Controls how much uphill/downhill affects speed.
    /// Higher = more dramatic terrain influence.
    /// </summary>
    private static readonly FixedPoint SlopeStrength = FixedPoint.FromInt(2);

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Advances movement by exactly one simulation tick. Pure function —
    /// returns a new MovementState without mutating the input.
    /// </summary>
    /// <param name="current">The unit's state at the start of this tick.</param>
    /// <param name="input">Steering/AI input for this tick.</param>
    /// <param name="profile">The unit type's movement characteristics.</param>
    /// <param name="terrain">The terrain grid for height/slope/type queries.</param>
    /// <returns>The unit's state at the end of this tick.</returns>
    public static MovementState AdvanceTick(
        MovementState current,
        MovementInput input,
        MovementProfile profile,
        TerrainGrid terrain)
    {
        var next = current;

        // ────────────────────────────────────────────────────────────
        // STEP 1: TURN TOWARD DESIRED DIRECTION
        // ────────────────────────────────────────────────────────────
        // Rotate Facing toward the angle of DesiredDirection, limited
        // by TurnRate per tick. Fast units with low turn rates (trucks)
        // have wide turning circles; infantry snap instantly.
        // ────────────────────────────────────────────────────────────

        if (input.DesiredDirection != FixedVector2.Zero)
        {
            FixedPoint desiredAngle = Atan2(input.DesiredDirection.Y, input.DesiredDirection.X);
            desiredAngle = NormalizeAngle(desiredAngle);

            FixedPoint currentAngle = NormalizeAngle(current.Facing);
            FixedPoint delta = NormalizeAngleDelta(desiredAngle - currentAngle);

            // TurnRate is radians per second; divide by tick rate for per-tick limit
            FixedPoint maxTurn = profile.TurnRate / TickRate;

            if (FixedPoint.Abs(delta) <= maxTurn)
            {
                next.Facing = desiredAngle;
            }
            else
            {
                // Turn in the shorter direction
                if (delta > FixedPoint.Zero)
                    next.Facing = NormalizeAngle(currentAngle + maxTurn);
                else
                    next.Facing = NormalizeAngle(currentAngle - maxTurn);
            }
        }

        // ────────────────────────────────────────────────────────────
        // STEP 2: TERRAIN EFFECTS
        // ────────────────────────────────────────────────────────────
        // Query slope and terrain type at the current position.
        // Slope affects max speed: uphill penalizes, downhill gives
        // a modest bonus (capped to prevent runaway acceleration).
        // Terrain type (road, sand, mud, water) applies a modifier
        // from the unit's MovementProfile.
        // ────────────────────────────────────────────────────────────

        FixedVector2 slopeVec = terrain.GetSlope(current.Position);
        FixedPoint slopeX = slopeVec.X;
        FixedPoint slopeY = slopeVec.Y;
        var terrainType = terrain.GetTerrainType(current.Position);

        // Terrain type speed modifier (road = fast, mud = slow, etc.)
        // TerrainSpeedModifiers is IReadOnlyDictionary — keyed lookup
        // is deterministic (same key always yields same value).
        FixedPoint terrainModifier = FixedPoint.One;
        if (profile.TerrainSpeedModifiers != null)
        {
            if (profile.TerrainSpeedModifiers.TryGetValue(terrainType, out FixedPoint modifier))
            {
                terrainModifier = modifier;
            }
        }

        // Compute slope penalty along the unit's facing direction.
        // Dot product of (slopeX, slopeY) with unit's facing direction:
        //   positive = uphill, negative = downhill
        FixedPoint faceDirX = FixedCos(next.Facing);
        FixedPoint faceDirY = FixedSin(next.Facing);
        FixedPoint slopeAlongFacing = slopeX * faceDirX + slopeY * faceDirY;

        // slopePenalty: 1.0 on flat, <1.0 uphill, >1.0 (capped) downhill
        FixedPoint slopePenalty = FixedPoint.One - (slopeAlongFacing * SlopeStrength);
        slopePenalty = FixedPoint.Clamp(slopePenalty, MinSlopePenalty, DownhillSpeedCap);

        // Check max slope angle — if terrain is steeper than the unit can handle,
        // the unit cannot proceed (tanks can't climb cliffs).
        FixedPoint slopeMagnitudeSq = slopeX * slopeX + slopeY * slopeY;
        FixedPoint maxSlopeSq = profile.MaxSlopeAngle * profile.MaxSlopeAngle;
        bool slopeTooSteep = slopeMagnitudeSq > maxSlopeSq;

        FixedPoint effectiveMaxSpeed;
        if (slopeTooSteep)
        {
            // Can't climb — effective max speed is zero
            effectiveMaxSpeed = FixedPoint.Zero;
        }
        else
        {
            effectiveMaxSpeed = profile.MaxSpeed * terrainModifier * slopePenalty;
            // Never go below zero
            effectiveMaxSpeed = FixedPoint.Max(effectiveMaxSpeed, FixedPoint.Zero);
        }

        // ────────────────────────────────────────────────────────────
        // STEP 3: ACCELERATION / DECELERATION
        // ────────────────────────────────────────────────────────────
        // Mass-adjusted acceleration: F=ma → a = F/m
        // Heavier units (SCUD Launcher) accelerate/brake slower.
        // Lighter units (Rocket Buggy) are nimble.
        // ────────────────────────────────────────────────────────────

        FixedPoint targetSpeed;
        if (input.Brake)
        {
            targetSpeed = FixedPoint.Zero;
        }
        else
        {
            targetSpeed = effectiveMaxSpeed * input.DesiredSpeed;
        }

        // Mass-adjusted rates (per tick). Higher mass = slower response.
        // Mass is expected to be >= 1.0; we divide accel by mass.
        FixedPoint effectiveMass = FixedPoint.Max(profile.Mass, FixedPoint.One);
        FixedPoint accelPerTick = (profile.Acceleration / effectiveMass) / TickRate;
        FixedPoint decelPerTick = (profile.Deceleration / effectiveMass) / TickRate;

        // Braking uses double deceleration for responsive stops
        if (input.Brake)
        {
            decelPerTick = decelPerTick * FixedPoint.FromInt(2);
        }

        if (next.Speed < targetSpeed)
        {
            // Accelerate
            next.Speed = next.Speed + accelPerTick;
            if (next.Speed > targetSpeed)
                next.Speed = targetSpeed;
        }
        else if (next.Speed > targetSpeed)
        {
            // Decelerate
            next.Speed = next.Speed - decelPerTick;
            if (next.Speed < targetSpeed)
                next.Speed = targetSpeed;
        }

        // Clamp speed to non-negative
        next.Speed = FixedPoint.Max(next.Speed, FixedPoint.Zero);

        // ────────────────────────────────────────────────────────────
        // STEP 4: APPLY VELOCITY
        // ────────────────────────────────────────────────────────────
        // Move along the facing direction at current speed.
        // Velocity vector = facing direction * speed / tickRate
        // ────────────────────────────────────────────────────────────

        FixedPoint dx = FixedCos(next.Facing) * next.Speed / TickRate;
        FixedPoint dy = FixedSin(next.Facing) * next.Speed / TickRate;
        next.Velocity = new FixedVector2(dx, dy);

        FixedVector2 newPosition = current.Position + next.Velocity;
        next.Position = newPosition;

        // ────────────────────────────────────────────────────────────
        // STEP 5: HEIGHT TRACKING & AIRBORNE PHYSICS
        // ────────────────────────────────────────────────────────────
        // This is where the Rocket Buggy magic happens.
        //
        // Ground vehicles track terrain height via "suspension."
        // When a fast, light vehicle crests a hill, the terrain drops
        // away faster than gravity pulls the unit down → airborne.
        //
        // Key factors:
        //   - SuspensionStiffness: how tightly the unit follows terrain
        //     (high = planted like a tank, low = bouncy like a buggy)
        //   - Mass: heavier units resist becoming airborne
        //   - Speed: faster units launch farther off crests
        //   - GravityMultiplier: per-unit gravity tuning
        //
        // Helicopters/aircraft: SuspensionStiffness = 0, meaning they
        // ignore terrain height entirely and fly at a fixed altitude.
        // ────────────────────────────────────────────────────────────

        FixedPoint terrainHeightAtNew = terrain.GetHeight(next.Position);

        if (profile.SuspensionStiffness == FixedPoint.Zero)
        {
            // ── Aircraft / Helicopter ──
            // Ignore terrain, maintain profile-defined height.
            // Height is set externally or stays constant.
            // No airborne state — they're always "flying."
            next.Height = current.Height;
            next.VerticalVelocity = FixedPoint.Zero;
            next.IsAirborne = false;
        }
        else if (current.IsAirborne)
        {
            // ── Currently airborne — apply gravity ──
            FixedPoint gravityPerTick = Gravity * profile.GravityMultiplier / TickRate;
            next.VerticalVelocity = current.VerticalVelocity - gravityPerTick;
            next.Height = current.Height + (next.VerticalVelocity / TickRate);

            // Check for landing
            if (next.Height <= terrainHeightAtNew)
            {
                next.Height = terrainHeightAtNew;
                next.VerticalVelocity = FixedPoint.Zero;
                next.IsAirborne = false;
            }
        }
        else
        {
            // ── Ground vehicle / infantry — terrain following ──
            // Compare where gravity would place us vs where terrain is.
            //
            // expectedHeight: where the unit "wants" to be based on
            // suspension (smoothly follows terrain at stiffness rate).
            // If terrain drops away and the unit's expected height is
            // significantly above the terrain → launch into air.

            FixedPoint previousTerrainHeight = terrain.GetHeight(current.Position);
            FixedPoint terrainDelta = terrainHeightAtNew - previousTerrainHeight;

            // Suspension interpolation: stiff = follows terrain exactly,
            // soft = lags behind terrain changes.
            // suspensionFollow = terrainDelta * (stiffness / maxStiffness)
            // We normalize stiffness assuming 1.0 = perfectly rigid.
            FixedPoint stiffnessFactor = FixedPoint.Min(profile.SuspensionStiffness, FixedPoint.One);
            FixedPoint heightFollowDelta = terrainDelta * stiffnessFactor;
            FixedPoint expectedHeight = current.Height + heightFollowDelta;

            // If terrain drops away (negative delta) and the unit is moving
            // fast enough, the unit may become airborne.
            // Airborne condition: expected height > terrain height by threshold,
            // AND unit has enough speed to "launch."
            //
            // The launch threshold is inversely proportional to speed and
            // directly proportional to mass — fast light units launch easily,
            // slow heavy units stay grounded.
            FixedPoint heightGap = expectedHeight - terrainHeightAtNew;

            // Launch threshold: heavier units need a bigger gap to go airborne.
            // Base threshold scaled by mass, inversely by speed.
            // A rocket buggy (mass=0.5, speed=high) launches easily.
            // A SCUD launcher (mass=5.0, speed=low) almost never launches.
            FixedPoint speedFactor = FixedPoint.Max(next.Speed, FixedPoint.One);
            FixedPoint launchThreshold = (effectiveMass / speedFactor) / TickRate;
            // Ensure a minimum threshold so units don't launch from tiny bumps
            FixedPoint minLaunchThreshold = FixedPoint.FromRaw(3277); // ~0.05
            launchThreshold = FixedPoint.Max(launchThreshold, minLaunchThreshold);

            if (heightGap > launchThreshold && terrainDelta < FixedPoint.Zero)
            {
                // AIRBORNE! Unit launched off a crest.
                next.IsAirborne = true;
                next.Height = expectedHeight;
                // Initial vertical velocity: proportional to speed and
                // inversely to mass. Fast light vehicles get more air.
                // Use the terrain slope as the "ramp angle."
                FixedPoint launchSpeed = next.Speed * (FixedPoint.One - stiffnessFactor);
                launchSpeed = launchSpeed / effectiveMass;
                next.VerticalVelocity = launchSpeed;
            }
            else
            {
                // Normal terrain following — snap to terrain height
                next.Height = terrainHeightAtNew;
                next.VerticalVelocity = FixedPoint.Zero;
                next.IsAirborne = false;
            }
        }

        // ────────────────────────────────────────────────────────────
        // STEP 6: STUCK DETECTION
        // ────────────────────────────────────────────────────────────
        // If the unit hasn't moved more than Epsilon over several ticks,
        // flag it as stuck. The AI/steering layer can use this to request
        // a repath or give up.
        // ────────────────────────────────────────────────────────────

        FixedPoint movedSq = current.Position.DistanceSquaredTo(next.Position);
        if (movedSq <= StuckEpsilonSq && input.DesiredSpeed > FixedPoint.Zero && !input.Brake)
        {
            next.StuckTicks = current.StuckTicks + 1;
        }
        else
        {
            next.StuckTicks = 0;
        }

        next.IsStuck = next.StuckTicks >= StuckThresholdTicks;

        return next;
    }

    // ═══════════════════════════════════════════════════════════════════
    // FIXED-POINT TRIGONOMETRY
    // ═══════════════════════════════════════════════════════════════════
    //
    // These use polynomial approximations (no floats, no lookup tables
    // with non-deterministic indexing). All results are in Q16.16.
    //
    // We use a 5th-order Taylor/minimax-style approximation for sin,
    // and derive cos and atan2 from it.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fixed-point sine approximation. Input in fixed-point radians.
    /// Uses a polynomial approximation over [-π, π].
    /// Accurate to ~0.001 which is sufficient for movement physics.
    /// </summary>
    public static FixedPoint FixedSin(FixedPoint angle)
    {
        // Normalize to [-π, π]
        angle = NormalizeAngleSigned(angle);

        // Bhaskara I-inspired rational approximation adapted for fixed-point:
        // sin(x) ≈ x * (π - |x|) / (π² / 4 + |x| * (π - |x|) * someConstant)
        //
        // But for simplicity and determinism, we use a 3rd-order polynomial
        // that's accurate enough for game physics:
        //   sin(x) ≈ x - x³/6 (first two terms of Taylor series)
        //   with a correction term for better accuracy away from 0.

        // Compute x² and x³ using raw math to avoid overflow
        FixedPoint x = angle;
        FixedPoint x2 = x * x;
        FixedPoint x3 = x2 * x;
        FixedPoint x5 = x3 * x2;

        // sin(x) ≈ x - x³/6 + x⁵/120
        // 1/6 in Q16.16 ≈ 10923
        // 1/120 in Q16.16 ≈ 546
        FixedPoint term1 = x;
        FixedPoint term2 = x3 * FixedPoint.FromRaw(10923); // x³ * (1/6)
        FixedPoint term3 = x5 * FixedPoint.FromRaw(546);   // x⁵ * (1/120)

        FixedPoint result = term1 - term2 + term3;

        // Clamp to [-1, 1] to handle accumulated error
        return FixedPoint.Clamp(result, -FixedPoint.One, FixedPoint.One);
    }

    /// <summary>
    /// Fixed-point cosine. cos(x) = sin(x + π/2).
    /// </summary>
    public static FixedPoint FixedCos(FixedPoint angle)
    {
        return FixedSin(angle + HalfPi);
    }

    /// <summary>
    /// Fixed-point atan2 approximation. Returns angle in fixed-point
    /// radians in range [0, 2π). Uses the CORDIC-free rational approximation.
    /// </summary>
    public static FixedPoint Atan2(FixedPoint y, FixedPoint x)
    {
        // Handle special cases
        if (x == FixedPoint.Zero && y == FixedPoint.Zero)
            return FixedPoint.Zero;

        FixedPoint absX = FixedPoint.Abs(x);
        FixedPoint absY = FixedPoint.Abs(y);

        // Compute atan(min/max) using a polynomial approximation
        // atan(t) ≈ t - t³/3 for small t (|t| <= 1)
        // We ensure |t| <= 1 by using min/max
        FixedPoint minVal, maxVal;
        bool swapped;
        if (absX >= absY)
        {
            minVal = absY;
            maxVal = absX;
            swapped = false;
        }
        else
        {
            minVal = absX;
            maxVal = absY;
            swapped = true;
        }

        // t = min / max, guaranteed |t| <= 1
        FixedPoint t;
        if (maxVal == FixedPoint.Zero)
            t = FixedPoint.Zero;
        else
            t = minVal / maxVal;

        FixedPoint t2 = t * t;
        FixedPoint t3 = t2 * t;

        // atan(t) ≈ t * (1 - t²/3) — simplified for speed
        // More accurate: atan(t) ≈ t - t³ * 0.3333
        // 1/3 in Q16.16 = 21845
        FixedPoint atanResult = t - t3 * FixedPoint.FromRaw(21845);

        // If we swapped, result = π/2 - atan
        if (swapped)
        {
            atanResult = HalfPi - atanResult;
        }

        // Map to correct quadrant
        if (x.Raw < 0 && y.Raw >= 0)
            atanResult = Pi - atanResult;
        else if (x.Raw < 0 && y.Raw < 0)
            atanResult = Pi + atanResult;
        else if (x.Raw >= 0 && y.Raw < 0)
            atanResult = TwoPi - atanResult;

        return NormalizeAngle(atanResult);
    }

    /// <summary>
    /// Normalizes an angle to [0, 2π).
    /// </summary>
    public static FixedPoint NormalizeAngle(FixedPoint angle)
    {
        // Use modular arithmetic on raw values for determinism
        while (angle.Raw < 0)
            angle = angle + TwoPi;
        while (angle.Raw >= TwoPi.Raw)
            angle = angle - TwoPi;
        return angle;
    }

    /// <summary>
    /// Normalizes an angle to (-π, π].
    /// </summary>
    private static FixedPoint NormalizeAngleSigned(FixedPoint angle)
    {
        while (angle.Raw > Pi.Raw)
            angle = angle - TwoPi;
        while (angle.Raw < -Pi.Raw)
            angle = angle + TwoPi;
        return angle;
    }

    /// <summary>
    /// Normalizes an angle delta to (-π, π] — the shortest rotation.
    /// </summary>
    private static FixedPoint NormalizeAngleDelta(FixedPoint delta)
    {
        return NormalizeAngleSigned(delta);
    }
}
