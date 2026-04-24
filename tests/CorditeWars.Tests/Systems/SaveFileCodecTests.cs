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

    // ── Decode error / edge paths ─────────────────────────────────────

    [Fact]
    public void Decode_EmptyPayload_ReturnsNull()
    {
        var opts = CreateOptions();
        SaveGameData? result = SaveFileCodec.Decode(System.Array.Empty<byte>(), opts);
        Assert.Null(result);
    }

    [Fact]
    public void Decode_UnrecognizedFormat_ThrowsInvalidDataException()
    {
        var opts = CreateOptions();
        // Not '{', not the proprietary magic header — should throw.
        byte[] payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Throws<System.IO.InvalidDataException>(
            () => SaveFileCodec.Decode(payload, opts));
    }

    [Fact]
    public void Decode_NullPayload_Throws()
    {
        var opts = CreateOptions();
        Assert.Throws<System.ArgumentNullException>(
            () => SaveFileCodec.Decode(null!, opts));
    }

    [Fact]
    public void Decode_NullOptions_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => SaveFileCodec.Decode(new byte[] { 0x42 }, null!));
    }

    // ── IsProprietaryPayload edge cases ───────────────────────────────

    [Fact]
    public void IsProprietaryPayload_EmptyPayload_ReturnsFalse()
    {
        Assert.False(SaveFileCodec.IsProprietaryPayload(System.Array.Empty<byte>()));
    }

    [Fact]
    public void IsProprietaryPayload_PayloadShorterThanHeader_ReturnsFalse()
    {
        // Magic header is "CORDSAVE" (8 bytes) + 1 version byte; anything shorter is invalid.
        byte[] payload = Encoding.ASCII.GetBytes("CORD"); // 4 bytes
        Assert.False(SaveFileCodec.IsProprietaryPayload(payload));
    }

    [Fact]
    public void IsProprietaryPayload_HeaderByteMismatch_ReturnsFalse()
    {
        // Same length as a valid header but with one byte off.
        byte[] payload = Encoding.ASCII.GetBytes("XORDSAVE");
        // Append a version byte so we get past the length check.
        byte[] full = new byte[payload.Length + 1];
        System.Array.Copy(payload, full, payload.Length);
        full[^1] = 1;
        Assert.False(SaveFileCodec.IsProprietaryPayload(full));
    }

    [Fact]
    public void IsProprietaryPayload_WrongVersionByte_ReturnsFalse()
    {
        // Correct magic header but unsupported version byte.
        byte[] magic = Encoding.ASCII.GetBytes("CORDSAVE");
        byte[] full = new byte[magic.Length + 1];
        System.Array.Copy(magic, full, magic.Length);
        full[^1] = 99; // unknown version
        Assert.False(SaveFileCodec.IsProprietaryPayload(full));
    }
}
