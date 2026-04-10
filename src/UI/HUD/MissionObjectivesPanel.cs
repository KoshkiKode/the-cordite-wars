using Godot;
using CorditeWars.Game;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Small overlay panel shown in the top-right of the screen during campaign
/// missions.  Displays the mission name and objective list from
/// <see cref="CampaignMatchContext"/> so the player always knows what to do.
///
/// Hidden automatically when no campaign context is present (skirmish / MP).
/// </summary>
public partial class MissionObjectivesPanel : CanvasLayer
{
    private VBoxContainer _container = null!;
    private Label _missionTitle = null!;
    private VBoxContainer _objectivesList = null!;

    // ── Factory / initialization ──────────────────────────────────────

    /// <summary>
    /// Initializes the panel from the given campaign context.
    /// If <paramref name="context"/> is null the panel stays invisible.
    /// </summary>
    public void Initialize(CampaignMatchContext? context)
    {
        Layer = 15; // above HUD (10), below pause menu (30)
        BuildUI();

        if (context is null)
        {
            Visible = false;
            return;
        }

        _missionTitle.Text = $"M{context.MissionNumber}: {context.MissionName}";
        PopulateObjectives(context.Objectives);
        Visible = true;
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
        // Semi-transparent background anchored to the top-right
        var bg = new PanelContainer();
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        bg.OffsetLeft = -320;
        bg.OffsetTop = 8;
        bg.OffsetRight = -8;
        bg.OffsetBottom = 8;  // grows downward with content via SIZE_SHRINK_END
        bg.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        bg.CustomMinimumSize = new Vector2(312, 0);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.82f);
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.CornerRadiusTopLeft = 6;
        style.CornerRadiusTopRight = 6;
        style.BorderColor = UITheme.Border;
        style.BorderWidthBottom = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderWidthTop = 1;
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        bg.AddThemeStyleboxOverride("panel", style);
        AddChild(bg);

        _container = new VBoxContainer();
        _container.AddThemeConstantOverride("separation", 4);
        bg.AddChild(_container);

        // Mission title
        _missionTitle = new Label();
        _missionTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        UITheme.StyleLabel(_missionTitle, UITheme.FontSizeSmall, UITheme.Accent);
        _container.AddChild(_missionTitle);

        // Thin separator
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separator", UITheme.Border);
        _container.AddChild(sep);

        // Objectives list container
        _objectivesList = new VBoxContainer();
        _objectivesList.AddThemeConstantOverride("separation", 2);
        _container.AddChild(_objectivesList);
    }

    private void PopulateObjectives(string[] objectives)
    {
        foreach (string obj in objectives)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            _objectivesList.AddChild(row);

            // Bullet
            var bullet = new Label();
            bullet.Text = "▸";
            UITheme.StyleLabel(bullet, UITheme.FontSizeSmall, UITheme.TextMuted);
            row.AddChild(bullet);

            // Objective text
            var lbl = new Label();
            lbl.Text = obj;
            lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            UITheme.StyleLabel(lbl, UITheme.FontSizeSmall, UITheme.TextPrimary);
            row.AddChild(lbl);
        }
    }
}
