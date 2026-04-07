using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;
using CorditeWars.UI.Minimap;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Bottom-left minimap. Renders a live terrain + entity-blip composite from
/// <see cref="MinimapData"/>. Supports click-to-move camera panning.
/// </summary>
public partial class MinimapPanel : PanelContainer
{
    private const int MinimapSize = 200;
    private const int MinimapRes = 256; // internal resolution for pixel buffer

    private TextureRect? _minimapTexture;
    private Image? _minimapImage;
    private ImageTexture? _minimapTex;
    private MinimapViewportOverlay? _viewportOverlay;

    // Live data sources wired up via SetupLiveData()
    private MinimapData? _minimapData;
    private UnitSpawner? _unitSpawner;
    private BuildingPlacer? _buildingPlacer;
    private RTSCamera? _camera;
    private int _gridWidth = 256;
    private int _gridHeight = 256;

    // Blip list reused each frame to avoid allocation
    private readonly List<MinimapBlip> _blips = new();

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize()
    {
        Name = "MinimapPanel";

        // Bottom-left positioning
        AnchorLeft = 0;
        AnchorTop = 1;
        AnchorRight = 0;
        AnchorBottom = 1;
        OffsetLeft = 8;
        OffsetTop = -MinimapSize - 8;
        OffsetRight = MinimapSize + 8;
        OffsetBottom = -8;

        CustomMinimumSize = new Vector2(MinimapSize, MinimapSize);

        // Dark panel background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.102f, 0.102f, 0.141f, 0.95f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f);
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        AddThemeStyleboxOverride("panel", style);

        // Minimap texture backed by an RGBA8 image we update each frame
        _minimapImage = Image.CreateEmpty(MinimapRes, MinimapRes, false, Image.Format.Rgba8);
        _minimapImage.Fill(new Color(0.15f, 0.25f, 0.15f));
        _minimapTex = ImageTexture.CreateFromImage(_minimapImage);

        _minimapTexture = new TextureRect();
        _minimapTexture.AnchorsPreset = (int)LayoutPreset.FullRect;
        _minimapTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _minimapTexture.StretchMode = TextureRect.StretchModeEnum.Scale;
        _minimapTexture.Texture = _minimapTex;
        AddChild(_minimapTexture);

        // Viewport + cordite node overlay (drawn on top)
        _viewportOverlay = new MinimapViewportOverlay();
        AddChild(_viewportOverlay);
    }

    /// <summary>
    /// Wires the minimap to live game data so it can render terrain and units.
    /// Must be called after the map has been loaded and the TerrainGrid is ready.
    /// </summary>
    public void SetupLiveData(
        TerrainGrid terrain,
        int gridWidth,
        int gridHeight,
        UnitSpawner unitSpawner,
        BuildingPlacer buildingPlacer,
        RTSCamera camera)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _unitSpawner = unitSpawner;
        _buildingPlacer = buildingPlacer;
        _camera = camera;

        _minimapData = new MinimapData(MinimapRes, MinimapRes, gridWidth, gridHeight);
        _minimapData.GenerateTerrainLayer(terrain);

        // Bake initial terrain layer into the display
        _minimapData.Composite();
        UploadPixels(_minimapData.CompositePixels);

        if (_viewportOverlay is not null)
            _viewportOverlay.Setup(_minimapData, camera, gridWidth, gridHeight);
    }

    // ── Per-frame update ─────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_minimapData is null || _unitSpawner is null || _minimapImage is null || _minimapTex is null)
            return;

        // Build entity blip list
        _blips.Clear();

        var units = _unitSpawner.GetAllUnits();
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.IsAlive) continue;
            int gx = (int)u.SimPosition.X.ToFloat();
            int gy = (int)u.SimPosition.Y.ToFloat();
            // Player index for colour: subtract 1 because PlayerColors[0] = Green = player 1
            int pIdx = u.PlayerId - 1;
            if (pIdx < 0) pIdx = 0;
            _blips.Add(new MinimapBlip(gx, gy, pIdx, BlipType.Unit));
        }

        if (_buildingPlacer is not null)
        {
            var buildings = _buildingPlacer.GetAllBuildings();
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                int data_w = b.Data?.FootprintWidth ?? 3;
                int data_h = b.Data?.FootprintHeight ?? 3;
                int pIdx = b.PlayerId - 1;
                if (pIdx < 0) pIdx = 0;
                _blips.Add(new MinimapBlip(b.GridX, b.GridY, pIdx, BlipType.Building, data_w, data_h));
            }
        }

        _minimapData.UpdateEntityLayer(_blips);
        _minimapData.Composite();
        UploadPixels(_minimapData.CompositePixels);
    }

    // ── Input (Click-to-Move) ────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn &&
            mouseBtn.Pressed &&
            mouseBtn.ButtonIndex == MouseButton.Left)
        {
            HandleMinimapClick(mouseBtn.Position);
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mouseMotion &&
                 Godot.Input.IsMouseButtonPressed(MouseButton.Left))
        {
            HandleMinimapClick(mouseMotion.Position);
            AcceptEvent();
        }
    }

    private void HandleMinimapClick(Vector2 localPos)
    {
        float normX = localPos.X / MinimapSize;
        float normY = localPos.Y / MinimapSize;
        float worldX = normX * _gridWidth;
        float worldZ = normY * _gridHeight;
        EventBus.Instance?.EmitMinimapClick(new Vector3(worldX, 0f, worldZ));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void UploadPixels(byte[] rgba)
    {
        if (_minimapImage is null || _minimapTex is null) return;
        _minimapImage.SetData(MinimapRes, MinimapRes, false, Image.Format.Rgba8, rgba);
        _minimapTex.Update(_minimapImage);
    }
}

/// <summary>
/// Draws the camera viewport rectangle and cordite-node dots on top of the minimap.
/// </summary>
internal partial class MinimapViewportOverlay : Control
{
    private MinimapData? _minimap;
    private RTSCamera? _camera;
    private int _gridWidth;
    private int _gridHeight;

    public void Setup(MinimapData minimap, RTSCamera camera, int gridWidth, int gridHeight)
    {
        _minimap = minimap;
        _camera = camera;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    public MinimapViewportOverlay()
    {
        Name = "ViewportOverlay";
        AnchorsPreset = (int)LayoutPreset.FullRect;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (_camera is null)
        {
            // Fallback: static indicator
            DrawRect(new Rect2((Size.X - 40) * 0.5f, (Size.Y - 40) * 0.5f, 40, 40),
                new Color(1f, 1f, 1f, 0.4f), false, 1.5f);
            return;
        }

        // Compute viewport rect in minimap UI pixel space
        Vector3 focus = _camera.FocusPoint;
        float zoom = _camera.CurrentZoom;

        // Approximate visible area at this zoom level
        const float BaseViewW = 40f;
        const float BaseViewH = 30f;
        float viewW = BaseViewW * (zoom / 30f);
        float viewH = BaseViewH * (zoom / 30f);

        float scaleX = Size.X / _gridWidth;
        float scaleY = Size.Y / _gridHeight;

        float rectX = (focus.X - viewW * 0.5f) * scaleX;
        float rectY = (focus.Z - viewH * 0.5f) * scaleY;
        float rectW = viewW * scaleX;
        float rectH = viewH * scaleY;

        rectX = Mathf.Max(rectX, 0);
        rectY = Mathf.Max(rectY, 0);
        if (rectX + rectW > Size.X) rectW = Size.X - rectX;
        if (rectY + rectH > Size.Y) rectH = Size.Y - rectY;

        DrawRect(new Rect2(rectX, rectY, rectW, rectH), new Color(1f, 1f, 1f, 0.55f), false, 1.5f);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }
}
