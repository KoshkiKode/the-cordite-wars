using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Units;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════════
// COLLISION RESOLVER — Post-movement physical collision detection & response
// ═══════════════════════════════════════════════════════════════════════════════
//
// EXECUTION ORDER (per tick):
//   1. All units move via MovementSimulator.AdvanceTick()
//   2. DetectCollisions() — find overlapping unit pairs via SpatialHash
//   3. ResolveCollisions() — push apart, apply crush damage
//   4. ResolveStaticCollisions() — push units out of buildings/terrain
//   5. Positions written back to unit state
//
// DESIGN PHILOSOPHY (inspired by C&C Generals):
//
//   Collision in an RTS is NOT physics simulation — it's a game-feel feature.
//   We don't want realistic elastic collisions (bouncing tanks look absurd).
//   We want:
//
//   1. Units that never overlap permanently.  Temporary overlap is OK during
//      a single tick, but by the end of resolution they should be separated.
//
//   2. CRUSH mechanics: heavy vehicles (Overlord tank) can drive through
//      infantry, instantly killing them.  This rewards micro (dodging tanks)
//      and punishes careless unit positioning.
//
//   3. MASS-BASED push: a heavy unit barely moves when bumped by a light one.
//      A tank formation holds its ground when infantry run into it.
//      Push magnitude: overlap * (otherMass / (selfMass + otherMass)).
//
//   4. Static collision: units pushed into buildings or terrain edges get
//      shoved back out.  The unit always loses — buildings never move.
//
//   5. AIR-GROUND SEPARATION: air units and ground units exist in different
//      collision domains.  A helicopter does not collide with a tank beneath
//      it.  Air units collide only with other air units at similar altitude.
//
// DETERMINISM:
//   - All math uses FixedPoint / FixedVector2 (no float, no double).
//   - Collision pairs are processed in ascending (UnitIdA, UnitIdB) order.
//   - No LINQ, no Dictionary iteration, no HashSet.
//   - Units are always processed by ascending ID to ensure identical results
//     on all clients in lockstep multiplayer.
//
// ═══════════════════════════════════════════════════════════════════════════════

// ── Data Structures ──────────────────────────────────────────────────────────

/// <summary>
/// Information about a unit needed for collision detection and resolution.
/// Extracted from the full unit state to keep the collision system decoupled
/// from the unit storage format.
/// </summary>
public struct UnitCollisionInfo
{
    /// <summary>Unique unit identifier.  Used for pair deduplication and result mapping.</summary>
    public int UnitId;

    /// <summary>Owner player ID.  Used for friendly-fire rules (no crush on own units).</summary>
    public int PlayerId;

    /// <summary>Current world position after movement this tick.</summary>
    public FixedVector2 Position;

    /// <summary>
    /// Collision radius in world units.  Derived from footprint:
    ///   radius = max(footprintWidth, footprintHeight) * 0.5
    /// A 1×1 infantry unit has radius 0.5; a 2×2 tank has radius 1.0.
    /// </summary>
    public FixedPoint Radius;

    /// <summary>
    /// Unit mass.  Affects push proportions: heavier units move less when
    /// colliding with lighter units.  Typical values: infantry=1, jeep=3,
    /// tank=10, Overlord=20.
    /// </summary>
    public FixedPoint Mass;

    /// <summary>
    /// Crush strength.  0 = cannot crush.  Values > 0 allow the unit to crush
    /// lighter units on collision.  From MovementProfile.CrushStrength.
    /// </summary>
    public FixedPoint CrushStrength;

    /// <summary>
    /// Armor classification for crush eligibility checks.  Infantry (Unarmored)
    /// and Light vehicles can be crushed by heavy units.
    /// </summary>
    public ArmorType ArmorClass;

    /// <summary>
    /// True if this unit is airborne (helicopter, jet).  Air units do not
    /// collide with ground units — they occupy a different collision layer.
    /// </summary>
    public bool IsAirUnit;

    /// <summary>
    /// Flight altitude for air units.  Air-vs-air collisions only occur
    /// between units at similar altitude (difference &lt; altitude threshold).
    /// Ground units have Height = 0 (irrelevant since they don't collide
    /// with air units).
    /// </summary>
    public FixedPoint Height;
}

/// <summary>
/// Describes a single collision between two units.  Built during detection,
/// consumed during resolution.
/// </summary>
public struct CollisionPair
{
    /// <summary>Lower unit ID in the pair (always UnitIdA &lt; UnitIdB).</summary>
    public int UnitIdA;

    /// <summary>Higher unit ID in the pair.</summary>
    public int UnitIdB;

    /// <summary>Position of unit A at detection time.</summary>
    public FixedVector2 PositionA;

    /// <summary>Position of unit B at detection time.</summary>
    public FixedVector2 PositionB;

    /// <summary>Collision radius of unit A.</summary>
    public FixedPoint RadiusA;

    /// <summary>Collision radius of unit B.</summary>
    public FixedPoint RadiusB;

    /// <summary>Mass of unit A.</summary>
    public FixedPoint MassA;

    /// <summary>Mass of unit B.</summary>
    public FixedPoint MassB;

    /// <summary>
    /// How far the two circles overlap (positive = collision).
    /// overlap = radiusA + radiusB - distance.
    /// A value of 0.5 means they're overlapping by 0.5 world units.
    /// </summary>
    public FixedPoint Overlap;

    /// <summary>
    /// Unit direction vector from A to B, normalized.
    /// Used to compute push directions: A is pushed in -Normal, B in +Normal.
    /// </summary>
    public FixedVector2 Normal;

    /// <summary>
    /// True if one unit can crush the other.  The crusher is determined by
    /// CrushStrength &gt; 0 and mass ratio &gt; 2:1.
    /// </summary>
    public bool IsCrush;
}

/// <summary>
/// Result of a crush event — one unit was destroyed by being driven over.
/// </summary>
public struct CrushResult
{
    /// <summary>The unit that was crushed (killed).</summary>
    public int CrushedUnitId;

    /// <summary>The unit that did the crushing.</summary>
    public int CrusherUnitId;
}

/// <summary>
/// Per-unit output of collision resolution.  Contains the adjusted position
/// and any damage taken from crush or impact.
/// </summary>
public struct UnitCollisionResult
{
    /// <summary>Unit ID this result applies to.</summary>
    public int UnitId;

    /// <summary>Adjusted position after collision pushback.</summary>
    public FixedVector2 NewPosition;

    /// <summary>Damage taken from crush impact.  0 if no crush occurred.</summary>
    public FixedPoint DamageTaken;

    /// <summary>
    /// True if this unit was crushed (instant kill).  The unit should be
    /// destroyed by the caller after processing results.
    /// </summary>
    public bool WasCrushed;
}

// ── Collision Resolver ───────────────────────────────────────────────────────

/// <summary>
/// Stateless collision detection and resolution system.  Operates on
/// pre-extracted <see cref="UnitCollisionInfo"/> data and outputs position
/// adjustments and damage via result lists.  No internal state — all context
/// flows through parameters for determinism and testability.
/// </summary>
public class CollisionResolver
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// Altitude difference threshold for air-vs-air collision.
    /// Two air units must be within this altitude range to collide.
    /// Prevents high-altitude jets from colliding with low-altitude helicopters.
    /// ~2.0 world units in Q16.16.
    /// </summary>
    private static readonly FixedPoint AirAltitudeThreshold = FixedPoint.FromInt(2);

    /// <summary>
    /// Mass ratio threshold for crush eligibility.  The crusher must have
    /// at least this ratio of mass vs. the crushee.  2× mass = can crush.
    /// An Overlord tank (mass 20) easily crushes infantry (mass 1), but a
    /// jeep (mass 3) cannot crush infantry since 3 < 1*2... Actually 3 > 2,
    /// so it depends on CrushStrength. The mass ratio is a NECESSARY condition,
    /// not sufficient — CrushStrength must also be > 0.
    /// </summary>
    private static readonly FixedPoint CrushMassRatio = FixedPoint.FromInt(2);

    /// <summary>
    /// Damage dealt when a unit is crushed.  Set extremely high to ensure
    /// instant kill for infantry.  Light vehicles take this as heavy damage
    /// (may survive if they have enough HP, but usually don't).
    /// 9999 HP in Q16.16.
    /// </summary>
    private static readonly FixedPoint CrushDamage = FixedPoint.FromInt(9999);

    /// <summary>
    /// Minimum push distance to prevent units from getting permanently stuck
    /// at zero overlap (floating-point epsilon equivalent for FixedPoint).
    /// ~0.01 world units in Q16.16.
    /// </summary>
    private static readonly FixedPoint MinPushEpsilon = FixedPoint.FromRaw(655);

    // ── Detection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects all colliding unit pairs using the spatial hash for broad-phase
    /// and circle-circle overlap for narrow-phase.
    ///
    /// CRITICAL FOR DETERMINISM: units are processed in ascending ID order.
    /// For each unit, we only check units with HIGHER IDs to avoid generating
    /// duplicate pairs (A-B is the same collision as B-A).
    ///
    /// Air units and ground units are in separate collision domains.  Air units
    /// only collide with other air units at similar altitude.
    /// </summary>
    /// <param name="units">
    /// All active units, indexed by their position in this list.  The list
    /// MUST be sorted by UnitId in ascending order for determinism.
    /// </param>
    /// <param name="spatial">
    /// Spatial hash containing all units (rebuilt this tick).
    /// Used for broad-phase: we query the spatial hash for each unit to find
    /// nearby candidates, then do narrow-phase circle-circle checks.
    /// </param>
    /// <param name="outPairs">
    /// Output list of collision pairs.  Cleared before detection begins.
    /// Each pair has UnitIdA &lt; UnitIdB (guaranteed by the algorithm).
    /// </param>
    public void DetectCollisions(
        List<UnitCollisionInfo> units,
        SpatialHash spatial,
        List<CollisionPair> outPairs)
    {
        outPairs.Clear();

        // Reusable list for spatial query results — avoids allocation per unit
        List<int> nearbyCandidates = new List<int>(64);

        // Build a lookup from unitId → index in the units list.
        // We need this because the spatial hash returns unit IDs, not list indices.
        // Since we require units to be sorted by ID in ascending order, and unit IDs
        // are typically sequential, we can use a simple array for O(1) lookup.
        //
        // Find the max unit ID to size the array.
        int maxUnitId = 0;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].UnitId > maxUnitId)
                maxUnitId = units[i].UnitId;
        }

        // Map unitId → index in units list.  -1 means "no unit with this ID."
        int[] unitIdToIndex = new int[maxUnitId + 1];
        for (int i = 0; i < unitIdToIndex.Length; i++)
        {
            unitIdToIndex[i] = -1;
        }
        for (int i = 0; i < units.Count; i++)
        {
            unitIdToIndex[units[i].UnitId] = i;
        }

        // Process each unit in ascending ID order (guaranteed by sorted input)
        for (int i = 0; i < units.Count; i++)
        {
            UnitCollisionInfo unitA = units[i];

            // Query the spatial hash for units near A.
            // We use a generous query radius: A's radius + the maximum possible
            // other unit radius.  Since we don't know the max radius cheaply,
            // we query with A's radius * 2 + a generous buffer.  In practice,
            // the spatial hash returns all units in overlapping cells, which
            // already includes nearby units regardless of their radius.
            nearbyCandidates.Clear();
            spatial.QueryRadius(unitA.Position, unitA.Radius + unitA.Radius + FixedPoint.FromInt(2), nearbyCandidates);

            for (int j = 0; j < nearbyCandidates.Count; j++)
            {
                int candidateId = nearbyCandidates[j];

                // DEDUPLICATION: only process pairs where A's ID < B's ID.
                // This guarantees each collision is detected exactly once.
                if (candidateId <= unitA.UnitId)
                    continue;

                // Look up candidate in the units list
                if (candidateId >= unitIdToIndex.Length)
                    continue;
                int candidateIndex = unitIdToIndex[candidateId];
                if (candidateIndex < 0)
                    continue;

                UnitCollisionInfo unitB = units[candidateIndex];

                // ── Domain check: air vs. ground ──
                // Air units never collide with ground units.
                if (unitA.IsAirUnit != unitB.IsAirUnit)
                    continue;

                // Air-vs-air: only collide if at similar altitude
                if (unitA.IsAirUnit && unitB.IsAirUnit)
                {
                    FixedPoint altitudeDiff = FixedPoint.Abs(unitA.Height - unitB.Height);
                    if (altitudeDiff > AirAltitudeThreshold)
                        continue;
                }

                // ── Narrow-phase: circle-circle overlap ──
                FixedVector2 delta = unitB.Position - unitA.Position;
                FixedPoint distSq = delta.LengthSquared;
                FixedPoint combinedRadius = unitA.Radius + unitB.Radius;
                FixedPoint combinedRadiusSq = combinedRadius * combinedRadius;

                if (distSq >= combinedRadiusSq)
                    continue; // No overlap

                // Compute actual distance and overlap magnitude
                FixedPoint dist = FixedPoint.Sqrt(distSq);
                FixedPoint overlap = combinedRadius - dist;
                if (overlap <= FixedPoint.Zero)
                    continue;

                // Compute collision normal (direction from A to B)
                FixedVector2 normal;
                if (dist > FixedPoint.Zero)
                {
                    normal = new FixedVector2(delta.X / dist, delta.Y / dist);
                }
                else
                {
                    // Units are at the exact same position — degenerate case.
                    // Use a deterministic fallback: push in +X direction.
                    // This is arbitrary but consistent across all clients.
                    normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
                }

                // ── Crush check ──
                bool isCrush = false;
                isCrush = CheckCrush(unitA, unitB) || CheckCrush(unitB, unitA);

                outPairs.Add(new CollisionPair
                {
                    UnitIdA = unitA.UnitId,
                    UnitIdB = unitB.UnitId,
                    PositionA = unitA.Position,
                    PositionB = unitB.Position,
                    RadiusA = unitA.Radius,
                    RadiusB = unitB.Radius,
                    MassA = unitA.Mass,
                    MassB = unitB.Mass,
                    Overlap = overlap,
                    Normal = normal,
                    IsCrush = isCrush
                });
            }
        }
    }

    /// <summary>
    /// Checks if unit 'crusher' can crush unit 'target'.
    /// Requirements:
    ///   1. Crusher has CrushStrength &gt; 0
    ///   2. Target's ArmorClass is Unarmored (infantry) or Light (light vehicles)
    ///   3. Crusher's Mass &gt; Target's Mass × CrushMassRatio (2×)
    /// </summary>
    private static bool CheckCrush(UnitCollisionInfo crusher, UnitCollisionInfo target)
    {
        if (crusher.CrushStrength <= FixedPoint.Zero)
            return false;

        if (target.ArmorClass != ArmorType.Unarmored && target.ArmorClass != ArmorType.Light)
            return false;

        if (crusher.Mass <= target.Mass * CrushMassRatio)
            return false;

        return true;
    }

    // ── Resolution ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves all detected collisions by pushing units apart and applying
    /// crush damage.
    ///
    /// Resolution rules:
    ///   1. CRUSH: if the pair is a crush, the lighter unit takes massive
    ///      damage (CrushDamage = 9999).  Infantry die instantly.  Light
    ///      vehicles take heavy damage but may survive with enough HP.
    ///      The crusher is NOT pushed — it drives right through.
    ///
    ///   2. PUSH: if no crush, both units are pushed apart proportional to
    ///      inverse mass.  The heavier unit moves less.
    ///        pushA = overlap × (massB / (massA + massB)) in the -Normal direction
    ///        pushB = overlap × (massA / (massA + massB)) in the +Normal direction
    ///
    ///   3. STATIC BUILDINGS: if one "unit" is actually a building (mass = MaxValue
    ///      or effectively infinite), it never moves — the other unit absorbs
    ///      the entire push.  The mass formula handles this naturally since
    ///      massBuilding / (massUnit + massBuilding) ≈ 1.0.
    ///
    /// Results are accumulated into outResults.  Multiple collisions involving
    /// the same unit produce multiple result entries — the caller should sum
    /// position deltas and take the max damage.
    /// </summary>
    /// <param name="pairs">Collision pairs from DetectCollisions.</param>
    /// <param name="outResults">
    /// Output list of per-unit position adjustments and damage.  Cleared
    /// before resolution begins.
    /// </param>
    public void ResolveCollisions(List<CollisionPair> pairs, List<UnitCollisionResult> outResults)
    {
        outResults.Clear();

        for (int i = 0; i < pairs.Count; i++)
        {
            CollisionPair pair = pairs[i];

            if (pair.IsCrush)
            {
                // ── CRUSH RESOLUTION ──
                // Determine who crushes whom.  The unit with CrushStrength > 0
                // and higher mass is the crusher.  We re-derive this from the
                // pair data rather than storing it, to keep CollisionPair small.
                //
                // If A crushes B: A doesn't move, B takes CrushDamage.
                // If B crushes A: B doesn't move, A takes CrushDamage.
                // (Both crushing each other is theoretically possible but
                //  practically impossible since both would need CrushStrength > 0
                //  AND the other to be Unarmored/Light — highly unlikely.)

                // Check A crushes B
                bool aCrushesB = pair.MassA > pair.MassB * CrushMassRatio;
                // Check B crushes A
                bool bCrushesA = pair.MassB > pair.MassA * CrushMassRatio;

                if (aCrushesB)
                {
                    // A crushes B — A stays, B takes damage
                    outResults.Add(new UnitCollisionResult
                    {
                        UnitId = pair.UnitIdA,
                        NewPosition = pair.PositionA,
                        DamageTaken = FixedPoint.Zero,
                        WasCrushed = false
                    });
                    outResults.Add(new UnitCollisionResult
                    {
                        UnitId = pair.UnitIdB,
                        NewPosition = pair.PositionB,
                        DamageTaken = CrushDamage,
                        WasCrushed = true
                    });
                }
                else if (bCrushesA)
                {
                    // B crushes A — B stays, A takes damage
                    outResults.Add(new UnitCollisionResult
                    {
                        UnitId = pair.UnitIdA,
                        NewPosition = pair.PositionA,
                        DamageTaken = CrushDamage,
                        WasCrushed = true
                    });
                    outResults.Add(new UnitCollisionResult
                    {
                        UnitId = pair.UnitIdB,
                        NewPosition = pair.PositionB,
                        DamageTaken = FixedPoint.Zero,
                        WasCrushed = false
                    });
                }
                else
                {
                    // Edge case: crush flagged but neither meets mass ratio
                    // (shouldn't happen with correct detection, but handle gracefully).
                    // Fall through to normal push resolution.
                    ResolvePush(pair, outResults);
                }
            }
            else
            {
                // ── NORMAL PUSH RESOLUTION ──
                ResolvePush(pair, outResults);
            }
        }
    }

    /// <summary>
    /// Pushes two units apart proportional to inverse mass.
    /// The heavier unit moves less; the lighter unit moves more.
    ///
    /// Formula:
    ///   totalMass = massA + massB
    ///   pushA = overlap × (massB / totalMass) — moves A away from B
    ///   pushB = overlap × (massA / totalMass) — moves B away from A
    ///
    /// Direction:
    ///   A is pushed in -Normal direction (away from B)
    ///   B is pushed in +Normal direction (away from A)
    ///
    /// Edge case: if totalMass is zero (shouldn't happen — all units have
    /// mass ≥ 1), push equally by half overlap each.
    /// </summary>
    private static void ResolvePush(CollisionPair pair, List<UnitCollisionResult> outResults)
    {
        FixedPoint totalMass = pair.MassA + pair.MassB;

        FixedPoint pushFractionA;
        FixedPoint pushFractionB;

        if (totalMass > FixedPoint.Zero)
        {
            // Standard mass-weighted push:
            //   A's push = overlap * (B's mass / total) — lighter A moves more
            //   B's push = overlap * (A's mass / total) — lighter B moves more
            pushFractionA = pair.MassB / totalMass;
            pushFractionB = pair.MassA / totalMass;
        }
        else
        {
            // Degenerate: both masses zero.  Push equally.
            pushFractionA = FixedPoint.Half;
            pushFractionB = FixedPoint.Half;
        }

        // Ensure minimum push to prevent permanent overlap at tiny overlaps
        FixedPoint pushMagnitudeA = FixedPoint.Max(pair.Overlap * pushFractionA, MinPushEpsilon);
        FixedPoint pushMagnitudeB = FixedPoint.Max(pair.Overlap * pushFractionB, MinPushEpsilon);

        // A is pushed in -Normal direction (away from B)
        FixedVector2 pushA = new FixedVector2(
            -(pair.Normal.X * pushMagnitudeA),
            -(pair.Normal.Y * pushMagnitudeA)
        );

        // B is pushed in +Normal direction (away from A)
        FixedVector2 pushB = new FixedVector2(
            pair.Normal.X * pushMagnitudeB,
            pair.Normal.Y * pushMagnitudeB
        );

        outResults.Add(new UnitCollisionResult
        {
            UnitId = pair.UnitIdA,
            NewPosition = pair.PositionA + pushA,
            DamageTaken = FixedPoint.Zero,
            WasCrushed = false
        });

        outResults.Add(new UnitCollisionResult
        {
            UnitId = pair.UnitIdB,
            NewPosition = pair.PositionB + pushB,
            DamageTaken = FixedPoint.Zero,
            WasCrushed = false
        });
    }

    // ── Static Collision Resolution ──────────────────────────────────────────

    /// <summary>
    /// Checks each unit against buildings and terrain boundaries, pushing
    /// units out of any blocked cells they've been shoved into by the
    /// unit-vs-unit collision resolution above.
    ///
    /// This is the "last line of defense" — after all dynamic pushes are
    /// applied, some units may end up inside buildings or outside the map.
    /// This method slides them to the nearest valid position.
    ///
    /// Algorithm:
    ///   1. For each unit, compute the grid cell its center occupies.
    ///   2. Check if that cell (and adjacent cells within the unit's radius)
    ///      are blocked by terrain or buildings.
    ///   3. If blocked, search outward in a spiral pattern for the nearest
    ///      unblocked cell and move the unit there.
    ///
    /// Why not just check the center cell?
    ///   A unit with radius 1.0 at position (5.9, 3.0) has its circle
    ///   overlapping cells (5,3) and (6,3).  If cell (6,3) is a building,
    ///   the unit must be pushed left even though its center is in (5,3).
    /// </summary>
    /// <param name="units">
    /// Mutable list of unit collision info.  Positions are modified in-place
    /// for units that need to be pushed out of blocked cells.
    /// MUST be sorted by UnitId in ascending order.
    /// </param>
    /// <param name="terrain">Terrain grid for static blocking (cliffs, terrain type).</param>
    /// <param name="occupancy">Occupancy grid for dynamic blocking (buildings).</param>
    public void ResolveStaticCollisions(
        List<UnitCollisionInfo> units,
        TerrainGrid terrain,
        OccupancyGrid occupancy)
    {
        for (int i = 0; i < units.Count; i++)
        {
            UnitCollisionInfo unit = units[i];

            // Air units don't collide with terrain or buildings
            if (unit.IsAirUnit)
                continue;

            // Check if the unit's center cell is blocked
            int centerCellX = unit.Position.X.ToInt();
            int centerCellY = unit.Position.Y.ToInt();

            bool needsPush = false;

            // Check center cell and cells within the unit's radius
            int radiusCells = unit.Radius.ToInt() + 1; // +1 for safety margin
            int checkMinX = centerCellX - radiusCells;
            int checkMinY = centerCellY - radiusCells;
            int checkMaxX = centerCellX + radiusCells;
            int checkMaxY = centerCellY + radiusCells;

            // Clamp to grid bounds for terrain checks
            if (checkMinX < 0) checkMinX = 0;
            if (checkMinY < 0) checkMinY = 0;
            if (checkMaxX >= terrain.Width) checkMaxX = terrain.Width - 1;
            if (checkMaxY >= terrain.Height) checkMaxY = terrain.Height - 1;

            for (int cy = checkMinY; cy <= checkMaxY && !needsPush; cy++)
            {
                for (int cx = checkMinX; cx <= checkMaxX && !needsPush; cx++)
                {
                    // Check if this cell overlaps the unit's collision circle
                    // Cell center in world coords: (cx + 0.5, cy + 0.5)
                    FixedPoint cellCenterX = FixedPoint.FromInt(cx) + FixedPoint.Half;
                    FixedPoint cellCenterY = FixedPoint.FromInt(cy) + FixedPoint.Half;

                    FixedPoint dx = unit.Position.X - cellCenterX;
                    FixedPoint dy = unit.Position.Y - cellCenterY;
                    FixedPoint distSq = dx * dx + dy * dy;

                    // The cell's "radius" is ~0.707 (half diagonal of a 1×1 cell).
                    // For simplicity, we use radius + 1.0 as the overlap threshold.
                    FixedPoint overlapThresholdSq = (unit.Radius + FixedPoint.One) * (unit.Radius + FixedPoint.One);

                    if (distSq > overlapThresholdSq)
                        continue;

                    // Check terrain blocking
                    TerrainCell terrainCell = terrain.GetCellSafe(cx, cy);
                    if (terrainCell.IsBlocked)
                    {
                        needsPush = true;
                        break;
                    }

                    // Check occupancy blocking (buildings only — units are handled
                    // by dynamic collision above)
                    OccupancyCell occCell = occupancy.GetCell(cx, cy);
                    if (occCell.Type == OccupancyType.Building)
                    {
                        needsPush = true;
                        break;
                    }
                }
            }

            if (!needsPush)
                continue;

            // ── Find nearest unblocked position ──
            // Search outward from the unit's current cell in a spiral pattern.
            // The first unblocked cell found is the closest valid position.
            // We search up to 10 cells out — if nothing is found, the unit
            // is in a degenerate situation (surrounded by buildings) and we
            // leave it in place (the game should prevent this via placement rules).

            FixedVector2 bestPosition = unit.Position;
            bool foundFree = false;

            // Spiral search: check distances 1, 2, 3... from center cell
            for (int dist = 1; dist <= 10 && !foundFree; dist++)
            {
                // Check all cells at Manhattan distance 'dist' from center.
                // Process in deterministic order: top row, bottom row, left col, right col.
                for (int d = -dist; d <= dist && !foundFree; d++)
                {
                    // Top row: (centerX + d, centerY - dist)
                    if (TryCellForPushout(centerCellX + d, centerCellY - dist, unit.PlayerId, terrain, occupancy, out bestPosition))
                    {
                        foundFree = true;
                        break;
                    }
                    // Bottom row: (centerX + d, centerY + dist)
                    if (TryCellForPushout(centerCellX + d, centerCellY + dist, unit.PlayerId, terrain, occupancy, out bestPosition))
                    {
                        foundFree = true;
                        break;
                    }
                }
                if (foundFree) break;

                // Left and right columns (excluding corners already checked)
                for (int d = -dist + 1; d <= dist - 1 && !foundFree; d++)
                {
                    // Left col: (centerX - dist, centerY + d)
                    if (TryCellForPushout(centerCellX - dist, centerCellY + d, unit.PlayerId, terrain, occupancy, out bestPosition))
                    {
                        foundFree = true;
                        break;
                    }
                    // Right col: (centerX + dist, centerY + d)
                    if (TryCellForPushout(centerCellX + dist, centerCellY + d, unit.PlayerId, terrain, occupancy, out bestPosition))
                    {
                        foundFree = true;
                        break;
                    }
                }
            }

            if (foundFree)
            {
                // Update the unit's position in the list.
                // Since UnitCollisionInfo is a struct, we must write it back.
                unit.Position = bestPosition;
                units[i] = unit;
            }
            // If no free cell found within 10 cells, leave the unit in place.
            // This is a degenerate case that shouldn't occur in normal gameplay.
        }
    }

    // ── AssetRegistry Integration ───────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="UnitCollisionInfo"/> using data from the
    /// <see cref="AssetRegistry"/> instead of hard-coded values.
    /// The collision radius, mass, and crush strength are all read from the
    /// asset manifest via the unit's data ID.
    /// </summary>
    /// <param name="unitId">Runtime unit instance ID.</param>
    /// <param name="playerId">Owning player ID.</param>
    /// <param name="dataId">Unit type ID for registry lookup (e.g., "valkyr_windrunner").</param>
    /// <param name="position">Current world position.</param>
    /// <param name="isAirUnit">Whether this unit is airborne.</param>
    /// <param name="height">Flight altitude (0 for ground units).</param>
    /// <param name="armorClass">Armor classification for crush eligibility.</param>
    /// <param name="registry">The asset registry to look up physics data from.</param>
    /// <returns>A fully populated <see cref="UnitCollisionInfo"/>.</returns>
    public static UnitCollisionInfo BuildCollisionInfo(
        int unitId,
        int playerId,
        string dataId,
        FixedVector2 position,
        bool isAirUnit,
        FixedPoint height,
        ArmorType armorClass,
        AssetRegistry registry)
    {
        return new UnitCollisionInfo
        {
            UnitId = unitId,
            PlayerId = playerId,
            Position = position,
            Radius = registry.GetCollisionRadius(dataId),
            Mass = registry.GetMass(dataId),
            CrushStrength = registry.GetCrushStrength(dataId),
            ArmorClass = armorClass,
            IsAirUnit = isAirUnit,
            Height = height
        };
    }

    /// <summary>
    /// Checks if a grid cell is a valid pushout destination: in bounds,
    /// terrain is not blocked, and occupancy is passable for the given player.
    /// If valid, outputs the cell's center position as the target position.
    /// </summary>
    /// <param name="cellX">Grid X coordinate to check.</param>
    /// <param name="cellY">Grid Y coordinate to check.</param>
    /// <param name="playerId">The player ID of the unit being pushed out.</param>
    /// <param name="terrain">Terrain grid for static blocking checks.</param>
    /// <param name="occupancy">Occupancy grid for dynamic blocking checks.</param>
    /// <param name="outPosition">
    /// If the cell is valid, set to the cell's center in world coordinates.
    /// </param>
    /// <returns>True if the cell is a valid pushout destination.</returns>
    private static bool TryCellForPushout(
        int cellX,
        int cellY,
        int playerId,
        TerrainGrid terrain,
        OccupancyGrid occupancy,
        out FixedVector2 outPosition)
    {
        outPosition = FixedVector2.Zero;

        // Bounds check
        if (!terrain.IsInBounds(cellX, cellY))
            return false;

        // Terrain must not be blocked
        TerrainCell terrainCell = terrain.GetCellSafe(cellX, cellY);
        if (terrainCell.IsBlocked)
            return false;

        // Void terrain is always impassable
        if (terrainCell.Type == TerrainType.Void)
            return false;

        // Occupancy must be passable for this player
        if (!occupancy.IsCellPassable(cellX, cellY, playerId))
            return false;

        // Cell is valid — return its center position
        outPosition = new FixedVector2(
            FixedPoint.FromInt(cellX) + FixedPoint.Half,
            FixedPoint.FromInt(cellY) + FixedPoint.Half
        );
        return true;
    }
}
