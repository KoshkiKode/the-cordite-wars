using Godot;

namespace UnnamedRTS.UI;

/// <summary>
/// Fade-to-black transition between scenes using CanvasLayer + ColorRect tween.
/// Call TransitionTo() to fade out, change scene, and fade in.
/// </summary>
public partial class SceneTransition : CanvasLayer
{
    private static SceneTransition? _instance;
    public static SceneTransition? Instance => _instance;

    private ColorRect _overlay = null!;
    private bool _transitioning;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Ready()
    {
        Layer = 100;
        _overlay = new ColorRect();
        _overlay.Color = new Color(0, 0, 0, 0);
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_overlay);
    }

    public static void TransitionTo(SceneTree tree, string scenePath, float duration = 0.4f)
    {
        if (_instance != null && !_instance._transitioning)
        {
            _instance.DoTransition(tree, scenePath, duration);
        }
        else
        {
            tree.ChangeSceneToFile(scenePath);
        }
    }

    private async void DoTransition(SceneTree tree, string scenePath, float duration)
    {
        _transitioning = true;
        float half = duration / 2.0f;

        // Fade to black
        var tween = CreateTween();
        tween.TweenProperty(_overlay, "color", new Color(0, 0, 0, 1), half);
        await ToSignal(tween, Tween.SignalName.Finished);

        // Change scene
        tree.ChangeSceneToFile(scenePath);

        // Wait one frame for the new scene to load
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        // Fade from black
        var tween2 = CreateTween();
        tween2.TweenProperty(_overlay, "color", new Color(0, 0, 0, 0), half);
        await ToSignal(tween2, Tween.SignalName.Finished);

        _transitioning = false;
    }

    /// <summary>
    /// Fade in from black (used for initial scene entry).
    /// </summary>
    public void FadeIn(float duration = 0.5f)
    {
        _overlay.Color = new Color(0, 0, 0, 1);
        var tween = CreateTween();
        tween.TweenProperty(_overlay, "color", new Color(0, 0, 0, 0), duration);
    }

    /// <summary>
    /// Fade out to black (used for scene exit).
    /// </summary>
    public async void FadeOutThen(SceneTree tree, string scenePath, float duration = 0.5f)
    {
        _transitioning = true;
        var tween = CreateTween();
        tween.TweenProperty(_overlay, "color", new Color(0, 0, 0, 1), duration);
        await ToSignal(tween, Tween.SignalName.Finished);
        tree.ChangeSceneToFile(scenePath);
        _transitioning = false;
    }
}
