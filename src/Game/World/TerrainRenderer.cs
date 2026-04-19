using System;
using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Systems.Graphics;

namespace CorditeWars.Game.World;

/// <summary>
/// Procedurally generates the ground mesh from MapData.
/// Creates a subdivided plane (Width x Height cells), applies elevation from
/// ElevationZones with smooth falloff, vertex-colors by biome, river channels
/// carved lower with blue tint, path areas lighter. Custom vertex-color shader.
/// </summary>
public partial class TerrainRenderer : Node3D
{
    private MapData _mapData = null!;
    private float[] _elevationMap = null!;
    private Color?[] _featureOverrides = null!;
    private float _noiseStrength = 0.08f;
    // Chunk size used when splitting the terrain mesh. 120 cells gives
    // ~14 400 quads per chunk (28 800 triangles, ~86 400 indices), safely
    // within the 65 535 vertex limit of a 16-bit index buffer used by
    // some GPU drivers, and keeps collision trimesh sizes manageable.
    private const int ChunkCellSize = 120;

    // Biome base colors
    private static readonly Color TemperateGrass = new(0.28f, 0.52f, 0.15f);
    private static readonly Color TemperateDirt = new(0.45f, 0.35f, 0.22f);
    private static readonly Color DesertSand = new(0.76f, 0.65f, 0.42f);
    private static readonly Color DesertRock = new(0.55f, 0.45f, 0.32f);
    private static readonly Color RockyGray = new(0.48f, 0.46f, 0.44f);
    private static readonly Color RockyGreen = new(0.3f, 0.42f, 0.2f);
    private static readonly Color CoastalSand = new(0.72f, 0.66f, 0.48f);
    private static readonly Color CoastalGreen = new(0.25f, 0.48f, 0.22f);
    private static readonly Color RiverBlue = new(0.2f, 0.35f, 0.55f);
    private static readonly Color PathColor = new(0.6f, 0.55f, 0.42f);
    private static readonly Color TropicalGrass = new(0.15f, 0.58f, 0.22f);
    private static readonly Color TropicalSand = new(0.82f, 0.74f, 0.52f);
    private static readonly Color OasisGreen = new(0.2f, 0.55f, 0.25f);
    private static readonly Color OasisBlue = new(0.18f, 0.42f, 0.52f);

    // Terrain shader source — vertex-color biome tint + procedural multi-octave noise detail.
    // No external texture files required: all detail is generated analytically in the shader.
    // The noise produces micro-variation that reads like grass blades, sand grains, or rock
    // facets depending on the underlying biome colour.
    private const string TerrainShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

varying vec3 v_world_pos;
varying vec3 v_biome_color;
varying vec3 v_vertex_normal;

// --- Procedural noise helpers -------------------------------------------------

// Hash function that maps a 2D integer coordinate to a pseudo-random float [0,1].
float hash(vec2 p) {
    p = fract(p * vec2(127.1, 311.7));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Smooth value noise — bicubic interpolation between hashed corners.
float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Fractional Brownian Motion — 4 octaves for natural surface variation.
float fbm(vec2 p) {
    return valueNoise(p)        * 0.500
         + valueNoise(p * 2.1)  * 0.250
         + valueNoise(p * 4.3)  * 0.125
         + valueNoise(p * 8.7)  * 0.063;
}

// --- Vertex stage -------------------------------------------------------------

void vertex() {
    v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_biome_color  = COLOR.rgb;
    v_vertex_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

// --- Fragment stage -----------------------------------------------------------

void fragment() {
    vec2 pos = v_world_pos.xz;

    // Multi-scale noise: fine detail (micro-surface) + coarse (patches)
    float n_fine   = fbm(pos * 10.0);   // grass-blade / grain scale
    float n_coarse = fbm(pos * 1.8);    // soil patch / dune scale

    // Weighted blend: fine detail dominant, coarse provides large-area variation
    float n = n_fine * 0.55 + n_coarse * 0.45;

    // Remap to [0,1] with smooth S-curve
    float surface = smoothstep(0.25, 0.75, n);

    // --- AO simulation: troughs between blades/grains appear darker
    float ao = mix(0.50, 1.00, surface);

    // --- Specular bright peaks simulate dew / quartz / mica glints
    float glint = pow(surface, 6.0) * 0.18;

    // --- Colour from biome vertex colour
    vec3 base = v_biome_color;

    // Slightly desaturate at troughs (dirt visible between grass tufts)
    float lum = dot(base, vec3(0.299, 0.587, 0.114));
    vec3 desaturated = mix(vec3(lum), base, 0.65);
    base = mix(desaturated, base, surface);

    vec3 final_color = base * ao + glint * base;

    ALBEDO    = clamp(final_color, 0.0, 1.0);
    ROUGHNESS = mix(0.95, 0.68, surface);   // troughs rough, peaks slightly smoother
    METALLIC  = 0.0;
    SPECULAR  = 0.07;
    NORMAL_MAP_DEPTH = 0.0;  // no normal map — lighting comes from mesh normals
}
";

    /// <summary>
    /// Generates terrain mesh from the provided MapData.
    /// Call once when loading a map.
    /// </summary>
    public void Generate(MapData mapData, QualityTier tier = QualityTier.Medium)
    {
        _mapData = mapData;

        // Quality-dependent settings
        _noiseStrength = tier switch
        {
            QualityTier.Potato => 0.0f,  // no noise — flat colors save fill-rate
            QualityTier.Low    => 0.04f, // half noise
            _                  => 0.08f  // Medium / High: full variation
        };

        bool castShadows = tier >= QualityTier.Medium;

        // Remove previous generated content if regenerating
        foreach (Node child in GetChildren())
            child.QueueFree();

        int width = _mapData.Width;
        int height = _mapData.Height;

        // Build elevation map
        _elevationMap = new float[width * height];
        BuildElevationMap(width, height);

        // Apply river carving
        ApplyRiverCarving(width, height);

        // Build feature colour overrides (sea_edge, oasis, ridgeline) before mesh gen
        _featureOverrides = BuildFeatureOverrideMap(width, height);

        // Apply vertex-color shader
        var shader = new Shader();
        shader.Code = TerrainShaderSource;
        var material = new ShaderMaterial();
        material.Shader = shader;

        // Build river/path overlays once and generate chunked terrain meshes
        bool[] isRiver = BuildRiverMap(width, height);
        bool[] isPath = BuildPathMap(width, height);
        int chunkCount = GenerateTerrainChunks(width, height, isRiver, isPath, material, castShadows);

        GD.Print($"[TerrainRenderer] Generated terrain {width}x{height} ({chunkCount} chunks), biome={_mapData.Biome}, quality={tier}");
    }

    /// <summary>
    /// Returns terrain elevation at a given grid coordinate (float for rendering).
    /// </summary>
    public float GetElevationAt(int x, int y)
    {
        if (_elevationMap == null || _mapData == null) return 0f;
        if (x < 0 || x >= _mapData.Width || y < 0 || y >= _mapData.Height) return 0f;
        return _elevationMap[y * _mapData.Width + x];
    }

    /// <summary>
    /// Returns interpolated elevation at a world position (fractional coordinates).
    /// </summary>
    public float GetElevationAtWorld(float worldX, float worldZ)
    {
        if (_elevationMap == null || _mapData == null) return 0f;

        float gx = worldX;
        float gz = worldZ;

        int x0 = (int)MathF.Floor(gx);
        int z0 = (int)MathF.Floor(gz);
        int x1 = x0 + 1;
        int z1 = z0 + 1;

        float fx = gx - x0;
        float fz = gz - z0;

        float e00 = GetElevationAt(x0, z0);
        float e10 = GetElevationAt(x1, z0);
        float e01 = GetElevationAt(x0, z1);
        float e11 = GetElevationAt(x1, z1);

        // Bilinear interpolation
        float top = e00 + (e10 - e00) * fx;
        float bottom = e01 + (e11 - e01) * fx;
        return top + (bottom - top) * fz;
    }

    // ── Elevation Map ──────────────────────────────────────────────────────

    private void BuildElevationMap(int width, int height)
    {
        // Base elevation is 0
        Array.Clear(_elevationMap, 0, _elevationMap.Length);

        // Apply each elevation zone with smooth falloff
        if (_mapData.ElevationZones == null) return;

        for (int i = 0; i < _mapData.ElevationZones.Length; i++)
        {
            ElevationZone zone = _mapData.ElevationZones[i];
            float zoneHeight = zone.Height.ToFloat();
            int cx = zone.CenterX;
            int cy = zone.CenterY;
            int radius = zone.Radius;

            if (radius <= 0) continue;

            // Only iterate over the bounding box of the zone
            int minX = Math.Max(0, cx - radius);
            int maxX = Math.Min(width - 1, cx + radius);
            int minY = Math.Max(0, cy - radius);
            int maxY = Math.Min(height - 1, cy + radius);

            float radiusSq = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float distSq = dx * dx + dy * dy;

                    if (distSq >= radiusSq) continue;

                    // Smooth falloff using cosine interpolation
                    float dist = MathF.Sqrt(distSq);
                    float t = dist / radius;
                    float falloff = 0.5f * (1f + MathF.Cos(t * MathF.PI));

                    _elevationMap[y * width + x] += zoneHeight * falloff;
                }
            }
        }
    }

    private void ApplyRiverCarving(int width, int height)
    {
        if (_mapData.TerrainFeatures == null) return;

        for (int i = 0; i < _mapData.TerrainFeatures.Length; i++)
        {
            TerrainFeature feature = _mapData.TerrainFeatures[i];
            if (feature.Type != "river" || feature.Points == null || feature.Points.Length < 2)
                continue;

            // Carve along each segment of the river
            for (int seg = 0; seg < feature.Points.Length - 1; seg++)
            {
                int[] p0 = feature.Points[seg];
                int[] p1 = feature.Points[seg + 1];
                if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

                CarveRiverSegment(width, height, p0[0], p0[1], p1[0], p1[1]);
            }
        }
    }

    private void CarveRiverSegment(int width, int height, int x0, int y0, int x1, int y1)
    {
        const int riverWidth = 4;
        const float carveDepth = -1.5f;

        float dx = x1 - x0;
        float dy = y1 - y0;
        float segLength = MathF.Sqrt(dx * dx + dy * dy);
        if (segLength < 0.01f) return;

        int steps = (int)MathF.Ceiling(segLength);
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            float cx = x0 + dx * t;
            float cy = y0 + dy * t;

            int minX = Math.Max(0, (int)(cx - riverWidth));
            int maxX = Math.Min(width - 1, (int)(cx + riverWidth));
            int minY = Math.Max(0, (int)(cy - riverWidth));
            int maxY = Math.Min(height - 1, (int)(cy + riverWidth));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float rdx = x - cx;
                    float rdy = y - cy;
                    float dist = MathF.Sqrt(rdx * rdx + rdy * rdy);
                    if (dist > riverWidth) continue;

                    float falloff = 1f - (dist / riverWidth);
                    falloff = falloff * falloff; // quadratic falloff
                    _elevationMap[y * width + x] += carveDepth * falloff;
                }
            }
        }
    }

    // ── Mesh Generation ────────────────────────────────────────────────────

    private int GenerateTerrainChunks(int width, int height, bool[] isRiver, bool[] isPath, Material material, bool castShadows)
    {
        int chunkCount = 0;
        for (int y0 = 0; y0 < height; y0 += ChunkCellSize)
        {
            int y1 = Math.Min(height, y0 + ChunkCellSize);
            for (int x0 = 0; x0 < width; x0 += ChunkCellSize)
            {
                int x1 = Math.Min(width, x0 + ChunkCellSize);
                var mesh = BuildTerrainChunkMesh(width, height, x0, y0, x1, y1, isRiver, isPath);

                var meshInstance = new MeshInstance3D();
                meshInstance.Mesh = mesh;
                meshInstance.CastShadow = castShadows
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off;
                meshInstance.MaterialOverride = material;
                AddChild(meshInstance);

                CreateCollisionBody(mesh);
                chunkCount++;
            }
        }
        return chunkCount;
    }

    private ArrayMesh BuildTerrainChunkMesh(
        int globalWidth,
        int globalHeight,
        int x0,
        int y0,
        int x1,
        int y1,
        bool[] isRiver,
        bool[] isPath)
    {
        int chunkWidth = x1 - x0 + 1;
        int chunkHeight = y1 - y0 + 1;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Generate chunk vertices in world-space positions
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int sampleX = Math.Clamp(x, 0, globalWidth - 1);
                int sampleY = Math.Clamp(y, 0, globalHeight - 1);
                float elev = _elevationMap[sampleY * globalWidth + sampleX];

                // Compute vertex color based on biome + features
                Color color = GetVertexColor(sampleX, sampleY, globalWidth, globalHeight, isRiver, isPath);

                st.SetColor(color);

                // Compute normal from neighboring elevations
                Vector3 normal = ComputeNormal(sampleX, sampleY, globalWidth, globalHeight);
                st.SetNormal(normal);

                st.SetUV(new Vector2((float)sampleX / globalWidth, (float)sampleY / globalHeight));
                st.AddVertex(new Vector3(x, elev, y));
            }
        }

        // Generate indices (two triangles per cell) for this chunk-local grid
        for (int y = 0; y < chunkHeight - 1; y++)
        {
            for (int x = 0; x < chunkWidth - 1; x++)
            {
                int topLeft = y * chunkWidth + x;
                int topRight = topLeft + 1;
                int bottomLeft = (y + 1) * chunkWidth + x;
                int bottomRight = bottomLeft + 1;

                // Triangle 1
                st.AddIndex(topLeft);
                st.AddIndex(bottomLeft);
                st.AddIndex(topRight);

                // Triangle 2
                st.AddIndex(topRight);
                st.AddIndex(bottomLeft);
                st.AddIndex(bottomRight);
            }
        }

        return st.Commit();
    }

    private Vector3 ComputeNormal(int x, int y, int width, int height)
    {
        float left = GetElevationAt(Math.Max(0, x - 1), y);
        float right = GetElevationAt(Math.Min(width - 1, x + 1), y);
        float up = GetElevationAt(x, Math.Max(0, y - 1));
        float down = GetElevationAt(x, Math.Min(height - 1, y + 1));

        // Central difference normal computation
        var normal = new Vector3(left - right, 2f, up - down);
        return normal.Normalized();
    }

    // ── Color Assignment ───────────────────────────────────────────────────

    private Color GetVertexColor(int x, int y, int width, int height, bool[] isRiver, bool[] isPath)
    {
        int idx = y * width + x;

        // River override
        if (isRiver[idx])
            return RiverBlue;

        // Path override
        if (isPath[idx])
            return PathColor;

        // Feature-specific override (sea_edge, oasis, ridgeline)
        var featureOverride = _featureOverrides[idx];
        if (featureOverride.HasValue)
            return featureOverride.Value;

        // Biome-based coloring
        string biome = _mapData.Biome ?? "temperate";
        Color baseColor;

        switch (biome)
        {
            case "desert":
            case "volcanic":
                baseColor = GetDesertColor(x, y, width, height);
                break;
            case "rocky":
            case "mountain":
                baseColor = GetRockyColor(x, y, width, height);
                break;
            case "coastal":
            case "archipelago":
                baseColor = GetCoastalColor(x, y, width, height);
                break;
            case "tropical":
                baseColor = GetTropicalColor(x, y, width, height);
                break;
            case "mixed":
                baseColor = GetMixedColor(x, y, width, height);
                break;
            default: // temperate
                baseColor = GetTemperateColor(x, y, width, height);
                break;
        }

        // Add subtle noise variation based on position (strength 0 on Potato, 0.04 on Low, 0.08 on Medium/High)
        if (_noiseStrength > 0f)
        {
            float noise = PseudoNoise(x, y) * _noiseStrength;
            baseColor.R = Math.Clamp(baseColor.R + noise, 0f, 1f);
            baseColor.G = Math.Clamp(baseColor.G + noise * 0.7f, 0f, 1f);
            baseColor.B = Math.Clamp(baseColor.B + noise * 0.5f, 0f, 1f);
        }

        return baseColor;
    }

    private Color GetTemperateColor(int x, int y, int width, int height)
    {
        float elev = _elevationMap[y * width + x];

        // Higher = more rocky, lower = more green
        if (elev > 3f) return TemperateDirt.Lerp(RockyGray, Math.Clamp((elev - 3f) / 4f, 0f, 1f));
        if (elev > 1.5f) return TemperateGrass.Lerp(TemperateDirt, (elev - 1.5f) / 1.5f);

        // Edge fade: near map borders get slight brown
        float edgeDist = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        if (edgeDist < 10f)
        {
            float edgeT = 1f - edgeDist / 10f;
            return TemperateGrass.Lerp(TemperateDirt, edgeT * 0.5f);
        }

        return TemperateGrass;
    }

    private static Color GetDesertColor(int x, int y, int width, int height)
    {
        // Use position-based variation
        float variation = PseudoNoise(x * 3, y * 3);
        if (variation > 0.3f)
            return DesertSand.Lerp(DesertRock, (variation - 0.3f) / 0.7f * 0.5f);
        return DesertSand;
    }

    private Color GetRockyColor(int x, int y, int width, int height)
    {
        float elev = _elevationMap[y * width + x];
        if (elev > 2f) return RockyGray;

        float variation = PseudoNoise(x * 2, y * 2);
        if (variation > 0.6f) return RockyGreen;
        return RockyGray.Lerp(RockyGreen, variation * 0.3f);
    }

    private Color GetCoastalColor(int x, int y, int width, int height)
    {
        float elev = _elevationMap[y * width + x];

        // Low elevation near edges = sand beaches
        float edgeDist = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        if (elev < 0.5f && edgeDist < 20f)
            return CoastalSand;
        if (elev < 1f)
            return CoastalSand.Lerp(CoastalGreen, (elev - 0.5f) * 2f);

        return CoastalGreen;
    }

    private Color GetTropicalColor(int x, int y, int width, int height)
    {
        float elev = _elevationMap[y * width + x];

        // Sandy beaches at low elevation near map edges
        float edgeDist = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        if (edgeDist < 15f && elev < 1.5f)
            return TropicalSand.Lerp(TropicalGrass, MathF.Min(edgeDist / 15f, 1f) * 0.6f);

        // Vibrant jungle floor; slightly darker at higher elevations
        if (elev > 2.5f)
            return TropicalGrass.Darkened(Math.Clamp((elev - 2.5f) * 0.12f, 0f, 0.3f));

        return TropicalGrass;
    }

    private Color GetMixedColor(int x, int y, int width, int height)
    {
        float elev = _elevationMap[y * width + x];

        // Use position noise to choose between the two biome palettes that
        // make up a "mixed" map (temperate interior, rocky/coastal edges).
        float edgeDist = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        float edgeT = 1f - Math.Clamp(edgeDist / 40f, 0f, 1f); // 0 = interior, 1 = edge

        // Sector variation based on diagonal noise
        float sectorNoise = PseudoNoise(x / 32, y / 32) + 0.5f; // [0, 1]
        float t = Math.Clamp(edgeT * 0.6f + sectorNoise * 0.4f, 0f, 1f);

        Color inner = GetTemperateColor(x, y, width, height);
        Color outer = elev > 1.5f ? GetRockyColor(x, y, width, height) : GetCoastalColor(x, y, width, height);
        return inner.Lerp(outer, t);
    }

    // ── Feature Override Map ────────────────────────────────────────────────

    /// <summary>
    /// Builds a per-cell colour override array for terrain features that are not
    /// rivers or paths (those are handled by isRiver/isPath bools).
    /// Handles: sea_edge → CoastalSand strip, oasis → OasisGreen/OasisBlue pool,
    /// ridgeline → RockyGray painted along the polyline.
    /// </summary>
    private Color?[] BuildFeatureOverrideMap(int width, int height)
    {
        var overrides = new Color?[width * height];

        if (_mapData.TerrainFeatures == null) return overrides;

        for (int i = 0; i < _mapData.TerrainFeatures.Length; i++)
        {
            TerrainFeature feature = _mapData.TerrainFeatures[i];
            if (feature.Points == null || feature.Points.Length == 0) continue;

            switch (feature.Type)
            {
                case "sea_edge":
                    PaintBoundingBox(overrides, width, height, feature.Points, CoastalSand);
                    break;

                case "oasis":
                    PaintOasis(overrides, width, height, feature.Points);
                    break;

                case "ridgeline":
                    PaintPolyline(overrides, width, height, feature.Points, RockyGray, brushRadius: 2);
                    break;
            }
        }

        return overrides;
    }

    /// <summary>Fills every cell inside the bounding box of the given points with <paramref name="color"/>.</summary>
    private static void PaintBoundingBox(Color?[] overrides, int width, int height, int[][] points, Color color)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            int[] pt = points[i];
            if (pt == null || pt.Length < 2) continue;
            if (pt[0] < minX) minX = pt[0];
            if (pt[1] < minY) minY = pt[1];
            if (pt[0] > maxX) maxX = pt[0];
            if (pt[1] > maxY) maxY = pt[1];
        }
        if (minX == int.MaxValue) return;

        for (int y = Math.Max(0, minY); y <= Math.Min(height - 1, maxY); y++)
            for (int x = Math.Max(0, minX); x <= Math.Min(width - 1, maxX); x++)
                overrides[y * width + x] = color;
    }

    /// <summary>
    /// Paints an oasis: outer ring as <see cref="OasisGreen"/>, inner half as
    /// <see cref="OasisBlue"/> (shallow water tint).
    /// </summary>
    private static void PaintOasis(Color?[] overrides, int width, int height, int[][] points)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            int[] pt = points[i];
            if (pt == null || pt.Length < 2) continue;
            if (pt[0] < minX) minX = pt[0];
            if (pt[1] < minY) minY = pt[1];
            if (pt[0] > maxX) maxX = pt[0];
            if (pt[1] > maxY) maxY = pt[1];
        }
        if (minX == int.MaxValue) return;

        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float rx = (maxX - minX) * 0.5f;
        float ry = (maxY - minY) * 0.5f;

        for (int y = Math.Max(0, minY); y <= Math.Min(height - 1, maxY); y++)
        {
            for (int x = Math.Max(0, minX); x <= Math.Min(width - 1, maxX); x++)
            {
                // Normalised ellipse distance [0=centre, 1=edge]
                float nx = rx > 0f ? (x - cx) / rx : 0f;
                float ny = ry > 0f ? (y - cy) / ry : 0f;
                float d = MathF.Sqrt(nx * nx + ny * ny);
                if (d > 1f) continue;

                // Centre 50% → shallow water blue; outer 50% → lush green
                overrides[y * width + x] = d < 0.5f ? OasisBlue : OasisGreen;
            }
        }
    }

    /// <summary>Paints a fixed-width brush stroke along a polyline.</summary>
    private static void PaintPolyline(Color?[] overrides, int width, int height, int[][] points,
        Color color, int brushRadius)
    {
        for (int seg = 0; seg < points.Length - 1; seg++)
        {
            int[] p0 = points[seg];
            int[] p1 = points[seg + 1];
            if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

            float dx = p1[0] - p0[0];
            float dy = p1[1] - p0[1];
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) continue;

            int steps = (int)MathF.Ceiling(len);
            float rSq = brushRadius * brushRadius;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float cx = p0[0] + dx * t;
                float cy = p0[1] + dy * t;

                int x0 = Math.Max(0, (int)(cx - brushRadius));
                int x1 = Math.Min(width - 1, (int)(cx + brushRadius));
                int y0 = Math.Max(0, (int)(cy - brushRadius));
                int y1 = Math.Min(height - 1, (int)(cy + brushRadius));

                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float ddx = x - cx, ddy = y - cy;
                        if (ddx * ddx + ddy * ddy <= rSq)
                            overrides[y * width + x] = color;
                    }
            }
        }
    }

    // ── Feature Maps ───────────────────────────────────────────────────────

    private bool[] BuildRiverMap(int width, int height)
    {
        bool[] map = new bool[width * height];
        if (_mapData.TerrainFeatures == null) return map;

        for (int i = 0; i < _mapData.TerrainFeatures.Length; i++)
        {
            TerrainFeature feature = _mapData.TerrainFeatures[i];
            if (feature.Type != "river" || feature.Points == null || feature.Points.Length < 2)
                continue;

            for (int seg = 0; seg < feature.Points.Length - 1; seg++)
            {
                int[] p0 = feature.Points[seg];
                int[] p1 = feature.Points[seg + 1];
                if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

                MarkRiverSegment(map, width, height, p0[0], p0[1], p1[0], p1[1], 3);
            }
        }

        return map;
    }

    private static void MarkRiverSegment(bool[] map, int width, int height,
        int x0, int y0, int x1, int y1, int brushRadius)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float segLength = MathF.Sqrt(dx * dx + dy * dy);
        if (segLength < 0.01f) return;

        int steps = (int)MathF.Ceiling(segLength);
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            float cx = x0 + dx * t;
            float cy = y0 + dy * t;

            int minX = Math.Max(0, (int)(cx - brushRadius));
            int maxX = Math.Min(width - 1, (int)(cx + brushRadius));
            int minY = Math.Max(0, (int)(cy - brushRadius));
            int maxY = Math.Min(height - 1, (int)(cy + brushRadius));

            float rSq = brushRadius * brushRadius;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float ddx = x - cx;
                    float ddy = y - cy;
                    if (ddx * ddx + ddy * ddy <= rSq)
                        map[y * width + x] = true;
                }
            }
        }
    }

    private bool[] BuildPathMap(int width, int height)
    {
        bool[] map = new bool[width * height];
        if (_mapData.TerrainFeatures == null) return map;

        for (int i = 0; i < _mapData.TerrainFeatures.Length; i++)
        {
            TerrainFeature feature = _mapData.TerrainFeatures[i];
            if (feature.Type != "path" || feature.Points == null || feature.Points.Length < 2)
                continue;

            for (int seg = 0; seg < feature.Points.Length - 1; seg++)
            {
                int[] p0 = feature.Points[seg];
                int[] p1 = feature.Points[seg + 1];
                if (p0 == null || p0.Length < 2 || p1 == null || p1.Length < 2) continue;

                MarkRiverSegment(map, width, height, p0[0], p0[1], p1[0], p1[1], 2);
            }
        }

        return map;
    }

    // ── Collision ──────────────────────────────────────────────────────────

    private void CreateCollisionBody(ArrayMesh mesh)
    {
        var staticBody = new StaticBody3D();
        staticBody.CollisionLayer = 1; // terrain layer
        staticBody.CollisionMask = 0;

        var shape = mesh.CreateTrimeshShape();
        var collisionShape = new CollisionShape3D();
        collisionShape.Shape = shape;
        staticBody.AddChild(collisionShape);

        AddChild(staticBody);
    }

    // ── Utility ────────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic pseudo-noise for vertex color variation.
    /// Returns value in [-0.5, 0.5].
    /// </summary>
    private static float PseudoNoise(int x, int y)
    {
        // Simple hash-based noise
        int n = x + y * 57;
        n = (n << 13) ^ n;
        int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
        return (m / (float)0x7fffffff) - 0.5f;
    }
}
