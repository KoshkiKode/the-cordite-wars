using System.Text.Json;
using CorditeWars.Game.World;

namespace CorditeWars.Tests.Game.World;

/// <summary>
/// Tests for <see cref="ProceduralModelData"/> and <see cref="ProceduralPrimitive"/>
/// — pure data models with no Godot dependency.
/// Covers default values, property assignment, and JSON round-trip fidelity.
/// </summary>
public class ProceduralModelDataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ══════════════════════════════════════════════════════════════════
    // ProceduralPrimitive — defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProceduralPrimitive_Defaults_AreExpected()
    {
        var primitive = new ProceduralPrimitive();

        Assert.Equal(PrimitiveShape.Box, primitive.Shape);
        Assert.Equal(3, primitive.Position.Length);
        Assert.Equal(3, primitive.RotationDeg.Length);
        Assert.Equal(3, primitive.Scale.Length);
        Assert.All(primitive.Position, v => Assert.Equal(0f, v));
        Assert.All(primitive.RotationDeg, v => Assert.Equal(0f, v));
        Assert.All(primitive.Scale, v => Assert.Equal(1f, v));
        Assert.Equal("#808080", primitive.Color);
    }

    [Fact]
    public void ProceduralPrimitive_AssignedValues_ArePreserved()
    {
        var primitive = new ProceduralPrimitive
        {
            Shape = PrimitiveShape.Sphere,
            Position = [1f, 2.5f, -0.5f],
            RotationDeg = [0f, 45f, 90f],
            Scale = [2f, 2f, 2f],
            Color = "#FF5733"
        };

        Assert.Equal(PrimitiveShape.Sphere, primitive.Shape);
        Assert.Equal(1f, primitive.Position[0]);
        Assert.Equal(2.5f, primitive.Position[1]);
        Assert.Equal(-0.5f, primitive.Position[2]);
        Assert.Equal(45f, primitive.RotationDeg[1]);
        Assert.Equal(2f, primitive.Scale[0]);
        Assert.Equal("#FF5733", primitive.Color);
    }

    // ══════════════════════════════════════════════════════════════════
    // ProceduralModelData — defaults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProceduralModelData_Defaults_AreExpected()
    {
        var model = new ProceduralModelData();

        Assert.Equal(string.Empty, model.Id);
        Assert.Equal(string.Empty, model.DisplayName);
        Assert.Equal(string.Empty, model.Category);
        Assert.Empty(model.Primitives);
    }

    [Fact]
    public void ProceduralModelData_AssignedValues_ArePreserved()
    {
        var p1 = new ProceduralPrimitive { Shape = PrimitiveShape.Box, Color = "#8B7355" };
        var p2 = new ProceduralPrimitive { Shape = PrimitiveShape.Cylinder, Color = "#228B22" };

        var model = new ProceduralModelData
        {
            Id = "oak_tree_01",
            DisplayName = "Oak Tree",
            Category = "tree",
            Primitives = [p1, p2]
        };

        Assert.Equal("oak_tree_01", model.Id);
        Assert.Equal("Oak Tree", model.DisplayName);
        Assert.Equal("tree", model.Category);
        Assert.Equal(2, model.Primitives.Length);
        Assert.Equal(PrimitiveShape.Box, model.Primitives[0].Shape);
        Assert.Equal(PrimitiveShape.Cylinder, model.Primitives[1].Shape);
    }

    // ══════════════════════════════════════════════════════════════════
    // PrimitiveShape enum
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PrimitiveShape_AllValues_CanBeInstantiated()
    {
        var shapes = new[]
        {
            PrimitiveShape.Box,
            PrimitiveShape.Sphere,
            PrimitiveShape.Cylinder,
            PrimitiveShape.Cone,
            PrimitiveShape.Capsule
        };

        foreach (var shape in shapes)
        {
            var prim = new ProceduralPrimitive { Shape = shape };
            Assert.Equal(shape, prim.Shape);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // JSON round-trip
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProceduralPrimitive_JsonRoundTrip_PreservesAllFields()
    {
        var original = new ProceduralPrimitive
        {
            Shape = PrimitiveShape.Cone,
            Position = [0f, 3f, 0f],
            RotationDeg = [0f, 0f, 0f],
            Scale = [1f, 2.5f, 1f],
            Color = "#FF4500"
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        var loaded = JsonSerializer.Deserialize<ProceduralPrimitive>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(PrimitiveShape.Cone, loaded!.Shape);
        Assert.Equal(0f, loaded.Position[0]);
        Assert.Equal(3f, loaded.Position[1]);
        Assert.Equal(2.5f, loaded.Scale[1]);
        Assert.Equal("#FF4500", loaded.Color);
    }

    [Fact]
    public void ProceduralModelData_JsonRoundTrip_PreservesAllFields()
    {
        var original = new ProceduralModelData
        {
            Id = "rock_small",
            DisplayName = "Small Rock",
            Category = "rock",
            Primitives =
            [
                new ProceduralPrimitive { Shape = PrimitiveShape.Sphere, Color = "#888888" },
                new ProceduralPrimitive { Shape = PrimitiveShape.Box, Color = "#666666" }
            ]
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        var loaded = JsonSerializer.Deserialize<ProceduralModelData>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal("rock_small", loaded!.Id);
        Assert.Equal("Small Rock", loaded.DisplayName);
        Assert.Equal("rock", loaded.Category);
        Assert.Equal(2, loaded.Primitives.Length);
        Assert.Equal(PrimitiveShape.Sphere, loaded.Primitives[0].Shape);
        Assert.Equal(PrimitiveShape.Box, loaded.Primitives[1].Shape);
    }

    [Fact]
    public void ProceduralModelData_JsonPropertyNames_UseSnakeCase()
    {
        var model = new ProceduralModelData { Id = "test", DisplayName = "Test", Category = "misc" };
        string json = JsonSerializer.Serialize(model, JsonOptions);

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"display_name\"", json);
        Assert.Contains("\"category\"", json);
        Assert.Contains("\"primitives\"", json);
    }

    [Fact]
    public void ProceduralPrimitive_JsonPropertyNames_UseSnakeCase()
    {
        var prim = new ProceduralPrimitive();
        string json = JsonSerializer.Serialize(prim, JsonOptions);

        Assert.Contains("\"shape\"", json);
        Assert.Contains("\"position\"", json);
        Assert.Contains("\"rotation_deg\"", json);
        Assert.Contains("\"scale\"", json);
        Assert.Contains("\"color\"", json);
    }
}
