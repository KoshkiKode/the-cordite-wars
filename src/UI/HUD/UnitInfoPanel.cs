using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Units;
using CorditeWars.UI.Input;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Bottom-center panel showing selected unit/building info.
/// Single unit: portrait, name, HP bar, armor, weapons.
/// Multi-select: grid of unit icons with HP bars.
/// </summary>
public partial class UnitInfoPanel : PanelContainer
{
    private SelectionManager? _selectionManager;
    private UnitDataRegistry? _unitDataRegistry;

    // Single unit display
    private VBoxContainer? _singleUnitDisplay;
    private Label? _unitNameLabel;
    private ProgressBar? _hpBar;
    private Label? _hpLabel;
    private Label? _armorLabel;
    private Label? _weaponLabel;
    private Label? _veterancyLabel;
    private Label? _stanceLabel;

    // Multi-select display
    private GridContainer? _multiSelectGrid;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(SelectionManager selectionManager, UnitDataRegistry unitDataRegistry)
    {
        _selectionManager = selectionManager;
        _unitDataRegistry = unitDataRegistry;

        Name = "UnitInfoPanel";

        // Bottom-center positioning
        AnchorLeft = 0.3f;
        AnchorTop = 1;
        AnchorRight = 0.7f;
        AnchorBottom = 1;
        OffsetTop = -160;
        OffsetBottom = -8;

        CustomMinimumSize = new Vector2(300, 150);

        // Dark panel
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.102f, 0.102f, 0.141f, 0.92f); // #1A1A24
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f); // #2A2A3A
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        AddThemeStyleboxOverride("panel", style);

        BuildSingleUnitDisplay();
        BuildMultiSelectDisplay();

        // Subscribe to selection changes
        EventBus.Instance?.Connect(EventBus.SignalName.SelectionChanged,
            Callable.From<int[]>(OnSelectionChanged));
    }

    private void BuildSingleUnitDisplay()
    {
        _singleUnitDisplay = new VBoxContainer();
        _singleUnitDisplay.Name = "SingleUnitDisplay";
        _singleUnitDisplay.AddThemeConstantOverride("separation", 4);
        AddChild(_singleUnitDisplay);

        _unitNameLabel = new Label();
        _unitNameLabel.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f));
        _unitNameLabel.AddThemeFontSizeOverride("font_size", 18);
        _singleUnitDisplay.AddChild(_unitNameLabel);

        // HP Bar
        var hpContainer = new HBoxContainer();
        hpContainer.AddThemeConstantOverride("separation", 8);
        _singleUnitDisplay.AddChild(hpContainer);

        _hpBar = new ProgressBar();
        _hpBar.CustomMinimumSize = new Vector2(200, 16);
        _hpBar.MaxValue = 100;
        _hpBar.ShowPercentage = false;
        StyleHPBar(_hpBar);
        hpContainer.AddChild(_hpBar);

        _hpLabel = new Label();
        _hpLabel.AddThemeColorOverride("font_color", new Color(0.533f, 0.533f, 0.627f));
        _hpLabel.AddThemeFontSizeOverride("font_size", 14);
        hpContainer.AddChild(_hpLabel);

        _armorLabel = new Label();
        _armorLabel.AddThemeColorOverride("font_color", new Color(0.533f, 0.533f, 0.627f));
        _armorLabel.AddThemeFontSizeOverride("font_size", 14);
        _singleUnitDisplay.AddChild(_armorLabel);

        _weaponLabel = new Label();
        _weaponLabel.AddThemeColorOverride("font_color", new Color(0.533f, 0.533f, 0.627f));
        _weaponLabel.AddThemeFontSizeOverride("font_size", 14);
        _singleUnitDisplay.AddChild(_weaponLabel);

        // Veterancy row
        var vetRow = new HBoxContainer();
        vetRow.AddThemeConstantOverride("separation", 8);
        _singleUnitDisplay.AddChild(vetRow);

        _veterancyLabel = new Label();
        _veterancyLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.25f)); // gold
        _veterancyLabel.AddThemeFontSizeOverride("font_size", 13);
        vetRow.AddChild(_veterancyLabel);

        _stanceLabel = new Label();
        _stanceLabel.AddThemeColorOverride("font_color", new Color(0.533f, 0.533f, 0.627f));
        _stanceLabel.AddThemeFontSizeOverride("font_size", 13);
        vetRow.AddChild(_stanceLabel);

        _singleUnitDisplay.Visible = false;
    }

    private void BuildMultiSelectDisplay()
    {
        _multiSelectGrid = new GridContainer();
        _multiSelectGrid.Name = "MultiSelectGrid";
        _multiSelectGrid.Columns = 8;
        _multiSelectGrid.AddThemeConstantOverride("h_separation", 4);
        _multiSelectGrid.AddThemeConstantOverride("v_separation", 4);
        AddChild(_multiSelectGrid);
        _multiSelectGrid.Visible = false;
    }

    // ── Selection Changed ────────────────────────────────────────────

    private void OnSelectionChanged(int[] unitIds)
    {
        if (_selectionManager is null) return;

        int count = _selectionManager.SelectedCount;

        if (count == 0)
        {
            _singleUnitDisplay!.Visible = false;
            _multiSelectGrid!.Visible = false;
            Visible = false;
            return;
        }

        Visible = true;

        if (count == 1)
        {
            ShowSingleUnit(_selectionManager.GetSelectedUnits()[0]);
        }
        else
        {
            ShowMultiSelect(_selectionManager.GetSelectedUnits());
        }
    }

    // ── Single Unit Display ──────────────────────────────────────────

    private void ShowSingleUnit(UnitNode3D unit)
    {
        _singleUnitDisplay!.Visible = true;
        _multiSelectGrid!.Visible = false;

        string displayName = unit.UnitTypeId;
        if (_unitDataRegistry is not null && _unitDataRegistry.HasUnit(unit.UnitTypeId))
            displayName = _unitDataRegistry.GetUnitData(unit.UnitTypeId).DisplayName;

        _unitNameLabel!.Text = displayName;

        float hpPercent = unit.Health.ToFloat() / GetMaxHealth(unit) * 100f;
        _hpBar!.Value = hpPercent;
        _hpLabel!.Text = $"{unit.Health.ToInt()}/{(int)GetMaxHealth(unit)}";

        if (_unitDataRegistry is not null && _unitDataRegistry.HasUnit(unit.UnitTypeId))
        {
            var data = _unitDataRegistry.GetUnitData(unit.UnitTypeId);
            _armorLabel!.Text = $"Armor: {data.ArmorClass} ({data.ArmorValue.ToInt()})";

            if (data.Weapons.Count > 0)
                _weaponLabel!.Text = $"Weapon: {data.Weapons[0].Type} (DMG: {data.Weapons[0].Damage.ToInt()})";
            else
                _weaponLabel!.Text = "No weapons";
        }

        // Veterancy & stance
        string vetStars = unit.Veterancy switch
        {
            CorditeWars.Systems.Pathfinding.VeterancyLevel.Heroic  => "★★★★ Heroic",
            CorditeWars.Systems.Pathfinding.VeterancyLevel.Elite   => "★★★ Elite",
            CorditeWars.Systems.Pathfinding.VeterancyLevel.Veteran => "★★ Veteran",
            _                                                        => "★ Recruit"
        };
        if (_veterancyLabel != null)
            _veterancyLabel.Text = vetStars;

        string stanceName = unit.Stance switch
        {
            CorditeWars.Systems.Pathfinding.UnitStance.Defensive  => "Defensive",
            CorditeWars.Systems.Pathfinding.UnitStance.HoldGround => "Hold Ground",
            CorditeWars.Systems.Pathfinding.UnitStance.HoldFire   => "Hold Fire",
            _                                                       => "Aggressive"
        };
        if (_stanceLabel != null)
            _stanceLabel.Text = $"| {stanceName}";
    }

    private float GetMaxHealth(UnitNode3D unit)
    {
        if (_unitDataRegistry is not null && _unitDataRegistry.HasUnit(unit.UnitTypeId))
            return _unitDataRegistry.GetUnitData(unit.UnitTypeId).MaxHealth.ToFloat();
        return 100f;
    }

    // ── Multi-Select Display ─────────────────────────────────────────

    private void ShowMultiSelect(IList<UnitNode3D> units)
    {
        _singleUnitDisplay!.Visible = false;
        _multiSelectGrid!.Visible = true;

        // Clear existing icons
        for (int i = _multiSelectGrid.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _multiSelectGrid.GetChild(i);
            _multiSelectGrid.RemoveChild(child);
            child.QueueFree();
        }

        // Add unit icons (max 24)
        int count = units.Count;
        if (count > 24) count = 24;

        for (int i = 0; i < count; i++)
        {
            var icon = CreateUnitIcon(units[i]);
            _multiSelectGrid.AddChild(icon);
        }
    }

    private Control CreateUnitIcon(UnitNode3D unit)
    {
        var container = new PanelContainer();
        container.CustomMinimumSize = new Vector2(32, 40);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.102f, 0.102f, 0.141f);
        style.BorderWidthBottom = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f);
        container.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        container.AddChild(vbox);

        // Type label
        var label = new Label();
        label.Text = unit.UnitTypeId.Length > 4 ? unit.UnitTypeId[..4] : unit.UnitTypeId;
        label.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f));
        label.AddThemeFontSizeOverride("font_size", 10);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(label);

        // HP bar (mini)
        var hpBar = new ProgressBar();
        hpBar.CustomMinimumSize = new Vector2(28, 4);
        hpBar.MaxValue = 100;
        hpBar.Value = unit.Health.ToFloat() / GetMaxHealth(unit) * 100f;
        hpBar.ShowPercentage = false;
        StyleHPBar(hpBar);
        vbox.AddChild(hpBar);

        return container;
    }

    // ── Update (refresh HP bars) ─────────────────────────────────────

    public override void _Process(double delta)
    {
        if (!Visible || _selectionManager is null || _selectionManager.SelectedCount == 0) return;

        if (_singleUnitDisplay!.Visible && _selectionManager.SelectedCount == 1)
        {
            var unit = _selectionManager.GetSelectedUnits()[0];
            if (unit.IsAlive)
            {
                float hpPercent = unit.Health.ToFloat() / GetMaxHealth(unit) * 100f;
                _hpBar!.Value = hpPercent;
                _hpLabel!.Text = $"{unit.Health.ToInt()}/{(int)GetMaxHealth(unit)}";
            }
        }
    }

    // ── Styling ──────────────────────────────────────────────────────

    private void StyleHPBar(ProgressBar bar)
    {
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.165f, 0.165f, 0.227f); // #2A2A3A
        bar.AddThemeStyleboxOverride("background", bgStyle);

        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = new Color(0.267f, 0.667f, 0.267f); // Green HP
        bar.AddThemeStyleboxOverride("fill", fillStyle);
    }
}
