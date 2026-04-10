using System.Collections.Generic;
using CorditeWars.Core;
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
    public void ResolveCollisions_HeavierUnitMovesSLess()
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
}
