using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for PathRequestManager — the budgeted, deterministic pathfinding
/// dispatcher that handles per-tick budgets and auto-batching of individual
/// A* requests into shared FlowFields when ≥ 5 units share a destination.
/// </summary>
public class PathRequestManagerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Creates an open, fully traversable grass grid.</summary>
    private static TerrainGrid OpenGrid(int w = 32, int h = 32)
        => new TerrainGrid(w, h, FixedPoint.One);

    /// <summary>Convenience: world-space position from integer grid coordinates.</summary>
    private static FixedVector2 V(int x, int y)
        => new FixedVector2(FixedPoint.FromInt(x), FixedPoint.FromInt(y));

    // ═══════════════════════════════════════════════════════════════════
    // PendingCount
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PendingCount_InitiallyZero()
    {
        var mgr = new PathRequestManager();
        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void RequestPath_IncreasesPendingCount()
    {
        var mgr = new PathRequestManager();
        var profile = MovementProfile.Infantry();

        mgr.RequestPath(1, profile, V(0, 0), V(5, 5), _ => { });

        Assert.Equal(1, mgr.PendingCount);
    }

    [Fact]
    public void RequestPath_Multiple_PendingCountMatchesEnqueued()
    {
        var mgr = new PathRequestManager();
        var profile = MovementProfile.Infantry();

        for (int i = 0; i < 4; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i + 1, 0), _ => { });

        Assert.Equal(4, mgr.PendingCount);
    }

    [Fact]
    public void RequestFlowField_IncreasesPendingCount()
    {
        var mgr = new PathRequestManager();
        var profile = MovementProfile.Infantry();

        mgr.RequestFlowField(profile, V(10, 10), new List<int> { 1, 2 }, _ => { });

        Assert.Equal(1, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ProcessRequests — Empty Queue
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_EmptyQueue_DoesNotThrow()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();

        mgr.ProcessRequests(grid); // should not throw
        Assert.Equal(0, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ProcessRequests — Single A* Path
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_SinglePath_CallbackInvoked()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        List<(int, int)>? received = null;

        mgr.RequestPath(1, profile, V(0, 0), V(5, 5), path => received = path);
        mgr.ProcessRequests(grid);

        Assert.NotNull(received);
    }

    [Fact]
    public void ProcessRequests_SinglePath_QueueCleared()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();

        mgr.RequestPath(1, profile, V(0, 0), V(5, 5), _ => { });
        mgr.ProcessRequests(grid);

        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_SinglePath_ReturnsNonEmptyPathOnOpenGrid()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        List<(int, int)>? path = null;

        mgr.RequestPath(1, profile, V(0, 0), V(4, 4), p => path = p);
        mgr.ProcessRequests(grid);

        Assert.NotNull(path);
        Assert.NotEmpty(path!);
    }

    [Fact]
    public void ProcessRequests_StartEqualsGoal_ReturnsSingleCellPath()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        List<(int, int)>? path = null;

        mgr.RequestPath(1, profile, V(3, 3), V(3, 3), p => path = p);
        mgr.ProcessRequests(grid);

        Assert.NotNull(path);
        Assert.Single(path!);
        Assert.Equal((3, 3), path![0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Budget Limiting
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_BudgetOfOne_ProcessesOnlyOneRequest()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int callbackCount = 0;

        mgr.RequestPath(1, profile, V(0, 0), V(2, 0), _ => callbackCount++);
        mgr.RequestPath(2, profile, V(0, 0), V(4, 0), _ => callbackCount++);
        mgr.RequestPath(3, profile, V(0, 0), V(6, 0), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);

        Assert.Equal(1, callbackCount);
        Assert.Equal(2, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_BudgetOfTwo_ProcessesTwoRequests()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int callbackCount = 0;

        for (int i = 0; i < 4; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i * 2 + 2, 0), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 2);

        Assert.Equal(2, callbackCount);
        Assert.Equal(2, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_BudgetExceedsQueueSize_ProcessesAll()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int callbackCount = 0;

        mgr.RequestPath(1, profile, V(0, 0), V(2, 2), _ => callbackCount++);
        mgr.RequestPath(2, profile, V(0, 0), V(4, 4), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.Equal(2, callbackCount);
        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_MultipleTicksConsumesFullQueue()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int callbackCount = 0;

        for (int i = 0; i < 3; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i * 3 + 3, 0), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(1, callbackCount);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(2, callbackCount);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(3, callbackCount);

        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_LargeQueue_SpreadAcrossMultipleTicks()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int totalCallbacks = 0;
        const int totalRequests = 10;

        for (int i = 0; i < totalRequests; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i + 1, i + 1), _ => totalCallbacks++);

        // 5 ticks × budget 2 = 10
        for (int t = 0; t < 5; t++)
            mgr.ProcessRequests(grid, maxPathsPerTick: 2);

        Assert.Equal(totalRequests, totalCallbacks);
        Assert.Equal(0, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FIFO Ordering
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_FIFOOrder_CallbacksFireInEnqueueOrder()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        var order = new List<int>();

        mgr.RequestPath(1, profile, V(0, 0), V(3, 0), _ => order.Add(1));
        mgr.RequestPath(2, profile, V(0, 0), V(5, 0), _ => order.Add(2));
        mgr.RequestPath(3, profile, V(0, 0), V(7, 0), _ => order.Add(3));

        mgr.ProcessRequests(grid, maxPathsPerTick: 3);

        Assert.Equal(new List<int> { 1, 2, 3 }, order);
    }

    [Fact]
    public void ProcessRequests_BudgetOne_FIFOOrder_FirstRequestProcessedFirst()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int firstCallback = -1;

        mgr.RequestPath(10, profile, V(0, 0), V(5, 5), _ => firstCallback = 10);
        mgr.RequestPath(20, profile, V(0, 0), V(6, 6), _ => firstCallback = 20);
        mgr.RequestPath(30, profile, V(0, 0), V(7, 7), _ => firstCallback = 30);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);

        Assert.Equal(10, firstCallback);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Auto-Batching: Below Threshold (< 5 → individual)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_FourRequestsSameDestination_ProcessedIndividually()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool batchFired = false;
        int callbackCount = 0;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);

        // 4 requests (below auto-batch threshold of 5)
        for (int i = 0; i < 4; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.False(batchFired, "4 requests to same destination should NOT be auto-batched");
        Assert.Equal(4, callbackCount);
        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_OneRequest_NoBatch()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool batchFired = false;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);
        mgr.RequestPath(1, profile, V(0, 0), V(5, 5), _ => { });
        mgr.ProcessRequests(grid);

        Assert.False(batchFired);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Auto-Batching: At/Above Threshold (≥ 5 → FlowField)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_FiveRequestsSameDestination_AutoBatchedAsFlowField()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool batchFired = false;
        int callbackCount = 0;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);

        // Exactly 5 requests to the same destination (at threshold)
        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.True(batchFired, "5 requests to same destination should be auto-batched into a FlowField");
        Assert.Equal(5, callbackCount); // all individual callbacks still fire
        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_SixRequestsSameDestination_AutoBatchedAsFlowField()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        FlowField? batchField = null;

        mgr.OnBatchFlowFieldGenerated(ff => batchField = ff);

        for (int i = 0; i < 6; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(15, 15), _ => { });

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.NotNull(batchField);
    }

    [Fact]
    public void ProcessRequests_BatchedCallbacks_ReceiveEmptyPathList()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        var receivedPaths = new List<List<(int, int)>>();

        mgr.OnBatchFlowFieldGenerated(_ => { });

        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), path => receivedPaths.Add(path));

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.Equal(5, receivedPaths.Count);
        // Batched individual requests receive an empty A* path
        // (the movement system should use the flow field instead)
        foreach (var path in receivedPaths)
            Assert.Empty(path);
    }

    [Fact]
    public void ProcessRequests_BatchCountsAsOneOperation_AgainstBudget()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int batchCallbackCount = 0;

        mgr.OnBatchFlowFieldGenerated(_ => batchCallbackCount++);

        // Two groups of 5 to different destinations
        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), _ => { });
        for (int i = 5; i < 10; i++)
            mgr.RequestPath(i, profile, V(i - 5, 0), V(20, 20), _ => { });

        // Budget of 1 should process only one batch
        mgr.ProcessRequests(grid, maxPathsPerTick: 1);

        Assert.Equal(1, batchCallbackCount);
        // The second group of 5 remains pending
        Assert.Equal(5, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Different Destinations Not Batched Together
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_DifferentDestinations_NotBatched()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool batchFired = false;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);

        // 5 requests but each to a different destination
        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i + 2, i + 2), _ => { });

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.False(batchFired, "Requests to different destinations must not be auto-batched");
    }

    [Fact]
    public void ProcessRequests_DifferentDestinations_AllCallbacksFire()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int callbackCount = 0;

        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(0, 0), V(i + 2, i + 2), _ => callbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.Equal(5, callbackCount);
        Assert.Equal(0, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Different Movement Profiles Not Batched Together
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_DifferentProfiles_SameDestination_NotCrossBatched()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var infantryProfile = MovementProfile.Infantry();
        var vehicleProfile = MovementProfile.LightVehicle();
        bool batchFired = false;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);

        // 3 infantry + 3 vehicle to the same destination — neither group reaches threshold
        for (int i = 0; i < 3; i++)
            mgr.RequestPath(i, infantryProfile, V(i, 0), V(10, 10), _ => { });
        for (int i = 3; i < 6; i++)
            mgr.RequestPath(i, vehicleProfile, V(i - 3, 0), V(10, 10), _ => { });

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.False(batchFired,
            "Different movement profiles sharing a destination must not be cross-batched");
    }

    [Fact]
    public void ProcessRequests_FivePerProfile_SameDestination_EachGroupBatchedSeparately()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var infantryProfile = MovementProfile.Infantry();
        var vehicleProfile = MovementProfile.LightVehicle();
        int batchCount = 0;

        mgr.OnBatchFlowFieldGenerated(_ => batchCount++);

        // 5 infantry + 5 vehicle to the same destination — each group should batch independently
        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, infantryProfile, V(i, 0), V(10, 10), _ => { });
        for (int i = 5; i < 10; i++)
            mgr.RequestPath(i, vehicleProfile, V(i - 5, 0), V(10, 10), _ => { });

        mgr.ProcessRequests(grid, maxPathsPerTick: 20);

        Assert.Equal(2, batchCount);
        Assert.Equal(0, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Explicit FlowField Requests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RequestFlowField_Explicit_CallbackInvoked()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        FlowField? receivedFF = null;

        mgr.RequestFlowField(profile, V(10, 10), new List<int> { 1, 2, 3 },
            ff => receivedFF = ff);

        mgr.ProcessRequests(grid);

        Assert.NotNull(receivedFF);
    }

    [Fact]
    public void RequestFlowField_Explicit_QueueCleared()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();

        mgr.RequestFlowField(profile, V(5, 5), new List<int> { 1 }, _ => { });
        mgr.ProcessRequests(grid);

        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void RequestFlowField_Explicit_CountsAsOneOperation_AgainstBudget()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int ffCallbackCount = 0;

        mgr.RequestFlowField(profile, V(5, 5), new List<int> { 1 }, _ => ffCallbackCount++);
        mgr.RequestFlowField(profile, V(8, 8), new List<int> { 2 }, _ => ffCallbackCount++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 1);

        Assert.Equal(1, ffCallbackCount);
        Assert.Equal(1, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // OnBatchFlowFieldGenerated callback behaviour
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OnBatchFlowFieldGenerated_NotCalledForFewIndividualPaths()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool fired = false;

        mgr.OnBatchFlowFieldGenerated(_ => fired = true);

        // Only 2 requests — no auto-batching
        mgr.RequestPath(1, profile, V(0, 0), V(5, 5), _ => { });
        mgr.RequestPath(2, profile, V(1, 0), V(5, 5), _ => { });

        mgr.ProcessRequests(grid);

        Assert.False(fired);
    }

    [Fact]
    public void OnBatchFlowFieldGenerated_NotCalledForExplicitFlowField()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool fired = false;

        mgr.OnBatchFlowFieldGenerated(_ => fired = true);

        // Explicit flow field request should NOT trigger the batch event
        mgr.RequestFlowField(profile, V(5, 5), new List<int> { 1, 2, 3 }, _ => { });
        mgr.ProcessRequests(grid);

        Assert.False(fired,
            "Explicit FlowField requests must not trigger OnBatchFlowFieldGenerated");
    }

    [Fact]
    public void OnBatchFlowFieldGenerated_ReceivesNonNullFlowField_WhenBatchFires()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        FlowField? capturedFF = null;

        mgr.OnBatchFlowFieldGenerated(ff => capturedFF = ff);

        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), _ => { });

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);

        Assert.NotNull(capturedFF);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Mixed individual + batch + explicit requests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_MixedRequestTypes_AllProcessed()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        int totalCallbacks = 0;
        FlowField? explicitFF = null;

        mgr.OnBatchFlowFieldGenerated(_ => { });

        // 5 requests to same destination → auto-batched (1 flow-field operation)
        for (int i = 0; i < 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(20, 20), _ => totalCallbacks++);

        // 2 individual requests to different destinations (1 operation each)
        mgr.RequestPath(10, profile, V(0, 0), V(3, 3), _ => totalCallbacks++);
        mgr.RequestPath(11, profile, V(0, 0), V(7, 7), _ => totalCallbacks++);

        // 1 explicit flow field (1 operation)
        mgr.RequestFlowField(profile, V(15, 15), new List<int> { 20 },
            ff => { explicitFF = ff; totalCallbacks++; });

        mgr.ProcessRequests(grid, maxPathsPerTick: 20);

        Assert.Equal(0, mgr.PendingCount);
        Assert.NotNull(explicitFF);
        // 5 batch callbacks + 2 individual + 1 explicit = 8
        Assert.Equal(8, totalCallbacks);
    }

    [Fact]
    public void ProcessRequests_IndividualAndBatchInterleaved_BothProcessed()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        bool batchFired = false;
        int individualCallbacks = 0;

        mgr.OnBatchFlowFieldGenerated(_ => batchFired = true);

        // 1 individual request (seq 0)
        mgr.RequestPath(0, profile, V(0, 0), V(3, 0), _ => individualCallbacks++);
        // 5 requests to same destination (seq 1-5) → auto-batch
        for (int i = 1; i <= 5; i++)
            mgr.RequestPath(i, profile, V(i, 0), V(10, 10), _ => { });
        // 1 more individual request (seq 6)
        mgr.RequestPath(6, profile, V(0, 0), V(7, 0), _ => individualCallbacks++);

        mgr.ProcessRequests(grid, maxPathsPerTick: 20);

        Assert.True(batchFired);
        Assert.Equal(2, individualCallbacks);
        Assert.Equal(0, mgr.PendingCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Queue state after partial processing
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRequests_AfterPartialConsumption_RemainingRequestsStillProcessable()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        var results = new List<int>();

        mgr.RequestPath(1, profile, V(0, 0), V(2, 2), _ => results.Add(1));
        mgr.RequestPath(2, profile, V(0, 0), V(4, 4), _ => results.Add(2));
        mgr.RequestPath(3, profile, V(0, 0), V(6, 6), _ => results.Add(3));

        // First tick: process 1
        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(new List<int> { 1 }, results);

        // Second tick: process 1 more
        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(new List<int> { 1, 2 }, results);

        // Third tick: process final
        mgr.ProcessRequests(grid, maxPathsPerTick: 1);
        Assert.Equal(new List<int> { 1, 2, 3 }, results);

        Assert.Equal(0, mgr.PendingCount);
    }

    [Fact]
    public void ProcessRequests_NewRequestsAddedBetweenTicks_ArePickedUpNextTick()
    {
        var mgr = new PathRequestManager();
        var grid = OpenGrid();
        var profile = MovementProfile.Infantry();
        var results = new List<int>();

        mgr.RequestPath(1, profile, V(0, 0), V(2, 2), _ => results.Add(1));

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);
        Assert.Equal(new List<int> { 1 }, results);
        Assert.Equal(0, mgr.PendingCount);

        // Add a new request after the first tick has completed
        mgr.RequestPath(2, profile, V(0, 0), V(4, 4), _ => results.Add(2));
        Assert.Equal(1, mgr.PendingCount);

        mgr.ProcessRequests(grid, maxPathsPerTick: 10);
        Assert.Equal(new List<int> { 1, 2 }, results);
        Assert.Equal(0, mgr.PendingCount);
    }
}
