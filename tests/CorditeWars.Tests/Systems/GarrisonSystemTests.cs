using CorditeWars.Core;
using CorditeWars.Systems.Garrison;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for <see cref="GarrisonSystem"/> and <see cref="GarrisonSlot"/>.
/// Uses the raw-parameter overload of RegisterBuilding to avoid
/// constructing Godot Node classes in the test runner.
/// </summary>
public class GarrisonSystemTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static void Register(GarrisonSystem sys, int buildingId, int ownerId = 1,
        int capacity = 4, int defenseBonus = 50)
        => sys.RegisterBuilding(buildingId, ownerId, capacity, defenseBonus);

    // ═══════════════════════════════════════════════════════════════════════
    // GarrisonSlot
    // ═══════════════════════════════════════════════════════════════════════

    [Fact] public void GarrisonSlot_StartsEmpty() {
        var slot = new GarrisonSlot { Capacity = 4 };
        Assert.Equal(0, slot.Count);
        Assert.True(slot.HasSpace);
    }

    [Fact] public void GarrisonSlot_Add_IncreasesCount() {
        var slot = new GarrisonSlot { Capacity = 4 };
        Assert.True(slot.Add(1));
        Assert.Equal(1, slot.Count);
    }

    [Fact] public void GarrisonSlot_Add_RespectsCapacity() {
        var slot = new GarrisonSlot { Capacity = 2 };
        slot.Add(1); slot.Add(2);
        Assert.False(slot.Add(3));
        Assert.Equal(2, slot.Count);
    }

    [Fact] public void GarrisonSlot_Add_DuplicateFails() {
        var slot = new GarrisonSlot { Capacity = 4 };
        slot.Add(1);
        Assert.False(slot.Add(1));
        Assert.Equal(1, slot.Count);
    }

    [Fact] public void GarrisonSlot_Remove_DecreasesCount() {
        var slot = new GarrisonSlot { Capacity = 4 };
        slot.Add(1);
        Assert.True(slot.Remove(1));
        Assert.Equal(0, slot.Count);
    }

    [Fact] public void GarrisonSlot_Remove_NonexistentReturnsFalse() {
        var slot = new GarrisonSlot { Capacity = 4 };
        Assert.False(slot.Remove(99));
    }

    [Fact] public void GarrisonSlot_HasSpace_FalseWhenFull() {
        var slot = new GarrisonSlot { Capacity = 1 };
        slot.Add(1);
        Assert.False(slot.HasSpace);
    }

    [Fact] public void GarrisonSlot_Clear_RemovesAll() {
        var slot = new GarrisonSlot { Capacity = 4 };
        slot.Add(1); slot.Add(2); slot.Add(3);
        slot.Clear();
        Assert.Equal(0, slot.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GarrisonSystem — RegisterBuilding
    // ═══════════════════════════════════════════════════════════════════════

    [Fact] public void RegisterBuilding_WithCapacity_CreatesSlot() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        Assert.NotNull(sys.GetGarrisonForBuilding(1));
        Assert.Equal(4, sys.GetGarrisonForBuilding(1)!.Capacity);
    }

    [Fact] public void RegisterBuilding_WithZeroCapacity_DoesNotCreateSlot() {
        var sys = new GarrisonSystem();
        sys.RegisterBuilding(1, 1, 0, 0);
        Assert.Null(sys.GetGarrisonForBuilding(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GarrisonSystem — TryGarrison / TryEject
    // ═══════════════════════════════════════════════════════════════════════

    [Fact] public void TryGarrison_SucceedsWhenSlotHasSpace() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        Assert.True(sys.TryGarrison(10, 1));
        Assert.True(sys.IsGarrisoned(10));
        Assert.Equal(1, sys.GetGarrisonBuilding(10));
    }

    [Fact] public void TryGarrison_FailsWhenFull() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 1);
        sys.TryGarrison(10, 1);
        Assert.False(sys.TryGarrison(11, 1));
    }

    [Fact] public void TryGarrison_FailsIfAlreadyGarrisoned() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        sys.TryGarrison(10, 1);
        Assert.False(sys.TryGarrison(10, 1));
    }

    [Fact] public void TryGarrison_FailsIfBuildingNotRegistered() {
        var sys = new GarrisonSystem();
        Assert.False(sys.TryGarrison(10, 999));
    }

    [Fact] public void TryEject_RemovesUnitFromGarrison() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        sys.TryGarrison(10, 1);
        Assert.True(sys.TryEject(10));
        Assert.False(sys.IsGarrisoned(10));
    }

    [Fact] public void TryEject_FailsIfNotGarrisoned() {
        var sys = new GarrisonSystem();
        Assert.False(sys.TryEject(99));
    }

    [Fact] public void AfterEject_SlotHasSpace_Again() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 1);
        sys.TryGarrison(10, 1);
        Assert.False(sys.GetGarrisonForBuilding(1)!.HasSpace);
        sys.TryEject(10);
        Assert.True(sys.GetGarrisonForBuilding(1)!.HasSpace);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GarrisonSystem — OnBuildingDestroyed
    // ═══════════════════════════════════════════════════════════════════════

    [Fact] public void OnBuildingDestroyed_EjectsAllOccupants() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        sys.TryGarrison(10, 1); sys.TryGarrison(11, 1);
        var ejected = sys.OnBuildingDestroyed(1);
        Assert.Equal(2, ejected.Count);
        Assert.Contains(10, ejected);
        Assert.Contains(11, ejected);
        Assert.False(sys.IsGarrisoned(10));
        Assert.False(sys.IsGarrisoned(11));
    }

    [Fact] public void OnBuildingDestroyed_RemovesGarrisonSlot() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 4);
        sys.OnBuildingDestroyed(1);
        Assert.Null(sys.GetGarrisonForBuilding(1));
    }

    [Fact] public void OnBuildingDestroyed_UnknownBuilding_ReturnsEmpty() {
        var sys = new GarrisonSystem();
        Assert.Empty(sys.OnBuildingDestroyed(999));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GarrisonSystem — GetDefenseMultiplier
    // ═══════════════════════════════════════════════════════════════════════

    [Fact] public void GetDefenseMultiplier_IsOne_WhenNotGarrisoned() {
        var sys = new GarrisonSystem();
        Assert.Equal(FixedPoint.One, sys.GetDefenseMultiplier(99));
    }

    [Theory]
    [InlineData(50,  0.5f)]
    [InlineData(0,   1.0f)]
    [InlineData(100, 0.0f)]
    [InlineData(25,  0.75f)]
    public void GetDefenseMultiplier_ReflectsDefenseBonus(int bonusPercent, float expectedMultiplier) {
        var sys = new GarrisonSystem();
        Register(sys, 1, defenseBonus: bonusPercent);
        sys.TryGarrison(10, 1);
        Assert.Equal(expectedMultiplier, sys.GetDefenseMultiplier(10).ToFloat(), precision: 2);
    }

    [Fact] public void GetDefenseMultiplier_IsOne_AfterEject() {
        var sys = new GarrisonSystem();
        Register(sys, 1, defenseBonus: 60);
        sys.TryGarrison(10, 1);
        sys.TryEject(10);
        Assert.Equal(FixedPoint.One, sys.GetDefenseMultiplier(10));
    }

    [Fact] public void MultipleBuildings_TrackSeparately() {
        var sys = new GarrisonSystem();
        Register(sys, 1, capacity: 2);
        Register(sys, 2, capacity: 2);
        sys.TryGarrison(10, 1);
        sys.TryGarrison(11, 2);
        Assert.Equal(1, sys.GetGarrisonBuilding(10));
        Assert.Equal(2, sys.GetGarrisonBuilding(11));
    }
}
