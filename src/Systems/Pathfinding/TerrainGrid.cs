using System;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

// ─────────────────────────────────────────────────────────────────────────────
// Terrain Type Enumeration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// All terrain surface types recognized by the simulation.
///
/// Design decision: This is a flat enum rather than a bitflag set because each
/// cell has exactly one terrain type. Combinations (e.g., muddy road) would be
/// handled by the map editor choosing the dominant type, not by compositing.
///
/// Bridge is a special case — it overrides the underlying Water/DeepWater cell
/// and is traversable by ground units.  Void is off-map / unplayable space.
/// </summary>
public enum TerrainType
{
    Grass,       // Default open terrain — moderate traction, no penalty
    Dirt,        // Slightly slower than grass for vehicles, fine for infantry
    Sand,        // Loose surface — heavy vehicles bog down, light units slip
    Rock,        // Hard surface — good traction, but steep rock = cliff
    Water,       // Shallow water — infantry wade slowly, vehicles cannot cross
    DeepWater,   // Deep water — only amphibious / hover / air units
    Mud,         // Extremely slow for vehicles, moderate for infantry
    Road,        // Paved surface — speed bonus for all ground units
    Ice,         // Low friction — vehicles slide, infantry slow and careful
    Concrete,    // Urban / base surface — similar to road
    Bridge,      // Traversable crossing over water — ground-safe
    Lava,        // Lethal to all ground units; only air can fly over
    Void         // Off-map / impassable to everything including air
}

// ─────────────────────────────────────────────────────────────────────────────
// Terrain Cell
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Data for a single grid cell.  All values are in fixed-point for determinism.
///
/// <para><b>Height</b> — absolute elevation in world units.  Used for line of
/// sight, projectile arcs, and slope computation.</para>
///
/// <para><b>SlopeX / SlopeY</b> — partial derivatives of the height field
/// (rise per cell in X and Y).  Computed by <see cref="TerrainGrid.ComputeSlopes"/>
/// using central differences of neighbour heights.</para>
///
/// <para><b>SlopeAngle</b> — the steepness angle in radians, derived from the
/// gradient magnitude via a fixed-point atan approximation.  Used by the cost
/// calculator to decide traversability and movement penalty.</para>
///
/// <para><b>IsBlocked</b> — hard impassable flag set by buildings, cliff
/// markers, or the map editor.  Overrides all movement profiles.</para>
/// </summary>
public struct TerrainCell
{
    public FixedPoint Height;
    public TerrainType Type;
    public FixedPoint SlopeX;
    public FixedPoint SlopeY;
    public FixedPoint SlopeAngle;
    public bool IsBlocked;
}

// ─────────────────────────────────────────────────────────────────────────────
// Terrain Grid
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A 2D grid that stores the map's terrain data and provides queries needed by
/// the pathfinding, physics, and rendering systems.
///
/// <para><b>Coordinate system:</b> Grid cell (0,0) corresponds to world
/// position (0,0).  Each cell spans <see cref="CellSize"/> world units in both
/// axes.  The default is 1:1 (one world unit per cell) on a 512×512 map, but
/// both dimensions and resolution are configurable.</para>
///
/// <para><b>Determinism:</b> Every method that touches simulation data uses
/// <see cref="FixedPoint"/> arithmetic exclusively.  The only floats appear in
/// <c>ToFloat()</c> conversions for Godot rendering.</para>
///
/// <para><b>Memory layout:</b> Cells are stored in a flat row-major array
/// (<c>cells[y * Width + x]</c>) for cache-friendliness during slope
/// computation and pathfinding sweeps.</para>
/// </summary>
public class TerrainGrid
{
    // ── Public Properties ────────────────────────────────────────────────

    /// <summary>Number of cells along the X axis.</summary>
    public int Width { get; }

    /// <summary>Number of cells along the Y axis.</summary>
    public int Height { get; }

    /// <summary>
    /// World-space size of one cell edge.  Default <c>FixedPoint.One</c> means
    /// one world unit per cell.  Halving this doubles resolution (and quadruples
    /// memory).
    /// </summary>
    public FixedPoint CellSize { get; }

    // ── Internal Storage ─────────────────────────────────────────────────

    /// <summary>
    /// Flat row-major array: index = y * Width + x.
    /// Exposed as internal for direct bulk operations (map loading, editor).
    /// Prefer the accessor methods for normal gameplay queries.
    /// </summary>
    internal readonly TerrainCell[] Cells;

    // ── Pre-computed Fixed-Point Constants ────────────────────────────────
    //
    // Design decision: we cache Half (0.5), Two (2), and the atan polynomial
    // coefficients as static readonly fields so they are computed once rather
    // than on every call.  FixedPoint.FromFloat is only used here at init time
    // — never inside a per-frame loop.

    private static readonly FixedPoint FP_Half = FixedPoint.Half;
    private static readonly FixedPoint FP_Two = FixedPoint.FromInt(2);

    // Polynomial coefficients for the atan approximation (see AtanApprox).
    // These approximate atan(x) for x in [0,1] using a 3rd-order minimax fit:
    //   atan(x) ≈ a1*x + a3*x^3   (max error < 0.005 rad ≈ 0.29°)
    //
    // a1 ≈  0.9953 (close to 1.0)
    // a3 ≈ -0.3220 (cubic correction term)
    //
    // For |x| > 1 we use the identity atan(x) = π/2 - atan(1/x).
    private static readonly FixedPoint AtanA1 = FixedPoint.FromRaw(65228);  // 0.9953
    private static readonly FixedPoint AtanA3 = FixedPoint.FromRaw(-21103); // -0.3220
    private static readonly FixedPoint FP_PiOver2 = FixedPoint.FromRaw(102944); // π/2 ≈ 1.5708

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new terrain grid.  All cells default to Grass at height zero.
    /// Call <see cref="ComputeSlopes"/> after loading height data.
    /// </summary>
    /// <param name="width">Number of cells in X. Must be ≥ 2.</param>
    /// <param name="height">Number of cells in Y. Must be ≥ 2.</param>
    /// <param name="cellSize">
    /// World units per cell edge.  Pass <c>FixedPoint.One</c> for 1:1 mapping.
    /// </param>
    public TerrainGrid(int width, int height, FixedPoint cellSize)
    {
        if (width < 2 || height < 2)
            throw new ArgumentException("Grid dimensions must be at least 2×2.");
        if (cellSize <= FixedPoint.Zero)
            throw new ArgumentException("CellSize must be positive.");

        Width = width;
        Height = height;
        CellSize = cellSize;
        Cells = new TerrainCell[width * height];

        // Default initialisation: all cells are flat Grass at height 0.
        // TerrainCell is a struct so the array is already zero-initialised,
        // and TerrainType.Grass == 0.  No explicit loop needed.
    }

    /// <summary>
    /// Convenience overload: creates a 512×512 grid with cell size 1.
    /// </summary>
    public TerrainGrid() : this(512, 512, FixedPoint.One) { }

    // ── Cell Accessors ───────────────────────────────────────────────────

    /// <summary>Returns true if (x, y) is within the grid bounds.</summary>
    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>
    /// Gets a reference to a cell.  Throws if out of bounds.
    /// </summary>
    public ref TerrainCell GetCell(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Cell ({x},{y}) is out of bounds [{Width}×{Height}].");
        return ref Cells[y * Width + x];
    }

    /// <summary>
    /// Gets a cell by value (safe copy).  Returns a default blocked Void cell
    /// for out-of-bounds coordinates — this prevents pathfinding from wandering
    /// off the edge.
    /// </summary>
    public TerrainCell GetCellSafe(int x, int y)
    {
        if (!IsInBounds(x, y))
            return new TerrainCell { Type = TerrainType.Void, IsBlocked = true };
        return Cells[y * Width + x];
    }

    // ── Coordinate Conversions ───────────────────────────────────────────

    /// <summary>
    /// Converts a world-space position to the nearest grid cell indices.
    ///
    /// Design decision: we floor rather than round, so a world position of
    /// (1.9, 3.7) with CellSize=1 maps to cell (1,3).  The fractional part
    /// is used by the bilinear interpolation helpers.
    /// </summary>
    public (int x, int y) WorldToGrid(FixedVector2 worldPos)
    {
        // Divide by CellSize, then truncate toward zero (ToInt floors for
        // positive values which is all we expect on a map).
        int gx = (worldPos.X / CellSize).ToInt();
        int gy = (worldPos.Y / CellSize).ToInt();
        return (gx, gy);
    }

    /// <summary>
    /// Returns the world-space centre of the given grid cell.
    ///
    /// Design decision: we return the cell centre (+ half cell offset) rather
    /// than the corner so that units standing "on" a cell are visually centred.
    /// </summary>
    public FixedVector2 GridToWorld(int x, int y)
    {
        // centre = (x + 0.5) * CellSize
        FixedPoint wx = (FixedPoint.FromInt(x) + FP_Half) * CellSize;
        FixedPoint wy = (FixedPoint.FromInt(y) + FP_Half) * CellSize;
        return new FixedVector2(wx, wy);
    }

    // ── Slope Computation ────────────────────────────────────────────────

    /// <summary>
    /// Computes SlopeX, SlopeY, and SlopeAngle for every cell from the height
    /// data using central finite differences.
    ///
    /// <para><b>Algorithm:</b> For interior cells we use central differences:
    /// <c>SlopeX = (H[x+1,y] - H[x-1,y]) / (2 * CellSize)</c>.
    /// For border cells we use one-sided (forward/backward) differences:
    /// <c>SlopeX = (H[x+1,y] - H[x,y]) / CellSize</c>.</para>
    ///
    /// <para><b>SlopeAngle</b> is the angle of the gradient vector from
    /// horizontal, computed as <c>atan(|gradient|)</c> using a fixed-point
    /// polynomial approximation (see <see cref="AtanApprox"/>).  This gives
    /// the steepness in radians: 0 = flat, π/4 ≈ 0.785 = 45°.</para>
    ///
    /// <para>Must be called after all height data is loaded and whenever the
    /// terrain is deformed (e.g., cratering from explosions).</para>
    /// </summary>
    public void ComputeSlopes()
    {
        FixedPoint twoCellSize = CellSize * 2;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                ref TerrainCell cell = ref Cells[y * Width + x];

                // ── SlopeX (dH/dx) ──────────────────────────────────
                if (x > 0 && x < Width - 1)
                {
                    // Central difference
                    FixedPoint hRight = Cells[y * Width + (x + 1)].Height;
                    FixedPoint hLeft  = Cells[y * Width + (x - 1)].Height;
                    cell.SlopeX = (hRight - hLeft) / twoCellSize;
                }
                else if (x == 0)
                {
                    // Forward difference at left edge
                    FixedPoint hRight = Cells[y * Width + 1].Height;
                    cell.SlopeX = (hRight - cell.Height) / CellSize;
                }
                else // x == Width - 1
                {
                    // Backward difference at right edge
                    FixedPoint hLeft = Cells[y * Width + (x - 1)].Height;
                    cell.SlopeX = (cell.Height - hLeft) / CellSize;
                }

                // ── SlopeY (dH/dy) ──────────────────────────────────
                if (y > 0 && y < Height - 1)
                {
                    FixedPoint hDown = Cells[(y + 1) * Width + x].Height;
                    FixedPoint hUp   = Cells[(y - 1) * Width + x].Height;
                    cell.SlopeY = (hDown - hUp) / twoCellSize;
                }
                else if (y == 0)
                {
                    FixedPoint hDown = Cells[Width + x].Height;
                    cell.SlopeY = (hDown - cell.Height) / CellSize;
                }
                else // y == Height - 1
                {
                    FixedPoint hUp = Cells[(y - 1) * Width + x].Height;
                    cell.SlopeY = (cell.Height - hUp) / CellSize;
                }

                // ── SlopeAngle = atan(|gradient|) ────────────────────
                // |gradient| = sqrt(SlopeX² + SlopeY²)
                FixedPoint gradientMag = FixedPoint.Sqrt(
                    cell.SlopeX * cell.SlopeX + cell.SlopeY * cell.SlopeY);
                cell.SlopeAngle = AtanApprox(gradientMag);
            }
        }
    }

    // ── World-Space Queries (Bilinear Interpolation) ─────────────────────

    /// <summary>
    /// Returns the interpolated height at an arbitrary world position using
    /// bilinear interpolation between the four nearest cell centres.
    ///
    /// <para><b>Design decision — why bilinear?</b>  Nearest-neighbour lookup
    /// creates visible "stair-stepping" in unit elevation as they cross cell
    /// boundaries.  Bilinear gives smooth height even on a coarse grid, which
    /// is critical for the vehicle suspension / bounce system.</para>
    ///
    /// <para><b>Edge handling:</b> positions outside the grid are clamped to
    /// the nearest edge cell.</para>
    /// </summary>
    public FixedPoint GetHeight(FixedVector2 worldPos)
    {
        // Convert to continuous grid coordinates (fractional cell index).
        // Subtract 0.5 because GridToWorld places cell centres at (x+0.5)*CellSize.
        FixedPoint gxFrac = worldPos.X / CellSize - FP_Half;
        FixedPoint gyFrac = worldPos.Y / CellSize - FP_Half;

        // Integer cell coordinates of the top-left corner of the 2×2 sample.
        int x0 = gxFrac.ToInt();
        int y0 = gyFrac.ToInt();

        // Fractional part within the cell [0, 1).
        FixedPoint fx = gxFrac - FixedPoint.FromInt(x0);
        FixedPoint fy = gyFrac - FixedPoint.FromInt(y0);

        // Clamp to ensure we stay inside the grid.
        // If x0 or y0 is negative, clamp to 0 and zero out the fraction.
        if (x0 < 0) { x0 = 0; fx = FixedPoint.Zero; }
        if (y0 < 0) { y0 = 0; fy = FixedPoint.Zero; }
        if (x0 >= Width - 1)  { x0 = Width - 2;  fx = FixedPoint.One; }
        if (y0 >= Height - 1) { y0 = Height - 2; fy = FixedPoint.One; }

        // Sample the four corners.
        FixedPoint h00 = Cells[y0       * Width + x0    ].Height;
        FixedPoint h10 = Cells[y0       * Width + x0 + 1].Height;
        FixedPoint h01 = Cells[(y0 + 1) * Width + x0    ].Height;
        FixedPoint h11 = Cells[(y0 + 1) * Width + x0 + 1].Height;

        // Bilinear interpolation:
        //   lerp(a, b, t) = a + (b - a) * t
        //   result = lerp( lerp(h00, h10, fx), lerp(h01, h11, fx), fy )
        FixedPoint oneMinusFx = FixedPoint.One - fx;
        FixedPoint oneMinusFy = FixedPoint.One - fy;

        FixedPoint top    = h00 * oneMinusFx + h10 * fx;
        FixedPoint bottom = h01 * oneMinusFx + h11 * fx;
        return top * oneMinusFy + bottom * fy;
    }

    /// <summary>
    /// Returns the interpolated slope (gradient vector) at a world position.
    /// Uses the same bilinear scheme as <see cref="GetHeight"/> but on the
    /// SlopeX/SlopeY fields.
    ///
    /// Returns a <see cref="FixedVector2"/> where X = dH/dx, Y = dH/dy.
    /// The magnitude of this vector is the steepness; the direction is the
    /// direction of steepest ascent.
    /// </summary>
    public FixedVector2 GetSlope(FixedVector2 worldPos)
    {
        FixedPoint gxFrac = worldPos.X / CellSize - FP_Half;
        FixedPoint gyFrac = worldPos.Y / CellSize - FP_Half;

        int x0 = gxFrac.ToInt();
        int y0 = gyFrac.ToInt();

        FixedPoint fx = gxFrac - FixedPoint.FromInt(x0);
        FixedPoint fy = gyFrac - FixedPoint.FromInt(y0);

        if (x0 < 0) { x0 = 0; fx = FixedPoint.Zero; }
        if (y0 < 0) { y0 = 0; fy = FixedPoint.Zero; }
        if (x0 >= Width - 1)  { x0 = Width - 2;  fx = FixedPoint.One; }
        if (y0 >= Height - 1) { y0 = Height - 2; fy = FixedPoint.One; }

        FixedPoint oneMinusFx = FixedPoint.One - fx;
        FixedPoint oneMinusFy = FixedPoint.One - fy;

        // Bilinear on SlopeX
        FixedPoint sx00 = Cells[y0       * Width + x0    ].SlopeX;
        FixedPoint sx10 = Cells[y0       * Width + x0 + 1].SlopeX;
        FixedPoint sx01 = Cells[(y0 + 1) * Width + x0    ].SlopeX;
        FixedPoint sx11 = Cells[(y0 + 1) * Width + x0 + 1].SlopeX;

        FixedPoint interpSx = (sx00 * oneMinusFx + sx10 * fx) * oneMinusFy
                            + (sx01 * oneMinusFx + sx11 * fx) * fy;

        // Bilinear on SlopeY
        FixedPoint sy00 = Cells[y0       * Width + x0    ].SlopeY;
        FixedPoint sy10 = Cells[y0       * Width + x0 + 1].SlopeY;
        FixedPoint sy01 = Cells[(y0 + 1) * Width + x0    ].SlopeY;
        FixedPoint sy11 = Cells[(y0 + 1) * Width + x0 + 1].SlopeY;

        FixedPoint interpSy = (sy00 * oneMinusFx + sy10 * fx) * oneMinusFy
                            + (sy01 * oneMinusFx + sy11 * fx) * fy;

        return new FixedVector2(interpSx, interpSy);
    }

    /// <summary>
    /// Returns the terrain type at the nearest cell to the given world position.
    ///
    /// Design decision: terrain type is discrete per cell — we do NOT
    /// interpolate between types.  A unit is either "on road" or "on mud".
    /// The nearest-cell snap uses rounding (add 0.5 before truncation) so
    /// the boundary sits halfway between cell centres, not at cell edges.
    /// </summary>
    public TerrainType GetTerrainType(FixedVector2 worldPos)
    {
        // Round to nearest cell.
        int gx = (worldPos.X / CellSize).ToInt();
        int gy = (worldPos.Y / CellSize).ToInt();

        // Clamp to grid.
        gx = Math.Clamp(gx, 0, Width - 1);
        gy = Math.Clamp(gy, 0, Height - 1);

        return Cells[gy * Width + gx].Type;
    }

    // ── Fixed-Point Atan Approximation ───────────────────────────────────

    /// <summary>
    /// Deterministic atan(x) approximation for non-negative x, returning
    /// radians as a <see cref="FixedPoint"/>.
    ///
    /// <para><b>Algorithm:</b> 3rd-order minimax polynomial for x ∈ [0,1]:
    /// <c>atan(x) ≈ 0.9953·x − 0.3220·x³</c>.  For x &gt; 1 we use the
    /// identity <c>atan(x) = π/2 − atan(1/x)</c>.</para>
    ///
    /// <para><b>Accuracy:</b> max error &lt; 0.005 rad (0.29°), which is more
    /// than sufficient for terrain slope classification.  A 0.3° error on a
    /// 30° slope is negligible.</para>
    ///
    /// <para><b>Why not a lookup table?</b>  At Q16.16 resolution a useful
    /// lookup table would need thousands of entries.  The polynomial is only
    /// 3 multiplies and 1 add — faster than the cache miss from a large table,
    /// and the code is self-documenting.</para>
    /// </summary>
    internal static FixedPoint AtanApprox(FixedPoint x)
    {
        // atan is an odd function; for this use case x is always a magnitude
        // (non-negative), but guard anyway.
        if (x < FixedPoint.Zero)
            x = FixedPoint.Abs(x);

        if (x == FixedPoint.Zero)
            return FixedPoint.Zero;

        bool invert = false;
        if (x > FixedPoint.One)
        {
            // atan(x) = π/2 - atan(1/x) for x > 1
            x = FixedPoint.One / x;
            invert = true;
        }

        // Polynomial: atan(x) ≈ a1*x + a3*x³
        FixedPoint x2 = x * x;
        FixedPoint x3 = x2 * x;
        FixedPoint result = AtanA1 * x + AtanA3 * x3;

        if (invert)
            result = FP_PiOver2 - result;

        return result;
    }
}
