using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.Units;

// ═══════════════════════════════════════════════════════════════════════════════
// COMBAT RESOLVER — Deterministic damage pipeline for unit combat
// ═══════════════════════════════════════════════════════════════════════════════
//
// This system handles all combat calculations between units. It is the single
// source of truth for damage computation, target acquisition, and hit resolution.
//
// DAMAGE PIPELINE (per attack):
//   1. Accuracy roll: rng.NextInt(100) < weapon.AccuracyPercent → hit or miss
//   2. Base damage from weapon
//   3. Apply armor type modifier: weapon.ArmorModifiers[target.ArmorClass]
//   4. Apply flat armor reduction: max(1, damage - targetArmorValue)
//   5. If weapon has AoE > 0: splash damage with distance falloff
//   6. Return result
//
// TARGET ACQUISITION (C&C-style priority):
//   1. Keep current target if still valid and in range (prevents jitter)
//   2. Nearest enemy unit that any weapon can hit
//   3. Priority: threats attacking us > threats attacking allies > nearest > buildings
//
// DETERMINISM:
//   All math uses FixedPoint. RNG is a deterministic seeded PRNG.
//   No float, no LINQ, no Dictionary iteration, no non-deterministic ops.
//
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Information about a potential combat target. Pre-computed by the caller
/// to avoid redundant distance calculations.
/// </summary>
public struct CombatTarget
{
    /// <summary>The target unit's unique identifier.</summary>
    public int TargetId;

    /// <summary>World position of the target.</summary>
    public FixedVector2 TargetPosition;

    /// <summary>
    /// Squared distance from attacker to target. Pre-computed to avoid
    /// redundant sqrt operations during range checks.
    /// </summary>
    public FixedPoint DistanceSquared;

    /// <summary>The target's armor classification.</summary>
    public ArmorType TargetArmor;

    /// <summary>True if the target is an airborne unit.</summary>
    public bool IsAir;

    /// <summary>True if the target is a building/structure.</summary>
    public bool IsBuilding;

    /// <summary>True if the target is a naval unit.</summary>
    public bool IsNaval;

    /// <summary>Player ID of the target (for friend/foe checks).</summary>
    public int TargetPlayerId;
}

/// <summary>
/// Result of a single attack resolution. Contains all information needed
/// to apply damage, spawn visual effects, and trigger events.
/// </summary>
public struct AttackResult
{
    /// <summary>The attacking unit's ID.</summary>
    public int AttackerId;

    /// <summary>The primary target unit's ID.</summary>
    public int TargetId;

    /// <summary>Which weapon slot fired (0 or 1).</summary>
    public int WeaponIndex;

    /// <summary>Damage dealt to the primary target after all modifiers.</summary>
    public FixedPoint DamageDealt;

    /// <summary>True if the accuracy roll passed and the shot connected.</summary>
    public bool DidHit;

    /// <summary>True if the target's health dropped to zero or below.</summary>
    public bool TargetDestroyed;

    /// <summary>
    /// World position where the shot lands. Equals TargetPosition on hit,
    /// or a near-miss position on miss. Used for splash damage origin and VFX.
    /// </summary>
    public FixedVector2 ImpactPosition;

    /// <summary>
    /// Units hit by area-of-effect splash damage, or null if the weapon
    /// has no AoE. Each entry contains the unit ID and damage dealt.
    /// </summary>
    public List<(int unitId, FixedPoint damage)>? SplashTargets;
}

/// <summary>
/// Information about the attacking unit, passed to the combat resolver.
/// </summary>
public struct AttackerInfo
{
    /// <summary>The attacker's unique identifier.</summary>
    public int UnitId;

    /// <summary>The attacker's owning player.</summary>
    public int PlayerId;

    /// <summary>The attacker's current world position.</summary>
    public FixedVector2 Position;

    /// <summary>
    /// The attacker's current facing angle in fixed-point radians [0, 2π).
    /// Used for firing arc checks on fixed-weapon units.
    /// </summary>
    public FixedPoint Facing;

    /// <summary>
    /// The unit we're currently targeting, or null if idle.
    /// Used for target retention (don't jitter between targets).
    /// </summary>
    public int? CurrentTargetId;

    /// <summary>All weapons on this unit.</summary>
    public List<WeaponData> Weapons;

    /// <summary>
    /// Remaining cooldown ticks per weapon. Index matches Weapons list.
    /// A weapon can fire when its cooldown reaches 0 or below.
    /// </summary>
    public List<FixedPoint> WeaponCooldowns;
}

/// <summary>
/// Combat-relevant info about a unit in the simulation. Used by the target
/// acquisition system to evaluate potential targets.
/// </summary>
public struct UnitCombatInfo
{
    /// <summary>Unique identifier.</summary>
    public int UnitId;

    /// <summary>Owning player ID.</summary>
    public int PlayerId;

    /// <summary>Current world position.</summary>
    public FixedVector2 Position;

    /// <summary>Current health.</summary>
    public FixedPoint Health;

    /// <summary>Maximum health.</summary>
    public FixedPoint MaxHealth;

    /// <summary>Flat armor reduction value per hit.</summary>
    public FixedPoint ArmorValue;

    /// <summary>Armor classification for modifier lookups.</summary>
    public ArmorType ArmorClass;

    /// <summary>True if this is an airborne unit.</summary>
    public bool IsAir;

    /// <summary>True if this is a building/structure.</summary>
    public bool IsBuilding;

    /// <summary>True if this is a naval unit (Water movement domain).</summary>
    public bool IsNaval;

    /// <summary>True if this unit is currently stealthed and undetected.</summary>
    public bool IsStealthed;

    /// <summary>Collision radius for spatial queries.</summary>
    public FixedPoint Radius;
}

// NOTE: DeterministicRng is defined in CorditeWars.Core.DeterministicRng.
// The CombatResolver uses it via the 'using CorditeWars.Core;' import.
// It is the xoshiro256** algorithm — high quality, fast, and deterministic.

/// <summary>
/// Handles all deterministic combat calculations. Pure methods — no internal
/// mutable state. Thread-safe when given distinct RNG instances.
/// </summary>
public class CombatResolver
{
    // ── Constants ────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum damage dealt by any hit. Ensures that even heavily armored
    /// targets take at least 1 point of damage per successful hit.
    /// </summary>
    private static readonly FixedPoint MinDamage = FixedPoint.One;

    /// <summary>
    /// Splash damage falloff: 50% at the edge of the AoE radius.
    /// Linear interpolation from 100% at center to this value at edge.
    /// </summary>
    private static readonly FixedPoint SplashEdgeFalloff = FixedPoint.Half;

    /// <summary>Default armor modifier when a weapon has no entry for an armor type.</summary>
    private static readonly FixedPoint DefaultArmorModifier = FixedPoint.One;

    /// <summary>π constant for arc calculations.</summary>
    private static readonly FixedPoint TwoPi = FixedPoint.FromRaw(411775);
    private static readonly FixedPoint Pi = FixedPoint.FromRaw(205887);

    // ── Scratch lists (reused to avoid allocation) ───────────────────────

    private readonly List<int> _spatialQueryResults = new List<int>(64);
    private readonly List<int> _candidateTargets = new List<int>(64);

    // ═══════════════════════════════════════════════════════════════════════
    // WEAPON ELIGIBILITY
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether a weapon can engage a given target based on target
    /// type flags and range constraints.
    /// </summary>
    /// <param name="weapon">The weapon to check.</param>
    /// <param name="target">The potential target with pre-computed distance.</param>
    /// <returns>True if the weapon can fire at this target.</returns>
    public bool CanAttack(WeaponData weapon, CombatTarget target)
    {
        // ── Target type check ──
        // The weapon's CanTarget flags must include the target's type.
        if (target.IsAir)
        {
            if ((weapon.CanTarget & TargetType.Air) == TargetType.None)
                return false;
        }
        else if (target.IsBuilding)
        {
            if ((weapon.CanTarget & TargetType.Building) == TargetType.None)
                return false;
        }
        else if (target.IsNaval)
        {
            if ((weapon.CanTarget & TargetType.Naval) == TargetType.None)
                return false;
        }
        else
        {
            // Ground unit
            if ((weapon.CanTarget & TargetType.Ground) == TargetType.None)
                return false;
        }

        // ── Maximum range check ──
        // distance² <= range²
        FixedPoint rangeSq = weapon.Range * weapon.Range;
        if (target.DistanceSquared > rangeSq)
            return false;

        // ── Minimum range check (artillery dead zone) ──
        // distance² >= minRange²
        if (weapon.MinRange > FixedPoint.Zero)
        {
            FixedPoint minRangeSq = weapon.MinRange * weapon.MinRange;
            if (target.DistanceSquared < minRangeSq)
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ATTACK RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a single attack from one weapon against one target.
    /// Handles accuracy, damage modifiers, armor, and AoE splash.
    /// </summary>
    /// <param name="attacker">The attacking unit's combat info.</param>
    /// <param name="target">The primary target with pre-computed distance.</param>
    /// <param name="weapon">The weapon being fired.</param>
    /// <param name="weaponIndex">Index of the weapon in the attacker's weapon list.</param>
    /// <param name="rng">Deterministic RNG for accuracy rolls.</param>
    /// <param name="spatial">Spatial hash for AoE target queries.</param>
    /// <param name="allUnits">All units in the simulation for AoE damage lookup.</param>
    /// <returns>Complete attack result including damage, hit status, and splash.</returns>
    public AttackResult ResolveAttack(
        AttackerInfo attacker,
        CombatTarget target,
        WeaponData weapon,
        int weaponIndex,
        DeterministicRng rng,
        SpatialHash spatial,
        List<UnitCombatInfo> allUnits)
    {
        var result = new AttackResult
        {
            AttackerId = attacker.UnitId,
            TargetId = target.TargetId,
            WeaponIndex = weaponIndex,
            DamageDealt = FixedPoint.Zero,
            DidHit = false,
            TargetDestroyed = false,
            ImpactPosition = target.TargetPosition,
            SplashTargets = null
        };

        // ── Step 1: Accuracy roll ──
        // rng.NextInt(100) produces [0, 99]. Hit if result < accuracy percent.
        int accuracyRoll = rng.NextInt(100);
        int accuracyThreshold = weapon.AccuracyPercent.ToInt();

        if (accuracyRoll >= accuracyThreshold)
        {
            // Miss — shot goes wide, no damage
            result.DidHit = false;
            return result;
        }

        result.DidHit = true;

        // ── Step 2: Base damage ──
        FixedPoint damage = weapon.Damage;

        // ── Step 3: Armor type modifier ──
        // Lookup the weapon's modifier for this armor type.
        // Missing entries default to 1.0 (no modifier).
        FixedPoint armorModifier = DefaultArmorModifier;
        if (weapon.ArmorModifiers != null &&
            weapon.ArmorModifiers.TryGetValue(target.TargetArmor, out FixedPoint mod))
        {
            armorModifier = mod;
        }
        damage = damage * armorModifier;

        // ── Step 4: Flat armor reduction ──
        // Find the target's armor value from allUnits list.
        FixedPoint targetArmorValue = FixedPoint.Zero;
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].UnitId == target.TargetId)
            {
                targetArmorValue = allUnits[i].ArmorValue;
                break;
            }
        }
        damage = damage - targetArmorValue;
        damage = FixedPoint.Max(damage, MinDamage);

        result.DamageDealt = damage;

        // ── Step 5: Area of Effect (splash damage) ──
        if (weapon.AreaOfEffect > FixedPoint.Zero)
        {
            result.SplashTargets = new List<(int, FixedPoint)>(16);
            FixedPoint aoeSq = weapon.AreaOfEffect * weapon.AreaOfEffect;

            // Query spatial hash for all units near impact point
            _spatialQueryResults.Clear();
            spatial.QueryRadius(target.TargetPosition, weapon.AreaOfEffect, _spatialQueryResults);

            for (int i = 0; i < _spatialQueryResults.Count; i++)
            {
                int hitUnitId = _spatialQueryResults[i];

                // Don't double-count the primary target
                if (hitUnitId == target.TargetId)
                    continue;

                // Find this unit's info
                UnitCombatInfo hitUnit = default;
                bool found = false;
                for (int j = 0; j < allUnits.Count; j++)
                {
                    if (allUnits[j].UnitId == hitUnitId)
                    {
                        hitUnit = allUnits[j];
                        found = true;
                        break;
                    }
                }

                if (!found) continue;

                // Don't damage friendly units with splash (optional — can be removed
                // for friendly fire mechanics, but C&C-style games usually don't)
                // We include all units in splash for now — the tick system can filter.

                // Compute distance-based falloff: 100% at center, 50% at edge
                FixedPoint distSq = hitUnit.Position.DistanceSquaredTo(target.TargetPosition);
                if (distSq > aoeSq) continue; // Outside AoE radius

                FixedPoint dist = FixedPoint.Sqrt(distSq);
                FixedPoint normalizedDist = dist / weapon.AreaOfEffect;
                normalizedDist = FixedPoint.Clamp(normalizedDist, FixedPoint.Zero, FixedPoint.One);

                // Linear falloff: 1.0 at center → SplashEdgeFalloff at edge
                FixedPoint falloff = FixedPoint.One - (FixedPoint.One - SplashEdgeFalloff) * normalizedDist;

                // Splash damage = base weapon damage * falloff * armor modifier
                FixedPoint splashArmorMod = DefaultArmorModifier;
                if (weapon.ArmorModifiers != null &&
                    weapon.ArmorModifiers.TryGetValue(hitUnit.ArmorClass, out FixedPoint sMod))
                {
                    splashArmorMod = sMod;
                }

                FixedPoint splashDmg = weapon.Damage * falloff * splashArmorMod;
                splashDmg = splashDmg - hitUnit.ArmorValue;
                splashDmg = FixedPoint.Max(splashDmg, MinDamage);

                result.SplashTargets.Add((hitUnitId, splashDmg));
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TARGET ACQUISITION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acquires the best target for an attacker using C&C-style priority:
    ///   1. Current target if still valid and in range (prevent jitter)
    ///   2. Nearest enemy that any weapon can hit
    ///   3. Priority: threats to self > threats to allies > nearest > buildings
    /// </summary>
    /// <param name="attacker">The attacking unit.</param>
    /// <param name="weapons">The attacker's weapon loadout.</param>
    /// <param name="spatial">Spatial hash for nearby unit queries.</param>
    /// <param name="allUnits">All units in the simulation.</param>
    /// <param name="occupancy">Occupancy grid (reserved for future building queries).</param>
    /// <returns>The best target, or null if nothing is in range.</returns>
    public CombatTarget? AcquireTarget(
        AttackerInfo attacker,
        List<WeaponData> weapons,
        SpatialHash spatial,
        List<UnitCombatInfo> allUnits,
        OccupancyGrid occupancy)
    {
        if (weapons == null || weapons.Count == 0)
            return null;

        // ── Find maximum weapon range for spatial query ──
        FixedPoint maxRange = FixedPoint.Zero;
        for (int w = 0; w < weapons.Count; w++)
        {
            if (weapons[w].Range > maxRange)
                maxRange = weapons[w].Range;
        }

        if (maxRange == FixedPoint.Zero)
            return null;

        // ── Step 1: Check current target validity ──
        if (attacker.CurrentTargetId.HasValue)
        {
            int currentId = attacker.CurrentTargetId.Value;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i].UnitId == currentId &&
                    allUnits[i].PlayerId != attacker.PlayerId &&
                    allUnits[i].Health > FixedPoint.Zero &&
                    !allUnits[i].IsStealthed)
                {
                    // Build CombatTarget for current target
                    FixedPoint distSq = attacker.Position.DistanceSquaredTo(allUnits[i].Position);
                    CombatTarget currentTarget = new CombatTarget
                    {
                        TargetId = currentId,
                        TargetPosition = allUnits[i].Position,
                        DistanceSquared = distSq,
                        TargetArmor = allUnits[i].ArmorClass,
                        IsAir = allUnits[i].IsAir,
                        IsBuilding = allUnits[i].IsBuilding,
                        IsNaval = allUnits[i].IsNaval,
                        TargetPlayerId = allUnits[i].PlayerId
                    };

                    // Check if any weapon can still hit it
                    for (int w = 0; w < weapons.Count; w++)
                    {
                        if (CanAttack(weapons[w], currentTarget))
                        {
                            // Current target still valid — keep it to prevent jitter
                            return currentTarget;
                        }
                    }
                    break; // Found the unit but can't attack it — fall through
                }
            }
        }

        // ── Step 2: Query spatial hash for candidates ──
        _spatialQueryResults.Clear();
        spatial.QueryRadius(attacker.Position, maxRange, _spatialQueryResults);

        // ── Step 3: Evaluate all candidates ──
        CombatTarget? bestTarget = null;
        int bestPriority = int.MinValue;
        FixedPoint bestDistSq = FixedPoint.MaxValue;

        for (int i = 0; i < _spatialQueryResults.Count; i++)
        {
            int candidateId = _spatialQueryResults[i];

            // Find this unit's info
            UnitCombatInfo candidate = default;
            bool found = false;
            for (int j = 0; j < allUnits.Count; j++)
            {
                if (allUnits[j].UnitId == candidateId)
                {
                    candidate = allUnits[j];
                    found = true;
                    break;
                }
            }

            if (!found) continue;

            // Skip friendly units
            if (candidate.PlayerId == attacker.PlayerId) continue;

            // Skip dead or stealthed units
            if (candidate.Health <= FixedPoint.Zero) continue;
            if (candidate.IsStealthed) continue;

            // Build CombatTarget
            FixedPoint distSq = attacker.Position.DistanceSquaredTo(candidate.Position);
            CombatTarget ct = new CombatTarget
            {
                TargetId = candidateId,
                TargetPosition = candidate.Position,
                DistanceSquared = distSq,
                TargetArmor = candidate.ArmorClass,
                IsAir = candidate.IsAir,
                IsBuilding = candidate.IsBuilding,
                IsNaval = candidate.IsNaval,
                TargetPlayerId = candidate.PlayerId
            };

            // Check if ANY weapon can hit this target
            bool canHit = false;
            for (int w = 0; w < weapons.Count; w++)
            {
                if (CanAttack(weapons[w], ct))
                {
                    canHit = true;
                    break;
                }
            }

            if (!canHit) continue;

            // ── Compute priority score (C&C-style) ──
            // Higher priority = more desirable target.
            //   3 = threat attacking us (not yet tracked — reserved for future)
            //   2 = threat attacking allies (not yet tracked — reserved for future)
            //   1 = nearest non-building enemy
            //   0 = building
            int priority;
            if (candidate.IsBuilding)
            {
                priority = 0;
            }
            else
            {
                priority = 1;
                // Future: check if candidate is targeting us (priority 3)
                // or targeting our allies (priority 2).
                // For now, all non-building enemies have priority 1.
            }

            // Compare: higher priority wins. On tie, closer distance wins.
            bool isBetter = false;
            if (priority > bestPriority)
            {
                isBetter = true;
            }
            else if (priority == bestPriority && distSq < bestDistSq)
            {
                isBetter = true;
            }

            if (isBetter)
            {
                bestTarget = ct;
                bestPriority = priority;
                bestDistSq = distSq;
            }
        }

        return bestTarget;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FIRING ARC
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if a target is within a weapon's firing arc. Most units have
    /// 360° turrets (arcWidth = 2π), but fixed-weapon units (e.g., fixed
    /// artillery, bunkers) have limited arcs.
    /// </summary>
    /// <param name="attackerFacing">
    /// The attacker's current facing angle in radians [0, 2π).
    /// </param>
    /// <param name="toTarget">
    /// Direction vector from attacker to target (not necessarily normalized).
    /// </param>
    /// <param name="arcWidth">
    /// Total arc width in radians. 2π = full 360°. π = 180° frontal arc.
    /// The arc is centered on the attacker's facing direction.
    /// </param>
    /// <returns>True if the target falls within the firing arc.</returns>
    public bool IsInFiringArc(FixedPoint attackerFacing, FixedVector2 toTarget, FixedPoint arcWidth)
    {
        // Full circle arc → always in arc
        if (arcWidth >= TwoPi)
            return true;

        // Zero-length toTarget → can't determine angle
        if (toTarget.LengthSquared == FixedPoint.Zero)
            return true;

        // Compute angle to target
        FixedPoint targetAngle = MovementSimulator.Atan2(toTarget.Y, toTarget.X);

        // Compute angular difference
        FixedPoint delta = targetAngle - attackerFacing;

        // Normalize to [-π, π)
        while (delta > Pi)
            delta = delta - TwoPi;
        while (delta < -Pi)
            delta = delta + TwoPi;

        // Check if within half the arc width on either side
        FixedPoint halfArc = arcWidth * FixedPoint.Half;
        FixedPoint absDelta = FixedPoint.Abs(delta);

        return absDelta <= halfArc;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // REGISTRY INTEGRATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds an <see cref="AttackerInfo"/> using weapon data from the
    /// <see cref="UnitDataRegistry"/>. Ensures weapon ranges, damage values,
    /// and armor modifiers come from the loaded JSON data rather than
    /// hard-coded values.
    /// </summary>
    /// <param name="unitId">Runtime unit instance ID.</param>
    /// <param name="playerId">Owning player ID.</param>
    /// <param name="dataId">Unit type ID for registry lookup.</param>
    /// <param name="position">Current world position.</param>
    /// <param name="facing">Current facing angle in radians.</param>
    /// <param name="currentTargetId">Currently targeted unit, or null.</param>
    /// <param name="weaponCooldowns">Per-weapon cooldown ticks remaining.</param>
    /// <param name="registry">The unit data registry to look up weapon stats from.</param>
    /// <returns>A fully populated <see cref="AttackerInfo"/>.</returns>
    public static AttackerInfo BuildAttackerInfo(
        int unitId,
        int playerId,
        string dataId,
        FixedVector2 position,
        FixedPoint facing,
        int? currentTargetId,
        List<FixedPoint> weaponCooldowns,
        UnitDataRegistry registry)
    {
        UnitData unitData = registry.GetUnitData(dataId);
        return new AttackerInfo
        {
            UnitId = unitId,
            PlayerId = playerId,
            Position = position,
            Facing = facing,
            CurrentTargetId = currentTargetId,
            Weapons = unitData.Weapons,
            WeaponCooldowns = weaponCooldowns
        };
    }

    /// <summary>
    /// Builds a <see cref="UnitCombatInfo"/> using stats from the
    /// <see cref="UnitDataRegistry"/> and <see cref="AssetRegistry"/>.
    /// </summary>
    /// <param name="unitId">Runtime unit instance ID.</param>
    /// <param name="playerId">Owning player ID.</param>
    /// <param name="dataId">Unit type ID for registry lookup.</param>
    /// <param name="position">Current world position.</param>
    /// <param name="health">Current health.</param>
    /// <param name="isAir">Whether this is an airborne unit.</param>
    /// <param name="isBuilding">Whether this is a building.</param>
    /// <param name="isStealthed">Whether this unit is currently stealthed.</param>
    /// <param name="unitRegistry">The unit data registry for stats.</param>
    /// <param name="assetRegistry">The asset registry for collision radius.</param>
    /// <returns>A fully populated <see cref="UnitCombatInfo"/>.</returns>
    public static UnitCombatInfo BuildCombatInfo(
        int unitId,
        int playerId,
        string dataId,
        FixedVector2 position,
        FixedPoint health,
        bool isAir,
        bool isBuilding,
        bool isStealthed,
        UnitDataRegistry unitRegistry,
        AssetRegistry assetRegistry)
    {
        UnitData unitData = unitRegistry.GetUnitData(dataId);
        return new UnitCombatInfo
        {
            UnitId = unitId,
            PlayerId = playerId,
            Position = position,
            Health = health,
            MaxHealth = unitData.MaxHealth,
            ArmorValue = unitData.ArmorValue,
            ArmorClass = unitData.ArmorClass,
            IsAir = isAir,
            IsBuilding = isBuilding,
            IsStealthed = isStealthed,
            Radius = assetRegistry.GetCollisionRadius(dataId)
        };
    }
}
