using System.Collections.Generic;
using System.Runtime.InteropServices;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Systems.FogOfWar;

// ─────────────────────────────────────────────────────────────────────────────
//  VisionComponent
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight data component attached to every entity that provides vision.
/// Intended to be gathered into a flat list each tick and fed into
/// <see cref="VisionSystem.UpdateVision"/>.
/// </summary>
public struct VisionComponent
{
    /// <summary>Unique entity identifier.</summary>
    public int UnitId;

    /// <summary>Owning player (must match the <see cref="FogGrid.PlayerId"/>).</summary>
    public int PlayerId;

    /// <summary>Current world-space position of the unit.</summary>
    public FixedVector2 Position;

    /// <summary>Base sight range expressed in grid cells.</summary>
    public FixedPoint SightRange;

    /// <summary>
    /// The unit's current height (terrain height at its position, or
    /// flying altitude for air units). Used for line-of-sight checks.
    /// </summary>
    public FixedPoint Height;

    /// <summary>
    /// Air units ignore terrain line-of-sight blocking and receive no
    /// elevation bonus (they already fly above everything).
    /// </summary>
    public bool IsAirUnit;
}

// ─────────────────────────────────────────────────────────────────────────────
//  VisionSystem
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic per-tick vision calculation. Uses a full-recalculation
/// approach — every tick the fog is reset and then rebuilt from scratch
/// using reference counting. Simple, correct, and easy to reason about.
/// </summary>
public class VisionSystem
{
    // ── Tuning constants (fixed-point) ──────────────────────────────────

    /// <summary>
    /// Maximum extra sight range percentage an elevated unit can gain.
    /// A value of 0.5 means up to +50 % sight range at maximum height advantage.
    /// </summary>
    private static readonly FixedPoint MaxElevationBonus = FixedPoint.FromFloat(0.5f);

    /// <summary>
    /// Divisor that controls how quickly height difference translates to bonus.
    /// Higher values dampen the bonus. Expressed in terrain-height units.
    /// </summary>
    private static readonly FixedPoint ElevationBonusScale = FixedPoint.FromFloat(10.0f);

    /// <summary>
    /// Step size along the LOS ray, in fractions of a grid cell.
    /// Smaller = more accurate but slower. 0.5 is a good balance.
    /// </summary>
    private static readonly FixedPoint RayStepSize = FixedPoint.Half;

    // ── Reusable buffers to avoid per-tick allocations ──────────────────

    /// <summary>
    /// Cached lists of cells within a circle of a given integer radius.
    /// Indexed directly by radius; avoids Dictionary lookup overhead and
    /// non-deterministic hash iteration.
    /// Indices 0..MaxCachedRadius are pre-sized; larger radii fall back to
    /// an on-demand (uncached) computation.
    /// 64 covers all practical RTS sight ranges (typical units: 5–20 cells;
    /// extreme "reveal map" abilities: up to ~50 cells). Anything beyond that
    /// is exceedingly rare and not worth a permanent cache entry.
    /// </summary>
    private const int MaxCachedRadius = 64;
    private readonly List<(int dx, int dy)>?[] _radiusCache = new List<(int dx, int dy)>?[MaxCachedRadius + 1];

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Full vision recalculation for one player. Call once per simulation tick.
    /// <list type="number">
    ///   <item>Resets all visibility ref counts on the fog grid.</item>
    ///   <item>For every unit belonging to this player, computes visible cells
    ///         and marks them on the grid.</item>
    /// </list>
    /// </summary>
    /// <param name="fog">The player's fog grid (will be mutated).</param>
    /// <param name="terrain">The shared terrain grid (read-only).</param>
    /// <param name="units">All vision-providing entities for this player.</param>
    public void UpdateVision(FogGrid fog, TerrainGrid terrain, List<VisionComponent> units)
    {
        // Step 1 — clear previous tick's visibility
        fog.ResetVisibility();

        // Step 2 — rebuild from each unit
        for (int i = 0; i < units.Count; i++)
        {
            ref VisionComponent unit = ref System.Runtime.InteropServices.CollectionsMarshal
                .AsSpan(units)[i];

            if (unit.PlayerId != fog.PlayerId)
                continue;

            ComputeUnitVision(fog, terrain, in unit);
        }
    }

    /// <summary>
    /// Computes and applies the set of cells visible to a single unit.
    /// </summary>
    public void ComputeUnitVision(FogGrid fog, TerrainGrid terrain, in VisionComponent unit)
    {
        // Convert world position to grid coordinates
        (int cx, int cy) = terrain.WorldToGrid(unit.Position);

        // Effective sight range (including elevation bonus for ground units)
        FixedPoint effectiveRange = GetEffectiveSightRange(terrain, in unit);
        int radiusCells = effectiveRange.ToInt();
        if (radiusCells < 1) radiusCells = 1;

        // Retrieve (or build) the pre-computed circle offsets
        List<(int dx, int dy)> offsets = GetCellsInRadius(radiusCells);

        FixedPoint rangeSq = effectiveRange * effectiveRange;

        for (int i = 0; i < offsets.Count; i++)
        {
            (int dx, int dy) = offsets[i];
            int cellX = cx + dx;
            int cellY = cy + dy;

            // Bounds check
            if (!fog.IsInBounds(cellX, cellY))
                continue;

            // Precise distance check (the cached circle is conservative)
            FixedPoint distSq = FixedPoint.FromInt(dx * dx + dy * dy);
            if (distSq > rangeSq)
                continue;

            // Line-of-sight check
            if (unit.IsAirUnit || HasLineOfSight(terrain, unit.Position, unit.Height, cellX, cellY))
            {
                fog.AddVisibility(cellX, cellY);
            }
        }
    }

    // ── Line of Sight ───────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a ground-level observer can see a specific grid cell
    /// by raytracing across the terrain. Uses Bresenham-style grid stepping
    /// with fixed-point height interpolation.
    /// </summary>
    /// <param name="terrain">Terrain grid for height lookups.</param>
    /// <param name="from">Observer world position.</param>
    /// <param name="fromHeight">Observer eye-level height.</param>
    /// <param name="toX">Target cell X.</param>
    /// <param name="toY">Target cell Y.</param>
    /// <returns>True if the line of sight is unobstructed.</returns>
    public bool HasLineOfSight(TerrainGrid terrain, FixedVector2 from, FixedPoint fromHeight, int toX, int toY)
    {
        // Convert target cell to world-space center
        FixedVector2 toWorld = terrain.GridToWorld(toX, toY);

        // Direction vector from observer to target
        FixedVector2 delta = toWorld - from;
        FixedPoint   dist  = delta.Length;

        // Trivially visible if same cell or adjacent
        if (dist <= FixedPoint.One)
            return true;

        // Precompute reciprocal for t computation; precompute step increments
        // as raw FixedPoint components to avoid creating a FixedVector2 per step.
        FixedPoint invDist  = FixedPoint.One / dist;
        FixedPoint stepX    = delta.X * invDist * RayStepSize;
        FixedPoint stepY    = delta.Y * invDist * RayStepSize;

        // Target terrain height (used for linear interpolation of expected height)
        FixedPoint toHeight  = terrain.GetHeight(toWorld);
        FixedPoint heightDelta = toHeight - fromHeight;

        // Number of steps and how much t advances per step
        int        stepCount = (dist / RayStepSize).ToInt();
        FixedPoint tStep     = RayStepSize * invDist; // advance in [0,1] per step

        // Walk along the ray; track sample position with raw FixedPoint
        // to avoid allocating a new FixedVector2 struct every iteration.
        FixedPoint sampleX = from.X + stepX; // start at step i=1
        FixedPoint sampleY = from.Y + stepY;
        FixedPoint t       = tStep;

        for (int i = 1; i < stepCount; i++, sampleX += stepX, sampleY += stepY, t += tStep)
        {
            // Expected LOS height at this fraction along the ray
            FixedPoint expectedHeight = fromHeight + heightDelta * t;

            // Actual terrain height at the sample point
            FixedPoint terrainHeight = terrain.GetHeight(new FixedVector2(sampleX, sampleY));

            if (terrainHeight > expectedHeight)
                return false;
        }

        return true;
    }

    // ── Elevation Bonus ─────────────────────────────────────────────────

    /// <summary>
    /// Calculates the effective sight range for a unit, including the
    /// elevation advantage bonus for ground units on high terrain.
    /// </summary>
    private FixedPoint GetEffectiveSightRange(TerrainGrid terrain, in VisionComponent unit)
    {
        if (unit.IsAirUnit)
            return unit.SightRange;

        // Average terrain height — we use the unit's current position height as
        // a proxy for "average". The bonus comes from being ABOVE average.
        // We compare against a baseline of zero; any positive height helps.
        FixedPoint heightAdvantage = FixedPoint.Max(unit.Height, FixedPoint.Zero);

        // Normalized bonus: clamp(heightAdvantage / scale, 0, maxBonus)
        FixedPoint normalizedBonus;
        if (ElevationBonusScale > FixedPoint.Zero)
            normalizedBonus = FixedPoint.Clamp(
                heightAdvantage / ElevationBonusScale,
                FixedPoint.Zero,
                MaxElevationBonus
            );
        else
            normalizedBonus = FixedPoint.Zero;

        // Effective range = baseRange * (1 + bonus)
        return unit.SightRange * (FixedPoint.One + normalizedBonus);
    }

    // ── Circle Cell Enumeration ─────────────────────────────────────────

    /// <summary>
    /// Returns all integer (dx, dy) offsets within a filled circle of the
    /// given radius. Uses the midpoint circle algorithm for the perimeter
    /// and fills the interior. Results are cached by radius.
    /// </summary>
    /// <param name="radius">Radius in grid cells.</param>
    /// <returns>
    /// List of (dx, dy) offsets relative to the center (0,0). Includes (0,0).
    /// </returns>
    public List<(int dx, int dy)> GetCellsInRadius(int radius)
    {
        // Fast path: direct array lookup for common radii.
        if (radius <= MaxCachedRadius)
        {
            if (_radiusCache[radius] is not null)
                return _radiusCache[radius]!;

            var cells = BuildCellsInRadius(radius);
            _radiusCache[radius] = cells;
            return cells;
        }

        // Uncommon large radius — compute on demand without caching.
        return BuildCellsInRadius(radius);
    }

    private static List<(int dx, int dy)> BuildCellsInRadius(int radius)
    {
        var cells = new List<(int dx, int dy)>();
        int rSq   = radius * radius;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy <= rSq)
                    cells.Add((dx, dy));
            }
        }

        return cells;
    }
}
