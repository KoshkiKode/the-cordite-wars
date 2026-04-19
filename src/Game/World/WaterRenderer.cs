using System;
using Godot;
using CorditeWars.Systems.Graphics;

namespace CorditeWars.Game.World;

/// <summary>
/// Renders water planes along river TerrainFeature paths.
/// Animated scrolling UV shader with transparency and subtle waves.
/// Placed at slightly below terrain river channel elevation.
/// </summary>
public partial class WaterRenderer : Node3D
{
    private const float WaterYOffset = -0.3f;
    private const float WaterPlaneWidth = 8f;

    // Full animated water shader for Medium/High quality.
    // depth_draw_never: transparent water must NOT write to the depth buffer —
    // using depth_draw_opaque with blend_mix causes transparent surfaces to
    // incorrectly occlude geometry behind them in the depth test.
    private const string WaterShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_schlick_ggx;

uniform vec4 water_color : source_color = vec4(0.15, 0.3, 0.55, 0.7);
uniform vec4 water_color_deep : source_color = vec4(0.08, 0.18, 0.35, 0.85);
uniform float scroll_speed : hint_range(0.0, 2.0) = 0.15;
uniform float wave_amplitude : hint_range(0.0, 1.0) = 0.08;
uniform float wave_frequency : hint_range(0.0, 10.0) = 2.5;
uniform float foam_threshold : hint_range(0.0, 1.0) = 0.7;

void vertex() {
    float wave = sin(VERTEX.x * wave_frequency + TIME * 2.0) *
                 cos(VERTEX.z * wave_frequency * 0.7 + TIME * 1.5);
    VERTEX.y += wave * wave_amplitude;
}

void fragment() {
    vec2 uv_scroll = UV + vec2(TIME * scroll_speed, TIME * scroll_speed * 0.3);

    // Simple procedural wave pattern
    float pattern = sin(uv_scroll.x * 12.0) * cos(uv_scroll.y * 8.0) * 0.5 + 0.5;

    vec3 color = mix(water_color_deep.rgb, water_color.rgb, pattern * 0.6);

    // Foam near edges (using UV proximity to 0/1)
    float edge = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    if (edge < 0.1) {
        float foam = smoothstep(0.1, 0.0, edge) * 0.3;
        color = mix(color, vec3(0.8, 0.85, 0.9), foam);
    }

    ALBEDO = color;
    ALPHA = mix(water_color_deep.a, water_color.a, pattern * 0.5);
    ROUGHNESS = 0.1;
    METALLIC = 0.2;
    SPECULAR = 0.8;
}
";

    // Simplified static water shader for Potato/Low quality.
    // No vertex animation, no scrolling UV — minimal GPU cost.
    private const string WaterShaderSimpleSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled;

uniform vec4 water_color : source_color = vec4(0.15, 0.3, 0.55, 0.65);

void fragment() {
    ALBEDO = water_color.rgb;
    ALPHA = water_color.a;
    ROUGHNESS = 0.5;
    METALLIC = 0.0;
}
";

    private ShaderMaterial _waterMaterial = null!;

    /// <summary>
    /// Generates water planes for all river features in the map data.
    /// Must be called after TerrainRenderer.Generate() so we can sample elevation.
    /// </summary>
    public void Generate(MapData mapData, TerrainRenderer terrainRenderer, QualityTier tier = QualityTier.Medium)
    {
        // Remove previous generated content if regenerating (e.g., map editor updates).
        foreach (Node child in GetChildren())
            child.QueueFree();

        // Choose animated vs static water based on quality tier
        string shaderSource = tier >= QualityTier.Medium ? WaterShaderSource : WaterShaderSimpleSource;

        // Create shared material
        var shader = new Shader();
        shader.Code = shaderSource;
        _waterMaterial = new ShaderMaterial();
        _waterMaterial.Shader = shader;

        if (mapData.TerrainFeatures == null) return;

        for (int i = 0; i < mapData.TerrainFeatures.Length; i++)
        {
            TerrainFeature feature = mapData.TerrainFeatures[i];

            if (feature.Type == "river" && feature.Points != null && feature.Points.Length >= 2)
            {
                CreateRiverWater(feature, terrainRenderer);
            }
            else if ((feature.Type == "water_body" || feature.Type == "oasis" || feature.Type == "sea_edge")
                     && feature.Points != null && feature.Points.Length >= 2)
            {
                CreateWaterBodyPlane(feature, terrainRenderer);
            }
        }

        GD.Print($"[WaterRenderer] Generated water for {mapData.TerrainFeatures.Length} terrain features.");
    }

    private void CreateRiverWater(TerrainFeature river, TerrainRenderer terrainRenderer)
    {
        // Create a water plane for each segment of the river
        for (int seg = 0; seg < river.Points.Length - 1; seg++)
        {
            int[] p0 = river.Points[seg];
            int[] p1 = river.Points[seg + 1];
            if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

            CreateWaterSegment(p0[0], p0[1], p1[0], p1[1], terrainRenderer);
        }
    }

    private void CreateWaterSegment(int x0, int y0, int x1, int y1, TerrainRenderer terrainRenderer)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float segLength = MathF.Sqrt(dx * dx + dy * dy);
        if (segLength < 0.5f) return;

        // Midpoint of segment
        float midX = (x0 + x1) * 0.5f;
        float midY = (y0 + y1) * 0.5f;

        // Get terrain elevation at midpoint for water height
        float terrainElev = terrainRenderer != null
            ? terrainRenderer.GetElevationAtWorld(midX, midY)
            : 0f;
        float waterY = terrainElev + WaterYOffset;

        // Direction and perpendicular
        float ndx = dx / segLength;
        float ndy = dy / segLength;

        // Create a quad mesh for the water segment
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Perpendicular vector (rotated 90 degrees)
        float perpX = -ndy * WaterPlaneWidth * 0.5f;
        float perpY = ndx * WaterPlaneWidth * 0.5f;

        // Extend slightly along direction for overlap
        float extX = ndx * 0.5f;
        float extY = ndy * 0.5f;

        // Four corners of the water quad
        Vector3 v0 = new(x0 - extX + perpX, waterY, y0 - extY + perpY);
        Vector3 v1 = new(x0 - extX - perpX, waterY, y0 - extY - perpY);
        Vector3 v2 = new(x1 + extX + perpX, waterY, y1 + extY + perpY);
        Vector3 v3 = new(x1 + extX - perpX, waterY, y1 + extY - perpY);

        Vector3 normal = Vector3.Up;

        // Triangle 1
        st.SetNormal(normal);
        st.SetUV(new Vector2(0, 0));
        st.AddVertex(v0);

        st.SetNormal(normal);
        st.SetUV(new Vector2(0, 1));
        st.AddVertex(v1);

        st.SetNormal(normal);
        st.SetUV(new Vector2(1, 0));
        st.AddVertex(v2);

        // Triangle 2
        st.SetNormal(normal);
        st.SetUV(new Vector2(0, 1));
        st.AddVertex(v1);

        st.SetNormal(normal);
        st.SetUV(new Vector2(1, 1));
        st.AddVertex(v3);

        st.SetNormal(normal);
        st.SetUV(new Vector2(1, 0));
        st.AddVertex(v2);

        var mesh = st.Commit();
        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = mesh;
        meshInstance.MaterialOverride = _waterMaterial;
        meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        AddChild(meshInstance);
    }

    /// <summary>
    /// Renders a large water body (ocean/lake/sea) defined by a 2-point rectangle or
    /// polygon in the terrain feature data.  Uses a deeper-colour variant of the water
    /// shader to visually distinguish open water from rivers.
    ///
    /// <para>For 2-point features: points[0] = top-left corner, points[1] = bottom-right.
    /// For polygon features: the bounding box is computed and a single large plane is
    /// rendered at the mean elevation of the region.</para>
    /// </summary>
    private void CreateWaterBodyPlane(TerrainFeature body, TerrainRenderer terrainRenderer)
    {
        if (body.Points == null || body.Points.Length < 2) return;

        // Compute bounding box
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        for (int i = 0; i < body.Points.Length; i++)
        {
            int[] pt = body.Points[i];
            if (pt == null || pt.Length < 2) continue;
            if (pt[0] < minX) minX = pt[0];
            if (pt[1] < minY) minY = pt[1];
            if (pt[0] > maxX) maxX = pt[0];
            if (pt[1] > maxY) maxY = pt[1];
        }

        if (minX == int.MaxValue) return;

        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float width  = maxX - minX;
        float height = maxY - minY;

        // Sample elevation at the centre of the water body
        float terrainElev = terrainRenderer != null
            ? terrainRenderer.GetElevationAtWorld(cx, cy)
            : 0f;
        float waterY = terrainElev + WaterYOffset - 0.1f; // slightly lower for open water

        // Build a single large quad covering the bounding box
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        Vector3 v0 = new(minX, waterY, minY);
        Vector3 v1 = new(maxX, waterY, minY);
        Vector3 v2 = new(minX, waterY, maxY);
        Vector3 v3 = new(maxX, waterY, maxY);

        Vector3 normal = Vector3.Up;
        float uvScale = 0.05f; // tile the shader pattern over the large water body

        st.SetNormal(normal); st.SetUV(new Vector2(0,               0));               st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(new Vector2(width * uvScale, 0));               st.AddVertex(v1);
        st.SetNormal(normal); st.SetUV(new Vector2(0,               height * uvScale)); st.AddVertex(v2);

        st.SetNormal(normal); st.SetUV(new Vector2(width * uvScale, 0));               st.AddVertex(v1);
        st.SetNormal(normal); st.SetUV(new Vector2(width * uvScale, height * uvScale)); st.AddVertex(v3);
        st.SetNormal(normal); st.SetUV(new Vector2(0,               height * uvScale)); st.AddVertex(v2);

        var mesh = st.Commit();
        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = mesh;
        meshInstance.MaterialOverride = _waterMaterial;
        meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        AddChild(meshInstance);
    }
}
