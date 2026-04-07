using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.Units;

/// <summary>
/// Broad unit classification. Drives UI grouping, AI targeting
/// priority, and formation behaviour.
/// </summary>
public enum UnitCategory
{
    Infantry,
    LightVehicle,
    HeavyVehicle,
    Tank,
    APC,
    Artillery,
    Helicopter,
    Jet,
    Support,
    Special,
    Defense,
    PatrolBoat,    // Small fast naval vessel — cheap, early-game scouting
    Destroyer,     // Mid-range naval combat ship — balanced attack and defence
    Submarine,     // Stealth naval unit — torpedo specialist, low visibility
    CapitalShip    // Heavy naval asset — high health, powerful weapons, slow
}

/// <summary>
/// Weapon archetype. Used to look up base audio/VFX and
/// to drive bonus/malus tables against <see cref="ArmorType"/>.
/// </summary>
public enum WeaponType
{
    None,
    MachineGun,
    Cannon,
    Missile,
    Rockets,
    Laser,
    Flak,
    Bomb,
    Mortar,
    Sniper,
    Flamethrower,
    EMP,
    GatlingGun,
    SAM,
    Torpedo,
    ChemicalSpray
}

/// <summary>
/// Armor classification. Weapon damage is modified by a per-armor-type
/// multiplier stored in <see cref="WeaponData.ArmorModifiers"/>.
/// </summary>
public enum ArmorType
{
    Unarmored,
    Light,
    Medium,
    Heavy,
    Aircraft,
    Building,
    Naval    // Ship hull — anti-ship weapons (torpedoes) deal bonus damage;
             // most small-arms weapons are ineffective against naval armour
}

/// <summary>
/// Flags for valid engagement targets. A weapon can engage any
/// combination of ground, air, building, and naval targets.
/// </summary>
[Flags]
public enum TargetType
{
    None     = 0,
    Ground   = 1,
    Air      = 2,
    Building = 4,
    Naval    = 8
}

/// <summary>
/// Immutable definition of a single weapon hardpoint, loaded from JSON.
/// A unit may carry zero, one, or two weapons.
/// </summary>
public sealed class WeaponData
{
    /// <summary>Unique weapon identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Weapon archetype.</summary>
    public WeaponType Type { get; init; } = WeaponType.None;

    /// <summary>Damage dealt per shot.</summary>
    public FixedPoint Damage { get; init; }

    /// <summary>Shots per second.</summary>
    public FixedPoint RateOfFire { get; init; }

    /// <summary>Maximum engagement range in grid cells.</summary>
    public FixedPoint Range { get; init; }

    /// <summary>Minimum engagement range (e.g., artillery deadzone). 0 = no minimum.</summary>
    public FixedPoint MinRange { get; init; }

    /// <summary>Projectile travel speed. 0 = hitscan.</summary>
    public FixedPoint ProjectileSpeed { get; init; }

    /// <summary>Splash damage radius. 0 = single-target.</summary>
    public FixedPoint AreaOfEffect { get; init; }

    /// <summary>Which target types this weapon can engage.</summary>
    public TargetType CanTarget { get; init; } = TargetType.Ground;

    /// <summary>Base hit chance (100 = always hits).</summary>
    public FixedPoint AccuracyPercent { get; init; } = FixedPoint.FromInt(100);

    /// <summary>
    /// Damage multiplier vs each armor type.
    /// Missing entries default to 1.0× at runtime.
    /// </summary>
    public Dictionary<ArmorType, FixedPoint> ArmorModifiers { get; init; } = new();
}

/// <summary>
/// Immutable data object describing a unit template, loaded from JSON.
/// Instances of this class are shared across all units of the same type;
/// per-instance runtime state lives elsewhere.
/// </summary>
public sealed class UnitData
{
    // ── Identity ─────────────────────────────────────────────────────

    /// <summary>Unique identifier (e.g., "valkyr_interceptor").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Owning faction ID.</summary>
    public string FactionId { get; init; } = string.Empty;

    /// <summary>Broad unit classification.</summary>
    public UnitCategory Category { get; init; }

    /// <summary>
    /// Maps to a <c>MovementProfile</c> factory method name
    /// (e.g., "Infantry", "LightVehicle", "Jet").
    /// </summary>
    public string MovementClassId { get; init; } = string.Empty;

    // ── Core Stats ───────────────────────────────────────────────────

    /// <summary>Maximum hit points.</summary>
    public FixedPoint MaxHealth { get; init; }

    /// <summary>Flat damage reduction per hit.</summary>
    public FixedPoint ArmorValue { get; init; }

    /// <summary>Armor classification for incoming-damage modifiers.</summary>
    public ArmorType ArmorClass { get; init; }

    /// <summary>Vision range in grid cells.</summary>
    public FixedPoint SightRange { get; init; }

    /// <summary>Seconds to produce this unit.</summary>
    public FixedPoint BuildTime { get; init; }

    /// <summary>Primary resource cost.</summary>
    public int Cost { get; init; }

    /// <summary>Secondary resource cost.</summary>
    public int SecondaryCost { get; init; }

    /// <summary>Population supply consumed.</summary>
    public int PopulationCost { get; init; }

    // ── Weapons ──────────────────────────────────────────────────────

    /// <summary>Weapon hardpoints (0, 1, or 2 entries).</summary>
    public List<WeaponData> Weapons { get; init; } = new();

    // ── Abilities ────────────────────────────────────────────────────

    /// <summary>Unique ability identifier, or null if none.</summary>
    public string? SpecialAbilityId { get; init; }

    /// <summary>Flavor text.</summary>
    public string Description { get; init; } = string.Empty;

    // ── Movement Overrides ───────────────────────────────────────────
    // When non-null these replace the corresponding value from the
    // base MovementProfile resolved via MovementClassId.

    /// <summary>Override base movement speed.</summary>
    public FixedPoint? SpeedOverride { get; init; }

    /// <summary>Override base turn rate.</summary>
    public FixedPoint? TurnRateOverride { get; init; }

    /// <summary>Override base mass.</summary>
    public FixedPoint? MassOverride { get; init; }

    // ── Footprint ────────────────────────────────────────────────────

    /// <summary>Grid footprint width.</summary>
    public int FootprintWidth { get; init; } = 1;

    /// <summary>Grid footprint height.</summary>
    public int FootprintHeight { get; init; } = 1;

    // ── Flags ────────────────────────────────────────────────────────

    /// <summary>Can enter garrison-capable buildings.</summary>
    public bool CanGarrison { get; init; }

    /// <summary>Can crush infantry units by driving over them.</summary>
    public bool CanCrush { get; init; }

    /// <summary>Starts invisible until attacking or detected.</summary>
    public bool IsStealthed { get; init; }

    /// <summary>Can reveal stealthed units within sight range.</summary>
    public bool IsDetector { get; init; }

    /// <summary>
    /// Computes the MovementProfile dynamically based on this unit's base class
    /// and any per-unit overrides defined.
    /// </summary>
    public MovementProfile GetMovementProfile()
    {
        MovementProfile profile = MovementClassId switch
        {
            "Infantry"     => MovementProfile.Infantry(),
            "LightVehicle" => MovementProfile.LightVehicle(),
            "HeavyVehicle" => MovementProfile.HeavyVehicle(),
            "APC"          => MovementProfile.APC(),
            "Tank"         => MovementProfile.Tank(),
            "Artillery"    => MovementProfile.Artillery(),
            "Helicopter"   => MovementProfile.Helicopter(),
            "Jet"          => MovementProfile.Jet(),
            "Naval"        => MovementProfile.Naval(),
            _ => throw new ArgumentException(
                $"Unknown MovementClassId '{MovementClassId}' on unit '{Id}'.")
        };

        if (SpeedOverride.HasValue)
            profile = profile.WithSpeed(SpeedOverride.Value);

        if (TurnRateOverride.HasValue)
            profile = profile.WithTurnRate(TurnRateOverride.Value);

        if (MassOverride.HasValue)
            profile = profile.WithMass(MassOverride.Value);

        return profile;
    }
}
