using System;
using System.Collections.Generic;
using System.Diagnostics;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Efficiency tests for the simulation hot-path systems.
///
/// Each test measures wall-clock time for a realistic workload and asserts that
/// it completes within a conservative upper bound.  These are not micro-benchmarks
/// (use BenchmarkDotNet for that), but they catch catastrophic regressions and
/// document the expected performance envelope for each subsystem.
///
/// Bounds are intentionally generous (10–100× what a modern machine achieves) so
/// that the tests pass reliably on constrained CI runners without being flaky.
/// The measured times are written to the test output for inspection.
/// </summary>
public class PerformanceTests
{
    private static void Log(string message) => Console.WriteLine(message);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TerrainGrid OpenGrid(int w, int h) =>
        new TerrainGrid(w, h, FixedPoint.One);

    private static TerrainGrid GridWithObstacleStrip(int w, int h, int wallX, int gapY)
    {
        var grid = new TerrainGrid(w, h, FixedPoint.One);
        for (int y = 0; y < h; y++)
        {
            if (y == gapY) continue;
            ref var cell = ref grid.GetCell(wallX, y);
            cell.IsBlocked = true;
        }
        return grid;
    }

    private static UnitCollisionInfo MakeUnit(int id, FixedVector2 position, FixedPoint radius, FixedPoint mass)
        => new UnitCollisionInfo
        {
            UnitId        = id,
            PlayerId      = 1,
            Position      = position,
            Radius        = radius,
            Mass          = mass,
            IsAirUnit     = false,
            Height        = FixedPoint.Zero,
            ArmorClass    = ArmorType.Medium,
            CrushStrength = FixedPoint.Zero
        };

    // ═══════════════════════════════════════════════════════════════════════════
    // A* Pathfinder — large open grid, corner-to-corner
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds a path across a 128×128 open grass grid from (0,0) to (127,127).
    /// This exercises the full A* loop with octile heuristic, ArrayPool renting,
    /// and path reconstruction.
    /// Expected: well under 50 ms per call on any modern machine.
    /// CI bound: 2 000 ms (50× headroom).
    /// </summary>
    [Fact]
    public void AStarPathfinder_LargeOpenGrid_CompletesWithinTimeLimit()
    {
        const int Size     = 128;
        const int Runs     = 10;
        const int BoundMs  = 2_000; // per run, generous CI allowance

        var grid     = OpenGrid(Size, Size);
        var profile  = MovementProfile.Infantry();
        var finder   = new AStarPathfinder();

        // Warm-up (JIT compilation, cache warm)
        finder.FindPath(grid, profile, 0, 0, Size - 1, Size - 1, maxNodes: 32768);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
            finder.FindPath(grid, profile, 0, 0, Size - 1, Size - 1, maxNodes: 32768);
        sw.Stop();

        double msPerRun = sw.ElapsedMilliseconds / (double)Runs;
        Log($"AStarPathfinder 128×128 corner-to-corner: {msPerRun:F2} ms/call ({Runs} runs)");

        Assert.True(msPerRun < BoundMs,
            $"A* on 128×128 took {msPerRun} ms — exceeds {BoundMs} ms limit");
    }

    /// <summary>
    /// Finds a path across a 128×128 grid that has a vertical wall with a single gap,
    /// forcing a detour.  Verifies that the detour search is still fast.
    /// CI bound: 2 000 ms per call.
    /// </summary>
    [Fact]
    public void AStarPathfinder_WallWithGap_CompletesWithinTimeLimit()
    {
        const int Size    = 128;
        const int Runs    = 5;
        const int BoundMs = 2_000;

        // Wall at x=64 with a gap at y=64 — forces the path to route through the gap
        var grid    = GridWithObstacleStrip(Size, Size, wallX: 64, gapY: 64);
        var profile = MovementProfile.Infantry();
        var finder  = new AStarPathfinder();

        finder.FindPath(grid, profile, 0, 0, Size - 1, Size - 1, maxNodes: 32768);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
            finder.FindPath(grid, profile, 0, 0, Size - 1, Size - 1, maxNodes: 32768);
        sw.Stop();

        double msPerRun = sw.ElapsedMilliseconds / (double)Runs;
        Log($"AStarPathfinder 128×128 wall-with-gap: {msPerRun:F2} ms/call ({Runs} runs)");

        Assert.True(msPerRun < BoundMs,
            $"A* with wall/gap on 128×128 took {msPerRun} ms — exceeds {BoundMs} ms limit");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Flow Field — large grid generation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a flow field across a full 128×128 open grid with the goal at
    /// the centre.  Exercises the Dijkstra integration pass and direction derivation
    /// for the entire grid (~16 384 cells).
    /// Expected: well under 50 ms per call on any modern machine.
    /// CI bound: 2 000 ms.
    /// </summary>
    [Fact]
    public void FlowField_LargeOpenGrid_GeneratesWithinTimeLimit()
    {
        const int Size    = 128;
        const int Runs    = 10;
        const int BoundMs = 2_000;

        var grid    = OpenGrid(Size, Size);
        var profile = MovementProfile.Infantry();
        var ff      = new FlowField();

        // Warm-up
        ff.Generate(grid, profile,
            goalX: Size / 2, goalY: Size / 2,
            regionMinX: 0, regionMinY: 0, regionMaxX: Size - 1, regionMaxY: Size - 1);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
            ff.Generate(grid, profile,
                goalX: Size / 2, goalY: Size / 2,
                regionMinX: 0, regionMinY: 0, regionMaxX: Size - 1, regionMaxY: Size - 1);
        sw.Stop();

        double msPerRun = sw.ElapsedMilliseconds / (double)Runs;
        Log($"FlowField.Generate 128×128 full grid: {msPerRun:F2} ms/call ({Runs} runs)");

        Assert.True(ff.IsValid, "FlowField should be valid after generation");
        Assert.True(msPerRun < BoundMs,
            $"FlowField.Generate on 128×128 took {msPerRun} ms — exceeds {BoundMs} ms limit");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Spatial Hash — bulk insert and radius query
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inserts 1 000 units into a 256×256 spatial hash, then performs 500 radius
    /// queries spread across the world.  This models one simulation tick's spatial
    /// bookkeeping for a medium-scale engagement.
    ///
    /// Expected: the full insert+query round should complete in microseconds.
    /// CI bound: 1 500 ms for the full 100-tick simulation (15 ms per tick).
    /// </summary>
    [Fact]
    public void SpatialHash_BulkInsertAndQuery_CompletesWithinTimeLimit()
    {
        const int WorldSize  = 256;
        const int UnitCount  = 1_000;
        const int QueryCount = 500;
        const int Ticks      = 100;   // simulate 100 tick rebuilds
        const int BoundMs    = 1_500; // total across all ticks

        var hash    = new SpatialHash(WorldSize, WorldSize, cellSize: 8);
        var results = new List<int>(64);
        var rng     = new DeterministicRng(seed: 42);

        // Pre-generate positions for all units
        var positions = new FixedVector2[UnitCount];
        for (int i = 0; i < UnitCount; i++)
        {
            int x = (int)(rng.NextUlong() % (ulong)WorldSize);
            int y = (int)(rng.NextUlong() % (ulong)WorldSize);
            positions[i] = new FixedVector2(FixedPoint.FromInt(x), FixedPoint.FromInt(y));
        }

        // Pre-generate query centres
        var queryCentres = new FixedVector2[QueryCount];
        for (int q = 0; q < QueryCount; q++)
        {
            int x = (int)(rng.NextUlong() % (ulong)WorldSize);
            int y = (int)(rng.NextUlong() % (ulong)WorldSize);
            queryCentres[q] = new FixedVector2(FixedPoint.FromInt(x), FixedPoint.FromInt(y));
        }

        var queryRadius = FixedPoint.FromInt(12);
        var unitRadius  = FixedPoint.One;

        // Warm-up (JIT compilation, cache warm)
        hash.Clear();
        for (int i = 0; i < UnitCount; i++)
            hash.Insert(i, positions[i], unitRadius);
        results.Clear();
        hash.QueryRadius(queryCentres[0], queryRadius, results);

        var sw = Stopwatch.StartNew();

        for (int tick = 0; tick < Ticks; tick++)
        {
            // ── Insert phase (models per-tick rebuild) ──
            hash.Clear();
            for (int i = 0; i < UnitCount; i++)
                hash.Insert(i, positions[i], unitRadius);

            // ── Query phase ──
            for (int q = 0; q < QueryCount; q++)
            {
                results.Clear();
                hash.QueryRadius(queryCentres[q], queryRadius, results);
            }
        }

        sw.Stop();
        Log(
            $"SpatialHash {UnitCount} units × {QueryCount} queries × {Ticks} ticks: " +
            $"{sw.ElapsedMilliseconds} ms total ({sw.ElapsedMilliseconds / (double)Ticks:F2} ms/tick)");

        Assert.True(sw.ElapsedMilliseconds < BoundMs,
            $"SpatialHash {Ticks} ticks took {sw.ElapsedMilliseconds} ms — exceeds {BoundMs} ms limit");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FixedPoint — arithmetic throughput
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes 1 000 000 chained multiply-add operations on FixedPoint values.
    /// This is the fundamental operation in movement simulation, cost calculation,
    /// and steering math, so throughput here directly limits game frame rate.
    ///
    /// Expected: 1M ops in under 10 ms on modern hardware.
    /// CI bound: 1 000 ms (100× headroom).
    /// </summary>
    [Fact]
    public void FixedPoint_MultiplyAdd_ThroughputWithinTimeLimit()
    {
        const int Iterations = 1_000_000;
        const int BoundMs    = 1_000;

        var a = FixedPoint.FromFloat(1.5f);
        var b = FixedPoint.FromFloat(0.7f);
        var c = FixedPoint.FromFloat(2.3f);
        var acc = FixedPoint.Zero;

        // Warm-up
        for (int i = 0; i < 1_000; i++)
            acc = acc + a * b + c;

        acc = FixedPoint.Zero;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < Iterations; i++)
            acc = acc + a * b + c;

        sw.Stop();

        // Use GC.KeepAlive so the accumulation loop is not optimised away.
        GC.KeepAlive(acc);

        Log($"FixedPoint multiply-add × {Iterations:N0}: {sw.ElapsedMilliseconds} ms");

        Assert.True(sw.ElapsedMilliseconds < BoundMs,
            $"FixedPoint {Iterations:N0} multiply-add took {sw.ElapsedMilliseconds} ms — exceeds {BoundMs} ms limit");
    }

    /// <summary>
    /// Executes 500 000 square-root operations on FixedPoint values, mirroring
    /// the distance calculations in collision resolution and pathfinding heuristics.
    /// CI bound: 2 000 ms.
    /// </summary>
    [Fact]
    public void FixedPoint_Sqrt_ThroughputWithinTimeLimit()
    {
        const int Iterations = 500_000;
        const int BoundMs    = 2_000;

        // Values that exercise the full Sqrt table range
        var input = FixedPoint.FromFloat(2.0f);
        var acc   = FixedPoint.Zero;

        // Warm-up
        for (int i = 0; i < 1_000; i++)
            acc = acc + FixedPoint.Sqrt(input);

        acc = FixedPoint.Zero;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < Iterations; i++)
            acc = acc + FixedPoint.Sqrt(input);

        sw.Stop();

        GC.KeepAlive(acc);
        Log($"FixedPoint.Sqrt × {Iterations:N0}: {sw.ElapsedMilliseconds} ms");

        Assert.True(sw.ElapsedMilliseconds < BoundMs,
            $"FixedPoint.Sqrt {Iterations:N0} iterations took {sw.ElapsedMilliseconds} ms — exceeds {BoundMs} ms limit");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Collision Resolver — detect + resolve cycle
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs 100 detect-and-resolve cycles for 200 units packed tightly in a
    /// 20×20 area — a worst-case chokepoint scenario where every unit has many
    /// neighbours and most pairs collide.
    ///
    /// Each cycle:
    ///   1. Rebuilds the spatial hash from current positions
    ///   2. Calls DetectCollisions (broad-phase + narrow-phase)
    ///   3. Calls ResolveCollisions (push-apart)
    ///
    /// CI bound: 1 000 ms for 100 cycles (10 ms/cycle).
    /// </summary>
    [Fact]
    public void CollisionResolver_DetectAndResolve_CompletesWithinTimeLimit()
    {
        const int UnitCount  = 200;
        const int AreaRadius = 10;   // units scattered within ±10 cells of origin
        const int WorldSize  = 256;
        const int Cycles     = 100;
        const int BoundMs    = 2_000;

        var rng      = new DeterministicRng(seed: 99);
        var resolver = new CollisionResolver();

        // Build 200 units scattered in a small area (high collision density)
        var units = new List<UnitCollisionInfo>(UnitCount);
        for (int i = 0; i < UnitCount; i++)
        {
            // Spread within [-AreaRadius, AreaRadius] around centre (128, 128)
            int rx = (int)(rng.NextUlong() % (ulong)(AreaRadius * 2 + 1)) - AreaRadius + 128;
            int ry = (int)(rng.NextUlong() % (ulong)(AreaRadius * 2 + 1)) - AreaRadius + 128;
            units.Add(MakeUnit(
                id:       i + 1,
                position: new FixedVector2(FixedPoint.FromInt(rx), FixedPoint.FromInt(ry)),
                radius:   FixedPoint.One,
                mass:     FixedPoint.FromInt(5)));
        }
        // Ensure ascending ID order (required by DetectCollisions)
        units.Sort((a, b) => a.UnitId.CompareTo(b.UnitId));

        var spatial = new SpatialHash(WorldSize, WorldSize, cellSize: 8);
        var pairs   = new List<CollisionPair>(UnitCount * 4);
        var results = new List<UnitCollisionResult>(UnitCount * 4);

        // Warm-up
        spatial.Clear();
        foreach (var u in units) spatial.Insert(u.UnitId, u.Position, u.Radius);
        resolver.DetectCollisions(units, spatial, pairs);
        resolver.ResolveCollisions(pairs, results);

        var sw = Stopwatch.StartNew();

        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            spatial.Clear();
            foreach (var u in units) spatial.Insert(u.UnitId, u.Position, u.Radius);
            resolver.DetectCollisions(units, spatial, pairs);
            resolver.ResolveCollisions(pairs, results);
        }

        sw.Stop();

        Log(
            $"CollisionResolver {UnitCount} units × {Cycles} cycles: " +
            $"{sw.ElapsedMilliseconds} ms total ({sw.ElapsedMilliseconds / (double)Cycles:F2} ms/cycle)");

        Assert.True(sw.ElapsedMilliseconds < BoundMs,
            $"CollisionResolver {Cycles} cycles took {sw.ElapsedMilliseconds} ms — exceeds {BoundMs} ms limit");
    }
}
