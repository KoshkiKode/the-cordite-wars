using System;
using CorditeWars.Core;

namespace CorditeWars.UI.Minimap;

/// <summary>
/// Handles the camera viewport indicator on the minimap and click-to-scroll.
/// Computes the white rectangle showing what the game camera currently sees,
/// and converts minimap clicks back into world positions for camera scrolling.
/// </summary>
public class MinimapCamera
{
    /// <summary>
    /// Color of the viewport border drawn on the minimap (white, slightly transparent).
    /// </summary>
    public (byte r, byte g, byte b, byte a) ViewportBorderColor { get; set; } = (255, 255, 255, 200);

    // ── Assumed world-space cell size ───────────────────────────────
    // Each grid cell is 1×1 in world units. If the game uses a different
    // scale, this constant can be adjusted.
    private const float CellWorldSize = 1f;

    // ── Assumed base viewport size (in world units) at zoom 1.0 ────
    // A reasonable RTS camera showing ~40 cells wide at default zoom.
    private const float BaseViewportWidth = 40f;
    private const float BaseViewportHeight = 30f;

    /// <summary>
    /// Computes the rectangle on the minimap that represents the game camera's
    /// current field of view.
    /// </summary>
    /// <param name="minimap">The minimap data (for coordinate mapping).</param>
    /// <param name="cameraWorldPos">Camera look-at centre in world space (FixedVector2).</param>
    /// <param name="cameraZoom">Current camera zoom level. 1 = default, &lt;1 = zoomed in, &gt;1 = zoomed out.</param>
    /// <param name="gridWidth">Terrain grid width in cells.</param>
    /// <param name="gridHeight">Terrain grid height in cells.</param>
    /// <returns>A tuple (x, y, w, h) in minimap pixel coordinates.</returns>
    public (int x, int y, int w, int h) GetViewportRect(
        MinimapData minimap,
        FixedVector2 cameraWorldPos,
        FixedPoint cameraZoom,
        int gridWidth,
        int gridHeight)
    {
        float zoom = cameraZoom.ToFloat();
        if (zoom <= 0f) zoom = 1f;

        // The camera sees this many world units:
        float viewW = BaseViewportWidth * zoom;
        float viewH = BaseViewportHeight * zoom;

        // Camera centre in grid coordinates (world pos / cell size).
        float camGridX = cameraWorldPos.X.ToFloat() / CellWorldSize;
        float camGridY = cameraWorldPos.Y.ToFloat() / CellWorldSize;

        // Top-left corner of the viewport in grid space.
        float gridLeft = camGridX - viewW * 0.5f;
        float gridTop = camGridY - viewH * 0.5f;

        // Convert grid-space rectangle to minimap pixels.
        float mmScaleX = (float)minimap.MinimapWidth / gridWidth;
        float mmScaleY = (float)minimap.MinimapHeight / gridHeight;

        int rectX = (int)(gridLeft * mmScaleX);
        int rectY = (int)(gridTop * mmScaleY);
        int rectW = Math.Max(1, (int)(viewW * mmScaleX));
        int rectH = Math.Max(1, (int)(viewH * mmScaleY));

        // Clamp to minimap bounds.
        rectX = Math.Clamp(rectX, 0, minimap.MinimapWidth - 1);
        rectY = Math.Clamp(rectY, 0, minimap.MinimapHeight - 1);
        rectW = Math.Min(rectW, minimap.MinimapWidth - rectX);
        rectH = Math.Min(rectH, minimap.MinimapHeight - rectY);

        return (rectX, rectY, rectW, rectH);
    }

    /// <summary>
    /// Converts a click on the minimap into a world position for the game camera.
    /// The returned FixedVector2 can be fed directly into the camera scrolling system.
    /// </summary>
    /// <param name="minimap">The minimap data (for coordinate mapping).</param>
    /// <param name="clickPixelX">X pixel coordinate of the click on the minimap.</param>
    /// <param name="clickPixelY">Y pixel coordinate of the click on the minimap.</param>
    /// <param name="gridWidth">Terrain grid width in cells.</param>
    /// <param name="gridHeight">Terrain grid height in cells.</param>
    /// <returns>World position (FixedVector2) corresponding to the click location.</returns>
    public FixedVector2 MinimapClickToWorld(
        MinimapData minimap,
        int clickPixelX,
        int clickPixelY,
        int gridWidth,
        int gridHeight)
    {
        // Clamp click to minimap bounds.
        clickPixelX = Math.Clamp(clickPixelX, 0, minimap.MinimapWidth - 1);
        clickPixelY = Math.Clamp(clickPixelY, 0, minimap.MinimapHeight - 1);

        // Convert minimap pixel → grid cell.
        var (gx, gy) = minimap.MinimapToGrid(clickPixelX, clickPixelY);

        // Convert grid cell → world position (cell centre).
        // Use FixedPoint for the interface with the simulation.
        FixedPoint worldX = FixedPoint.FromFloat((gx + 0.5f) * CellWorldSize);
        FixedPoint worldY = FixedPoint.FromFloat((gy + 0.5f) * CellWorldSize);

        return new FixedVector2(worldX, worldY);
    }

    /// <summary>
    /// Draws the camera viewport rectangle border onto an RGBA overlay buffer.
    /// Only the 1-pixel border is drawn; the interior is left untouched.
    /// </summary>
    /// <param name="overlay">RGBA byte array to draw into.</param>
    /// <param name="overlayWidth">Pixel width of the overlay.</param>
    /// <param name="overlayHeight">Pixel height of the overlay.</param>
    /// <param name="rectX">Top-left X of the viewport rectangle.</param>
    /// <param name="rectY">Top-left Y of the viewport rectangle.</param>
    /// <param name="rectW">Width of the viewport rectangle in pixels.</param>
    /// <param name="rectH">Height of the viewport rectangle in pixels.</param>
    public void DrawViewportRect(
        byte[] overlay,
        int overlayWidth,
        int overlayHeight,
        int rectX,
        int rectY,
        int rectW,
        int rectH)
    {
        var (r, g, b, a) = ViewportBorderColor;

        int x0 = Math.Max(0, rectX);
        int y0 = Math.Max(0, rectY);
        int x1 = Math.Min(overlayWidth - 1, rectX + rectW - 1);
        int y1 = Math.Min(overlayHeight - 1, rectY + rectH - 1);

        // Top edge
        for (int x = x0; x <= x1; x++)
            SetPixel(overlay, overlayWidth, x, y0, r, g, b, a);

        // Bottom edge
        for (int x = x0; x <= x1; x++)
            SetPixel(overlay, overlayWidth, x, y1, r, g, b, a);

        // Left edge (excluding corners already drawn)
        for (int y = y0 + 1; y < y1; y++)
            SetPixel(overlay, overlayWidth, x0, y, r, g, b, a);

        // Right edge (excluding corners already drawn)
        for (int y = y0 + 1; y < y1; y++)
            SetPixel(overlay, overlayWidth, x1, y, r, g, b, a);
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static void SetPixel(byte[] buffer, int width, int x, int y,
                                  byte r, byte g, byte b, byte a)
    {
        int idx = (y * width + x) * 4;
        if (idx < 0 || idx + 3 >= buffer.Length)
            return;

        buffer[idx + 0] = r;
        buffer[idx + 1] = g;
        buffer[idx + 2] = b;
        buffer[idx + 3] = a;
    }
}
