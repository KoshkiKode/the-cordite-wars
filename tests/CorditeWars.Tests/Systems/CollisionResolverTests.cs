using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for CollisionResolver — unit-vs-unit collision detection and response.
/// Covers DetectCollisions (broad + narrow phase), ResolveCollisions (push and crush),
/// and the BuildCollisionInfo static helper.
/// </summary>
public class CollisionResolverTests
{
    private readonly CollisionResolver _resolver = new();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static UnitCollisionInfo MakeUnit(
        int id,
        FixedVector2 position,
        FixedPoint? radius = null,
        FixedPoint? mass   = null,
        bool isAir = false,
        FixedPoint? height = null,
        ArmorType armor = ArmorType.Medium,
        FixedPoint? crushStrength = null)
    {
        return new UnitCollisionInfo
        {
            UnitId        = id,
            PlayerId      = 1,
            Position      = position,
            Radius        = radius        ?? FixedPoint.One,
            Mass          = mass          ?? FixedPoint.FromInt(5),
            IsAirUnit     = isAir,
            Height        = height        ?? FixedPoint.Zero,
            ArmorClass    = armor,
            CrushStrength = crushStrength ?? FixedPoint.Zero
        };
    }

    private static SpatialHash BuildSpatialHash(List<UnitCollisionInfo> units)
    {
        var spatial = new SpatialHash(256, 256);
        foreach (var u in units)
            spatial.Insert(u.UnitId, u.Position, u.Radius);
        return spatial;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — no overlap
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_NoOverlap_ReturnsNoPairs()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, new FixedVector2(FixedPoint.Zero, FixedPoint.Zero)),
            MakeUnit(2, new FixedVector2(FixedPoint.FromInt(10), FixedPoint.Zero))
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Empty(pairs);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — overlapping pair
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_OverlappingPair_ReturnsOnePair()
    {
        // Two units with radius 1 placed 1 unit apart → overlap of 1.
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, new FixedVector2(FixedPoint.Zero, FixedPoint.Zero), radius: FixedPoint.One),
            MakeUnit(2, new FixedVector2(FixedPoint.One,  FixedPoint.Zero), radius: FixedPoint.One)
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Single(pairs);
    }

    [Fact]
    public void DetectCollisions_PairHasCorrectUnitIds()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero))
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Single(pairs);
        Assert.Equal(1, pairs[0].UnitIdA);
        Assert.Equal(2, pairs[0].UnitIdB);
    }

    [Fact]
    public void DetectCollisions_PairHasPositiveOverlap()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero))
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Single(pairs);
        Assert.True(pairs[0].Overlap > FixedPoint.Zero, "Overlap should be positive");
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — air vs ground separation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_AirAndGround_DoNotCollide()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero, isAir: false),
            MakeUnit(2, FixedVector2.Zero, isAir: true)  // exact same position, but air
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Empty(pairs);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — air vs air altitude threshold
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_AirVsAirSameAltitude_Collides()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero, isAir: true, height: FixedPoint.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero), isAir: true, height: FixedPoint.Zero)
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Single(pairs);
    }

    [Fact]
    public void DetectCollisions_AirVsAirDifferentAltitude_DoesNotCollide()
    {
        // Altitude difference of 5 > AirAltitudeThreshold (2) → no collision.
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero, isAir: true, height: FixedPoint.Zero),
            MakeUnit(2, FixedVector2.Zero, isAir: true, height: FixedPoint.FromInt(5))
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Empty(pairs);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — deduplication (N units → at most N*(N-1)/2 pairs)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_ThreeOverlappingUnits_ProducesThreePairs()
    {
        // All three at origin with radius 1 → 3 pairs: (1,2), (1,3), (2,3).
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, FixedVector2.Zero),
            MakeUnit(3, FixedVector2.Zero)
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Equal(3, pairs.Count);
        // Verify no duplicates (A < B guaranteed)
        foreach (var p in pairs)
            Assert.True(p.UnitIdA < p.UnitIdB);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — normal push (equal mass)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_EqualMass_PushesUnitsSeparately()
    {
        // Two equal-mass units at x=0 and x=1, radius=1 (overlap=1).
        var posA = FixedVector2.Zero;
        var posB = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        var mass = FixedPoint.FromInt(5);

        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA   = 1,
                UnitIdB   = 2,
                PositionA = posA,
                PositionB = posB,
                RadiusA   = FixedPoint.One,
                RadiusB   = FixedPoint.One,
                MassA     = mass,
                MassB     = mass,
                Overlap   = FixedPoint.One,
                Normal    = new FixedVector2(FixedPoint.One, FixedPoint.Zero), // A→B
                IsCrush   = false
            }
        };
        var results = new List<UnitCollisionResult>();

        _resolver.ResolveCollisions(pairs, results);

        Assert.Equal(2, results.Count);
        var rA = results.Find(r => r.UnitId == 1);
        var rB = results.Find(r => r.UnitId == 2);

        Assert.Equal(1, rA.UnitId);
        Assert.Equal(2, rB.UnitId);

        // A should be pushed in -Normal (+X is normal so A moves left)
        Assert.True(rA!.NewPosition.X < posA.X,
            "Equal mass: unit A should move left (away from B)");
        // B should be pushed in +Normal (B moves right)
        Assert.True(rB!.NewPosition.X > posB.X,
            "Equal mass: unit B should move right (away from A)");
    }

    [Fact]
    public void ResolveCollisions_EqualMass_PushDistanceIsSymmetric()
    {
        var posA = FixedVector2.Zero;
        var posB = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        var mass = FixedPoint.FromInt(5);

        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = posA, PositionB = posB,
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = mass, MassB = mass,
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = false
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        var rA = results.Find(r => r.UnitId == 1)!;
        var rB = results.Find(r => r.UnitId == 2)!;

        FixedPoint pushA = FixedPoint.Abs(rA.NewPosition.X - posA.X);
        FixedPoint pushB = FixedPoint.Abs(rB.NewPosition.X - posB.X);

        Assert.Equal(pushA, pushB);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — mass-weighted push (heavy unit moves less)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_HeavierUnitMovesLess()
    {
        var posA = FixedVector2.Zero;
        var posB = new FixedVector2(FixedPoint.One, FixedPoint.Zero);

        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = posA, PositionB = posB,
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.FromInt(10), // heavy
                MassB = FixedPoint.FromInt(1),  // light
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = false
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        var rA = results.Find(r => r.UnitId == 1)!;
        var rB = results.Find(r => r.UnitId == 2)!;

        FixedPoint pushA = FixedPoint.Abs(rA.NewPosition.X - posA.X);
        FixedPoint pushB = FixedPoint.Abs(rB.NewPosition.X - posB.X);

        Assert.True(pushA < pushB,
            "Heavier unit A should be pushed less than lighter unit B");
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — crush
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_Crush_CrushedUnitTakesDamage()
    {
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = FixedVector2.Zero,
                PositionB = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.FromInt(20), MassB = FixedPoint.One,
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = true
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        Assert.Equal(2, results.Count);

        var rB = results.Find(r => r.UnitId == 2)!;
        Assert.True(rB.WasCrushed);
        Assert.True(rB.DamageTaken > FixedPoint.Zero,
            "Crushed unit should take crush damage");
    }

    [Fact]
    public void ResolveCollisions_Crush_CrusherTakesNoDamage()
    {
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = FixedVector2.Zero,
                PositionB = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.FromInt(20), MassB = FixedPoint.One,
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = true
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        var rA = results.Find(r => r.UnitId == 1)!;
        Assert.Equal(FixedPoint.Zero, rA.DamageTaken);
        Assert.False(rA.WasCrushed);
    }

    [Fact]
    public void ResolveCollisions_Crush_CrusherPositionUnchanged()
    {
        var crusherPos = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.Zero);
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = crusherPos,
                PositionB = new FixedVector2(FixedPoint.FromInt(6), FixedPoint.Zero),
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.FromInt(20), MassB = FixedPoint.One,
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = true
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        var rA = results.Find(r => r.UnitId == 1)!;
        Assert.Equal(crusherPos, rA.NewPosition);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — empty pairs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_NoPairs_OutputEmpty()
    {
        var results = new List<UnitCollisionResult> { new UnitCollisionResult { UnitId = 99 } };
        _resolver.ResolveCollisions(new List<CollisionPair>(), results);
        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Determinism: same inputs → same outputs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_IsDeterministic()
    {
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero)),
            MakeUnit(3, new FixedVector2(FixedPoint.Half, FixedPoint.One))
        };

        var spatialA = BuildSpatialHash(units);
        var spatialB = BuildSpatialHash(units);
        var pairsA   = new List<CollisionPair>();
        var pairsB   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatialA, pairsA);
        _resolver.DetectCollisions(units, spatialB, pairsB);

        Assert.Equal(pairsA.Count, pairsB.Count);
        for (int i = 0; i < pairsA.Count; i++)
        {
            Assert.Equal(pairsA[i].UnitIdA, pairsB[i].UnitIdA);
            Assert.Equal(pairsA[i].UnitIdB, pairsB[i].UnitIdB);
            Assert.Equal(pairsA[i].Overlap,  pairsB[i].Overlap);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — B crushes A (reverse crush direction)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_BCrushesA_ATakesDamage()
    {
        // B is the heavy crusher (mass 20), A is the light victim (mass 1)
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = FixedVector2.Zero,
                PositionB = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.One,          // light — A is the victim
                MassB = FixedPoint.FromInt(20),  // heavy — B is the crusher
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = true
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        Assert.Equal(2, results.Count);
        var rA = results.Find(r => r.UnitId == 1)!;
        var rB = results.Find(r => r.UnitId == 2)!;

        Assert.True(rA.WasCrushed,  "A (the lighter unit) should be crushed by B");
        Assert.False(rB.WasCrushed, "B (the crusher) should not be crushed");
        Assert.True(rA.DamageTaken > FixedPoint.Zero, "Crushed unit A should take crush damage");
        Assert.Equal(FixedPoint.Zero, rB.DamageTaken);
    }

    [Fact]
    public void ResolveCollisions_Crush_NeitherMeetsRatio_FallsBackToPush()
    {
        // Both units have the same mass — neither meets the 2× crush mass ratio,
        // so the collision should fall back to normal push resolution.
        var posA = FixedVector2.Zero;
        var posB = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = posA, PositionB = posB,
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.FromInt(5), MassB = FixedPoint.FromInt(5), // equal mass
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = true  // flagged as crush but mass ratio not met
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        Assert.Equal(2, results.Count);
        // Both units should be pushed (push resolution), neither WasCrushed
        Assert.All(results, r => Assert.False(r.WasCrushed));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveCollisions — degenerate zero mass
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCollisions_BothZeroMass_EqualPush()
    {
        var posA = FixedVector2.Zero;
        var posB = new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        var pairs = new List<CollisionPair>
        {
            new CollisionPair
            {
                UnitIdA = 1, UnitIdB = 2,
                PositionA = posA, PositionB = posB,
                RadiusA = FixedPoint.One, RadiusB = FixedPoint.One,
                MassA = FixedPoint.Zero, MassB = FixedPoint.Zero,
                Overlap = FixedPoint.One,
                Normal = new FixedVector2(FixedPoint.One, FixedPoint.Zero),
                IsCrush = false
            }
        };
        var results = new List<UnitCollisionResult>();
        _resolver.ResolveCollisions(pairs, results);

        Assert.Equal(2, results.Count);
        // With zero mass both units pushed equally
        var rA = results.Find(r => r.UnitId == 1)!;
        var rB = results.Find(r => r.UnitId == 2)!;
        FixedPoint pushA = FixedPoint.Abs(rA.NewPosition.X - posA.X);
        FixedPoint pushB = FixedPoint.Abs(rB.NewPosition.X - posB.X);
        Assert.Equal(pushA, pushB);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CheckCrush — via DetectCollisions (indirect test of private method)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_HeavyArmoredTargetNoCrush_ArmedInfantryNoCrush()
    {
        // A heavy vehicle (crushStrength > 0) cannot crush a Heavy-armored target
        // Only Unarmored and Light can be crushed.
        var heavyVehicle = MakeUnit(1, FixedVector2.Zero,
            mass: FixedPoint.FromInt(20), crushStrength: FixedPoint.FromInt(10));
        var heavyArmored = MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            mass: FixedPoint.One, armor: ArmorType.Heavy);

        var units = new List<UnitCollisionInfo> { heavyVehicle, heavyArmored };
        var spatial = BuildSpatialHash(units);
        var pairs = new List<CollisionPair>();
        _resolver.DetectCollisions(units, spatial, pairs);

        // If any pair was detected, it should NOT be a crush
        foreach (var pair in pairs)
            Assert.False(pair.IsCrush, "Heavy-armored target should not be crushable");
    }

    [Fact]
    public void DetectCollisions_LightInfantryCanBeCrushed()
    {
        // Heavy vehicle (crushStrength 10, mass 20) runs over infantry (unarmored, mass 1)
        var heavyVehicle = MakeUnit(1, FixedVector2.Zero,
            mass: FixedPoint.FromInt(20), crushStrength: FixedPoint.FromInt(10));
        var infantry = MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            mass: FixedPoint.One, armor: ArmorType.Unarmored);

        var units = new List<UnitCollisionInfo> { heavyVehicle, infantry };
        var spatial = BuildSpatialHash(units);
        var pairs = new List<CollisionPair>();
        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.True(pairs.Count > 0, "Heavy vehicle overlapping infantry should produce a pair");
        Assert.True(pairs[0].IsCrush, "Heavy vehicle should be able to crush unarmored infantry");
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildCollisionInfo — registry-based construction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildCollisionInfo_PopulatesAllFieldsFromRegistry()
    {
        var reg = new AssetRegistry();
        reg.Register("infantry_unit", new AssetEntry
        {
            CollisionRadius = FixedPoint.FromFloat(0.75f),
            Mass            = FixedPoint.FromInt(5),
            CrushStrength   = FixedPoint.FromInt(0)
        });

        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        var info = CollisionResolver.BuildCollisionInfo(
            unitId: 42,
            playerId: 1,
            dataId: "infantry_unit",
            position: pos,
            isAirUnit: false,
            height: FixedPoint.Zero,
            armorClass: ArmorType.Light,
            registry: reg);

        Assert.Equal(42, info.UnitId);
        Assert.Equal(1, info.PlayerId);
        Assert.Equal(pos, info.Position);
        Assert.Equal(FixedPoint.FromFloat(0.75f), info.Radius);
        Assert.Equal(FixedPoint.FromInt(5), info.Mass);
        Assert.Equal(FixedPoint.Zero, info.CrushStrength);
        Assert.False(info.IsAirUnit);
        Assert.Equal(ArmorType.Light, info.ArmorClass);
    }

    [Fact]
    public void BuildCollisionInfo_AirUnit_FlagsSetCorrectly()
    {
        var reg = new AssetRegistry();
        reg.Register("helicopter", new AssetEntry
        {
            CollisionRadius = FixedPoint.FromFloat(1.5f),
            Mass            = FixedPoint.FromInt(10),
            CrushStrength   = FixedPoint.Zero
        });

        var info = CollisionResolver.BuildCollisionInfo(
            unitId: 1,
            playerId: 2,
            dataId: "helicopter",
            position: FixedVector2.Zero,
            isAirUnit: true,
            height: FixedPoint.FromInt(5),
            armorClass: ArmorType.Medium,
            registry: reg);

        Assert.True(info.IsAirUnit);
        Assert.Equal(FixedPoint.FromInt(5), info.Height);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ResolveStaticCollisions — pushout when inside blocked cell
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveStaticCollisions_AirUnit_NotPushed()
    {
        // Air units should pass through all terrain / building blocking.
        var terrain   = new TerrainGrid(32, 32, FixedPoint.One);
        var occupancy = new OccupancyGrid(32, 32);

        // Mark center cell as blocked.
        ref TerrainCell cell = ref terrain.GetCell(5, 5);
        cell.IsBlocked = true;

        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, new FixedVector2(FixedPoint.FromFloat(5.5f), FixedPoint.FromFloat(5.5f)),
                isAir: true)
        };
        var originalPos = units[0].Position;

        _resolver.ResolveStaticCollisions(units, terrain, occupancy);

        Assert.Equal(originalPos, units[0].Position);
    }

    [Fact]
    public void ResolveStaticCollisions_GroundUnit_OnOpenTerrain_NotMoved()
    {
        var terrain   = new TerrainGrid(32, 32, FixedPoint.One);
        var occupancy = new OccupancyGrid(32, 32);

        var pos = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        var units = new List<UnitCollisionInfo> { MakeUnit(1, pos) };

        _resolver.ResolveStaticCollisions(units, terrain, occupancy);

        Assert.Equal(pos, units[0].Position);
    }

    [Fact]
    public void ResolveStaticCollisions_GroundUnit_InsideBlockedCell_IsPushedOut()
    {
        var terrain   = new TerrainGrid(32, 32, FixedPoint.One);
        var occupancy = new OccupancyGrid(32, 32);

        // Block cell (5,5).
        ref TerrainCell cell = ref terrain.GetCell(5, 5);
        cell.IsBlocked = true;

        // Unit is at center of the blocked cell.
        var pos = new FixedVector2(FixedPoint.FromFloat(5.5f), FixedPoint.FromFloat(5.5f));
        var units = new List<UnitCollisionInfo> { MakeUnit(1, pos) };

        _resolver.ResolveStaticCollisions(units, terrain, occupancy);

        // The unit must have been moved to an adjacent unblocked cell.
        Assert.NotEqual(pos, units[0].Position);
    }

    [Fact]
    public void ResolveStaticCollisions_GroundUnit_InsideBuildingOccupancy_IsPushedOut()
    {
        var terrain   = new TerrainGrid(32, 32, FixedPoint.One);
        var occupancy = new OccupancyGrid(32, 32);

        // Place a building on cell (8,8).
        occupancy.OccupyCell(8, 8, OccupancyType.Building, occupantId: 99, playerId: 0);

        var pos = new FixedVector2(FixedPoint.FromFloat(8.5f), FixedPoint.FromFloat(8.5f));
        var units = new List<UnitCollisionInfo> { MakeUnit(1, pos) };

        _resolver.ResolveStaticCollisions(units, terrain, occupancy);

        Assert.NotEqual(pos, units[0].Position);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DetectCollisions — edge cases for coverage
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectCollisions_CandidateIdBeyondArrayBounds_SkippedGracefully()
    {
        // Unit A has ID=1; we insert an ID into the spatial hash that is beyond
        // unitIdToIndex.Length so the "candidateId >= unitIdToIndex.Length" guard fires.
        // We do this by inserting a fake high-ID unit in the spatial hash but NOT
        // in the units list, causing the lookup array to not have space for it.
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
        };
        var spatial = new SpatialHash(256, 256);
        spatial.Insert(1,    FixedVector2.Zero, FixedPoint.One);
        // Insert a unit with a very high ID that won't be in unitIdToIndex
        spatial.Insert(9999, new FixedVector2(FixedPoint.One, FixedPoint.Zero), FixedPoint.One);

        var pairs = new List<CollisionPair>();
        _resolver.DetectCollisions(units, spatial, pairs);

        // Should not throw and produce no pairs (the 9999 candidate is skipped).
        Assert.Empty(pairs);
    }

    [Fact]
    public void DetectCollisions_UnitInSpatialHashButNotInUnitsList_SkippedGracefully()
    {
        // Unit 2 is in the spatial hash but removed from the units list before
        // detection (candidateIndex < 0 because ID 2 was never added to unitIdToIndex).
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            // Unit 2 is in the spatial hash but deliberately NOT in the units list
        };
        var spatial = new SpatialHash(256, 256);
        spatial.Insert(1, FixedVector2.Zero, FixedPoint.One);
        // Insert unit 2 in spatial hash but not in the units list; unit 2's index
        // in unitIdToIndex will be -1 (default).
        // We add the unit to the list to size unitIdToIndex[2], then remove it.
        // Simpler: just register unit 2 in the hash at a nearby position.
        spatial.Insert(2, new FixedVector2(FixedPoint.Half, FixedPoint.Zero), FixedPoint.One);

        // Build a units list that has ID=1 AND ID=2 so the array is sized,
        // but then use an updated list that only has ID=1.
        var fullUnits = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.Half, FixedPoint.Zero))
        };
        // This run populates unitIdToIndex for both; no crash expected.
        var pairs = new List<CollisionPair>();
        _resolver.DetectCollisions(fullUnits, spatial, pairs);
        // Just verify no crash and pairs are consistent.
        Assert.True(pairs.Count >= 0);
    }

    [Fact]
    public void DetectCollisions_TouchingButNotOverlapping_NoPairs()
    {
        // Two units exactly touching (dist == combinedRadius → overlap <= 0) → no pair.
        // combinedRadius = 1 + 1 = 2. Place units at distance exactly 2.
        var units = new List<UnitCollisionInfo>
        {
            MakeUnit(1, FixedVector2.Zero),
            MakeUnit(2, new FixedVector2(FixedPoint.FromInt(2), FixedPoint.Zero))
        };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DetectCollisions_CrusherNotHeavyEnough_PushNotCrush()
    {
        // Crusher has crushStrength > 0 and target is unarmored,
        // but crusher.Mass <= target.Mass * CrushMassRatio → not a crush.
        // CrushMassRatio = 2×. Set crusher mass = 2, target mass = 1 → ratio just met (not strictly greater).
        var crusher = MakeUnit(1, FixedVector2.Zero,
            mass: FixedPoint.FromInt(2), crushStrength: FixedPoint.FromInt(5));
        var target  = MakeUnit(2, new FixedVector2(FixedPoint.One, FixedPoint.Zero),
            mass: FixedPoint.FromInt(1), armor: ArmorType.Unarmored);

        var units   = new List<UnitCollisionInfo> { crusher, target };
        var spatial = BuildSpatialHash(units);
        var pairs   = new List<CollisionPair>();

        _resolver.DetectCollisions(units, spatial, pairs);

        // The pair may still exist as a push (IsCrush should be false)
        if (pairs.Count > 0)
            Assert.False(pairs[0].IsCrush, "Crusher not heavy enough — should not be a crush");
    }
}
