using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.FogOfWar;

// ─────────────────────────────────────────────────────────────────────────────
//  GhostedEntity
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A snapshot of an enemy entity as it was last seen by this player.
/// Buildings persist as ghosts until re-scouted; mobile units are removed
/// immediately when they leave vision (they likely moved — showing a stale
/// position is misleading, matching classic C&amp;C behaviour).
/// </summary>
public struct GhostedEntity
{
    /// <summary>Original entity ID in the simulation.</summary>
    public int EntityId;

    /// <summary>Owner player ID of the ghosted entity.</summary>
    public int PlayerId;

    /// <summary>World position where the entity was last seen.</summary>
    public FixedVector2 Position;

    /// <summary>Type identifier (e.g. "tank", "barracks") for rendering the ghost.</summary>
    public string EntityTypeId;

    /// <summary>Last observed health as a percentage (0–1 in fixed point).</summary>
    public FixedPoint HealthPercent;

    /// <summary>Simulation tick when the entity was last visible.</summary>
    public ulong LastSeenTick;

    /// <summary>
    /// True for buildings (ghosts persist), false for mobile units (ghosts
    /// are discarded when the entity leaves vision).
    /// </summary>
    public bool IsBuilding;
}

// ─────────────────────────────────────────────────────────────────────────────
//  FogSnapshot
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-player snapshot of "last-known" enemy information in explored-but-not-visible
/// areas. Updated deterministically each tick (same order, same data on all clients)
/// so it stays in sync for lockstep multiplayer.
/// </summary>
public class FogSnapshot
{
    /// <summary>
    /// All currently stored ghost entities, keyed by their entity ID.
    /// </summary>
    public Dictionary<int, GhostedEntity> GhostedEntities { get; } = new();

    // ── Visibility Transition Callbacks ──────────────────────────────────

    /// <summary>
    /// Called when an enemy entity enters this player's vision.
    /// The real entity is now visible, so any ghost for it should be removed —
    /// the player sees the real thing instead.
    /// </summary>
    /// <param name="entityId">The entity that just became visible.</param>
    public void OnEntityBecameVisible(int entityId)
    {
        GhostedEntities.Remove(entityId);
    }

    /// <summary>
    /// Called when an enemy entity leaves this player's vision.
    /// <list type="bullet">
    ///   <item><b>Buildings</b>: a ghost is created/updated and persists until
    ///         the cell is re-scouted.</item>
    ///   <item><b>Units</b>: no ghost is stored — the unit likely moved, and
    ///         displaying a stale position would be misleading.</item>
    /// </list>
    /// </summary>
    public void OnEntityBecameHidden(
        int entityId,
        FixedVector2 lastPos,
        string typeId,
        FixedPoint health,
        bool isBuilding,
        ulong tick)
    {
        if (!isBuilding)
        {
            // Mobile units: discard any existing ghost and do NOT create a new one.
            GhostedEntities.Remove(entityId);
            return;
        }

        // Buildings: store (or update) a ghost
        GhostedEntities[entityId] = new GhostedEntity
        {
            EntityId = entityId,
            PlayerId = -1, // Will be set by caller if needed; kept lightweight
            Position = lastPos,
            EntityTypeId = typeId,
            HealthPercent = health,
            LastSeenTick = tick,
            IsBuilding = true
        };
    }

    /// <summary>
    /// Overload that also records the owning player ID of the ghosted entity.
    /// </summary>
    public void OnEntityBecameHidden(
        int entityId,
        int playerId,
        FixedVector2 lastPos,
        string typeId,
        FixedPoint health,
        bool isBuilding,
        ulong tick)
    {
        if (!isBuilding)
        {
            GhostedEntities.Remove(entityId);
            return;
        }

        GhostedEntities[entityId] = new GhostedEntity
        {
            EntityId = entityId,
            PlayerId = playerId,
            Position = lastPos,
            EntityTypeId = typeId,
            HealthPercent = health,
            LastSeenTick = tick,
            IsBuilding = true
        };
    }

    /// <summary>
    /// Called when an entity is destroyed (by any means). Removes the ghost
    /// if one exists — the building/unit no longer exists in the world.
    /// </summary>
    /// <param name="entityId">The destroyed entity.</param>
    public void OnEntityDestroyed(int entityId)
    {
        GhostedEntities.Remove(entityId);
    }

    // ── Queries ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all ghosts that are located in cells the player has explored
    /// but does NOT currently have vision on. These are the entities that
    /// should be rendered as grey/transparent "last-known" icons.
    /// </summary>
    /// <param name="fog">The player's fog grid for visibility checks.</param>
    /// <returns>List of ghosts in explored-but-not-visible cells.</returns>
    public List<GhostedEntity> GetVisibleGhosts(FogGrid fog)
    {
        var result = new List<GhostedEntity>();

        foreach (var kvp in GhostedEntities)
        {
            GhostedEntity ghost = kvp.Value;

            // Convert ghost world position to grid cell
            // We use a simple conversion: position components → integer cell coords.
            // This matches the convention where world position maps 1:1 to grid cells.
            int gx = ghost.Position.X.ToInt();
            int gy = ghost.Position.Y.ToInt();

            FogVisibility vis = fog.GetVisibility(gx, gy);

            // Only show ghosts in explored (shrouded) cells — NOT in visible
            // cells (the real entity is shown there) and not in unexplored cells
            // (the player has never seen this area).
            if (vis == FogVisibility.Explored)
            {
                result.Add(ghost);
            }
        }

        return result;
    }
}
