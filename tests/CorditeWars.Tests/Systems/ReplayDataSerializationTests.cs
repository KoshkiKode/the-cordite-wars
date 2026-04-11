using System.Collections.Generic;
using System.Text.Json;
using CorditeWars.Systems.Persistence;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for ReplayData, ReplayCommandEntry, and ReplayPlayerInfo JSON
/// serialization round-trips. Replays must survive an encode → decode cycle
/// without any data loss, so these tests verify every field.
/// </summary>
public class ReplayDataSerializationTests
{
    // ── JSON options ─────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // ── ReplayPlayerInfo ─────────────────────────────────────────────────

    [Fact]
    public void ReplayPlayerInfo_RoundTrips_AllFields()
    {
        var original = new ReplayPlayerInfo
        {
            PlayerId   = 2,
            FactionId  = "valkyr",
            PlayerName = "TestPilot",
            IsAI       = false
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayPlayerInfo>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(2,          loaded!.PlayerId);
        Assert.Equal("valkyr",   loaded.FactionId);
        Assert.Equal("TestPilot",loaded.PlayerName);
        Assert.False(loaded.IsAI);
    }

    [Fact]
    public void ReplayPlayerInfo_IsAI_True_RoundTrips()
    {
        var original = new ReplayPlayerInfo
        {
            PlayerId   = 3,
            FactionId  = "bastion",
            PlayerName = "CPU",
            IsAI       = true
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayPlayerInfo>(json, Options);

        Assert.NotNull(loaded);
        Assert.True(loaded!.IsAI);
    }

    [Fact]
    public void ReplayPlayerInfo_UsesSnakeCasePropertyNames()
    {
        var player = new ReplayPlayerInfo
        {
            PlayerId   = 1,
            FactionId  = "arcloft",
            PlayerName = "Alpha",
            IsAI       = false
        };

        string json = JsonSerializer.Serialize(player, Options);

        Assert.Contains("\"player_id\"", json);
        Assert.Contains("\"faction_id\"", json);
        Assert.Contains("\"player_name\"", json);
        Assert.Contains("\"is_ai\"", json);
    }

    // ── ReplayCommandEntry ───────────────────────────────────────────────

    [Fact]
    public void ReplayCommandEntry_RoundTrips_AllFields()
    {
        var original = new ReplayCommandEntry
        {
            Tick         = 1500,
            PlayerId     = 1,
            Type         = "Move",
            TargetX      = 42.5f,
            TargetZ      = 18.75f,
            UnitIds      = new[] { 3, 7, 12 },
            TargetUnitId = -1
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayCommandEntry>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(1500UL,     loaded!.Tick);
        Assert.Equal(1,          loaded.PlayerId);
        Assert.Equal("Move",     loaded.Type);
        Assert.Equal(42.5f,      loaded.TargetX,  3);
        Assert.Equal(18.75f,     loaded.TargetZ,  3);
        Assert.Equal(3,          loaded.UnitIds.Length);
        Assert.Equal(3,          loaded.UnitIds[0]);
        Assert.Equal(7,          loaded.UnitIds[1]);
        Assert.Equal(12,         loaded.UnitIds[2]);
        Assert.Equal(-1,         loaded.TargetUnitId);
    }

    [Fact]
    public void ReplayCommandEntry_AttackCommand_TargetUnitIdPreserved()
    {
        var original = new ReplayCommandEntry
        {
            Tick         = 200,
            PlayerId     = 2,
            Type         = "Attack",
            UnitIds      = new[] { 5 },
            TargetUnitId = 99
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayCommandEntry>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(99, loaded!.TargetUnitId);
    }

    [Fact]
    public void ReplayCommandEntry_EmptyUnitIds_RoundTrips()
    {
        var original = new ReplayCommandEntry
        {
            Tick    = 1,
            Type    = "Pause",
            UnitIds = System.Array.Empty<int>()
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayCommandEntry>(json, Options);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.UnitIds);
    }

    [Fact]
    public void ReplayCommandEntry_UsesSnakeCasePropertyNames()
    {
        var entry = new ReplayCommandEntry
        {
            Tick         = 1,
            PlayerId     = 1,
            Type         = "Move",
            TargetX      = 1f,
            TargetZ      = 1f,
            UnitIds      = new[] { 1 },
            TargetUnitId = -1
        };

        string json = JsonSerializer.Serialize(entry, Options);

        Assert.Contains("\"tick\"",           json);
        Assert.Contains("\"player_id\"",      json);
        Assert.Contains("\"type\"",           json);
        Assert.Contains("\"target_x\"",       json);
        Assert.Contains("\"target_z\"",       json);
        Assert.Contains("\"unit_ids\"",       json);
        Assert.Contains("\"target_unit_id\"", json);
    }

    // ── ReplayData ───────────────────────────────────────────────────────

    [Fact]
    public void ReplayData_RoundTrips_AllScalarFields()
    {
        var original = new ReplayData
        {
            Version         = "1.2.3",
            SaveTimestamp   = "2025-06-01T12:00:00Z",
            MapId           = "crossroads",
            MatchSeed       = 987654321UL,
            TotalTicks      = 45000UL,
            DurationSeconds = 1500.0,
            WinnerPlayerId  = 1,
            MissionId       = null,
            Players         = System.Array.Empty<ReplayPlayerInfo>(),
            Commands        = new List<ReplayCommandEntry>()
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayData>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal("1.2.3",                  loaded!.Version);
        Assert.Equal("2025-06-01T12:00:00Z",   loaded.SaveTimestamp);
        Assert.Equal("crossroads",             loaded.MapId);
        Assert.Equal(987654321UL,              loaded.MatchSeed);
        Assert.Equal(45000UL,                  loaded.TotalTicks);
        Assert.Equal(1500.0,                   loaded.DurationSeconds, 6);
        Assert.Equal(1,                        loaded.WinnerPlayerId);
        Assert.Null(loaded.MissionId);
    }

    [Fact]
    public void ReplayData_MissionId_WhenSet_RoundTrips()
    {
        var original = new ReplayData
        {
            MissionId = "arcloft_mission_3",
            Players   = System.Array.Empty<ReplayPlayerInfo>(),
            Commands  = new List<ReplayCommandEntry>()
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayData>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal("arcloft_mission_3", loaded!.MissionId);
    }

    [Fact]
    public void ReplayData_Players_RoundTrips()
    {
        var original = new ReplayData
        {
            Players = new[]
            {
                new ReplayPlayerInfo { PlayerId = 1, FactionId = "bastion", PlayerName = "P1", IsAI = false },
                new ReplayPlayerInfo { PlayerId = 2, FactionId = "valkyr",  PlayerName = "AI", IsAI = true  }
            },
            Commands = new List<ReplayCommandEntry>()
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayData>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Players.Length);
        Assert.Equal("bastion", loaded.Players[0].FactionId);
        Assert.Equal("valkyr",  loaded.Players[1].FactionId);
        Assert.True(loaded.Players[1].IsAI);
    }

    [Fact]
    public void ReplayData_Commands_RoundTrips()
    {
        var original = new ReplayData
        {
            Players  = System.Array.Empty<ReplayPlayerInfo>(),
            Commands = new List<ReplayCommandEntry>
            {
                new() { Tick = 10,  PlayerId = 1, Type = "Move",   UnitIds = new[] { 1, 2 }, TargetUnitId = -1 },
                new() { Tick = 100, PlayerId = 2, Type = "Attack", UnitIds = new[] { 5 },    TargetUnitId = 7  }
            }
        };

        string json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<ReplayData>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Commands.Count);
        Assert.Equal(10UL,   loaded.Commands[0].Tick);
        Assert.Equal("Move", loaded.Commands[0].Type);
        Assert.Equal(100UL,  loaded.Commands[1].Tick);
        Assert.Equal(7,      loaded.Commands[1].TargetUnitId);
    }

    [Fact]
    public void ReplayData_DefaultVersion_IsSet()
    {
        // The default initialiser sets Version = "0.1.0"
        var data = new ReplayData();
        Assert.Equal("0.1.0", data.Version);
    }

    [Fact]
    public void ReplayData_DefaultWinnerPlayerId_IsMinusOne()
    {
        var data = new ReplayData();
        Assert.Equal(-1, data.WinnerPlayerId);
    }

    [Fact]
    public void ReplayData_UsesSnakeCasePropertyNames()
    {
        var data = new ReplayData
        {
            MapId    = "test",
            Players  = System.Array.Empty<ReplayPlayerInfo>(),
            Commands = new List<ReplayCommandEntry>()
        };

        string json = JsonSerializer.Serialize(data, Options);

        Assert.Contains("\"version\"",          json);
        Assert.Contains("\"save_timestamp\"",   json);
        Assert.Contains("\"map_id\"",           json);
        Assert.Contains("\"match_seed\"",       json);
        Assert.Contains("\"total_ticks\"",      json);
        Assert.Contains("\"duration_seconds\"", json);
        Assert.Contains("\"winner_player_id\"", json);
        Assert.Contains("\"mission_id\"",       json);
        Assert.Contains("\"players\"",          json);
        Assert.Contains("\"commands\"",         json);
    }
}
