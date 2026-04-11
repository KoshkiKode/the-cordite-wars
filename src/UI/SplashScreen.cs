using Godot;

namespace CorditeWars.UI;

/// <summary>
/// Boot splash: "CORDITE WARS / SIX FRONTS" logo fade-in, hold, fade-out.
/// Skip on any input. Version in bottom-right corner.
/// </summary>
public partial class SplashScreen : Control
{
    private const float FadeInDuration  = 0.5f;
    private const float HoldDuration    = 1.5f;
    private const float FadeOutDuration = 0.5f;
    private const string NextScene      = "res://scenes/UI/LoadingScreen.tscn";

    private static string VersionString
    {
        get
        {
            string version = ProjectSettings.GetSetting("application/config/version", "0.1.0").AsString();
            string build = ProjectSettings.GetSetting("application/config/version_build", "").AsString();
            return string.IsNullOrEmpty(build) ? $"v{version}" : $"v{version}-{build}";
        }
    }

    private Control _content = null!;
    private bool _skipping;

    public override void _Ready()
    {
        // Full-screen background
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 1);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Content container (what we fade in/out)
        _content = new Control();
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content.Modulate = new Color(1, 1, 1, 0);
        AddChild(_content);

        // Center container for title
        var center = new VBoxContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
        center.GrowHorizontal = GrowDirection.Both;
        center.GrowVertical = GrowDirection.Both;
        center.CustomMinimumSize = new Vector2(600, 200);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
        center.Alignment = BoxContainer.AlignmentMode.Center;
        _content.AddChild(center);

        // Title: CORDITE WARS
        var title = new Label();
        title.Text = Tr("GAME_TITLE");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeTitle, UITheme.Accent);
        center.AddChild(title);

        // Decorative separator
        var sep = new Label();
        sep.Text = "\u2500\u2500\u2500  " + Tr("GAME_SUBTITLE") + "  \u2500\u2500\u2500";
        sep.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(sep, UITheme.FontSizeSubtitle, UITheme.TextSecondary);
        center.AddChild(sep);

        // Version string (bottom-right)
        var version = new Label();
        version.Text = VersionString;
        UITheme.StyleLabel(version, UITheme.FontSizeSmall, UITheme.TextMuted);
        version.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize);
        version.GrowHorizontal = GrowDirection.Begin;
        version.GrowVertical = GrowDirection.Begin;
        version.OffsetLeft = -120;
        version.OffsetTop = -30;
        _content.AddChild(version);

        // Skip hint (bottom-center)
        var skipHint = new Label();
        skipHint.Text = Tr("SPLASH_SKIP_HINT");
        skipHint.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(skipHint, UITheme.FontSizeSmall, UITheme.TextMuted);
        skipHint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.KeepSize);
        skipHint.GrowHorizontal = GrowDirection.Both;
        skipHint.GrowVertical = GrowDirection.Begin;
        skipHint.OffsetTop = -30;
        _content.AddChild(skipHint);

        // Start the animation sequence
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
        // Fade in
        var tweenIn = CreateTween();
        tweenIn.TweenProperty(_content, "modulate", new Color(1, 1, 1, 1), FadeInDuration);
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        if (_skipping) return;

        // Hold
        await ToSignal(GetTree().CreateTimer(HoldDuration), SceneTreeTimer.SignalName.Timeout);

        if (_skipping) return;

        // Fade out
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
