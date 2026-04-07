using Godot;

namespace CorditeWars.Game.Units;

/// <summary>
/// C# port of the GDScript CohesiveMaterialFactory.
/// Creates and applies cohesive flat-shaded materials using the shared
/// cohesive_flat.gdshader. Walks MeshInstance3D children and replaces
/// their surface materials with configured ShaderMaterial instances.
/// </summary>
public static class CohesiveMaterial
{
    private const string ShaderPath = "res://assets/shaders/cohesive_flat.gdshader";

    private static Shader? _cachedShader;

    /// <summary>
    /// Creates a fully configured ShaderMaterial for a unit mesh surface.
    /// </summary>
    public static ShaderMaterial CreateUnitMaterial(
        Color baseColor,
        Color teamColor,
        bool useVertexColor = true,
        int lightBands = 4)
    {
        var mat = MakeBaseMaterial();

        mat.SetShaderParameter("base_color", baseColor);
        mat.SetShaderParameter("use_vertex_color", useVertexColor);
        mat.SetShaderParameter("light_bands", Mathf.Clamp(lightBands, 2, 8));
        mat.SetShaderParameter("team_color", teamColor);
        mat.SetShaderParameter("team_color_strength", 0.25f);

        // Default preset values
        mat.SetShaderParameter("band_smoothness", 0.05f);
        mat.SetShaderParameter("ambient_strength", 0.3f);
        mat.SetShaderParameter("ambient_color", new Color(0.2f, 0.2f, 0.25f, 1.0f));
        mat.SetShaderParameter("rim_power", 3.0f);
        mat.SetShaderParameter("rim_strength", 0.15f);
        mat.SetShaderParameter("rim_color", new Color(1.0f, 1.0f, 1.0f, 1.0f));
        mat.SetShaderParameter("noise_strength", 0.02f);
        mat.SetShaderParameter("noise_scale", 30.0f);
        mat.SetShaderParameter("enforce_palette", false);
        mat.SetShaderParameter("palette_strength", 0.8f);
        mat.SetShaderParameter("outline_width", 0.0f);
        mat.SetShaderParameter("is_outline_pass", false);

        return mat;
    }

    /// <summary>
    /// Walks the scene tree rooted at <paramref name="root"/> and replaces
    /// every MeshInstance3D's surface materials with cohesive shader materials.
    /// Preserves the original surface albedo color as a tint.
    /// </summary>
    public static void ApplyToScene(Node3D root, Color teamColor)
    {
        WalkAndApply(root, teamColor);
    }

    private static void WalkAndApply(Node node, Color teamColor)
    {
        if (node is MeshInstance3D meshInstance)
        {
            ReplaceMaterials(meshInstance, teamColor);
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            WalkAndApply(node.GetChild(i), teamColor);
        }
    }

    private static void ReplaceMaterials(MeshInstance3D meshInstance, Color teamColor)
    {
        Mesh? mesh = meshInstance.Mesh;
        if (mesh is null)
            return;

        int surfaceCount = mesh.GetSurfaceCount();

        for (int surfaceIdx = 0; surfaceIdx < surfaceCount; surfaceIdx++)
        {
            Color originalColor = Colors.White;
            Material? originalMat = meshInstance.GetActiveMaterial(surfaceIdx);

            if (originalMat is StandardMaterial3D stdMat)
            {
                originalColor = stdMat.AlbedoColor;
            }
            else if (originalMat is BaseMaterial3D baseMat)
            {
                originalColor = baseMat.AlbedoColor;
            }
            else if (originalMat is ShaderMaterial shaderMat)
            {
                Variant existingColor = shaderMat.GetShaderParameter("base_color");
                if (existingColor.VariantType == Variant.Type.Color)
                {
                    originalColor = existingColor.AsColor();
                }
            }

            ShaderMaterial newMat = CreateUnitMaterial(
                originalColor,
                teamColor,
                useVertexColor: true,
                lightBands: 4);

            meshInstance.SetSurfaceOverrideMaterial(surfaceIdx, newMat);
        }
    }

    private static ShaderMaterial MakeBaseMaterial()
    {
        if (_cachedShader is null)
        {
            _cachedShader = GD.Load<Shader>(ShaderPath);
            if (_cachedShader is null)
            {
                GD.PushError(
                    $"[CohesiveMaterial] Could not load shader at '{ShaderPath}'. " +
                    "Check that the file exists and is imported.");
                return new ShaderMaterial();
            }
        }

        var mat = new ShaderMaterial();
        mat.Shader = _cachedShader;
        return mat;
    }
}
