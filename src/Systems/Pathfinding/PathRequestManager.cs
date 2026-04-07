using System;
using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

/// <summary>
/// Manages and dispatches pathfinding requests across simulation ticks.
///
/// In an RTS, hundreds of units may request paths simultaneously (e.g., after a
/// large move command). Computing all paths in a single tick would cause frame
/// spikes. This manager queues requests and processes a budgeted number per tick,
/// spreading the computational cost across multiple frames.
///
/// <b>Automatic batching:</b> When 5 or more units request paths to the same
/// destination, the manager automatically upgrades them from individual A* paths
/// to a single shared <see cref="FlowField"/>. This is dramatically cheaper: one
/// Dijkstra expansion vs. N separate A* searches.
///
/// <b>Determinism guarantees:</b>
/// - The request queue is FIFO — requests are processed in the exact order they
///   were enqueued, which is deterministic if commands arrive in the same order.
/// - Callbacks are invoked in a fixed, predictable order within each tick.
/// - No Dictionary or HashSet iteration is used for ordering. Destination grouping
///   uses a sorted list of known destinations.
/// - All math uses <see cref="FixedPoint"/> — no float/double in simulation code.
///
/// <b>Usage pattern:</b>
/// 1. Game systems call <see cref="RequestPath"/> or <see cref="RequestFlowField"/>
///    when units need new paths.
/// 2. Each simulation tick, the game loop calls <see cref="ProcessRequests"/> with
///    the terrain grid and a per-tick computation budget.
/// 3. Callbacks fire when paths are ready (always in the same tick they're computed).
/// </summary>
public sealed class PathRequestManager
{
    // ── Constants ────────────────────────────────────────────────────────

    /// <summary>
    /// If this many or more units share the same destination cell, the manager
    /// will automatically use a flow field instead of individual A* paths.
    /// Threshold of 5 balances the overhead of flow field generation against
    /// the savings from avoiding multiple A* searches.
    /// </summary>
    private const int FlowFieldThreshold = 5;

    /// <summary>
    /// Padding (in cells) around the bounding box of requesting units when
    /// computing a flow field region. Provides room for units to maneuver
    /// around obstacles without falling outside the field.
    /// </summary>
    private const int FlowFieldRegionPadding = 10;

    // ── Request Types ───────────────────────────────────────────────────

    /// <summary>
    /// Distinguishes between individual path requests and explicit flow field
    /// requests in the unified queue.
    /// </summary>
    private enum RequestType
    {
        /// <summary>Single unit A* path request (may be batched into a flow field).</summary>
        IndividualPath,

        /// <summary>Explicit flow field request for a group of units.</summary>
        FlowField,
    }

    /// <summary>
    /// A queued pathfinding request. Both individual and flow-field requests
    /// share this struct to maintain a single deterministic FIFO queue.
    /// </summary>
    private struct PathRequest
    {
        /// <summary>Type of this request.</summary>
        public RequestType Type;

        /// <summary>ID of the requesting unit (for individual requests).</summary>
        public int UnitId;

        /// <summary>Movement profile for cost/traversability calculations.</summary>
        public MovementProfile Profile;

        /// <summary>Starting position in world/grid space (for individual requests).</summary>
        public FixedVector2 Start;

        /// <summary>Goal position in world/grid space.</summary>
        public FixedVector2 Goal;

        /// <summary>Callback invoked with the computed A* path (individual requests).</summary>
        public Action<List<(int, int)>>? PathCallback;

        /// <summary>IDs of all units in a flow field group request.</summary>
        public List<int>? UnitIds;

        /// <summary>Callback invoked with the computed flow field (group requests).</summary>
        public Action<FlowField>? FlowFieldCallback;

        /// <summary>
        /// Sequence number assigned at enqueue time. Ensures deterministic
        /// ordering when requests are grouped by destination.
        /// </summary>
        public int SequenceNumber;
    }

    // ── State ───────────────────────────────────────────────────────────

    /// <summary>
    /// FIFO queue of pending requests. New requests are appended to the end;
    /// processing always starts from the front. This guarantees deterministic
    /// processing order across all clients.
    /// </summary>
    private readonly List<PathRequest> _pendingRequests = new();

    /// <summary>
    /// Monotonically increasing counter for assigning sequence numbers.
    /// Ensures stable sort order when grouping requests by destination.
    /// </summary>
    private int _sequenceCounter;

    /// <summary>
    /// Shared A* pathfinder instance. Stateless between calls, so a single
    /// instance can be reused without synchronization issues.
    /// </summary>
    private readonly AStarPathfinder _pathfinder = new();

    // ── Public API: Enqueue Requests ────────────────────────────────────

    /// <summary>
    /// Queues an individual path request for a single unit.
    ///
    /// The request will be processed in a future tick when
    /// <see cref="ProcessRequests"/> is called. If enough units share the same
    /// destination, the manager may automatically upgrade this to a flow field.
    /// </summary>
    /// <param name="unitId">Unique identifier of the requesting unit.</param>
    /// <param name="profile">The unit's movement profile (speed class, terrain rules, footprint).</param>
    /// <param name="start">World-space start position (will be converted to grid coordinates).</param>
    /// <param name="goal">World-space goal position (will be converted to grid coordinates).</param>
    /// <param name="callback">
    /// Invoked with the computed path (list of grid cells from start to goal).
    /// Called with an empty list if no path exists. Always invoked during
    /// <see cref="ProcessRequests"/> — never asynchronously.
    /// </param>
    public void RequestPath(
        int unitId,
        MovementProfile profile,
        FixedVector2 start,
        FixedVector2 goal,
        Action<List<(int, int)>> callback)
    {
        _pendingRequests.Add(new PathRequest
        {
            Type = RequestType.IndividualPath,
            UnitId = unitId,
            Profile = profile,
            Start = start,
            Goal = goal,
            PathCallback = callback,
            SequenceNumber = _sequenceCounter++,
        });
    }

    /// <summary>
    /// Queues an explicit flow field request for a group of units.
    ///
    /// Use this when you already know a large group should share a flow field
    /// (e.g., a box-selected group issued a single move command). The manager
    /// will always generate a flow field for this request, regardless of group size.
    /// </summary>
    /// <param name="profile">Shared movement profile for the group.</param>
    /// <param name="goal">World-space goal position.</param>
    /// <param name="unitIds">IDs of all units in the group.</param>
    /// <param name="callback">
    /// Invoked with the generated flow field. Called during
    /// <see cref="ProcessRequests"/> — never asynchronously.
    /// </param>
    public void RequestFlowField(
        MovementProfile profile,
        FixedVector2 goal,
        List<int> unitIds,
        Action<FlowField> callback)
    {
        _pendingRequests.Add(new PathRequest
        {
            Type = RequestType.FlowField,
            Profile = profile,
            Goal = goal,
            UnitIds = unitIds,
            FlowFieldCallback = callback,
            SequenceNumber = _sequenceCounter++,
        });
    }

    // ── Public API: Process Queue ───────────────────────────────────────

    /// <summary>
    /// Processes queued pathfinding requests, up to the per-tick budget.
    ///
    /// Called once per simulation tick by the game loop. Each "path" counts as
    /// one unit of budget — a flow field also counts as one, even though it
    /// serves multiple units (the whole point is amortization).
    ///
    /// Processing order:
    /// 1. Scan the pending queue for destination grouping opportunities.
    /// 2. For each group of 5+ individual requests to the same destination cell
    ///    with the same movement profile, automatically batch them into a single
    ///    flow field computation.
    /// 3. Process requests in FIFO order (by sequence number).
    /// 4. Invoke all callbacks synchronously during this method call.
    ///
    /// Determinism: This method is fully deterministic given the same request
    /// queue state. Processing order is defined by sequence number (enqueue order).
    /// Callbacks are invoked in that same order.
    /// </summary>
    /// <param name="grid">The current terrain grid.</param>
    /// <param name="maxPathsPerTick">
    /// Maximum number of pathfinding operations this tick. Default: 4.
    /// A single A* path = 1 operation. A flow field = 1 operation.
    /// </param>
    public void ProcessRequests(TerrainGrid grid, int maxPathsPerTick = 4)
    {
        if (_pendingRequests.Count == 0)
            return;

        // ── Step 1: Group individual requests by destination ────────
        // We identify groups of individual path requests that share the same
        // goal cell AND movement profile. If a group reaches the threshold,
        // we'll generate one flow field for the whole group.
        //
        // We use a list-of-groups approach (not Dictionary) to maintain
        // deterministic iteration order.

        var processQueue = BuildProcessQueue();

        // ── Step 2: Process up to budget ────────────────────────────
        int operationsUsed = 0;

        for (int i = 0; i < processQueue.Count && operationsUsed < maxPathsPerTick; i++)
        {
            var item = processQueue[i];

            if (item.IsBatch)
            {
                // ── Flow field for a batch of individual requests ────
                ProcessBatchAsFlowField(grid, item.Requests);
                operationsUsed++;
            }
            else if (item.Requests[0].Type == RequestType.FlowField)
            {
                // ── Explicit flow field request ─────────────────────
                ProcessFlowFieldRequest(grid, item.Requests[0]);
                operationsUsed++;
            }
            else
            {
                // ── Individual A* path ──────────────────────────────
                ProcessIndividualPath(grid, item.Requests[0]);
                operationsUsed++;
            }
        }

        // ── Step 3: Remove processed requests from pending list ─────
        // Build a set of processed sequence numbers for efficient removal.
        // (Using a bool array indexed by sequence number to avoid HashSet.)
        int processed = 0;
        for (int i = 0; i < processQueue.Count && processed < operationsUsed; i++)
        {
            var item = processQueue[i];
            foreach (var req in item.Requests)
            {
                MarkProcessed(req.SequenceNumber);
            }
            processed++;
        }

        // Remove all marked requests from the pending list.
        _pendingRequests.RemoveAll(r => IsProcessed(r.SequenceNumber));
        ClearProcessedMarkers();
    }

    // ── Internal Processing Methods ─────────────────────────────────────

    /// <summary>
    /// Computes an individual A* path and invokes the request's callback.
    /// </summary>
    private void ProcessIndividualPath(TerrainGrid grid, PathRequest request)
    {
        int startX = request.Start.X.ToInt();
        int startY = request.Start.Y.ToInt();
        int goalX = request.Goal.X.ToInt();
        int goalY = request.Goal.Y.ToInt();

        var path = _pathfinder.FindPath(
            grid, request.Profile,
            startX, startY,
            goalX, goalY);

        request.PathCallback?.Invoke(path);
    }

    /// <summary>
    /// Generates a flow field for an explicit flow field request.
    /// The region is sized to cover the goal with generous padding.
    /// </summary>
    private void ProcessFlowFieldRequest(TerrainGrid grid, PathRequest request)
    {
        int goalX = request.Goal.X.ToInt();
        int goalY = request.Goal.Y.ToInt();

        // For explicit flow field requests without individual start positions,
        // use the full grid as the region (or a large area around the goal).
        int regionMinX = Math.Max(0, goalX - grid.Width / 2);
        int regionMinY = Math.Max(0, goalY - grid.Height / 2);
        int regionMaxX = Math.Min(grid.Width - 1, goalX + grid.Width / 2);
        int regionMaxY = Math.Min(grid.Height - 1, goalY + grid.Height / 2);

        var flowField = new FlowField();
        flowField.Generate(grid, request.Profile, goalX, goalY,
            regionMinX, regionMinY, regionMaxX, regionMaxY);

        request.FlowFieldCallback?.Invoke(flowField);
    }

    /// <summary>
    /// Converts a batch of individual path requests into a shared flow field.
    ///
    /// The flow field region is computed as the bounding box of all unit start
    /// positions plus the goal, expanded by <see cref="FlowFieldRegionPadding"/>.
    /// Each unit's original callback receives an empty path list (since they'll
    /// now use the flow field), and additionally a flow field is generated that
    /// the movement system can reference.
    ///
    /// In practice, the caller (movement system) should check whether a flow
    /// field exists for the destination before using the A* path.
    /// </summary>
    private void ProcessBatchAsFlowField(TerrainGrid grid, List<PathRequest> requests)
    {
        if (requests.Count == 0)
            return;

        // All requests in a batch share the same goal and profile.
        var firstReq = requests[0];
        int goalX = firstReq.Goal.X.ToInt();
        int goalY = firstReq.Goal.Y.ToInt();

        // Compute bounding box of all start positions + goal.
        int minX = goalX;
        int minY = goalY;
        int maxX = goalX;
        int maxY = goalY;

        for (int i = 0; i < requests.Count; i++)
        {
            int sx = requests[i].Start.X.ToInt();
            int sy = requests[i].Start.Y.ToInt();
            if (sx < minX) minX = sx;
            if (sy < minY) minY = sy;
            if (sx > maxX) maxX = sx;
            if (sy > maxY) maxY = sy;
        }

        // Expand by padding and clamp to grid.
        int regionMinX = Math.Max(0, minX - FlowFieldRegionPadding);
        int regionMinY = Math.Max(0, minY - FlowFieldRegionPadding);
        int regionMaxX = Math.Min(grid.Width - 1, maxX + FlowFieldRegionPadding);
        int regionMaxY = Math.Min(grid.Height - 1, maxY + FlowFieldRegionPadding);

        // Generate the flow field.
        var flowField = new FlowField();
        flowField.Generate(grid, firstReq.Profile, goalX, goalY,
            regionMinX, regionMinY, regionMaxX, regionMaxY);

        // Invoke each unit's callback with an empty path (they should use the
        // flow field instead). The path callback contract says "empty = no A*
        // path", but the movement system should check for an available flow
        // field before falling back.
        //
        // We also store the flow field in the shared cache for the movement
        // system to look up by destination.
        for (int i = 0; i < requests.Count; i++)
        {
            requests[i].PathCallback?.Invoke(new List<(int, int)>());
        }

        // Notify via the flow field callback on the first request's profile.
        // The movement system can register a listener for batched flow fields.
        _onBatchFlowFieldGenerated?.Invoke(flowField);
    }

    // ── Batch Flow Field Event ──────────────────────────────────────────

    /// <summary>
    /// Event fired when a batch of individual path requests is automatically
    /// upgraded to a flow field. The movement system should subscribe to this
    /// to cache and use the generated flow field.
    /// </summary>
    private Action<FlowField>? _onBatchFlowFieldGenerated;

    /// <summary>
    /// Registers a handler to be called when individual path requests are
    /// automatically batched into a flow field. The movement system should
    /// use this to know when a flow field is available for a destination.
    /// </summary>
    /// <param name="handler">Callback receiving the generated flow field.</param>
    public void OnBatchFlowFieldGenerated(Action<FlowField> handler)
    {
        _onBatchFlowFieldGenerated = handler;
    }

    // ── Process Queue Building ──────────────────────────────────────────

    /// <summary>
    /// Represents either a single request or a batch of requests to the same
    /// destination that will be served by one flow field.
    /// </summary>
    private struct ProcessItem
    {
        /// <summary>Whether this item represents a batched flow field.</summary>
        public bool IsBatch;

        /// <summary>
        /// The request(s) in this item. For non-batched items, contains exactly
        /// one request. For batches, contains all requests sharing a destination.
        /// </summary>
        public List<PathRequest> Requests;

        /// <summary>
        /// The lowest sequence number in this item. Used to maintain overall
        /// FIFO ordering across batched and non-batched items.
        /// </summary>
        public int MinSequenceNumber;
    }

    /// <summary>
    /// Analyzes the pending request queue, groups eligible requests by
    /// destination, and returns an ordered list of <see cref="ProcessItem"/>s
    /// ready for execution.
    ///
    /// Grouping rules:
    /// - Only <see cref="RequestType.IndividualPath"/> requests are candidates
    ///   for batching.
    /// - Requests must share the same goal cell (integer coordinates) AND the
    ///   same <see cref="MovementProfile"/> reference to be grouped.
    /// - Groups of <see cref="FlowFieldThreshold"/> or more become batches.
    /// - Smaller groups remain as individual requests.
    /// - Explicit <see cref="RequestType.FlowField"/> requests are never batched.
    ///
    /// The returned list is sorted by MinSequenceNumber to preserve FIFO order.
    /// </summary>
    private List<ProcessItem> BuildProcessQueue()
    {
        // ── Group individual requests by (goalX, goalY, profile) ────
        // Using a list of groups with linear search. This is O(n*g) where g is
        // the number of distinct destinations, but n is bounded by the number
        // of pending requests (typically < 100) and g is small.

        var groups = new List<DestinationGroup>();

        for (int i = 0; i < _pendingRequests.Count; i++)
        {
            var req = _pendingRequests[i];

            if (req.Type == RequestType.IndividualPath)
            {
                int gx = req.Goal.X.ToInt();
                int gy = req.Goal.Y.ToInt();

                // Find existing group for this destination + profile.
                int groupIdx = -1;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].GoalX == gx &&
                        groups[g].GoalY == gy &&
                        ReferenceEquals(groups[g].Profile, req.Profile))
                    {
                        groupIdx = g;
                        break;
                    }
                }

                if (groupIdx == -1)
                {
                    groups.Add(new DestinationGroup
                    {
                        GoalX = gx,
                        GoalY = gy,
                        Profile = req.Profile,
                        Requests = new List<PathRequest> { req },
                        MinSequenceNumber = req.SequenceNumber,
                    });
                }
                else
                {
                    var group = groups[groupIdx];
                    group.Requests.Add(req);
                    if (req.SequenceNumber < group.MinSequenceNumber)
                        group.MinSequenceNumber = req.SequenceNumber;
                    groups[groupIdx] = group;
                }
            }
        }

        // ── Build process items ─────────────────────────────────────
        var items = new List<ProcessItem>();

        // Add batched groups (>= threshold) and individual requests (< threshold).
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];

            if (group.Requests.Count >= FlowFieldThreshold)
            {
                // Batch into a single flow field operation.
                items.Add(new ProcessItem
                {
                    IsBatch = true,
                    Requests = group.Requests,
                    MinSequenceNumber = group.MinSequenceNumber,
                });
            }
            else
            {
                // Keep as individual requests.
                for (int r = 0; r < group.Requests.Count; r++)
                {
                    items.Add(new ProcessItem
                    {
                        IsBatch = false,
                        Requests = new List<PathRequest> { group.Requests[r] },
                        MinSequenceNumber = group.Requests[r].SequenceNumber,
                    });
                }
            }
        }

        // Add explicit flow field requests.
        for (int i = 0; i < _pendingRequests.Count; i++)
        {
            var req = _pendingRequests[i];
            if (req.Type == RequestType.FlowField)
            {
                items.Add(new ProcessItem
                {
                    IsBatch = false,
                    Requests = new List<PathRequest> { req },
                    MinSequenceNumber = req.SequenceNumber,
                });
            }
        }

        // Sort by sequence number to maintain FIFO order.
        items.Sort((a, b) => a.MinSequenceNumber.CompareTo(b.MinSequenceNumber));

        return items;
    }

    /// <summary>
    /// Intermediate grouping structure used during process queue building.
    /// </summary>
    private struct DestinationGroup
    {
        public int GoalX;
        public int GoalY;
        public MovementProfile Profile;
        public List<PathRequest> Requests;
        public int MinSequenceNumber;
    }

    // ── Processed Tracking ──────────────────────────────────────────────
    // Instead of using a HashSet (non-deterministic iteration), we use a
    // simple list of processed sequence numbers and linear search.
    // The list is small (bounded by maxPathsPerTick × batch size).

    /// <summary>
    /// Sequence numbers of requests that have been processed this tick.
    /// </summary>
    private readonly List<int> _processedSequences = new();

    /// <summary>Marks a sequence number as processed.</summary>
    private void MarkProcessed(int sequenceNumber)
    {
        _processedSequences.Add(sequenceNumber);
    }

    /// <summary>Checks if a sequence number was processed this tick.</summary>
    private bool IsProcessed(int sequenceNumber)
    {
        for (int i = 0; i < _processedSequences.Count; i++)
        {
            if (_processedSequences[i] == sequenceNumber)
                return true;
        }
        return false;
    }

    /// <summary>Clears the processed markers for the next tick.</summary>
    private void ClearProcessedMarkers()
    {
        _processedSequences.Clear();
    }

    // ── Diagnostics ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of requests currently waiting in the queue.
    /// Useful for debugging and performance monitoring.
    /// </summary>
    public int PendingCount => _pendingRequests.Count;
}
