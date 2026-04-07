using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════
// STEERING MANAGER — Deterministic steering behaviors for unit navigation
// ═══════════════════════════════════════════════════════════════════════════
//
// This system sits between pathfinding (which produces waypoints / flow
// fields) and the MovementSimulator (which applies physics). It answers
// the question: "Given my path and nearby units, which direction should
// I steer this tick?"
//
// Steering behaviors are combined with a priority-weighted system:
//
//   1. SEPARATION  (highest priority) — avoid overlapping neighbors
//   2. AVOIDANCE   — dodge obstacles / blocked cells ahead
//   3. PATH FOLLOW — steer toward next waypoint or follow flow field
//   4. ARRIVAL     — slow down when approaching final destination
//
// Each behavior returns a FixedVector2 "steering force." These are scaled
// by weights and summed. The result is normalized to produce the final
// DesiredDirection for MovementInput.
//
// DESIGN DECISIONS:
//   - All math is FixedPoint/FixedVector2 — deterministic across platforms.
//   - No allocations per tick (structs, pre-sized lists).
//   - Neighbor list is provided externally (spatial partitioning is the
//     caller's responsibility, not ours).
//   - FlowField and waypoint paths are both supported — group movement
//     uses flow fields, individual units use A* waypoints.
//   - We do NOT use Dictionary iteration — only indexed access on lists.
//
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Information about a nearby unit, used for separation and avoidance.
/// Provided by the spatial query system (e.g. grid-based neighbor lookup).
/// </summary>
public struct NearbyUnit
{
    /// <summary>World position of the neighboring unit.</summary>
    public FixedVector2 Position;

    /// <summary>Current velocity vector of the neighboring unit.</summary>
    public FixedVector2 Velocity;

    /// <summary>Collision radius of the neighboring unit.</summary>
    public FixedPoint Radius;
}

/// <summary>
/// All context needed to compute steering for one unit on one tick.
/// Passed by value to avoid heap allocation.
/// </summary>
public struct UnitSteeringContext
{
    /// <summary>Current world position of this unit.</summary>
    public FixedVector2 Position;

    /// <summary>Current velocity vector of this unit.</summary>
    public FixedVector2 Velocity;

    /// <summary>Collision radius of this unit.</summary>
    public FixedPoint Radius;

    /// <summary>
    /// A* waypoint path as (gridX, gridY) pairs, or null if using flow field.
    /// The first element is the start, last is the destination.
    /// </summary>
    public List<(int X, int Y)>? PathWaypoints;

    /// <summary>
    /// Flow field for group movement, or null if using waypoint path.
    /// Provides a direction vector at each grid cell.
    /// </summary>
    public FlowField? ActiveFlowField;

    /// <summary>
    /// Index into PathWaypoints of the waypoint we're currently heading toward.
    /// Updated by steering when we get close enough to advance.
    /// </summary>
    public int CurrentWaypointIndex;

    /// <summary>
    /// Nearby units within awareness radius, for separation/avoidance.
    /// Caller is responsible for spatial query — we just consume the list.
    /// </summary>
    public List<NearbyUnit> Neighbors;
}

/// <summary>
/// Result of steering computation. Includes the desired direction and an
/// updated waypoint index (which may have advanced if we reached a waypoint).
/// </summary>
public struct SteeringResult
{
    /// <summary>Normalized desired movement direction.</summary>
    public FixedVector2 DesiredDirection;

    /// <summary>Desired speed throttle [0, 1].</summary>
    public FixedPoint DesiredSpeed;

    /// <summary>
    /// Updated waypoint index — may have advanced if the unit passed
    /// through the current waypoint's acceptance radius.
    /// </summary>
    public int UpdatedWaypointIndex;

    /// <summary>True if the unit has reached its final destination.</summary>
    public bool HasArrived;
}

/// <summary>
/// Pure-static deterministic steering behaviors. No internal state — all
/// context flows through parameters. Thread-safe and side-effect-free.
/// </summary>
public static class SteeringManager
{
    // ── Tuning constants ────────────────────────────────────────────────

    /// <summary>
    /// Weight for separation behavior. Highest priority to prevent unit
    /// overlap / clumping.
    /// </summary>
    private static readonly FixedPoint SeparationWeight = FixedPoint.FromRaw(196608); // 3.0

    /// <summary>Weight for obstacle avoidance (look-ahead collision dodge).</summary>
    private static readonly FixedPoint AvoidanceWeight = FixedPoint.FromRaw(131072); // 2.0

    /// <summary>Weight for path-following (waypoint seek or flow field follow).</summary>
    private static readonly FixedPoint PathWeight = FixedPoint.FromRaw(65536); // 1.0

    /// <summary>Weight for arrival (slow down near destination). Applied additively.</summary>
    private static readonly FixedPoint ArrivalWeight = FixedPoint.FromRaw(98304); // 1.5

    /// <summary>
    /// Distance at which a waypoint is considered "reached" and the unit
    /// advances to the next one. In world units (Q16.16).
    /// Roughly 1.5 world units — generous enough for smooth pathing.
    /// </summary>
    private static readonly FixedPoint WaypointAcceptRadius = FixedPoint.FromRaw(98304); // 1.5

    /// <summary>Squared version for distance checks without sqrt.</summary>
    private static readonly FixedPoint WaypointAcceptRadiusSq =
        WaypointAcceptRadius * WaypointAcceptRadius;

    /// <summary>
    /// Distance from final destination at which arrival behavior begins
    /// decelerating. In world units (Q16.16). ~4.0 units.
    /// </summary>
    private static readonly FixedPoint ArrivalSlowdownRadius = FixedPoint.FromInt(4);

    /// <summary>Squared arrival radius for fast distance checks.</summary>
    private static readonly FixedPoint ArrivalSlowdownRadiusSq =
        ArrivalSlowdownRadius * ArrivalSlowdownRadius;

    /// <summary>
    /// Distance at which the unit considers itself "arrived" and stops.
    /// ~0.5 world units.
    /// </summary>
    private static readonly FixedPoint ArrivalStopRadius = FixedPoint.FromRaw(32768); // 0.5

    /// <summary>Squared arrival stop radius.</summary>
    private static readonly FixedPoint ArrivalStopRadiusSq =
        ArrivalStopRadius * ArrivalStopRadius;

    /// <summary>
    /// Look-ahead distance for avoidance, in multiples of the unit's speed.
    /// Higher = earlier reaction to obstacles.
    /// </summary>
    private static readonly FixedPoint AvoidanceLookaheadTicks = FixedPoint.FromInt(10);

    /// <summary>
    /// Size of the grid cell for converting waypoint (gridX, gridY) to world position.
    /// Must match the TerrainGrid cell size. Default = 1.0 world unit per cell.
    /// </summary>
    private static readonly FixedPoint GridCellSize = FixedPoint.One;

    /// <summary>Half cell offset for centering world position within a grid cell.</summary>
    private static readonly FixedPoint HalfCell = FixedPoint.Half;

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Computes the combined steering direction for a unit. Pure function.
    /// Returns both the desired direction and an updated waypoint index.
    /// </summary>
    public static SteeringResult ComputeSteering(UnitSteeringContext ctx)
    {
        var result = new SteeringResult
        {
            DesiredDirection = FixedVector2.Zero,
            DesiredSpeed = FixedPoint.One,
            UpdatedWaypointIndex = ctx.CurrentWaypointIndex,
            HasArrived = false
        };

        // ── Accumulate weighted steering forces ──

        FixedVector2 totalForce = FixedVector2.Zero;

        // 1. SEPARATION — always active, highest weight
        FixedVector2 separationForce = ComputeSeparation(ctx);
        totalForce = totalForce + separationForce * SeparationWeight;

        // 2. AVOIDANCE — active when moving
        if (ctx.Velocity.LengthSquared > FixedPoint.Zero)
        {
            FixedVector2 avoidanceForce = ComputeAvoidance(ctx);
            totalForce = totalForce + avoidanceForce * AvoidanceWeight;
        }

        // 3. PATH FOLLOWING — waypoint seek OR flow field follow
        FixedVector2 pathForce;
        int updatedWaypointIndex = ctx.CurrentWaypointIndex;

        if (ctx.ActiveFlowField != null)
        {
            pathForce = FollowFlowField(ctx);
        }
        else if (ctx.PathWaypoints != null && ctx.PathWaypoints.Count > 0)
        {
            pathForce = SeekWaypoint(ctx, out updatedWaypointIndex);
        }
        else
        {
            pathForce = FixedVector2.Zero;
        }

        totalForce = totalForce + pathForce * PathWeight;
        result.UpdatedWaypointIndex = updatedWaypointIndex;

        // 4. ARRIVAL — slow down near final destination
        FixedPoint arrivalThrottle = FixedPoint.One;
        bool hasDestination = false;
        FixedVector2 finalDest = FixedVector2.Zero;

        if (ctx.PathWaypoints != null && ctx.PathWaypoints.Count > 0)
        {
            hasDestination = true;
            var last = ctx.PathWaypoints[ctx.PathWaypoints.Count - 1];
            finalDest = GridToWorld(last.X, last.Y);
        }

        if (hasDestination)
        {
            FixedPoint distSqToFinal = ctx.Position.DistanceSquaredTo(finalDest);

            if (distSqToFinal <= ArrivalStopRadiusSq)
            {
                // Close enough — stop
                result.HasArrived = true;
                result.DesiredDirection = FixedVector2.Zero;
                result.DesiredSpeed = FixedPoint.Zero;
                return result;
            }

            if (distSqToFinal < ArrivalSlowdownRadiusSq)
            {
                // Within slowdown radius — compute arrival steering
                FixedVector2 arrivalForce = ComputeArrival(ctx, finalDest);
                totalForce = totalForce + arrivalForce * ArrivalWeight;

                // Proportional throttle: speed scales linearly with distance
                FixedPoint distToFinal = FixedPoint.Sqrt(distSqToFinal);
                arrivalThrottle = distToFinal / ArrivalSlowdownRadius;
                arrivalThrottle = FixedPoint.Clamp(arrivalThrottle, FixedPoint.FromRaw(6554), FixedPoint.One); // min 0.1
            }
        }

        // ── Combine and normalize ──
        if (totalForce.LengthSquared > FixedPoint.Zero)
        {
            result.DesiredDirection = totalForce.Normalized();
        }
        else
        {
            result.DesiredDirection = FixedVector2.Zero;
        }

        result.DesiredSpeed = arrivalThrottle;

        return result;
    }

    /// <summary>
    /// Convenience overload that returns just the desired direction vector.
    /// Use when you don't need the full SteeringResult.
    /// </summary>
    public static FixedVector2 ComputeSteeringDirection(UnitSteeringContext ctx)
    {
        return ComputeSteering(ctx).DesiredDirection;
    }

    // ── Individual Steering Behaviors ───────────────────────────────────

    /// <summary>
    /// SEEK WAYPOINT: Steer toward the next waypoint in the A* path.
    /// Advances the waypoint index when the unit enters the acceptance
    /// radius. Returns the direction toward the current target waypoint.
    /// </summary>
    private static FixedVector2 SeekWaypoint(UnitSteeringContext ctx, out int updatedIndex)
    {
        updatedIndex = ctx.CurrentWaypointIndex;

        if (ctx.PathWaypoints == null || ctx.PathWaypoints.Count == 0)
            return FixedVector2.Zero;

        // Clamp index
        if (updatedIndex >= ctx.PathWaypoints.Count)
        {
            updatedIndex = ctx.PathWaypoints.Count - 1;
            return FixedVector2.Zero;
        }

        // Get current target waypoint in world coordinates
        var wp = ctx.PathWaypoints[updatedIndex];
        FixedVector2 target = GridToWorld(wp.X, wp.Y);

        // Check if we're close enough to advance
        FixedPoint distSq = ctx.Position.DistanceSquaredTo(target);

        // Advance through waypoints we've already passed
        while (distSq <= WaypointAcceptRadiusSq && updatedIndex < ctx.PathWaypoints.Count - 1)
        {
            updatedIndex++;
            wp = ctx.PathWaypoints[updatedIndex];
            target = GridToWorld(wp.X, wp.Y);
            distSq = ctx.Position.DistanceSquaredTo(target);
        }

        // Steer toward the target waypoint
        FixedVector2 toTarget = target - ctx.Position;
        if (toTarget.LengthSquared > FixedPoint.Zero)
            return toTarget.Normalized();

        return FixedVector2.Zero;
    }

    /// <summary>
    /// FOLLOW FLOW FIELD: Read the direction vector from the flow field
    /// at the unit's current grid cell. Used for group movement — many
    /// units sharing one flow field produces natural formation behavior.
    /// </summary>
    private static FixedVector2 FollowFlowField(UnitSteeringContext ctx)
    {
        if (ctx.ActiveFlowField == null)
            return FixedVector2.Zero;

        // Convert world position to grid cell
        int cellX = WorldToGridX(ctx.Position.X);
        int cellY = WorldToGridY(ctx.Position.Y);

        // Query flow field direction vector at this cell
        FixedVector2 flowDir = ctx.ActiveFlowField.GetDirectionVector(cellX, cellY);
        return flowDir;
    }

    /// <summary>
    /// SEPARATION: Push away from nearby units to prevent overlap and
    /// clumping. Force is inversely proportional to distance — closer
    /// neighbors exert stronger repulsion. This is the most important
    /// steering behavior for natural-looking unit movement.
    /// </summary>
    private static FixedVector2 ComputeSeparation(UnitSteeringContext ctx)
    {
        if (ctx.Neighbors == null || ctx.Neighbors.Count == 0)
            return FixedVector2.Zero;

        FixedVector2 totalPush = FixedVector2.Zero;
        int pushCount = 0;

        for (int i = 0; i < ctx.Neighbors.Count; i++)
        {
            var neighbor = ctx.Neighbors[i];
            FixedVector2 away = ctx.Position - neighbor.Position;
            FixedPoint distSq = away.LengthSquared;

            // Skip if exactly on top (degenerate case — push in arbitrary deterministic direction)
            if (distSq == FixedPoint.Zero)
            {
                // Deterministic fallback: push in +X direction
                totalPush = totalPush + new FixedVector2(FixedPoint.One, FixedPoint.Zero);
                pushCount++;
                continue;
            }

            // Combined radii — units should be at least this far apart
            FixedPoint combinedRadius = ctx.Radius + neighbor.Radius;
            FixedPoint combinedRadiusSq = combinedRadius * combinedRadius;

            // Only separate if within combined radii (overlapping or very close)
            // Use a generous multiplier (2x) to start pushing before overlap
            FixedPoint awarenessRadiusSq = combinedRadiusSq * FixedPoint.FromInt(4);

            if (distSq < awarenessRadiusSq)
            {
                FixedPoint dist = FixedPoint.Sqrt(distSq);
                // Force inversely proportional to distance
                // Stronger when closer: force = (combinedRadius / dist) - 1
                // Clamped to prevent extreme forces
                FixedPoint strength = combinedRadius / FixedPoint.Max(dist, FixedPoint.FromRaw(655)); // min ~0.01
                strength = FixedPoint.Min(strength, FixedPoint.FromInt(3)); // cap at 3x

                FixedVector2 pushDir = away.Normalized();
                totalPush = totalPush + pushDir * strength;
                pushCount++;
            }
        }

        if (pushCount == 0)
            return FixedVector2.Zero;

        return totalPush;
    }

    /// <summary>
    /// AVOIDANCE: Look ahead along the unit's current velocity vector
    /// and steer away from neighbors that would cause a collision.
    /// This is predictive — it reacts to where units WILL be, not just
    /// where they are now.
    /// </summary>
    private static FixedVector2 ComputeAvoidance(UnitSteeringContext ctx)
    {
        if (ctx.Neighbors == null || ctx.Neighbors.Count == 0)
            return FixedVector2.Zero;

        FixedPoint speed = ctx.Velocity.Length;
        if (speed == FixedPoint.Zero)
            return FixedVector2.Zero;

        // Look-ahead: how far ahead to check (in world units)
        FixedPoint lookahead = speed * AvoidanceLookaheadTicks / FixedPoint.FromInt(GameManager.SimTickRate);
        FixedVector2 velocityNorm = ctx.Velocity.Normalized();

        FixedVector2 bestAvoidance = FixedVector2.Zero;
        FixedPoint closestThreatDist = FixedPoint.MaxValue;

        for (int i = 0; i < ctx.Neighbors.Count; i++)
        {
            var neighbor = ctx.Neighbors[i];

            // Relative position and velocity
            FixedVector2 relPos = neighbor.Position - ctx.Position;
            FixedVector2 relVel = ctx.Velocity - neighbor.Velocity;

            // Project relative position onto our velocity direction
            // (how far ahead is this neighbor along our path?)
            FixedPoint forwardDot = relPos.X * velocityNorm.X + relPos.Y * velocityNorm.Y;

            // Only care about neighbors ahead of us
            if (forwardDot <= FixedPoint.Zero || forwardDot > lookahead)
                continue;

            // Lateral distance: how close will we pass?
            // lateral² = |relPos|² - forwardDot²
            FixedPoint lateralSq = relPos.LengthSquared - (forwardDot * forwardDot);
            if (lateralSq < FixedPoint.Zero)
                lateralSq = FixedPoint.Zero; // numerical guard

            FixedPoint combinedRadius = ctx.Radius + neighbor.Radius;
            FixedPoint safeDistSq = combinedRadius * combinedRadius;

            // If we'll pass within combined radii, we need to avoid
            if (lateralSq < safeDistSq && forwardDot < closestThreatDist)
            {
                closestThreatDist = forwardDot;

                // Steer perpendicular to velocity, away from the neighbor
                // Perpendicular: rotate velocity 90° and pick the side away from neighbor
                FixedVector2 perpLeft = new FixedVector2(-velocityNorm.Y, velocityNorm.X);
                FixedVector2 perpRight = new FixedVector2(velocityNorm.Y, -velocityNorm.X);

                // Choose the perpendicular that pushes us away from the neighbor
                FixedPoint leftDot = perpLeft.X * relPos.X + perpLeft.Y * relPos.Y;
                // Negative dot = perpLeft points away from neighbor
                if (leftDot < FixedPoint.Zero)
                    bestAvoidance = perpLeft;
                else
                    bestAvoidance = perpRight;

                // Scale avoidance force by urgency (closer = stronger)
                FixedPoint urgency = (lookahead - forwardDot) / lookahead;
                urgency = FixedPoint.Clamp(urgency, FixedPoint.Zero, FixedPoint.One);
                bestAvoidance = bestAvoidance * urgency;
            }
        }

        return bestAvoidance;
    }

    /// <summary>
    /// ARRIVAL: When approaching the final destination, steer to decelerate
    /// smoothly. Returns a force that gently guides the unit toward the
    /// exact destination point while the throttle (handled by caller)
    /// reduces speed proportionally.
    /// </summary>
    private static FixedVector2 ComputeArrival(UnitSteeringContext ctx, FixedVector2 destination)
    {
        FixedVector2 toTarget = destination - ctx.Position;
        if (toTarget.LengthSquared == FixedPoint.Zero)
            return FixedVector2.Zero;

        // Steer directly toward the destination — the speed reduction
        // is handled via DesiredSpeed in the main ComputeSteering method.
        return toTarget.Normalized();
    }

    // ── Grid ↔ World Coordinate Helpers ─────────────────────────────────

    /// <summary>
    /// Converts a grid cell coordinate to a world position at the cell center.
    /// </summary>
    private static FixedVector2 GridToWorld(int gridX, int gridY)
    {
        return new FixedVector2(
            FixedPoint.FromInt(gridX) * GridCellSize + HalfCell,
            FixedPoint.FromInt(gridY) * GridCellSize + HalfCell
        );
    }

    /// <summary>Converts a world X coordinate to a grid cell X index.</summary>
    private static int WorldToGridX(FixedPoint worldX)
    {
        return (worldX / GridCellSize).ToInt();
    }

    /// <summary>Converts a world Y coordinate to a grid cell Y index.</summary>
    private static int WorldToGridY(FixedPoint worldY)
    {
        return (worldY / GridCellSize).ToInt();
    }
}
