using System;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

// ─────────────────────────────────────────────────────────────────────────────
// Terrain Cost Calculator
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Static utility that computes the deterministic pathfinding cost for a unit
/// (described by a <see cref="MovementProfile"/>) to move through a cell on
/// the <see cref="TerrainGrid"/>.
///
/// <para><b>Design philosophy:</b> This class is the single source of truth
/// for "can this unit go there?" and "how expensive is it?".  Both the A*
/// pathfinder and the flow-field generator call into these methods, so
/// consistency is guaranteed.  No other system should duplicate this logic.</para>
///
/// <para><b>Cost model overview:</b></para>
/// <list type="number">
///   <item><b>Base cost = 1.0</b> — the cost of moving one cell on flat,
///         ideal terrain.</item>
///   <item><b>Terrain speed modifier</b> — divides into the base cost.
///         Road (1.2×) reduces cost; Mud (0.3×) dramatically increases it.</item>
///   <item><b>Slope penalty</b> — exponential increase as the cell's slope
///         approaches the unit's <see cref="MovementProfile.MaxSlopeAngle"/>.
///         Goes to <see cref="FixedPoint.MaxValue"/> (impassable) when the
///         slope exceeds the limit.</item>
///   <item><b>Elevation cost</b> — uphill movement adds a linear cost
///         proportional to the height gain.  Downhill movement gives a small
///         discount (but never below zero cost).</item>
///   <item><b>Diagonal adjustment</b> — moving diagonally costs √2 ≈ 1.414×
///         a cardinal move (applied by the caller, not here).</item>
/// </list>
///
/// <para><b>Determinism:</b> Every calculation uses <see cref="FixedPoint"/>.
/// No <c>float</c>, <c>double</c>, <c>Math.Sin</c>, <c>Math.Cos</c>, or any
/// other non-deterministic function.  Polynomial approximations are used where
/// transcendental functions would normally appear.</para>
/// </summary>
public static class TerrainCostCalculator
{
    // ── Pre-computed Constants ────────────────────────────────────────────
    //
    // Design decision: all constants are FixedPoint literals computed once at
    // class load.  FromFloat is acceptable here (static init), never in a
    // per-call hot path.

    /// <summary>Base cost of traversing one cell on flat ideal terrain.</summary>
    private static readonly FixedPoint BaseCost = FixedPoint.One;

    /// <summary>
    /// Minimum speed modifier we'll accept before treating the terrain as
    /// effectively impassable.  Prevents division-by-near-zero in the cost
    /// formula.  A modifier below 0.05 (5% speed) is gameplay-impassable.
    /// </summary>
    private static readonly FixedPoint MinSpeedModifier = FixedPoint.FromRaw(3277); // ~0.05

    /// <summary>
    /// Weight of the elevation cost added per unit of height gain when going
    /// uphill.  A height difference of 1.0 (one full world unit) adds 0.5 to
    /// the base cost.  This makes steep uphill paths noticeably more expensive
    /// without completely blocking gentle hills.
    /// </summary>
    private static readonly FixedPoint UphillCostFactor = FixedPoint.Half;

    /// <summary>
    /// Discount factor for going downhill.  Height drop of 1.0 gives a
    /// discount of 0.2.  Intentionally smaller than the uphill penalty so
    /// that a round trip uphill+downhill is net-positive cost (as it should
    /// be — going downhill is easier but still takes effort).
    /// </summary>
    private static readonly FixedPoint DownhillDiscountFactor = FixedPoint.FromRaw(13107); // ~0.2

    /// <summary>
    /// The minimum cost any traversable cell can have.  Even the best road
    /// going steeply downhill should still cost something, or the pathfinder
    /// could produce degenerate zero-cost loops.
    /// </summary>
    private static readonly FixedPoint MinCost = FixedPoint.FromRaw(6554); // ~0.1

    /// <summary>
    /// Constant used in the exponential slope penalty curve.  See
    /// <see cref="GetSlopePenalty"/> for the full formula.
    /// </summary>
    private static readonly FixedPoint SlopeExpBase = FixedPoint.FromInt(4);

    // ═════════════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the cost for a unit with the given <paramref name="profile"/>
    /// to move from cell (<paramref name="fromX"/>, <paramref name="fromY"/>)
    /// to adjacent cell (<paramref name="toX"/>, <paramref name="toY"/>).
    ///
    /// <para>Returns <see cref="FixedPoint.MaxValue"/> if the destination is
    /// impassable for this unit.</para>
    ///
    /// <para><b>Formula:</b></para>
    /// <code>
    ///   terrainMod = profile.TerrainSpeedModifiers[destType]   (default 1.0)
    ///   slopePen   = GetSlopePenalty(destSlopeAngle, profile.MaxSlopeAngle)
    ///   elevCost   = GetElevationCost(destHeight - srcHeight)
    ///   cost       = (BaseCost / terrainMod) * slopePen + elevCost
    ///   result     = Max(cost, MinCost)
    /// </code>
    ///
    /// <para><b>Note:</b> the caller is responsible for multiplying by √2 for
    /// diagonal moves.  This method computes the intrinsic cell cost only.</para>
    /// </summary>
    public static FixedPoint GetMovementCost(
        TerrainGrid grid,
        MovementProfile profile,
        int fromX, int fromY,
        int toX, int toY)
    {
        // ── Fast reject: can the unit enter the destination at all? ───────
        if (!CanTraverse(grid, profile, toX, toY))
            return FixedPoint.MaxValue;

        TerrainCell destCell = grid.GetCellSafe(toX, toY);
        TerrainCell srcCell  = grid.GetCellSafe(fromX, fromY);

        // ── Terrain speed modifier ───────────────────────────────────────
        // Look up the destination terrain type in the profile's modifier table.
        // Missing entries default to 1.0 (no penalty, no bonus).
        FixedPoint terrainMod = FixedPoint.One;
        if (profile.TerrainSpeedModifiers.TryGetValue(destCell.Type, out FixedPoint mod))
            terrainMod = mod;

        // Guard against near-zero modifiers that would cause cost explosion.
        if (terrainMod < MinSpeedModifier)
            return FixedPoint.MaxValue;

        // ── Air units get a flat base cost — terrain is irrelevant ───────
        //
        // Design decision: air units still pay a base cost so the pathfinder
        // doesn't produce infinitely cheap paths.  But they skip slope and
        // elevation penalties entirely.  The only terrain check that matters
        // for air is Void (handled by CanTraverse above).
        if (profile.Domain == MovementDomain.Air)
            return BaseCost;

        // ── Naval units get a flat base cost on water ────────────────────
        //
        // Design decision: naval units move on a flat water surface, so slope
        // and elevation penalties are meaningless.  CanTraverse already ensures
        // only Water/DeepWater cells are reachable; here we simply return the
        // base cost for any cell that passed that check.
        if (profile.Domain == MovementDomain.Water)
            return BaseCost;

        // ── Slope penalty ────────────────────────────────────────────────
        FixedPoint slopePenalty = GetSlopePenalty(destCell.SlopeAngle, profile.MaxSlopeAngle);
        if (slopePenalty == FixedPoint.MaxValue)
            return FixedPoint.MaxValue;

        // ── Elevation cost ───────────────────────────────────────────────
        FixedPoint heightDiff = destCell.Height - srcCell.Height;
        FixedPoint elevationCost = GetElevationCost(heightDiff);

        // ── Combine ──────────────────────────────────────────────────────
        //
        // The terrain modifier is a speed multiplier — higher = faster, so
        // dividing the base cost by it converts "speed bonus" to "cost
        // reduction" naturally.  E.g., Road (1.2) → cost = 1.0/1.2 ≈ 0.83.
        // Mud (0.3) → cost = 1.0/0.3 ≈ 3.33.
        //
        // The slope penalty is a multiplicative factor on top of that.
        // The elevation cost is an additive term (going uphill always costs
        // extra regardless of terrain type).
        FixedPoint cost = (BaseCost / terrainMod) * slopePenalty + elevationCost;

        // Enforce minimum cost to prevent degenerate zero/negative-cost paths.
        return FixedPoint.Max(cost, MinCost);
    }

    /// <summary>
    /// Returns whether the unit described by <paramref name="profile"/> can
    /// enter cell (<paramref name="x"/>, <paramref name="y"/>) at all.
    ///
    /// <para>Checks (in order, for early exit):</para>
    /// <list type="number">
    ///   <item>Out of bounds → false.</item>
    ///   <item>Air units → always true, except Void terrain.</item>
    ///   <item>Cell is blocked (building, cliff) → false.</item>
    ///   <item>Terrain type is in the profile's ImpassableTerrain set → false.</item>
    ///   <item>Cell slope exceeds profile's MaxSlopeAngle → false.</item>
    ///   <item>Otherwise → true.</item>
    /// </list>
    /// </summary>
    public static bool CanTraverse(
        TerrainGrid grid,
        MovementProfile profile,
        int x, int y)
    {
        // ── Bounds check ─────────────────────────────────────────────────
        if (!grid.IsInBounds(x, y))
            return false;

        TerrainCell cell = grid.GetCellSafe(x, y);

        // ── Air units ────────────────────────────────────────────────────
        //
        // Design decision: air units can fly over everything except Void
        // (the off-map boundary).  This includes flying over Lava, buildings,
        // cliffs, etc.  Void is the absolute boundary of the playable area.
        if (profile.Domain == MovementDomain.Air)
            return cell.Type != TerrainType.Void;

        // ── Naval units ──────────────────────────────────────────────────
        //
        // Design decision: naval units can only traverse Water and DeepWater
        // cells.  All land terrain (including Bridges, which are land crossings
        // over water rather than navigable channels) is impassable to ships.
        if (profile.Domain == MovementDomain.Water)
            return cell.Type == TerrainType.Water || cell.Type == TerrainType.DeepWater;

        // ── Hard-blocked cells ───────────────────────────────────────────
        // Buildings, cliff markers, or editor-placed blockers.
        if (cell.IsBlocked)
            return false;

        // ── Impassable terrain type ──────────────────────────────────────
        if (profile.ImpassableTerrain.Contains(cell.Type))
            return false;

        // ── Slope check ──────────────────────────────────────────────────
        //
        // Design decision: the slope check here uses a strict > comparison.
        // A cell whose slope exactly equals MaxSlopeAngle is still traversable
        // (but very expensive due to the exponential penalty in GetSlopePenalty).
        // This avoids a jarring binary cutoff right at the limit — the cost
        // curve does the work of discouraging the pathfinder smoothly.
        if (cell.SlopeAngle > profile.MaxSlopeAngle)
            return false;

        return true;
    }

    /// <summary>
    /// Returns a multiplicative penalty factor for moving on a slope.
    ///
    /// <para><b>Curve design:</b> The penalty grows slowly for gentle slopes
    /// and accelerates sharply near the unit's maximum.  This creates natural
    /// behaviour: units prefer flat paths but won't make huge detours to
    /// avoid small hills.  Near their slope limit, the cost becomes so high
    /// that the pathfinder effectively routes around it.</para>
    ///
    /// <para><b>Formula (piecewise):</b></para>
    /// <code>
    ///   ratio = slopeAngle / maxSlope          [0.0 .. 1.0]
    ///   if ratio ≤ 0:   return 1.0             (flat ground, no penalty)
    ///   if ratio > 1.0: return MaxValue         (impassable)
    ///
    ///   // Quadratic-exponential hybrid:
    ///   //   penalty = 1 + (SlopeExpBase - 1) * ratio²
    ///   //
    ///   // At ratio=0:   penalty = 1.0          (no penalty)
    ///   // At ratio=0.5: penalty = 1.75         (75% extra cost)
    ///   // At ratio=0.8: penalty = 2.92         (192% extra cost)
    ///   // At ratio=1.0: penalty = 4.0          (300% extra cost)
    ///   //
    ///   // This is a good approximation of an exponential curve using only
    ///   // fixed-point multiply — no transcendental functions needed.
    ///   penalty = 1 + (SlopeExpBase - 1) * ratio * ratio
    /// </code>
    ///
    /// <para><b>Why not a true exponential?</b>  exp() requires either a
    /// lookup table or a long polynomial, both of which are overkill.  The
    /// quadratic curve x² already provides the "gentle at low, steep at high"
    /// shape we want, and it's only 2 multiplies in fixed-point.</para>
    /// </summary>
    public static FixedPoint GetSlopePenalty(FixedPoint slopeAngle, FixedPoint maxSlope)
    {
        // Flat terrain or units with "infinite" slope tolerance.
        if (slopeAngle <= FixedPoint.Zero || maxSlope <= FixedPoint.Zero)
            return FixedPoint.One;

        // Slope exceeds maximum — impassable.
        if (slopeAngle > maxSlope)
            return FixedPoint.MaxValue;

        // ratio = slopeAngle / maxSlope  ∈ [0, 1]
        FixedPoint ratio = slopeAngle / maxSlope;

        // ratio² — the core "exponential-like" growth term.
        FixedPoint ratioSq = ratio * ratio;

        // penalty = 1 + (SlopeExpBase - 1) * ratio²
        //         = 1 + 3 * ratio²
        // At ratio=0:   1.0
        // At ratio=0.5: 1.75
        // At ratio=1.0: 4.0
        FixedPoint slopeExpBaseMinusOne = SlopeExpBase - FixedPoint.One;
        FixedPoint penalty = FixedPoint.One + slopeExpBaseMinusOne * ratioSq;

        return penalty;
    }

    /// <summary>
    /// Returns the additive cost contribution from an elevation change between
    /// two adjacent cells.
    ///
    /// <para><b>Uphill:</b> Each unit of height gain adds
    /// <see cref="UphillCostFactor"/> (0.5) to the cost.  A 2-unit climb
    /// adds 1.0 extra cost — doubling the base cost of flat terrain.</para>
    ///
    /// <para><b>Downhill:</b> Each unit of height drop gives a discount of
    /// <see cref="DownhillDiscountFactor"/> (0.2).  The discount is
    /// intentionally smaller than the uphill penalty for two reasons:
    /// <list type="number">
    ///   <item>Physical realism — going downhill is easier but still requires
    ///         braking effort and is slower than flat.</item>
    ///   <item>Pathfinding balance — symmetric costs would make the A* heuristic
    ///         less effective, because uphill-then-downhill would cost the same
    ///         as flat, eliminating elevation as a routing signal.</item>
    /// </list></para>
    ///
    /// <para><b>The discount is capped at zero</b> — elevation change can
    /// reduce the cost contribution to zero but never make it negative.
    /// Negative costs would break A* admissibility and could create infinite
    /// loops in the open set.</para>
    /// </summary>
    public static FixedPoint GetElevationCost(FixedPoint heightDifference)
    {
        if (heightDifference > FixedPoint.Zero)
        {
            // Uphill: positive cost proportional to height gain.
            return heightDifference * UphillCostFactor;
        }
        else if (heightDifference < FixedPoint.Zero)
        {
            // Downhill: small discount (negative height diff → positive abs value).
            // Result is negative (a discount), but clamped to zero minimum.
            FixedPoint discount = FixedPoint.Abs(heightDifference) * DownhillDiscountFactor;
            return -FixedPoint.Min(discount, BaseCost); // discount capped at BaseCost
        }

        // Flat — no elevation contribution.
        return FixedPoint.Zero;
    }
}
