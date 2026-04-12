using System.Text.Json.Serialization;

namespace CorditeWars.Game.World;

/// <summary>
/// Data format for a procedurally-defined 3D model composed of coloured primitives.
/// Files are stored as JSON at data/props/models/{id}.model.json and authored
/// through <see cref="CorditeWars.UI.ModelDesigner.ModelDesignerUI"/>.
///
/// PropPlacer will load these files automatically when a model ID is not found in the
/// TerrainManifest, replacing the generic placeholder cube with the designed geometry.
/// </summary>
public sealed class ProceduralModelData
{
    /// <summary>Unique model identifier matching the PropData.ModelId that references it.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable display name shown in the Model Designer.</summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional category tag used to group models in the designer browser.
    /// Examples: "tree", "rock", "ruin", "structure".
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>All primitives that make up this model, applied in array order.</summary>
    [JsonPropertyName("primitives")]
    public ProceduralPrimitive[] Primitives { get; set; } = [];
}

/// <summary>Primitive types that can appear in a <see cref="ProceduralModelData"/>.</summary>
public enum PrimitiveShape
{
    Box,
    Sphere,
    Cylinder,
    Cone,
    Capsule,
}

/// <summary>
/// A single coloured primitive within a <see cref="ProceduralModelData"/>.
/// All position/rotation/scale values are in local model space.
/// Rotation is stored in degrees (Euler XYZ).
/// </summary>
public sealed class ProceduralPrimitive
{
    [JsonPropertyName("shape")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrimitiveShape Shape { get; set; } = PrimitiveShape.Box;

    [JsonPropertyName("position")]
    public float[] Position { get; set; } = [0f, 0f, 0f];

    [JsonPropertyName("rotation_deg")]
    public float[] RotationDeg { get; set; } = [0f, 0f, 0f];

    [JsonPropertyName("scale")]
    public float[] Scale { get; set; } = [1f, 1f, 1f];

    /// <summary>
    /// HTML colour string, e.g. "#8B7355".
    /// Rendered as an unshaded (emission-only) material to stay visible without lighting setup.
    /// </summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#808080";
}
