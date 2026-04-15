using System;
using Godot;
using CorditeWars.Game;

namespace CorditeWars.UI.HUD;

/// <summary>
/// F3-style in-game debug overlay (toggle with F3).
/// Renders two columns of diagnostic text over the game viewport:
/// left side shows world / simulation data, right side shows
/// performance / render data. Styled with a semi-transparent background
/// so it's readable without obscuring the map.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    // ── References ───────────────────────────────────────────────────

    private readonly GameSession _session;

    // ── Layout ───────────────────────────────────────────────────────

    private const int PanelMargin    = 8;
    private const int LabelFontSize  = 13;
    private const float BgAlpha      = 0.75f;

    // ── Left panel ───────────────────────────────────────────────────

    private PanelContainer? _leftPanel;
    private Label?          _leftLabel;

    // ── Right panel ──────────────────────────────────────────────────

    private PanelContainer? _rightPanel;
    private Label?          _rightLabel;

    // ── FPS / frame-time smoothing ────────────────────────────────────

    private double _frameTimeSmoother;
    private const double FrameTimeSmoothK = 0.1; // exponential moving average factor

    // ── Constructor ──────────────────────────────────────────────────

    public DebugOverlay(GameSession session)
    {
        _session = session;
        Name = "DebugOverlay";
    }

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 50; // above HUD (10), pause menu (30)
        Visible = false;

        // Shared background style
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0f, 0f, 0f, BgAlpha);
        bgStyle.ContentMarginLeft   = 8;
        bgStyle.ContentMarginRight  = 8;
        bgStyle.ContentMarginTop    = 5;
        bgStyle.ContentMarginBottom = 5;
        bgStyle.CornerRadiusTopLeft = bgStyle.CornerRadiusTopRight   = 3;
        bgStyle.CornerRadiusBottomLeft = bgStyle.CornerRadiusBottomRight = 3;

        (_leftPanel,  _leftLabel)  = BuildPanel(bgStyle, isLeft: true);
        (_rightPanel, _rightLabel) = BuildPanel(bgStyle, isLeft: false);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Smooth frame time
        _frameTimeSmoother = _frameTimeSmoother * (1.0 - FrameTimeSmoothK) + delta * FrameTimeSmoothK;

        var snap = _session.GetDebugSnapshot();
        float fps = (float)Engine.GetFramesPerSecond();
        float ms  = (float)(_frameTimeSmoother * 1000.0);

        if (_leftLabel is not null)
            _leftLabel.Text  = BuildLeftText(snap, fps, ms);

        if (_rightLabel is not null)
            _rightLabel.Text = BuildRightText(snap);
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Toggles the overlay's visibility.</summary>
    public void Toggle()
    {
        Visible = !Visible;
    }

    // ── Panel construction ───────────────────────────────────────────

    private (PanelContainer panel, Label label) BuildPanel(StyleBoxFlat bgStyle, bool isLeft)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", bgStyle);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        if (isLeft)
        {
            panel.AnchorLeft   = 0f;
            panel.AnchorRight  = 0f;
            panel.AnchorTop    = 0f;
            panel.AnchorBottom = 0f;
            panel.OffsetLeft   = PanelMargin;
            panel.OffsetTop    = PanelMargin;
            // Right / bottom expand to content
        }
        else
        {
            panel.AnchorLeft   = 1f;
            panel.AnchorRight  = 1f;
            panel.AnchorTop    = 0f;
            panel.AnchorBottom = 0f;
            panel.OffsetRight  = -PanelMargin;
            panel.OffsetTop    = PanelMargin;
            panel.GrowHorizontal = Control.GrowDirection.Begin;
        }

        var label = new Label();
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
        label.AddThemeFontSizeOverride("font_size", LabelFontSize);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(label);
        AddChild(panel);

        return (panel, label);
    }

    // ── Text builders ─────────────────────────────────────────────────

    private static string BuildLeftText(GameSession.DebugSnapshot snapshot, float fps, float ms)
    {
        return
            $"Cordite Wars — Debug Info\n" +
            $"\n" +
            $"FPS:    {fps:F0}  ({ms:F2} ms)\n" +
            $"Tick:   {snapshot.SimTick}\n" +
            $"State:  {snapshot.MatchState}  (×{snapshot.GameSpeed} speed)\n" +
            $"Net:    {(snapshot.IsMultiplayer ? "Multiplayer" : "Local")}\n" +
            $"\n" +
            $"Map:    {(string.IsNullOrEmpty(snapshot.MapId) ? "—" : snapshot.MapId)}\n" +
            $"Biome:  {(string.IsNullOrEmpty(snapshot.Biome) ? "—" : snapshot.Biome)}\n" +
            $"Size:   {snapshot.MapWidth} × {snapshot.MapHeight}\n" +
            $"Fog:    {(snapshot.FogOfWar ? "On" : "Off")}\n" +
            $"Win:    {snapshot.WinConditionName}\n" +
            $"\n" +
            $"Camera: {snapshot.CameraX:F1}, {snapshot.CameraY:F1}, {snapshot.CameraZ:F1}\n" +
            $"Zoom:   {snapshot.CameraZoom:F1}";
    }

    private static string BuildRightText(GameSession.DebugSnapshot snapshot)
    {
        return
            $"Units:     {snapshot.UnitCount}\n" +
            $"Buildings: {snapshot.BuildingCount}\n" +
            $"\n" +
            $"Cordite:   {snapshot.Cordite}\n" +
            $"VC:        {snapshot.VoltaicCharge}\n" +
            $"Supply:    {snapshot.Supply} / {snapshot.MaxSupply}\n";
    }
}
