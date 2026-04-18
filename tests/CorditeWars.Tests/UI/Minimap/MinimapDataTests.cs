using System.Collections.Generic;
using CorditeWars.Systems.FogOfWar;
using CorditeWars.UI.Minimap;

namespace CorditeWars.Tests.UI.Minimap;

/// <summary>
/// Tests for <see cref="MinimapData"/> — the pure pixel-math data layer.
/// Covers coordinate mapping, fog overlay, entity overlay, and compositing.
/// No Godot runtime required.
/// </summary>
public class MinimapDataTests
{
    // ══════════════════════════════════════════════════════════════════
    // Constructor / dimensions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_SetsCorrectDimensions()
    {
        var mm = new MinimapData(256, 128, 200, 100);

        Assert.Equal(256, mm.MinimapWidth);
        Assert.Equal(128, mm.MinimapHeight);
        Assert.Equal(200, mm.GridWidth);
        Assert.Equal(100, mm.GridHeight);
    }

    [Fact]
    public void Constructor_AllocatesCorrectBufferSizes()
    {
        var mm = new MinimapData(64, 64, 50, 50);
        int expected = 64 * 64 * 4;

        Assert.Equal(expected, mm.TerrainPixels.Length);
        Assert.Equal(expected, mm.FogOverlay.Length);
        Assert.Equal(expected, mm.EntityOverlay.Length);
        Assert.Equal(expected, mm.CompositePixels.Length);
    }

    // ══════════════════════════════════════════════════════════════════
    // GridToMinimap / MinimapToGrid
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void GridToMinimap_Origin_MapsToZeroZero()
    {
        var mm = new MinimapData(256, 256, 200, 200);
        var (px, py) = mm.GridToMinimap(0, 0);
        Assert.Equal(0, px);
        Assert.Equal(0, py);
    }

    [Fact]
    public void GridToMinimap_MaxGridCorner_ClampedToMaxPixel()
    {
        var mm = new MinimapData(256, 256, 200, 200);
        var (px, py) = mm.GridToMinimap(199, 199);
        // Should be within valid pixel range
        Assert.InRange(px, 0, 255);
        Assert.InRange(py, 0, 255);
    }

    [Fact]
    public void MinimapToGrid_Origin_MapsToZeroZero()
    {
        var mm = new MinimapData(256, 256, 200, 200);
        var (gx, gy) = mm.MinimapToGrid(0, 0);
        Assert.Equal(0, gx);
        Assert.Equal(0, gy);
    }

    [Fact]
    public void MinimapToGrid_MaxPixel_ClampedToMaxGrid()
    {
        var mm = new MinimapData(256, 256, 200, 200);
        var (gx, gy) = mm.MinimapToGrid(255, 255);
        Assert.InRange(gx, 0, 199);
        Assert.InRange(gy, 0, 199);
    }

    [Fact]
    public void GridToMinimap_MidGrid_MapsToApproximateMidPixel()
    {
        // 256-pixel minimap over 256-cell grid: 1:1 mapping
        var mm = new MinimapData(256, 256, 256, 256);
        var (px, py) = mm.GridToMinimap(128, 128);
        Assert.Equal(128, px);
        Assert.Equal(128, py);
    }

    [Fact]
    public void GridToMinimap_NegativeCoords_ClampedToZero()
    {
        var mm = new MinimapData(256, 256, 200, 200);
        var (px, py) = mm.GridToMinimap(-5, -5);
        Assert.Equal(0, px);
        Assert.Equal(0, py);
    }

    // ══════════════════════════════════════════════════════════════════
    // UpdateFogLayer
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateFogLayer_NullFog_ClearsAllFogOverlay()
    {
        var mm = new MinimapData(8, 8, 8, 8);
        // Pre-fill FogOverlay with non-zero data
        for (int i = 0; i < mm.FogOverlay.Length; i++)
            mm.FogOverlay[i] = 255;

        mm.UpdateFogLayer(null);

        // All bytes should be 0 (fully transparent — no fog)
        foreach (byte b in mm.FogOverlay)
            Assert.Equal(0, b);
    }

    [Fact]
    public void UpdateFogLayer_UnexploredGrid_SetsOpaqueBlack()
    {
        var mm = new MinimapData(4, 4, 4, 4);
        var fog = new FogGrid(4, 4, playerId: 0, FogMode.Campaign); // starts Unexplored

        mm.UpdateFogLayer(fog);

        // Every pixel's alpha should be 255 (Unexplored = fully opaque black)
        for (int i = 3; i < mm.FogOverlay.Length; i += 4)
            Assert.Equal(255, mm.FogOverlay[i]);
    }

    [Fact]
    public void UpdateFogLayer_ExploredCell_SetsSemiTransparentBlack()
    {
        var mm = new MinimapData(4, 4, 4, 4);
        var fog = new FogGrid(4, 4, playerId: 0, FogMode.Campaign);
        fog.Cells[0, 0].Visibility = FogVisibility.Explored;

        mm.UpdateFogLayer(fog);

        // Pixel (0,0) → fog pixel index 0. Alpha should be 160 for Explored.
        Assert.Equal(160, mm.FogOverlay[3]);
    }

    [Fact]
    public void UpdateFogLayer_VisibleCell_SetsTransparent()
    {
        var mm = new MinimapData(4, 4, 4, 4);
        var fog = new FogGrid(4, 4, playerId: 0, FogMode.Campaign);
        fog.Cells[0, 0].Visibility = FogVisibility.Visible;

        mm.UpdateFogLayer(fog);

        // Alpha should be 0 for a Visible cell
        Assert.Equal(0, mm.FogOverlay[3]);
    }

    // ══════════════════════════════════════════════════════════════════
    // UpdateEntityLayer
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateEntityLayer_EmptyBlips_ClearsEntityOverlay()
    {
        var mm = new MinimapData(8, 8, 8, 8);
        // Pre-fill with non-zero data
        for (int i = 0; i < mm.EntityOverlay.Length; i++)
            mm.EntityOverlay[i] = 200;

        mm.UpdateEntityLayer(new List<MinimapBlip>());

        foreach (byte b in mm.EntityOverlay)
            Assert.Equal(0, b);
    }

    [Fact]
    public void UpdateEntityLayer_SingleUnitBlip_PaintsPixel()
    {
        var mm = new MinimapData(32, 32, 32, 32);
        var blips = new List<MinimapBlip>
        {
            new MinimapBlip(0, 0, playerIndex: 0, BlipType.Unit)
        };

        mm.UpdateEntityLayer(blips);

        // At least one pixel should be painted.
        bool anyPainted = false;
        for (int i = 0; i < mm.EntityOverlay.Length; i += 4)
        {
            if (mm.EntityOverlay[i + 3] > 0)
            {
                anyPainted = true;
                break;
            }
        }
        Assert.True(anyPainted, "At least one pixel should have non-zero alpha after painting a unit blip.");
    }

    [Fact]
    public void UpdateEntityLayer_ResourceNodeBlip_PaintsGoldPixel()
    {
        var mm = new MinimapData(32, 32, 32, 32);
        var blips = new List<MinimapBlip>
        {
            new MinimapBlip(0, 0, playerIndex: 0, BlipType.ResourceNode, footprintW: 2, footprintH: 2)
        };

        mm.UpdateEntityLayer(blips);

        // Resource nodes are gold: R=255, G=215, B=0
        for (int i = 0; i < mm.EntityOverlay.Length; i += 4)
        {
            if (mm.EntityOverlay[i + 3] > 0)
            {
                Assert.Equal(255, mm.EntityOverlay[i + 0]); // R
                Assert.Equal(215, mm.EntityOverlay[i + 1]); // G
                Assert.Equal(0, mm.EntityOverlay[i + 2]);   // B
                return;
            }
        }
        Assert.Fail("No pixel was painted for ResourceNode blip.");
    }

    // ══════════════════════════════════════════════════════════════════
    // Composite
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Composite_AllClear_PassesThroughTerrainColor()
    {
        var mm = new MinimapData(2, 2, 2, 2);
        // Set terrain to bright red
        for (int i = 0; i < mm.TerrainPixels.Length; i += 4)
        {
            mm.TerrainPixels[i + 0] = 200; // R
            mm.TerrainPixels[i + 1] = 0;   // G
            mm.TerrainPixels[i + 2] = 0;   // B
            mm.TerrainPixels[i + 3] = 255; // A
        }
        // Fog and entity overlays remain zero (transparent)

        mm.Composite();

        // With no fog/entity overlay the terrain color passes straight through
        for (int i = 0; i < mm.CompositePixels.Length; i += 4)
        {
            Assert.Equal(200, mm.CompositePixels[i + 0]);
            Assert.Equal(0, mm.CompositePixels[i + 1]);
            Assert.Equal(0, mm.CompositePixels[i + 2]);
            Assert.Equal(255, mm.CompositePixels[i + 3]); // always opaque
        }
    }

    [Fact]
    public void Composite_FullBlackFog_ProducesBlackOutput()
    {
        var mm = new MinimapData(2, 2, 2, 2);
        // Terrain = white
        for (int i = 0; i < mm.TerrainPixels.Length; i += 4)
        {
            mm.TerrainPixels[i + 0] = 255;
            mm.TerrainPixels[i + 1] = 255;
            mm.TerrainPixels[i + 2] = 255;
            mm.TerrainPixels[i + 3] = 255;
        }
        // Fog = fully opaque black
        for (int i = 0; i < mm.FogOverlay.Length; i += 4)
        {
            mm.FogOverlay[i + 0] = 0;
            mm.FogOverlay[i + 1] = 0;
            mm.FogOverlay[i + 2] = 0;
            mm.FogOverlay[i + 3] = 255;
        }

        mm.Composite();

        for (int i = 0; i < mm.CompositePixels.Length; i += 4)
        {
            Assert.Equal(0, mm.CompositePixels[i + 0]);
            Assert.Equal(0, mm.CompositePixels[i + 1]);
            Assert.Equal(0, mm.CompositePixels[i + 2]);
            Assert.Equal(255, mm.CompositePixels[i + 3]);
        }
    }

    [Fact]
    public void Composite_EntityOverlayOnTerrain_IsBlendedOnTop()
    {
        var mm = new MinimapData(2, 2, 2, 2);
        // Terrain = black (all zero except alpha)
        for (int i = 0; i < mm.TerrainPixels.Length; i += 4)
            mm.TerrainPixels[i + 3] = 255;

        // Entity overlay = fully opaque white at first pixel
        mm.EntityOverlay[0] = 255; // R
        mm.EntityOverlay[1] = 255; // G
        mm.EntityOverlay[2] = 255; // B
        mm.EntityOverlay[3] = 255; // A (fully opaque)

        mm.Composite();

        Assert.Equal(255, mm.CompositePixels[0]);
        Assert.Equal(255, mm.CompositePixels[1]);
        Assert.Equal(255, mm.CompositePixels[2]);
    }
}
