using Godot;
using CorditeWars.Game.Tutorial;

namespace CorditeWars.UI.HUD;

public partial class TutorialOverlay : CanvasLayer
{
    private Panel?  _panel;
    private Label?  _titleLabel;
    private Label?  _bodyLabel;
    private Button? _nextBtn;
    private Button? _skipBtn;

    private TutorialManager? _manager;

    public override void _Ready()
    {
        Layer = 20;
        BuildUI();
        Visible = false;
    }

    public void Attach(TutorialManager manager)
    {
        _manager = manager;
        manager.StepChanged   += ShowStep;
        manager.TutorialEnded += HideTutorial;
        if (manager.CurrentStep is { } step)
            ShowStep(step);
    }

    public void ShowStep(TutorialStep step)
    {
        if (_titleLabel is not null) _titleLabel.Text = step.Title;
        if (_bodyLabel  is not null) _bodyLabel.Text  = step.Body;
        Visible = true;
    }

    public void HideTutorial()
    {
        Visible = false;
    }

    private void BuildUI()
    {
        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 0);
        bg.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(bg);

        var anchor = new Control();
        anchor.AnchorsPreset = (int)Control.LayoutPreset.BottomRight;
        anchor.OffsetLeft   = -460;
        anchor.OffsetTop    = -220;
        anchor.OffsetRight  = -20;
        anchor.OffsetBottom = -20;
        AddChild(anchor);

        _panel = new Panel();
        _panel.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        anchor.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        margin.AddThemeConstantOverride("margin_left",   12);
        margin.AddThemeConstantOverride("margin_right",  12);
        margin.AddThemeConstantOverride("margin_top",    12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        _titleLabel = new Label();
        _titleLabel.Text = string.Empty;
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_titleLabel);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        _bodyLabel = new Label();
        _bodyLabel.Text = string.Empty;
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _bodyLabel.AddThemeFontSizeOverride("font_size", 13);
        _bodyLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_bodyLabel);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(btnRow);

        _nextBtn = new Button();
        _nextBtn.Text = "Next \u25BA";
        _nextBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _nextBtn.Pressed += () => _manager?.AdvanceStep();
        btnRow.AddChild(_nextBtn);

        _skipBtn = new Button();
        _skipBtn.Text = "Skip";
        _skipBtn.Pressed += () => _manager?.SkipTutorial();
        btnRow.AddChild(_skipBtn);
    }
}
