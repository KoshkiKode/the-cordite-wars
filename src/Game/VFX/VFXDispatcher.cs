using System.Collections.Generic;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.VFX;

/// <summary>
/// Pure-C# mapping layer that translates simulation combat events into lists of
/// <see cref="VFXRequest"/> values.  Contains no Godot dependencies and is
/// therefore fully unit-testable.
///
/// <para>
/// <see cref="CombatVFXBridge"/> delegates all dispatch decisions here and
/// then converts each request into a real <see cref="Godot.GpuParticles3D"/>
/// via <see cref="ParticleFactory"/>.
/// </para>
/// </summary>
public static class VFXDispatcher
{
    // ── AttackFired ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effects to spawn at the weapon-discharge position.
    /// Every weapon produces a muzzle flash; selected types add extra effects.
    /// </summary>
    public static IReadOnlyList<VFXRequest> GetAttackFiredEffects(WeaponType weaponType)
    {
        var effects = new List<VFXRequest>(2);

        // Muzzle flash on every discharge
        effects.Add(VFXRequest.At(VFXEffectType.MuzzleFlash));

        // Rockets, missiles, and torpedoes leave a thruster trail
        if (weaponType is WeaponType.Missile or WeaponType.Rockets or WeaponType.SAM or WeaponType.Torpedo)
            effects.Add(VFXRequest.At(VFXEffectType.ThrusterTrail));

        // Area-denial weapons add a smoke puff at the emitter
        if (weaponType is WeaponType.Flamethrower or WeaponType.ChemicalSpray)
            effects.Add(VFXRequest.At(VFXEffectType.SmokePuff));

        return effects;
    }

    // ── AttackImpact ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effects to spawn at an impact point.
    /// A clean miss with no AoE produces no visual feedback.
    /// </summary>
    public static IReadOnlyList<VFXRequest> GetAttackImpactEffects(bool isHit, bool hasAoe)
    {
        // Clean miss — no visual
        if (!isHit && !hasAoe)
            return [];

        if (hasAoe)
        {
            // Splash weapon: explosion + lingering smoke
            return
            [
                VFXRequest.At(VFXEffectType.ExplosionMedium),
                VFXRequest.At(VFXEffectType.SmokePuff)
            ];
        }

        // Direct hit: sparks only
        return [VFXRequest.At(VFXEffectType.Spark)];
    }

    // ── UnitDeath ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effects to spawn when a unit is destroyed.
    /// The effect set scales with the unit's combat category.
    /// </summary>
    public static IReadOnlyList<VFXRequest> GetUnitDeathEffects(UnitCategory category)
    {
        switch (category)
        {
            // Infantry and special ops: small blast + dust
            case UnitCategory.Infantry:
            case UnitCategory.Special:
                return
                [
                    VFXRequest.At(VFXEffectType.ExplosionSmall),
                    VFXRequest.At(VFXEffectType.DustCloud)
                ];

            // Light armour and small vessels: medium explosion + smoke
            case UnitCategory.LightVehicle:
            case UnitCategory.APC:
            case UnitCategory.Support:
            case UnitCategory.Defense:
            case UnitCategory.PatrolBoat:
                return
                [
                    VFXRequest.At(VFXEffectType.ExplosionMedium),
                    VFXRequest.At(VFXEffectType.SmokePuff)
                ];

            // Heavy armour, aircraft, and sub-capital ships: large explosion + smoke + sparks
            case UnitCategory.HeavyVehicle:
            case UnitCategory.Tank:
            case UnitCategory.Artillery:
            case UnitCategory.Helicopter:
            case UnitCategory.Jet:
            case UnitCategory.Destroyer:
            case UnitCategory.Submarine:
                return
                [
                    VFXRequest.At(VFXEffectType.ExplosionLarge),
                    VFXRequest.At(VFXEffectType.SmokePuff),
                    VFXRequest.At(VFXEffectType.Spark)
                ];

            // Capital ship: massive triple-explosion sequence fanned across the hull
            case UnitCategory.CapitalShip:
                return
                [
                    VFXRequest.At(VFXEffectType.ExplosionLarge),
                    new VFXRequest(VFXEffectType.ExplosionLarge,   OffsetX:  2f, OffsetY: 0f, OffsetZ: 0f),
                    new VFXRequest(VFXEffectType.ExplosionMedium,  OffsetX: -1f, OffsetY: 1f, OffsetZ: 1f),
                    VFXRequest.At(VFXEffectType.SmokePuff),
                    VFXRequest.At(VFXEffectType.WaterSplash)
                ];

            default:
                return [VFXRequest.At(VFXEffectType.ExplosionSmall)];
        }
    }
}
