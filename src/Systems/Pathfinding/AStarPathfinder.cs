using System;
using System.Buffers;
using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

/// <summary>
/// A* pathfinding implementation for the RTS simulation layer.
///
/// Key design decisions:
/// - ALL math uses <see cref="FixedPoint"/> and <see cref="FixedVector2"/> for
///   cross-platform determinism in lockstep multiplayer. No float/double in any
///   cost, heuristic, or distance calculation.
/// - Uses an array-backed binary min-heap (<see cref="MinHeap{T}"/>) instead of
///   .NET's PriorityQueue to guarantee deterministic tie-breaking and avoid
///   hidden allocations after initial capacity reservation.
/// - Supports footprint-aware traversal: large units (e.g., 3×3) check that ALL
///   cells they would occupy at each candidate position are traversable.
/// - 8-directional movement with diagonal cost = sqrt(2) in FixedPoint.
/// - Heuristic: octile distance scaled by the minimum possible terrain cost
///   (ensures admissibility).
/// - Open/closed tracking uses a flat array indexed by (y * gridWidth + x)
///   rather than HashSet to avoid non-deterministic iteration order.
///
/// Not yet implemented:
/// - Jump Point Search (JPS) — planned optimization for large open maps.
/// - Hierarchical pathfinding — will be added when map sizes exceed ~256×256.
/// </summary>
public sealed class AStarPathfinder
{
    // ── Pre-computed Constants ───────────────────────────────────────────

    /// <summary>
    /// Cost of moving one cell in a cardinal direction (N/S/E/W).
    /// Represented as FixedPoint 1.0 (raw = 65536).
    /// </summary>
    private static readonly FixedPoint CardinalCost = FixedPoint.One;

    /// <summary>
    /// Cost of moving one cell diagonally (NE/NW/SE/SW).
    /// sqrt(2) ≈ 1.41421356 in Q16.16 fixed-point = raw 92682.
    /// Pre-computed once to avoid per-node sqrt calls.
    /// </summary>
    private static readonly FixedPoint DiagonalCost = FixedPoint.Sqrt(FixedPoint.FromInt(2));

    /// <summary>
    /// Minimum possible terrain cost multiplier. Used to scale the heuristic
    /// so it remains admissible (never overestimates). If terrain costs are
    /// always >= 1.0, this is 1.0. Adjust if terrain has cost < 1 (roads, etc.).
    /// </summary>
    private static readonly FixedPoint MinTerrainCost = FixedPoint.One;

    /// <summary>
    /// The 8 neighbor offsets: N, NE, E, SE, S, SW, W, NW.
    /// Order is deterministic and fixed.
    /// </summary>
    private static readonly (int dx, int dy)[] NeighborOffsets =
    {
        ( 0, -1), // N
        ( 1, -1), // NE
        ( 1,  0), // E
        ( 1,  1), // SE
        ( 0,  1), // S
        (-1,  1), // SW
        (-1,  0), // W
        (-1, -1), // NW
    };

    /// <summary>
    /// Whether each of the 8 neighbor directions is diagonal.
    /// Indices correspond to <see cref="NeighborOffsets"/>.
    /// </summary>
    private static readonly bool[] IsDiagonal =
    {
        false, // N
        true,  // NE
        false, // E
        true,  // SE
        false, // S
        true,  // SW
        false, // W
        true,  // NW
    };

    // ── PathNode ────────────────────────────────────────────────────────

    /// <summary>
    /// Internal node used during A* search. Stored in a flat array indexed
    /// by insertion order (not grid position) to allow fast heap operations.
    /// </summary>
    private struct PathNode
    {
        /// <summary>Grid X coordinate of this node.</summary>
        public int X;

        /// <summary>Grid Y coordinate of this node.</summary>
        public int Y;

        /// <summary>Cost from start to this node along the best known path.</summary>
        public FixedPoint GCost;

        /// <summary>Heuristic estimate from this node to the goal.</summary>
        public FixedPoint HCost;

        /// <summary>Total estimated cost: GCost + HCost. Used for heap ordering.</summary>
        public FixedPoint FCost;

        /// <summary>
        /// Index into the node array of this node's parent, or -1 if this is
        /// the start node. Used to reconstruct the path once the goal is reached.
        /// </summary>
        public int ParentIndex;
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the shortest path from (startX, startY) to (goalX, goalY) on the
    /// given terrain grid, respecting the unit's movement profile.
    ///
    /// The returned path is a list of (x, y) grid coordinates from start to goal
    /// (inclusive of both endpoints). Returns an empty list if no path exists or
    /// the search exceeds <paramref name="maxNodes"/> expanded nodes.
    ///
    /// For large units (footprint > 1×1), the position represents the top-left
    /// corner of the unit's bounding box. All cells the unit would occupy are
    /// checked for traversability at each candidate position.
    /// </summary>
    /// <param name="grid">The terrain grid to pathfind on.</param>
    /// <param name="profile">Movement profile of the requesting unit (domain, class, size).</param>
    /// <param name="startX">Grid X of the starting position (top-left of footprint).</param>
    /// <param name="startY">Grid Y of the starting position (top-left of footprint).</param>
    /// <param name="goalX">Grid X of the goal position (top-left of footprint).</param>
    /// <param name="goalY">Grid Y of the goal position (top-left of footprint).</param>
    /// <param name="maxNodes">
    /// Maximum number of nodes to expand before giving up. Prevents the search
    /// from consuming too many resources on a single frame. Default: 2048.
    /// </param>
    /// <returns>
    /// Ordered list of (x, y) grid cells from start to goal, or an empty list
    /// if no path was found.
    /// </returns>
    public List<(int x, int y)> FindPath(
        TerrainGrid grid,
        MovementProfile profile,
        int startX, int startY,
        int goalX, int goalY,
        int maxNodes = 2048)
    {
        // ── Early-out checks ────────────────────────────────────────
        var result = new List<(int x, int y)>();

        // Trivial case: already at the goal.
        if (startX == goalX && startY == goalY)
        {
            result.Add((startX, startY));
            return result;
        }

        // Goal must be in bounds and traversable for the unit's full footprint.
        if (!IsFootprintTraversable(grid, profile, goalX, goalY))
            return result;

        // Start must also be traversable (or we can't even begin).
        if (!IsFootprintTraversable(grid, profile, startX, startY))
            return result;

        // ── Rent working arrays from the pool ───────────────────────
        // Pooling avoids repeated Large Object Heap allocations (gridSize can
        // be 262 144 ints for a 512×512 map) and the associated GC pressure.
        int gridWidth  = grid.Width;
        int gridHeight = grid.Height;
        int gridSize   = gridWidth * gridHeight;

        // cellToNode: maps flat cell index → index into nodes[].  -1 = unvisited.
        int[]      cellToNode = ArrayPool<int>.Shared.Rent(gridSize);
        // closed: marks fully-expanded cells.
        bool[]     closed     = ArrayPool<bool>.Shared.Rent(gridSize);
        // nodes: flat node storage indexed by insertion order.
        PathNode[] nodes      = ArrayPool<PathNode>.Shared.Rent(maxNodes);
        // heapItems: backing store for the open-set min-heap.
        int[]      heapItems  = ArrayPool<int>.Shared.Rent(maxNodes * 4);

        try
        {
            // ArrayPool.Rent() does not guarantee zero-initialised buffers.
            Array.Fill(cellToNode, -1, 0, gridSize);
            Array.Clear(closed, 0, gridSize);
            // nodes and heapItems are written before being read; no init needed.

            int nodeCount = 0;

            // ── Seed the open set with the start node ───────────────
            FixedPoint startH = OctileHeuristic(startX, startY, goalX, goalY);
            nodes[nodeCount] = new PathNode
            {
                X = startX,
                Y = startY,
                GCost = FixedPoint.Zero,
                HCost = startH,
                FCost = startH,
                ParentIndex = -1
            };
            cellToNode[startY * gridWidth + startX] = nodeCount;

            // Min-heap ordered by FCost, breaking ties by lower GCost.
            // Uses the pooled heapItems array; capacity is 4× maxNodes to
            // accommodate lazy-deletion duplicates without reallocation.
            var openHeap = new MinHeap<int>(heapItems, maxNodes * 4, (a, b) =>
            {
                int cmp = nodes[a].FCost.CompareTo(nodes[b].FCost);
                if (cmp != 0) return cmp;
                // Tie-break: prefer the node that has travelled farther.
                return nodes[b].GCost.CompareTo(nodes[a].GCost);
            });
            openHeap.Push(nodeCount);
            nodeCount++;

            // ── Main A* loop ────────────────────────────────────────
            while (openHeap.Count > 0)
            {
                int currentIdx = openHeap.Pop();
                ref PathNode current = ref nodes[currentIdx];

                int cx      = current.X;
                int cy      = current.Y;
                int cellKey = cy * gridWidth + cx;

                // Skip stale heap entries (lazy deletion).
                if (closed[cellKey])
                    continue;

                closed[cellKey] = true;

                // ── Goal reached — reconstruct path ─────────────────
                if (cx == goalX && cy == goalY)
                    return ReconstructPath(nodes, currentIdx);

                // ── Expand all 8 neighbours ──────────────────────────
                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = cx + NeighborOffsets[dir].dx;
                    int ny = cy + NeighborOffsets[dir].dy;

                    if (nx < 0 || ny < 0 || nx >= gridWidth || ny >= gridHeight)
                        continue;

                    int neighborKey = ny * gridWidth + nx;

                    if (closed[neighborKey])
                        continue;

                    if (!IsFootprintTraversable(grid, profile, nx, ny))
                        continue;

                    // Corner-cutting prevention for diagonal moves.
                    if (IsDiagonal[dir])
                    {
                        int adjX1 = cx + NeighborOffsets[dir].dx;
                        int adjY1 = cy;
                        int adjX2 = cx;
                        int adjY2 = cy + NeighborOffsets[dir].dy;

                        if (!IsFootprintTraversable(grid, profile, adjX1, adjY1) ||
                            !IsFootprintTraversable(grid, profile, adjX2, adjY2))
                            continue;
                    }

                    FixedPoint stepCost    = IsDiagonal[dir] ? DiagonalCost : CardinalCost;
                    FixedPoint terrainCost = TerrainCostCalculator.GetMovementCost(
                        grid, profile, cx, cy, nx, ny);
                    FixedPoint tentativeG  = current.GCost + stepCost * terrainCost;

                    int existingIdx = cellToNode[neighborKey];

                    if (existingIdx == -1)
                    {
                        if (nodeCount >= maxNodes)
                            return result; // budget exhausted

                        FixedPoint h = OctileHeuristic(nx, ny, goalX, goalY);
                        nodes[nodeCount] = new PathNode
                        {
                            X           = nx,
                            Y           = ny,
                            GCost       = tentativeG,
                            HCost       = h,
                            FCost       = tentativeG + h,
                            ParentIndex = currentIdx
                        };
                        cellToNode[neighborKey] = nodeCount;
                        openHeap.Push(nodeCount);
                        nodeCount++;
                    }
                    else if (tentativeG < nodes[existingIdx].GCost)
                    {
                        ref PathNode existing = ref nodes[existingIdx];
                        existing.GCost       = tentativeG;
                        existing.FCost       = tentativeG + existing.HCost;
                        existing.ParentIndex = currentIdx;
                        openHeap.Push(existingIdx);
                    }
                }
            }

            // Open set exhausted — no path exists.
            return result;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(cellToNode);
            ArrayPool<bool>.Shared.Return(closed);
            ArrayPool<PathNode>.Shared.Return(nodes);
            ArrayPool<int>.Shared.Return(heapItems);
        }
    }

    // ── Heuristic ───────────────────────────────────────────────────────

    /// <summary>
    /// Octile distance heuristic for 8-directional grids.
    ///
    /// Formula: max(dx, dy) + (sqrt(2) - 1) * min(dx, dy)
    ///
    /// This is the exact shortest distance on an unweighted 8-directional grid.
    /// We multiply by <see cref="MinTerrainCost"/> to ensure admissibility when
    /// terrain has variable costs (the heuristic must never overestimate).
    /// </summary>
    private static FixedPoint OctileHeuristic(int fromX, int fromY, int toX, int toY)
    {
        int dx = Math.Abs(toX - fromX);
        int dy = Math.Abs(toY - fromY);

        // Convert to FixedPoint for the calculation.
        FixedPoint fdx = FixedPoint.FromInt(dx);
        FixedPoint fdy = FixedPoint.FromInt(dy);

        // max(dx, dy) + (sqrt(2) - 1) * min(dx, dy)
        FixedPoint maxD = FixedPoint.Max(fdx, fdy);
        FixedPoint minD = FixedPoint.Min(fdx, fdy);
        FixedPoint diagonalExtra = DiagonalCost - CardinalCost; // sqrt(2) - 1

        return (maxD + diagonalExtra * minD) * MinTerrainCost;
    }

    // ── Footprint Traversability ────────────────────────────────────────

    /// <summary>
    /// Checks whether a unit with the given movement profile can occupy position
    /// (x, y), accounting for the unit's footprint size.
    ///
    /// For a 1×1 unit, this checks a single cell.
    /// For a 3×3 unit at position (x, y), this checks all cells from
    /// (x, y) to (x + 2, y + 2).
    ///
    /// The position (x, y) represents the top-left corner of the unit's
    /// bounding box on the grid.
    /// </summary>
    private static bool IsFootprintTraversable(
        TerrainGrid grid, MovementProfile profile, int x, int y)
    {
        int fw = profile.FootprintWidth;
        int fh = profile.FootprintHeight;

        // For the common 1×1 case, skip the loop overhead.
        if (fw == 1 && fh == 1)
        {
            return grid.IsInBounds(x, y) &&
                   TerrainCostCalculator.CanTraverse(grid, profile, x, y);
        }

        // Check every cell the footprint would occupy.
        for (int fy = 0; fy < fh; fy++)
        {
            for (int fx = 0; fx < fw; fx++)
            {
                int cx = x + fx;
                int cy = y + fy;

                if (!grid.IsInBounds(cx, cy))
                    return false;

                if (!TerrainCostCalculator.CanTraverse(grid, profile, cx, cy))
                    return false;
            }
        }

        return true;
    }

    // ── Path Reconstruction ─────────────────────────────────────────────

    /// <summary>
    /// Walks the parent chain from the goal node back to the start, then
    /// reverses the list to produce a path from start to goal.
    /// </summary>
    private static List<(int x, int y)> ReconstructPath(PathNode[] nodes, int goalIdx)
    {
        var path = new List<(int x, int y)>();
        int idx = goalIdx;

        while (idx != -1)
        {
            ref PathNode node = ref nodes[idx];
            path.Add((node.X, node.Y));
            idx = node.ParentIndex;
        }

        path.Reverse();
        return path;
    }

    // ── Binary Min-Heap ─────────────────────────────────────────────────

    /// <summary>
    /// Array-backed binary min-heap with a custom comparer.
    ///
    /// Design goals:
    /// - Zero allocations after the initial array allocation (no List resizing,
    ///   no boxing, no LINQ).
    /// - Deterministic ordering: elements with equal priority are ordered by the
    ///   comparer, which must provide a total order (no ties left to
    ///   implementation-defined behavior).
    /// - O(log n) push and pop, O(1) peek.
    ///
    /// This is used instead of .NET's PriorityQueue because:
    /// 1. PriorityQueue does not guarantee stable/deterministic ordering for
    ///    equal-priority elements across platforms.
    /// 2. We need to avoid hidden allocations in the simulation hot path.
    /// 3. Full control over the comparison logic for lockstep determinism.
    /// </summary>
    /// <typeparam name="T">The element type stored in the heap.</typeparam>
    private sealed class MinHeap<T>
    {
        private readonly T[] _items;
        private readonly int _capacity;
        private readonly Comparison<T> _compare;
        private int _count;

        /// <summary>Number of elements currently in the heap.</summary>
        public int Count => _count;

        /// <summary>
        /// Creates a new min-heap backed by a pre-allocated array.
        /// The array must be at least <paramref name="capacity"/> elements long.
        /// Used with ArrayPool to eliminate per-call heap allocation.
        /// </summary>
        public MinHeap(T[] backingArray, int capacity, Comparison<T> compare)
        {
            _items    = backingArray;
            _capacity = capacity;
            _compare  = compare;
            _count    = 0;
        }

        /// <summary>
        /// Inserts an element into the heap. O(log n).
        /// </summary>
        /// <param name="item">The element to insert.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the heap is at capacity. In practice, this is guarded by
        /// the maxNodes check in <see cref="FindPath"/>.
        /// </exception>
        public void Push(T item)
        {
            if (_count >= _capacity)
                throw new InvalidOperationException(
                    "MinHeap capacity exceeded. Increase maxNodes or heap size.");

            _items[_count] = item;
            SiftUp(_count);
            _count++;
        }

        /// <summary>
        /// Removes and returns the minimum element. O(log n).
        /// </summary>
        /// <returns>The element with the lowest priority value.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the heap is empty.
        /// </exception>
        public T Pop()
        {
            if (_count == 0)
                throw new InvalidOperationException("MinHeap is empty.");

            T min = _items[0];
            _count--;
            _items[0] = _items[_count];
            _items[_count] = default!; // Clear reference (not strictly needed for value types).

            if (_count > 0)
                SiftDown(0);

            return min;
        }

        /// <summary>
        /// Returns the minimum element without removing it. O(1).
        /// </summary>
        public T Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("MinHeap is empty.");
            return _items[0];
        }

        /// <summary>
        /// Restores heap property by moving element at <paramref name="index"/>
        /// upward until it is no longer smaller than its parent.
        /// </summary>
        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1; // (index - 1) / 2
                if (_compare(_items[index], _items[parent]) < 0)
                {
                    // Swap child with parent.
                    (_items[index], _items[parent]) = (_items[parent], _items[index]);
                    index = parent;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Restores heap property by moving element at <paramref name="index"/>
        /// downward until it is no larger than both children.
        /// </summary>
        private void SiftDown(int index)
        {
            while (true)
            {
                int left = (index << 1) + 1;  // 2 * index + 1
                int right = (index << 1) + 2; // 2 * index + 2
                int smallest = index;

                if (left < _count && _compare(_items[left], _items[smallest]) < 0)
                    smallest = left;

                if (right < _count && _compare(_items[right], _items[smallest]) < 0)
                    smallest = right;

                if (smallest == index)
                    break;

                (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
                index = smallest;
            }
        }
    }
}
