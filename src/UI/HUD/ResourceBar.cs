using Godot;
using CorditeWars.Game.Economy;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Top bar HUD: Cordite, Voltaic Charge, Supply counters.
/// Updates every frame from EconomyManager.
/// </summary>
public partial class ResourceBar : PanelContainer
{
    private int _localPlayerId;
    private EconomyManager? _economyManager;

    private Label? _corditeLabel;
    private Label? _vcLabel;
    private Label? _supplyLabel;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(int localPlayerId, EconomyManager economyManager)
    {
        _localPlayerId = localPlayerId;
        _economyManager = economyManager;

        Name = "ResourceBar";

        // Position at top of screen
        AnchorsPreset = (int)LayoutPreset.TopWide;
        CustomMinimumSize = new Vector2(0, 40);
        OffsetBottom = 40;

        // Dark background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.051f, 0.051f, 0.071f, 0.9f); // #0D0D12
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        AddThemeStyleboxOverride("panel", style);

        // Layout
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 40);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(hbox);

        // Cordite
        _corditeLabel = CreateResourceLabel(hbox, "Cordite: 0");

        // Voltaic Charge
        _vcLabel = CreateResourceLabel(hbox, "VC: 0");

        // Supply
        _supplyLabel = CreateResourceLabel(hbox, "Supply: 0/0");
    }

    private Label CreateResourceLabel(HBoxContainer parent, string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f)); // #E0E0E8
        label.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(label);
        return label;
    }

    // ── Update ───────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_economyManager is null) return;

        PlayerEconomy? economy = _economyManager.GetPlayer(_localPlayerId);
        if (economy is null) return;

        int cordite = economy.Cordite.ToInt();
        int vc = economy.VoltaicCharge.ToInt();

        if (_corditeLabel is not null)
            _corditeLabel.Text = $"Cordite: {cordite}";

        if (_vcLabel is not null)
            _vcLabel.Text = $"VC: {vc}";

        if (_supplyLabel is not null)
            _supplyLabel.Text = $"Supply: {economy.CurrentSupply}/{economy.MaxSupply}";
    }
}
