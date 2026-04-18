using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Factions;
using CorditeWars.Game.Units;

namespace CorditeWars.Tests.Game.Factions;

/// <summary>
/// Tests for <see cref="FixedPointJsonConverter"/>,
/// <see cref="NullableFixedPointJsonConverter"/>, <see cref="FactionData"/>,
/// and the in-memory query methods on <see cref="FactionRegistry"/>.
/// All tests are Godot-free.
/// </summary>
public class FactionDataAndJsonConverterTests
{
    // ══════════════════════════════════════════════════════════════════
    // FixedPointJsonConverter
    // ══════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions ConverterOptions = new()
    {
        Converters = { new FixedPointJsonConverter() }
    };

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(3.5f)]
    [InlineData(-2.25f)]
    [InlineData(100.0f)]
    public void FixedPointConverter_RoundTrip_FloatValue(float value)
    {
        FixedPoint original = FixedPoint.FromFloat(value);
        string json = JsonSerializer.Serialize(original, ConverterOptions);
        FixedPoint deserialized = JsonSerializer.Deserialize<FixedPoint>(json, ConverterOptions);

        // Allow a small rounding tolerance from float → FixedPoint conversion
        Assert.InRange(Math.Abs(deserialized.ToFloat() - value), 0f, 0.01f);
    }

    [Fact]
    public void FixedPointConverter_Write_ProducesNumber()
    {
        FixedPoint val = FixedPoint.FromFloat(2.5f);
        string json = JsonSerializer.Serialize(val, ConverterOptions);

        // Must be a JSON number (no quotes)
        Assert.DoesNotContain("\"", json);
        Assert.True(double.TryParse(json, out _), $"Expected numeric JSON but got: {json}");
    }

    [Fact]
    public void FixedPointConverter_Read_IntegerToken_Works()
    {
        // JSON integers are also a valid Number token
        FixedPoint val = JsonSerializer.Deserialize<FixedPoint>("5", ConverterOptions);
        Assert.Equal(5f, val.ToFloat(), 2);
    }

    [Fact]
    public void FixedPointConverter_Read_NonNumber_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<FixedPoint>("\"not_a_number\"", ConverterOptions));
    }

    // ══════════════════════════════════════════════════════════════════
    // NullableFixedPointJsonConverter
    // ══════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions NullableOptions = new()
    {
        Converters = { new NullableFixedPointJsonConverter() }
    };

    [Fact]
    public void NullableFixedPointConverter_Null_RoundTrips()
    {
        FixedPoint? original = null;
        string json = JsonSerializer.Serialize(original, NullableOptions);
        FixedPoint? deserialized = JsonSerializer.Deserialize<FixedPoint?>(json, NullableOptions);
        Assert.Null(deserialized);
        Assert.Equal("null", json);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.5f)]
    [InlineData(-3.0f)]
    public void NullableFixedPointConverter_NonNull_RoundTrips(float value)
    {
        FixedPoint? original = FixedPoint.FromFloat(value);
        string json = JsonSerializer.Serialize(original, NullableOptions);
        FixedPoint? deserialized = JsonSerializer.Deserialize<FixedPoint?>(json, NullableOptions);
        Assert.NotNull(deserialized);
        Assert.InRange(Math.Abs(deserialized!.Value.ToFloat() - value), 0f, 0.01f);
    }

    [Fact]
    public void NullableFixedPointConverter_Write_NonNull_ProducesNumber()
    {
        FixedPoint? val = FixedPoint.FromFloat(7.0f);
        string json = JsonSerializer.Serialize(val, NullableOptions);
        Assert.DoesNotContain("\"", json);
        Assert.True(double.TryParse(json, out _));
    }

    // ══════════════════════════════════════════════════════════════════
    // FactionData – defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void FactionData_Defaults_AreExpected()
    {
        var data = new FactionData();

        Assert.Equal(string.Empty, data.Id);
        Assert.Equal(string.Empty, data.DisplayName);
        Assert.Equal(default(FactionArchetype), data.Archetype);
        Assert.Equal(string.Empty, data.Description);
        Assert.Equal(string.Empty, data.AestheticTheme);
        Assert.Equal(0, data.ColorIndex);
        Assert.Equal("#FFFFFF", data.PrimaryColor);
        Assert.Equal("#AAAAAA", data.SecondaryColor);
        Assert.Equal("#FFFFFF", data.AccentColor);
        Assert.Equal(FixedPoint.One, data.HarvesterSpeedMod);
        Assert.Equal(FixedPoint.One, data.BuildSpeedMod);
        Assert.Equal(FixedPoint.One, data.UnitBuildSpeedMod);
        Assert.Equal(FixedPoint.One, data.IncomeMod);
        Assert.Equal(FixedPoint.One, data.DefenseCostMod);
        Assert.Equal(FixedPoint.One, data.AirUnitCostMod);
        Assert.Equal(FixedPoint.One, data.GroundUnitCostMod);
        Assert.Equal(FixedPoint.One, data.RepairRateMod);
        Assert.Equal(string.Empty, data.FactionMechanicId);
        Assert.Empty(data.AvailableUnitIds);
        Assert.Empty(data.AvailableBuildingIds);
        Assert.Empty(data.TechTreeUnlocks);
    }

    [Fact]
    public void FactionData_AssignedValues_ArePreserved()
    {
        var data = new FactionData
        {
            Id = "valkyr",
            DisplayName = "Valkyr Confederation",
            Archetype = FactionArchetype.AirPrimary,
            Description = "Aerial supremacy faction.",
            AestheticTheme = "sleek_hightech",
            ColorIndex = 2,
            PrimaryColor = "#2196F3",
            SecondaryColor = "#0D47A1",
            AccentColor = "#64B5F6",
            HarvesterSpeedMod = FixedPoint.FromFloat(1.1f),
            BuildSpeedMod = FixedPoint.FromFloat(0.9f),
            UnitBuildSpeedMod = FixedPoint.FromFloat(1.2f),
            IncomeMod = FixedPoint.FromFloat(0.8f),
            DefenseCostMod = FixedPoint.FromFloat(1.3f),
            AirUnitCostMod = FixedPoint.FromFloat(0.85f),
            GroundUnitCostMod = FixedPoint.FromFloat(1.05f),
            RepairRateMod = FixedPoint.FromFloat(1.5f),
            FactionMechanicId = "air_dominance",
            AvailableUnitIds = ["valkyr_interceptor", "valkyr_gunship"],
            AvailableBuildingIds = ["valkyr_airfield"],
            TechTreeUnlocks = new Dictionary<string, FixedPoint>
            {
                { "adv_avionics", FixedPoint.FromFloat(500f) }
            }
        };

        Assert.Equal("valkyr", data.Id);
        Assert.Equal(FactionArchetype.AirPrimary, data.Archetype);
        Assert.Equal("#2196F3", data.PrimaryColor);
        Assert.Equal(2, data.AvailableUnitIds.Count);
        Assert.Contains("valkyr_interceptor", data.AvailableUnitIds);
        Assert.Single(data.TechTreeUnlocks);
    }

    [Fact]
    public void FactionArchetype_AllValuesRepresented()
    {
        // Enumerate all enum values to ensure no cast errors
        var values = Enum.GetValues<FactionArchetype>();
        Assert.True(values.Length >= 6);
    }

    // ══════════════════════════════════════════════════════════════════
    // FactionRegistry – in-memory query methods (no Godot file I/O)
    // ══════════════════════════════════════════════════════════════════

    private static FactionRegistry BuildRegistry()
    {
        var reg = new FactionRegistry();

        reg.Factions.Add("arcloft", new FactionData
        {
            Id = "arcloft",
            AvailableUnitIds = ["arcloft_tank", "arcloft_infantry"],
            AvailableBuildingIds = ["arcloft_hq", "arcloft_factory"]
        });
        reg.Factions.Add("valkyr", new FactionData
        {
            Id = "valkyr",
            AvailableUnitIds = ["valkyr_interceptor"],
            AvailableBuildingIds = ["valkyr_airfield"]
        });

        reg.Units.Add("arcloft_tank", new UnitData { Id = "arcloft_tank", FactionId = "arcloft" });
        reg.Units.Add("arcloft_infantry", new UnitData { Id = "arcloft_infantry", FactionId = "arcloft" });
        reg.Units.Add("valkyr_interceptor", new UnitData { Id = "valkyr_interceptor", FactionId = "valkyr" });

        reg.Buildings.Add("arcloft_hq", new BuildingData { Id = "arcloft_hq", FactionId = "arcloft" });
        reg.Buildings.Add("arcloft_factory", new BuildingData { Id = "arcloft_factory", FactionId = "arcloft" });
        reg.Buildings.Add("valkyr_airfield", new BuildingData { Id = "valkyr_airfield", FactionId = "valkyr" });

        return reg;
    }

    [Fact]
    public void FactionRegistry_GetFaction_ReturnsCorrectData()
    {
        var reg = BuildRegistry();
        FactionData faction = reg.GetFaction("arcloft");
        Assert.Equal("arcloft", faction.Id);
    }

    [Fact]
    public void FactionRegistry_GetFaction_UnknownId_Throws()
    {
        var reg = BuildRegistry();
        Assert.Throws<KeyNotFoundException>(() => reg.GetFaction("unknown_faction"));
    }

    [Fact]
    public void FactionRegistry_GetUnit_ReturnsCorrectData()
    {
        var reg = BuildRegistry();
        UnitData unit = reg.GetUnit("valkyr_interceptor");
        Assert.Equal("valkyr", unit.FactionId);
    }

    [Fact]
    public void FactionRegistry_GetUnit_UnknownId_Throws()
    {
        var reg = BuildRegistry();
        Assert.Throws<KeyNotFoundException>(() => reg.GetUnit("nonexistent_unit"));
    }

    [Fact]
    public void FactionRegistry_GetBuilding_ReturnsCorrectData()
    {
        var reg = BuildRegistry();
        BuildingData building = reg.GetBuilding("arcloft_hq");
        Assert.Equal("arcloft", building.FactionId);
    }

    [Fact]
    public void FactionRegistry_GetBuilding_UnknownId_Throws()
    {
        var reg = BuildRegistry();
        Assert.Throws<KeyNotFoundException>(() => reg.GetBuilding("nonexistent_building"));
    }

    [Fact]
    public void FactionRegistry_GetUnitsForFaction_ReturnsOnlyMatchingFaction()
    {
        var reg = BuildRegistry();
        List<UnitData> units = reg.GetUnitsForFaction("arcloft");
        Assert.Equal(2, units.Count);
        Assert.All(units, u => Assert.Equal("arcloft", u.FactionId));
    }

    [Fact]
    public void FactionRegistry_GetUnitsForFaction_NoMatches_ReturnsEmpty()
    {
        var reg = BuildRegistry();
        Assert.Empty(reg.GetUnitsForFaction("bastion"));
    }

    [Fact]
    public void FactionRegistry_GetBuildingsForFaction_ReturnsOnlyMatchingFaction()
    {
        var reg = BuildRegistry();
        List<BuildingData> buildings = reg.GetBuildingsForFaction("valkyr");
        Assert.Single(buildings);
        Assert.Equal("valkyr_airfield", buildings[0].Id);
    }

    [Fact]
    public void FactionRegistry_GetBuildingsForFaction_NoMatches_ReturnsEmpty()
    {
        var reg = BuildRegistry();
        Assert.Empty(reg.GetBuildingsForFaction("bastion"));
    }

    [Fact]
    public void FactionRegistry_PublicCollections_InitiallyEmpty()
    {
        var reg = new FactionRegistry();
        Assert.Empty(reg.Factions);
        Assert.Empty(reg.Units);
        Assert.Empty(reg.Buildings);
    }
}
