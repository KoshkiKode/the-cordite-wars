using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CorditeWars.Systems.Persistence;

namespace CorditeWars.Tests.Systems;

public class SaveFileCodecTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    [Fact]
    public void EncodeDecode_ProprietaryPayload_RoundTrips()
    {
        var opts = CreateOptions();
        var data = new SaveGameData
        {
            MapId = "crossroads",
            CurrentTick = 1024,
            MatchSeed = 12345,
            Players =
            [
                new PlayerSaveData
                {
                    PlayerId = 1,
                    FactionId = "arcloft",
                    PlayerName = string.Empty,
                    IsAI = false
                }
            ]
        };

        byte[] payload = SaveFileCodec.Encode(data, opts);
        SaveGameData? loaded = SaveFileCodec.Decode(payload, opts);

        Assert.NotNull(loaded);
        Assert.Equal(data.MapId, loaded.MapId);
        Assert.Equal(data.CurrentTick, loaded.CurrentTick);
        Assert.Equal(data.MatchSeed, loaded.MatchSeed);
        Assert.Single(loaded.Players);
        Assert.Equal(string.Empty, loaded.Players[0].PlayerName);
    }

    [Fact]
    public void Decode_LegacyJsonPayload_RoundTrips()
    {
        var opts = CreateOptions();
        var data = new SaveGameData
        {
            MapId = "archipelago",
            CurrentTick = 55
        };

        string json = JsonSerializer.Serialize(data, opts);
        byte[] payload = Encoding.UTF8.GetBytes(json);

        SaveGameData? loaded = SaveFileCodec.Decode(payload, opts);

        Assert.NotNull(loaded);
        Assert.Equal("archipelago", loaded.MapId);
        Assert.Equal(55UL, loaded.CurrentTick);
    }

    [Fact]
    public void Encode_ProducesProprietaryHeader()
    {
        var opts = CreateOptions();
        var data = new SaveGameData { MapId = "test" };

        byte[] payload = SaveFileCodec.Encode(data, opts);

        Assert.True(SaveFileCodec.IsProprietaryPayload(payload));
        Assert.NotEqual((byte)'{', payload[0]);
    }
}
