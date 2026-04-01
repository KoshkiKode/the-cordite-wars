using System;
using System.Collections.Generic;
using UnnamedRTS.Core;
using UnnamedRTS.Systems.Pathfinding;

namespace UnnamedRTS.Systems.Networking;

// ═══════════════════════════════════════════════════════════════════════════
// COMMAND SYSTEM — Deterministic command queue for lockstep networking
// ═══════════════════════════════════════════════════════════════════════════
//
// In lockstep networking (used by C&C, StarCraft, AoE), clients do NOT
// send unit positions or game state over the network. Instead:
//
//   1. Each client captures player input as COMMANDS (move here, attack
//      that, build this).
//   2. Commands are timestamped with the simulation tick they should
//      execute on (usually current tick + input delay).
//   3. All clients exchange commands and execute them on the SAME tick.
//   4. Because the simulation is deterministic (FixedPoint math, seeded
//      RNG, sorted iteration), all clients produce identical results.
//
// This means:
//   - Network bandwidth is tiny (just commands, not positions of 200 units)
//   - Cheating is harder (clients validate each other's state via checksums)
//   - Replay is trivial (just replay the command stream)
//
// DETERMINISM REQUIREMENTS FOR THIS FILE:
//   - GetCommandsForTick must return commands in a DETERMINISTIC order:
//     sorted by PlayerId, then by CommandType ordinal, then by insertion
//     order. We use SortedList<ulong, List<GameCommand>> keyed by tick,
//     and sort within each tick's list explicitly.
//   - No Dictionary iteration. No HashSet. No LINQ that could reorder.
//   - All positions are FixedVector2. No float/double.
//
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Enumerates all command types. The integer values define deterministic
/// sort order when multiple commands execute on the same tick for the
/// same player. Stop and Hold execute before Move to prevent contradictions.
/// </summary>
public enum CommandType
{
    Stop = 0,
    HoldPosition = 1,
    Move = 2,
    AttackMove = 3,
    Attack = 4,
    Patrol = 5
}

/// <summary>
/// Context provided to commands during execution. Gives commands access
/// to the game systems they need to modify deterministically.
/// </summary>
public struct GameCommandContext
{
    /// <summary>The terrain grid for pathfinding and terrain queries.</summary>
    public TerrainGrid Terrain;

    /// <summary>Current simulation tick.</summary>
    public ulong CurrentTick;

    /// <summary>
    /// Callback to look up a unit's current state by ID. Returns null if
    /// the unit no longer exists (destroyed). We use a delegate rather
    /// than exposing the full unit registry to limit command scope.
    /// </summary>
    public Func<int, UnitCommandView?> GetUnit;

    /// <summary>
    /// Callback to issue movement orders to units. The command system
    /// tells the movement/AI system what to do — it doesn't move units
    /// directly.
    /// </summary>
    public Action<int, UnitOrder> IssueOrder;

    /// <summary>The deterministic RNG for any randomness commands need.</summary>
    public DeterministicRng Rng;
}

/// <summary>
/// Read-only view of a unit's state, provided to commands for decision-making.
/// Commands should not mutate unit state directly — they issue orders via
/// the context's IssueOrder callback.
/// </summary>
public struct UnitCommandView
{
    /// <summary>Unique unit identifier.</summary>
    public int UnitId;

    /// <summary>Owning player ID.</summary>
    public int PlayerId;

    /// <summary>Current world position.</summary>
    public FixedVector2 Position;

    /// <summary>Current movement state.</summary>
    public MovementState Movement;

    /// <summary>Whether the unit is alive.</summary>
    public bool IsAlive;
}

/// <summary>
/// An order issued to a unit by the command system. The unit's AI/movement
/// layer reads this and acts accordingly.
/// </summary>
public struct UnitOrder
{
    /// <summary>What kind of order this is.</summary>
    public UnitOrderType Type;

    /// <summary>Target position for move/attack-move/patrol orders.</summary>
    public FixedVector2 TargetPosition;

    /// <summary>Target unit ID for attack orders. -1 if not targeting a unit.</summary>
    public int TargetUnitId;

    /// <summary>
    /// Patrol waypoints for patrol orders. Null for non-patrol orders.
    /// Stored as a list so the unit can cycle through them.
    /// </summary>
    public List<FixedVector2>? PatrolWaypoints;
}

/// <summary>
/// Types of orders that can be issued to units.
/// </summary>
public enum UnitOrderType
{
    Stop,
    Move,
    AttackMove,
    Attack,
    Patrol,
    HoldPosition
}

// ═══════════════════════════════════════════════════════════════════════════
// GAME COMMANDS — Abstract base + concrete implementations
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstract base for all game commands. A command is an atomic player
/// action that executes deterministically on a specific simulation tick.
/// </summary>
public abstract class GameCommand
{
    /// <summary>
    /// The simulation tick this command executes on. Set by the networking
    /// layer (usually currentTick + inputDelay). All clients must agree.
    /// </summary>
    public ulong ScheduledTick { get; set; }

    /// <summary>
    /// The player who issued this command. Used for ownership validation
    /// and deterministic sort order.
    /// </summary>
    public int PlayerId { get; set; }

    /// <summary>
    /// The type of command. Used for deterministic sorting — commands of
    /// different types for the same player on the same tick execute in
    /// CommandType enum order.
    /// </summary>
    public abstract CommandType Type { get; }

    /// <summary>
    /// Insertion sequence number assigned by CommandBuffer.AddCommand.
    /// Breaks ties when PlayerId and Type are equal — ensures FIFO order
    /// for identical command types from the same player on the same tick.
    /// </summary>
    internal long InsertionOrder { get; set; }

    /// <summary>
    /// Execute this command deterministically. All side effects must flow
    /// through the GameCommandContext callbacks (IssueOrder, etc.).
    /// NO random access to global state. NO float math. NO allocation
    /// of non-deterministic collections.
    /// </summary>
    public abstract void Execute(GameCommandContext ctx);
}

// ── Concrete Commands ───────────────────────────────────────────────────

/// <summary>
/// Orders selected units to move to a target position. The most common
/// command in any RTS — right-click on terrain.
/// </summary>
public class MoveCommand : GameCommand
{
    public override CommandType Type => CommandType.Move;

    /// <summary>Target world position to move to (FixedVector2).</summary>
    public FixedVector2 TargetPosition { get; set; }

    /// <summary>
    /// IDs of the units to move. Stored as a sorted list to guarantee
    /// deterministic iteration order.
    /// </summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        // Sort unit IDs for deterministic processing order
        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;

            // Validate ownership — only the owning player can command a unit
            if (unit.Value.PlayerId != PlayerId) continue;

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.Move,
                TargetPosition = TargetPosition,
                TargetUnitId = -1,
                PatrolWaypoints = null
            });
        }
    }
}

/// <summary>
/// Orders units to move to a target position, attacking any enemies
/// encountered along the way. The units will engage and then resume
/// moving once the threat is eliminated.
/// </summary>
public class AttackMoveCommand : GameCommand
{
    public override CommandType Type => CommandType.AttackMove;

    /// <summary>Target world position (FixedVector2).</summary>
    public FixedVector2 TargetPosition { get; set; }

    /// <summary>IDs of the units to attack-move.</summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;
            if (unit.Value.PlayerId != PlayerId) continue;

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.AttackMove,
                TargetPosition = TargetPosition,
                TargetUnitId = -1,
                PatrolWaypoints = null
            });
        }
    }
}

/// <summary>
/// Orders units to stop immediately. Clears any current orders and
/// applies braking. Units will halt in place.
/// </summary>
public class StopCommand : GameCommand
{
    public override CommandType Type => CommandType.Stop;

    /// <summary>IDs of the units to stop.</summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;
            if (unit.Value.PlayerId != PlayerId) continue;

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.Stop,
                TargetPosition = FixedVector2.Zero,
                TargetUnitId = -1,
                PatrolWaypoints = null
            });
        }
    }
}

/// <summary>
/// Orders units to attack a specific target unit. Units will pursue
/// the target and engage when in range. If the target dies or becomes
/// invalid, units revert to idle.
/// </summary>
public class AttackCommand : GameCommand
{
    public override CommandType Type => CommandType.Attack;

    /// <summary>The ID of the unit to attack.</summary>
    public int TargetUnitId { get; set; }

    /// <summary>IDs of the attacking units.</summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        // Validate target exists
        var target = ctx.GetUnit(TargetUnitId);
        if (target == null || !target.Value.IsAlive)
            return; // Target gone — command is a no-op

        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;
            if (unit.Value.PlayerId != PlayerId) continue;

            // Don't attack yourself
            if (unitId == TargetUnitId) continue;

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.Attack,
                TargetPosition = target.Value.Position, // Initial chase position
                TargetUnitId = TargetUnitId,
                PatrolWaypoints = null
            });
        }
    }
}

/// <summary>
/// Orders units to patrol between a list of waypoints. Units cycle
/// through the waypoints indefinitely and will engage enemies they
/// encounter along the route (like attack-move, but looping).
/// </summary>
public class PatrolCommand : GameCommand
{
    public override CommandType Type => CommandType.Patrol;

    /// <summary>
    /// Ordered list of patrol waypoints (FixedVector2). Units cycle
    /// through these in order, looping back to the first after the last.
    /// </summary>
    public List<FixedVector2> Waypoints { get; set; } = new();

    /// <summary>IDs of the patrolling units.</summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        if (Waypoints.Count == 0) return;

        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;
            if (unit.Value.PlayerId != PlayerId) continue;

            // Create a copy of the waypoint list for this unit
            // (each unit tracks its own patrol progress independently)
            var waypointsCopy = new List<FixedVector2>(Waypoints.Count);
            for (int w = 0; w < Waypoints.Count; w++)
                waypointsCopy.Add(Waypoints[w]);

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.Patrol,
                TargetPosition = Waypoints[0],
                TargetUnitId = -1,
                PatrolWaypoints = waypointsCopy
            });
        }
    }
}

/// <summary>
/// Orders units to hold their current position. Units will not move
/// but will still attack enemies that enter their weapon range.
/// Distinct from Stop: held units don't chase fleeing enemies.
/// </summary>
public class HoldPositionCommand : GameCommand
{
    public override CommandType Type => CommandType.HoldPosition;

    /// <summary>IDs of the units to hold position.</summary>
    public List<int> UnitIds { get; set; } = new();

    public override void Execute(GameCommandContext ctx)
    {
        UnitIds.Sort();

        for (int i = 0; i < UnitIds.Count; i++)
        {
            int unitId = UnitIds[i];
            var unit = ctx.GetUnit(unitId);
            if (unit == null) continue;
            if (unit.Value.PlayerId != PlayerId) continue;

            ctx.IssueOrder(unitId, new UnitOrder
            {
                Type = UnitOrderType.HoldPosition,
                TargetPosition = unit.Value.Position, // Hold at current position
                TargetUnitId = -1,
                PatrolWaypoints = null
            });
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// COMMAND BUFFER — Tick-indexed deterministic command queue
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Stores and retrieves game commands indexed by simulation tick. This is
/// the central command queue for lockstep networking.
///
/// Thread safety: NOT thread-safe. All access must be from the simulation
/// thread. Network receive threads should queue commands through a
/// thread-safe handoff (not this class's responsibility).
///
/// Storage: SortedList&lt;ulong, List&lt;GameCommand&gt;&gt; keyed by tick number.
/// SortedList uses an internal array (not a hash table), so iteration
/// order is deterministic by key. Within each tick, commands are sorted
/// explicitly by (PlayerId, CommandType, InsertionOrder).
/// </summary>
public class CommandBuffer
{
    /// <summary>
    /// Commands indexed by the tick they execute on. SortedList guarantees
    /// deterministic key ordering (ascending tick number).
    /// </summary>
    private readonly SortedList<ulong, List<GameCommand>> _commandsByTick = new();

    /// <summary>
    /// Monotonically increasing insertion counter. Used to break ties in
    /// sort order — when two commands have the same PlayerId and Type,
    /// they execute in the order they were added (FIFO).
    /// </summary>
    private long _insertionCounter;

    /// <summary>
    /// Adds a command to the buffer. The command's ScheduledTick determines
    /// when it will execute. Commands can be added for any future tick.
    /// Adding commands for past ticks is allowed (for late arrivals) but
    /// the caller is responsible for handling desync if the tick already ran.
    /// </summary>
    public void AddCommand(GameCommand cmd)
    {
        cmd.InsertionOrder = _insertionCounter++;

        if (!_commandsByTick.TryGetValue(cmd.ScheduledTick, out var list))
        {
            list = new List<GameCommand>(4); // small initial capacity
            _commandsByTick.Add(cmd.ScheduledTick, list);
        }

        list.Add(cmd);
    }

    /// <summary>
    /// Returns all commands scheduled for the given tick, sorted in
    /// deterministic execution order:
    ///   1. By PlayerId (ascending)
    ///   2. By CommandType enum value (ascending — Stop before Move, etc.)
    ///   3. By insertion order (ascending — FIFO for same player + type)
    ///
    /// Returns an empty list if no commands exist for this tick.
    /// The returned list is a NEW list — callers may consume it freely.
    ///
    /// After retrieval, the tick's commands are removed from the buffer
    /// to prevent unbounded memory growth.
    /// </summary>
    public List<GameCommand> GetCommandsForTick(ulong tick)
    {
        if (!_commandsByTick.TryGetValue(tick, out var commands))
            return new List<GameCommand>(0);

        // Sort deterministically: PlayerId → CommandType → InsertionOrder
        // Using a stable comparison chain. No LINQ — explicit comparisons.
        commands.Sort((a, b) =>
        {
            int cmp = a.PlayerId.CompareTo(b.PlayerId);
            if (cmp != 0) return cmp;

            cmp = ((int)a.Type).CompareTo((int)b.Type);
            if (cmp != 0) return cmp;

            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        // Remove from buffer to free memory for past ticks
        _commandsByTick.Remove(tick);

        return commands;
    }

    /// <summary>
    /// Peeks at commands for a tick without removing them.
    /// Returns an empty list if no commands exist.
    /// </summary>
    public List<GameCommand> PeekCommandsForTick(ulong tick)
    {
        if (!_commandsByTick.TryGetValue(tick, out var commands))
            return new List<GameCommand>(0);

        // Return a sorted copy without removing from buffer
        var copy = new List<GameCommand>(commands);
        copy.Sort((a, b) =>
        {
            int cmp = a.PlayerId.CompareTo(b.PlayerId);
            if (cmp != 0) return cmp;

            cmp = ((int)a.Type).CompareTo((int)b.Type);
            if (cmp != 0) return cmp;

            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        return copy;
    }

    /// <summary>
    /// Returns true if there are any commands scheduled for the given tick.
    /// </summary>
    public bool HasCommandsForTick(ulong tick)
    {
        return _commandsByTick.ContainsKey(tick) && _commandsByTick[tick].Count > 0;
    }

    /// <summary>
    /// Returns the total number of pending commands across all ticks.
    /// Useful for debugging and network diagnostics.
    /// </summary>
    public int TotalPendingCommands
    {
        get
        {
            int total = 0;
            // SortedList iteration is deterministic (by key order)
            for (int i = 0; i < _commandsByTick.Count; i++)
            {
                total += _commandsByTick.Values[i].Count;
            }
            return total;
        }
    }

    /// <summary>
    /// Clears all pending commands. Used when starting a new match or
    /// resetting state.
    /// </summary>
    public void Clear()
    {
        _commandsByTick.Clear();
        _insertionCounter = 0;
    }

    /// <summary>
    /// Removes all commands for ticks before the given tick. Used to
    /// garbage-collect commands that can no longer execute (e.g., after
    /// a confirmed sync point in networking).
    /// </summary>
    public void PurgeBefore(ulong tick)
    {
        // Collect keys to remove (can't modify during iteration)
        var keysToRemove = new List<ulong>();
        for (int i = 0; i < _commandsByTick.Count; i++)
        {
            ulong key = _commandsByTick.Keys[i];
            if (key < tick)
                keysToRemove.Add(key);
            else
                break; // SortedList is ordered — no more keys < tick
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            _commandsByTick.Remove(keysToRemove[i]);
        }
    }
}
