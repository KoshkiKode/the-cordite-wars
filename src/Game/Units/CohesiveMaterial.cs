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

    /// <summary>Weight of the original surface color in the faction-base-color blend (0–1).</summary>
    internal const float BaseColorWeight = 0.45f;
    /// <summary>Weight of the faction base color in the blend (0–1). Must satisfy BaseColorWeight + FactionColorWeight == 1.</summary>
    internal const float FactionColorWeight = 0.55f;

    // Compile-time guard: weights must sum to exactly 1.0 to form a proper lerp.
    static CohesiveMaterial()
    {
        const float tolerance = 0.0001f;
        float sum = BaseColorWeight + FactionColorWeight;
        if (sum < 1.0f - tolerance || sum > 1.0f + tolerance)
        {
            GD.PushError(
                $"[CohesiveMaterial] BaseColorWeight ({BaseColorWeight}) + FactionColorWeight ({FactionColorWeight}) " +
                $"= {sum}; must equal 1.0. Fix the blend constants.");
        }
    }

    /// <summary>
    /// Blends <paramref name="baseColor"/> with <paramref name="factionColor"/> using the shared
    /// <see cref="BaseColorWeight"/>/<see cref="FactionColorWeight"/> ratio.
    /// Used both in the shader material path and the fallback mesh path so the formula stays in sync.
    /// </summary>
    internal static Color BlendWithFaction(Color baseColor, Color factionColor) =>
        new Color(
            baseColor.R * BaseColorWeight + factionColor.R * FactionColorWeight,
            baseColor.G * BaseColorWeight + factionColor.G * FactionColorWeight,
            baseColor.B * BaseColorWeight + factionColor.B * FactionColorWeight,
            baseColor.A);

    private static Shader? _cachedShader;

    /// <summary>
    /// Creates a fully configured ShaderMaterial for a unit mesh surface.
    /// </summary>
    /// <param name="baseColor">Original surface albedo color from the mesh.</param>
    /// <param name="teamColor">Player/faction primary color applied as a tint.</param>
    /// <param name="factionBaseColor">Faction secondary color blended into the model base.</param>
    /// <param name="rimColor">Color of the rim/edge glow — use faction primary for strong identity.</param>
    /// <param name="teamColorStrength">How strongly teamColor tints the model (0–1). Higher = more vivid faction look.</param>
    /// <param name="useVertexColor">Whether to read albedo from mesh vertex colors.</param>
    /// <param name="lightBands">Number of cel-shading bands (2–8).</param>
    public static ShaderMaterial CreateUnitMaterial(
        Color baseColor,
        Color teamColor,
        Color factionBaseColor,
        Color rimColor,
        float teamColorStrength,
        bool useVertexColor = true,
        int lightBands = 4)
    {
        var mat = MakeBaseMaterial();

        // Blend faction base color into the surface base color for model identity
        Color blendedBase = BlendWithFaction(baseColor, factionBaseColor);

        mat.SetShaderParameter("base_color", blendedBase);
        mat.SetShaderParameter("use_vertex_color", useVertexColor);
        mat.SetShaderParameter("light_bands", Mathf.Clamp(lightBands, 2, 8));
        mat.SetShaderParameter("team_color", teamColor);
        mat.SetShaderParameter("team_color_strength", Mathf.Clamp(teamColorStrength, 0.0f, 1.0f));

        // Default preset values
        mat.SetShaderParameter("band_smoothness", 0.05f);
        mat.SetShaderParameter("ambient_strength", 0.3f);
        mat.SetShaderParameter("ambient_color", new Color(0.2f, 0.2f, 0.25f, 1.0f));
        mat.SetShaderParameter("rim_power", 3.0f);
        mat.SetShaderParameter("rim_strength", 0.30f); // stronger rim so faction rim color reads clearly
        mat.SetShaderParameter("rim_color", rimColor);
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
    /// </summary>
    /// <param name="rimColor">Rim/edge glow color — use faction primary for strong visual identity.</param>
    /// <param name="teamColorStrength">
    /// How strongly the team color tints the model. Use a higher value for small units
    /// (e.g. infantry) so they are clearly faction-colored at RTS viewing distances.
    /// </param>
    public static void ApplyToScene(Node3D root, Color teamColor, Color factionBaseColor, Color rimColor, float teamColorStrength)
    {
        WalkAndApply(root, teamColor, factionBaseColor, rimColor, teamColorStrength);
    }

    /// <summary>
    /// Overload that uses the faction primary color as rim color and a default team-color strength.
    /// Provided for convenience; prefer the full overload for battlefield units.
    /// </summary>
    public static void ApplyToScene(Node3D root, Color teamColor, Color factionBaseColor)
    {
        WalkAndApply(root, teamColor, factionBaseColor, teamColor, 0.28f);
    }

    /// <summary>
    /// Overload that uses white as the faction base color (no faction tint).
    /// </summary>
    public static void ApplyToScene(Node3D root, Color teamColor)
    {
        WalkAndApply(root, teamColor, Colors.White, teamColor, 0.28f);
    }

    private static void WalkAndApply(Node node, Color teamColor, Color factionBaseColor, Color rimColor, float teamColorStrength)
    {
        if (node is MeshInstance3D meshInstance)
        {
            ReplaceMaterials(meshInstance, teamColor, factionBaseColor, rimColor, teamColorStrength);
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            WalkAndApply(node.GetChild(i), teamColor, factionBaseColor, rimColor, teamColorStrength);
        }
    }

    private static void ReplaceMaterials(MeshInstance3D meshInstance, Color teamColor, Color factionBaseColor, Color rimColor, float teamColorStrength)
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
                factionBaseColor,
                rimColor,
                teamColorStrength,
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
