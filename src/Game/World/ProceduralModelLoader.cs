using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace CorditeWars.Game.World;

/// <summary>
/// Builds a Godot <see cref="Node3D"/> scene graph from a <see cref="ProceduralModelData"/>
/// definition.  Used by <see cref="PropPlacer"/> as a fallback when a model ID is not
/// found in the TerrainManifest, and by the Model Designer UI for live preview.
///
/// Each primitive is realised as a <see cref="MeshInstance3D"/> with a
/// <see cref="StandardMaterial3D"/> in unshaded mode so models look correct even without
/// a light rig.
/// </summary>
public static class ProceduralModelLoader
{
    private const string ModelsDir = "data/props/models";

    // ── JSON options ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to load a procedural model for <paramref name="modelId"/>.
    /// Returns <c>null</c> if no matching file exists (PropPlacer will then use its
    /// built-in placeholder cube).
    /// </summary>
    public static Node3D? TryLoad(string modelId)
    {
        string path = ModelPath(modelId);
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ProceduralModelData>(json, JsonOptions);
            if (data is null) return null;
            return Build(data);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[ProceduralModelLoader] Failed to load '{modelId}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads and deserializes a <see cref="ProceduralModelData"/> from disk.
    /// Returns <c>null</c> when the file is absent or malformed.
    /// </summary>
    public static ProceduralModelData? LoadData(string modelId)
    {
        string path = ModelPath(modelId);
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProceduralModelData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[ProceduralModelLoader] Failed to read '{modelId}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Serializes <paramref name="data"/> and writes it to the models directory.
    /// </summary>
    public static void Save(ProceduralModelData data)
    {
        Directory.CreateDirectory(ModelsDir);
        string path = ModelPath(data.Id);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        File.WriteAllText(path, json);
        GD.Print($"[ProceduralModelLoader] Saved model '{data.Id}' → {path}");
    }

    /// <summary>
    /// Instantiates a live <see cref="Node3D"/> from the given model data.
    /// Suitable for both runtime placement and designer preview.
    /// </summary>
    public static Node3D Build(ProceduralModelData data)
    {
        var root = new Node3D();
        root.Name = data.Id;

        foreach (var prim in data.Primitives)
        {
            var mesh = CreateMesh(prim);
            if (mesh is null) continue;

            var mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.MaterialOverride = CreateMaterial(prim.Color);

            // Apply transform
            if (prim.Position is { Length: 3 })
                mi.Position = new Vector3(prim.Position[0], prim.Position[1], prim.Position[2]);

            if (prim.RotationDeg is { Length: 3 })
                mi.RotationDegrees = new Vector3(prim.RotationDeg[0], prim.RotationDeg[1], prim.RotationDeg[2]);

            if (prim.Scale is { Length: 3 })
                mi.Scale = new Vector3(prim.Scale[0], prim.Scale[1], prim.Scale[2]);

            root.AddChild(mi);
        }

        return root;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static Mesh? CreateMesh(ProceduralPrimitive prim)
    {
        // Scale drives mesh size via the node transform; mesh itself is unit-sized.
        return prim.Shape switch
        {
            PrimitiveShape.Box      => new BoxMesh     { Size = Vector3.One },
            PrimitiveShape.Sphere   => new SphereMesh  { Radius = 0.5f, Height = 1f },
            PrimitiveShape.Cylinder => new CylinderMesh{ TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f },
            PrimitiveShape.Cone     => new CylinderMesh{ TopRadius = 0f,   BottomRadius = 0.5f, Height = 1f },
            PrimitiveShape.Capsule  => new CapsuleMesh { Radius = 0.5f, Height = 1f },
            _                       => null
        };
    }

    private static StandardMaterial3D CreateMaterial(string htmlColor)
    {
        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.AlbedoColor = ParseColor(htmlColor);
        return mat;
    }

    private static Color ParseColor(string html)
    {
        return Color.FromHtml(html.StartsWith('#') ? html : "#" + html);
    }

    private static string ModelPath(string modelId)
        => Path.Combine(ModelsDir, $"{modelId}.model.json");
}
