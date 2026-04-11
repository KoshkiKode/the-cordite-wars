using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.UI.Input;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Bottom-right 3x4 grid of command buttons. Context-sensitive:
/// units → Move/Attack/Stop/Hold/Patrol/abilities.
/// Buildings → production buttons, rally point, upgrades.
/// </summary>
public partial class CommandCard : PanelContainer
{
    private const int Columns = 4;
    private const int Rows = 3;

    private SelectionManager? _selectionManager;
    private BuildingPlacer? _buildingPlacer;
    private BuildingRegistry? _buildingRegistry;
    private UnitDataRegistry? _unitDataRegistry;

    private GridContainer? _grid;
    private readonly Button[] _buttons = new Button[Columns * Rows];

    // Current button bindings
    private readonly CardAction[] _actions = new CardAction[Columns * Rows];

    // Campaign building restriction — null means all buildings are allowed
    private System.Collections.Generic.HashSet<string>? _allowedBuildingIds;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        SelectionManager selectionManager,
        BuildingPlacer buildingPlacer,
        BuildingRegistry buildingRegistry,
        UnitDataRegistry unitDataRegistry,
        System.Collections.Generic.HashSet<string>? allowedBuildingIds = null)
    {
        _selectionManager = selectionManager;
        _buildingPlacer = buildingPlacer;
        _buildingRegistry = buildingRegistry;
        _unitDataRegistry = unitDataRegistry;
        _allowedBuildingIds = allowedBuildingIds;

        Name = "CommandCard";

        // Bottom-right positioning
        AnchorLeft = 1;
        AnchorTop = 1;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = -260;
        OffsetTop = -180;
        OffsetRight = -8;
        OffsetBottom = -8;

        CustomMinimumSize = new Vector2(250, 170);

        // Dark panel
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.102f, 0.102f, 0.141f, 0.92f); // #1A1A24
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f);
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        AddThemeStyleboxOverride("panel", style);

        BuildGrid();

        // Subscribe to selection changes
        EventBus.Instance?.Connect(EventBus.SignalName.SelectionChanged,
            Callable.From<int[]>(OnSelectionChanged));
    }

    private void BuildGrid()
    {
        _grid = new GridContainer();
        _grid.Columns = Columns;
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        AddChild(_grid);

        for (int i = 0; i < Columns * Rows; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(54, 46);
            btn.Text = string.Empty;
            StyleCardButton(btn);

            int index = i;
            btn.Pressed += () => OnButtonPressed(index);
            _buttons[i] = btn;
            _grid.AddChild(btn);
        }
    }

    // ── Selection Changed ────────────────────────────────────────────

    private void OnSelectionChanged(int[] unitIds)
    {
        ClearAllButtons();

        if (unitIds.Length == 0)
        {
            Visible = false;
            return;
        }

        Visible = true;

        // Check if selection is units or buildings — for now, units only
        PopulateUnitCommands();
    }

    private void PopulateUnitCommands()
    {
        SetButton(0, "Move", "M", CardActionType.Move);
        SetButton(1, "Attack", "A", CardActionType.AttackMove);
        SetButton(2, "Stop", "S", CardActionType.Stop);
        SetButton(3, "Hold", "H", CardActionType.Hold);
        SetButton(4, "Patrol", "P", CardActionType.Patrol);
    }

    /// <summary>
    /// Populates the command card with building production buttons.
    /// Called externally when a building is selected.
    /// </summary>
    public void PopulateBuildingProduction(BuildingData buildingData, BuildingInstance building)
    {
        ClearAllButtons();
        Visible = true;

        if (_unitDataRegistry is null) return;

        // Show units this building can produce
        int slot = 0;
        for (int i = 0; i < buildingData.UnlocksUnitIds.Count && slot < Columns * Rows; i++)
        {
            string unitId = buildingData.UnlocksUnitIds[i];
            if (!_unitDataRegistry.HasUnit(unitId)) continue;

            UnitData unitData = _unitDataRegistry.GetUnitData(unitId);
            string label = unitData.DisplayName.Length > 6
                ? unitData.DisplayName[..6]
                : unitData.DisplayName;
            string tooltip = $"{unitData.DisplayName}\nCost: {unitData.Cost}C {unitData.SecondaryCost}VC";

            SetButton(slot, label, string.Empty, CardActionType.ProduceUnit, unitId);
            _buttons[slot].TooltipText = tooltip;
            slot++;
        }

        // Rally point button in last slot
        SetButton(Columns * Rows - 1, "Rally", "R", CardActionType.SetRally);
    }

    /// <summary>
    /// Populates building placement buttons for a construction building (e.g., HQ).
    /// When a campaign <see cref="_allowedBuildingIds"/> set is present only those
    /// buildings are shown, hiding higher-tier structures not yet unlocked.
    /// </summary>
    public void PopulateBuildingPlacement(string factionId)
    {
        ClearAllButtons();
        Visible = true;

        if (_buildingRegistry is null) return;

        var buildings = _buildingRegistry.GetFactionBuildings(factionId);
        int slot = 0;
        for (int i = 0; i < buildings.Count && slot < Columns * Rows; i++)
        {
            // Skip buildings not yet unlocked for this campaign mission
            if (_allowedBuildingIds != null && !_allowedBuildingIds.Contains(buildings[i].Id))
                continue;

            string label = buildings[i].DisplayName.Length > 6
                ? buildings[i].DisplayName[..6]
                : buildings[i].DisplayName;
            string tooltip = $"{buildings[i].DisplayName}\nCost: {buildings[i].Cost}C {buildings[i].SecondaryCost}VC";

            SetButton(slot, label, string.Empty, CardActionType.PlaceBuilding, buildings[i].Id);
            _buttons[slot].TooltipText = tooltip;
            slot++;
        }
    }

    // ── Button Actions ───────────────────────────────────────────────

    private void OnButtonPressed(int index)
    {
        if (index < 0 || index >= _actions.Length) return;
        var action = _actions[index];
        if (action.Type == CardActionType.None) return;

        switch (action.Type)
        {
            case CardActionType.Move:
                // Already handled by right-click, but could enter move cursor
                break;
            case CardActionType.AttackMove:
                // Enter attack-move mode — CommandInput handles the hotkey too
                break;
            case CardActionType.Stop:
                EmitStopCommand();
                break;
            case CardActionType.Hold:
                EmitHoldCommand();
                break;
            case CardActionType.Patrol:
                // Enter patrol mode
                break;
            case CardActionType.ProduceUnit:
                EmitProduceUnit(action.TargetId);
                break;
            case CardActionType.PlaceBuilding:
                _buildingPlacer?.EnterPlacementMode(action.TargetId);
                break;
        }
    }

    private void EmitStopCommand()
    {
        // Synthesize key press for CommandInput
        var keyEvent = new InputEventKey();
        keyEvent.Keycode = Key.S;
        keyEvent.Pressed = true;
        GetViewport().PushInput(keyEvent);
    }

    private void EmitHoldCommand()
    {
        var keyEvent = new InputEventKey();
        keyEvent.Keycode = Key.H;
        keyEvent.Pressed = true;
        GetViewport().PushInput(keyEvent);
    }

    private void EmitProduceUnit(string unitTypeId)
    {
        // Production is handled via ProductionQueue — signal through EventBus
        EventBus.Instance?.EmitBuildCommandIssued(unitTypeId, Vector3.Zero);
    }

    // ── Button Management ────────────────────────────────────────────

    private void SetButton(int index, string text, string hotkey, CardActionType type, string targetId = "")
    {
        if (index < 0 || index >= _buttons.Length) return;

        string display = string.IsNullOrEmpty(hotkey) ? text : $"[{hotkey}] {text}";
        _buttons[index].Text = display;
        _buttons[index].Disabled = false;
        _actions[index] = new CardAction { Type = type, TargetId = targetId };
    }

    private void ClearAllButtons()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].Text = string.Empty;
            _buttons[i].Disabled = true;
            _buttons[i].TooltipText = string.Empty;
            _actions[i] = new CardAction { Type = CardActionType.None };
        }
    }

    // ── Styling ──────────────────────────────────────────────────────

    private void StyleCardButton(Button btn)
    {
        var normal = new StyleBoxFlat();
        normal.BgColor = new Color(0.102f, 0.102f, 0.141f); // #1A1A24
        normal.BorderWidthBottom = 1;
        normal.BorderWidthTop = 1;
        normal.BorderWidthLeft = 1;
        normal.BorderWidthRight = 1;
        normal.BorderColor = new Color(0.165f, 0.165f, 0.227f);
        normal.CornerRadiusTopLeft = 3;
        normal.CornerRadiusTopRight = 3;
        normal.CornerRadiusBottomLeft = 3;
        normal.CornerRadiusBottomRight = 3;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = new Color(0.145f, 0.145f, 0.208f); // #252535
        hover.BorderWidthBottom = 1;
        hover.BorderWidthTop = 1;
        hover.BorderWidthLeft = 1;
        hover.BorderWidthRight = 1;
        hover.BorderColor = new Color(0.29f, 0.62f, 0.80f); // #4A9ECC
        hover.CornerRadiusTopLeft = 3;
        hover.CornerRadiusTopRight = 3;
        hover.CornerRadiusBottomLeft = 3;
        hover.CornerRadiusBottomRight = 3;
        btn.AddThemeStyleboxOverride("hover", hover);

        var pressed = new StyleBoxFlat();
        pressed.BgColor = new Color(0.29f, 0.62f, 0.80f, 0.3f);
        pressed.BorderWidthBottom = 1;
        pressed.BorderWidthTop = 1;
        pressed.BorderWidthLeft = 1;
        pressed.BorderWidthRight = 1;
        pressed.BorderColor = new Color(0.29f, 0.62f, 0.80f);
        pressed.CornerRadiusTopLeft = 3;
        pressed.CornerRadiusTopRight = 3;
        pressed.CornerRadiusBottomLeft = 3;
        pressed.CornerRadiusBottomRight = 3;
        btn.AddThemeStyleboxOverride("pressed", pressed);

        btn.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f));
        btn.AddThemeFontSizeOverride("font_size", 11);
    }
}

// ── Supporting Types ─────────────────────────────────────────────────

internal enum CardActionType
{
    None,
    Move,
    AttackMove,
    Stop,
    Hold,
    Patrol,
    ProduceUnit,
    PlaceBuilding,
    SetRally
}

internal struct CardAction
{
    public CardActionType Type;
    public string TargetId;
}
