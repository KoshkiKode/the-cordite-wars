using CorditeWars.Core;
using CorditeWars.Systems.FogOfWar;

namespace CorditeWars.Tests.Game.FogOfWar;

/// <summary>
/// Tests for FogSnapshot — per-player last-known enemy state (ghost entities).
/// Covers the building-ghost lifecycle, unit ghost non-persistence,
/// destroy cleanup, and GetVisibleGhosts filtering.
/// </summary>
public class FogSnapshotTests
{
    private static readonly FixedVector2 SomePos =
        new(FixedPoint.FromInt(5), FixedPoint.FromInt(5));

    private static readonly FixedPoint FullHealth = FixedPoint.One;
    private const ulong Tick0 = 0UL;

    // ── OnEntityBecameHidden — buildings ───────────────────────────────────

    [Fact]
    public void OnEntityBecameHidden_Building_CreatesGhost()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(1, SomePos, "barracks", FullHealth, isBuilding: true, Tick0);

        Assert.True(snap.GhostedEntities.ContainsKey(1));
        var ghost = snap.GhostedEntities[1];
        Assert.Equal(1, ghost.EntityId);
        Assert.Equal("barracks", ghost.EntityTypeId);
        Assert.Equal(SomePos, ghost.Position);
        Assert.Equal(FullHealth, ghost.HealthPercent);
        Assert.Equal(Tick0, ghost.LastSeenTick);
        Assert.True(ghost.IsBuilding);
    }

    [Fact]
    public void OnEntityBecameHidden_BuildingWithPlayerId_RecordsPlayerId()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(2, playerId: 3, SomePos, "factory", FullHealth,
            isBuilding: true, Tick0);

        var ghost = snap.GhostedEntities[2];
        Assert.Equal(3, ghost.PlayerId);
    }

    [Fact]
    public void OnEntityBecameHidden_Building_UpdatesExistingGhost()
    {
        var snap = new FogSnapshot();
        var pos1 = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        var pos2 = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        FixedPoint half = FixedPoint.One / FixedPoint.FromInt(2);

        snap.OnEntityBecameHidden(1, pos1, "barracks", FullHealth, isBuilding: true, 0UL);
        snap.OnEntityBecameHidden(1, pos2, "barracks", half, isBuilding: true, 42UL);

        // Latest call should win
        Assert.Equal(pos2, snap.GhostedEntities[1].Position);
        Assert.Equal(half, snap.GhostedEntities[1].HealthPercent);
        Assert.Equal(42UL, snap.GhostedEntities[1].LastSeenTick);
    }

    // ── OnEntityBecameHidden — units ───────────────────────────────────────

    [Fact]
    public void OnEntityBecameHidden_Unit_DoesNotCreateGhost()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(10, SomePos, "tank", FullHealth, isBuilding: false, Tick0);

        Assert.False(snap.GhostedEntities.ContainsKey(10));
    }

    [Fact]
    public void OnEntityBecameHidden_Unit_RemovesExistingGhost()
    {
        // First record a building ghost, then call with isBuilding=false
        // (edge-case: entity type changed or ghost was stale)
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(5, SomePos, "some_entity", FullHealth, isBuilding: true, Tick0);
        Assert.True(snap.GhostedEntities.ContainsKey(5));

        snap.OnEntityBecameHidden(5, SomePos, "some_entity", FullHealth, isBuilding: false, 1UL);
        Assert.False(snap.GhostedEntities.ContainsKey(5));
    }

    [Fact]
    public void OnEntityBecameHidden_UnitWithPlayerId_DoesNotCreateGhost()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(20, playerId: 2, SomePos, "ranger", FullHealth,
            isBuilding: false, Tick0);

        Assert.False(snap.GhostedEntities.ContainsKey(20));
    }

    // ── OnEntityBecameVisible ──────────────────────────────────────────────

    [Fact]
    public void OnEntityBecameVisible_RemovesGhost()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(1, SomePos, "barracks", FullHealth, isBuilding: true, Tick0);
        Assert.True(snap.GhostedEntities.ContainsKey(1));

        snap.OnEntityBecameVisible(1);
        Assert.False(snap.GhostedEntities.ContainsKey(1));
    }

    [Fact]
    public void OnEntityBecameVisible_NonExistentGhost_DoesNotThrow()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameVisible(999); // should not throw
        Assert.Empty(snap.GhostedEntities);
    }

    // ── OnEntityDestroyed ──────────────────────────────────────────────────

    [Fact]
    public void OnEntityDestroyed_RemovesGhost()
    {
        var snap = new FogSnapshot();
        snap.OnEntityBecameHidden(3, SomePos, "power_plant", FullHealth, isBuilding: true, Tick0);
        snap.OnEntityDestroyed(3);

        Assert.False(snap.GhostedEntities.ContainsKey(3));
    }

    [Fact]
    public void OnEntityDestroyed_NonExistentGhost_DoesNotThrow()
    {
        var snap = new FogSnapshot();
        snap.OnEntityDestroyed(999); // should not throw
        Assert.Empty(snap.GhostedEntities);
    }

    // ── GetVisibleGhosts ───────────────────────────────────────────────────

    [Fact]
    public void GetVisibleGhosts_ExploredCell_ReturnsGhost()
    {
        var snap = new FogSnapshot();
        // Ghost at grid cell (5, 5)
        var pos = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        snap.OnEntityBecameHidden(1, pos, "barracks", FullHealth, isBuilding: true, Tick0);

        // Fog grid with cell (5,5) = Explored
        var fog = new FogGrid(16, 16, 1, FogMode.Campaign);
        fog.AddVisibility(5, 5);    // → Visible
        fog.RemoveVisibility(5, 5); // → Explored

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Single(ghosts);
        Assert.Equal(1, ghosts[0].EntityId);
    }

    [Fact]
    public void GetVisibleGhosts_VisibleCell_DoesNotReturnGhost()
    {
        var snap = new FogSnapshot();
        var pos = new FixedVector2(FixedPoint.FromInt(3), FixedPoint.FromInt(3));
        snap.OnEntityBecameHidden(2, pos, "factory", FullHealth, isBuilding: true, Tick0);

        // Cell (3,3) is currently Visible
        var fog = new FogGrid(16, 16, 1, FogMode.Campaign);
        fog.AddVisibility(3, 3);

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Empty(ghosts);
    }

    [Fact]
    public void GetVisibleGhosts_UnexploredCell_DoesNotReturnGhost()
    {
        var snap = new FogSnapshot();
        var pos = new FixedVector2(FixedPoint.FromInt(7), FixedPoint.FromInt(7));
        snap.OnEntityBecameHidden(3, pos, "refinery", FullHealth, isBuilding: true, Tick0);

        // Cell (7,7) is Unexplored
        var fog = new FogGrid(16, 16, 1, FogMode.Campaign);

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Empty(ghosts);
    }

    [Fact]
    public void GetVisibleGhosts_MultipleGhosts_FiltersCorrectly()
    {
        var snap = new FogSnapshot();
        var fog = new FogGrid(16, 16, 1, FogMode.Campaign);

        // Ghost 1 → Explored cell
        var pos1 = new FixedVector2(FixedPoint.FromInt(2), FixedPoint.FromInt(2));
        snap.OnEntityBecameHidden(1, pos1, "barracks", FullHealth, isBuilding: true, Tick0);
        fog.AddVisibility(2, 2);
        fog.RemoveVisibility(2, 2); // Explored

        // Ghost 2 → Visible cell (should be hidden — real entity visible)
        var pos2 = new FixedVector2(FixedPoint.FromInt(4), FixedPoint.FromInt(4));
        snap.OnEntityBecameHidden(2, pos2, "factory", FullHealth, isBuilding: true, Tick0);
        fog.AddVisibility(4, 4); // stays Visible

        // Ghost 3 → Unexplored cell
        var pos3 = new FixedVector2(FixedPoint.FromInt(10), FixedPoint.FromInt(10));
        snap.OnEntityBecameHidden(3, pos3, "silo", FullHealth, isBuilding: true, Tick0);
        // (10,10) left Unexplored

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Single(ghosts);
        Assert.Equal(1, ghosts[0].EntityId);
    }

    [Fact]
    public void GetVisibleGhosts_EmptySnapshot_ReturnsEmpty()
    {
        var snap = new FogSnapshot();
        var fog = new FogGrid(8, 8, 1, FogMode.Campaign);

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Empty(ghosts);
    }

    [Fact]
    public void GetVisibleGhosts_AfterEntityBecameVisible_ReturnsEmpty()
    {
        var snap = new FogSnapshot();
        var fog = new FogGrid(16, 16, 1, FogMode.Campaign);

        var pos = new FixedVector2(FixedPoint.FromInt(5), FixedPoint.FromInt(5));
        snap.OnEntityBecameHidden(1, pos, "barracks", FullHealth, isBuilding: true, Tick0);
        fog.AddVisibility(5, 5);
        fog.RemoveVisibility(5, 5); // Explored

        // Now the real entity becomes visible — ghost removed
        snap.OnEntityBecameVisible(1);

        var ghosts = snap.GetVisibleGhosts(fog);
        Assert.Empty(ghosts);
    }
}
