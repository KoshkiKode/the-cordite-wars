using System;
using System.Collections.Generic;
using System.IO;
using CorditeWars.Core;

namespace CorditeWars.Systems.Networking;

/// <summary>
/// Binary serialization for GameCommand subclasses and checksum packets.
/// Uses BinaryWriter/BinaryReader for compact wire format.
/// FixedPoint values are serialized as their raw int representation.
/// FixedVector2 values are serialized as two raw ints.
/// </summary>
public static class CommandSerializer
{
    // ── Command type IDs for wire format ────────────────────────────
    private const byte WireMoveCommand = 0;
    private const byte WireAttackMoveCommand = 1;
    private const byte WireStopCommand = 2;
    private const byte WireAttackCommand = 3;
    private const byte WirePatrolCommand = 4;
    private const byte WireHoldPositionCommand = 5;

    /// <summary>
    /// Serializes a GameCommand into a byte array for network transmission.
    /// Format: [1 byte wireType] [8 bytes scheduledTick] [4 bytes playerId] [payload...]
    /// </summary>
    public static byte[] Serialize(GameCommand cmd)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);

        byte wireType = GetWireType(cmd);
        w.Write(wireType);
        w.Write(cmd.ScheduledTick);
        w.Write(cmd.PlayerId);

        switch (cmd)
        {
            case MoveCommand move:
                WriteFixedVector2(w, move.TargetPosition);
                WriteIntList(w, move.UnitIds);
                break;

            case AttackMoveCommand atkMove:
                WriteFixedVector2(w, atkMove.TargetPosition);
                WriteIntList(w, atkMove.UnitIds);
                break;

            case StopCommand stop:
                WriteIntList(w, stop.UnitIds);
                break;

            case AttackCommand attack:
                w.Write(attack.TargetUnitId);
                WriteIntList(w, attack.UnitIds);
                break;

            case PatrolCommand patrol:
                w.Write(patrol.Waypoints.Count);
                for (int i = 0; i < patrol.Waypoints.Count; i++)
                {
                    WriteFixedVector2(w, patrol.Waypoints[i]);
                }
                WriteIntList(w, patrol.UnitIds);
                break;

            case HoldPositionCommand hold:
                WriteIntList(w, hold.UnitIds);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown command type: {cmd.GetType().Name}");
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a byte array back into a GameCommand.
    /// </summary>
    public static GameCommand Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);

        byte wireType = r.ReadByte();
        ulong scheduledTick = r.ReadUInt64();
        int playerId = r.ReadInt32();

        GameCommand cmd;

        switch (wireType)
        {
            case WireMoveCommand:
            {
                var target = ReadFixedVector2(r);
                var unitIds = ReadIntList(r);
                cmd = new MoveCommand
                {
                    TargetPosition = target,
                    UnitIds = unitIds
                };
                break;
            }

            case WireAttackMoveCommand:
            {
                var target = ReadFixedVector2(r);
                var unitIds = ReadIntList(r);
                cmd = new AttackMoveCommand
                {
                    TargetPosition = target,
                    UnitIds = unitIds
                };
                break;
            }

            case WireStopCommand:
            {
                var unitIds = ReadIntList(r);
                cmd = new StopCommand { UnitIds = unitIds };
                break;
            }

            case WireAttackCommand:
            {
                int targetUnitId = r.ReadInt32();
                var unitIds = ReadIntList(r);
                cmd = new AttackCommand
                {
                    TargetUnitId = targetUnitId,
                    UnitIds = unitIds
                };
                break;
            }

            case WirePatrolCommand:
            {
                int waypointCount = r.ReadInt32();
                var waypoints = new List<FixedVector2>(waypointCount);
                for (int i = 0; i < waypointCount; i++)
                {
                    waypoints.Add(ReadFixedVector2(r));
                }
                var unitIds = ReadIntList(r);
                cmd = new PatrolCommand
                {
                    Waypoints = waypoints,
                    UnitIds = unitIds
                };
                break;
            }

            case WireHoldPositionCommand:
            {
                var unitIds = ReadIntList(r);
                cmd = new HoldPositionCommand { UnitIds = unitIds };
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Unknown wire command type: {wireType}");
        }

        cmd.ScheduledTick = scheduledTick;
        cmd.PlayerId = playerId;
        return cmd;
    }

    /// <summary>
    /// Serializes a checksum packet: [8 bytes tick] [4 bytes checksum].
    /// </summary>
    public static byte[] SerializeChecksum(ulong tick, uint checksum)
    {
        var data = new byte[12];
        using var ms = new MemoryStream(data);
        using var w = new BinaryWriter(ms);
        w.Write(tick);
        w.Write(checksum);
        return data;
    }

    /// <summary>
    /// Deserializes a checksum packet.
    /// </summary>
    public static (ulong tick, uint checksum) DeserializeChecksum(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);
        ulong tick = r.ReadUInt64();
        uint checksum = r.ReadUInt32();
        return (tick, checksum);
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static byte GetWireType(GameCommand cmd)
    {
        switch (cmd)
        {
            case MoveCommand: return WireMoveCommand;
            case AttackMoveCommand: return WireAttackMoveCommand;
            case StopCommand: return WireStopCommand;
            case AttackCommand: return WireAttackCommand;
            case PatrolCommand: return WirePatrolCommand;
            case HoldPositionCommand: return WireHoldPositionCommand;
            default:
                throw new InvalidOperationException(
                    $"Unknown command type: {cmd.GetType().Name}");
        }
    }

    private static void WriteFixedVector2(BinaryWriter w, FixedVector2 v)
    {
        w.Write(v.X.Raw);
        w.Write(v.Y.Raw);
    }

    private static FixedVector2 ReadFixedVector2(BinaryReader r)
    {
        int x = r.ReadInt32();
        int y = r.ReadInt32();
        return new FixedVector2(FixedPoint.FromRaw(x), FixedPoint.FromRaw(y));
    }

    private static void WriteIntList(BinaryWriter w, List<int> list)
    {
        w.Write(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            w.Write(list[i]);
        }
    }

    private static List<int> ReadIntList(BinaryReader r)
    {
        int count = r.ReadInt32();
        var list = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(r.ReadInt32());
        }
        return list;
    }
}
