using Godot;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.UI.Input;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Master HUD container. Lays out ResourceBar (top), MinimapPanel (bottom-left),
/// UnitInfoPanel (bottom-center), CommandCard (bottom-right),
/// ProductionQueueDisplay (above command card), and box-select overlay.
/// </summary>
public partial class GameHUD : CanvasLayer
{
    // ── Children ─────────────────────────────────────────────────────

    private ResourceBar? _resourceBar;
    private MinimapPanel? _minimapPanel;
    private UnitInfoPanel? _unitInfoPanel;
    private CommandCard? _commandCard;
    private ProductionQueueDisplay? _productionQueueDisplay;

    // Box select overlay
    private SelectionManager? _selectionManager;
    private Control? _boxSelectOverlay;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int localPlayerId,
        EconomyManager economyManager,
        SelectionManager selectionManager,
        BuildingPlacer buildingPlacer,
        UnitSpawner unitSpawner,
        UnitDataRegistry unitDataRegistry,
        BuildingRegistry buildingRegistry)
    {
        _selectionManager = selectionManager;
        Name = "GameHUD";
        Layer = 10;

        // Resource Bar — top
        _resourceBar = new ResourceBar();
        _resourceBar.Initialize(localPlayerId, economyManager);
        AddChild(_resourceBar);

        // Minimap — bottom-left
        _minimapPanel = new MinimapPanel();
        _minimapPanel.Initialize();
        AddChild(_minimapPanel);

        // Unit Info — bottom-center
        _unitInfoPanel = new UnitInfoPanel();
        _unitInfoPanel.Initialize(selectionManager, unitDataRegistry);
        AddChild(_unitInfoPanel);

        // Command Card — bottom-right
        _commandCard = new CommandCard();
        _commandCard.Initialize(selectionManager, buildingPlacer, buildingRegistry, unitDataRegistry);
        AddChild(_commandCard);

        // Production Queue Display — above command card
        _productionQueueDisplay = new ProductionQueueDisplay();
        _productionQueueDisplay.Initialize(selectionManager);
        AddChild(_productionQueueDisplay);

        // Box select overlay
        _boxSelectOverlay = new BoxSelectOverlay(selectionManager);
        AddChild(_boxSelectOverlay);
    }
}

/// <summary>
/// Draws the selection rectangle when dragging.
/// </summary>
internal partial class BoxSelectOverlay : Control
{
    private readonly SelectionManager _selectionManager;

    public BoxSelectOverlay(SelectionManager selectionManager)
    {
        _selectionManager = selectionManager;
        Name = "BoxSelectOverlay";
        AnchorsPreset = (int)LayoutPreset.FullRect;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (!_selectionManager.IsDragging) return;

        Rect2 rect = _selectionManager.GetDragRect();
        if (rect.Size.LengthSquared() < 64) return;

        // Green selection box
        DrawRect(rect, new Color(0.29f, 0.62f, 0.80f, 0.15f), true);
        DrawRect(rect, new Color(0.29f, 0.62f, 0.80f, 0.8f), false, 1.5f);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }
}
