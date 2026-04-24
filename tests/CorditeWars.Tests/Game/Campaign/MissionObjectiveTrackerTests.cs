using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Campaign;

namespace CorditeWars.Tests.Game.Campaign;

/// <summary>
/// Tests for <see cref="MissionObjectiveTracker"/> — campaign mission objectives.
/// Covers all five objective types (BuildBuilding, MaintainUnitType, SurviveTimer,
/// DestroyBuildingType, AccumulateCordite), required vs optional objectives,
/// AllPrimaryObjectivesComplete, AnyObjectiveFailed, and tracker initialization.
/// </summary>
public class MissionObjectiveTrackerTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TypedObjective MakeSurviveTimer(int ticks, bool required = true, string label = "Survive") =>
        new TypedObjective
        {
            Type     = ObjectiveType.SurviveTimer,
            Label    = label,
            Count    = 1,
            Ticks    = ticks,
            Required = required
        };

    private static TypedObjective MakeAccumulateCordite(int amount, bool required = true) =>
        new TypedObjective
        {
            Type     = ObjectiveType.AccumulateCordite,
            Label    = "Accumulate Cordite",
            TargetId = string.Empty,
            Count    = amount,
            Required = required
        };

    private static TypedObjective MakeDestroyBuilding(string typeId, int count = 1, bool required = true) =>
        new TypedObjective
        {
            Type     = ObjectiveType.DestroyBuildingType,
            Label    = $"Destroy {typeId}",
            TargetId = typeId,
            Count    = count,
            Required = required
        };

    private static TypedObjective MakeBuildBuilding(string typeId, int count = 1, bool required = true) =>
        new TypedObjective
        {
            Type     = ObjectiveType.BuildBuilding,
            Label    = $"Build {typeId}",
            TargetId = typeId,
            Count    = count,
            Required = required
        };

    private static TypedObjective MakeMaintainUnit(string typeId, int count = 1, bool required = true) =>
        new TypedObjective
        {
            Type     = ObjectiveType.MaintainUnitType,
            Label    = $"Maintain {typeId}",
            TargetId = typeId,
            Count    = count,
            Required = required
        };

    private static MissionSessionContext EmptyContext(FixedPoint cordite = default) =>
        new MissionSessionContext
        {
            PlayerCordite = cordite
        };

    private static MissionObjectiveTracker MakeTracker(IEnumerable<TypedObjective> objectives,
        ulong startTick = 0)
    {
        var tracker = new MissionObjectiveTracker();
        tracker.Initialize(new List<TypedObjective>(objectives), startTick);
        return tracker;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AllPrimaryObjectivesComplete — empty objective list
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllPrimaryObjectivesComplete_NoObjectives_ReturnsFalse()
    {
        var tracker = MakeTracker(new List<TypedObjective>());
        Assert.False(tracker.AllPrimaryObjectivesComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AllPrimaryObjectivesComplete — single objective
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllPrimaryObjectivesComplete_OneRequiredIncomplete_ReturnsFalse()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(300) });
        Assert.False(tracker.AllPrimaryObjectivesComplete);
    }

    [Fact]
    public void AllPrimaryObjectivesComplete_OneRequiredComplete_ReturnsTrue()
    {
        var obj = MakeSurviveTimer(10);
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 10);
        Assert.True(tracker.AllPrimaryObjectivesComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AllPrimaryObjectivesComplete — optional objectives never block completion
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllPrimaryObjectivesComplete_OnlyOptionalObjectives_ReturnsTrue()
    {
        var obj = MakeSurviveTimer(10, required: false);
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 10);
        // No required objectives: the for-loop finds nothing blocking, and
        // Count > 0 is satisfied, so AllPrimaryObjectivesComplete returns true.
        Assert.True(tracker.AllPrimaryObjectivesComplete);
    }

    [Fact]
    public void AllPrimaryObjectivesComplete_RequiredAndOptional_OnlyRequiredMustFinish()
    {
        var required = MakeSurviveTimer(10, required: true);
        var optional = MakeAccumulateCordite(9999, required: false);
        var tracker  = MakeTracker(new[] { required, optional }, startTick: 0);

        tracker.Tick(1, EmptyContext(), currentTick: 10);

        // Required is done; optional is not — should still be complete
        Assert.True(tracker.AllPrimaryObjectivesComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AnyObjectiveFailed
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnyObjectiveFailed_NoObjectivesFailed_ReturnsFalse()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(300) });
        Assert.False(tracker.AnyObjectiveFailed);
    }

    [Fact]
    public void AnyObjectiveFailed_OneObjectiveManuallyFailed_ReturnsTrue()
    {
        var obj = MakeSurviveTimer(300);
        var tracker = MakeTracker(new[] { obj });

        // Manually mark failed (as GameSession would do externally)
        tracker.Objectives[0].IsFailed = true;
        Assert.True(tracker.AnyObjectiveFailed);
    }

    [Fact]
    public void AnyObjectiveFailed_CompletedObjective_ReturnsFalse()
    {
        var obj = MakeSurviveTimer(5);
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 5);
        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.AnyObjectiveFailed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SurviveTimer objective
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SurviveTimer_NotEnoughTicksElapsed_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(100) }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 50);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void SurviveTimer_ExactTicksElapsed_Completes()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(100) }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 100);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void SurviveTimer_MoreThanEnoughTicksElapsed_Completes()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(100) }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 200);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void SurviveTimer_StartsFromNonZeroStartTick()
    {
        // Start at tick 1000; objective needs 300 ticks
        var tracker = MakeTracker(new[] { MakeSurviveTimer(300) }, startTick: 1000);

        // At tick 1299 (only 299 elapsed), not yet done
        tracker.Tick(1, EmptyContext(), currentTick: 1299);
        Assert.False(tracker.Objectives[0].IsComplete);

        // At tick 1300 (exactly 300 elapsed), done
        tracker.Tick(1, EmptyContext(), currentTick: 1300);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void SurviveTimer_AlreadyComplete_TickDoesNotChangeState()
    {
        var tracker = MakeTracker(new[] { MakeSurviveTimer(10) }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 10);
        Assert.True(tracker.Objectives[0].IsComplete);

        // Extra ticks should not reset or double-complete
        tracker.Tick(1, EmptyContext(), currentTick: 9999);
        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.Objectives[0].IsFailed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AccumulateCordite objective
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AccumulateCordite_InsufficientCordite_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(1000) });
        var ctx = EmptyContext(FixedPoint.FromInt(500));
        tracker.Tick(1, ctx, currentTick: 0);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void AccumulateCordite_ExactCordite_Completes()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(1000) });
        var ctx = EmptyContext(FixedPoint.FromInt(1000));
        tracker.Tick(1, ctx, currentTick: 0);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void AccumulateCordite_ExcessCordite_Completes()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(500) });
        var ctx = EmptyContext(FixedPoint.FromInt(9999));
        tracker.Tick(1, ctx, currentTick: 0);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void AccumulateCordite_ZeroCordite_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(100) });
        var ctx = EmptyContext(FixedPoint.Zero);
        tracker.Tick(1, ctx, currentTick: 0);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void AccumulateCordite_AlreadyComplete_TickDoesNotChangeState()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(100) });
        var ctx = EmptyContext(FixedPoint.FromInt(100));
        tracker.Tick(1, ctx, currentTick: 0);
        Assert.True(tracker.Objectives[0].IsComplete);

        // Subsequent tick with no cordite should not un-complete
        tracker.Tick(1, EmptyContext(FixedPoint.Zero), currentTick: 1);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DestroyBuildingType objective
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DestroyBuildingType_NoDestructions_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 1) });
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyBuildingType_WrongBuilding_NoEffect()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 1) });
        tracker.NotifyBuildingDestroyed("some_other_building");
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyBuildingType_CorrectBuildingOnce_CompletesCountOne()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 1) });
        tracker.NotifyBuildingDestroyed("enemy_hq");
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyBuildingType_PartialDestructions_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 3) });
        tracker.NotifyBuildingDestroyed("enemy_hq");
        tracker.NotifyBuildingDestroyed("enemy_hq");
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyBuildingType_ExactDestructions_Completes()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 2) });
        tracker.NotifyBuildingDestroyed("enemy_hq");
        tracker.NotifyBuildingDestroyed("enemy_hq");
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyBuildingType_AlreadyComplete_ExtraDestroyNoEffect()
    {
        var tracker = MakeTracker(new[] { MakeDestroyBuilding("enemy_hq", count: 1) });
        tracker.NotifyBuildingDestroyed("enemy_hq");
        Assert.True(tracker.Objectives[0].IsComplete);

        // Destroy again — state should remain complete (not double-fail or throw)
        tracker.NotifyBuildingDestroyed("enemy_hq");
        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.Objectives[0].IsFailed);
    }

    [Fact]
    public void DestroyBuildingType_MultipleObjectives_EachTracksIndependently()
    {
        var obj1 = MakeDestroyBuilding("barracks", count: 1);
        var obj2 = MakeDestroyBuilding("vehicle_factory", count: 2);
        var tracker = MakeTracker(new[] { obj1, obj2 });

        tracker.NotifyBuildingDestroyed("barracks");
        tracker.NotifyBuildingDestroyed("vehicle_factory");

        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.Objectives[1].IsComplete);

        tracker.NotifyBuildingDestroyed("vehicle_factory");
        Assert.True(tracker.Objectives[1].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildBuilding objective (checked via Tick, with empty context)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildBuilding_EmptyContext_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeBuildBuilding("barracks") });
        tracker.Tick(1, EmptyContext(), currentTick: 1);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MaintainUnitType objective (checked via Tick, with empty context)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MaintainUnitType_EmptyContext_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeMaintainUnit("infantry") });
        tracker.Tick(1, EmptyContext(), currentTick: 1);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Initialize resets tracker
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Initialize_ClearsPreviousObjectives()
    {
        var tracker = new MissionObjectiveTracker();

        // First initialization with one objective
        var first = new List<TypedObjective> { MakeSurviveTimer(10) };
        tracker.Initialize(first, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 10);
        Assert.True(tracker.Objectives[0].IsComplete);

        // Re-initialize with a different objective list
        var second = new List<TypedObjective> { MakeAccumulateCordite(500) };
        tracker.Initialize(second, startTick: 0);

        Assert.Single(tracker.Objectives);
        Assert.Equal(ObjectiveType.AccumulateCordite, tracker.Objectives[0].Type);
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void Initialize_SetsObjectivesInOrder()
    {
        var objs = new List<TypedObjective>
        {
            MakeSurviveTimer(100),
            MakeAccumulateCordite(500),
            MakeDestroyBuilding("barracks")
        };
        var tracker = new MissionObjectiveTracker();
        tracker.Initialize(objs, startTick: 0);

        Assert.Equal(3, tracker.Objectives.Count);
        Assert.Equal(ObjectiveType.SurviveTimer,         tracker.Objectives[0].Type);
        Assert.Equal(ObjectiveType.AccumulateCordite,    tracker.Objectives[1].Type);
        Assert.Equal(ObjectiveType.DestroyBuildingType,  tracker.Objectives[2].Type);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple objectives — mixed state
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleObjectives_AllRequired_NotDoneUntilAll()
    {
        var survive = MakeSurviveTimer(100);
        var cordite = MakeAccumulateCordite(500);
        var destroy = MakeDestroyBuilding("hq");
        var tracker = MakeTracker(new[] { survive, cordite, destroy }, startTick: 0);

        // Survive and cordite done, destroy not yet
        tracker.Tick(1, EmptyContext(FixedPoint.FromInt(500)), currentTick: 100);
        Assert.False(tracker.AllPrimaryObjectivesComplete);

        // Now also destroy
        tracker.NotifyBuildingDestroyed("hq");
        Assert.True(tracker.AllPrimaryObjectivesComplete);
    }

    [Fact]
    public void MultipleObjectives_FailedObjective_DoesNotPreventOthersCompleting()
    {
        var survive = MakeSurviveTimer(10);
        var cordite = MakeAccumulateCordite(100);
        var tracker = MakeTracker(new[] { survive, cordite }, startTick: 0);

        // Mark survive as failed
        tracker.Objectives[0].IsFailed = true;

        // Cordite completed
        tracker.Tick(1, EmptyContext(FixedPoint.FromInt(100)), currentTick: 1);
        Assert.True(tracker.Objectives[1].IsComplete);
        Assert.True(tracker.AnyObjectiveFailed);
    }

    [Fact]
    public void FailedObjective_TickSkipsItCorrectly()
    {
        var obj = MakeSurviveTimer(10);
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        obj.IsFailed = true;

        // Even though enough ticks elapsed, failed obj is skipped
        tracker.Tick(1, EmptyContext(), currentTick: 10);
        Assert.False(tracker.Objectives[0].IsComplete);
        Assert.True(tracker.Objectives[0].IsFailed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Objectives — Tick with different playerIds has no cross-contamination
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AccumulateCordite_MultipleTickCalls_AccumulationLogicUsesContextEachTime()
    {
        var tracker = MakeTracker(new[] { MakeAccumulateCordite(1000) });

        // First tick: insufficient cordite
        tracker.Tick(1, EmptyContext(FixedPoint.FromInt(999)), currentTick: 0);
        Assert.False(tracker.Objectives[0].IsComplete);

        // Second tick: enough cordite
        tracker.Tick(1, EmptyContext(FixedPoint.FromInt(1000)), currentTick: 1);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EscortUnit objective
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EscortUnit_NoUnitsOfType_FailsWhenRequired()
    {
        var obj = new TypedObjective
        {
            Type = ObjectiveType.EscortUnit,
            Label = "Escort tank",
            TargetId = "tank",
            Count = 1,
            Required = true
        };
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        // Empty context means no units → should fail (required escort unit missing)
        tracker.Tick(1, EmptyContext(), currentTick: 1);
        Assert.True(tracker.Objectives[0].IsFailed);
    }

    [Fact]
    public void EscortUnit_ObjectNotFailed_WhenOptional()
    {
        var obj = new TypedObjective
        {
            Type = ObjectiveType.EscortUnit,
            Label = "Escort tank (optional)",
            TargetId = "tank",
            Count = 1,
            Required = false
        };
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        // No units of type, but not required — should not fail
        tracker.Tick(1, EmptyContext(), currentTick: 1);
        Assert.False(tracker.Objectives[0].IsFailed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DefendPosition / ReachLocation objective (survive-timer equivalents)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefendPosition_TicksElapsed_Completes()
    {
        var obj = new TypedObjective
        {
            Type = ObjectiveType.DefendPosition,
            Label = "Hold the zone",
            Ticks = 50,
            Required = true
        };
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 50);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void ReachLocation_TicksElapsed_Completes()
    {
        var obj = new TypedObjective
        {
            Type = ObjectiveType.ReachLocation,
            Label = "Reach the objective",
            Ticks = 30,
            Required = true
        };
        var tracker = MakeTracker(new[] { obj }, startTick: 0);
        tracker.Tick(1, EmptyContext(), currentTick: 29);
        Assert.False(tracker.Objectives[0].IsComplete);
        tracker.Tick(1, EmptyContext(), currentTick: 30);
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DestroyUnitType objective (driven via NotifyUnitDestroyed)
    // ═══════════════════════════════════════════════════════════════════════

    private static TypedObjective MakeDestroyUnit(string typeId, int count = 1, bool required = true) =>
        new TypedObjective
        {
            Type     = ObjectiveType.DestroyUnitType,
            Label    = $"Destroy {typeId}",
            TargetId = typeId,
            Count    = count,
            Required = required
        };

    [Fact]
    public void DestroyUnitType_NoDestructions_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank") });
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_WrongUnitType_NoEffect()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank", count: 1) });
        tracker.NotifyUnitDestroyed("some_other_unit");
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_CorrectUnitOnce_CompletesCountOne()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank", count: 1) });
        tracker.NotifyUnitDestroyed("enemy_tank");
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_PartialDestructions_StaysIncomplete()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank", count: 5) });
        tracker.NotifyUnitDestroyed("enemy_tank");
        tracker.NotifyUnitDestroyed("enemy_tank");
        Assert.False(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_ExactDestructions_Completes()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank", count: 3) });
        tracker.NotifyUnitDestroyed("enemy_tank");
        tracker.NotifyUnitDestroyed("enemy_tank");
        tracker.NotifyUnitDestroyed("enemy_tank");
        Assert.True(tracker.Objectives[0].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_ExtraDestructionsAfterComplete_NoStateChange()
    {
        var tracker = MakeTracker(new[] { MakeDestroyUnit("enemy_tank", count: 1) });
        tracker.NotifyUnitDestroyed("enemy_tank");
        Assert.True(tracker.Objectives[0].IsComplete);

        tracker.NotifyUnitDestroyed("enemy_tank");
        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.Objectives[0].IsFailed);
    }

    [Fact]
    public void DestroyUnitType_MultipleObjectives_EachTracksIndependently()
    {
        var obj1 = MakeDestroyUnit("scout", count: 1);
        var obj2 = MakeDestroyUnit("tank",  count: 2);
        var tracker = MakeTracker(new[] { obj1, obj2 });

        tracker.NotifyUnitDestroyed("scout");
        tracker.NotifyUnitDestroyed("tank");

        Assert.True(tracker.Objectives[0].IsComplete);
        Assert.False(tracker.Objectives[1].IsComplete);

        tracker.NotifyUnitDestroyed("tank");
        Assert.True(tracker.Objectives[1].IsComplete);
    }

    [Fact]
    public void DestroyUnitType_DoesNotAffectDestroyBuildingObjectives()
    {
        // NotifyUnitDestroyed must only advance DestroyUnitType objectives,
        // not DestroyBuildingType ones (and vice-versa).
        var unitObj  = MakeDestroyUnit("enemy_tank", count: 1);
        var bldgObj  = MakeDestroyBuilding("enemy_tank", count: 1); // same TargetId

        var tracker = MakeTracker(new[] { unitObj, bldgObj });
        tracker.NotifyUnitDestroyed("enemy_tank");

        Assert.True(tracker.Objectives[0].IsComplete);   // unit objective complete
        Assert.False(tracker.Objectives[1].IsComplete);  // building objective unaffected
    }
}
