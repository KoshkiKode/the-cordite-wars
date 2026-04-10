using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CorditeWars.Core;
using Godot;

namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Records the command stream during a match and saves it to
/// <c>user://replays/{timestamp}.json</c> when the match ends.
///
/// <para>
/// Only active when "Auto-save Replays" is enabled in the Options menu (the
/// setting is read from <c>user://settings.cfg</c> key <c>Game/auto_save_replays</c>).
/// </para>
///
/// <para>
/// Usage: call <see cref="BeginRecording"/> once the match starts, then call
/// <see cref="RecordCommand"/> each time a command is executed, and finally
/// call <see cref="FinalizeAndSave"/> when the match ends.
/// </para>
/// </summary>
public sealed class ReplayManager
{
    private const string ReplayDirectory = "user://replays";
    private const int MaxReplays = 20;

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    // ── Recording state ───────────────────────────────────────────────

    private bool _recording;
    private string _mapId = string.Empty;
    private ulong _matchSeed;
    private string? _missionId;
    private ReplayPlayerInfo[] _players = [];
    private readonly List<ReplayCommandEntry> _commands = new();
    private DateTime _startTime;

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Starts recording a new replay. Clears any previously recorded data.
    /// </summary>
    public void BeginRecording(
        string mapId,
        ulong matchSeed,
        ReplayPlayerInfo[] players,
        string? missionId = null)
    {
        _mapId = mapId;
        _matchSeed = matchSeed;
        _players = players;
        _missionId = missionId;
        _commands.Clear();
        _startTime = DateTime.UtcNow;
        _recording = true;

        GD.Print($"[ReplayManager] Recording started. Map={mapId}, Seed={matchSeed}");
    }

    /// <summary>
    /// Records a single command entry. No-op if not currently recording.
    /// </summary>
    public void RecordCommand(ReplayCommandEntry entry)
    {
        if (!_recording) return;
        _commands.Add(entry);
    }

    /// <summary>
    /// Convenience overload: record a command from its component parts.
    /// </summary>
    public void RecordCommand(
        ulong tick,
        int playerId,
        string commandType,
        float targetX = 0f,
        float targetZ = 0f,
        int[]? unitIds = null,
        int targetUnitId = -1)
    {
        if (!_recording) return;

        _commands.Add(new ReplayCommandEntry
        {
            Tick         = tick,
            PlayerId     = playerId,
            Type         = commandType,
            TargetX      = targetX,
            TargetZ      = targetZ,
            UnitIds      = unitIds ?? [],
            TargetUnitId = targetUnitId
        });
    }

    /// <summary>
    /// Stops recording and saves the replay to disk if auto-save is enabled.
    /// Safe to call even if recording never started.
    /// </summary>
    /// <param name="totalTicks">Final simulation tick count.</param>
    /// <param name="winnerPlayerId">Player ID of the winner, or -1 for a draw.</param>
    /// <param name="autoSaveEnabled">Whether to persist the file. Defaults to true.</param>
    public void FinalizeAndSave(ulong totalTicks, int winnerPlayerId, bool autoSaveEnabled = true)
    {
        if (!_recording)
        {
            GD.PushWarning("[ReplayManager] FinalizeAndSave called without an active recording.");
            return;
        }

        _recording = false;

        if (!autoSaveEnabled)
        {
            GD.Print("[ReplayManager] Auto-save disabled — replay discarded.");
            return;
        }

        var data = new ReplayData
        {
            Version         = "0.1.0",
            SaveTimestamp   = DateTime.UtcNow.ToString("o"),
            MapId           = _mapId,
            MatchSeed       = _matchSeed,
            TotalTicks      = totalTicks,
            DurationSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
            WinnerPlayerId  = winnerPlayerId,
            MissionId       = _missionId,
            Players         = _players,
            Commands        = new List<ReplayCommandEntry>(_commands)
        };

        Save(data);
    }

    // ── Disk I/O ─────────────────────────────────────────────────────

    private static void Save(ReplayData data)
    {
        EnsureDirectory();
        PruneOldReplays();

        // Timestamp-based filename: replay_20260410T210045_crossroads.json
        string ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        string fileName = $"replay_{ts}_{data.MapId}.json";
        string filePath = $"{ReplayDirectory}/{fileName}";

        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);

            using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                GD.PushError($"[ReplayManager] Cannot open '{filePath}' for writing " +
                             $"(error: {FileAccess.GetOpenError()}).");
                return;
            }

            file.StoreString(json);
            file.Flush();
            GD.Print($"[ReplayManager] Replay saved: {fileName} ({data.Commands.Count} commands, " +
                     $"{data.TotalTicks} ticks).");
        }
        catch (Exception ex)
        {
            GD.PushError($"[ReplayManager] Failed to save replay: {ex.Message}");
        }
    }

    private static void EnsureDirectory()
    {
        if (!DirAccess.DirExistsAbsolute(ReplayDirectory))
        {
            var err = DirAccess.MakeDirAbsolute(ReplayDirectory);
            if (err != Error.Ok)
                GD.PushError($"[ReplayManager] Failed to create replay directory: {err}");
        }
    }

    private static void PruneOldReplays()
    {
        // Keep at most MaxReplays files, deleting the oldest by name (timestamp prefix).
        using var dir = DirAccess.Open(ReplayDirectory);
        if (dir is null) return;

        var files = new List<string>();
        dir.ListDirBegin();
        string f = dir.GetNext();
        while (!string.IsNullOrEmpty(f))
        {
            if (!dir.CurrentIsDir() && f.StartsWith("replay_") && f.EndsWith(".json"))
                files.Add(f);
            f = dir.GetNext();
        }
        dir.ListDirEnd();

        files.Sort(StringComparer.Ordinal); // lexicographic = chronological (timestamps)

        while (files.Count >= MaxReplays)
        {
            string oldest = files[0];
            files.RemoveAt(0);
            DirAccess.RemoveAbsolute($"{ReplayDirectory}/{oldest}");
            GD.Print($"[ReplayManager] Pruned old replay: {oldest}");
        }
    }

    // ── JSON helpers ──────────────────────────────────────────────────

    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = false, // keep replay files compact
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
