using Godot;
using CorditeWars.Game;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.Systems.Pathfinding;
using CorditeWars.Systems.Superweapon;
using CorditeWars.UI.Input;

namespace CorditeWars.UI.HUD;

/// <summary>
/// Master HUD container. Lays out ResourceBar (top), MinimapPanel (bottom-left),
/// UnitInfoPanel (bottom-center), CommandCard (bottom-right),
/// ProductionQueueDisplay (above command card), and box-select overlay.
/// When a campaign context is present, also shows
/// <see cref="MissionObjectivesPanel"/> in the top-right corner.
/// </summary>
public partial class GameHUD : CanvasLayer
{
    // ── Children ─────────────────────────────────────────────────────

    private ResourceBar? _resourceBar;
    private MinimapPanel? _minimapPanel;
    private UnitInfoPanel? _unitInfoPanel;
    private CommandCard? _commandCard;
    private ProductionQueueDisplay? _productionQueueDisplay;
    private MissionObjectivesPanel? _missionObjectivesPanel;
    private ChatPanel? _chatPanel;
    private SuperweaponPanel? _superweaponPanel;

    // Box select overlay
    private SelectionManager? _selectionManager;
    private Control? _boxSelectOverlay;

    // ── Initialization ───────────────────────────────────────────────

    /// <summary>
    /// Wires the CommandCard rally button to <paramref name="commandInput"/>.
    /// Call after Initialize once CommandInput is available.
    /// </summary>
    public void SetCommandInput(CommandInput commandInput)
    {
        if (_commandCard is not null)
            _commandCard.RallyModeRequested += () => commandInput.SetRallyMode(true);
    }

    public void Initialize(
        int localPlayerId,
        EconomyManager economyManager,
        SelectionManager selectionManager,
        BuildingPlacer buildingPlacer,
        UnitSpawner unitSpawner,
        UnitDataRegistry unitDataRegistry,
        BuildingRegistry buildingRegistry,
        CampaignMatchContext? campaignContext = null,
        string playerName = "Commander",
        Color playerColor = default,
        SuperweaponSystem? superweaponSystem = null)
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
        _commandCard.Initialize(selectionManager, buildingPlacer, buildingRegistry, unitDataRegistry,
            campaignContext?.AllowedBuildingIds);
        AddChild(_commandCard);

        // Production Queue Display — above command card
        _productionQueueDisplay = new ProductionQueueDisplay();
        _productionQueueDisplay.Initialize(selectionManager);
        AddChild(_productionQueueDisplay);

        // Box select overlay
        _boxSelectOverlay = new BoxSelectOverlay(selectionManager);
        AddChild(_boxSelectOverlay);

        // Mission objectives panel — top-right, campaign missions only
        _missionObjectivesPanel = new MissionObjectivesPanel();
        _missionObjectivesPanel.Initialize(campaignContext);
        AddChild(_missionObjectivesPanel);

        // Chat panel — bottom-left overlay (above minimap)
        _chatPanel = new ChatPanel();
        Color chatColor = playerColor == default ? new Color(0.3f, 0.75f, 1f) : playerColor;
        _chatPanel.Initialize(localPlayerId, playerName, chatColor);
        AddChild(_chatPanel);

        // Superweapon panel — top-right
        if (superweaponSystem != null)
        {
            _superweaponPanel = new SuperweaponPanel();
            _superweaponPanel.Initialize(localPlayerId, superweaponSystem);
            AddChild(_superweaponPanel);
        }
    }

    public override void _Process(double delta)
    {
        _superweaponPanel?.Update();
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
    private static readonly Color DefaultSelectionFill = new(0.29f, 0.62f, 0.80f, 0.15f);
    private static readonly Color DefaultSelectionBorder = new(0.29f, 0.62f, 0.80f, 0.8f);
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
            fill = DefaultSelectionFill;
            border = DefaultSelectionBorder;
        }

        DrawRect(rect, fill, true);
        DrawRect(rect, border, false, 1.5f);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }
}
