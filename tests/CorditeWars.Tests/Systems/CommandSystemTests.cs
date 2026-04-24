using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Networking;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Tests.Systems;

public class CommandSystemTests
{
    private static FixedVector2 Vec(int x, int y) =>
        new(FixedPoint.FromInt(x), FixedPoint.FromInt(y));

    private static UnitCommandView Unit(int id, int playerId, int x, int y, bool isAlive = true) =>
        new()
        {
            UnitId = id,
            PlayerId = playerId,
            Position = Vec(x, y),
            IsAlive = isAlive
        };

    private static GameCommandContext BuildContext(
        Dictionary<int, UnitCommandView> units,
        List<(int UnitId, UnitOrder Order)> issued)
    {
        return new GameCommandContext
        {
            GetUnit = id => units.TryGetValue(id, out var unit) ? unit : null,
            IssueOrder = (unitId, order) => issued.Add((unitId, order)),
            Terrain = null!,
            CurrentTick = 0,
            Rng = new DeterministicRng(1)
        };
    }

    [Fact]
    public void MoveCommand_Execute_SortsIdsAndSkipsMissingOrEnemyUnits()
    {
        var units = new Dictionary<int, UnitCommandView>
        {
            [10] = Unit(10, playerId: 1, x: 3, y: 4),
            [2] = Unit(2, playerId: 1, x: 1, y: 2),
            [7] = Unit(7, playerId: 2, x: 9, y: 9)
        };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);
        var target = Vec(25, 30);

        var cmd = new MoveCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 10, 999, 7, 2 },
            TargetPosition = target
        };

        cmd.Execute(ctx);

        Assert.Equal(2, issued.Count);
        Assert.Collection(
            issued,
            first =>
            {
                Assert.Equal(2, first.UnitId);
                Assert.Equal(UnitOrderType.Move, first.Order.Type);
                Assert.Equal(target, first.Order.TargetPosition);
                Assert.Equal(-1, first.Order.TargetUnitId);
                Assert.Null(first.Order.PatrolWaypoints);
            },
            second =>
            {
                Assert.Equal(10, second.UnitId);
                Assert.Equal(UnitOrderType.Move, second.Order.Type);
                Assert.Equal(target, second.Order.TargetPosition);
            });
    }

    [Fact]
    public void AttackCommand_Execute_NoOrdersWhenTargetMissingOrDead()
    {
        var attacker = Unit(1, playerId: 4, x: 10, y: 10);
        var unitsMissingTarget = new Dictionary<int, UnitCommandView> { [1] = attacker };
        var issuedMissing = new List<(int UnitId, UnitOrder Order)>();
        var missingCtx = BuildContext(unitsMissingTarget, issuedMissing);

        new AttackCommand
        {
            PlayerId = 4,
            UnitIds = new List<int> { 1 },
            TargetUnitId = 404
        }.Execute(missingCtx);

        Assert.Empty(issuedMissing);

        var unitsDeadTarget = new Dictionary<int, UnitCommandView>
        {
            [1] = attacker,
            [2] = Unit(2, playerId: 8, x: 4, y: 5, isAlive: false)
        };
        var issuedDead = new List<(int UnitId, UnitOrder Order)>();
        var deadCtx = BuildContext(unitsDeadTarget, issuedDead);

        new AttackCommand
        {
            PlayerId = 4,
            UnitIds = new List<int> { 1 },
            TargetUnitId = 2
        }.Execute(deadCtx);

        Assert.Empty(issuedDead);
    }

    [Fact]
    public void AttackCommand_Execute_SkipsSelfAndEnemyUnitsAndUsesTargetPosition()
    {
        var target = Unit(9, playerId: 2, x: 42, y: 99, isAlive: true);
        var units = new Dictionary<int, UnitCommandView>
        {
            [9] = target,
            [3] = Unit(3, playerId: 1, x: 1, y: 1),
            [7] = Unit(7, playerId: 1, x: 2, y: 2),
            [6] = Unit(6, playerId: 2, x: 3, y: 3)
        };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);

        new AttackCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 9, 6, 7, 3 },
            TargetUnitId = 9
        }.Execute(ctx);

        Assert.Equal(2, issued.Count);
        Assert.Collection(
            issued,
            first =>
            {
                Assert.Equal(3, first.UnitId);
                Assert.Equal(UnitOrderType.Attack, first.Order.Type);
                Assert.Equal(9, first.Order.TargetUnitId);
                Assert.Equal(target.Position, first.Order.TargetPosition);
            },
            second =>
            {
                Assert.Equal(7, second.UnitId);
                Assert.Equal(UnitOrderType.Attack, second.Order.Type);
                Assert.Equal(9, second.Order.TargetUnitId);
                Assert.Equal(target.Position, second.Order.TargetPosition);
            });
    }

    [Fact]
    public void PatrolCommand_Execute_EmptyWaypointsProducesNoOrders()
    {
        var units = new Dictionary<int, UnitCommandView> { [1] = Unit(1, playerId: 1, x: 0, y: 0) };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);

        new PatrolCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 1 },
            Waypoints = new List<FixedVector2>()
        }.Execute(ctx);

        Assert.Empty(issued);
    }

    [Fact]
    public void PatrolCommand_Execute_CopiesWaypointListPerOrder()
    {
        var units = new Dictionary<int, UnitCommandView> { [1] = Unit(1, playerId: 1, x: 0, y: 0) };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);
        var waypoints = new List<FixedVector2> { Vec(1, 2), Vec(3, 4) };

        new PatrolCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 1 },
            Waypoints = waypoints
        }.Execute(ctx);

        Assert.Single(issued);
        var order = issued[0].Order;
        Assert.Equal(UnitOrderType.Patrol, order.Type);
        Assert.Equal(waypoints[0], order.TargetPosition);
        Assert.NotNull(order.PatrolWaypoints);
        Assert.Equal(waypoints, order.PatrolWaypoints);
        Assert.NotSame(waypoints, order.PatrolWaypoints);
    }

    [Fact]
    public void HoldPositionAndSetStance_UseCurrentUnitPositionAndSelectedStance()
    {
        var heldUnit = Unit(5, playerId: 1, x: 12, y: 14);
        var stanceUnit = Unit(8, playerId: 1, x: 30, y: 31);
        var units = new Dictionary<int, UnitCommandView>
        {
            [5] = heldUnit,
            [8] = stanceUnit
        };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);

        new HoldPositionCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 5 }
        }.Execute(ctx);

        new SetStanceCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 8 },
            Stance = UnitStance.Defensive
        }.Execute(ctx);

        Assert.Equal(2, issued.Count);
        Assert.Equal(UnitOrderType.HoldPosition, issued[0].Order.Type);
        Assert.Equal(heldUnit.Position, issued[0].Order.TargetPosition);
        Assert.Equal(UnitOrderType.SetStance, issued[1].Order.Type);
        Assert.Equal(stanceUnit.Position, issued[1].Order.TargetPosition);
        Assert.Equal(UnitStance.Defensive, issued[1].Order.Stance);
    }

    [Fact]
    public void AttackMoveAndStop_Execute_IssueExpectedOrdersForOwnedUnitsOnly()
    {
        var units = new Dictionary<int, UnitCommandView>
        {
            [2] = Unit(2, playerId: 1, x: 1, y: 2),
            [4] = Unit(4, playerId: 1, x: 4, y: 5),
            [9] = Unit(9, playerId: 2, x: 9, y: 9)
        };
        var issued = new List<(int UnitId, UnitOrder Order)>();
        var ctx = BuildContext(units, issued);

        var target = Vec(100, 200);
        new AttackMoveCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 9, 4, 2, 999 },
            TargetPosition = target
        }.Execute(ctx);

        new StopCommand
        {
            PlayerId = 1,
            UnitIds = new List<int> { 4, 9, 2 }
        }.Execute(ctx);

        Assert.Equal(4, issued.Count);
        Assert.Collection(
            issued,
            first =>
            {
                Assert.Equal(2, first.UnitId);
                Assert.Equal(UnitOrderType.AttackMove, first.Order.Type);
                Assert.Equal(target, first.Order.TargetPosition);
            },
            second =>
            {
                Assert.Equal(4, second.UnitId);
                Assert.Equal(UnitOrderType.AttackMove, second.Order.Type);
                Assert.Equal(target, second.Order.TargetPosition);
            },
            third =>
            {
                Assert.Equal(2, third.UnitId);
                Assert.Equal(UnitOrderType.Stop, third.Order.Type);
                Assert.Equal(FixedVector2.Zero, third.Order.TargetPosition);
                Assert.Equal(-1, third.Order.TargetUnitId);
            },
            fourth =>
            {
                Assert.Equal(4, fourth.UnitId);
                Assert.Equal(UnitOrderType.Stop, fourth.Order.Type);
            });
    }

    [Fact]
    public void CommandBuffer_GetCommandsForTick_SortsByPlayerTypeThenInsertionAndRemovesTick()
    {
        var buffer = new CommandBuffer();

        buffer.AddCommand(new MoveCommand { ScheduledTick = 12, PlayerId = 2 });
        buffer.AddCommand(new AttackMoveCommand { ScheduledTick = 12, PlayerId = 1 });
        buffer.AddCommand(new HoldPositionCommand { ScheduledTick = 12, PlayerId = 1 });
        buffer.AddCommand(new MoveCommand { ScheduledTick = 12, PlayerId = 1 });
        buffer.AddCommand(new MoveCommand { ScheduledTick = 12, PlayerId = 1 });

        var commands = buffer.GetCommandsForTick(12);

        Assert.Equal(5, commands.Count);
        Assert.IsType<HoldPositionCommand>(commands[0]);
        Assert.IsType<MoveCommand>(commands[1]);
        Assert.IsType<MoveCommand>(commands[2]);
        Assert.IsType<AttackMoveCommand>(commands[3]);
        Assert.IsType<MoveCommand>(commands[4]);
        Assert.False(buffer.HasCommandsForTick(12));
    }

    [Fact]
    public void CommandBuffer_PeekInjectPurgeAndClear_WorkAsExpected()
    {
        var buffer = new CommandBuffer();
        buffer.AddCommand(new MoveCommand { ScheduledTick = 3, PlayerId = 5 });
        buffer.AddCommand(new StopCommand { ScheduledTick = 1, PlayerId = 1 });

        var injected = new List<GameCommand>
        {
            new SetStanceCommand { PlayerId = 2, UnitIds = new List<int> { 7 }, Stance = UnitStance.Aggressive },
            new HoldPositionCommand { PlayerId = 1, UnitIds = new List<int> { 8 } }
        };
        buffer.InjectCommands(10, injected);

        Assert.Equal(4, buffer.TotalPendingCommands);

        var peeked = buffer.PeekCommandsForTick(10);
        Assert.Equal(2, peeked.Count);
        Assert.True(buffer.HasCommandsForTick(10));
        Assert.All(peeked, c => Assert.Equal((ulong)10, c.ScheduledTick));
        Assert.Collection(
            peeked,
            first => Assert.IsType<HoldPositionCommand>(first),
            second => Assert.IsType<SetStanceCommand>(second));

        peeked.Clear();
        Assert.Equal(4, buffer.TotalPendingCommands);

        buffer.PurgeBefore(3);
        Assert.False(buffer.HasCommandsForTick(1));
        Assert.True(buffer.HasCommandsForTick(3));
        Assert.True(buffer.HasCommandsForTick(10));

        buffer.Clear();
        Assert.Equal(0, buffer.TotalPendingCommands);
        Assert.False(buffer.HasCommandsForTick(3));
        Assert.False(buffer.HasCommandsForTick(10));
    }

    // ── CommandBuffer additional edge cases ─────────────────────────────────

    [Fact]
    public void CommandBuffer_GetCommandsForTick_EmptyTick_ReturnsEmptyList()
    {
        var buffer = new CommandBuffer();
        // No commands have been added for tick 99
        var result = buffer.GetCommandsForTick(99);
        Assert.Empty(result);
    }

    [Fact]
    public void CommandBuffer_PeekCommandsForTick_EmptyTick_ReturnsEmptyList()
    {
        var buffer = new CommandBuffer();
        var result = buffer.PeekCommandsForTick(42);
        Assert.Empty(result);
    }

    [Fact]
    public void CommandBuffer_PeekCommandsForTick_SortsAndDoesNotRemoveTick()
    {
        var buffer = new CommandBuffer();
        // Two commands for the same player but different types — forces the
        // CommandType-ordering branch of the sort comparison.
        buffer.AddCommand(new MoveCommand    { ScheduledTick = 5, PlayerId = 1 });
        buffer.AddCommand(new StopCommand    { ScheduledTick = 5, PlayerId = 1 });

        var peeked = buffer.PeekCommandsForTick(5);

        Assert.Equal(2, peeked.Count);
        // StopCommand enum value is less than MoveCommand, so it comes first.
        Assert.IsType<StopCommand>(peeked[0]);
        Assert.IsType<MoveCommand>(peeked[1]);

        // Peek must NOT remove the tick.
        Assert.True(buffer.HasCommandsForTick(5));
        Assert.Equal(2, buffer.TotalPendingCommands);
    }

    [Fact]
    public void CommandBuffer_PeekCommandsForTick_InsertionOrderTieBreak()
    {
        var buffer = new CommandBuffer();
        // Three Move commands for the same player — forces the
        // InsertionOrder tiebreak branch.
        buffer.AddCommand(new MoveCommand { ScheduledTick = 7, PlayerId = 1 });
        buffer.AddCommand(new MoveCommand { ScheduledTick = 7, PlayerId = 1 });
        buffer.AddCommand(new MoveCommand { ScheduledTick = 7, PlayerId = 1 });

        var peeked = buffer.PeekCommandsForTick(7);

        Assert.Equal(3, peeked.Count);
        Assert.All(peeked, c => Assert.IsType<MoveCommand>(c));
    }
}
