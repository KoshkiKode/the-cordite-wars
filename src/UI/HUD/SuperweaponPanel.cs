using Godot;
using CorditeWars.Core;
using CorditeWars.Systems.Superweapon;

namespace CorditeWars.UI.HUD;

/// <summary>
/// HUD panel showing the local player's superweapon ability.
/// Displays the ability name, a charge/cooldown progress bar, and a fire button.
///
/// Wire up by calling <see cref="Initialize"/> then update each frame via
/// <see cref="Update"/>.
///
/// Pressing the fire button emits <see cref="EventBus.SuperweaponActivateRequested"/>
/// so that <c>CommandInput</c> can enter targeting mode.
/// </summary>
public partial class SuperweaponPanel : PanelContainer
{
    private SuperweaponSystem? _system;
    private int _playerId;
    private Label? _nameLabel;
    private Label? _statusLabel;
    private ProgressBar? _chargeBar;
    private Button? _fireButton;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(int playerId, SuperweaponSystem system)
    {
        _playerId = playerId;
        _system   = system;
        Name      = "SuperweaponPanel";

        // Position: top-right (below tech/resource info)
        AnchorLeft   = 1;
        AnchorTop    = 0;
        AnchorRight  = 1;
        AnchorBottom = 0;
        OffsetLeft   = -220;
        OffsetRight  = -8;
        OffsetTop    = 60;
        OffsetBottom = 130;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.88f);
        bg.SetBorderWidthAll(1);
        bg.BorderColor = new Color(0.2f, 0.2f, 0.35f);
        bg.SetCornerRadiusAll(4);
        bg.ContentMarginLeft = bg.ContentMarginRight = 10;
        bg.ContentMarginTop = bg.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        AddChild(vbox);

        _nameLabel = new Label { Text = "Ability" };
        UITheme.StyleLabel(_nameLabel, UITheme.FontSizeSmall, UITheme.Accent);
        vbox.AddChild(_nameLabel);

        _chargeBar = new ProgressBar();
        _chargeBar.MinValue = 0;
        _chargeBar.MaxValue = 100;
        _chargeBar.Value    = 0;
        _chargeBar.ShowPercentage = false;
        _chargeBar.CustomMinimumSize = new Vector2(180, 12);
        vbox.AddChild(_chargeBar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(row);

        _statusLabel = new Label { Text = "Charging…" };
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        _statusLabel.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        row.AddChild(_statusLabel);

        _fireButton = new Button { Text = "FIRE" };
        UITheme.StyleButton(_fireButton);
        _fireButton.Disabled = true;
        _fireButton.Pressed += OnFirePressed;
        row.AddChild(_fireButton);
    }

    // ── Update ───────────────────────────────────────────────────────

    /// <summary>
    /// Call once per render frame to refresh the HUD from system state.
    /// </summary>
    public void Update()
    {
        if (_system is null) return;

        var state = _system.GetState(_playerId);
        if (state is null) { Visible = false; return; }
        Visible = true;

        if (_nameLabel != null)
            _nameLabel.Text = state.Data.DisplayName;

        float charge = state.ChargePercent * 100f;
        if (_chargeBar != null)
            _chargeBar.Value = charge;

        bool ready = state.IsReady;
        if (_statusLabel != null)
            _statusLabel.Text = ready ? "READY" : $"{(int)(charge)}%";
        if (_statusLabel != null)
            _statusLabel.AddThemeColorOverride("font_color",
                ready ? UITheme.SuccessColor : UITheme.TextMuted);

        if (_fireButton != null)
            _fireButton.Disabled = !ready;
    }

    // ── Button ───────────────────────────────────────────────────────

    private void OnFirePressed()
    {
        EventBus.Instance?.EmitSuperweaponActivateRequested(_playerId);
    }
}
