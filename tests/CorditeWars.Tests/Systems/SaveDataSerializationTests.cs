using System.Text.Json;
using CorditeWars.Core;
using CorditeWars.Systems.Persistence;

namespace CorditeWars.Tests.Systems;

/// <summary>
/// Tests for <see cref="SaveGameData"/> serialization and
/// <see cref="FixedPointSaveJsonConverter"/> round-trip fidelity.
/// All save values must survive a JSON encode→decode cycle without any
/// precision loss, since the simulation checksum depends on exact FixedPoint
/// values.
/// </summary>
public class SaveDataSerializationTests
{
    // ── JSON options (same as SaveManager uses internally) ───────────────────

    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase));
        return opts;
    }

    // ── FixedPointSaveJsonConverter ─────────────────────────────────────────

    [Fact]
    public void FixedPointSaveJsonConverter_RoundTrips_Zero()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new FixedPointSaveJsonConverter());

        FixedPoint value = FixedPoint.Zero;
        string json = JsonSerializer.Serialize(value, opts);
        FixedPoint roundTripped = JsonSerializer.Deserialize<FixedPoint>(json, opts);

        Assert.Equal(value.Raw, roundTripped.Raw);
    }

    [Fact]
    public void FixedPointSaveJsonConverter_RoundTrips_PositiveValue()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new FixedPointSaveJsonConverter());

        FixedPoint value = FixedPoint.FromInt(100);
        string json = JsonSerializer.Serialize(value, opts);
        FixedPoint roundTripped = JsonSerializer.Deserialize<FixedPoint>(json, opts);

        Assert.Equal(value.Raw, roundTripped.Raw);
    }

    [Fact]
    public void FixedPointSaveJsonConverter_RoundTrips_FractionalValue()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new FixedPointSaveJsonConverter());

        FixedPoint value = FixedPoint.FromFloat(3.14f);
        string json = JsonSerializer.Serialize(value, opts);
        FixedPoint roundTripped = JsonSerializer.Deserialize<FixedPoint>(json, opts);

        // Raw int must be bit-identical (lossless round-trip)
        Assert.Equal(value.Raw, roundTripped.Raw);
    }

    [Fact]
    public void FixedPointSaveJsonConverter_WritesRawInteger()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new FixedPointSaveJsonConverter());

        FixedPoint value = FixedPoint.FromInt(1); // raw = 65536 in Q16.16
        string json = JsonSerializer.Serialize(value, opts);

        // The serialized form should be the raw integer, not a float string like "1.0"
        Assert.DoesNotContain(".", json);
        Assert.Equal(value.Raw.ToString(), json);
    }

    // ── SaveGameData ────────────────────────────────────────────────────────

    [Fact]
    public void SaveGameData_RoundTrips_DefaultValues()
    {
        var opts = CreateOptions();

        var data = new SaveGameData
        {
            Version = "0.1.0",
            ProtocolVersion = 1,
            SaveTimestamp = "2025-01-01T00:00:00Z",
            MapId = "test_map",
            MatchSeed = 42UL,
            CurrentTick = 1000UL,
            GameSpeed = 1,
            FogOfWar = true,
            StartingCordite = 5000
        };

        string json = JsonSerializer.Serialize(data, opts);
        SaveGameData? loaded = JsonSerializer.Deserialize<SaveGameData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(data.Version, loaded.Version);
        Assert.Equal(data.ProtocolVersion, loaded.ProtocolVersion);
        Assert.Equal(data.SaveTimestamp, loaded.SaveTimestamp);
        Assert.Equal(data.MapId, loaded.MapId);
        Assert.Equal(data.MatchSeed, loaded.MatchSeed);
        Assert.Equal(data.CurrentTick, loaded.CurrentTick);
        Assert.Equal(data.GameSpeed, loaded.GameSpeed);
        Assert.Equal(data.FogOfWar, loaded.FogOfWar);
        Assert.Equal(data.StartingCordite, loaded.StartingCordite);
    }

    [Fact]
    public void SaveGameData_RoundTrips_RngState()
    {
        var opts = CreateOptions();

        var data = new SaveGameData
        {
            RngState0 = 0xDEADBEEF_CAFEBABE,
            RngState1 = 0x0123456789ABCDEF,
            RngState2 = 0xFEDCBA9876543210,
            RngState3 = 0x1111111111111111
        };

        string json = JsonSerializer.Serialize(data, opts);
        SaveGameData? loaded = JsonSerializer.Deserialize<SaveGameData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(data.RngState0, loaded.RngState0);
        Assert.Equal(data.RngState1, loaded.RngState1);
        Assert.Equal(data.RngState2, loaded.RngState2);
        Assert.Equal(data.RngState3, loaded.RngState3);
    }

    [Fact]
    public void SaveGameData_DefaultVersion_IsSet()
    {
        var data = new SaveGameData();
        Assert.Equal("0.1.0", data.Version);
    }

    [Fact]
    public void SaveGameData_DefaultProtocolVersion_IsOne()
    {
        var data = new SaveGameData();
        Assert.Equal(1, data.ProtocolVersion);
    }

    [Fact]
    public void SaveGameData_EmptyArrayFields_RoundTrip()
    {
        var opts = CreateOptions();

        var data = new SaveGameData
        {
            Players = [],
            Units = [],
            Buildings = [],
            Harvesters = [],
            CorditeNodes = [],
            CommandHistory = []
        };

        string json = JsonSerializer.Serialize(data, opts);
        SaveGameData? loaded = JsonSerializer.Deserialize<SaveGameData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Players);
        Assert.Empty(loaded.Units);
        Assert.Empty(loaded.Buildings);
        Assert.Empty(loaded.Harvesters);
        Assert.Empty(loaded.CorditeNodes);
        Assert.Empty(loaded.CommandHistory);
    }

    // ── PlayerSaveData ──────────────────────────────────────────────────────

    [Fact]
    public void PlayerSaveData_RoundTrips_ResourceFields()
    {
        var opts = CreateOptions();

        var player = new PlayerSaveData
        {
            PlayerId = 1,
            FactionId = "bastion",
            PlayerName = "TestPlayer",
            IsAI = false,
            AIDifficulty = 0,
            Cordite = 10000L,
            VoltaicCharge = 300L,
            CurrentSupply = 20,
            MaxSupply = 200,
            ReactorCount = 2,
            RefineryCount = 1,
            DepotCount = 3,
            CompletedUpgrades = ["upgrade_a", "upgrade_b"],
            CurrentResearch = "upgrade_c",
            ResearchProgress = 32768L,
            CompletedBuildings = ["bastion_barracks"]
        };

        string json = JsonSerializer.Serialize(player, opts);
        PlayerSaveData? loaded = JsonSerializer.Deserialize<PlayerSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(player.PlayerId, loaded.PlayerId);
        Assert.Equal(player.FactionId, loaded.FactionId);
        Assert.Equal(player.PlayerName, loaded.PlayerName);
        Assert.Equal(player.IsAI, loaded.IsAI);
        Assert.Equal(player.Cordite, loaded.Cordite);
        Assert.Equal(player.VoltaicCharge, loaded.VoltaicCharge);
        Assert.Equal(player.CurrentSupply, loaded.CurrentSupply);
        Assert.Equal(player.MaxSupply, loaded.MaxSupply);
        Assert.Equal(player.ReactorCount, loaded.ReactorCount);
        Assert.Equal(player.RefineryCount, loaded.RefineryCount);
        Assert.Equal(player.DepotCount, loaded.DepotCount);
        Assert.Equal(player.CurrentResearch, loaded.CurrentResearch);
        Assert.Equal(player.ResearchProgress, loaded.ResearchProgress);
        Assert.Equal(2, loaded.CompletedUpgrades.Length);
        Assert.Single(loaded.CompletedBuildings);
    }

    [Fact]
    public void PlayerSaveData_NullCurrentResearch_RoundTrips()
    {
        var opts = CreateOptions();

        var player = new PlayerSaveData
        {
            PlayerId = 2,
            CurrentResearch = null
        };

        string json = JsonSerializer.Serialize(player, opts);
        PlayerSaveData? loaded = JsonSerializer.Deserialize<PlayerSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Null(loaded.CurrentResearch);
    }

    // ── UnitSaveData ─────────────────────────────────────────────────────────

    [Fact]
    public void UnitSaveData_RoundTrips_AllFields()
    {
        var opts = CreateOptions();

        var unit = new UnitSaveData
        {
            UnitId = 42,
            UnitTypeId = "bastion_infantry",
            PlayerId = 1,
            PositionX = FixedPoint.FromInt(10).Raw,
            PositionY = FixedPoint.FromInt(20).Raw,
            Facing = FixedPoint.FromFloat(1.57f).Raw,
            Health = FixedPoint.FromInt(80).Raw,
            IsAlive = true,
            CurrentOrderType = "Move",
            TargetX = FixedPoint.FromInt(50).Raw,
            TargetY = FixedPoint.FromInt(60).Raw,
            TargetUnitId = -1
        };

        string json = JsonSerializer.Serialize(unit, opts);
        UnitSaveData? loaded = JsonSerializer.Deserialize<UnitSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(unit.UnitId, loaded.UnitId);
        Assert.Equal(unit.UnitTypeId, loaded.UnitTypeId);
        Assert.Equal(unit.PlayerId, loaded.PlayerId);
        Assert.Equal(unit.PositionX, loaded.PositionX);
        Assert.Equal(unit.PositionY, loaded.PositionY);
        Assert.Equal(unit.Facing, loaded.Facing);
        Assert.Equal(unit.Health, loaded.Health);
        Assert.Equal(unit.IsAlive, loaded.IsAlive);
        Assert.Equal(unit.CurrentOrderType, loaded.CurrentOrderType);
        Assert.Equal(unit.TargetX, loaded.TargetX);
        Assert.Equal(unit.TargetY, loaded.TargetY);
        Assert.Equal(unit.TargetUnitId, loaded.TargetUnitId);
    }

    // ── BuildingSaveData ─────────────────────────────────────────────────────

    [Fact]
    public void BuildingSaveData_RoundTrips_AllFields()
    {
        var opts = CreateOptions();

        var building = new BuildingSaveData
        {
            BuildingId = 100001,
            BuildingTypeId = "bastion_barracks",
            PlayerId = 1,
            PositionX = 15,
            PositionY = 22,
            Health = FixedPoint.FromInt(1000).Raw,
            IsConstructed = true,
            ConstructionProgress = FixedPoint.FromInt(100).Raw,
            ProductionQueue = new ProductionQueueSaveData
            {
                CurrentUnitTypeId = "bastion_infantry",
                CurrentProgress = FixedPoint.FromFloat(0.5f).Raw,
                CurrentBuildTime = FixedPoint.FromInt(10).Raw,
                QueuedUnitTypeIds = ["bastion_infantry", "bastion_scout"]
            }
        };

        string json = JsonSerializer.Serialize(building, opts);
        BuildingSaveData? loaded = JsonSerializer.Deserialize<BuildingSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(building.BuildingId, loaded.BuildingId);
        Assert.Equal(building.BuildingTypeId, loaded.BuildingTypeId);
        Assert.Equal(building.PositionX, loaded.PositionX);
        Assert.Equal(building.PositionY, loaded.PositionY);
        Assert.Equal(building.Health, loaded.Health);
        Assert.True(loaded.IsConstructed);
        Assert.NotNull(loaded.ProductionQueue);
        Assert.Equal("bastion_infantry", loaded.ProductionQueue.CurrentUnitTypeId);
        Assert.Equal(2, loaded.ProductionQueue.QueuedUnitTypeIds.Length);
    }

    [Fact]
    public void BuildingSaveData_NullProductionQueue_RoundTrips()
    {
        var opts = CreateOptions();

        var building = new BuildingSaveData
        {
            BuildingId = 100002,
            BuildingTypeId = "bastion_reactor",
            PlayerId = 1,
            ProductionQueue = null
        };

        string json = JsonSerializer.Serialize(building, opts);
        BuildingSaveData? loaded = JsonSerializer.Deserialize<BuildingSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Null(loaded.ProductionQueue);
    }

    // ── HarvesterSaveData ────────────────────────────────────────────────────

    [Fact]
    public void HarvesterSaveData_RoundTrips_AllFields()
    {
        var opts = CreateOptions();

        var harvester = new HarvesterSaveData
        {
            UnitId = 7,
            PlayerId = 1,
            State = "Gathering",
            CorditeCarrying = 250,
            AssignedNodeId = 3,
            AssignedRefineryId = 100001
        };

        string json = JsonSerializer.Serialize(harvester, opts);
        HarvesterSaveData? loaded = JsonSerializer.Deserialize<HarvesterSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(harvester.UnitId, loaded.UnitId);
        Assert.Equal(harvester.PlayerId, loaded.PlayerId);
        Assert.Equal(harvester.State, loaded.State);
        Assert.Equal(harvester.CorditeCarrying, loaded.CorditeCarrying);
        Assert.Equal(harvester.AssignedNodeId, loaded.AssignedNodeId);
        Assert.Equal(harvester.AssignedRefineryId, loaded.AssignedRefineryId);
    }

    // ── CorditeNodeSaveData ──────────────────────────────────────────────────

    [Fact]
    public void CorditeNodeSaveData_RoundTrips_AllFields()
    {
        var opts = CreateOptions();

        var node = new CorditeNodeSaveData
        {
            NodeId = 5,
            PositionX = 30,
            PositionY = 45,
            RemainingCordite = 8000
        };

        string json = JsonSerializer.Serialize(node, opts);
        CorditeNodeSaveData? loaded = JsonSerializer.Deserialize<CorditeNodeSaveData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(node.NodeId, loaded.NodeId);
        Assert.Equal(node.PositionX, loaded.PositionX);
        Assert.Equal(node.PositionY, loaded.PositionY);
        Assert.Equal(node.RemainingCordite, loaded.RemainingCordite);
    }

    // ── SavedCommand ─────────────────────────────────────────────────────────

    [Fact]
    public void SavedCommand_RoundTrips_AllFields()
    {
        var opts = CreateOptions();

        var cmd = new SavedCommand
        {
            Tick = 500UL,
            PlayerId = 2,
            CommandType = "Move"
        };

        string json = JsonSerializer.Serialize(cmd, opts);
        SavedCommand? loaded = JsonSerializer.Deserialize<SavedCommand>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(cmd.Tick, loaded.Tick);
        Assert.Equal(cmd.PlayerId, loaded.PlayerId);
        Assert.Equal(cmd.CommandType, loaded.CommandType);
    }

    // ── Full save document round-trip ─────────────────────────────────────────

    [Fact]
    public void SaveGameData_FullDocument_RoundTrips()
    {
        var opts = CreateOptions();

        var data = new SaveGameData
        {
            Version = "0.1.0",
            ProtocolVersion = 1,
            SaveTimestamp = "2025-06-15T12:34:56Z",
            MapId = "crossroads",
            MatchSeed = 99999UL,
            CurrentTick = 3600UL,
            GameSpeed = 1,
            FogOfWar = true,
            StartingCordite = 5000,
            RngState0 = 1,
            RngState1 = 2,
            RngState2 = 3,
            RngState3 = 4,
            Players =
            [
                new PlayerSaveData
                {
                    PlayerId = 1,
                    FactionId = "bastion",
                    PlayerName = "Human",
                    IsAI = false,
                    Cordite = 7500L,
                    VoltaicCharge = 0L,
                    CurrentSupply = 15,
                    MaxSupply = 200
                },
                new PlayerSaveData
                {
                    PlayerId = 2,
                    FactionId = "arcloft",
                    PlayerName = "AI",
                    IsAI = true,
                    AIDifficulty = 1
                }
            ],
            Units =
            [
                new UnitSaveData
                {
                    UnitId = 1,
                    UnitTypeId = "bastion_infantry",
                    PlayerId = 1,
                    Health = FixedPoint.FromInt(100).Raw,
                    IsAlive = true
                }
            ],
            Buildings =
            [
                new BuildingSaveData
                {
                    BuildingId = -100,
                    BuildingTypeId = "bastion_command_center",
                    PlayerId = 1,
                    IsConstructed = true
                }
            ],
            CommandHistory =
            [
                new SavedCommand { Tick = 1, PlayerId = 1, CommandType = "Move" }
            ]
        };

        string json = JsonSerializer.Serialize(data, opts);
        SaveGameData? loaded = JsonSerializer.Deserialize<SaveGameData>(json, opts);

        Assert.NotNull(loaded);
        Assert.Equal(data.MapId, loaded.MapId);
        Assert.Equal(data.MatchSeed, loaded.MatchSeed);
        Assert.Equal(data.CurrentTick, loaded.CurrentTick);
        Assert.Equal(2, loaded.Players.Length);
        Assert.Single(loaded.Units);
        Assert.Single(loaded.Buildings);
        Assert.Single(loaded.CommandHistory);
        Assert.Equal(data.Players[0].FactionId, loaded.Players[0].FactionId);
        Assert.Equal(data.Players[1].IsAI, loaded.Players[1].IsAI);
        Assert.Equal(data.Units[0].UnitTypeId, loaded.Units[0].UnitTypeId);
        Assert.Equal(data.Buildings[0].BuildingId, loaded.Buildings[0].BuildingId);
    }
}
