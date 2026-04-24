using System;
using CorditeWars.Core;
using CorditeWars.Game.Assets;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════════
// OCCUPANCY GRID — Dynamic blocking layer for units and buildings
// ═══════════════════════════════════════════════════════════════════════════════
//
// RELATIONSHIP TO TERRAIN GRID:
//   The TerrainGrid stores STATIC blocking (terrain type, cliffs, slopes, water).
//   This OccupancyGrid stores DYNAMIC blocking (units, buildings, reservations).
//
//   Pathfinding uses BOTH: a cell is passable only if the terrain allows it AND
//   the occupancy grid doesn't block it (or blocks it with a friendly unit that
//   can be nudged aside).
//
// REBUILD STRATEGY:
//   This grid is rebuilt from scratch every simulation tick — NOT incrementally
//   updated.  Why?
//
//   1. DETERMINISM: Incremental updates are error-prone.  If a unit dies and
//      doesn't properly vacate its cell, the grid becomes permanently corrupted.
//      Rebuilding from the authoritative unit list each tick guarantees the grid
//      always reflects the true game state.
//
//   2. SIMPLICITY: No need to track "which cells did this unit occupy last tick?"
//      or handle edge cases like units teleporting (being garrison-ejected, etc.).
//
//   3. PERFORMANCE: For 1000 units on a 512×512 grid, a full rebuild is ~1000
//      OccupyCell calls — trivially fast (<0.1ms).  Buildings are also cheap
//      since they're placed infrequently and their footprints are small.
//
//   The rebuild happens in UnitInteractionSystem.UpdateOccupancy(), called at
//   the start of each tick before pathfinding and movement.
//
// FRIENDLY PASSABILITY:
//   A key RTS convention (from C&C Generals, StarCraft, AoE): friendly units
//   can path through each other's cells.  They don't phase through instantly —
//   the steering system handles the actual separation — but the pathfinder
//   won't consider a friendly unit's cell as blocked.  This prevents pathing
//   deadlocks where a group of your own units can't navigate because they
//   block each other's path nodes.
//
//   Enemy units and buildings are always hard-blocked.
//
// RESERVED CELLS:
//   When a unit commits to moving into a cell (e.g., an APC docking, or a unit
//   being placed by the construction system), we "reserve" the destination cell.
//   This prevents two units from pathing to the same cell simultaneously.
//   The reservation is short-lived — it's replaced by a full Occupy when the
//   unit arrives, or cleared if the unit cancels.
//
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// What type of entity occupies a cell.
/// </summary>
public enum OccupancyType
{
    /// <summary>Cell is empty — fully passable.</summary>
    Empty = 0,

    /// <summary>Cell is occupied by a unit.  Friendly units can path through (nudge aside).</summary>
    Unit = 1,

    /// <summary>Cell is occupied by a building.  Always blocks movement — buildings never move.</summary>
    Building = 2,

    /// <summary>
    /// Cell is reserved by a unit that is moving into it.  Treated as blocked
    /// for pathfinding (so no other unit claims it) but not yet physically occupied.
    /// Prevents two units from targeting the same destination cell.
    /// </summary>
    Reserved = 3
}

/// <summary>
/// State of a single cell in the occupancy grid.
/// Value type (struct) to avoid heap allocation for the 262,144 cells of a 512×512 grid.
/// </summary>
public struct OccupancyCell
{
    /// <summary>What type of entity is in this cell.</summary>
    public OccupancyType Type;

    /// <summary>
    /// The unit or building ID occupying this cell.
    /// -1 if empty.  Used for click-selection ("what did the player click on?"),
    /// collision detection ("which unit am I colliding with?"), and building
    /// footprint queries.
    /// </summary>
    public int OccupantId;

    /// <summary>
    /// Player ID of the occupant.  -1 if empty.
    /// Used for friendly-passability checks: a unit owned by player 1 can
    /// path through cells occupied by other player-1 units, but not through
    /// cells occupied by player-2 units.
    /// </summary>
    public int PlayerId;
}

/// <summary>
/// 2D grid tracking which cells are occupied by units and buildings.
/// Dimensions match the TerrainGrid.  Rebuilt from unit positions each tick
/// for determinism.
/// </summary>
public class OccupancyGrid
{
    // ── Public Properties ────────────────────────────────────────────────────

    /// <summary>Grid width in cells.  Matches TerrainGrid.Width.</summary>
    public int Width { get; }

    /// <summary>Grid height in cells.  Matches TerrainGrid.Height.</summary>
    public int Height { get; }

    // ── Internal Storage ─────────────────────────────────────────────────────
    //
    // Flat row-major array: index = y * Width + x.
    // Using a flat array instead of OccupancyCell[,] allows Clear() to use a
    // single vectorised Span.Fill instead of a nested loop, and eliminates the
    // extra multiply that 2D-array indexing imposes on every access.

    /// <summary>
    /// The occupancy state for each grid cell.  Flat row-major layout:
    /// index = y * Width + x.
    /// </summary>
    public OccupancyCell[] Cells { get; }

    // Pre-baked "empty" cell value used for fast bulk clearing.
    private static readonly OccupancyCell EmptyCell = new OccupancyCell
    {
        Type       = OccupancyType.Empty,
        OccupantId = -1,
        PlayerId   = -1,
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an occupancy grid matching the given terrain dimensions.
    /// All cells start empty.
    /// </summary>
    /// <param name="width">Number of cells in the X axis (matches TerrainGrid.Width).</param>
    /// <param name="height">Number of cells in the Y axis (matches TerrainGrid.Height).</param>
    public OccupancyGrid(int width, int height)
    {
        if (width <= 0) throw new ArgumentException("Width must be positive.", nameof(width));
        if (height <= 0) throw new ArgumentException("Height must be positive.", nameof(height));

        Width  = width;
        Height = height;
        Cells  = new OccupancyCell[width * height];

        // Populate with the correct "empty" sentinel values.
        Clear();
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all cells to empty.  Called at the start of each tick before
    /// rebuilding from the current unit/building positions.
    /// Uses a single vectorised Span.Fill for maximum throughput.
    /// </summary>
    public void Clear()
    {
        Cells.AsSpan().Fill(EmptyCell);
    }

    // ── Occupy / Vacate ──────────────────────────────────────────────────────

    /// <summary>
    /// Marks a single cell as occupied by a unit or building.
    /// Silently ignores out-of-bounds coordinates (units at the world edge
    /// may have footprint cells outside the grid).
    /// </summary>
    /// <param name="x">Grid X coordinate.</param>
    /// <param name="y">Grid Y coordinate.</param>
    /// <param name="type">Occupancy type (Unit, Building, or Reserved).</param>
    /// <param name="occupantId">The ID of the unit or building.</param>
    /// <param name="playerId">The owner's player ID.</param>
    public void OccupyCell(int x, int y, OccupancyType type, int occupantId, int playerId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        ref OccupancyCell cell = ref Cells[y * Width + x];
        cell.Type       = type;
        cell.OccupantId = occupantId;
        cell.PlayerId   = playerId;
    }

    /// <summary>
    /// Marks a rectangular footprint as occupied.  Used for buildings (e.g.,
    /// a 3×3 War Factory) and large units (2×2 Overlord tank).
    /// The (x, y) is the top-left corner of the footprint.
    /// </summary>
    /// <param name="x">Top-left grid X coordinate of the footprint.</param>
    /// <param name="y">Top-left grid Y coordinate of the footprint.</param>
    /// <param name="width">Footprint width in cells.</param>
    /// <param name="height">Footprint height in cells.</param>
    /// <param name="type">Occupancy type.</param>
    /// <param name="occupantId">The ID of the unit or building.</param>
    /// <param name="playerId">The owner's player ID.</param>
    public void OccupyFootprint(int x, int y, int width, int height, OccupancyType type, int occupantId, int playerId)
    {
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                OccupyCell(x + dx, y + dy, type, occupantId, playerId);
            }
        }
    }

    /// <summary>
    /// Clears a single cell (marks as empty).
    /// Silently ignores out-of-bounds coordinates.
    /// </summary>
    /// <param name="x">Grid X coordinate.</param>
    /// <param name="y">Grid Y coordinate.</param>
    public void VacateCell(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        Cells[y * Width + x] = EmptyCell;
    }

    /// <summary>
    /// Clears a rectangular footprint (marks all cells as empty).
    /// </summary>
    /// <param name="x">Top-left grid X coordinate.</param>
    /// <param name="y">Top-left grid Y coordinate.</param>
    /// <param name="width">Footprint width in cells.</param>
    /// <param name="height">Footprint height in cells.</param>
    public void VacateFootprint(int x, int y, int width, int height)
    {
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                VacateCell(x + dx, y + dy);
            }
        }
    }

    /// <summary>
    /// Reserves a cell for a unit that is about to move into it.
    /// Prevents other pathfinding queries from claiming this cell as a
    /// destination.  The reservation is replaced by a full OccupyCell when
    /// the unit arrives, or cleared if the move is cancelled.
    /// </summary>
    /// <param name="x">Grid X coordinate.</param>
    /// <param name="y">Grid Y coordinate.</param>
    /// <param name="unitId">The unit claiming this cell.</param>
    /// <param name="playerId">The unit's owner.</param>
    public void ReserveCell(int x, int y, int unitId, int playerId)
    {
        OccupyCell(x, y, OccupancyType.Reserved, unitId, playerId);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the cell is completely empty (no unit, no building,
    /// no reservation).  Out-of-bounds cells are NOT free.
    /// </summary>
    public bool IsCellFree(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;

        return Cells[y * Width + x].Type == OccupancyType.Empty;
    }

    /// <summary>
    /// Returns true if a unit owned by <paramref name="requestingPlayerId"/>
    /// can path through this cell.
    ///
    /// Passability rules (inspired by C&amp;C Generals / StarCraft):
    /// <list type="bullet">
    ///   <item>Empty cells are always passable.</item>
    ///   <item>Friendly UNIT cells are passable (steering will nudge them apart).</item>
    ///   <item>Friendly RESERVED cells are NOT passable (someone already claimed it).</item>
    ///   <item>Buildings are NEVER passable (regardless of owner).</item>
    ///   <item>Enemy units are NOT passable (you can't walk through hostiles).</item>
    ///   <item>Out-of-bounds cells are NOT passable.</item>
    /// </list>
    /// </summary>
    /// <param name="x">Grid X coordinate.</param>
    /// <param name="y">Grid Y coordinate.</param>
    /// <param name="requestingPlayerId">The player ID of the unit trying to pass.</param>
    /// <returns>True if the cell can be traversed by this player's units.</returns>
    public bool IsCellPassable(int x, int y, int requestingPlayerId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;

        OccupancyCell cell = Cells[y * Width + x];

        // Empty is always passable
        if (cell.Type == OccupancyType.Empty)
            return true;

        // Buildings are never passable, regardless of owner
        if (cell.Type == OccupancyType.Building)
            return false;

        // Friendly units can be nudged aside — their cell is passable for pathfinding
        if (cell.Type == OccupancyType.Unit && cell.PlayerId == requestingPlayerId)
            return true;

        // Reserved cells, enemy units, and anything else block movement
        return false;
    }

    /// <summary>
    /// Returns true if the entire rectangular footprint is free (all cells Empty).
    /// Used for building placement validation: "Can I place a 3×3 building here?"
    /// </summary>
    /// <param name="x">Top-left grid X coordinate.</param>
    /// <param name="y">Top-left grid Y coordinate.</param>
    /// <param name="width">Footprint width in cells.</param>
    /// <param name="height">Footprint height in cells.</param>
    /// <returns>True if every cell in the footprint is empty.</returns>
    public bool IsFootprintFree(int x, int y, int width, int height)
    {
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                if (!IsCellFree(x + dx, y + dy))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns the full occupancy info for a cell.  Returns an empty cell
    /// (Type=Empty, OccupantId=-1, PlayerId=-1) for out-of-bounds coordinates.
    /// </summary>
    public OccupancyCell GetCell(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return EmptyCell;

        return Cells[y * Width + x];
    }

    /// <summary>
    /// Returns the occupant ID at the given cell, or -1 if empty or out of bounds.
    /// Convenience method for click-selection and collision queries.
    /// </summary>
    public int GetOccupantAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return -1;

        return Cells[y * Width + x].OccupantId;
    }

    // ── AssetRegistry Integration ───────────────────────────────────────

    /// <summary>
    /// Occupies a unit's footprint using the <see cref="AssetRegistry"/> to look up
    /// the correct footprint size for the unit type, instead of hard-coded values.
    /// The unit's center position is converted to the top-left grid cell of its footprint.
    /// </summary>
    /// <param name="centerX">Grid X coordinate of the unit's center cell.</param>
    /// <param name="centerY">Grid Y coordinate of the unit's center cell.</param>
    /// <param name="dataId">Unit type ID for registry lookup (e.g., "valkyr_zephyr_buggy").</param>
    /// <param name="occupantId">The runtime unit instance ID.</param>
    /// <param name="playerId">The owning player ID.</param>
    /// <param name="registry">The asset registry to look up footprint size from.</param>
    public void OccupyUnitFootprint(int centerX, int centerY, string dataId, int occupantId, int playerId, AssetRegistry registry)
    {
        var (width, height) = registry.GetFootprint(dataId);
        int topLeftX = centerX - width / 2;
        int topLeftY = centerY - height / 2;
        OccupyFootprint(topLeftX, topLeftY, width, height, OccupancyType.Unit, occupantId, playerId);
    }

    /// <summary>
    /// Vacates a unit's footprint using the <see cref="AssetRegistry"/> to look up
    /// the correct footprint size.
    /// </summary>
    /// <param name="centerX">Grid X coordinate of the unit's center cell.</param>
    /// <param name="centerY">Grid Y coordinate of the unit's center cell.</param>
    /// <param name="dataId">Unit type ID for registry lookup.</param>
    /// <param name="registry">The asset registry to look up footprint size from.</param>
    public void VacateUnitFootprint(int centerX, int centerY, string dataId, AssetRegistry registry)
    {
        var (width, height) = registry.GetFootprint(dataId);
        int topLeftX = centerX - width / 2;
        int topLeftY = centerY - height / 2;
        VacateFootprint(topLeftX, topLeftY, width, height);
    }

    /// <summary>
    /// Checks if a unit's full footprint is free, using the <see cref="AssetRegistry"/>
    /// to look up the correct footprint size.
    /// </summary>
    /// <param name="centerX">Grid X coordinate of the unit's center cell.</param>
    /// <param name="centerY">Grid Y coordinate of the unit's center cell.</param>
    /// <param name="dataId">Unit type ID for registry lookup.</param>
    /// <param name="registry">The asset registry to look up footprint size from.</param>
    /// <returns>True if every cell in the footprint is empty.</returns>
    public bool IsUnitFootprintFree(int centerX, int centerY, string dataId, AssetRegistry registry)
    {
        var (width, height) = registry.GetFootprint(dataId);
        int topLeftX = centerX - width / 2;
        int topLeftY = centerY - height / 2;
        return IsFootprintFree(topLeftX, topLeftY, width, height);
    }
}
