using Godot;
using CorditeWars.Systems.Localization;

namespace CorditeWars.UI.HUD;

/// <summary>
/// HUD overlay that displays subtitles for voice lines at the bottom
/// of the screen. Listens to SubtitleManager signals.
///
/// Auto-hides after the specified duration. Semi-transparent background
/// for readability over gameplay.
/// </summary>
public partial class SubtitleOverlay : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _label = null!;
    private Godot.Timer? _hideTimer;

    public override void _Ready()
    {
        Name = "SubtitleOverlay";
        Layer = 20; // Above game HUD

        // Panel at bottom-center of screen
        _panel = new PanelContainer();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.OffsetTop = -80;
        _panel.OffsetBottom = -20;
        _panel.OffsetLeft = -400;
        _panel.OffsetRight = 400;
        _panel.CustomMinimumSize = new Vector2(200, 40);
        _panel.Visible = false;

        // Semi-transparent dark background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0, 0, 0, 0.75f);
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(12);
        style.ContentMarginLeft = 24;
        style.ContentMarginRight = 24;
        _panel.AddThemeStyleboxOverride("panel", style);

        AddChild(_panel);

        // Subtitle text label
        _label = new Label();
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 1));
        _panel.AddChild(_label);

        // Connect to SubtitleManager signals
        ConnectToSubtitleManager();
    }

    private void ConnectToSubtitleManager()
    {
        // May be called before SubtitleManager autoload is ready;
        // defer connection if needed.
        CallDeferred(MethodName.DeferredConnect);
    }

    private void DeferredConnect()
    {
        var sm = SubtitleManager.Instance;
        if (sm == null)
        {
            GD.PushWarning("[SubtitleOverlay] SubtitleManager not found, subtitles will not display.");
            return;
        }

        sm.SubtitleShowRequested += OnShowSubtitle;
        sm.SubtitleHideRequested += OnHideSubtitle;
    }

    private void OnShowSubtitle(string text, float duration)
    {
        _label.Text = text;
        _panel.Visible = true;

        // Reset/create hide timer
        _hideTimer?.QueueFree();
        _hideTimer = new Godot.Timer();
        _hideTimer.WaitTime = duration;
        _hideTimer.OneShot = true;
        _hideTimer.Timeout += OnHideSubtitle;
        AddChild(_hideTimer);
        _hideTimer.Start();
    }

    private void OnHideSubtitle()
    {
        _panel.Visible = false;
        _hideTimer?.QueueFree();
        _hideTimer = null;
    }
}
