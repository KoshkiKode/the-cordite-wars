using System.Collections.Generic;
using UnnamedRTS.Core;

namespace UnnamedRTS.Systems.Pathfinding;

// ─────────────────────────────────────────────────────────────────────────────
// Movement Domain & Class Enumerations
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// High-level movement domain.  Determines which broad category of terrain
/// interactions apply.  The pathfinding system uses this for fast early-out
/// checks before consulting the full <see cref="MovementProfile"/>.
///
/// <list type="bullet">
///   <item><b>Ground</b> — affected by terrain type, slope, mud, etc.</item>
///   <item><b>Amphibious</b> — ground rules on land, but can also enter shallow water.</item>
///   <item><b>Hover</b> — ignores most ground penalties (mud, sand) but cannot fly over cliffs.</item>
///   <item><b>Air</b> — ignores all terrain except Void (off-map).</item>
/// </list>
/// </summary>
public enum MovementDomain
{
    Ground,
    Amphibious,
    Hover,
    Air
}

/// <summary>
/// Fine-grained movement class.  Each class maps to a specific set of default
/// terrain modifiers, slope limits, and physics characteristics.  Used by the
/// <see cref="MovementProfile"/> factory methods to produce canonical presets.
///
/// Design decision: we separate Domain from Class because multiple classes can
/// share a domain (Infantry and HeavyVehicle are both Ground) but differ in
/// every other characteristic.
/// </summary>
public enum MovementClass
{
    Infantry,       // Soldiers on foot — slow, agile, steep slopes OK
    LightVehicle,   // Rocket buggies, jeeps — fast, bouncy, moderate slopes
    HeavyVehicle,   // SCUD launchers, cranes — slow, heavy, flat terrain only
    Amphibious,     // APCs that can swim — ground + shallow water
    Hover,          // Hovercraft — floats over mud/water, limited by cliffs
    LowAir,         // Helicopters — low altitude, affected by tall structures
    HighAir         // Jets — high altitude, ignores everything except Void
}

// ─────────────────────────────────────────────────────────────────────────────
// Movement Profile
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable data object that defines how a unit type interacts with the
/// terrain grid.  Every field uses <see cref="FixedPoint"/> for determinism.
///
/// <para><b>Design philosophy:</b> A profile is pure data — no behaviour, no
/// Godot Node dependency.  It can be serialized, diffed, and shared across the
/// network as part of the game config.  The cost calculator and physics systems
/// read from it but never mutate it.</para>
///
/// <para><b>Speed modifiers:</b> <see cref="TerrainSpeedModifiers"/> maps each
/// <see cref="TerrainType"/> to a multiplier applied to <see cref="MaxSpeed"/>.
/// A modifier of 1.0 means "no change", 0.3 means "30% of max speed", and
/// 1.2 means "20% speed bonus" (roads).  Types listed in
/// <see cref="ImpassableTerrain"/> are hard-blocked regardless of modifier.</para>
///
/// <para><b>Suspension &amp; bounce:</b> <see cref="SuspensionStiffness"/>
/// controls how the unit's visual model responds to height changes between
/// ticks.  Low stiffness (rocket buggy) means the chassis oscillates wildly
/// on bumps; high stiffness (SCUD launcher) means the body barely moves but
/// transfers all shock to speed loss.  This is a rendering/feel parameter that
/// also feeds into a deterministic "bump slowdown" in the physics step.</para>
/// </summary>
public sealed class MovementProfile
{
    // ── Core Identity ────────────────────────────────────────────────────

    public MovementDomain Domain { get; }
    public MovementClass Class { get; }

    // ── Kinematics ───────────────────────────────────────────────────────

    /// <summary>Top speed on flat, ideal terrain (world units per tick).</summary>
    public FixedPoint MaxSpeed { get; }

    /// <summary>Acceleration (speed added per tick toward MaxSpeed).</summary>
    public FixedPoint Acceleration { get; }

    /// <summary>Deceleration / braking force (speed removed per tick).</summary>
    public FixedPoint Deceleration { get; }

    /// <summary>
    /// Maximum heading change per tick in radians.  Infantry turn fast;
    /// heavy vehicles turn very slowly.
    /// </summary>
    public FixedPoint TurnRate { get; }

    // ── Slope & Terrain ──────────────────────────────────────────────────

    /// <summary>
    /// Maximum slope angle (radians) this unit can traverse.  Movement cost
    /// increases exponentially as the actual slope approaches this limit, and
    /// the cell becomes impassable if the slope exceeds it.
    ///
    /// Reference values:
    ///   Infantry      ~0.87  (50°)
    ///   Light vehicle ~0.61  (35°)
    ///   Heavy vehicle ~0.35  (20°)
    ///   Artillery     ~0.26  (15°)
    /// </summary>
    public FixedPoint MaxSlopeAngle { get; }

    // ── Physics ──────────────────────────────────────────────────────────

    /// <summary>
    /// Unit mass in arbitrary units.  Affects momentum (heavier = slower to
    /// start/stop), slope drag (heavier = more effort going uphill), and the
    /// crush mechanic (heavier units crush lighter obstacles).
    /// </summary>
    public FixedPoint Mass { get; }

    /// <summary>
    /// Suspension stiffness for vehicles.  High = stiff ride (SCUD launcher),
    /// low = bouncy ride (rocket buggy).  Affects both visual bounce amplitude
    /// and a deterministic "roughness penalty" in the physics step.
    ///
    /// Design note: Infantry have stiffness 0 (no suspension simulation).
    /// Air units also have 0 (no ground contact).
    /// </summary>
    public FixedPoint SuspensionStiffness { get; }

    /// <summary>
    /// Minimum terrain roughness (slope magnitude) before the unit starts
    /// taking a speed penalty.  High clearance vehicles (jeeps) tolerate more
    /// bumps than low-slung heavy vehicles.
    /// </summary>
    public FixedPoint GroundClearance { get; }

    /// <summary>
    /// Gravity multiplier.  1.0 for ground units (full gravity affects jumps,
    /// ramps), 0.0 for air units (no gravity in flight), intermediate values
    /// for hover units that bob above the surface.
    /// </summary>
    public FixedPoint GravityMultiplier { get; }

    /// <summary>
    /// Crush strength.  0 = cannot crush anything.  Higher values allow the
    /// unit to drive through infantry (crush ≥ 1), light walls (crush ≥ 2),
    /// or heavy structures (crush ≥ 3).  Inspired by the C&amp;C Generals
    /// Overlord tank.
    /// </summary>
    public FixedPoint CrushStrength { get; }

    // ── Footprint ────────────────────────────────────────────────────────

    /// <summary>
    /// Number of grid cells this unit occupies in the X axis.
    /// A 2×2 footprint means the unit blocks a 2×2 area for other ground units.
    /// </summary>
    public int FootprintWidth { get; }

    /// <summary>Number of grid cells in the Y axis.</summary>
    public int FootprintHeight { get; }

    // ── Terrain Interaction Tables ───────────────────────────────────────

    /// <summary>
    /// Speed multiplier per terrain type.  Missing entries default to 1.0
    /// (no modifier).  Values &gt; 1.0 give a speed bonus (e.g., Road = 1.2).
    /// Values &lt; 1.0 impose a penalty (e.g., Mud = 0.3 for vehicles).
    /// </summary>
    public IReadOnlyDictionary<TerrainType, FixedPoint> TerrainSpeedModifiers { get; }

    /// <summary>
    /// Terrain types this unit absolutely cannot enter, regardless of speed
    /// modifiers or slope.  Checked first by the cost calculator for a fast
    /// reject.
    /// </summary>
    public IReadOnlySet<TerrainType> ImpassableTerrain { get; }

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Private constructor — use the static factory methods below for canonical
    /// profiles, or create custom profiles via <see cref="Builder"/>.
    /// </summary>
    private MovementProfile(
        MovementDomain domain,
        MovementClass movementClass,
        FixedPoint maxSpeed,
        FixedPoint acceleration,
        FixedPoint deceleration,
        FixedPoint turnRate,
        FixedPoint maxSlopeAngle,
        FixedPoint mass,
        FixedPoint suspensionStiffness,
        FixedPoint groundClearance,
        FixedPoint gravityMultiplier,
        FixedPoint crushStrength,
        int footprintWidth,
        int footprintHeight,
        Dictionary<TerrainType, FixedPoint> terrainSpeedModifiers,
        HashSet<TerrainType> impassableTerrain)
    {
        Domain = domain;
        Class = movementClass;
        MaxSpeed = maxSpeed;
        Acceleration = acceleration;
        Deceleration = deceleration;
        TurnRate = turnRate;
        MaxSlopeAngle = maxSlopeAngle;
        Mass = mass;
        SuspensionStiffness = suspensionStiffness;
        GroundClearance = groundClearance;
        GravityMultiplier = gravityMultiplier;
        CrushStrength = crushStrength;
        FootprintWidth = footprintWidth;
        FootprintHeight = footprintHeight;
        TerrainSpeedModifiers = terrainSpeedModifiers;
        ImpassableTerrain = impassableTerrain;
    }

    // ── With* Overrides ──────────────────────────────────────────────

    /// <summary>Creates a copy of this profile with a different MaxSpeed.</summary>
    public MovementProfile WithSpeed(FixedPoint newSpeed)
    {
        return new MovementProfile(
            Domain, Class, newSpeed, Acceleration, Deceleration, TurnRate,
            MaxSlopeAngle, Mass, SuspensionStiffness, GroundClearance,
            GravityMultiplier, CrushStrength, FootprintWidth, FootprintHeight,
            new Dictionary<TerrainType, FixedPoint>(
                (IDictionary<TerrainType, FixedPoint>)TerrainSpeedModifiers),
            new HashSet<TerrainType>(ImpassableTerrain));
    }

    /// <summary>Creates a copy of this profile with a different TurnRate.</summary>
    public MovementProfile WithTurnRate(FixedPoint newTurnRate)
    {
        return new MovementProfile(
            Domain, Class, MaxSpeed, Acceleration, Deceleration, newTurnRate,
            MaxSlopeAngle, Mass, SuspensionStiffness, GroundClearance,
            GravityMultiplier, CrushStrength, FootprintWidth, FootprintHeight,
            new Dictionary<TerrainType, FixedPoint>(
                (IDictionary<TerrainType, FixedPoint>)TerrainSpeedModifiers),
            new HashSet<TerrainType>(ImpassableTerrain));
    }

    /// <summary>Creates a copy of this profile with a different Mass.</summary>
    public MovementProfile WithMass(FixedPoint newMass)
    {
        return new MovementProfile(
            Domain, Class, MaxSpeed, Acceleration, Deceleration, TurnRate,
            MaxSlopeAngle, newMass, SuspensionStiffness, GroundClearance,
            GravityMultiplier, CrushStrength, FootprintWidth, FootprintHeight,
            new Dictionary<TerrainType, FixedPoint>(
                (IDictionary<TerrainType, FixedPoint>)TerrainSpeedModifiers),
            new HashSet<TerrainType>(ImpassableTerrain));
    }

    // ── Helper: FixedPoint Shorthands ────────────────────────────────────
    //
    // These are used only within the factory methods below.  We define them
    // here to keep the preset code concise and readable.  FromFloat is
    // acceptable here because factories run once at load time, not per-tick.

    private static FixedPoint FP(float v) => FixedPoint.FromFloat(v);
    private static FixedPoint FPi(int v) => FixedPoint.FromInt(v);

    // ═════════════════════════════════════════════════════════════════════
    // STATIC FACTORY METHODS — Canonical Presets
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Infantry</b> — soldiers on foot.
    ///
    /// <list type="bullet">
    ///   <item>Slow (MaxSpeed 0.15 units/tick ≈ 4.5 units/s at 30 tps).</item>
    ///   <item>Very agile — fast turn rate, high max slope (50°).</item>
    ///   <item>Can traverse almost everything: grass, dirt, sand, rock, mud,
    ///         road, ice, concrete, bridge.  Slowed by mud and sand.</item>
    ///   <item>Cannot enter Water, DeepWater, Lava, or Void.</item>
    ///   <item>1×1 footprint — fits through narrow gaps.</item>
    ///   <item>Light mass (1.0) — can be crushed by heavy vehicles.</item>
    /// </list>
    /// </summary>
    public static MovementProfile Infantry()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(1.0f) },
            { TerrainType.Dirt,     FP(0.9f) },
            { TerrainType.Sand,     FP(0.7f) },
            { TerrainType.Rock,     FP(0.8f) },
            { TerrainType.Mud,      FP(0.5f) },
            { TerrainType.Road,     FP(1.1f) },
            { TerrainType.Ice,      FP(0.6f) },
            { TerrainType.Concrete, FP(1.1f) },
            { TerrainType.Bridge,   FP(1.0f) },
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.Infantry,
            maxSpeed:             FP(0.15f),    // ~4.5 u/s at 30 tps
            acceleration:         FP(0.03f),    // reaches max speed in ~5 ticks
            deceleration:         FP(0.05f),    // stops quickly
            turnRate:             FP(0.30f),    // ~17° per tick — very agile
            maxSlopeAngle:        FP(0.87f),    // ~50° — soldiers can scramble up steep hills
            mass:                 FP(1.0f),
            suspensionStiffness:  FixedPoint.Zero,  // no suspension model for infantry
            groundClearance:      FP(0.05f),    // feet handle rough ground well
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FixedPoint.Zero,  // cannot crush anything
            footprintWidth:       1,
            footprintHeight:      1,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Light Vehicle</b> — fast attack buggies, rocket buggies (C&amp;C
    /// Generals style).
    ///
    /// <list type="bullet">
    ///   <item>Fast (MaxSpeed 0.35 units/tick ≈ 10.5 u/s) with quick accel.</item>
    ///   <item>Very bouncy suspension (stiffness 0.3) — flies off ramps.</item>
    ///   <item>Moderate slope limit (35°).  Bounces reduce effective speed on
    ///         rough terrain.</item>
    ///   <item>Mud and sand slow it significantly.  Road gives a big bonus.</item>
    ///   <item>Cannot enter Water, DeepWater, Lava, or Void.</item>
    ///   <item>2×2 footprint.  Light mass (3.0) — cannot crush infantry.</item>
    /// </list>
    /// </summary>
    public static MovementProfile LightVehicle()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(1.0f) },
            { TerrainType.Dirt,     FP(0.85f) },
            { TerrainType.Sand,     FP(0.5f) },
            { TerrainType.Rock,     FP(0.7f) },
            { TerrainType.Mud,      FP(0.35f) },
            { TerrainType.Road,     FP(1.25f) },
            { TerrainType.Ice,      FP(0.45f) },
            { TerrainType.Concrete, FP(1.2f) },
            { TerrainType.Bridge,   FP(1.0f) },
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.LightVehicle,
            maxSpeed:             FP(0.35f),    // ~10.5 u/s — fast
            acceleration:         FP(0.06f),    // quick off the line
            deceleration:         FP(0.04f),    // light brakes
            turnRate:             FP(0.15f),    // ~8.6° per tick
            maxSlopeAngle:        FP(0.61f),    // ~35°
            mass:                 FP(3.0f),
            suspensionStiffness:  FP(0.3f),     // very bouncy
            groundClearance:      FP(0.15f),    // high clearance — handles bumps
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FixedPoint.Zero,
            footprintWidth:       2,
            footprintHeight:      2,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Heavy Vehicle</b> — SCUD launchers, mobile construction vehicles.
    ///
    /// <list type="bullet">
    ///   <item>Slow (MaxSpeed 0.10 u/tick ≈ 3.0 u/s).  Sluggish accel/decel.</item>
    ///   <item>Very stiff suspension (0.9) — barely bounces, but transfers
    ///         shock to massive speed loss on rough terrain.</item>
    ///   <item>Low slope limit (20°).  Needs relatively flat ground.</item>
    ///   <item>Mud is brutal (0.15 modifier).  Sand is bad (0.3).  Roads are
    ///         essential (1.3 bonus).</item>
    ///   <item>Cannot enter Water, DeepWater, Lava, Ice, or Void.  Ice is
    ///         impassable because the heavy weight breaks through.</item>
    ///   <item>3×3 footprint.  Very heavy mass (12.0).</item>
    /// </list>
    /// </summary>
    public static MovementProfile HeavyVehicle()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(0.9f) },
            { TerrainType.Dirt,     FP(0.7f) },
            { TerrainType.Sand,     FP(0.3f) },
            { TerrainType.Rock,     FP(0.6f) },
            { TerrainType.Mud,      FP(0.15f) },
            { TerrainType.Road,     FP(1.3f) },
            { TerrainType.Concrete, FP(1.25f) },
            { TerrainType.Bridge,   FP(0.8f) },  // bridges creak under the weight
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Ice,    // too heavy — breaks through
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.HeavyVehicle,
            maxSpeed:             FP(0.10f),
            acceleration:         FP(0.01f),    // takes ~10 ticks to reach max
            deceleration:         FP(0.015f),   // slow to stop (momentum)
            turnRate:             FP(0.05f),     // ~2.9° per tick — very sluggish
            maxSlopeAngle:        FP(0.35f),     // ~20°
            mass:                 FP(12.0f),
            suspensionStiffness:  FP(0.9f),      // very stiff
            groundClearance:      FP(0.05f),     // low to the ground
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FP(2.0f),      // crushes infantry and light walls
            footprintWidth:       3,
            footprintHeight:      3,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>APC</b> — armoured personnel carrier.  Medium all-rounder.
    ///
    /// <list type="bullet">
    ///   <item>Medium speed (0.20 u/tick ≈ 6.0 u/s).</item>
    ///   <item>Moderate slope (30°).  Decent off-road capability.</item>
    ///   <item>Can crush infantry (crush strength 1.0).</item>
    ///   <item>2×2 footprint.  Medium mass (6.0).</item>
    /// </list>
    /// </summary>
    public static MovementProfile APC()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(1.0f) },
            { TerrainType.Dirt,     FP(0.85f) },
            { TerrainType.Sand,     FP(0.55f) },
            { TerrainType.Rock,     FP(0.7f) },
            { TerrainType.Mud,      FP(0.3f) },
            { TerrainType.Road,     FP(1.2f) },
            { TerrainType.Ice,      FP(0.5f) },
            { TerrainType.Concrete, FP(1.15f) },
            { TerrainType.Bridge,   FP(1.0f) },
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.Amphibious, // APC class uses Amphibious movement class for versatility
            maxSpeed:             FP(0.20f),
            acceleration:         FP(0.03f),
            deceleration:         FP(0.04f),
            turnRate:             FP(0.10f),     // ~5.7° per tick
            maxSlopeAngle:        FP(0.52f),     // ~30°
            mass:                 FP(6.0f),
            suspensionStiffness:  FP(0.6f),
            groundClearance:      FP(0.10f),
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FP(1.0f),      // crushes infantry
            footprintWidth:       2,
            footprintHeight:      2,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Tank</b> — main battle tank.  Medium-heavy combat unit.
    ///
    /// <list type="bullet">
    ///   <item>Medium speed (0.18 u/tick ≈ 5.4 u/s).</item>
    ///   <item>Good slope handling for its weight (28° max).  Tracked drive
    ///         gives better traction than wheeled vehicles.</item>
    ///   <item>Crushes infantry (strength 1.5).  Heavy enough to push through
    ///         light fences.</item>
    ///   <item>2×2 footprint.  Heavy mass (8.0).</item>
    /// </list>
    /// </summary>
    public static MovementProfile Tank()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(1.0f) },
            { TerrainType.Dirt,     FP(0.9f) },
            { TerrainType.Sand,     FP(0.5f) },
            { TerrainType.Rock,     FP(0.75f) },
            { TerrainType.Mud,      FP(0.25f) },
            { TerrainType.Road,     FP(1.15f) },
            { TerrainType.Ice,      FP(0.4f) },
            { TerrainType.Concrete, FP(1.1f) },
            { TerrainType.Bridge,   FP(0.9f) },
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.HeavyVehicle,
            maxSpeed:             FP(0.18f),
            acceleration:         FP(0.025f),
            deceleration:         FP(0.03f),
            turnRate:             FP(0.08f),     // ~4.6° per tick
            maxSlopeAngle:        FP(0.49f),     // ~28°
            mass:                 FP(8.0f),
            suspensionStiffness:  FP(0.7f),
            groundClearance:      FP(0.08f),
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FP(1.5f),      // crushes infantry + light fences
            footprintWidth:       2,
            footprintHeight:      2,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Artillery</b> — long-range indirect fire platform.
    ///
    /// <list type="bullet">
    ///   <item>Very slow (0.06 u/tick ≈ 1.8 u/s).  Needs escort.</item>
    ///   <item>Needs very flat terrain (15° max slope).  Gets stuck easily.</item>
    ///   <item>3×3 footprint — needs wide roads and open terrain.</item>
    ///   <item>Extremely heavy (14.0).  Crushes infantry and light walls.</item>
    ///   <item>Road-dependent: terrible off-road, excellent on roads.</item>
    /// </list>
    /// </summary>
    public static MovementProfile Artillery()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>
        {
            { TerrainType.Grass,    FP(0.7f) },
            { TerrainType.Dirt,     FP(0.5f) },
            { TerrainType.Sand,     FP(0.2f) },
            { TerrainType.Rock,     FP(0.4f) },
            { TerrainType.Mud,      FP(0.1f) },
            { TerrainType.Road,     FP(1.3f) },
            { TerrainType.Concrete, FP(1.25f) },
            { TerrainType.Bridge,   FP(0.7f) },  // risky — heavy load on bridge
        };

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Water,
            TerrainType.DeepWater,
            TerrainType.Lava,
            TerrainType.Ice,    // too heavy
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Ground,
            movementClass:        MovementClass.HeavyVehicle,
            maxSpeed:             FP(0.06f),
            acceleration:         FP(0.008f),   // glacial acceleration
            deceleration:         FP(0.01f),
            turnRate:             FP(0.03f),     // ~1.7° per tick — barely turns
            maxSlopeAngle:        FP(0.26f),     // ~15° — needs flat ground
            mass:                 FP(14.0f),
            suspensionStiffness:  FP(0.95f),     // almost rigid
            groundClearance:      FP(0.03f),     // very low
            gravityMultiplier:    FixedPoint.One,
            crushStrength:        FP(2.0f),
            footprintWidth:       3,
            footprintHeight:      3,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Helicopter</b> — low-altitude rotary-wing aircraft.
    ///
    /// <list type="bullet">
    ///   <item>Fast (0.30 u/tick ≈ 9.0 u/s).  Good acceleration.</item>
    ///   <item>Air domain — ignores all terrain for movement purposes.</item>
    ///   <item>Terrain modifiers are all 1.0 (terrain is irrelevant in flight).</item>
    ///   <item>Only Void is impassable (off-map boundary).</item>
    ///   <item>2×2 footprint (for landing pad / collision purposes).</item>
    ///   <item>Zero gravity multiplier — no ground interaction in flight.</item>
    /// </list>
    /// </summary>
    public static MovementProfile Helicopter()
    {
        // Air units get a uniform 1.0 modifier — terrain doesn't slow them.
        var modifiers = new Dictionary<TerrainType, FixedPoint>();

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Air,
            movementClass:        MovementClass.LowAir,
            maxSpeed:             FP(0.30f),
            acceleration:         FP(0.04f),
            deceleration:         FP(0.05f),
            turnRate:             FP(0.20f),     // ~11.5° per tick — agile in the air
            maxSlopeAngle:        FP(1.57f),     // π/2 — irrelevant for air, set to max
            mass:                 FP(4.0f),
            suspensionStiffness:  FixedPoint.Zero,  // no ground contact
            groundClearance:      FixedPoint.Zero,
            gravityMultiplier:    FixedPoint.Zero,  // no gravity in flight
            crushStrength:        FixedPoint.Zero,
            footprintWidth:       2,
            footprintHeight:      2,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }

    /// <summary>
    /// <b>Jet</b> — high-altitude fixed-wing aircraft.
    ///
    /// <list type="bullet">
    ///   <item>Fastest unit (0.50 u/tick ≈ 15.0 u/s).  High acceleration.</item>
    ///   <item>High air domain — above everything.</item>
    ///   <item>Slow turn rate (fixed-wing physics — wide banking turns).</item>
    ///   <item>1×1 footprint (small air-to-air collision profile).</item>
    ///   <item>Only Void is impassable.</item>
    /// </list>
    /// </summary>
    public static MovementProfile Jet()
    {
        var modifiers = new Dictionary<TerrainType, FixedPoint>();

        var impassable = new HashSet<TerrainType>
        {
            TerrainType.Void,
        };

        return new MovementProfile(
            domain:               MovementDomain.Air,
            movementClass:        MovementClass.HighAir,
            maxSpeed:             FP(0.50f),     // ~15 u/s — fastest
            acceleration:         FP(0.05f),
            deceleration:         FP(0.03f),     // jets are hard to slow down
            turnRate:             FP(0.06f),     // ~3.4° per tick — wide turns
            maxSlopeAngle:        FP(1.57f),     // irrelevant for air
            mass:                 FP(5.0f),
            suspensionStiffness:  FixedPoint.Zero,
            groundClearance:      FixedPoint.Zero,
            gravityMultiplier:    FixedPoint.Zero,
            crushStrength:        FixedPoint.Zero,
            footprintWidth:       1,
            footprintHeight:      1,
            terrainSpeedModifiers: modifiers,
            impassableTerrain:    impassable
        );
    }
}
