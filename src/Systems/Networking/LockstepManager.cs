using System.Collections.Generic;
using Godot;
using CorditeWars.Core;

namespace CorditeWars.Systems.Networking;

/// <summary>
/// Core lockstep protocol coordinator. Manages command synchronization,
/// tick advancement gating, and desync detection across all networked peers.
///
/// Flow:
///   1. Local player issues command → SubmitLocalCommand stamps tick + inputDelay, buffers, broadcasts
///   2. Remote commands arrive via NetworkTransport → ReceiveRemoteCommand buffers them
///   3. GameManager calls CanAdvanceTick(nextTick) — returns true only when ALL players
///      have confirmed their commands for that tick (empty = "no commands" confirmation)
///   4. GetCommandsForTick merges all players' commands in deterministic order
///   5. Periodic checksums are exchanged and compared for desync detection
/// </summary>
public partial class LockstepManager : Node
{
    /// <summary>This client's player slot index (0–5).</summary>
    public int LocalPlayerId { get; private set; }

    /// <summary>Total players in the match.</summary>
    public int PlayerCount { get; private set; }

    /// <summary>Ticks ahead to schedule local commands (default 6 = 200ms at 30 tps).</summary>
    public int InputDelay { get; private set; } = 6;

    /// <summary>Latest tick where all players' commands have been received.</summary>
    public ulong ConfirmedTick { get; private set; }

    /// <summary>Whether this client is the host.</summary>
    public bool IsHost { get; private set; }

    // Per-player, per-tick command lists.
    // Outer key = playerId, inner key = tick.
    private readonly SortedList<int, SortedList<ulong, List<GameCommand>>> _commandBuffers = new();

    // Tracks which ticks have been "confirmed" (command set sent) per player.
    // Key = playerId, value = set of ticks that player has confirmed (stored as sorted list for determinism).
    private readonly SortedList<int, SortedList<ulong, bool>> _confirmedTicks = new();

    // Per-player checksums keyed by tick, for desync comparison.
    private readonly SortedList<int, SortedList<ulong, uint>> _remoteChecksums = new();

    // Local checksum history for comparison.
    private readonly SortedList<ulong, uint> _localChecksums = new();

    private NetworkTransport? _transport;

    /// <summary>
    /// Initializes the lockstep manager for a new match.
    /// Must be called before the first tick.
    /// </summary>
    public void Initialize(
        int localPlayerId,
        int playerCount,
        bool isHost,
        int inputDelay,
        NetworkTransport transport)
    {
        LocalPlayerId = localPlayerId;
        PlayerCount = playerCount;
        IsHost = isHost;
        InputDelay = inputDelay;
        ConfirmedTick = 0;
        _transport = transport;

        _commandBuffers.Clear();
        _confirmedTicks.Clear();
        _remoteChecksums.Clear();
        _localChecksums.Clear();

        // Initialize per-player structures
        for (int p = 0; p < playerCount; p++)
        {
            _commandBuffers[p] = new SortedList<ulong, List<GameCommand>>();
            _confirmedTicks[p] = new SortedList<ulong, bool>();
            _remoteChecksums[p] = new SortedList<ulong, uint>();
        }

        // Pre-confirm tick 0 for all players (no commands at tick 0)
        for (int p = 0; p < playerCount; p++)
        {
            _confirmedTicks[p][0] = true;
        }

        _transport.CommandReceived += OnCommandPacketReceived;
        _transport.ChecksumReceived += OnChecksumPacketReceived;

        GD.Print($"[LockstepManager] Initialized: player={localPlayerId}, count={playerCount}, host={isHost}, delay={inputDelay}");
    }

    /// <summary>
    /// Submits a local player command. Stamps it with CurrentTick + InputDelay,
    /// buffers locally, and broadcasts to all peers.
    /// </summary>
    public void SubmitLocalCommand(GameCommand cmd, ulong currentTick)
    {
        ulong scheduledTick = currentTick + (ulong)InputDelay;
        cmd.ScheduledTick = scheduledTick;
        cmd.PlayerId = LocalPlayerId;

        // Buffer locally
        AddCommandToBuffer(LocalPlayerId, scheduledTick, cmd);

        // Serialize and broadcast
        byte[] data = CommandSerializer.Serialize(cmd);
        _transport?.BroadcastCommand(data);
    }

    /// <summary>
    /// Confirms that the local player has no more commands for the given tick.
    /// Called automatically by the tick advancement logic. Also sends a
    /// "tick confirmed" marker to peers so they know we're done for that tick.
    /// </summary>
    public void ConfirmLocalTick(ulong tick)
    {
        if (!_confirmedTicks[LocalPlayerId].ContainsKey(tick))
        {
            _confirmedTicks[LocalPlayerId][tick] = true;
        }

        // Send an empty command packet as tick confirmation to all peers.
        // We encode it as a special "NOP" — a command packet with just the header
        // indicating playerId + tick + no payload, signaling the tick is complete.
        byte[] confirmPacket = CommandSerializer.SerializeChecksum(tick, 0);
        // Reuse the checksum channel concept but with a dedicated confirm message.
        // Actually, let's send a proper empty-tick-confirm via the command channel.
        // We'll encode it as: [4 bytes playerId] [8 bytes tick] [0xFF sentinel]
        byte[] data = new byte[13];
        WriteInt(data, 0, LocalPlayerId);
        WriteUlong(data, 4, tick);
        data[12] = 0xFF; // Sentinel for "tick confirmed, no more commands"
        _transport?.BroadcastCommand(data);
    }

    /// <summary>
    /// Returns true only if ALL players have confirmed their commands for this tick.
    /// Empty command list counts as confirmed (the player did nothing that tick).
    /// </summary>
    public bool CanAdvanceTick(ulong tick)
    {
        for (int p = 0; p < PlayerCount; p++)
        {
            if (!_confirmedTicks[p].ContainsKey(tick))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Merges all players' commands for a tick into a single deterministically-ordered list.
    /// Sort order: PlayerId ascending, then CommandType ascending, then insertion order.
    /// </summary>
    public List<GameCommand> GetCommandsForTick(ulong tick)
    {
        var merged = new List<GameCommand>();

        for (int p = 0; p < PlayerCount; p++)
        {
            var playerBuffer = _commandBuffers[p];
            if (playerBuffer.TryGetValue(tick, out var commands))
            {
                for (int c = 0; c < commands.Count; c++)
                {
                    merged.Add(commands[c]);
                }
                playerBuffer.Remove(tick);
            }
        }

        // Sort deterministically: PlayerId → CommandType → InsertionOrder
        merged.Sort((a, b) =>
        {
            int cmp = a.PlayerId.CompareTo(b.PlayerId);
            if (cmp != 0) return cmp;

            cmp = ((int)a.Type).CompareTo((int)b.Type);
            if (cmp != 0) return cmp;

            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        return merged;
    }

    /// <summary>
    /// Stores a local checksum and broadcasts to peers.
    /// Compares against received remote checksums — emits DesyncDetected on mismatch.
    /// </summary>
    public void SubmitChecksum(ulong tick, uint checksum)
    {
        _localChecksums[tick] = checksum;

        // Broadcast to all peers
        byte[] data = CommandSerializer.SerializeChecksum(tick, checksum);
        _transport?.BroadcastChecksum(data);

        // Compare against any already-received remote checksums for this tick
        CheckForDesync(tick);

        // Purge old checksum data (keep last 300 ticks = 10 seconds)
        PurgeOldData(tick);
    }

    /// <summary>
    /// Cleans up when shutting down.
    /// </summary>
    public void Shutdown()
    {
        if (_transport != null)
        {
            _transport.CommandReceived -= OnCommandPacketReceived;
            _transport.ChecksumReceived -= OnChecksumPacketReceived;
            _transport = null;
        }

        _commandBuffers.Clear();
        _confirmedTicks.Clear();
        _remoteChecksums.Clear();
        _localChecksums.Clear();
    }

    // ── Network packet handlers ────────────────────────────────────

    private void OnCommandPacketReceived(int senderPeerId, byte[] data)
    {
        // Check for tick-confirm sentinel
        if (data.Length == 13 && data[12] == 0xFF)
        {
            int playerId = ReadInt(data, 0);
            ulong tick = ReadUlong(data, 4);

            if (playerId >= 0 && playerId < PlayerCount)
            {
                if (!_confirmedTicks[playerId].ContainsKey(tick))
                {
                    _confirmedTicks[playerId][tick] = true;
                }
            }
            return;
        }

        // Normal command packet — deserialize
        GameCommand cmd = CommandSerializer.Deserialize(data);
        int cmdPlayerId = cmd.PlayerId;
        ulong cmdTick = cmd.ScheduledTick;

        if (cmdPlayerId >= 0 && cmdPlayerId < PlayerCount)
        {
            AddCommandToBuffer(cmdPlayerId, cmdTick, cmd);
        }
    }

    private void OnChecksumPacketReceived(int senderPeerId, byte[] data)
    {
        var (tick, checksum) = CommandSerializer.DeserializeChecksum(data);

        // Map senderPeerId to playerId — we need to find which player slot this peer occupies.
        // For simplicity, store by senderPeerId and check against all.
        // In practice, the LobbyManager maintains the peerId→playerId mapping.
        // Here we store in a generic bucket and compare.
        if (!_remoteChecksums.ContainsKey(senderPeerId))
        {
            _remoteChecksums[senderPeerId] = new SortedList<ulong, uint>();
        }
        _remoteChecksums[senderPeerId][tick] = checksum;

        CheckForDesync(tick);
    }

    // ── Internal helpers ───────────────────────────────────────────

    private void AddCommandToBuffer(int playerId, ulong tick, GameCommand cmd)
    {
        var playerBuffer = _commandBuffers[playerId];
        if (!playerBuffer.TryGetValue(tick, out var list))
        {
            list = new List<GameCommand>(4);
            playerBuffer[tick] = list;
        }
        list.Add(cmd);

        // Receiving a command for a tick implicitly confirms it
        // (the player has submitted at least one command for that tick).
        // Final confirmation comes from the ConfirmLocalTick sentinel.
        // We don't auto-confirm here because more commands may follow.
    }

    private void CheckForDesync(ulong tick)
    {
        if (!_localChecksums.TryGetValue(tick, out uint localHash))
            return;

        for (int i = 0; i < _remoteChecksums.Count; i++)
        {
            var remoteByTick = _remoteChecksums.Values[i];
            if (remoteByTick.TryGetValue(tick, out uint remoteHash))
            {
                if (remoteHash != localHash)
                {
                    GD.PrintErr($"[LockstepManager] DESYNC detected at tick {tick}! local={localHash:X8} remote={remoteHash:X8}");
                    EventBus.Instance?.EmitDesyncDetected(tick);
                    return;
                }
            }
        }
    }

    private void PurgeOldData(ulong currentTick)
    {
        if (currentTick < 300) return;
        ulong cutoff = currentTick - 300;

        // Purge local checksums
        PurgeSortedListBefore(_localChecksums, cutoff);

        // Purge remote checksums
        for (int i = 0; i < _remoteChecksums.Count; i++)
        {
            PurgeSortedListBefore(_remoteChecksums.Values[i], cutoff);
        }

        // Purge confirmed ticks
        for (int p = 0; p < PlayerCount; p++)
        {
            PurgeSortedListBefore(_confirmedTicks[p], cutoff);
        }
    }

    private static void PurgeSortedListBefore<TValue>(SortedList<ulong, TValue> list, ulong cutoff)
    {
        var keysToRemove = new List<ulong>();
        for (int i = 0; i < list.Count; i++)
        {
            ulong key = list.Keys[i];
            if (key < cutoff)
                keysToRemove.Add(key);
            else
                break; // SortedList is ordered
        }
        for (int i = 0; i < keysToRemove.Count; i++)
        {
            list.Remove(keysToRemove[i]);
        }
    }

    // ── Binary helpers (avoid BinaryWriter allocation) ─────────────

    private static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUlong(byte[] buf, int offset, ulong value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        buf[offset + 4] = (byte)((value >> 32) & 0xFF);
        buf[offset + 5] = (byte)((value >> 40) & 0xFF);
        buf[offset + 6] = (byte)((value >> 48) & 0xFF);
        buf[offset + 7] = (byte)((value >> 56) & 0xFF);
    }

    private static int ReadInt(byte[] buf, int offset)
    {
        return buf[offset]
            | (buf[offset + 1] << 8)
            | (buf[offset + 2] << 16)
            | (buf[offset + 3] << 24);
    }

    private static ulong ReadUlong(byte[] buf, int offset)
    {
        return (ulong)buf[offset]
            | ((ulong)buf[offset + 1] << 8)
            | ((ulong)buf[offset + 2] << 16)
            | ((ulong)buf[offset + 3] << 24)
            | ((ulong)buf[offset + 4] << 32)
            | ((ulong)buf[offset + 5] << 40)
            | ((ulong)buf[offset + 6] << 48)
            | ((ulong)buf[offset + 7] << 56);
    }
}
