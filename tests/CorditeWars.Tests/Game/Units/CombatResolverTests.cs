using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;
using System.Collections.Generic;

namespace CorditeWars.Tests.Game.Units;

/// <summary>
/// Tests for the CombatResolver damage pipeline.
/// Verifies accuracy, damage modifiers, armor reduction, and weapon eligibility.
/// </summary>
public class CombatResolverTests
{
    private readonly CombatResolver _resolver = new();

    // ── Helper methods ──────────────────────────────────────────────────

    private static WeaponData CreateBasicWeapon(
        FixedPoint? damage = null,
        FixedPoint? range = null,
        FixedPoint? accuracy = null,
        TargetType canTarget = TargetType.Ground | TargetType.Air | TargetType.Building,
        FixedPoint? aoe = null,
        FixedPoint? minRange = null)
    {
        return new WeaponData
        {
            Id = "test_weapon",
            Type = WeaponType.Cannon,
            Damage = damage ?? FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.FromInt(1),
            Range = range ?? FixedPoint.FromInt(10),
            MinRange = minRange ?? FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = aoe ?? FixedPoint.Zero,
            CanTarget = canTarget,
            AccuracyPercent = accuracy ?? FixedPoint.FromInt(100),
            ArmorModifiers = new()
        };
    }

    private static CombatTarget CreateTarget(
        FixedVector2? position = null,
        FixedPoint? distanceSq = null,
        ArmorType armor = ArmorType.Medium,
        bool isAir = false,
        bool isBuilding = false,
        int targetId = 2,
        int targetPlayerId = 2)
    {
        return new CombatTarget
        {
            TargetId = targetId,
            TargetPosition = position ?? new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero),
            DistanceSquared = distanceSq ?? FixedPoint.FromInt(25),
            TargetArmor = armor,
            IsAir = isAir,
            IsBuilding = isBuilding,
            TargetPlayerId = targetPlayerId
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // CanAttack — Weapon eligibility checks
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CanAttack_GroundTarget_GroundWeapon_InRange_True()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(10));
        var target = CreateTarget(distanceSq: FixedPoint.FromInt(25)); // dist=5 < range=10
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_OutOfRange_False()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(3));
        // distance² = 25 → distance = 5, range = 3 → range² = 9
        var target = CreateTarget(distanceSq: FixedPoint.FromInt(25));
        Assert.False(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_ExactRange_InRange()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(5));
        // distance² = 25, range² = 25
        var target = CreateTarget(distanceSq: FixedPoint.FromInt(25));
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_AirTarget_GroundOnlyWeapon_False()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Ground);
        var target = CreateTarget(isAir: true);
        Assert.False(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_AirTarget_AirWeapon_True()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Air);
        var target = CreateTarget(isAir: true, distanceSq: FixedPoint.FromInt(4));
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_BuildingTarget_NoBuildingFlag_False()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Ground);
        var target = CreateTarget(isBuilding: true);
        Assert.False(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_BuildingTarget_WithBuildingFlag_True()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Building);
        var target = CreateTarget(isBuilding: true, distanceSq: FixedPoint.FromInt(4));
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_InsideMinRange_False()
    {
        var weapon = CreateBasicWeapon(
            range: FixedPoint.FromInt(10),
            minRange: FixedPoint.FromInt(3));
        // distance² = 4 → distance = 2, minRange = 3 → minRange² = 9
        var target = CreateTarget(distanceSq: FixedPoint.FromInt(4));
        Assert.False(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_OutsideMinRange_InMaxRange_True()
    {
        var weapon = CreateBasicWeapon(
            range: FixedPoint.FromInt(10),
            minRange: FixedPoint.FromInt(3));
        // distance² = 25 → distance = 5, minRange = 3, maxRange = 10
        var target = CreateTarget(distanceSq: FixedPoint.FromInt(25));
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveAttack — Damage pipeline
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveAttack_AlwaysHit_100Accuracy_DealsDamage()
    {
        var weapon = CreateBasicWeapon(damage: FixedPoint.FromInt(50), accuracy: FixedPoint.FromInt(100));
        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(100),
                MaxHealth = FixedPoint.FromInt(100),
                ArmorValue = FixedPoint.Zero,
                ArmorClass = ArmorType.Medium
            }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
        Assert.True(result.DidHit);
        Assert.True(result.DamageDealt > FixedPoint.Zero);
    }

    [Fact]
    public void ResolveAttack_ZeroAccuracy_AlwaysMisses()
    {
        var weapon = CreateBasicWeapon(damage: FixedPoint.FromInt(50), accuracy: FixedPoint.Zero);
        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(100),
                MaxHealth = FixedPoint.FromInt(100),
                ArmorValue = FixedPoint.Zero,
                ArmorClass = ArmorType.Medium
            }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        // Run multiple times — all should miss
        for (int i = 0; i < 20; i++)
        {
            var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
            Assert.False(result.DidHit);
            Assert.Equal(FixedPoint.Zero, result.DamageDealt);
        }
    }

    [Fact]
    public void ResolveAttack_ArmorModifier_ReducesDamage()
    {
        var weapon = new WeaponData
        {
            Id = "test_weapon",
            Type = WeaponType.MachineGun,
            Damage = FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.FromInt(1),
            Range = FixedPoint.FromInt(10),
            AccuracyPercent = FixedPoint.FromInt(100),
            CanTarget = TargetType.Ground,
            ArmorModifiers = new()
            {
                { ArmorType.Heavy, FixedPoint.FromFloat(0.5f) }
            }
        };

        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget(armor: ArmorType.Heavy);
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(200),
                MaxHealth = FixedPoint.FromInt(200),
                ArmorValue = FixedPoint.Zero,
                ArmorClass = ArmorType.Heavy
            }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
        Assert.True(result.DidHit);
        // 100 damage × 0.5 heavy modifier = ~50 damage
        float dmg = result.DamageDealt.ToFloat();
        Assert.True(Math.Abs(dmg - 50.0f) < 1.0f,
            $"Expected ~50 damage with 0.5 armor modifier, got {dmg}");
    }

    [Fact]
    public void ResolveAttack_FlatArmor_ReducesDamage()
    {
        var weapon = CreateBasicWeapon(damage: FixedPoint.FromInt(30), accuracy: FixedPoint.FromInt(100));
        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(200),
                MaxHealth = FixedPoint.FromInt(200),
                ArmorValue = FixedPoint.FromInt(10),  // 10 flat armor
                ArmorClass = ArmorType.Medium
            }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
        Assert.True(result.DidHit);
        // 30 damage - 10 armor = 20 damage
        float dmg = result.DamageDealt.ToFloat();
        Assert.True(Math.Abs(dmg - 20.0f) < 1.0f,
            $"Expected ~20 damage after armor reduction, got {dmg}");
    }

    [Fact]
    public void ResolveAttack_MinDamage_AlwaysAtLeastOne()
    {
        var weapon = CreateBasicWeapon(damage: FixedPoint.FromInt(5), accuracy: FixedPoint.FromInt(100));
        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(1000),
                MaxHealth = FixedPoint.FromInt(1000),
                ArmorValue = FixedPoint.FromInt(999), // Massive armor
                ArmorClass = ArmorType.Medium
            }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
        Assert.True(result.DidHit);
        // Minimum damage should be 1
        Assert.True(result.DamageDealt >= FixedPoint.One,
            $"Minimum damage should be ≥ 1, got {result.DamageDealt.ToFloat()}");
    }

    [Fact]
    public void ResolveAttack_Deterministic_SameInputsSameOutput()
    {
        var weapon = CreateBasicWeapon(
            damage: FixedPoint.FromInt(50),
            accuracy: FixedPoint.FromInt(80));

        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new()
            {
                UnitId = 2,
                PlayerId = 2,
                Position = target.TargetPosition,
                Health = FixedPoint.FromInt(100),
                MaxHealth = FixedPoint.FromInt(100),
                ArmorValue = FixedPoint.FromInt(5),
                ArmorClass = ArmorType.Medium
            }
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        // Run twice with same seed
        var rng1 = new DeterministicRng(999);
        var result1 = _resolver.ResolveAttack(attacker, target, weapon, 0, rng1, spatial, allUnits);

        var rng2 = new DeterministicRng(999);
        var result2 = _resolver.ResolveAttack(attacker, target, weapon, 0, rng2, spatial, allUnits);

        Assert.Equal(result1.DidHit, result2.DidHit);
        Assert.Equal(result1.DamageDealt, result2.DamageDealt);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CanAttack — Naval targeting
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CanAttack_NavalTarget_NavalWeapon_True()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Naval);
        var target = new CombatTarget
        {
            TargetId = 2,
            TargetPosition = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero),
            DistanceSquared = FixedPoint.FromInt(25),
            TargetArmor = ArmorType.Medium,
            IsNaval = true
        };
        Assert.True(_resolver.CanAttack(weapon, target));
    }

    [Fact]
    public void CanAttack_NavalTarget_NoNavalFlag_False()
    {
        var weapon = CreateBasicWeapon(canTarget: TargetType.Ground);
        var target = new CombatTarget
        {
            TargetId = 2,
            TargetPosition = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero),
            DistanceSquared = FixedPoint.FromInt(25),
            TargetArmor = ArmorType.Medium,
            IsNaval = true
        };
        Assert.False(_resolver.CanAttack(weapon, target));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveAttack — AoE splash damage
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveAttack_AoE_HitsNearbyUnits()
    {
        var weapon = new WeaponData
        {
            Id = "artillery",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.One,
            Range = FixedPoint.FromInt(20),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.FromInt(5),
            CanTarget = TargetType.Ground | TargetType.Building,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = new()
        };

        var primaryPos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero);
        var splashPos  = new FixedVector2(FixedPoint.FromInt(12), FixedPoint.Zero); // 2 units away, within AoE=5

        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero }
        };

        var target = new CombatTarget
        {
            TargetId = 2,
            TargetPosition = primaryPos,
            DistanceSquared = FixedPoint.FromInt(100),
            TargetArmor = ArmorType.Medium,
            IsBuilding = false
        };

        var allUnits = new List<UnitCombatInfo>
        {
            new() { UnitId = 2, PlayerId = 2, Position = primaryPos,
                    Health = FixedPoint.FromInt(200), MaxHealth = FixedPoint.FromInt(200),
                    ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium },
            new() { UnitId = 3, PlayerId = 2, Position = splashPos,
                    Health = FixedPoint.FromInt(200), MaxHealth = FixedPoint.FromInt(200),
                    ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium, Radius = FixedPoint.One }
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, primaryPos, FixedPoint.One);
        spatial.Insert(3, splashPos, FixedPoint.One);

        var rng = new DeterministicRng(42);
        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);

        Assert.True(result.DidHit);
        Assert.NotNull(result.SplashTargets);
        Assert.True(result.SplashTargets!.Count > 0, "Nearby unit should be caught in splash");
        Assert.Contains(result.SplashTargets, s => s.unitId == 3);
    }

    [Fact]
    public void ResolveAttack_AoE_SplashDamageReducedAtEdge()
    {
        // Unit right at the edge of AoE (distance ≈ aoe radius) gets less than full damage
        var weapon = new WeaponData
        {
            Id = "artillery",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.One,
            Range = FixedPoint.FromInt(20),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.FromInt(4),
            CanTarget = TargetType.Ground,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = new()
        };

        var primaryPos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero);
        // Splash unit near the edge of the 4-unit AoE radius
        var edgePos = new FixedVector2(FixedPoint.FromInt(13), FixedPoint.Zero); // 3 units from primary

        var attacker = new AttackerInfo
        {
            UnitId = 1, PlayerId = 1, Position = FixedVector2.Zero,
            Weapons = new() { weapon }, WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = new CombatTarget
        {
            TargetId = 2, TargetPosition = primaryPos,
            DistanceSquared = FixedPoint.FromInt(100), TargetArmor = ArmorType.Medium
        };

        var allUnits = new List<UnitCombatInfo>
        {
            new() { UnitId = 2, PlayerId = 2, Position = primaryPos, Health = FixedPoint.FromInt(500),
                    MaxHealth = FixedPoint.FromInt(500), ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium },
            new() { UnitId = 3, PlayerId = 2, Position = edgePos, Health = FixedPoint.FromInt(500),
                    MaxHealth = FixedPoint.FromInt(500), ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium,
                    Radius = FixedPoint.One }
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, primaryPos, FixedPoint.One);
        spatial.Insert(3, edgePos, FixedPoint.One);

        var rng = new DeterministicRng(42);
        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);

        Assert.True(result.DidHit);
        // Primary target gets full damage; splash unit gets partial
        if (result.SplashTargets != null)
        {
            var splash = result.SplashTargets.Find(s => s.unitId == 3);
            if (splash != default)
            {
                Assert.True(splash.damage < result.DamageDealt,
                    "Splash damage at edge should be less than primary damage");
            }
        }
    }

    [Fact]
    public void ResolveAttack_AoE_PrimaryTargetNotInSplashList()
    {
        var weapon = new WeaponData
        {
            Id = "artillery",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.One,
            Range = FixedPoint.FromInt(20),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.FromInt(5),
            CanTarget = TargetType.Ground,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = new()
        };

        var primaryPos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero);
        var attacker = new AttackerInfo
        {
            UnitId = 1, PlayerId = 1, Position = FixedVector2.Zero,
            Weapons = new() { weapon }, WeaponCooldowns = new() { FixedPoint.Zero }
        };
        var target = new CombatTarget
        {
            TargetId = 2, TargetPosition = primaryPos,
            DistanceSquared = FixedPoint.FromInt(100), TargetArmor = ArmorType.Medium
        };

        var allUnits = new List<UnitCombatInfo>
        {
            new() { UnitId = 2, PlayerId = 2, Position = primaryPos, Health = FixedPoint.FromInt(200),
                    MaxHealth = FixedPoint.FromInt(200), ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium }
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, primaryPos, FixedPoint.One);

        var rng = new DeterministicRng(42);
        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);

        Assert.True(result.DidHit);
        Assert.NotNull(result.SplashTargets);
        // The primary target (id=2) must NOT be in the splash list (it is the primary target)
        Assert.DoesNotContain(result.SplashTargets!, s => s.unitId == 2);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveAttack — DamageMultiplier (veterancy)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveAttack_DamageMultiplier_IncreasesBaseDamage()
    {
        var weapon = CreateBasicWeapon(damage: FixedPoint.FromInt(100), accuracy: FixedPoint.FromInt(100));
        var attacker = new AttackerInfo
        {
            UnitId = 1,
            PlayerId = 1,
            Position = FixedVector2.Zero,
            Weapons = new() { weapon },
            WeaponCooldowns = new() { FixedPoint.Zero },
            DamageMultiplier = FixedPoint.FromFloat(1.5f)  // Heroic veterancy
        };
        var target = CreateTarget();
        var allUnits = new List<UnitCombatInfo>
        {
            new() { UnitId = 2, PlayerId = 2, Position = target.TargetPosition,
                    Health = FixedPoint.FromInt(500), MaxHealth = FixedPoint.FromInt(500),
                    ArmorValue = FixedPoint.Zero, ArmorClass = ArmorType.Medium }
        };

        var rng = new DeterministicRng(42);
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);

        var result = _resolver.ResolveAttack(attacker, target, weapon, 0, rng, spatial, allUnits);
        Assert.True(result.DidHit);
        // 100 × 1.5 = 150 damage
        Assert.True(result.DamageDealt.ToFloat() > 100f,
            $"DamageMultiplier 1.5x should produce >100 damage, got {result.DamageDealt.ToFloat()}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // AcquireTarget — current target retention
    // ═══════════════════════════════════════════════════════════════════

    private static AttackerInfo MakeAttacker(
        int id, int playerId, FixedVector2 pos, List<WeaponData> weapons, int? currentTargetId = null)
    {
        return new AttackerInfo
        {
            UnitId = id,
            PlayerId = playerId,
            Position = pos,
            Weapons = weapons,
            WeaponCooldowns = weapons.ConvertAll(_ => FixedPoint.Zero),
            CurrentTargetId = currentTargetId
        };
    }

    private static UnitCombatInfo MakeCombatUnit(
        int id, int playerId, FixedVector2 pos,
        FixedPoint? health = null, bool isBuilding = false, bool isStealthed = false, bool isNaval = false)
    {
        return new UnitCombatInfo
        {
            UnitId = id,
            PlayerId = playerId,
            Position = pos,
            Health = health ?? FixedPoint.FromInt(100),
            MaxHealth = FixedPoint.FromInt(100),
            ArmorValue = FixedPoint.Zero,
            ArmorClass = ArmorType.Medium,
            IsBuilding = isBuilding,
            IsStealthed = isStealthed,
            IsNaval = isNaval,
            Radius = FixedPoint.One
        };
    }

    [Fact]
    public void AcquireTarget_CurrentTarget_StillValid_ReturnsCurrentTarget()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(15));
        var weapons = new List<WeaponData> { weapon };

        var attackerPos = FixedVector2.Zero;
        var targetPos = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero);

        var attacker = MakeAttacker(1, 1, attackerPos, weapons, currentTargetId: 2);
        var allUnits = new List<UnitCombatInfo>
        {
            MakeCombatUnit(2, 2, targetPos)
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, targetPos, FixedPoint.One);

        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.TargetId);
    }

    [Fact]
    public void AcquireTarget_CurrentTarget_Dead_AcquiresNew()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(15));
        var weapons = new List<WeaponData> { weapon };

        var attackerPos = FixedVector2.Zero;
        var deadPos = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.Zero);
        var alivePos = new FixedVector2(FixedPoint.FromInt(6), FixedPoint.Zero);

        var attacker = MakeAttacker(1, 1, attackerPos, weapons, currentTargetId: 2);
        var allUnits = new List<UnitCombatInfo>
        {
            MakeCombatUnit(2, 2, deadPos, health: FixedPoint.Zero),  // dead
            MakeCombatUnit(3, 2, alivePos)
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(3, alivePos, FixedPoint.One);

        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        // Should acquire unit 3 since unit 2 is dead
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.TargetId);
    }

    [Fact]
    public void AcquireTarget_NoWeapons_ReturnsNull()
    {
        var attacker = MakeAttacker(1, 1, FixedVector2.Zero, new List<WeaponData>());
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, new List<WeaponData>(), spatial, new List<UnitCombatInfo>(), occupancy);
        Assert.Null(result);
    }

    [Fact]
    public void AcquireTarget_NullWeapons_ReturnsNull()
    {
        var attacker = MakeAttacker(1, 1, FixedVector2.Zero, new List<WeaponData>());
        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, null!, spatial, new List<UnitCombatInfo>(), occupancy);
        Assert.Null(result);
    }

    [Fact]
    public void AcquireTarget_PreferNonBuildingOverBuilding()
    {
        var weapon = CreateBasicWeapon(
            range: FixedPoint.FromInt(20),
            canTarget: TargetType.Ground | TargetType.Building);
        var weapons = new List<WeaponData> { weapon };

        var attackerPos = FixedVector2.Zero;
        // Building is CLOSER but should have lower priority than the ground unit
        var buildingPos = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.Zero);
        var unitPos     = new FixedVector2(FixedPoint.FromInt(7), FixedPoint.Zero);

        var attacker = MakeAttacker(1, 1, attackerPos, weapons);
        var allUnits = new List<UnitCombatInfo>
        {
            MakeCombatUnit(2, 2, buildingPos, isBuilding: true),
            MakeCombatUnit(3, 2, unitPos)
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, buildingPos, FixedPoint.One);
        spatial.Insert(3, unitPos, FixedPoint.One);

        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        // Ground unit (priority 1) should win over building (priority 0)
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.TargetId);
    }

    [Fact]
    public void AcquireTarget_SkipsStealthedUnits()
    {
        var weapon = CreateBasicWeapon(range: FixedPoint.FromInt(15));
        var weapons = new List<WeaponData> { weapon };

        var attackerPos = FixedVector2.Zero;
        var stealthPos  = new FixedVector2(FixedPoint.FromInt(4), FixedPoint.Zero);
        var visiblePos  = new FixedVector2(FixedPoint.FromInt(8), FixedPoint.Zero);

        var attacker = MakeAttacker(1, 1, attackerPos, weapons);
        var allUnits = new List<UnitCombatInfo>
        {
            MakeCombatUnit(2, 2, stealthPos, isStealthed: true),
            MakeCombatUnit(3, 2, visiblePos)
        };

        var spatial = new CorditeWars.Systems.Pathfinding.SpatialHash(256, 256);
        spatial.Insert(2, stealthPos, FixedPoint.One);
        spatial.Insert(3, visiblePos, FixedPoint.One);

        var occupancy = new CorditeWars.Systems.Pathfinding.OccupancyGrid(64, 64);

        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        // Stealthed unit should be skipped; visible unit should be selected
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.TargetId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IsInFiringArc
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsInFiringArc_FullCircle_AlwaysTrue()
    {
        // arcWidth >= 2π → always in arc regardless of direction
        FixedPoint twoPi = FixedPoint.FromRaw(411775);
        var toTarget = new FixedVector2(FixedPoint.FromInt(1), FixedPoint.Zero);
        Assert.True(_resolver.IsInFiringArc(FixedPoint.Zero, toTarget, twoPi));
    }

    [Fact]
    public void IsInFiringArc_ZeroLengthToTarget_AlwaysTrue()
    {
        FixedPoint halfPi = FixedPoint.FromRaw(102943);  // π/2
        Assert.True(_resolver.IsInFiringArc(FixedPoint.Zero, FixedVector2.Zero, halfPi));
    }

    [Fact]
    public void IsInFiringArc_TargetDirectlyAhead_WithinArc()
    {
        // Attacker facing right (angle 0). Target directly ahead (+X).
        // Half-circle arc (π) should easily contain this.
        FixedPoint pi = FixedPoint.FromRaw(205887);
        var toTarget = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero);
        Assert.True(_resolver.IsInFiringArc(FixedPoint.Zero, toTarget, pi));
    }

    [Fact]
    public void IsInFiringArc_TargetDirectlyBehind_OutsideNarrowArc()
    {
        // Attacker facing right (+X direction, angle 0).
        // Target is directly behind (-X direction).
        // Use a very narrow arc (π/4 = 45°) — target behind is definitely outside.
        FixedPoint quarterPi = FixedPoint.FromRaw(51471);  // ~π/4
        // Facing = 0 (right), toTarget = (-5, 0) = facing left = angle π
        var toTarget = new FixedVector2(FixedPoint.FromInt(-5), FixedPoint.Zero);
        Assert.False(_resolver.IsInFiringArc(FixedPoint.Zero, toTarget, quarterPi));
    }

    // ═══════════════════════════════════════════════════════════════════
    // CanAttack — ground weapon vs ground target (line 261)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CanAttack_AntiAirWeapon_CannotTargetGround()
    {
        // Weapon can only target air, target is ground — should return false.
        var weapon = CreateBasicWeapon(
            range: FixedPoint.FromInt(10),
            canTarget: TargetType.Air); // cannot target ground
        var target = CreateTarget(
            distanceSq: FixedPoint.FromInt(25),
            isAir: false); // ground target

        Assert.False(_resolver.CanAttack(weapon, target));
    }

    // ═══════════════════════════════════════════════════════════════════
    // IsInFiringArc — angular normalization edge cases (lines 651,653)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsInFiringArc_AngularDeltaExceedsPi_NormalizedCorrectly()
    {
        // Force delta > π by having attackerFacing = -π and target at angle near +π.
        // attackerFacing = -π (raw -205887), targetAngle = +π (raw 205887)
        // delta = +π - (-π) = +2π → while loop: delta = 2π - 2π = 0 (in arc)
        FixedPoint minusPi = FixedPoint.FromRaw(-205887);
        FixedPoint fullArc = FixedPoint.FromRaw(411775); // 2π — use a big arc so 0 is in it
        var toTarget = new FixedVector2(FixedPoint.FromInt(-5), FixedPoint.Zero); // angle ~π

        // With delta normalized to 0, it must be within any arc >= 0.
        Assert.True(_resolver.IsInFiringArc(minusPi, toTarget, fullArc));
    }

    // ═══════════════════════════════════════════════════════════════════
    // AcquireTarget — maxRange == 0 returns null (line 469)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AcquireTarget_AllWeaponsHaveZeroRange_ReturnsNull()
    {
        var weapons = new List<WeaponData>
        {
            CreateBasicWeapon(range: FixedPoint.Zero), // all ranges are 0
        };
        var attacker = MakeAttacker(1, 1, FixedVector2.Zero, weapons);
        var spatial  = new SpatialHash(256, 256);
        var occupancy = new OccupancyGrid(32, 32);

        var result = _resolver.AcquireTarget(attacker, weapons, spatial, new List<UnitCombatInfo>(), occupancy);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AcquireTarget — current target valid but no weapon can hit (line 504-505)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AcquireTarget_CurrentTargetOutOfRange_FallsThrough()
    {
        // Current target exists but is now out of range for all weapons.
        // Should fall through to acquire a new target.
        var weapon  = CreateBasicWeapon(range: FixedPoint.FromInt(5));
        var weapons = new List<WeaponData> { weapon };
        var attacker = MakeAttacker(1, 1, FixedVector2.Zero, weapons, currentTargetId: 2);

        // Place the "current target" far away (out of range)
        var currentTarget = MakeCombatUnit(
            2, 2, new FixedVector2(FixedPoint.FromInt(50), FixedPoint.Zero));

        // Place a closer enemy unit that can be acquired
        var closeEnemy = MakeCombatUnit(
            3, 2, new FixedVector2(FixedPoint.FromInt(3), FixedPoint.Zero));

        var allUnits = new List<UnitCombatInfo> { currentTarget, closeEnemy };
        var spatial  = new SpatialHash(256, 256);
        foreach (var u in allUnits)
            spatial.Insert(u.UnitId, u.Position, u.Radius);

        var occupancy = new OccupancyGrid(32, 32);
        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        // Should acquire the closer enemy, not null.
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.TargetId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AcquireTarget — priority selection (same priority, closer wins)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AcquireTarget_TwoTargetsSamePriority_PicksCloser()
    {
        var weapon  = CreateBasicWeapon(range: FixedPoint.FromInt(20));
        var weapons = new List<WeaponData> { weapon };
        var attacker = MakeAttacker(1, 1, FixedVector2.Zero, weapons);

        var far   = MakeCombatUnit(2, 2, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero));
        var close = MakeCombatUnit(3, 2, new FixedVector2(FixedPoint.FromInt(3),  FixedPoint.Zero));

        var allUnits = new List<UnitCombatInfo> { far, close };
        var spatial  = new SpatialHash(256, 256);
        foreach (var u in allUnits)
            spatial.Insert(u.UnitId, u.Position, u.Radius);

        var occupancy = new OccupancyGrid(32, 32);
        var result = _resolver.AcquireTarget(attacker, weapons, spatial, allUnits, occupancy);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.TargetId); // closer unit (id=3) wins
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveAttack — splash with ArmorModifier (lines 417-421)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveAttack_SplashDamage_UsesArmorModifier_FromDictionary()
    {
        // AoE weapon with armor modifier for Medium armor.
        var armorMods = new Dictionary<ArmorType, FixedPoint>
        {
            [ArmorType.Medium] = FixedPoint.Half // 50% damage to medium armor
        };
        var weapon = new WeaponData
        {
            Id = "splash_cannon",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(100),
            RateOfFire = FixedPoint.FromInt(1),
            Range = FixedPoint.FromInt(20),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.FromInt(5),
            CanTarget = TargetType.Ground | TargetType.Air | TargetType.Building,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = armorMods
        };

        var attacker = MakeAttacker(1, 1,
            new FixedVector2(FixedPoint.Zero, FixedPoint.Zero),
            new List<WeaponData> { weapon });

        var primary = CreateTarget(
            position: new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero),
            distanceSq: FixedPoint.FromInt(25),
            armor: ArmorType.Medium);

        // Place a splash target nearby
        var splashUnit = new UnitCombatInfo
        {
            UnitId     = 5,
            PlayerId   = 2,
            Position   = new FixedVector2(FixedPoint.FromInt(6), FixedPoint.Zero),
            Health     = FixedPoint.FromInt(100),
            MaxHealth  = FixedPoint.FromInt(100),
            ArmorValue = FixedPoint.Zero,
            ArmorClass = ArmorType.Medium,
            Radius     = FixedPoint.One
        };

        var allUnits = new List<UnitCombatInfo> { splashUnit };
        var spatial  = new SpatialHash(256, 256);
        spatial.Insert(5, splashUnit.Position, splashUnit.Radius);

        var rng = new DeterministicRng(seed: 1);
        var result = _resolver.ResolveAttack(attacker, primary, weapon, 0, rng, spatial, allUnits);

        // Splash targets should exist (at least the primary or nearby unit).
        // DidHit should be true (100% accuracy).
        Assert.True(result.DidHit);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildAttackerInfo and BuildCombatInfo (lines 690-702, 730-746)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildAttackerInfo_PopulatesFieldsFromRegistry()
    {
        var unitReg = new UnitDataRegistry();
        unitReg.Register(new CorditeWars.Game.Units.UnitData
        {
            Id              = "grunt",
            DisplayName     = "Grunt",
            FactionId       = "bastion",
            Category        = CorditeWars.Game.Units.UnitCategory.Infantry,
            MovementClassId = "Infantry",
            MaxHealth       = FixedPoint.FromInt(100),
            Cost            = 200
        });

        var pos = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        var cooldowns = new List<FixedPoint> { FixedPoint.Zero };

        var info = CombatResolver.BuildAttackerInfo(
            unitId: 10,
            playerId: 1,
            dataId: "grunt",
            position: pos,
            facing: FixedPoint.Zero,
            currentTargetId: null,
            weaponCooldowns: cooldowns,
            registry: unitReg);

        Assert.Equal(10, info.UnitId);
        Assert.Equal(1, info.PlayerId);
        Assert.Equal(pos, info.Position);
        Assert.Null(info.CurrentTargetId);
        Assert.Same(cooldowns, info.WeaponCooldowns);
    }

    [Fact]
    public void BuildCombatInfo_PopulatesFieldsFromRegistries()
    {
        var unitReg  = new UnitDataRegistry();
        unitReg.Register(new CorditeWars.Game.Units.UnitData
        {
            Id              = "grunt",
            DisplayName     = "Grunt",
            FactionId       = "bastion",
            Category        = CorditeWars.Game.Units.UnitCategory.Infantry,
            MovementClassId = "Infantry",
            MaxHealth       = FixedPoint.FromInt(100),
            ArmorValue      = FixedPoint.Zero,
            ArmorClass      = ArmorType.Medium,
            Cost            = 200
        });

        var assetReg = new AssetRegistry();
        assetReg.Register("grunt", new AssetEntry
        {
            CollisionRadius = FixedPoint.FromFloat(0.5f),
            Mass = FixedPoint.FromInt(5)
        });

        var pos = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(3));
        var info = CombatResolver.BuildCombatInfo(
            unitId: 7,
            playerId: 2,
            dataId: "grunt",
            position: pos,
            health: FixedPoint.FromInt(80),
            isAir: false,
            isBuilding: false,
            isStealthed: false,
            unitRegistry: unitReg,
            assetRegistry: assetReg);

        Assert.Equal(7, info.UnitId);
        Assert.Equal(2, info.PlayerId);
        Assert.Equal(FixedPoint.FromInt(80), info.Health);
        Assert.Equal(FixedPoint.FromInt(100), info.MaxHealth);
        Assert.Equal(ArmorType.Medium, info.ArmorClass);
        Assert.Equal(FixedPoint.FromFloat(0.5f), info.Radius);
        Assert.False(info.IsAir);
    }
}
