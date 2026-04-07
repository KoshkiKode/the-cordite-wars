using System;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Systems.Pathfinding;
using CorditeWars.Systems.FogOfWar;

namespace CorditeWars.UI.Minimap;

/// <summary>
/// Blip categories rendered on the minimap entity overlay.
/// </summary>
public enum BlipType
{
    Unit,
    Building,
    GhostedBuilding,
    ResourceNode
}

/// <summary>
/// A single entity marker to be drawn on the minimap.
/// </summary>
public struct MinimapBlip
{
    /// <summary>Grid-space X coordinate.</summary>
    public int GridX;

    /// <summary>Grid-space Y coordinate.</summary>
    public int GridY;

    /// <summary>Player index (0-7) used to look up the player color.</summary>
    public int PlayerIndex;

    /// <summary>What kind of blip this is (unit dot, building footprint, etc.).</summary>
    public BlipType Type;

    /// <summary>Width of the footprint in grid cells (1 for units).</summary>
    public int FootprintW;

    /// <summary>Height of the footprint in grid cells (1 for units).</summary>
    public int FootprintH;

    public MinimapBlip(int gridX, int gridY, int playerIndex, BlipType type,
                       int footprintW = 1, int footprintH = 1)
    {
        GridX = gridX;
        GridY = gridY;
        PlayerIndex = playerIndex;
        Type = type;
        FootprintW = footprintW;
        FootprintH = footprintH;
    }
}

/// <summary>
/// The minimap DATA layer — computes raw RGBA pixel buffers that represent
/// terrain, fog-of-war, and entity blips. This is the engine, not the renderer.
/// A separate Godot node consumes these buffers and uploads them to textures/shaders.
/// </summary>
public class MinimapData
{
    // ── Dimensions ───────────────────────────────────────────────────

    /// <summary>Pixel width of the minimap image.</summary>
    public int MinimapWidth { get; }

    /// <summary>Pixel height of the minimap image.</summary>
    public int MinimapHeight { get; }

    /// <summary>Width of the simulation terrain grid (in cells).</summary>
    public int GridWidth { get; }

    /// <summary>Height of the simulation terrain grid (in cells).</summary>
    public int GridHeight { get; }

    // ── Scale factors (float is fine — this is cosmetic UI) ─────────

    private readonly float _gridToMinimapX;
    private readonly float _gridToMinimapY;
    private readonly float _minimapToGridX;
    private readonly float _minimapToGridY;

    // ── Pixel buffers (RGBA, 4 bytes per pixel) ─────────────────────

    /// <summary>Static terrain colors. Generated once when the map loads.</summary>
    public byte[] TerrainPixels { get; }

    /// <summary>Fog overlay. Updated every frame.</summary>
    public byte[] FogOverlay { get; }

    /// <summary>Entity blips overlay. Updated every frame.</summary>
    public byte[] EntityOverlay { get; }

    /// <summary>Final composited image (terrain + fog + entities).</summary>
    public byte[] CompositePixels { get; }

    // ── Player colors (C&C-style 8-player palette) ──────────────────

    private static readonly (byte R, byte G, byte B)[] PlayerColors = new (byte, byte, byte)[]
    {
        (0,   200, 0),    // 0 — Green
        (200, 0,   0),    // 1 — Red
        (0,   80,  220),  // 2 — Blue
        (240, 220, 0),    // 3 — Yellow
        (240, 140, 0),    // 4 — Orange
        (160, 0,   200),  // 5 — Purple
        (0,   190, 190),  // 6 — Teal
        (240, 100, 160),  // 7 — Pink
    };

    // ── Terrain color table ─────────────────────────────────────────

    private static readonly (byte R, byte G, byte B)[] TerrainColors = new (byte, byte, byte)[]
    {
        (34,  85,  34),   // Grass
        (139, 90,  43),   // Dirt
        (210, 180, 100),  // Sand
        (120, 120, 120),  // Rock
        (30,  60,  150),  // Water
        (15,  30,  100),  // DeepWater
        (80,  55,  30),   // Mud
        (160, 155, 145),  // Road
        (180, 220, 240),  // Ice
        (140, 140, 140),  // Concrete
        (130, 110, 90),   // Bridge
        (200, 50,  10),   // Lava
        (0,   0,   0),    // Void
    };

    // ── Constructor ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a new MinimapData with the given pixel dimensions mapped to a grid.
    /// </summary>
    /// <param name="minimapWidth">Desired minimap pixel width (e.g. 256).</param>
    /// <param name="minimapHeight">Desired minimap pixel height (e.g. 256).</param>
    /// <param name="gridWidth">Terrain grid width in cells.</param>
    /// <param name="gridHeight">Terrain grid height in cells.</param>
    public MinimapData(int minimapWidth, int minimapHeight, int gridWidth, int gridHeight)
    {
        MinimapWidth = minimapWidth;
        MinimapHeight = minimapHeight;
        GridWidth = gridWidth;
        GridHeight = gridHeight;

        _gridToMinimapX = (float)minimapWidth / gridWidth;
        _gridToMinimapY = (float)minimapHeight / gridHeight;
        _minimapToGridX = (float)gridWidth / minimapWidth;
        _minimapToGridY = (float)gridHeight / minimapHeight;

        int pixelCount = minimapWidth * minimapHeight * 4;
        TerrainPixels = new byte[pixelCount];
        FogOverlay = new byte[pixelCount];
        EntityOverlay = new byte[pixelCount];
        CompositePixels = new byte[pixelCount];
    }

    // ── Coordinate mapping ──────────────────────────────────────────

    /// <summary>
    /// Converts a grid cell coordinate to the corresponding minimap pixel.
    /// </summary>
    public (int px, int py) GridToMinimap(int gridX, int gridY)
    {
        int px = Math.Clamp((int)(gridX * _gridToMinimapX), 0, MinimapWidth - 1);
        int py = Math.Clamp((int)(gridY * _gridToMinimapY), 0, MinimapHeight - 1);
        return (px, py);
    }

    /// <summary>
    /// Converts a minimap pixel coordinate back to a grid cell.
    /// Used for click-to-scroll.
    /// </summary>
    public (int gx, int gy) MinimapToGrid(int px, int py)
    {
        int gx = Math.Clamp((int)(px * _minimapToGridX), 0, GridWidth - 1);
        int gy = Math.Clamp((int)(py * _minimapToGridY), 0, GridHeight - 1);
        return (gx, gy);
    }

    // ── Terrain layer (called once) ─────────────────────────────────

    /// <summary>
    /// Generates the static terrain colour layer from the simulation grid.
    /// Call once when the map finishes loading.
    /// <para>
    /// If the minimap is smaller than the grid, multiple cells are averaged
    /// per pixel (down-sample). If larger, nearest-neighbour upscale is used.
    /// Terrain height modulates brightness (0.7–1.3 range).
    /// </para>
    /// </summary>
    public void GenerateTerrainLayer(TerrainGrid terrain)
    {
        for (int py = 0; py < MinimapHeight; py++)
        {
            for (int px = 0; px < MinimapWidth; px++)
            {
                // Determine the range of grid cells that map to this pixel.
                float gxStartF = px * _minimapToGridX;
                float gyStartF = py * _minimapToGridY;
                float gxEndF = (px + 1) * _minimapToGridX;
                float gyEndF = (py + 1) * _minimapToGridY;

                int gxStart = Math.Clamp((int)gxStartF, 0, GridWidth - 1);
                int gyStart = Math.Clamp((int)gyStartF, 0, GridHeight - 1);
                int gxEnd = Math.Clamp((int)Math.Ceiling(gxEndF), 1, GridWidth);
                int gyEnd = Math.Clamp((int)Math.Ceiling(gyEndF), 1, GridHeight);

                float rSum = 0f, gSum = 0f, bSum = 0f;
                int sampleCount = 0;

                for (int gy = gyStart; gy < gyEnd; gy++)
                {
                    for (int gx = gxStart; gx < gxEnd; gx++)
                    {
                        if (!terrain.IsInBounds(gx, gy))
                            continue;

                        var cell = terrain.GetCell(gx, gy);
                        int typeIndex = (int)cell.Type;
                        if (typeIndex < 0 || typeIndex >= TerrainColors.Length)
                            typeIndex = TerrainColors.Length - 1; // Void fallback

                        var (cr, cg, cb) = TerrainColors[typeIndex];

                        // Height shading: remap height to a brightness factor [0.7, 1.3].
                        // Assume height is in a roughly [0, 10] range in the simulation;
                        // clamp the factor so extreme heights don't blow out colors.
                        float heightVal = cell.Height.ToFloat();
                        float heightFactor = 0.7f + (heightVal * 0.06f); // 0 → 0.7, 5 → 1.0, 10 → 1.3
                        heightFactor = Math.Clamp(heightFactor, 0.7f, 1.3f);

                        rSum += cr * heightFactor;
                        gSum += cg * heightFactor;
                        bSum += cb * heightFactor;
                        sampleCount++;
                    }
                }

                byte finalR, finalG, finalB;
                if (sampleCount > 0)
                {
                    finalR = (byte)Math.Clamp((int)(rSum / sampleCount), 0, 255);
                    finalG = (byte)Math.Clamp((int)(gSum / sampleCount), 0, 255);
                    finalB = (byte)Math.Clamp((int)(bSum / sampleCount), 0, 255);
                }
                else
                {
                    // Out-of-bounds fallback: black
                    finalR = 0; finalG = 0; finalB = 0;
                }

                int idx = (py * MinimapWidth + px) * 4;
                TerrainPixels[idx + 0] = finalR;
                TerrainPixels[idx + 1] = finalG;
                TerrainPixels[idx + 2] = finalB;
                TerrainPixels[idx + 3] = 255; // fully opaque
            }
        }
    }

    // ── Fog layer (called every frame) ──────────────────────────────

    /// <summary>
    /// Updates the fog overlay from the current fog-of-war state.
    /// <list type="bullet">
    ///   <item><description>Unexplored → fully black (0,0,0,255)</description></item>
    ///   <item><description>Explored → semi-transparent black (0,0,0,160)</description></item>
    ///   <item><description>Visible → fully transparent (0,0,0,0)</description></item>
    /// </list>
    /// </summary>
    public void UpdateFogLayer(FogGrid? fog)
    {
        if (fog is null)
        {
            // No fog grid available — treat everything as fully visible
            Array.Clear(FogOverlay, 0, FogOverlay.Length);
            return;
        }

        for (int py = 0; py < MinimapHeight; py++)
        {
            for (int px = 0; px < MinimapWidth; px++)
            {
                // Sample the grid cell at the centre of this pixel's coverage area.
                var (gx, gy) = MinimapToGrid(px, py);

                byte alpha;
                FogVisibility vis = fog.GetVisibility(gx, gy);
                switch (vis)
                {
                    case FogVisibility.Unexplored:
                        alpha = 255;
                        break;
                    case FogVisibility.Explored:
                        alpha = 160;
                        break;
                    case FogVisibility.Visible:
                    default:
                        alpha = 0;
                        break;
                }

                int idx = (py * MinimapWidth + px) * 4;
                FogOverlay[idx + 0] = 0;
                FogOverlay[idx + 1] = 0;
                FogOverlay[idx + 2] = 0;
                FogOverlay[idx + 3] = alpha;
            }
        }
    }

    // ── Entity layer (called every frame) ───────────────────────────

    /// <summary>
    /// Updates the entity overlay with unit dots, building footprints,
    /// ghosted buildings, and resource nodes.
    /// </summary>
    public void UpdateEntityLayer(List<MinimapBlip> blips)
    {
        // Clear the overlay to fully transparent.
        Array.Clear(EntityOverlay, 0, EntityOverlay.Length);

        foreach (var blip in blips)
        {
            int colorIndex = Math.Clamp(blip.PlayerIndex, 0, PlayerColors.Length - 1);
            var (cr, cg, cb) = PlayerColors[colorIndex];
            byte alpha = 255;

            switch (blip.Type)
            {
                case BlipType.GhostedBuilding:
                    // Dimmer version of the player colour for ghosted entities.
                    cr = (byte)(cr / 2);
                    cg = (byte)(cg / 2);
                    cb = (byte)(cb / 2);
                    alpha = 180;
                    break;

                case BlipType.ResourceNode:
                    // Resource nodes are always bright gold regardless of player.
                    cr = 255; cg = 215; cb = 0;
                    alpha = 255;
                    break;
            }

            // Determine the pixel footprint on the minimap.
            var (startPx, startPy) = GridToMinimap(blip.GridX, blip.GridY);

            if (blip.Type == BlipType.Unit)
            {
                // Units render as a 2×2 pixel dot (or 1×1 if minimap is tiny).
                int dotSize = Math.Max(1, Math.Min(2, Math.Min(MinimapWidth, MinimapHeight) / 64));
                PaintRect(EntityOverlay, startPx, startPy, dotSize, dotSize, cr, cg, cb, alpha);
            }
            else
            {
                // Buildings / ghosted buildings / resource nodes use footprint size.
                var (endPx, endPy) = GridToMinimap(
                    blip.GridX + blip.FootprintW,
                    blip.GridY + blip.FootprintH);

                int rectW = Math.Max(1, endPx - startPx);
                int rectH = Math.Max(1, endPy - startPy);
                PaintRect(EntityOverlay, startPx, startPy, rectW, rectH, cr, cg, cb, alpha);
            }
        }
    }

    // ── Composite (called every frame after all layers updated) ─────

    /// <summary>
    /// Composites TerrainPixels + FogOverlay + EntityOverlay into CompositePixels
    /// using standard alpha blending (source-over).
    /// </summary>
    public void Composite()
    {
        int totalBytes = MinimapWidth * MinimapHeight * 4;

        for (int i = 0; i < totalBytes; i += 4)
        {
            // Start with terrain (fully opaque).
            int r = TerrainPixels[i + 0];
            int g = TerrainPixels[i + 1];
            int b = TerrainPixels[i + 2];

            // Blend fog overlay (source-over alpha blend).
            int fogA = FogOverlay[i + 3];
            if (fogA > 0)
            {
                int fogR = FogOverlay[i + 0];
                int fogG = FogOverlay[i + 1];
                int fogB = FogOverlay[i + 2];

                // Standard alpha blend: result = src * srcA + dst * (1 - srcA)
                r = (fogR * fogA + r * (255 - fogA)) / 255;
                g = (fogG * fogA + g * (255 - fogA)) / 255;
                b = (fogB * fogA + b * (255 - fogA)) / 255;
            }

            // Blend entity overlay on top.
            int entA = EntityOverlay[i + 3];
            if (entA > 0)
            {
                int entR = EntityOverlay[i + 0];
                int entG = EntityOverlay[i + 1];
                int entB = EntityOverlay[i + 2];

                r = (entR * entA + r * (255 - entA)) / 255;
                g = (entG * entA + g * (255 - entA)) / 255;
                b = (entB * entA + b * (255 - entA)) / 255;
            }

            CompositePixels[i + 0] = (byte)r;
            CompositePixels[i + 1] = (byte)g;
            CompositePixels[i + 2] = (byte)b;
            CompositePixels[i + 3] = 255; // final image is always opaque
        }
    }

    // ── Private helpers ─────────────────────────────────────────────

    /// <summary>
    /// Paints a filled rectangle onto the given RGBA buffer.
    /// Coordinates are clamped to buffer bounds.
    /// </summary>
    private void PaintRect(byte[] buffer, int x, int y, int w, int h,
                           byte r, byte g, byte b, byte a)
    {
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(MinimapWidth, x + w);
        int y1 = Math.Min(MinimapHeight, y + h);

        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                int idx = (py * MinimapWidth + px) * 4;
                buffer[idx + 0] = r;
                buffer[idx + 1] = g;
                buffer[idx + 2] = b;
                buffer[idx + 3] = a;
            }
        }
    }
}
