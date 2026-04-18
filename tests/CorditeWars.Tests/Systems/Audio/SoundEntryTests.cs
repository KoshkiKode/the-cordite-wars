using System;
using System.Text.Json;
using CorditeWars.Systems.Audio;

namespace CorditeWars.Tests.Systems.Audio;

/// <summary>
/// Tests for <see cref="SoundEntry"/> — the pure-data model loaded from
/// the sound manifest. No Godot runtime or file I/O required.
/// </summary>
public class SoundEntryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ══════════════════════════════════════════════════════════════════
    // SoundEntry — defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SoundEntry_Defaults_AreExpected()
    {
        var entry = new SoundEntry();

        Assert.NotNull(entry.Files);
        Assert.Empty(entry.Files);
        Assert.Equal(0.8f, entry.Volume);
        Assert.Equal(0.0f, entry.PitchVariation);
        Assert.Equal(50.0f, entry.MaxDistance);
        Assert.Equal(5, entry.Priority);
        Assert.Equal(0.0f, entry.Cooldown);
        Assert.Null(entry.Subtitles);
    }

    // ══════════════════════════════════════════════════════════════════
    // SoundEntry — assigned values
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SoundEntry_AssignedValues_ArePreserved()
    {
        var entry = new SoundEntry
        {
            Files = ["sfx/cannon_fire_01.wav", "sfx/cannon_fire_02.wav"],
            Volume = 0.9f,
            PitchVariation = 0.1f,
            MaxDistance = 80.0f,
            Priority = 8,
            Cooldown = 0.3f,
            Subtitles = ["Firing cannon!"]
        };

        Assert.Equal(2, entry.Files.Length);
        Assert.Equal("sfx/cannon_fire_01.wav", entry.Files[0]);
        Assert.Equal(0.9f, entry.Volume);
        Assert.Equal(0.1f, entry.PitchVariation);
        Assert.Equal(80.0f, entry.MaxDistance);
        Assert.Equal(8, entry.Priority);
        Assert.Equal(0.3f, entry.Cooldown);
        Assert.NotNull(entry.Subtitles);
        Assert.Single(entry.Subtitles!);
    }

    // ══════════════════════════════════════════════════════════════════
    // SoundEntry — JSON round-trip
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SoundEntry_JsonRoundTrip_PreservesAllFields()
    {
        var original = new SoundEntry
        {
            Files = ["sfx/explosion_large.wav"],
            Volume = 1.0f,
            PitchVariation = 0.05f,
            MaxDistance = 120.0f,
            Priority = 9,
            Cooldown = 0.5f,
            Subtitles = null
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        var loaded = JsonSerializer.Deserialize<SoundEntry>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Files);
        Assert.Equal("sfx/explosion_large.wav", loaded.Files[0]);
        Assert.Equal(1.0f, loaded.Volume);
        Assert.Equal(0.05f, loaded.PitchVariation, 5);
        Assert.Equal(120.0f, loaded.MaxDistance);
        Assert.Equal(9, loaded.Priority);
        Assert.Equal(0.5f, loaded.Cooldown, 5);
        Assert.Null(loaded.Subtitles);
    }

    [Fact]
    public void SoundEntry_JsonPropertyNames_UsePascalCase()
    {
        var entry = new SoundEntry
        {
            Files = ["test.wav"],
            Volume = 0.5f,
            PitchVariation = 0.0f
        };

        string json = JsonSerializer.Serialize(entry, JsonOptions);

        Assert.Contains("\"Files\"", json);
        Assert.Contains("\"Volume\"", json);
        Assert.Contains("\"PitchVariation\"", json);
        Assert.Contains("\"MaxDistance\"", json);
        Assert.Contains("\"Priority\"", json);
        Assert.Contains("\"Cooldown\"", json);
    }

    [Fact]
    public void SoundEntry_EmptyFiles_SerializesAsEmptyArray()
    {
        var entry = new SoundEntry { Files = Array.Empty<string>() };
        string json = JsonSerializer.Serialize(entry, JsonOptions);

        Assert.Contains("\"Files\":[]", json.Replace(" ", ""));
    }

    [Fact]
    public void SoundEntry_MultipleFiles_AllPreservedInOrder()
    {
        var entry = new SoundEntry
        {
            Files = ["a.wav", "b.wav", "c.wav"]
        };

        string json = JsonSerializer.Serialize(entry, JsonOptions);
        var loaded = JsonSerializer.Deserialize<SoundEntry>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Files.Length);
        Assert.Equal("a.wav", loaded.Files[0]);
        Assert.Equal("b.wav", loaded.Files[1]);
        Assert.Equal("c.wav", loaded.Files[2]);
    }

    [Fact]
    public void SoundEntry_WithSubtitles_RoundTrips()
    {
        var entry = new SoundEntry
        {
            Files = ["voice/line_01.wav"],
            Subtitles = ["This is a voice line."]
        };

        string json = JsonSerializer.Serialize(entry, JsonOptions);
        var loaded = JsonSerializer.Deserialize<SoundEntry>(json, JsonOptions);

        Assert.NotNull(loaded?.Subtitles);
        Assert.Single(loaded!.Subtitles!);
        Assert.Equal("This is a voice line.", loaded.Subtitles![0]);
    }
}
