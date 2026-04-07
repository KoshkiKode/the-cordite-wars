using Godot;

namespace UnnamedRTS.UI;

/// <summary>
/// Scrolling credits screen. Auto-scrolls upward.
/// Any input speeds up scroll; ESC/Back returns to MainMenu.
/// </summary>
public partial class CreditsScreen : Control
{
    private const float ScrollSpeed = 40.0f;
    private const float FastScrollMultiplier = 4.0f;
    private const string CreditsText = @"CORDITE WARS: SIX FRONTS


Design & Development
Game Studio


3D Models
Kenney (kenney.nl) — CC0
Quaternius (quaternius.com) — CC0
KayKit / Kay Lousberg — CC0
Majadroid — CC0
Low Poly Assets — CC0


Sound Effects
Antoine Goumain (antoinegoumain.fr) — CC-BY 4.0
Kenney (kenney.nl) — CC0

Built With
Godot Engine 4.4
C# / .NET 8


Special Thanks
The open-source game development community


© 2026 Cordite Wars. All rights reserved.";

    private Label _creditsLabel = null!;
    private float _scrollOffset;
    private bool _fastScroll;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Header with back button
        var header = new HBoxContainer();
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.KeepSize);
        header.OffsetTop = 20;
        header.OffsetLeft = 40;
        header.OffsetRight = -40;
        header.OffsetBottom = 60;
        AddChild(header);

        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
        header.AddChild(backBtn);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(spacer);

        var title = new Label();
        title.Text = Tr("CREDITS_TITLE");
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // Clipping container for scrolling text
        var clipContainer = new Control();
        clipContainer.ClipContents = true;
        clipContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        clipContainer.OffsetTop = 80;
        clipContainer.OffsetLeft = 200;
        clipContainer.OffsetRight = -200;
        clipContainer.OffsetBottom = -40;
        AddChild(clipContainer);

        // Credits label — starts below visible area
        _creditsLabel = new Label();
        _creditsLabel.Text = CreditsText;
        _creditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _creditsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_creditsLabel, UITheme.FontSizeLarge, UITheme.TextPrimary);
        _creditsLabel.Position = new Vector2(0, 0);
        _creditsLabel.Size = new Vector2(clipContainer.Size.X > 0 ? clipContainer.Size.X : 1520, 0);
        _creditsLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        clipContainer.AddChild(_creditsLabel);

        // Start below screen
        _scrollOffset = 600;
    }

    public override void _Process(double delta)
    {
        float speed = _fastScroll ? ScrollSpeed * FastScrollMultiplier : ScrollSpeed;
        _scrollOffset -= speed * (float)delta;
        _creditsLabel.Position = new Vector2(_creditsLabel.Position.X, _scrollOffset);

        // Reset when fully scrolled past
        if (_scrollOffset < -(_creditsLabel.Size.Y + 200))
        {
            GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey keyEvent)
        {
            if (keyEvent.Keycode == Key.Escape && keyEvent.Pressed)
            {
                GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
                GetViewport().SetInputAsHandled();
                return;
            }
            _fastScroll = keyEvent.Pressed;
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventMouseButton mouseEvent)
        {
            _fastScroll = mouseEvent.Pressed;
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventScreenTouch touchEvent)
        {
            _fastScroll = touchEvent.Pressed;
            GetViewport().SetInputAsHandled();
        }
    }
}
