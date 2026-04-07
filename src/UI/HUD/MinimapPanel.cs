using Godot;
using CorditeWars.Core;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Bottom-left minimap. Renders terrain colors, unit dots (team colored),
/// and supports click-to-move camera panning.
/// </summary>
public partial class MinimapPanel : PanelContainer
{
    private const int MinimapSize = 200;

    private TextureRect? _minimapTexture;
    private Control? _viewportRect;

    // Map bounds for coordinate conversion
    private float _mapWidth = 256f;
    private float _mapHeight = 256f;

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
        style.BgColor = new Color(0.102f, 0.102f, 0.141f, 0.95f); // #1A1A24
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f); // #2A2A3A
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        AddThemeStyleboxOverride("panel", style);

        // Minimap texture (placeholder dark green)
        _minimapTexture = new TextureRect();
        _minimapTexture.AnchorsPreset = (int)LayoutPreset.FullRect;
        _minimapTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _minimapTexture.StretchMode = TextureRect.StretchModeEnum.Scale;

        // Generate a placeholder minimap image
        var img = Image.CreateEmpty(MinimapSize, MinimapSize, false, Image.Format.Rgba8);
        img.Fill(new Color(0.15f, 0.25f, 0.15f));
        var tex = ImageTexture.CreateFromImage(img);
        _minimapTexture.Texture = tex;
        AddChild(_minimapTexture);

        // Camera viewport rectangle overlay
        _viewportRect = new MinimapViewportRect();
        AddChild(_viewportRect);
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
        // Convert minimap coords to world coords
        float normX = localPos.X / MinimapSize;
        float normY = localPos.Y / MinimapSize;

        float worldX = normX * _mapWidth;
        float worldZ = normY * _mapHeight;

        Vector3 worldPos = new Vector3(worldX, 0f, worldZ);
        EventBus.Instance?.EmitMinimapClick(worldPos);
    }

    public void SetMapBounds(float width, float height)
    {
        _mapWidth = width;
        _mapHeight = height;
    }
}

/// <summary>
/// Draws the camera viewport rectangle on the minimap.
/// </summary>
internal partial class MinimapViewportRect : Control
{
    public MinimapViewportRect()
    {
        Name = "ViewportRect";
        AnchorsPreset = (int)LayoutPreset.FullRect;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        // Draw a simple viewport indicator at center
        float size = 40f;
        Rect2 rect = new Rect2(
            (Size.X - size) * 0.5f,
            (Size.Y - size) * 0.5f,
            size, size);
        DrawRect(rect, new Color(1f, 1f, 1f, 0.5f), false, 1.5f);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }
}
