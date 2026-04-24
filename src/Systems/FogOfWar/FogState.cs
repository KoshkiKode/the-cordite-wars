namespace CorditeWars.Systems.FogOfWar;

// ─────────────────────────────────────────────────────────────────────────────
//  Enums
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The three C&amp;C-style fog-of-war visibility states.
/// </summary>
public enum FogVisibility : byte
{
    /// <summary>Never seen by this player. Rendered as solid black.</summary>
    Unexplored = 0,

    /// <summary>
    /// Was visible at some point but no friendly unit currently sees it.
    /// Shows last-known terrain (greyed out) and ghosted buildings.
    /// </summary>
    Explored = 1,

    /// <summary>Currently in sight of at least one friendly unit.</summary>
    Visible = 2
}

/// <summary>
/// Determines the initial fog state for a match.
/// </summary>
public enum FogMode : byte
{
    /// <summary>
    /// Campaign mode — map starts fully unexplored (black).
    /// </summary>
    Campaign = 0,

    /// <summary>
    /// Skirmish / multiplayer — terrain is pre-explored but units and
    /// buildings are hidden until scouted.
    /// </summary>
    Skirmish = 1
}

// ─────────────────────────────────────────────────────────────────────────────
//  FogCell
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-cell fog state. Stored in a flat grid owned by <see cref="FogGrid"/>.
/// </summary>
public struct FogCell
{
    /// <summary>Current visibility state for this cell.</summary>
    public FogVisibility Visibility;

    /// <summary>
    /// Number of friendly units currently able to see this cell.
    /// When &gt; 0 the cell is <see cref="FogVisibility.Visible"/>.
    /// When it drops to 0 the cell transitions to <see cref="FogVisibility.Explored"/>.
    /// </summary>
    public ushort VisibilityRefCount;

    /// <summary>
    /// Once true, this cell can never return to <see cref="FogVisibility.Unexplored"/>.
    /// </summary>
    public bool WasEverVisible;
}

// ─────────────────────────────────────────────────────────────────────────────
//  FogGrid
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-player fog-of-war grid. Each player maintains their own instance so
/// visibility is completely independent. Dimensions match the terrain grid.
/// </summary>
public class FogGrid
{
    /// <summary>Grid width in cells (matches <c>TerrainGrid.Width</c>).</summary>
    public int Width { get; }

    /// <summary>Grid height in cells (matches <c>TerrainGrid.Height</c>).</summary>
    public int Height { get; }

    /// <summary>The owning player's ID.</summary>
    public int PlayerId { get; }

    /// <summary>
    /// Flat row-major cell array. Index = y * Width + x.
    /// Using a flat array rather than <c>FogCell[,]</c> lets bulk operations
    /// (constructor init, ResetVisibility, RevealAll) use Span.Fill for
    /// vectorised throughput, and gives the renderer sequential read access.
    /// </summary>
    public FogCell[] Cells { get; }

    // Pre-baked cell values for fast bulk fills.
    private static readonly FogCell ExploredCell = new FogCell
    {
        Visibility        = FogVisibility.Explored,
        VisibilityRefCount = 0,
        WasEverVisible    = true,
    };
    private static readonly FogCell RevealedCell = new FogCell
    {
        Visibility        = FogVisibility.Visible,
        VisibilityRefCount = 1,
        WasEverVisible    = true,
    };

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new fog grid for the given player.
    /// </summary>
    /// <param name="width">Grid width (cells).</param>
    /// <param name="height">Grid height (cells).</param>
    /// <param name="playerId">Owning player.</param>
    /// <param name="mode">
    ///   <see cref="FogMode.Campaign"/>: all cells start Unexplored.<br/>
    ///   <see cref="FogMode.Skirmish"/>: all cells start Explored (terrain
    ///   revealed, units/buildings hidden until scouted).
    /// </param>
    public FogGrid(int width, int height, int playerId, FogMode mode)
    {
        Width    = width;
        Height   = height;
        PlayerId = playerId;
        Cells    = new FogCell[width * height];

        if (mode == FogMode.Skirmish)
            Cells.AsSpan().Fill(ExploredCell);
        // Campaign: default struct values → Unexplored, refcount 0, WasEverVisible false
    }

    // ── Queries ──────────────────────────────────────────────────────────

    /// <summary>Returns the visibility state of the cell at (x, y).</summary>
    public FogVisibility GetVisibility(int x, int y)
    {
        if (!IsInBounds(x, y)) return FogVisibility.Unexplored;
        return Cells[y * Width + x].Visibility;
    }

    /// <summary>Shorthand: is the cell currently visible to this player?</summary>
    public bool IsVisible(int x, int y)
    {
        return IsInBounds(x, y) && Cells[y * Width + x].Visibility == FogVisibility.Visible;
    }

    /// <summary>Shorthand: has the cell ever been seen (Explored or Visible)?</summary>
    public bool IsExplored(int x, int y)
    {
        return IsInBounds(x, y) && Cells[y * Width + x].Visibility >= FogVisibility.Explored;
    }

    // ── Visibility Manipulation ──────────────────────────────────────────

    /// <summary>
    /// Increments the visibility reference count for this cell.
    /// Called when a friendly unit can see this cell.
    /// </summary>
    public void AddVisibility(int x, int y)
    {
        if (!IsInBounds(x, y)) return;

        ref FogCell cell = ref Cells[y * Width + x];
        cell.VisibilityRefCount++;
        cell.Visibility     = FogVisibility.Visible;
        cell.WasEverVisible = true;
    }

    /// <summary>
    /// Decrements the visibility reference count for this cell.
    /// When the count reaches 0 the cell transitions to Explored.
    /// </summary>
    public void RemoveVisibility(int x, int y)
    {
        if (!IsInBounds(x, y)) return;

        ref FogCell cell = ref Cells[y * Width + x];

        if (cell.VisibilityRefCount > 0)
            cell.VisibilityRefCount--;

        if (cell.VisibilityRefCount == 0 && cell.Visibility == FogVisibility.Visible)
            cell.Visibility = FogVisibility.Explored;
    }

    /// <summary>
    /// Called at the start of each vision update tick.<br/>
    /// Resets all reference counts to 0 and transitions previously-Visible
    /// cells to Explored. Cells that were Unexplored stay Unexplored.
    /// The <see cref="VisionSystem"/> will then re-add visibility for every
    /// unit, rebuilding accurate ref counts from scratch.
    /// Uses a single flat pass over the array for cache-friendly throughput.
    /// </summary>
    public void ResetVisibility()
    {
        var span = Cells.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            ref FogCell cell = ref span[i];
            cell.VisibilityRefCount = 0;
            if (cell.Visibility == FogVisibility.Visible)
                cell.Visibility = FogVisibility.Explored;
        }
    }

    // ── Debug / Cheat ────────────────────────────────────────────────────

    /// <summary>
    /// Sets every cell to Visible with a ref count of 1 and marks
    /// WasEverVisible. Useful for debugging or a "reveal map" cheat.
    /// </summary>
    public void RevealAll()
    {
        Cells.AsSpan().Fill(RevealedCell);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Returns true if (x, y) is within grid bounds.</summary>
    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }
}
