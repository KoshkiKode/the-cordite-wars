using Godot;

namespace CorditeWars.UI;

/// <summary>
/// Placeholder branding screen shown between Godot startup branding and
/// the Cordite Wars intro splash.
/// </summary>
public partial class KoshkiKodeBrandingScreen : Control
{
    private const float FadeInDuration = 0.35f;
    private const float HoldDuration = 1.5f;
    private const float FadeOutDuration = 0.35f;
    private const string NextScene = "res://scenes/UI/SplashScreen.tscn";

    private Control _content = null!;
    private bool _skipping;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 1);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _content = new Control();
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content.Modulate = new Color(1, 1, 1, 0);
        AddChild(_content);

        var center = new VBoxContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
        center.GrowHorizontal = GrowDirection.Both;
        center.GrowVertical = GrowDirection.Both;
        center.CustomMinimumSize = new Vector2(700, 220);
        center.Alignment = BoxContainer.AlignmentMode.Center;
        _content.AddChild(center);

        var title = new Label();
        title.Text = "KoshkiKode";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeTitle, UITheme.Accent);
        center.AddChild(title);

        var subtitle = new Label();
        subtitle.Text = "BRANDING PLACEHOLDER";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(subtitle, UITheme.FontSizeSubtitle, UITheme.TextSecondary);
        center.AddChild(subtitle);

        var skipHint = new Label();
        skipHint.Text = Tr("SPLASH_SKIP_HINT");
        skipHint.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(skipHint, UITheme.FontSizeSmall, UITheme.TextMuted);
        skipHint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.KeepSize);
        skipHint.GrowHorizontal = GrowDirection.Both;
        skipHint.GrowVertical = GrowDirection.Begin;
        skipHint.OffsetTop = -30;
        _content.AddChild(skipHint);

        PlayAnimation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey or InputEventMouseButton or InputEventScreenTouch)
        {
            if (!_skipping)
            {
                _skipping = true;
                GoToNextScene();
            }
        }
    }

    private async void PlayAnimation()
    {
        var tweenIn = CreateTween();
        tweenIn.TweenProperty(_content, "modulate", new Color(1, 1, 1, 1), FadeInDuration);
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        if (_skipping) return;

        await ToSignal(GetTree().CreateTimer(HoldDuration), SceneTreeTimer.SignalName.Timeout);

        if (_skipping) return;

        var tweenOut = CreateTween();
        tweenOut.TweenProperty(_content, "modulate", new Color(1, 1, 1, 0), FadeOutDuration);
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        if (_skipping) return;

        GoToNextScene();
    }

    private void GoToNextScene()
    {
        GetTree().ChangeSceneToFile(NextScene);
    }
}
