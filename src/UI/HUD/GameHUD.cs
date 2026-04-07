using Godot;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;
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

        // Minimap — bottom-left (terrain wired via SetupMinimapData after map loads)
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

    /// <summary>
    /// Wires the minimap to live terrain and game data.
    /// Call once after the map TerrainGrid is ready (i.e. after GameSession.StartMatch).
    /// </summary>
    public void SetupMinimapData(
        TerrainGrid terrain,
        int gridWidth,
        int gridHeight,
        UnitSpawner unitSpawner,
        BuildingPlacer buildingPlacer,
        RTSCamera camera)
    {
        _minimapPanel?.SetupLiveData(terrain, gridWidth, gridHeight, unitSpawner, buildingPlacer, camera);
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

        var accessibility = AccessibilitySettings.Instance;
        Color fill, border;
        if (accessibility is not null)
        {
            (fill, border) = accessibility.GetSelectionBoxColors();
        }
        else
        {
            fill = new Color(0.29f, 0.62f, 0.80f, 0.15f);
            border = new Color(0.29f, 0.62f, 0.80f, 0.8f);
        }

        DrawRect(rect, fill, true);
        DrawRect(rect, border, false, 1.5f);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }
}
