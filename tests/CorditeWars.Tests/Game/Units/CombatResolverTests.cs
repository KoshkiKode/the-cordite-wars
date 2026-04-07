using CorditeWars.Core;
using CorditeWars.Game.Units;

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
}
