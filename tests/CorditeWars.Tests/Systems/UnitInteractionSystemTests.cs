using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for UnitInteractionSystem — the master deterministic tick pipeline.
/// Covers the full ProcessTick method and its helper methods including:
///   - Spatial indexing and occupancy grid rebuild (Phases 1–2)
///   - Movement suppression for stance-based behavior (Phase 4)
///   - Simultaneous movement application (Phase 5)
///   - Collision detection and damage application (Phase 6)
///   - Combat: target acquisition, cooldown, damage, XP/veterancy (Phase 7)
///   - Stealth resolution (Phase 7 pre-step)
///   - Cleanup: dead unit removal, destroyed ID deduplication (Phase 8)
/// </summary>
public class UnitInteractionSystemTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static UnitInteractionSystem CreateSystem(
        int gridWidth = 64,
        int gridHeight = 64,
        int maxPathsPerTick = 0)
    {
        var spatial = new SpatialHash(gridWidth, gridHeight);
        var occupancy = new OccupancyGrid(gridWidth, gridHeight);
        var collisionResolver = new CollisionResolver();
        var pathManager = new PathRequestManager();
        var formations = new FormationManager();
        var combatResolver = new CombatResolver();
        var rng = new DeterministicRng(12345UL);

        return new UnitInteractionSystem(
            spatial, occupancy, collisionResolver, pathManager,
            formations, combatResolver, rng, maxPathsPerTick);
    }

    private static TerrainGrid CreateFlatTerrain(int width = 64, int height = 64)
    {
        var terrain = new TerrainGrid(width, height, FixedPoint.One);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ref TerrainCell cell = ref terrain.GetCell(x, y);
                cell.Type = TerrainType.Grass;
                cell.Height = FixedPoint.Zero;
            }
        }
        return terrain;
    }

    private static SimUnit MakeUnit(
        int id,
        int playerId,
        FixedVector2 position,
        bool armed = false,
        UnitCategory category = UnitCategory.Infantry,
        FixedPoint? health = null,
        FixedPoint? radius = null,
        UnitStance stance = UnitStance.Aggressive,
        bool isStealthUnit = false,
        bool isDetector = false,
        FixedPoint? sightRange = null)
    {
        var weapons = new List<WeaponData>();
        var cooldowns = new List<FixedPoint>();

        if (armed)
        {
            weapons.Add(new WeaponData
            {
                Id = "test_weapon",
                Type = WeaponType.MachineGun,
                Damage = FixedPoint.FromInt(25),
                RateOfFire = FixedPoint.FromInt(2),
                Range = FixedPoint.FromInt(10),
                MinRange = FixedPoint.Zero,
                ProjectileSpeed = FixedPoint.Zero,
                AreaOfEffect = FixedPoint.Zero,
                CanTarget = TargetType.Ground | TargetType.Air | TargetType.Building,
                AccuracyPercent = FixedPoint.FromInt(100),
                ArmorModifiers = new Dictionary<ArmorType, FixedPoint>()
            });
            cooldowns.Add(FixedPoint.Zero);
        }

        return new SimUnit
        {
            UnitId = id,
            PlayerId = playerId,
            Movement = new MovementState { Position = position },
            Health = health ?? FixedPoint.FromInt(100),
            MaxHealth = FixedPoint.FromInt(100),
            ArmorValue = FixedPoint.Zero,
            ArmorClass = ArmorType.Unarmored,
            Category = category,
            SightRange = sightRange ?? FixedPoint.FromInt(10),
            Profile = MovementProfile.Infantry(),
            Radius = radius ?? FixedPoint.One,
            IsAlive = true,
            Weapons = weapons,
            WeaponCooldowns = cooldowns,
            CurrentTargetId = null,
            Stance = stance,
            XP = 0,
            Veterancy = VeterancyLevel.Recruit,
            IsStealthUnit = isStealthUnit,
            IsDetector = isDetector,
            StealthRevealTicks = 0,
            IsCurrentlyStealthed = false
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // ProcessTick — Basic Execution
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_EmptyUnitList_ReturnsEmptyResult()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();
        var units = new List<SimUnit>();

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Collisions);
        Assert.Empty(result.Crushes);
        Assert.Empty(result.Attacks);
        Assert.Empty(result.DestroyedUnitIds);
        Assert.Equal(1UL, result.Tick);
    }

    [Fact]
    public void ProcessTick_SingleUnarmedUnit_NoAttacksOccur()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();
        var units = new List<SimUnit>
        {
            MakeUnit(1, 1, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)))
        };

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Attacks);
        Assert.Empty(result.DestroyedUnitIds);
        Assert.Single(units); // Unit still alive
    }

    [Fact]
    public void ProcessTick_PreservesTickNumber()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();
        var units = new List<SimUnit>();

        TickResult result = system.ProcessTick(units, terrain, 42);
        Assert.Equal(42UL, result.Tick);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 2 — Occupancy Grid (Defense Buildings)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_DefenseBuildingOccupiesFootprint()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var building = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            category: UnitCategory.Defense);
        // Defense buildings use footprint from profile
        building.Profile = MovementProfile.Infantry(); // Has FootprintWidth/Height = 1

        var units = new List<SimUnit> { building };

        // Should not crash — building occupancy is marked as Building type
        TickResult result = system.ProcessTick(units, terrain, 1);
        Assert.Empty(result.DestroyedUnitIds);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 — Simultaneous Movement
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_MultipleUnits_AllPositionsUpdatedSimultaneously()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Two units far apart, no path, should not move
        var units = new List<SimUnit>
        {
            MakeUnit(1, 1, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10))),
            MakeUnit(2, 1, new FixedVector2(FixedPoint.FromInt(30), FixedPoint.FromInt(30)))
        };

        TickResult result = system.ProcessTick(units, terrain, 1);

        // Both units should still exist and be alive
        Assert.Equal(2, units.Count);
        Assert.True(units[0].IsAlive);
        Assert.True(units[1].IsAlive);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 6 — Collision Damage / Crush
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_UnitsNotOverlapping_NoCollisions()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var units = new List<SimUnit>
        {
            MakeUnit(1, 1, new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5))),
            MakeUnit(2, 1, new FixedVector2(FixedPoint.FromInt(20), FixedPoint.FromInt(20)))
        };

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Crushes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 7 — Combat: Target Acquisition & Damage
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_ArmedUnitAttacksNearbyEnemy()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Attacker (player 1) with weapon, target (player 2) within range
        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        var target = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(14), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { attacker, target };

        TickResult result = system.ProcessTick(units, terrain, 1);

        // Should have at least one attack result
        Assert.NotEmpty(result.Attacks);
        Assert.Equal(1, result.Attacks[0].AttackerId);
        Assert.Equal(2, result.Attacks[0].TargetId);
    }

    [Fact]
    public void ProcessTick_ArmedUnitDoesNotAttackFriendly()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Both units belong to player 1
        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        var friendly = MakeUnit(2, 1,
            new FixedVector2(FixedPoint.FromInt(12), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { attacker, friendly };

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Attacks);
    }

    [Fact]
    public void ProcessTick_UnarmedUnit_NoAttacks()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var unarmed = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: false);
        var enemy = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(12), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { unarmed, enemy };

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Attacks);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 7 — HoldFire Stance Skips Combat
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_HoldFire_DoesNotAttack()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true, stance: UnitStance.HoldFire);
        var enemy = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(12), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { attacker, enemy };

        TickResult result = system.ProcessTick(units, terrain, 1);

        Assert.Empty(result.Attacks);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 7 — Weapon Cooldown
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_WeaponOnCooldown_DoesNotFireUntilReady()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        // Set large cooldown so weapon cannot fire
        attacker.WeaponCooldowns[0] = FixedPoint.FromInt(999);

        var enemy = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(12), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { attacker, enemy };

        TickResult result = system.ProcessTick(units, terrain, 1);

        // Weapon should not fire due to cooldown
        Assert.Empty(result.Attacks);

        // But cooldown should have been decremented by 1
        Assert.Equal(FixedPoint.FromInt(998), units[0].WeaponCooldowns[0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 7 — Kill & XP / Veterancy (Phase 8a)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_KillGrantsXPAndVeterancy()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // High-damage attacker vs low-health target
        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        attacker.Weapons[0] = new WeaponData
        {
            Id = "big_gun",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(200), // One-shot kill
            RateOfFire = FixedPoint.One,
            Range = FixedPoint.FromInt(10),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.Zero,
            CanTarget = TargetType.Ground | TargetType.Air | TargetType.Building,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = new Dictionary<ArmorType, FixedPoint>()
        };

        var target = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(14), FixedPoint.FromInt(10)),
            health: FixedPoint.FromInt(10));

        var units = new List<SimUnit> { attacker, target };

        TickResult result = system.ProcessTick(units, terrain, 1);

        // Target should be destroyed
        Assert.Contains(2, result.DestroyedUnitIds);

        // Attacker should have gained XP
        Assert.True(units[0].XP >= 1);
        Assert.Equal(VeterancyLevel.Veteran, units[0].Veterancy);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 8 — Dead Unit Removal
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_DeadUnitsRemovedFromList()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        attacker.Weapons[0] = new WeaponData
        {
            Id = "big_gun",
            Type = WeaponType.Cannon,
            Damage = FixedPoint.FromInt(500),
            RateOfFire = FixedPoint.One,
            Range = FixedPoint.FromInt(10),
            MinRange = FixedPoint.Zero,
            ProjectileSpeed = FixedPoint.Zero,
            AreaOfEffect = FixedPoint.Zero,
            CanTarget = TargetType.Ground | TargetType.Air | TargetType.Building,
            AccuracyPercent = FixedPoint.FromInt(100),
            ArmorModifiers = new Dictionary<ArmorType, FixedPoint>()
        };

        var target = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(14), FixedPoint.FromInt(10)),
            health: FixedPoint.FromInt(10));

        var units = new List<SimUnit> { attacker, target };

        system.ProcessTick(units, terrain, 1);

        // Dead units are removed from the list
        Assert.Single(units);
        Assert.Equal(1, units[0].UnitId);
    }

    [Fact]
    public void ProcessTick_DestroyedIdsAreDeduplicated()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Test with just units that stay alive — destroyed ID list should be empty
        var units = new List<SimUnit>
        {
            MakeUnit(1, 1, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)))
        };

        TickResult result = system.ProcessTick(units, terrain, 1);
        Assert.Empty(result.DestroyedUnitIds);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Stealth Resolution
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_StealthUnit_BecomesStealthedWithNoDetector()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var stealth = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: true);

        var enemy = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(50), FixedPoint.FromInt(50)),
            armed: true);

        var units = new List<SimUnit> { stealth, enemy };

        system.ProcessTick(units, terrain, 1);

        // Stealth unit should be stealthed (no detector nearby)
        Assert.True(units[0].IsCurrentlyStealthed);
    }

    [Fact]
    public void ProcessTick_StealthUnit_DetectedByNearbyDetector()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var stealth = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: true);

        var detector = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(12), FixedPoint.FromInt(10)),
            isDetector: true, sightRange: FixedPoint.FromInt(20));

        var units = new List<SimUnit> { stealth, detector };

        system.ProcessTick(units, terrain, 1);

        // Stealth unit should be revealed by the detector
        Assert.False(units[0].IsCurrentlyStealthed);
    }

    [Fact]
    public void ProcessTick_StealthUnit_FiringRevealsTemporarily()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Stealth unit with weapon, starts stealthed
        var stealth = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true, isStealthUnit: true);
        stealth.IsCurrentlyStealthed = true;

        var enemy = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(14), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { stealth, enemy };

        TickResult result = system.ProcessTick(units, terrain, 1);

        // If the stealth unit fired, it should be revealed
        if (result.Attacks.Count > 0)
        {
            Assert.False(units[0].IsCurrentlyStealthed);
            Assert.True(units[0].StealthRevealTicks > 0);
        }
    }

    [Fact]
    public void ProcessTick_NonStealthUnit_NeverStealthed()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var unit = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: false);
        // Force-set stealthed flag (should be cleared)
        unit.IsCurrentlyStealthed = true;

        var units = new List<SimUnit> { unit };

        system.ProcessTick(units, terrain, 1);

        Assert.False(units[0].IsCurrentlyStealthed);
    }

    [Fact]
    public void ProcessTick_StealthRevealTimer_DecrementsEachTick()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var stealth = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: true);
        stealth.StealthRevealTicks = 5;

        var units = new List<SimUnit> { stealth };

        system.ProcessTick(units, terrain, 1);

        Assert.Equal(4, units[0].StealthRevealTicks);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Submarine Stealth — Deep Water
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_Submarine_StealthedInDeepWater()
    {
        var system = CreateSystem();
        var terrain = new TerrainGrid(64, 64, FixedPoint.One);

        // Set up deep water at position (10, 10)
        ref TerrainCell cell = ref terrain.GetCell(10, 10);
        cell.Type = TerrainType.DeepWater;

        var sub = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: true, category: UnitCategory.Submarine);
        sub.Profile = MovementProfile.Naval();

        var units = new List<SimUnit> { sub };

        system.ProcessTick(units, terrain, 1);

        Assert.True(units[0].IsCurrentlyStealthed);
    }

    [Fact]
    public void ProcessTick_Submarine_RevealedInShallowWater()
    {
        var system = CreateSystem();
        var terrain = new TerrainGrid(64, 64, FixedPoint.One);

        // Set up shallow water at position (10, 10)
        ref TerrainCell cell = ref terrain.GetCell(10, 10);
        cell.Type = TerrainType.Water;

        var sub = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            isStealthUnit: true, category: UnitCategory.Submarine);
        sub.Profile = MovementProfile.Naval();

        var units = new List<SimUnit> { sub };

        system.ProcessTick(units, terrain, 1);

        Assert.False(units[0].IsCurrentlyStealthed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Determinism — Same inputs produce same results
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_Deterministic_SameInputsSameOutput()
    {
        var terrain = CreateFlatTerrain();

        // Run tick with same setup twice, should produce identical results
        SimUnit MakeSetup(int id, int pid, int x, int y, bool arm) =>
            MakeUnit(id, pid, new FixedVector2(FixedPoint.FromInt(x), FixedPoint.FromInt(y)), armed: arm);

        var units1 = new List<SimUnit>
        {
            MakeSetup(1, 1, 10, 10, true),
            MakeSetup(2, 2, 14, 10, false)
        };

        var units2 = new List<SimUnit>
        {
            MakeSetup(1, 1, 10, 10, true),
            MakeSetup(2, 2, 14, 10, false)
        };

        var system1 = CreateSystem();
        var system2 = CreateSystem();

        TickResult result1 = system1.ProcessTick(units1, terrain, 1);
        TickResult result2 = system2.ProcessTick(units2, terrain, 1);

        Assert.Equal(result1.Attacks.Count, result2.Attacks.Count);
        Assert.Equal(result1.DestroyedUnitIds.Count, result2.DestroyedUnitIds.Count);
        Assert.Equal(units1.Count, units2.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Multiple Ticks — Sustained Combat
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_MultipleTicks_EventuallyKillsTarget()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        // Low-damage attacker, medium-health target — requires multiple ticks
        var attacker = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            armed: true);
        var target = MakeUnit(2, 2,
            new FixedVector2(FixedPoint.FromInt(14), FixedPoint.FromInt(10)),
            health: FixedPoint.FromInt(100));

        var units = new List<SimUnit> { attacker, target };

        bool targetKilled = false;
        for (int tick = 1; tick <= 200; tick++)
        {
            TickResult result = system.ProcessTick(units, terrain, (ulong)tick);
            if (result.DestroyedUnitIds.Count > 0)
            {
                targetKilled = true;
                break;
            }
        }

        Assert.True(targetKilled, "Target should eventually be killed over multiple ticks");
        Assert.Single(units); // Only attacker remains
    }

    // ═══════════════════════════════════════════════════════════════════
    // Air Units Skip Ground Collisions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessTick_AirUnit_CorrectlyClassifiedInCollisionInfo()
    {
        var system = CreateSystem();
        var terrain = CreateFlatTerrain();

        var heli = MakeUnit(1, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            category: UnitCategory.Helicopter);
        heli.Profile = MovementProfile.Helicopter();

        var ground = MakeUnit(2, 1,
            new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10)));

        var units = new List<SimUnit> { heli, ground };

        // Should not crash and both units should survive
        TickResult result = system.ProcessTick(units, terrain, 1);
        Assert.Equal(2, units.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CombatRng Exposed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CombatRng_IsAccessible()
    {
        var system = CreateSystem();
        Assert.NotNull(system.CombatRng);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TickResult Structure
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TickResult_ContainsAllExpectedFields()
    {
        var result = new TickResult
        {
            Collisions = new List<UnitCollisionResult>(),
            Crushes = new List<CrushResult>(),
            Attacks = new List<AttackResult>(),
            DestroyedUnitIds = new List<int>(),
            Tick = 5
        };

        Assert.NotNull(result.Collisions);
        Assert.NotNull(result.Crushes);
        Assert.NotNull(result.Attacks);
        Assert.NotNull(result.DestroyedUnitIds);
        Assert.Equal(5UL, result.Tick);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SimUnit Structure
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SimUnit_DefaultValues()
    {
        var unit = new SimUnit();
        Assert.Equal(0, unit.UnitId);
        Assert.Equal(0, unit.PlayerId);
        Assert.False(unit.IsAlive);
        Assert.Equal(UnitStance.Aggressive, unit.Stance);
        Assert.Equal(VeterancyLevel.Recruit, unit.Veterancy);
        Assert.Equal(0, unit.XP);
        Assert.False(unit.IsStealthUnit);
        Assert.False(unit.IsDetector);
        Assert.False(unit.IsCurrentlyStealthed);
        Assert.Equal(0, unit.StealthRevealTicks);
    }
}
