using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Networking;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for CommandSerializer — binary serialization of GameCommand subclasses
/// and checksum packets for lockstep networking.
/// Verifies round-trip fidelity for every command type, edge cases
/// (empty unit lists, multiple waypoints), and the checksum packet format.
/// </summary>
public class CommandSerializerTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static FixedVector2 Vec(int x, int y) =>
        new(FixedPoint.FromInt(x), FixedPoint.FromInt(y));

    // ── MoveCommand ────────────────────────────────────────────────────────

    [Fact]
    public void MoveCommand_RoundTrip_PreservesAllFields()
    {
        var original = new MoveCommand
        {
            ScheduledTick = 42UL,
            PlayerId = 2,
            TargetPosition = Vec(100, 200),
            UnitIds = new List<int> { 1, 3, 7 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (MoveCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.TargetPosition, restored.TargetPosition);
        Assert.Equal(original.UnitIds, restored.UnitIds);
    }

    [Fact]
    public void MoveCommand_EmptyUnitList_RoundTrip()
    {
        var original = new MoveCommand
        {
            ScheduledTick = 1UL,
            PlayerId = 1,
            TargetPosition = Vec(0, 0),
            UnitIds = new List<int>()
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (MoveCommand)CommandSerializer.Deserialize(bytes);

        Assert.Empty(restored.UnitIds);
        Assert.Equal(original.TargetPosition, restored.TargetPosition);
    }

    [Fact]
    public void MoveCommand_NegativeCoordinates_RoundTrip()
    {
        // FixedVector2 components can represent negative world positions
        var pos = new FixedVector2(FixedPoint.FromInt(-50), FixedPoint.FromInt(-75));
        var original = new MoveCommand
        {
            ScheduledTick = 5UL,
            PlayerId = 1,
            TargetPosition = pos,
            UnitIds = new List<int> { 10 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (MoveCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(pos, restored.TargetPosition);
    }

    // ── AttackMoveCommand ──────────────────────────────────────────────────

    [Fact]
    public void AttackMoveCommand_RoundTrip_PreservesAllFields()
    {
        var original = new AttackMoveCommand
        {
            ScheduledTick = 99UL,
            PlayerId = 3,
            TargetPosition = Vec(50, 75),
            UnitIds = new List<int> { 5, 10, 15 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (AttackMoveCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.TargetPosition, restored.TargetPosition);
        Assert.Equal(original.UnitIds, restored.UnitIds);
    }

    // ── StopCommand ────────────────────────────────────────────────────────

    [Fact]
    public void StopCommand_RoundTrip_PreservesAllFields()
    {
        var original = new StopCommand
        {
            ScheduledTick = 10UL,
            PlayerId = 1,
            UnitIds = new List<int> { 2, 4 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (StopCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.UnitIds, restored.UnitIds);
    }

    [Fact]
    public void StopCommand_EmptyUnitList_RoundTrip()
    {
        var original = new StopCommand
        {
            ScheduledTick = 3UL,
            PlayerId = 2,
            UnitIds = new List<int>()
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (StopCommand)CommandSerializer.Deserialize(bytes);
        Assert.Empty(restored.UnitIds);
    }

    // ── AttackCommand ──────────────────────────────────────────────────────

    [Fact]
    public void AttackCommand_RoundTrip_PreservesAllFields()
    {
        var original = new AttackCommand
        {
            ScheduledTick = 200UL,
            PlayerId = 2,
            TargetUnitId = 77,
            UnitIds = new List<int> { 1, 2, 3 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (AttackCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.TargetUnitId, restored.TargetUnitId);
        Assert.Equal(original.UnitIds, restored.UnitIds);
    }

    [Fact]
    public void AttackCommand_SingleUnit_RoundTrip()
    {
        var original = new AttackCommand
        {
            ScheduledTick = 7UL,
            PlayerId = 1,
            TargetUnitId = 42,
            UnitIds = new List<int> { 11 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (AttackCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(42, restored.TargetUnitId);
        Assert.Equal(new List<int> { 11 }, restored.UnitIds);
    }

    // ── PatrolCommand ──────────────────────────────────────────────────────

    [Fact]
    public void PatrolCommand_MultipleWaypoints_RoundTrip()
    {
        var wp = new List<FixedVector2>
        {
            Vec(10, 20),
            Vec(30, 40),
            Vec(50, 60)
        };

        var original = new PatrolCommand
        {
            ScheduledTick = 150UL,
            PlayerId = 4,
            Waypoints = wp,
            UnitIds = new List<int> { 8, 9 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (PatrolCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.UnitIds, restored.UnitIds);
        Assert.Equal(3, restored.Waypoints.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(wp[i], restored.Waypoints[i]);
        }
    }

    [Fact]
    public void PatrolCommand_SingleWaypoint_RoundTrip()
    {
        var original = new PatrolCommand
        {
            ScheduledTick = 1UL,
            PlayerId = 1,
            Waypoints = new List<FixedVector2> { Vec(5, 5) },
            UnitIds = new List<int> { 1 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (PatrolCommand)CommandSerializer.Deserialize(bytes);

        Assert.Single(restored.Waypoints);
        Assert.Equal(Vec(5, 5), restored.Waypoints[0]);
    }

    [Fact]
    public void PatrolCommand_EmptyWaypoints_RoundTrip()
    {
        var original = new PatrolCommand
        {
            ScheduledTick = 1UL,
            PlayerId = 1,
            Waypoints = new List<FixedVector2>(),
            UnitIds = new List<int> { 1 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (PatrolCommand)CommandSerializer.Deserialize(bytes);

        Assert.Empty(restored.Waypoints);
    }

    // ── HoldPositionCommand ────────────────────────────────────────────────

    [Fact]
    public void HoldPositionCommand_RoundTrip_PreservesAllFields()
    {
        var original = new HoldPositionCommand
        {
            ScheduledTick = 300UL,
            PlayerId = 3,
            UnitIds = new List<int> { 20, 21, 22 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (HoldPositionCommand)CommandSerializer.Deserialize(bytes);

        Assert.Equal(original.ScheduledTick, restored.ScheduledTick);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.UnitIds, restored.UnitIds);
    }

    [Fact]
    public void HoldPositionCommand_EmptyUnitList_RoundTrip()
    {
        var original = new HoldPositionCommand
        {
            ScheduledTick = 5UL,
            PlayerId = 1,
            UnitIds = new List<int>()
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (HoldPositionCommand)CommandSerializer.Deserialize(bytes);
        Assert.Empty(restored.UnitIds);
    }

    // ── Checksum packet ────────────────────────────────────────────────────

    [Fact]
    public void SerializeDeserializeChecksum_RoundTrip()
    {
        int playerId = 1;
        ulong tick = 12345678UL;
        uint checksum = 0xDEADBEEFu;

        byte[] bytes = CommandSerializer.SerializeChecksum(playerId, tick, checksum);
        var (restoredPlayerId, restoredTick, restoredChecksum) = CommandSerializer.DeserializeChecksum(bytes);

        Assert.Equal(playerId, restoredPlayerId);
        Assert.Equal(tick, restoredTick);
        Assert.Equal(checksum, restoredChecksum);
    }

    [Fact]
    public void SerializeChecksum_ProducesSixteenBytes()
    {
        byte[] bytes = CommandSerializer.SerializeChecksum(0, 1UL, 0u);
        Assert.Equal(16, bytes.Length);
    }

    [Fact]
    public void SerializeDeserializeChecksum_ZeroValues_RoundTrip()
    {
        byte[] bytes = CommandSerializer.SerializeChecksum(0, 0UL, 0u);
        var (playerId, tick, checksum) = CommandSerializer.DeserializeChecksum(bytes);
        Assert.Equal(0, playerId);
        Assert.Equal(0UL, tick);
        Assert.Equal(0u, checksum);
    }

    [Fact]
    public void SerializeDeserializeChecksum_MaxValues_RoundTrip()
    {
        byte[] bytes = CommandSerializer.SerializeChecksum(int.MaxValue, ulong.MaxValue, uint.MaxValue);
        var (playerId, tick, checksum) = CommandSerializer.DeserializeChecksum(bytes);
        Assert.Equal(int.MaxValue, playerId);
        Assert.Equal(ulong.MaxValue, tick);
        Assert.Equal(uint.MaxValue, checksum);
    }

    // ── Determinism ────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_SameCommand_ProducesIdenticalBytes()
    {
        var cmd = new MoveCommand
        {
            ScheduledTick = 10UL,
            PlayerId = 1,
            TargetPosition = Vec(20, 30),
            UnitIds = new List<int> { 5, 6 }
        };

        byte[] bytes1 = CommandSerializer.Serialize(cmd);
        byte[] bytes2 = CommandSerializer.Serialize(cmd);

        Assert.Equal(bytes1, bytes2);
    }

    // ── FixedPoint precision preserved ────────────────────────────────────

    [Fact]
    public void MoveCommand_SubCellPosition_PreservesRawValue()
    {
        // Use a fractional FixedPoint position that is NOT a whole integer
        FixedPoint fractX = FixedPoint.FromFloat(1.5f);
        FixedPoint fractY = FixedPoint.FromFloat(2.75f);
        var pos = new FixedVector2(fractX, fractY);

        var original = new MoveCommand
        {
            ScheduledTick = 1UL,
            PlayerId = 1,
            TargetPosition = pos,
            UnitIds = new List<int> { 1 }
        };

        byte[] bytes = CommandSerializer.Serialize(original);
        var restored = (MoveCommand)CommandSerializer.Deserialize(bytes);

        // Raw representation must match exactly — no float rounding allowed
        Assert.Equal(fractX.Raw, restored.TargetPosition.X.Raw);
        Assert.Equal(fractY.Raw, restored.TargetPosition.Y.Raw);
    }

    // ── Unknown type / invalid wire format throws ───────────────────────────

    [Fact]
    public void Serialize_UnknownCommandType_ThrowsInvalidOperationException()
    {
        // SetStanceCommand is not handled by the serializer switch.
        var cmd = new SetStanceCommand
        {
            ScheduledTick = 1,
            PlayerId = 1,
            UnitIds = new List<int> { 1 },
            Stance = CorditeWars.Systems.Pathfinding.UnitStance.Aggressive
        };
        Assert.Throws<InvalidOperationException>(() => CommandSerializer.Serialize(cmd));
    }

    [Fact]
    public void Deserialize_UnknownWireType_ThrowsInvalidOperationException()
    {
        // Build a byte stream with an unrecognised wire-type byte (e.g. 0xFF).
        // Header: 1 byte wireType + 8 bytes scheduledTick (ulong) + 4 bytes playerId (int)
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write((byte)0xFF);        // unknown wire type
        w.Write((ulong)1);          // scheduledTick
        w.Write((int)1);            // playerId
        w.Flush();
        byte[] data = ms.ToArray();

        Assert.Throws<InvalidOperationException>(() => CommandSerializer.Deserialize(data));
    }
}
