using System;
using System.Buffers;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

/// <summary>
/// Enumerates the 8 cardinal/ordinal directions plus a "no direction" sentinel.
/// Stored as a byte for compact packing in the direction grid.
///
/// Direction layout (compass rose):
///
///      NW  N  NE
///       \\ | /
///    W ── · ── E
///       / | \\
///      SW  S  SE
/// </summary>
public enum FlowDirection : byte
{
    /// <summary>No valid flow direction (impassable or goal cell).</summary>
    None = 0,

    /// <summary>North: (0, -1)</summary>
    N = 1,

    /// <summary>Northeast: (1, -1)</summary>
    NE = 2,

    /// <summary>East: (1, 0)</summary>
    E = 3,

    /// <summary>Southeast: (1, 1)</summary>
    SE = 4,

    /// <summary>South: (0, 1)</summary>
    S = 5,

    /// <summary>Southwest: (-1, 1)</summary>
    SW = 6,

    /// <summary>West: (-1, 0)</summary>
    W = 7,

    /// <summary>Northwest: (-1, -1)</summary>
    NW = 8,
}

/// <summary>
/// Flow field for efficient group movement.
///
/// When many units share the same destination, computing individual A* paths for
/// each is wasteful. A flow field computes a single cost (integration) field from
/// the destination using Dijkstra's algorithm, then derives a direction field from
/// the cost gradient. Each unit simply looks up its current cell to get a movement
/// direction — O(1) per unit per tick.
///
/// Architecture:
/// 1. <b>Integration field</b> (<see cref="IntegrationField"/>): a 2D array of
///    <see cref="FixedPoint"/> values representing the minimum traversal cost from
///    each cell to the goal. Computed via Dijkstra (BFS with weighted edges).
///
/// 2. <b>Direction field</b> (<see cref="Directions"/>): a 2D array of
///    <see cref="FlowDirection"/> bytes. Each cell points toward the neighbor with
///    the lowest integration cost — i.e., the steepest descent toward the goal.
///
/// The field can be computed for a sub-region of the map to avoid processing the
/// entire grid when units are clustered in a small area.
///
/// DETERMINISM: All math uses <see cref="FixedPoint"/>. The Dijkstra frontier is
/// managed with the same deterministic <see cref="MinHeap"/> pattern used by
/// <see cref="AStarPathfinder"/> (array-backed, no non-deterministic collections).
/// Neighbor iteration order is fixed, so identical inputs always produce identical
/// direction fields across all platforms.
/// </summary>
public sealed class FlowField
{
    // ── Pre-computed Direction Vectors ───────────────────────────────────

    /// <summary>
    /// Normalized <see cref="FixedVector2"/> for each <see cref="FlowDirection"/>.
    /// Index by (int)FlowDirection to get the unit vector. The diagonal vectors
    /// are normalized to length 1 (not (1,1) which has length sqrt(2)).
    ///
    /// Pre-computing these avoids per-lookup sqrt calls during simulation ticks.
    /// </summary>
    private static readonly FixedVector2[] DirectionVectors;

    /// <summary>
    /// Offsets (dx, dy) for each <see cref="FlowDirection"/> value.
    /// Indexed by (int)FlowDirection. None maps to (0, 0).
    /// </summary>
    private static readonly (int dx, int dy)[] DirectionOffsets =
    {
        ( 0,  0), // None
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
    /// The 8 neighbor offsets for integration/direction computation.
    /// Order: N, NE, E, SE, S, SW, W, NW — must match <see cref="FlowDirection"/>
    /// enum values 1–8.
    /// </summary>
    private static readonly (int dx, int dy)[] NeighborOffsets =
    {
        ( 0, -1), // N  = 1
        ( 1, -1), // NE = 2
        ( 1,  0), // E  = 3
        ( 1,  1), // SE = 4
        ( 0,  1), // S  = 5
        (-1,  1), // SW = 6
        (-1,  0), // W  = 7
        (-1, -1), // NW = 8
    };

    /// <summary>
    /// Whether each of the 8 neighbor directions (indices 0–7) is diagonal.
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

    /// <summary>
    /// Cost of moving one cell in a cardinal direction (1.0 in FixedPoint).
    /// </summary>
    private static readonly FixedPoint CardinalCost = FixedPoint.One;

    /// <summary>
    /// Cost of moving one cell diagonally (sqrt(2) in FixedPoint).
    /// </summary>
    private static readonly FixedPoint DiagonalCost = FixedPoint.Sqrt(FixedPoint.FromInt(2));

    /// <summary>
    /// Sentinel value representing an unreachable cell in the integration field.
    /// Set to FixedPoint.MaxValue so any real cost is always lower.
    /// </summary>
    private static readonly FixedPoint Unreachable = FixedPoint.MaxValue;

    // ── Static Constructor ──────────────────────────────────────────────

    static FlowField()
    {
        // Build normalized direction vectors for all 9 FlowDirection values.
        DirectionVectors = new FixedVector2[9];
        DirectionVectors[0] = FixedVector2.Zero; // None

        for (int i = 1; i <= 8; i++)
        {
            var (dx, dy) = DirectionOffsets[i];
            var vec = new FixedVector2(FixedPoint.FromInt(dx), FixedPoint.FromInt(dy));
            DirectionVectors[i] = vec.Normalized();
        }
    }

    // ── Public Fields / Properties ──────────────────────────────────────

    /// <summary>
    /// The direction field. Each cell contains a <see cref="FlowDirection"/>
    /// (cast to byte) indicating which neighbour to move toward.
    /// Flat row-major layout: index = localY * RegionWidth + localX.
    /// </summary>
    public byte[] Directions { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// The integration (cost) field. Each cell stores the minimum cost to reach
    /// the goal from that cell. <see cref="FixedPoint.MaxValue"/> means unreachable.
    /// Flat row-major layout: index = localY * RegionWidth + localX.
    /// </summary>
    public FixedPoint[] IntegrationField { get; private set; } = Array.Empty<FixedPoint>();

    /// <summary>
    /// Whether this flow field has been successfully computed and is ready for use.
    /// </summary>
    public bool IsValid { get; private set; }

    /// <summary>Grid X coordinate of the goal cell.</summary>
    public int GoalX { get; private set; }

    /// <summary>Grid Y coordinate of the goal cell.</summary>
    public int GoalY { get; private set; }

    /// <summary>Minimum X of the computed region (inclusive), in world grid coords.</summary>
    public int RegionMinX { get; private set; }

    /// <summary>Minimum Y of the computed region (inclusive), in world grid coords.</summary>
    public int RegionMinY { get; private set; }

    /// <summary>Maximum X of the computed region (inclusive), in world grid coords.</summary>
    public int RegionMaxX { get; private set; }

    /// <summary>Maximum Y of the computed region (inclusive), in world grid coords.</summary>
    public int RegionMaxY { get; private set; }

    /// <summary>Width of the computed region in cells.</summary>
    private int RegionWidth => RegionMaxX - RegionMinX + 1;

    /// <summary>Height of the computed region in cells.</summary>
    private int RegionHeight => RegionMaxY - RegionMinY + 1;

    // ── Generation ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates the integration and direction fields for the specified region.
    ///
    /// Algorithm:
    /// 1. Initialize all cells to <see cref="Unreachable"/>.
    /// 2. Set the goal cell cost to zero and push it onto a min-heap.
    /// 3. Dijkstra expansion: pop the cheapest cell, relax all 8 neighbors.
    ///    Edge cost = base step cost (cardinal 1.0 / diagonal sqrt(2)) ×
    ///    terrain movement cost from <see cref="TerrainCostCalculator"/>.
    /// 4. After integration, sweep every cell and set its direction to the
    ///    neighbor with the lowest integration cost.
    /// </summary>
    /// <param name="grid">The terrain grid.</param>
    /// <param name="profile">Movement profile (determines traversability and costs).</param>
    /// <param name="goalX">Goal cell X in world grid coordinates.</param>
    /// <param name="goalY">Goal cell Y in world grid coordinates.</param>
    /// <param name="regionMinX">Left edge of the region to compute (inclusive).</param>
    /// <param name="regionMinY">Top edge of the region to compute (inclusive).</param>
    /// <param name="regionMaxX">Right edge of the region to compute (inclusive).</param>
    /// <param name="regionMaxY">Bottom edge of the region to compute (inclusive).</param>
    public void Generate(
        TerrainGrid grid,
        MovementProfile profile,
        int goalX, int goalY,
        int regionMinX, int regionMinY,
        int regionMaxX, int regionMaxY)
    {
        // ── Clamp region to grid bounds ─────────────────────────────
        regionMinX = Math.Max(regionMinX, 0);
        regionMinY = Math.Max(regionMinY, 0);
        regionMaxX = Math.Min(regionMaxX, grid.Width - 1);
        regionMaxY = Math.Min(regionMaxY, grid.Height - 1);

        RegionMinX = regionMinX;
        RegionMinY = regionMinY;
        RegionMaxX = regionMaxX;
        RegionMaxY = regionMaxY;
        GoalX      = goalX;
        GoalY      = goalY;
        IsValid    = false;

        int width  = RegionWidth;
        int height = RegionHeight;

        if (width <= 0 || height <= 0)
            return;

        // ── Validate goal is within region ──────────────────────────
        if (goalX < regionMinX || goalX > regionMaxX ||
            goalY < regionMinY || goalY > regionMaxY)
            return;

        // ── Allocate / rent flat arrays ─────────────────────────────
        // Using ArrayPool avoids LOH allocations when the same FlowField
        // object is reused for repeated group-move commands.
        int totalCells = width * height;

        FixedPoint[] integrationField = ArrayPool<FixedPoint>.Shared.Rent(totalCells);
        byte[]       directions       = ArrayPool<byte>.Shared.Rent(totalCells);
        bool[]       finalized        = ArrayPool<bool>.Shared.Rent(totalCells);
        int[]        heapItems        = ArrayPool<int>.Shared.Rent(totalCells * 4);

        try
        {
            // Initialize integration field to Unreachable, finalized to false.
            // Array.Fill / Array.Clear use vectorised (SIMD) paths in .NET 5+.
            Array.Fill(integrationField, Unreachable, 0, totalCells);
            Array.Clear(finalized, 0, totalCells);
            // directions are set before being read; no pre-init needed.

            // ── Build integration field via Dijkstra ────────────────
            var heap = new DijkstraHeap(heapItems, totalCells * 4, integrationField, width);

            int goalLocalX = goalX - regionMinX;
            int goalLocalY = goalY - regionMinY;
            integrationField[goalLocalY * width + goalLocalX] = FixedPoint.Zero;
            heap.Push(goalLocalX, goalLocalY);

            while (heap.Count > 0)
            {
                var (cx, cy) = heap.Pop();
                int packedC  = cy * width + cx;

                if (finalized[packedC])
                    continue;

                finalized[packedC] = true;

                FixedPoint currentCost = integrationField[packedC];

                int worldX = cx + regionMinX;
                int worldY = cy + regionMinY;

                for (int dir = 0; dir < 8; dir++)
                {
                    int nlx = cx + NeighborOffsets[dir].dx;
                    int nly = cy + NeighborOffsets[dir].dy;

                    if (nlx < 0 || nly < 0 || nlx >= width || nly >= height)
                        continue;

                    int packedN = nly * width + nlx;
                    if (finalized[packedN])
                        continue;

                    int nwx = nlx + regionMinX;
                    int nwy = nly + regionMinY;

                    if (!grid.IsInBounds(nwx, nwy) ||
                        !TerrainCostCalculator.CanTraverse(grid, profile, nwx, nwy))
                        continue;

                    if (IsDiagonal[dir])
                    {
                        int adjWorldX1 = worldX + NeighborOffsets[dir].dx;
                        int adjWorldY1 = worldY;
                        int adjWorldX2 = worldX;
                        int adjWorldY2 = worldY + NeighborOffsets[dir].dy;

                        if (!grid.IsInBounds(adjWorldX1, adjWorldY1) ||
                            !TerrainCostCalculator.CanTraverse(grid, profile, adjWorldX1, adjWorldY1) ||
                            !grid.IsInBounds(adjWorldX2, adjWorldY2) ||
                            !TerrainCostCalculator.CanTraverse(grid, profile, adjWorldX2, adjWorldY2))
                            continue;
                    }

                    FixedPoint stepCost    = IsDiagonal[dir] ? DiagonalCost : CardinalCost;
                    FixedPoint terrainCost = TerrainCostCalculator.GetMovementCost(
                        grid, profile, nwx, nwy, worldX, worldY);
                    FixedPoint newCost = currentCost + stepCost * terrainCost;

                    if (newCost < integrationField[packedN])
                    {
                        integrationField[packedN] = newCost;
                        heap.Push(nlx, nly);
                    }
                }
            }

            // ── Build direction field from integration gradient ─────
            // Outer loop y, inner loop x → sequential reads of integrationField
            // (row-major y*width+x) and sequential writes to directions.
            for (int ly = 0; ly < height; ly++)
            {
                int rowBase = ly * width;
                for (int lx = 0; lx < width; lx++)
                {
                    int packedLoc = rowBase + lx;

                    if (integrationField[packedLoc] == Unreachable)
                    {
                        directions[packedLoc] = (byte)FlowDirection.None;
                        continue;
                    }

                    if (lx == goalLocalX && ly == goalLocalY)
                    {
                        directions[packedLoc] = (byte)FlowDirection.None;
                        continue;
                    }

                    FixedPoint    bestCost = integrationField[packedLoc];
                    FlowDirection bestDir  = FlowDirection.None;

                    for (int dir = 0; dir < 8; dir++)
                    {
                        int nlx = lx + NeighborOffsets[dir].dx;
                        int nly = ly + NeighborOffsets[dir].dy;

                        if (nlx < 0 || nly < 0 || nlx >= width || nly >= height)
                            continue;

                        FixedPoint neighborCost = integrationField[nly * width + nlx];
                        if (neighborCost < bestCost)
                        {
                            bestCost = neighborCost;
                            bestDir  = (FlowDirection)(dir + 1);
                        }
                    }

                    directions[packedLoc] = (byte)bestDir;
                }
            }

            // ── Copy results into public properties ─────────────────
            // We keep a fresh copy so the rented arrays can be returned
            // and callers can safely read IntegrationField / Directions
            // after Generate() returns.
            IntegrationField = new FixedPoint[totalCells];
            Directions       = new byte[totalCells];
            Array.Copy(integrationField, IntegrationField, totalCells);
            Array.Copy(directions,       Directions,       totalCells);

            IsValid = true;
        }
        finally
        {
            ArrayPool<FixedPoint>.Shared.Return(integrationField);
            ArrayPool<byte>.Shared.Return(directions);
            ArrayPool<bool>.Shared.Return(finalized);
            ArrayPool<int>.Shared.Return(heapItems);
        }
    }

    // ── Lookup Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the flow direction at the given world grid coordinates.
    /// Returns <see cref="FlowDirection.None"/> if the coordinates are outside
    /// the computed region or the field is not valid.
    /// </summary>
    /// <param name="x">World grid X coordinate.</param>
    /// <param name="y">World grid Y coordinate.</param>
    public FlowDirection GetDirection(int x, int y)
    {
        if (!IsValid)
            return FlowDirection.None;

        int lx = x - RegionMinX;
        int ly = y - RegionMinY;

        if (lx < 0 || ly < 0 || lx >= RegionWidth || ly >= RegionHeight)
            return FlowDirection.None;

        return (FlowDirection)Directions[ly * RegionWidth + lx];
    }

    /// <summary>
    /// Returns a normalized <see cref="FixedVector2"/> for the flow direction at
    /// the given world grid coordinates. Returns <see cref="FixedVector2.Zero"/>
    /// if the cell has no direction or is outside the region.
    ///
    /// This is the primary method units call each tick to determine their
    /// movement vector.
    /// </summary>
    /// <param name="x">World grid X coordinate.</param>
    /// <param name="y">World grid Y coordinate.</param>
    public FixedVector2 GetDirectionVector(int x, int y)
    {
        FlowDirection dir = GetDirection(x, y);
        return DirectionVectors[(int)dir];
    }

    // ── Dijkstra Min-Heap ───────────────────────────────────────────────

    /// <summary>
    /// Specialised min-heap for Dijkstra's algorithm on the flow field.
    ///
    /// Stores packed cell indices (localY * regionWidth + localX) as ints.
    /// Priority is read from the flat integration-field array at index
    /// <c>packed</c>, so cost lookup is a single array access — no
    /// coordinate unpacking required.
    ///
    /// Accepts a pooled backing array to avoid allocation inside Generate().
    /// </summary>
    private sealed class DijkstraHeap
    {
        private readonly int[]        _items;
        private readonly int          _capacity;
        private readonly FixedPoint[] _costs; // flat integration field: costs[packed]
        private readonly int          _width; // region width for pack/unpack
        private int _count;

        public int Count => _count;

        public DijkstraHeap(int[] backingArray, int capacity, FixedPoint[] costs, int width)
        {
            _items    = backingArray;
            _capacity = capacity;
            _costs    = costs;
            _width    = width;
            _count    = 0;
        }

        /// <summary>Pushes (localX, localY) onto the heap.</summary>
        public void Push(int localX, int localY)
        {
            if (_count >= _capacity) return; // guard — should not happen with 4× capacity

            _items[_count] = localY * _width + localX;
            SiftUp(_count);
            _count++;
        }

        /// <summary>Pops and returns the (localX, localY) with the lowest cost.</summary>
        public (int localX, int localY) Pop()
        {
            int packed = _items[0];
            _count--;
            _items[0] = _items[_count];
            if (_count > 0) SiftDown(0);
            return (packed % _width, packed / _width);
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_costs[_items[index]] < _costs[_items[parent]])
                {
                    (_items[index], _items[parent]) = (_items[parent], _items[index]);
                    index = parent;
                }
                else break;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left     = (index << 1) + 1;
                int right    = (index << 1) + 2;
                int smallest = index;

                if (left  < _count && _costs[_items[left]]  < _costs[_items[smallest]]) smallest = left;
                if (right < _count && _costs[_items[right]] < _costs[_items[smallest]]) smallest = right;

                if (smallest == index) break;

                (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
                index = smallest;
            }
        }
    }
}
