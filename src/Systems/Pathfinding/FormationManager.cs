using System;
using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════════
// FORMATION MANAGER — Deterministic formation computation for group movement
// ═══════════════════════════════════════════════════════════════════════════════
//
// When a player selects multiple units and issues a move command, they should
// move in formation — not all pile onto the same cell. This system computes
// per-unit target positions arranged in a formation shape around the
// destination point.
//
// FORMATION TYPES:
//   Line   — side by side, perpendicular to move direction. Good for charges.
//   Column — single file along move direction. Good for narrow paths.
//   Wedge  — V-shape pointing forward. Default for mixed groups.
//   Box    — rectangular grid. Good for defense.
//   Spread — loose scatter around destination. Default for air units.
//
// SORTING PRINCIPLE:
//   Within a formation, heavier/slower units go in front (they set the pace).
//   Lighter/faster units go in back or on flanks. This prevents fast units
//   from running ahead and dying. Inspired by real-world military doctrine
//   and C&C Generals formation behavior.
//
// DETERMINISM:
//   All math uses FixedPoint / FixedVector2. No float, no LINQ, no
//   non-deterministic iteration. Unit lists are processed in stable-sorted
//   order by mass (descending), with UnitId as tiebreaker.
//
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Formation shape type. Determines how units are arranged relative to
/// the formation center and movement direction.
/// </summary>
public enum FormationType
{
    /// <summary>No formation — each unit moves independently to the destination.</summary>
    None,

    /// <summary>
    /// Side-by-side line perpendicular to the movement direction.
    /// Good for charges and broad-front engagements.
    /// </summary>
    Line,

    /// <summary>
    /// Single file along the movement direction.
    /// Good for navigating narrow paths and choke points.
    /// </summary>
    Column,

    /// <summary>
    /// V-shape pointing forward with the heaviest unit at the tip.
    /// Default for mixed groups. Provides a strong front with flank coverage.
    /// </summary>
    Wedge,

    /// <summary>
    /// Rectangular grid, roughly square aspect ratio.
    /// Good for defense and compact movement through open terrain.
    /// </summary>
    Box,

    /// <summary>
    /// Loose scatter distribution around the destination.
    /// Default for air units. Minimizes vulnerability to AoE attacks.
    /// </summary>
    Spread
}

/// <summary>
/// Input data for a single unit participating in a formation.
/// </summary>
public struct FormationUnit
{
    /// <summary>Unique identifier for this unit.</summary>
    public int UnitId;

    /// <summary>Collision radius — determines spacing between slots.</summary>
    public FixedPoint Radius;

    /// <summary>Maximum movement speed. Used for pace-matching (slowest sets pace).</summary>
    public FixedPoint Speed;

    /// <summary>Unit mass. Heavier units sort to the front of formations.</summary>
    public FixedPoint Mass;

    /// <summary>True if this is an air unit. Air units default to Spread formation.</summary>
    public bool IsAir;
}

/// <summary>
/// Output data for a single unit's assigned slot within a formation.
/// </summary>
public struct FormationSlot
{
    /// <summary>The unit assigned to this slot.</summary>
    public int UnitId;

    /// <summary>Offset from the formation center (in local formation space).</summary>
    public FixedVector2 Offset;

    /// <summary>Absolute world position for this unit's formation slot.</summary>
    public FixedVector2 WorldTarget;
}

/// <summary>
/// Computes deterministic formation layouts for groups of units.
/// All math uses FixedPoint for lockstep determinism.
/// </summary>
public class FormationManager
{
    // ── Spacing Constants ────────────────────────────────────────────────

    /// <summary>
    /// Multiplier applied to unit radius for computing inter-slot spacing.
    /// radius * 2.5 gives comfortable breathing room between units.
    /// </summary>
    private static readonly FixedPoint SpacingMultiplier = FixedPoint.FromRaw(163840); // 2.5

    /// <summary>
    /// Multiplier for Spread formation spacing: radius * 3.0 minimum
    /// distance between neighbors (Poisson-disk-like distribution).
    /// </summary>
    private static readonly FixedPoint SpreadSpacingMultiplier = FixedPoint.FromInt(3);

    /// <summary>
    /// Minimum slot spacing to prevent units from being placed too close
    /// together even if their radii are very small. ~1.0 world unit.
    /// </summary>
    private static readonly FixedPoint MinSlotSpacing = FixedPoint.One;

    /// <summary>Half constant for centering calculations.</summary>
    private static readonly FixedPoint FP_Half = FixedPoint.Half;

    /// <summary>Threshold for "mostly infantry" auto-select: 70% infantry → Line.</summary>
    private static readonly FixedPoint InfantryThreshold = FixedPoint.FromRaw(45875); // 0.7

    // ── Scratch Lists (reused to avoid allocation) ───────────────────────

    private readonly List<FormationUnit> _sortedUnits = new List<FormationUnit>(64);
    private readonly List<FormationSlot> _slots = new List<FormationSlot>(64);

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes formation slots for a group of units moving to a destination.
    /// </summary>
    /// <param name="units">The units in the formation group.</param>
    /// <param name="destination">World-space destination point (formation center).</param>
    /// <param name="currentCenter">
    /// Current center of mass of the group. Used to compute the formation
    /// facing direction (from current center toward destination).
    /// </param>
    /// <param name="type">The formation shape to use.</param>
    /// <returns>A list of formation slots, one per unit, with world-space targets.</returns>
    public List<FormationSlot> ComputeFormation(
        List<FormationUnit> units,
        FixedVector2 destination,
        FixedVector2 currentCenter,
        FormationType type)
    {
        _slots.Clear();

        if (units == null || units.Count == 0)
            return _slots;

        // If only one unit, just send it to the destination directly
        if (units.Count == 1)
        {
            _slots.Add(new FormationSlot
            {
                UnitId = units[0].UnitId,
                Offset = FixedVector2.Zero,
                WorldTarget = destination
            });
            return _slots;
        }

        // No formation → everyone goes to the destination
        if (type == FormationType.None)
        {
            for (int i = 0; i < units.Count; i++)
            {
                _slots.Add(new FormationSlot
                {
                    UnitId = units[i].UnitId,
                    Offset = FixedVector2.Zero,
                    WorldTarget = destination
                });
            }
            return _slots;
        }

        // ── Sort units: heavier/slower in front, lighter/faster in back ──
        // Stable sort by mass descending, with UnitId as tiebreaker for determinism.
        _sortedUnits.Clear();
        for (int i = 0; i < units.Count; i++)
            _sortedUnits.Add(units[i]);

        StableSortByMassDescending(_sortedUnits);

        // ── Compute formation facing direction ──
        // Direction from current group center toward the destination.
        // If center == destination (e.g., units told to hold position),
        // default facing to +X (east).
        FixedVector2 facingDir;
        FixedVector2 delta = destination - currentCenter;
        if (delta.LengthSquared > FixedPoint.Zero)
        {
            facingDir = delta.Normalized();
        }
        else
        {
            facingDir = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        }

        // Perpendicular direction (left of facing) for lateral placement
        FixedVector2 perpDir = new FixedVector2(-facingDir.Y, facingDir.X);

        // ── Dispatch to shape-specific computation ──
        switch (type)
        {
            case FormationType.Line:
                ComputeLineFormation(_sortedUnits, destination, facingDir, perpDir);
                break;
            case FormationType.Column:
                ComputeColumnFormation(_sortedUnits, destination, facingDir, perpDir);
                break;
            case FormationType.Wedge:
                ComputeWedgeFormation(_sortedUnits, destination, facingDir, perpDir);
                break;
            case FormationType.Box:
                ComputeBoxFormation(_sortedUnits, destination, facingDir, perpDir);
                break;
            case FormationType.Spread:
                ComputeSpreadFormation(_sortedUnits, destination, facingDir, perpDir);
                break;
            default:
                // Fallback: everyone to destination
                for (int i = 0; i < _sortedUnits.Count; i++)
                {
                    _slots.Add(new FormationSlot
                    {
                        UnitId = _sortedUnits[i].UnitId,
                        Offset = FixedVector2.Zero,
                        WorldTarget = destination
                    });
                }
                break;
        }

        return _slots;
    }

    /// <summary>
    /// Heuristic auto-selection of formation type based on unit composition.
    /// </summary>
    /// <param name="units">The units in the formation group.</param>
    /// <returns>The recommended formation type.</returns>
    public FormationType AutoSelectFormation(List<FormationUnit> units)
    {
        if (units == null || units.Count == 0)
            return FormationType.None;

        if (units.Count == 1)
            return FormationType.None;

        int airCount = 0;
        int infantryCount = 0;
        int vehicleCount = 0;
        int totalCount = units.Count;

        for (int i = 0; i < totalCount; i++)
        {
            if (units[i].IsAir)
            {
                airCount++;
            }
            else if (units[i].Mass <= FixedPoint.FromInt(2))
            {
                // Light units (mass <= 2) are considered infantry-like
                infantryCount++;
            }
            else
            {
                vehicleCount++;
            }
        }

        // All air → Spread
        if (airCount == totalCount)
            return FormationType.Spread;

        // Large groups (> 8 units) → Box for compactness
        if (totalCount > 8)
            return FormationType.Box;

        // Mixed with vehicles → Wedge (good all-purpose with heavy front)
        if (vehicleCount > 0 && infantryCount > 0)
            return FormationType.Wedge;

        // Mostly infantry (>= 70%) → Line for broad front
        // Compute without floating point: infantryCount * 10 >= totalCount * 7
        if (infantryCount * 10 >= totalCount * 7)
            return FormationType.Line;

        // Default → Wedge
        return FormationType.Wedge;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FORMATION SHAPE METHODS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LINE FORMATION: Units arranged side-by-side perpendicular to the
    /// movement direction. Center unit at formation center, others spaced
    /// outward alternating left and right.
    /// Spacing = max(unit.Radius * 2.5, MinSlotSpacing).
    /// </summary>
    private void ComputeLineFormation(
        List<FormationUnit> units,
        FixedVector2 center,
        FixedVector2 facingDir,
        FixedVector2 perpDir)
    {
        int count = units.Count;

        // Compute average spacing based on unit sizes
        FixedPoint totalSpacing = FixedPoint.Zero;
        for (int i = 0; i < count; i++)
        {
            FixedPoint unitSpacing = units[i].Radius * SpacingMultiplier;
            unitSpacing = FixedPoint.Max(unitSpacing, MinSlotSpacing);
            totalSpacing = totalSpacing + unitSpacing;
        }
        FixedPoint avgSpacing = totalSpacing / FixedPoint.FromInt(count);

        // Place units along the perpendicular axis, centered on the formation center.
        // Unit 0 (heaviest) goes in the center. Others alternate left/right.
        for (int i = 0; i < count; i++)
        {
            // Slot index: 0 → center, 1 → right 1, 2 → left 1, 3 → right 2, ...
            int slotIndex;
            bool goRight;
            if (i == 0)
            {
                slotIndex = 0;
                goRight = true; // doesn't matter for center
            }
            else
            {
                // Alternating: odd indices go right, even indices go left
                int halfIndex = (i + 1) / 2;
                goRight = (i % 2 == 1);
                slotIndex = halfIndex;
            }

            FixedPoint lateralOffset;
            if (i == 0)
            {
                lateralOffset = FixedPoint.Zero;
            }
            else
            {
                lateralOffset = avgSpacing * FixedPoint.FromInt(slotIndex);
                if (!goRight)
                    lateralOffset = -lateralOffset;
            }

            FixedVector2 offset = perpDir * lateralOffset;
            FixedVector2 worldPos = center + offset;

            _slots.Add(new FormationSlot
            {
                UnitId = units[i].UnitId,
                Offset = offset,
                WorldTarget = worldPos
            });
        }
    }

    /// <summary>
    /// COLUMN FORMATION: Units arranged single file along the movement
    /// direction. First unit (heaviest) at the front, rest following behind.
    /// Spacing = max(unit.Radius * 2.5, MinSlotSpacing).
    /// </summary>
    private void ComputeColumnFormation(
        List<FormationUnit> units,
        FixedVector2 center,
        FixedVector2 facingDir,
        FixedVector2 perpDir)
    {
        int count = units.Count;

        // The lead unit (index 0, heaviest) is placed at the destination.
        // Subsequent units are placed behind (opposite to facing direction).
        FixedPoint cumulativeOffset = FixedPoint.Zero;

        for (int i = 0; i < count; i++)
        {
            FixedPoint unitSpacing = units[i].Radius * SpacingMultiplier;
            unitSpacing = FixedPoint.Max(unitSpacing, MinSlotSpacing);

            FixedVector2 offset;
            if (i == 0)
            {
                offset = FixedVector2.Zero;
            }
            else
            {
                // Previous unit's spacing + this unit's spacing / 2
                FixedPoint prevSpacing = units[i - 1].Radius * SpacingMultiplier;
                prevSpacing = FixedPoint.Max(prevSpacing, MinSlotSpacing);
                cumulativeOffset = cumulativeOffset + (prevSpacing + unitSpacing) * FP_Half;
                // Behind the facing direction (negative facing)
                offset = facingDir * (-cumulativeOffset);
            }

            FixedVector2 worldPos = center + offset;

            _slots.Add(new FormationSlot
            {
                UnitId = units[i].UnitId,
                Offset = offset,
                WorldTarget = worldPos
            });
        }
    }

    /// <summary>
    /// WEDGE FORMATION: V-shape pointing forward. The heaviest unit is at
    /// the tip (front). Subsequent units fan out behind in alternating
    /// left/right positions, creating a V-shape.
    /// </summary>
    private void ComputeWedgeFormation(
        List<FormationUnit> units,
        FixedVector2 center,
        FixedVector2 facingDir,
        FixedVector2 perpDir)
    {
        int count = units.Count;

        // Average spacing for consistent geometry
        FixedPoint totalSpacing = FixedPoint.Zero;
        for (int i = 0; i < count; i++)
        {
            FixedPoint unitSpacing = units[i].Radius * SpacingMultiplier;
            unitSpacing = FixedPoint.Max(unitSpacing, MinSlotSpacing);
            totalSpacing = totalSpacing + unitSpacing;
        }
        FixedPoint avgSpacing = totalSpacing / FixedPoint.FromInt(count);

        // Unit 0 at the tip (destination point).
        // Unit 1 → right-back, Unit 2 → left-back, Unit 3 → right-further-back, etc.
        // Each row goes further back and further out laterally.
        for (int i = 0; i < count; i++)
        {
            if (i == 0)
            {
                // Tip of the wedge
                _slots.Add(new FormationSlot
                {
                    UnitId = units[i].UnitId,
                    Offset = FixedVector2.Zero,
                    WorldTarget = center
                });
                continue;
            }

            int rowIndex = (i + 1) / 2; // 1→1, 2→1, 3→2, 4→2, 5→3, ...
            bool goRight = (i % 2 == 1);

            // Each row goes further back
            FixedPoint backOffset = avgSpacing * FixedPoint.FromInt(rowIndex);
            // Each row fans out laterally
            FixedPoint lateralOffset = avgSpacing * FixedPoint.FromInt(rowIndex);

            if (!goRight)
                lateralOffset = -lateralOffset;

            // Offset = back (opposite facing) + lateral (perpendicular)
            FixedVector2 offset = facingDir * (-backOffset) + perpDir * lateralOffset;
            FixedVector2 worldPos = center + offset;

            _slots.Add(new FormationSlot
            {
                UnitId = units[i].UnitId,
                Offset = offset,
                WorldTarget = worldPos
            });
        }
    }

    /// <summary>
    /// BOX FORMATION: Rectangular grid, roughly square aspect ratio.
    /// Number of columns = ceil(sqrt(count)). Heavy units fill front rows
    /// first (left to right, top to bottom in facing-space).
    /// </summary>
    private void ComputeBoxFormation(
        List<FormationUnit> units,
        FixedVector2 center,
        FixedVector2 facingDir,
        FixedVector2 perpDir)
    {
        int count = units.Count;

        // Compute columns = ceil(sqrt(count)) using integer math
        int cols = 1;
        while (cols * cols < count)
            cols++;

        int rows = (count + cols - 1) / cols; // ceil(count / cols)

        // Average spacing
        FixedPoint totalSpacing = FixedPoint.Zero;
        for (int i = 0; i < count; i++)
        {
            FixedPoint unitSpacing = units[i].Radius * SpacingMultiplier;
            unitSpacing = FixedPoint.Max(unitSpacing, MinSlotSpacing);
            totalSpacing = totalSpacing + unitSpacing;
        }
        FixedPoint avgSpacing = totalSpacing / FixedPoint.FromInt(count);

        // Center the grid: half-width and half-depth offsets
        FixedPoint halfWidth = avgSpacing * FixedPoint.FromInt(cols - 1) * FP_Half;
        FixedPoint halfDepth = avgSpacing * FixedPoint.FromInt(rows - 1) * FP_Half;

        int unitIndex = 0;
        for (int row = 0; row < rows && unitIndex < count; row++)
        {
            for (int col = 0; col < cols && unitIndex < count; col++)
            {
                // Lateral position: column offset from center
                FixedPoint lateralOffset = avgSpacing * FixedPoint.FromInt(col) - halfWidth;
                // Depth position: row 0 is front, increasing rows go back
                FixedPoint depthOffset = avgSpacing * FixedPoint.FromInt(row) - halfDepth;

                // Front rows are in the facing direction, back rows behind
                // Negate depthOffset so row 0 is furthest forward
                FixedVector2 offset = facingDir * (-depthOffset) + perpDir * lateralOffset;
                FixedVector2 worldPos = center + offset;

                _slots.Add(new FormationSlot
                {
                    UnitId = units[unitIndex].UnitId,
                    Offset = offset,
                    WorldTarget = worldPos
                });
                unitIndex++;
            }
        }
    }

    /// <summary>
    /// SPREAD FORMATION: Poisson-disk-like distribution around the center.
    /// Each unit is placed at a minimum distance of radius * 3 from all
    /// previously placed neighbors. Uses a deterministic spiral placement
    /// algorithm (no randomness needed — deterministic positions for
    /// deterministic unit ordering).
    /// </summary>
    private void ComputeSpreadFormation(
        List<FormationUnit> units,
        FixedVector2 center,
        FixedVector2 facingDir,
        FixedVector2 perpDir)
    {
        int count = units.Count;

        // First unit at center
        _slots.Add(new FormationSlot
        {
            UnitId = units[0].UnitId,
            Offset = FixedVector2.Zero,
            WorldTarget = center
        });

        if (count == 1) return;

        // Average minimum spacing: radius * 3.0
        FixedPoint totalMinDist = FixedPoint.Zero;
        for (int i = 0; i < count; i++)
        {
            FixedPoint minDist = units[i].Radius * SpreadSpacingMultiplier;
            minDist = FixedPoint.Max(minDist, MinSlotSpacing);
            totalMinDist = totalMinDist + minDist;
        }
        FixedPoint avgMinDist = totalMinDist / FixedPoint.FromInt(count);

        // Place remaining units in a deterministic expanding spiral.
        // Use a Fermat spiral: angle = i * golden_angle, radius = avgMinDist * sqrt(i).
        // Golden angle ≈ 2.399963 radians.
        // We approximate sqrt(i) using integer sqrt for determinism.
        FixedPoint goldenAngle = FixedPoint.FromRaw(157287); // ~2.4 in Q16.16

        for (int i = 1; i < count; i++)
        {
            // Angle for this position on the spiral
            FixedPoint angle = goldenAngle * FixedPoint.FromInt(i);

            // Radius: avgMinDist * sqrt(i)
            FixedPoint sqrtI = FixedPoint.Sqrt(FixedPoint.FromInt(i));
            FixedPoint spiralRadius = avgMinDist * sqrtI;

            // Convert polar to cartesian offset using facing/perp as axes
            FixedPoint cosA = MovementSimulator.FixedCos(angle);
            FixedPoint sinA = MovementSimulator.FixedSin(angle);

            FixedVector2 offset = facingDir * (cosA * spiralRadius) + perpDir * (sinA * spiralRadius);
            FixedVector2 worldPos = center + offset;

            _slots.Add(new FormationSlot
            {
                UnitId = units[i].UnitId,
                Offset = offset,
                WorldTarget = worldPos
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SORTING UTILITIES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stable insertion sort by mass descending, with UnitId ascending as
    /// tiebreaker. Insertion sort is used because formation groups are
    /// typically small (< 50 units) and stability is required for determinism.
    /// No LINQ, no non-deterministic iteration.
    /// </summary>
    private static void StableSortByMassDescending(List<FormationUnit> units)
    {
        // Insertion sort — O(n²) but stable and deterministic.
        // Formation groups are small enough that this is fine.
        for (int i = 1; i < units.Count; i++)
        {
            FormationUnit key = units[i];
            int j = i - 1;

            // Move elements that are "less than" key (lighter mass or same mass
            // but higher UnitId) one position ahead.
            while (j >= 0 && ShouldSwapMassDesc(units[j], key))
            {
                units[j + 1] = units[j];
                j--;
            }
            units[j + 1] = key;
        }
    }

    /// <summary>
    /// Returns true if 'a' should come AFTER 'b' in mass-descending order.
    /// i.e., 'a' is lighter than 'b', or same mass but higher UnitId.
    /// </summary>
    private static bool ShouldSwapMassDesc(FormationUnit a, FormationUnit b)
    {
        if (a.Mass < b.Mass) return true;
        if (a.Mass > b.Mass) return false;
        // Same mass → lower UnitId first (ascending tiebreaker)
        return a.UnitId > b.UnitId;
    }
}
