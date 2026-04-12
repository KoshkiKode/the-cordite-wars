using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;

namespace CorditeWars.Systems.Pathfinding;

// ═══════════════════════════════════════════════════════════════════════════════
// UNIT INTERACTION SYSTEM — The master simulation tick pipeline
// ═══════════════════════════════════════════════════════════════════════════════
//
// This is the HEART of the deterministic simulation. Every simulation tick,
// this system processes all units through a fixed sequence of phases. The
// phase order is SACRED — every client in a lockstep multiplayer game must
// execute these phases in exactly this order to produce identical results.
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║                        TICK PIPELINE ORDER                              ║
// ╠═══════════════════════════════════════════════════════════════════════════╣
// ║ Phase 1 — Spatial Indexing       (rebuild spatial hash from positions)  ║
// ║ Phase 2 — Build Occupancy        (rebuild occupancy grid)              ║
// ║ Phase 3 — Pathfinding            (process queued path requests)        ║
// ║ Phase 4 — Steering               (compute per-unit movement direction) ║
// ║ Phase 5 — Movement               (advance physics, store new states)   ║
// ║ Phase 6 — Collision Resolution   (detect + resolve unit/building hits) ║
// ║ Phase 7 — Combat                 (target acquire, fire, apply damage)  ║
// ║ Phase 8 — Cleanup                (remove dead units, update vision)    ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// DETERMINISM RULES:
//   1. ALL math uses FixedPoint / FixedVector2. No float. No double.
//   2. All units processed in ASCENDING UnitId order within each phase.
//   3. No LINQ. No Dictionary iteration. No HashSet iteration.
//   4. No System.Random. Combat uses DeterministicRng only.
//   5. No parallel execution. Single-threaded, sequential phases.
//   6. Movement states are computed then applied simultaneously (Phase 5).
//   7. This file is the single source of truth for tick execution order.
//
// If you change the phase order, add a phase, or change the processing order
// within a phase, ALL clients must be updated simultaneously. A desync here
// means the game is broken for multiplayer.
//
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Combined simulation state for one unit. Contains all per-unit data needed
/// by the tick pipeline. Updated in-place between ticks.
/// </summary>
public struct SimUnit
{
    // ── Identity ─────────────────────────────────────────────────────────

    /// <summary>Unique unit identifier. Units are always processed in ascending ID order.</summary>
    public int UnitId;

    /// <summary>Owning player identifier. Used for friend/foe checks.</summary>
    public int PlayerId;

    // ── Movement ─────────────────────────────────────────────────────────

    /// <summary>Current movement state (position, velocity, facing, etc.).</summary>
    public MovementState Movement;

    /// <summary>Movement characteristics for this unit type (speed, turn rate, mass, etc.).</summary>
    public MovementProfile Profile;

    /// <summary>
    /// Current A* waypoint path as (gridX, gridY) pairs, or null if no active path.
    /// Set by pathfinding when a move command is issued.
    /// </summary>
    public List<(int, int)>? CurrentPath;

    /// <summary>
    /// Active flow field for group movement, or null if using waypoint path.
    /// Shared among units in the same movement group.
    /// </summary>
    public FlowField? ActiveFlowField;

    /// <summary>Index into CurrentPath of the waypoint currently being pursued.</summary>
    public int CurrentWaypointIndex;

    // ── Collision ────────────────────────────────────────────────────────

    /// <summary>Collision/footprint radius in world units.</summary>
    public FixedPoint Radius;

    // ── Health & Armor ───────────────────────────────────────────────────

    /// <summary>Current hit points. When <= 0 the unit is destroyed.</summary>
    public FixedPoint Health;

    /// <summary>Maximum hit points.</summary>
    public FixedPoint MaxHealth;

    /// <summary>Flat damage reduction per hit.</summary>
    public FixedPoint ArmorValue;

    /// <summary>Armor classification for incoming damage modifiers.</summary>
    public ArmorType ArmorClass;

    // ── Weapons & Combat ────────────────────────────────────────────────

    /// <summary>Weapon hardpoints (0, 1, or 2 entries). Null or empty = unarmed.</summary>
    public List<WeaponData> Weapons;

    /// <summary>
    /// Remaining cooldown per weapon in ticks. Index matches Weapons list.
    /// A weapon can fire when its cooldown reaches 0 or below.
    /// </summary>
    public List<FixedPoint> WeaponCooldowns;

    /// <summary>
    /// The unit we are currently targeting, or null if idle.
    /// Preserved across ticks for target retention (anti-jitter).
    /// </summary>
    public int? CurrentTargetId;

    // ── Status Flags ────────────────────────────────────────────────────

    /// <summary>True if the unit is alive. Set to false when destroyed.</summary>
    public bool IsAlive;

    /// <summary>Broad unit classification (Infantry, Tank, Helicopter, etc.).</summary>
    public UnitCategory Category;

    /// <summary>Vision range in grid cells. Used by the fog-of-war system.</summary>
    public FixedPoint SightRange;

    // ── Stealth ──────────────────────────────────────────────────────────

    /// <summary>
    /// Template flag: this unit has inherent stealth capability (loaded from
    /// <c>UnitData.IsStealthed</c>). Does not change at runtime.
    /// </summary>
    public bool IsStealthUnit;

    /// <summary>
    /// Template flag: this unit can detect stealthed enemies within its
    /// normal sight range (loaded from <c>UnitData.IsDetector</c>).
    /// Does not change at runtime.
    /// </summary>
    public bool IsDetector;

    /// <summary>
    /// Ticks remaining during which this unit is temporarily revealed after
    /// having fired a weapon. Counts down to 0 each tick, at which point the
    /// unit re-enters stealth (if <see cref="IsStealthUnit"/> is true and no
    /// detector is nearby).
    /// </summary>
    public int StealthRevealTicks;

    /// <summary>
    /// Resolved each tick by <c>ResolveStealthStates</c>. True when this unit
    /// is effectively hidden from enemies: it has stealth capability, has not
    /// fired recently, and no enemy detector is within detection range.
    /// </summary>
    public bool IsCurrentlyStealthed;
}

/// <summary>
/// Result of one complete simulation tick. Contains all events that occurred
/// during the tick for consumption by the rendering/audio/UI layers.
/// </summary>
public struct TickResult
{
    /// <summary>All unit-vs-unit collision resolutions that occurred this tick.</summary>
    public List<UnitCollisionResult> Collisions;

    /// <summary>All crush events (heavy unit driving over light unit) this tick.</summary>
    public List<CrushResult> Crushes;

    /// <summary>All attack resolutions (shots fired) this tick.</summary>
    public List<AttackResult> Attacks;

    /// <summary>IDs of all units destroyed this tick (from combat or crushing).</summary>
    public List<int> DestroyedUnitIds;

    /// <summary>The simulation tick number this result corresponds to.</summary>
    public ulong Tick;
}

/// <summary>
/// The master tick coordinator. Processes all simulation phases in the exact
/// deterministic order required for lockstep multiplayer.
///
/// USAGE:
///   var system = new UnitInteractionSystem(...);
///   TickResult result = system.ProcessTick(units, terrain, currentTick);
///   // Apply result to rendering, UI, audio
/// </summary>
public class UnitInteractionSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // DEPENDENCIES (injected via constructor)
    // ═══════════════════════════════════════════════════════════════════════

    private readonly SpatialHash _spatialHash;
    private readonly OccupancyGrid _occupancyGrid;
    private readonly CollisionResolver _collisionResolver;
    private readonly PathRequestManager _pathRequestManager;
    private readonly FormationManager _formationManager;
    private readonly CombatResolver _combatResolver;
    private readonly DeterministicRng _combatRng;

    /// <summary>The deterministic combat RNG. Exposed for save/load state restoration.</summary>
    public DeterministicRng CombatRng => _combatRng;

    // ── Configuration ────────────────────────────────────────────────────

    /// <summary>Maximum number of path requests to process per tick.</summary>
    private readonly int _maxPathsPerTick;

    /// <summary>
    /// Number of ticks a stealthed unit remains temporarily revealed after it
    /// fires a weapon. At the default 10-tick/s sim rate this equals 1.5 seconds.
    /// </summary>
    private const int StealthRevealDuration = 15;

    /// <summary>Awareness radius multiplier for steering neighbor queries.</summary>
    private static readonly FixedPoint NeighborQueryRadius = FixedPoint.FromInt(8);

    /// <summary>One tick worth of cooldown to decrement each tick.</summary>
    private static readonly FixedPoint OneTick = FixedPoint.One;

    // ── Scratch buffers (reused each tick to avoid GC pressure) ──────────

    private readonly List<MovementState> _newMovementStates = new List<MovementState>(256);
    private readonly List<int> _newWaypointIndices = new List<int>(256);
    private readonly List<UnitCollisionInfo> _collisionInfos = new List<UnitCollisionInfo>(256);
    private readonly List<NearbyUnit> _neighborBuffer = new List<NearbyUnit>(32);
    private readonly List<int> _spatialResults = new List<int>(32);
    private readonly List<UnitCombatInfo> _combatInfos = new List<UnitCombatInfo>(256);
    private readonly List<int> _destroyedIds = new List<int>(32);
    private readonly List<AttackResult> _attackResults = new List<AttackResult>(64);
    private readonly List<CollisionPair> _collisionPairs = new List<CollisionPair>(64);
    private readonly List<UnitCollisionResult> _collisionResults = new List<UnitCollisionResult>(64);
    private readonly List<CrushResult> _crushResults = new List<CrushResult>(16);

    // ═══════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new UnitInteractionSystem with all required subsystem references.
    /// </summary>
    /// <param name="spatialHash">Spatial partitioning for proximity queries.</param>
    /// <param name="occupancyGrid">Cell-level occupancy tracking.</param>
    /// <param name="collisionResolver">Unit collision detection and resolution.</param>
    /// <param name="pathRequestManager">Asynchronous pathfinding request processor.</param>
    /// <param name="formationManager">Formation layout computation.</param>
    /// <param name="combatResolver">Deterministic combat calculation.</param>
    /// <param name="combatRng">Deterministic RNG for combat accuracy rolls.</param>
    /// <param name="maxPathsPerTick">
    /// Maximum path requests to process per tick. Higher = more responsive
    /// pathfinding but more CPU per tick. Default 8.
    /// </param>
    public UnitInteractionSystem(
        SpatialHash spatialHash,
        OccupancyGrid occupancyGrid,
        CollisionResolver collisionResolver,
        PathRequestManager pathRequestManager,
        FormationManager formationManager,
        CombatResolver combatResolver,
        DeterministicRng combatRng,
        int maxPathsPerTick = 8)
    {
        _spatialHash = spatialHash;
        _occupancyGrid = occupancyGrid;
        _collisionResolver = collisionResolver;
        _pathRequestManager = pathRequestManager;
        _formationManager = formationManager;
        _combatResolver = combatResolver;
        _combatRng = combatRng;
        _maxPathsPerTick = maxPathsPerTick;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MAIN TICK ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Processes one complete simulation tick. This is the deterministic tick
    /// pipeline that EVERY client must execute identically.
    ///
    /// CRITICAL: The phase order below is SACRED. Do not reorder, skip, or
    /// interleave phases. All units within each phase are processed in
    /// ascending UnitId order.
    ///
    /// The unit list MUST be sorted by UnitId ascending before calling this
    /// method. The caller is responsible for maintaining sort order.
    /// </summary>
    /// <param name="units">
    /// All living units in the simulation, sorted by UnitId ascending.
    /// This list is mutated in place (positions updated, health reduced, etc.).
    /// </param>
    /// <param name="terrain">The terrain grid for height/slope/type queries.</param>
    /// <param name="currentTick">The current simulation tick number.</param>
    /// <returns>
    /// A TickResult containing all events that occurred during this tick.
    /// The rendering/audio/UI layer consumes these events to produce feedback.
    /// </returns>
    public TickResult ProcessTick(List<SimUnit> units, TerrainGrid terrain, ulong currentTick)
    {
        // ── Clear scratch buffers ──
        _newMovementStates.Clear();
        _newWaypointIndices.Clear();
        _collisionInfos.Clear();
        _combatInfos.Clear();
        _destroyedIds.Clear();
        _attackResults.Clear();
        _collisionPairs.Clear();
        _collisionResults.Clear();
        _crushResults.Clear();

        // ══════════════════════════════════════════════════════════════════
        // PHASE 1 — SPATIAL INDEXING
        // ══════════════════════════════════════════════════════════════════
        // Rebuild the spatial hash from scratch each tick. This is a clean
        // rebuild rather than incremental update — simpler, no stale data,
        // and fast enough for typical unit counts (< 2000 units).
        //
        // Processing order: ascending UnitId (list is pre-sorted).
        // ══════════════════════════════════════════════════════════════════

        _spatialHash.Clear();

        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;

            _spatialHash.Insert(unit.UnitId, unit.Movement.Position, unit.Radius);
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 2 — BUILD OCCUPANCY
        // ══════════════════════════════════════════════════════════════════
        // Rebuild the occupancy grid. Each living unit marks the cells it
        // occupies. Buildings mark their full footprint.
        //
        // The occupancy grid is used by:
        //   - Pathfinding (avoid occupied cells)
        //   - Steering (avoid obstacles)
        //   - Combat (target acquisition for buildings)
        //
        // Processing order: ascending UnitId (list is pre-sorted).
        // ══════════════════════════════════════════════════════════════════

        _occupancyGrid.Clear();

        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;

            // Convert world position to grid cell
            int cellX = unit.Movement.Position.X.ToInt();
            int cellY = unit.Movement.Position.Y.ToInt();

            // Determine occupancy type based on unit category
            OccupancyType occType;
            if (unit.Category == UnitCategory.Defense)
            {
                occType = OccupancyType.Building;
                // Buildings occupy their full footprint
                int fw = unit.Profile.FootprintWidth;
                int fh = unit.Profile.FootprintHeight;
                for (int dy = 0; dy < fh; dy++)
                {
                    for (int dx = 0; dx < fw; dx++)
                    {
                        _occupancyGrid.OccupyCell(
                            cellX + dx, cellY + dy,
                            occType, unit.UnitId, unit.PlayerId);
                    }
                }
            }
            else
            {
                occType = OccupancyType.Unit;
                _occupancyGrid.OccupyCell(cellX, cellY, occType, unit.UnitId, unit.PlayerId);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 3 — PATHFINDING
        // ══════════════════════════════════════════════════════════════════
        // Process queued path requests up to the per-tick budget.
        // This is the only place pathfinding runs — path requests are
        // queued by move commands and processed here.
        //
        // The path request manager is responsible for assigning completed
        // paths back to units (via SimUnit.CurrentPath / ActiveFlowField).
        // ══════════════════════════════════════════════════════════════════

        _pathRequestManager.ProcessRequests(terrain, _maxPathsPerTick);

        // ══════════════════════════════════════════════════════════════════
        // PHASE 4 — STEERING
        // ══════════════════════════════════════════════════════════════════
        // For each unit (in ID order), compute the desired movement
        // direction and speed using the SteeringManager.
        //
        // This phase:
        //   1. Queries the spatial hash for nearby neighbors
        //   2. Builds a UnitSteeringContext with path, neighbors, state
        //   3. Calls SteeringManager.ComputeSteering → SteeringResult
        //   4. Builds a MovementInput from the steering result
        //
        // The MovementInput is consumed by Phase 5 (Movement).
        //
        // Processing order: ascending UnitId.
        // ══════════════════════════════════════════════════════════════════

        // Pre-allocate movement input array (parallel to units list)
        // We store inputs separately because Phase 5 needs them.
        MovementInput[] movementInputs = new MovementInput[units.Count];

        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive)
            {
                movementInputs[i] = new MovementInput
                {
                    DesiredDirection = FixedVector2.Zero,
                    DesiredSpeed = FixedPoint.Zero,
                    Brake = true
                };
                continue;
            }

            // ── Query neighbors for separation/avoidance ──
            _neighborBuffer.Clear();
            _spatialResults.Clear();
            _spatialHash.QueryRadius(unit.Movement.Position, NeighborQueryRadius, _spatialResults);

            for (int n = 0; n < _spatialResults.Count; n++)
            {
                int neighborId = _spatialResults[n];
                if (neighborId == unit.UnitId) continue; // Skip self

                // Find the neighbor's data — linear scan is acceptable for
                // small neighbor counts (typically < 20 per query).
                for (int j = 0; j < units.Count; j++)
                {
                    if (units[j].UnitId == neighborId && units[j].IsAlive)
                    {
                        _neighborBuffer.Add(new NearbyUnit
                        {
                            Position = units[j].Movement.Position,
                            Velocity = units[j].Movement.Velocity,
                            Radius = units[j].Radius
                        });
                        break;
                    }
                }
            }

            // ── Build steering context ──
            UnitSteeringContext ctx = new UnitSteeringContext
            {
                Position = unit.Movement.Position,
                Velocity = unit.Movement.Velocity,
                Radius = unit.Radius,
                PathWaypoints = unit.CurrentPath,
                ActiveFlowField = unit.ActiveFlowField,
                CurrentWaypointIndex = unit.CurrentWaypointIndex,
                Neighbors = new List<NearbyUnit>(_neighborBuffer.Count)
            };

            // Copy neighbor buffer into context (SteeringManager reads it)
            for (int n = 0; n < _neighborBuffer.Count; n++)
            {
                ctx.Neighbors.Add(_neighborBuffer[n]);
            }

            // ── Compute steering ──
            SteeringResult steering = SteeringManager.ComputeSteering(ctx);

            // ── Update waypoint index from steering result ──
            SimUnit updatedUnit = unit;
            updatedUnit.CurrentWaypointIndex = steering.UpdatedWaypointIndex;
            units[i] = updatedUnit;

            // ── Build movement input ──
            movementInputs[i] = new MovementInput
            {
                DesiredDirection = steering.DesiredDirection,
                DesiredSpeed = steering.DesiredSpeed,
                Brake = steering.HasArrived
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 5 — MOVEMENT
        // ══════════════════════════════════════════════════════════════════
        // For each unit (in ID order), advance the movement simulation by
        // one tick using the MovementSimulator.
        //
        // CRITICAL: New positions are computed but NOT applied immediately.
        // All units move "simultaneously" — we compute all new states first,
        // then apply them all at once. This prevents order-dependent
        // position updates where unit A moves into B's old position before
        // B has moved.
        //
        // Processing order: ascending UnitId.
        // ══════════════════════════════════════════════════════════════════

        _newMovementStates.Clear();

        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive)
            {
                _newMovementStates.Add(unit.Movement); // No change for dead units
                continue;
            }

            // Advance physics by one tick
            MovementState newState = MovementSimulator.AdvanceTick(
                unit.Movement,
                movementInputs[i],
                unit.Profile,
                terrain);

            _newMovementStates.Add(newState);
        }

        // ── Apply all new positions simultaneously ──
        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            unit.Movement = _newMovementStates[i];
            units[i] = unit;
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 6 — COLLISION RESOLUTION
        // ══════════════════════════════════════════════════════════════════
        // Now that all units have moved, detect and resolve collisions.
        //
        // Steps:
        //   6a. Build UnitCollisionInfo list from updated positions
        //   6b. DetectCollisions → find overlapping unit pairs
        //   6c. ResolveCollisions → push units apart, compute crush damage
        //   6d. Apply position adjustments from collision resolution
        //   6e. ResolveStaticCollisions → push units out of buildings/terrain
        //
        // Processing order: ascending UnitId for building the info list.
        // Collision resolution order is handled internally by the resolver
        // (must also be deterministic).
        // ══════════════════════════════════════════════════════════════════

        _collisionInfos.Clear();

        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;

            bool isAirUnit = (unit.Category == UnitCategory.Helicopter ||
                              unit.Category == UnitCategory.Jet);

            _collisionInfos.Add(new UnitCollisionInfo
            {
                UnitId = unit.UnitId,
                PlayerId = unit.PlayerId,
                Position = unit.Movement.Position,
                Radius = unit.Radius,
                Mass = unit.Profile.Mass,
                CrushStrength = unit.Profile.CrushStrength,
                ArmorClass = unit.ArmorClass,
                IsAirUnit = isAirUnit,
                Height = unit.Movement.Height
            });
        }

        // 6b. Detect collisions between units
        _collisionResolver.DetectCollisions(_collisionInfos, _spatialHash, _collisionPairs);

        // 6c. Resolve collisions — compute push-out vectors and crush damage
        _collisionResolver.ResolveCollisions(_collisionPairs, _collisionResults);

        // 6d. Apply position adjustments from collision resolution
        for (int r = 0; r < _collisionResults.Count; r++)
        {
            UnitCollisionResult cr = _collisionResults[r];
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].UnitId == cr.UnitId)
                {
                    SimUnit unit = units[i];
                    unit.Movement.Position = cr.NewPosition;

                    // Apply collision damage (from crushing)
                    if (cr.DamageTaken > FixedPoint.Zero)
                    {
                        unit.Health = unit.Health - cr.DamageTaken;
                        if (unit.Health <= FixedPoint.Zero)
                        {
                            unit.Health = FixedPoint.Zero;
                            unit.IsAlive = false;
                            _destroyedIds.Add(unit.UnitId);
                        }
                    }
                    units[i] = unit;
                    break;
                }
            }
        }

        // 6e. Resolve static collisions (push out of buildings/terrain)
        _collisionResolver.ResolveStaticCollisions(_collisionInfos, terrain, _occupancyGrid);

        // Apply static collision position adjustments
        for (int c = 0; c < _collisionInfos.Count; c++)
        {
            UnitCollisionInfo info = _collisionInfos[c];
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].UnitId == info.UnitId && units[i].IsAlive)
                {
                    SimUnit unit = units[i];
                    unit.Movement.Position = info.Position;
                    units[i] = unit;
                    break;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 7 — COMBAT
        // ══════════════════════════════════════════════════════════════════
        // For each unit with weapons (in ID order):
        //   7a. Acquire target if no current target
        //   7b. Decrement weapon cooldowns
        //   7c. If cooldown <= 0 and target valid: resolve attack
        //   7d. Apply damage to targets
        //   7e. Check for kills → add to destroyed list
        //
        // Uses CombatResolver from CorditeWars.Game.Units namespace.
        //
        // Processing order: ascending UnitId.
        // ══════════════════════════════════════════════════════════════════

        // Phase 7 pre-step — resolve stealth states for all units.
        // Must run before building _combatInfos so that stealthed units are
        // correctly excluded from target acquisition this tick.
        ResolveStealthStates(units, terrain);

        // Build combat info list for target acquisition
        _combatInfos.Clear();
        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;

            bool isAirUnit = (unit.Category == UnitCategory.Helicopter ||
                              unit.Category == UnitCategory.Jet);
            bool isBuilding = (unit.Category == UnitCategory.Defense);

            _combatInfos.Add(new UnitCombatInfo
            {
                UnitId = unit.UnitId,
                PlayerId = unit.PlayerId,
                Position = unit.Movement.Position,
                Health = unit.Health,
                MaxHealth = unit.MaxHealth,
                ArmorValue = unit.ArmorValue,
                ArmorClass = unit.ArmorClass,
                IsAir = isAirUnit,
                IsBuilding = isBuilding,
                IsStealthed = unit.IsCurrentlyStealthed,
                Radius = unit.Radius
            });
        }

        // Rebuild spatial hash after collision resolution moved units
        _spatialHash.Clear();
        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;
            _spatialHash.Insert(unit.UnitId, unit.Movement.Position, unit.Radius);
        }

        // Process combat for each armed, living unit
        for (int i = 0; i < units.Count; i++)
        {
            SimUnit unit = units[i];
            if (!unit.IsAlive) continue;
            if (unit.Weapons == null || unit.Weapons.Count == 0) continue;

            // ── 7a. Target Acquisition ──
            if (!unit.CurrentTargetId.HasValue || !IsTargetStillValid(unit.CurrentTargetId.Value, units))
            {
                AttackerInfo atkInfo = BuildAttackerInfo(unit);
                CombatTarget? newTarget = _combatResolver.AcquireTarget(
                    atkInfo, unit.Weapons, _spatialHash, _combatInfos, _occupancyGrid);

                unit.CurrentTargetId = newTarget.HasValue ? newTarget.Value.TargetId : (int?)null;
            }

            // ── 7b. Decrement weapon cooldowns ──
            if (unit.WeaponCooldowns != null)
            {
                for (int w = 0; w < unit.WeaponCooldowns.Count; w++)
                {
                    if (unit.WeaponCooldowns[w] > FixedPoint.Zero)
                    {
                        unit.WeaponCooldowns[w] = unit.WeaponCooldowns[w] - OneTick;
                    }
                }
            }

            // ── 7c. Fire weapons if cooldown ready and target valid ──
            if (unit.CurrentTargetId.HasValue)
            {
                // Build CombatTarget for the current target
                CombatTarget? targetInfo = BuildCombatTarget(unit, unit.CurrentTargetId.Value, units);

                if (targetInfo.HasValue)
                {
                    CombatTarget ct = targetInfo.Value;

                    for (int w = 0; w < unit.Weapons.Count; w++)
                    {
                        // Check cooldown
                        if (unit.WeaponCooldowns != null && w < unit.WeaponCooldowns.Count &&
                            unit.WeaponCooldowns[w] > FixedPoint.Zero)
                        {
                            continue; // Weapon still on cooldown
                        }

                        // Check if this weapon can hit the target
                        if (!_combatResolver.CanAttack(unit.Weapons[w], ct))
                            continue;

                        // ── FIRE! ──
                        AttackerInfo atkInfo = BuildAttackerInfo(unit);
                        AttackResult result = _combatResolver.ResolveAttack(
                            atkInfo, ct, unit.Weapons[w], w, _combatRng,
                            _spatialHash, _combatInfos);

                        _attackResults.Add(result);

                        // Stealth: firing temporarily breaks stealth so enemies can
                        // briefly see and return fire against this unit.
                        if (unit.IsStealthUnit && unit.IsCurrentlyStealthed)
                        {
                            unit.IsCurrentlyStealthed = false;
                            unit.StealthRevealTicks = StealthRevealDuration;
                        }

                        // Reset weapon cooldown
                        // Cooldown = SimTickRate / RateOfFire (ticks between shots)
                        if (unit.WeaponCooldowns != null && w < unit.WeaponCooldowns.Count)
                        {
                            FixedPoint tickRate = FixedPoint.FromInt(GameManager.SimTickRate);
                            if (unit.Weapons[w].RateOfFire > FixedPoint.Zero)
                            {
                                unit.WeaponCooldowns[w] = tickRate / unit.Weapons[w].RateOfFire;
                            }
                            else
                            {
                                unit.WeaponCooldowns[w] = tickRate; // Default 1 second cooldown
                            }
                        }

                        // ── 7d. Apply damage to primary target ──
                        if (result.DidHit)
                        {
                            ApplyDamage(units, result.TargetId, result.DamageDealt, _destroyedIds);
                        }

                        // Apply splash damage
                        if (result.SplashTargets != null)
                        {
                            for (int s = 0; s < result.SplashTargets.Count; s++)
                            {
                                var splash = result.SplashTargets[s];
                                ApplyDamage(units, splash.unitId, splash.damage, _destroyedIds);
                            }
                        }
                    }
                }
                else
                {
                    // Target no longer valid — clear it so we acquire a new one next tick
                    unit.CurrentTargetId = null;
                }
            }

            units[i] = unit;
        }

        // ══════════════════════════════════════════════════════════════════
        // PHASE 8 — CLEANUP
        // ══════════════════════════════════════════════════════════════════
        // Remove destroyed units from the active list, update fog of war,
        // and compile the final TickResult.
        //
        // Steps:
        //   8a. Mark destroyed units as not alive (some may have been
        //       marked during combat; we do a final pass to be sure)
        //   8b. Remove destroyed units from the list
        //   8c. Update fog of war / vision (calls into VisionSystem)
        //   8d. Build and return TickResult
        // ══════════════════════════════════════════════════════════════════

        // 8a. Final pass: ensure all destroyed units are marked dead
        for (int d = 0; d < _destroyedIds.Count; d++)
        {
            int deadId = _destroyedIds[d];
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].UnitId == deadId)
                {
                    SimUnit unit = units[i];
                    unit.IsAlive = false;
                    unit.Health = FixedPoint.Zero;
                    units[i] = unit;
                    break;
                }
            }
        }

        // 8b. Remove destroyed units from the list (iterate backwards to
        // avoid index shifting issues). This modifies the list in place.
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (!units[i].IsAlive)
            {
                units.RemoveAt(i);
            }
        }

        // 8c. Update fog of war / vision
        // Vision updates are handled by GameSession after ProcessTick returns,
        // using VisionSystem.UpdateVision() on each player's FogGrid.
        // This keeps FogGrid[] ownership in GameSession while maintaining
        // deterministic tick ordering (vision runs after cleanup every tick).

        // 8d. Build and return TickResult
        // Deduplicate destroyed IDs (a unit could appear multiple times if
        // hit by both crush and combat in the same tick)
        List<int> uniqueDestroyed = new List<int>(_destroyedIds.Count);
        for (int i = 0; i < _destroyedIds.Count; i++)
        {
            bool alreadyAdded = false;
            for (int j = 0; j < uniqueDestroyed.Count; j++)
            {
                if (uniqueDestroyed[j] == _destroyedIds[i])
                {
                    alreadyAdded = true;
                    break;
                }
            }
            if (!alreadyAdded)
            {
                uniqueDestroyed.Add(_destroyedIds[i]);
            }
        }

        TickResult tickResult = new TickResult
        {
            Collisions = new List<UnitCollisionResult>(_collisionResults.Count),
            Crushes = new List<CrushResult>(_crushResults.Count),
            Attacks = new List<AttackResult>(_attackResults.Count),
            DestroyedUnitIds = uniqueDestroyed,
            Tick = currentTick
        };

        for (int i = 0; i < _collisionResults.Count; i++)
            tickResult.Collisions.Add(_collisionResults[i]);
        for (int i = 0; i < _crushResults.Count; i++)
            tickResult.Crushes.Add(_crushResults[i]);
        for (int i = 0; i < _attackResults.Count; i++)
            tickResult.Attacks.Add(_attackResults[i]);

        return tickResult;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if a target unit is still alive and valid in the unit list.
    /// </summary>
    private static bool IsTargetStillValid(int targetId, List<SimUnit> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].UnitId == targetId)
            {
                return units[i].IsAlive && units[i].Health > FixedPoint.Zero;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds an AttackerInfo struct from a SimUnit for combat resolution.
    /// </summary>
    private static AttackerInfo BuildAttackerInfo(SimUnit unit)
    {
        return new AttackerInfo
        {
            UnitId = unit.UnitId,
            PlayerId = unit.PlayerId,
            Position = unit.Movement.Position,
            Facing = unit.Movement.Facing,
            CurrentTargetId = unit.CurrentTargetId,
            Weapons = unit.Weapons,
            WeaponCooldowns = unit.WeaponCooldowns
        };
    }

    /// <summary>
    /// Builds a CombatTarget struct for a specific target unit. Returns null
    /// if the target is not found or not alive.
    /// </summary>
    private static CombatTarget? BuildCombatTarget(SimUnit attacker, int targetId, List<SimUnit> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].UnitId == targetId && units[i].IsAlive)
            {
                SimUnit target = units[i];
                bool isAir = (target.Category == UnitCategory.Helicopter ||
                              target.Category == UnitCategory.Jet);
                bool isBuilding = (target.Category == UnitCategory.Defense);

                return new CombatTarget
                {
                    TargetId = targetId,
                    TargetPosition = target.Movement.Position,
                    DistanceSquared = attacker.Movement.Position.DistanceSquaredTo(target.Movement.Position),
                    TargetArmor = target.ArmorClass,
                    IsAir = isAir,
                    IsBuilding = isBuilding,
                    TargetPlayerId = target.PlayerId
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the stealth state for every unit in the list.
    /// Called once per tick, before building <see cref="_combatInfos"/>, so that
    /// the resulting <see cref="SimUnit.IsCurrentlyStealthed"/> values are used
    /// for target acquisition and rendering synchronisation.
    ///
    /// Rules:
    /// <list type="bullet">
    ///   <item>
    ///     A stealth-capable unit (<see cref="SimUnit.IsStealthUnit"/> = true)
    ///     is effectively hidden when ALL of the following hold:
    ///     <list type="bullet">
    ///       <item>Its <see cref="SimUnit.StealthRevealTicks"/> countdown has
    ///             reached zero (i.e. it has not fired recently), AND</item>
    ///       <item>No enemy unit with <see cref="SimUnit.IsDetector"/> = true
    ///             is within that detector's <see cref="SimUnit.SightRange"/> of it, AND</item>
    ///       <item>For <see cref="UnitCategory.Submarine"/> units only: the unit
    ///             is in deep water (<see cref="TerrainType.DeepWater"/>). Submarines
    ///             automatically surface — and are therefore visible — when operating
    ///             in shallow water (<see cref="TerrainType.Water"/>).</item>
    ///     </list>
    ///   </item>
    ///   <item>
    ///     Non-stealth units are always <c>IsCurrentlyStealthed = false</c>.
    ///   </item>
    ///   <item>
    ///     <see cref="SimUnit.StealthRevealTicks"/> is decremented by 1 each
    ///     call (down to 0). It is set to <see cref="StealthRevealDuration"/>
    ///     by the combat phase whenever a stealth unit fires a weapon.
    ///   </item>
    /// </list>
    /// </summary>
    private static void ResolveStealthStates(List<SimUnit> units, TerrainGrid terrain)
    {
        // Step 1 — Decrement reveal timers
        for (int i = 0; i < units.Count; i++)
        {
            if (!units[i].IsAlive) continue;
            if (units[i].StealthRevealTicks <= 0) continue;

            SimUnit u = units[i];
            u.StealthRevealTicks--;
            units[i] = u;
        }

        // Step 2 — Resolve IsCurrentlyStealthed for each stealth-capable unit.
        //           Non-stealth units are trivially false and don't need touching.
        for (int i = 0; i < units.Count; i++)
        {
            if (!units[i].IsAlive) continue;
            if (!units[i].IsStealthUnit)
            {
                if (units[i].IsCurrentlyStealthed)
                {
                    SimUnit u = units[i];
                    u.IsCurrentlyStealthed = false;
                    units[i] = u;
                }
                continue;
            }

            SimUnit stealthUnit = units[i];

            // Revealed by recent attack?
            if (stealthUnit.StealthRevealTicks > 0)
            {
                stealthUnit.IsCurrentlyStealthed = false;
                units[i] = stealthUnit;
                continue;
            }

            // Submarines surface (are always visible) in shallow water.
            // Only DeepWater cells allow a submarine to remain submerged.
            // GetTerrainType is a single array read (O(1)) — no caching needed.
            if (stealthUnit.Category == UnitCategory.Submarine)
            {
                TerrainType cellType = terrain.GetTerrainType(stealthUnit.Movement.Position);
                if (cellType != TerrainType.DeepWater)
                {
                    stealthUnit.IsCurrentlyStealthed = false;
                    units[i] = stealthUnit;
                    continue;
                }
            }

            // Check whether any enemy detector can see this unit
            bool detectedByEnemy = false;
            FixedPoint stealthPosX = stealthUnit.Movement.Position.X;
            FixedPoint stealthPosY = stealthUnit.Movement.Position.Y;

            for (int j = 0; j < units.Count; j++)
            {
                if (!units[j].IsAlive) continue;
                if (!units[j].IsDetector) continue;
                if (units[j].PlayerId == stealthUnit.PlayerId) continue; // same team

                SimUnit detector = units[j];
                FixedPoint sightRange = detector.SightRange;

                // Use squared distance to avoid sqrt
                FixedPoint dx = detector.Movement.Position.X - stealthPosX;
                FixedPoint dy = detector.Movement.Position.Y - stealthPosY;
                FixedPoint distSq = dx * dx + dy * dy;
                FixedPoint rangeSq = sightRange * sightRange;

                if (distSq <= rangeSq)
                {
                    detectedByEnemy = true;
                    break;
                }
            }

            stealthUnit.IsCurrentlyStealthed = !detectedByEnemy;
            units[i] = stealthUnit;
        }
    }

    /// <summary>
    /// Applies damage to a unit by ID. If health drops to 0 or below,
    /// marks the unit as destroyed and adds it to the destroyed list.
    /// </summary>
    /// <param name="units">The unit list to search and modify.</param>
    /// <param name="targetId">ID of the unit to damage.</param>
    /// <param name="damage">Amount of damage to apply.</param>
    /// <param name="destroyedIds">List to append destroyed unit IDs to.</param>
    private static void ApplyDamage(List<SimUnit> units, int targetId, FixedPoint damage, List<int> destroyedIds)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].UnitId == targetId && units[i].IsAlive)
            {
                SimUnit target = units[i];
                target.Health = target.Health - damage;

                if (target.Health <= FixedPoint.Zero)
                {
                    target.Health = FixedPoint.Zero;
                    target.IsAlive = false;
                    destroyedIds.Add(target.UnitId);
                }

                units[i] = target;
                return;
            }
        }
    }
}
