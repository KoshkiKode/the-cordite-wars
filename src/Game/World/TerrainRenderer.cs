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
    private float _noiseStrength = 0.08f;
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

    // Terrain shader source — vertex-color based with simple directional lighting
    private const string TerrainShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert, specular_disabled;

varying vec3 v_color;

void vertex() {
    v_color = COLOR.rgb;
}

void fragment() {
    ALBEDO = v_color;
    ROUGHNESS = 0.9;
    METALLIC = 0.0;
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
        for (int y0 = 0; y0 < height - 1; y0 += ChunkCellSize)
        {
            int y1 = Math.Min(height - 1, y0 + ChunkCellSize);
            for (int x0 = 0; x0 < width - 1; x0 += ChunkCellSize)
            {
                int x1 = Math.Min(width - 1, x0 + ChunkCellSize);
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
                float elev = _elevationMap[y * globalWidth + x];

                // Compute vertex color based on biome + features
                Color color = GetVertexColor(x, y, globalWidth, globalHeight, isRiver, isPath);

                st.SetColor(color);

                // Compute normal from neighboring elevations
                Vector3 normal = ComputeNormal(x, y, globalWidth, globalHeight);
                st.SetNormal(normal);

                st.SetUV(new Vector2((float)x / globalWidth, (float)y / globalHeight));
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
