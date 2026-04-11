using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for SteeringManager.ComputeSteering — deterministic steering behaviors.
/// Verifies no-path idle state, waypoint advance logic, arrival detection,
/// and separation force generation.
/// </summary>
public class SteeringManagerTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>Creates a minimal UnitSteeringContext with no path, no neighbors.</summary>
    private static UnitSteeringContext MakeContext(
        float posX = 0f,
        float posY = 0f,
        float velX = 0f,
        float velY = 0f,
        float radius = 0.5f,
        List<(int X, int Y)>? waypoints = null,
        List<NearbyUnit>? neighbors = null)
    {
        return new UnitSteeringContext
        {
            Position = new FixedVector2(
                FixedPoint.FromFloat(posX),
                FixedPoint.FromFloat(posY)),
            Velocity = new FixedVector2(
                FixedPoint.FromFloat(velX),
                FixedPoint.FromFloat(velY)),
            Radius = FixedPoint.FromFloat(radius),
            PathWaypoints = waypoints,
            ActiveFlowField = null,
            CurrentWaypointIndex = 0,
            Neighbors = neighbors ?? new List<NearbyUnit>()
        };
    }

    private static NearbyUnit MakeNeighbor(float posX, float posY, float radius = 0.5f)
    {
        return new NearbyUnit
        {
            Position = new FixedVector2(FixedPoint.FromFloat(posX), FixedPoint.FromFloat(posY)),
            Velocity = FixedVector2.Zero,
            Radius = FixedPoint.FromFloat(radius)
        };
    }

    // ── No path / idle ───────────────────────────────────────────────────

    [Fact]
    public void ComputeSteering_NullPath_NoNeighbors_DirectionIsZero()
    {
        var ctx = MakeContext();
        SteeringResult result = SteeringManager.ComputeSteering(ctx);
        Assert.Equal(FixedVector2.Zero, result.DesiredDirection);
    }

    [Fact]
    public void ComputeSteering_EmptyPath_NoNeighbors_DirectionIsZero()
    {
        var ctx = MakeContext(waypoints: new List<(int, int)>());
        SteeringResult result = SteeringManager.ComputeSteering(ctx);
        Assert.Equal(FixedVector2.Zero, result.DesiredDirection);
    }

    [Fact]
    public void ComputeSteering_NullPath_NoNeighbors_HasNotArrived()
    {
        var ctx = MakeContext();
        SteeringResult result = SteeringManager.ComputeSteering(ctx);
        Assert.False(result.HasArrived);
    }

    [Fact]
    public void ComputeSteering_NullPath_NoNeighbors_SpeedIsFullThrottle()
    {
        var ctx = MakeContext();
        SteeringResult result = SteeringManager.ComputeSteering(ctx);
        // With no path and no neighbors no throttle reduction occurs
        Assert.Equal(FixedPoint.One, result.DesiredSpeed);
    }

    // ── Waypoint seek ────────────────────────────────────────────────────

    [Fact]
    public void ComputeSteering_SingleWaypoint_DirectionPointsTowardTarget()
    {
        // Unit at origin, waypoint at grid (10, 0) → world (10.5, 0.5)
        var ctx = MakeContext(posX: 0f, posY: 0f,
            waypoints: new List<(int, int)> { (10, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // X component of direction must be positive (pointing right toward waypoint)
        Assert.True(result.DesiredDirection.X > FixedPoint.Zero,
            $"Expected positive X direction, got {result.DesiredDirection.X.ToFloat():F3}");
    }

    [Fact]
    public void ComputeSteering_WaypointNorth_DirectionPointsNorth()
    {
        // Unit at (5.5, 0) waypoint at grid (5, 10) → world (5.5, 10.5)
        var ctx = MakeContext(posX: 5.5f, posY: 0f,
            waypoints: new List<(int, int)> { (5, 10) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.True(result.DesiredDirection.Y > FixedPoint.Zero,
            $"Expected positive Y direction, got {result.DesiredDirection.Y.ToFloat():F3}");
    }

    [Fact]
    public void ComputeSteering_MultipleSingleTickAdvance_IndexUnchangedWhenFar()
    {
        // Unit at (0,0), waypoints at grid (20,0) then (40,0)
        var ctx = MakeContext(posX: 0f, posY: 0f,
            waypoints: new List<(int, int)> { (20, 0), (40, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // Still far from first waypoint — index should not advance
        Assert.Equal(0, result.UpdatedWaypointIndex);
    }

    [Fact]
    public void ComputeSteering_UnitAlreadyAtWaypoint_AdvancesToNext()
    {
        // WaypointAcceptRadius is ~1.5 world units.
        // Grid cell (0,0) → world center (0.5, 0.5).
        // Place unit at the center of grid cell (0,0) = exactly (0.5, 0.5).
        // First waypoint grid (0,0) = world (0.5, 0.5) — distance ≈ 0 → should advance.
        var ctx = MakeContext(posX: 0.5f, posY: 0.5f,
            waypoints: new List<(int, int)> { (0, 0), (20, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // Should have advanced past the first waypoint
        Assert.Equal(1, result.UpdatedWaypointIndex);
    }

    // ── Arrival detection ────────────────────────────────────────────────

    [Fact]
    public void ComputeSteering_UnitAtFinalDestination_HasArrived()
    {
        // ArrivalStopRadius is ~0.5 world units.
        // Place unit effectively on top of the final waypoint grid (0,0) = (0.5, 0.5).
        var ctx = MakeContext(posX: 0.5f, posY: 0.5f,
            waypoints: new List<(int, int)> { (0, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.True(result.HasArrived);
    }

    [Fact]
    public void ComputeSteering_UnitAtFinalDestination_DirectionIsZero()
    {
        var ctx = MakeContext(posX: 0.5f, posY: 0.5f,
            waypoints: new List<(int, int)> { (0, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.Equal(FixedVector2.Zero, result.DesiredDirection);
    }

    [Fact]
    public void ComputeSteering_UnitAtFinalDestination_SpeedIsZero()
    {
        var ctx = MakeContext(posX: 0.5f, posY: 0.5f,
            waypoints: new List<(int, int)> { (0, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.Equal(FixedPoint.Zero, result.DesiredSpeed);
    }

    [Fact]
    public void ComputeSteering_FarFromDestination_HasNotArrived()
    {
        var ctx = MakeContext(posX: 0f, posY: 0f,
            waypoints: new List<(int, int)> { (50, 50) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.False(result.HasArrived);
    }

    // ── Separation force ─────────────────────────────────────────────────

    [Fact]
    public void ComputeSteering_OverlappingNeighbor_DirectionPointsAway()
    {
        // Neighbor is directly to the right of the unit at a small distance
        // (within combined radii → separation triggers)
        var neighbors = new List<NearbyUnit>
        {
            MakeNeighbor(posX: 0.6f, posY: 0f, radius: 0.5f)
        };
        // Unit at origin, radius 0.5 — combined = 1.0, neighbor at 0.6 → overlapping
        var ctx = MakeContext(posX: 0f, posY: 0f, radius: 0.5f, neighbors: neighbors);

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // Should push unit away (toward negative X — away from neighbor at +X)
        Assert.True(result.DesiredDirection.X < FixedPoint.Zero,
            $"Expected negative X (push away from neighbor), got {result.DesiredDirection.X.ToFloat():F3}");
    }

    [Fact]
    public void ComputeSteering_DistantNeighbor_NoSeparationEffect()
    {
        // Neighbor far away — separation awareness radius is 2× combined radii.
        // Combined radii = 1.0, awareness = 4.0 (sq). Place neighbor at dist 10.
        var neighbors = new List<NearbyUnit>
        {
            MakeNeighbor(posX: 10f, posY: 0f, radius: 0.5f)
        };
        var ctx = MakeContext(posX: 0f, posY: 0f, radius: 0.5f, neighbors: neighbors);

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // No path + no close neighbors → direction should be zero
        Assert.Equal(FixedVector2.Zero, result.DesiredDirection);
    }

    [Fact]
    public void ComputeSteering_ExactlyOverlappingNeighbor_PushesInPlusXDirection()
    {
        // Degenerate case: unit and neighbor at exactly the same position.
        // SteeringManager falls back to +X push to maintain determinism.
        var neighbors = new List<NearbyUnit>
        {
            MakeNeighbor(posX: 0f, posY: 0f, radius: 0.5f)
        };
        var ctx = MakeContext(posX: 0f, posY: 0f, radius: 0.5f, neighbors: neighbors);

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        // Separation force pushes +X; SeparationWeight (3.0) > other forces
        Assert.True(result.DesiredDirection.X > FixedPoint.Zero,
            $"Expected +X fallback direction, got {result.DesiredDirection.X.ToFloat():F3}");
    }

    // ── ComputeSteeringDirection convenience overload ────────────────────

    [Fact]
    public void ComputeSteeringDirection_MatchesComputeSteering()
    {
        var ctx = MakeContext(posX: 0f, posY: 0f,
            waypoints: new List<(int, int)> { (10, 5) });

        FixedVector2 dir = SteeringManager.ComputeSteeringDirection(ctx);
        SteeringResult full = SteeringManager.ComputeSteering(ctx);

        Assert.Equal(full.DesiredDirection, dir);
    }

    // ── Waypointindex initialised correctly ──────────────────────────────

    [Fact]
    public void ComputeSteering_WaypointIndexPreservedWhenFar()
    {
        var ctx = MakeContext(posX: 0f, posY: 0f,
            waypoints: new List<(int, int)> { (100, 0) });

        SteeringResult result = SteeringManager.ComputeSteering(ctx);

        Assert.Equal(0, result.UpdatedWaypointIndex);
    }
}
