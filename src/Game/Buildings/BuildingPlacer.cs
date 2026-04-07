using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Economy;
using CorditeWars.Game.World;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.Buildings;

/// <summary>
/// Ghost preview and placement validation for building construction.
/// Handles grid snapping, validity checks, and resource deduction.
/// </summary>
public partial class BuildingPlacer : Node
{
    // ── Dependencies ─────────────────────────────────────────────────

    private OccupancyGrid? _occupancyGrid;
    private EconomyManager? _economyManager;
    private BuildingRegistry? _buildingRegistry;
    private BuildingManifest? _buildingManifest;
    private Camera3D? _camera;
    private int _localPlayerId;

    // ── Placement State ──────────────────────────────────────────────

    private bool _isPlacing;
    private string _placingBuildingId = string.Empty;
    private BuildingData? _placingData;
    private BuildingModelEntry? _placingModel;
    private Node3D? _ghostMesh;
    private Vector3 _ghostPosition;
    private bool _isValidPlacement;

    // Grid snap size (matches occupancy grid cell size)
    private const float CellSize = 1f;

    // Build radius from nearest HQ/FOB
    private const float MaxBuildRadius = 50f;

    // Track placed buildings
    private readonly SortedList<int, BuildingInstance> _buildings = new();
    private int _nextBuildingId = 1;

    // HQ positions per player for build radius check
    private readonly SortedList<int, List<FixedVector2>> _hqPositions = new();

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int localPlayerId,
        OccupancyGrid occupancyGrid,
        EconomyManager economyManager,
        BuildingRegistry buildingRegistry,
        BuildingManifest buildingManifest,
        Camera3D camera)
    {
        _localPlayerId = localPlayerId;
        _occupancyGrid = occupancyGrid;
        _economyManager = economyManager;
        _buildingRegistry = buildingRegistry;
        _buildingManifest = buildingManifest;
        _camera = camera;
    }

    // ── Public API ───────────────────────────────────────────────────

    public bool IsPlacing => _isPlacing;

    public void EnterPlacementMode(string buildingId)
    {
        if (_buildingRegistry is null || !_buildingRegistry.HasBuilding(buildingId)) return;

        _placingBuildingId = buildingId;
        _placingData = _buildingRegistry.GetBuilding(buildingId);
        _placingModel = _buildingManifest?.HasEntry(buildingId) == true
            ? _buildingManifest.GetEntry(buildingId)
            : null;

        _isPlacing = true;
        CreateGhostMesh();
    }

    public void CancelPlacement()
    {
        _isPlacing = false;
        _placingBuildingId = string.Empty;
        _placingData = null;
        _placingModel = null;
        DestroyGhostMesh();
    }

    public void RegisterHQPosition(int playerId, FixedVector2 position)
    {
        if (!_hqPositions.ContainsKey(playerId))
            _hqPositions.Add(playerId, new List<FixedVector2>());
        _hqPositions[playerId].Add(position);
    }

    public BuildingInstance? GetBuilding(int buildingId)
    {
        if (_buildings.ContainsKey(buildingId))
            return _buildings[buildingId];
        return null;
    }

    public IList<BuildingInstance> GetAllBuildings() => _buildings.Values;

    /// <summary>
    /// Restores a building from save data without cost validation or placement checks.
    /// Used during LoadFromSave to rebuild the building list.
    /// </summary>
    public void RestoreBuilding(
        int buildingId,
        string buildingTypeId,
        int playerId,
        int gridX,
        int gridY,
        FixedPoint health,
        bool isConstructed,
        FixedPoint constructionProgress)
    {
        if (_buildingRegistry is null || !_buildingRegistry.HasBuilding(buildingTypeId))
        {
            GD.PushWarning($"[BuildingPlacer] Cannot restore unknown building type '{buildingTypeId}'.");
            return;
        }

        BuildingData data = _buildingRegistry.GetBuilding(buildingTypeId);

        var instance = new BuildingInstance();
        instance.Initialize(buildingId, buildingTypeId, data, playerId, gridX, gridY);
        instance.RestoreState(health, isConstructed, constructionProgress);

        AddChild(instance);
        _buildings.Add(buildingId, instance);

        // Reserve footprint in occupancy grid
        _occupancyGrid?.OccupyFootprint(
            gridX, gridY,
            data.FootprintWidth, data.FootprintHeight,
            OccupancyType.Building, buildingId, playerId);

        // Ensure _nextBuildingId stays ahead of restored IDs
        if (buildingId >= _nextBuildingId)
            _nextBuildingId = buildingId + 1;

        GD.Print($"[BuildingPlacer] Restored {buildingTypeId} (id={buildingId}) at ({gridX}, {gridY}).");
    }

    // ── Input Processing ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isPlacing) return;

        if (@event is InputEventMouseMotion mouseMotion)
        {
            UpdateGhostPosition(mouseMotion.Position);
        }
        else if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                TryPlaceBuilding();
                GetViewport().SetInputAsHandled();
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                CancelPlacement();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── Ghost Mesh ───────────────────────────────────────────────────

    private void CreateGhostMesh()
    {
        DestroyGhostMesh();

        if (_placingData is null) return;

        _ghostMesh = new Node3D();
        _ghostMesh.Name = "BuildingGhost";

        // Create a simple box mesh for preview
        var meshInst = new MeshInstance3D();
        var boxMesh = new BoxMesh();
        boxMesh.Size = new Vector3(
            _placingData.FootprintWidth * CellSize,
            2f,
            _placingData.FootprintHeight * CellSize);
        meshInst.Mesh = boxMesh;
        meshInst.Position = new Vector3(0, 1f, 0);

        // Transparent green material
        var mat = new StandardMaterial3D();
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.AlbedoColor = new Color(0.0f, 1.0f, 0.0f, 0.35f);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        meshInst.MaterialOverride = mat;

        _ghostMesh.AddChild(meshInst);
        AddChild(_ghostMesh);
    }

    private void DestroyGhostMesh()
    {
        if (_ghostMesh is not null)
        {
            _ghostMesh.QueueFree();
            _ghostMesh = null;
        }
    }

    private void UpdateGhostPosition(Vector2 screenPos)
    {
        if (_ghostMesh is null || _camera is null || _placingData is null) return;

        Vector3? worldPos = RaycastGround(screenPos);
        if (!worldPos.HasValue) return;

        // Snap to grid
        float halfW = _placingData.FootprintWidth * CellSize * 0.5f;
        float halfH = _placingData.FootprintHeight * CellSize * 0.5f;
        float snappedX = Mathf.Round(worldPos.Value.X / CellSize) * CellSize;
        float snappedZ = Mathf.Round(worldPos.Value.Z / CellSize) * CellSize;

        _ghostPosition = new Vector3(snappedX, 0f, snappedZ);
        _ghostMesh.GlobalPosition = _ghostPosition;

        // Validate placement
        _isValidPlacement = ValidatePlacement(snappedX, snappedZ);

        // Update ghost color
        UpdateGhostColor();
    }

    private void UpdateGhostColor()
    {
        if (_ghostMesh is null) return;

        var meshInst = _ghostMesh.GetChild(0) as MeshInstance3D;
        if (meshInst?.MaterialOverride is StandardMaterial3D mat)
        {
            mat.AlbedoColor = _isValidPlacement
                ? new Color(0.0f, 1.0f, 0.0f, 0.35f)  // Green = valid
                : new Color(1.0f, 0.0f, 0.0f, 0.35f);  // Red = invalid
        }
    }

    // ── Validation ───────────────────────────────────────────────────

    private bool ValidatePlacement(float worldX, float worldZ)
    {
        if (_placingData is null || _occupancyGrid is null || _economyManager is null) return false;

        int gridX = (int)Mathf.Round(worldX);
        int gridY = (int)Mathf.Round(worldZ);
        int fw = _placingData.FootprintWidth;
        int fh = _placingData.FootprintHeight;

        // Check occupancy grid — all cells must be free
        if (!_occupancyGrid.IsFootprintFree(gridX, gridY, fw, fh))
            return false;

        // Check build radius from nearest HQ/FOB
        if (!IsWithinBuildRadius(worldX, worldZ))
            return false;

        // Check if player can afford it
        PlayerEconomy? economy = _economyManager.GetPlayer(_localPlayerId);
        if (economy is null) return false;
        if (!economy.CanAfford(_placingData.Cost, _placingData.SecondaryCost))
            return false;

        return true;
    }

    private bool IsWithinBuildRadius(float worldX, float worldZ)
    {
        if (!_hqPositions.ContainsKey(_localPlayerId))
            return true; // No HQ registered = allow everywhere (early game)

        var positions = _hqPositions[_localPlayerId];
        for (int i = 0; i < positions.Count; i++)
        {
            float dx = worldX - positions[i].X.ToFloat();
            float dz = worldZ - positions[i].Y.ToFloat();
            float distSq = dx * dx + dz * dz;
            if (distSq <= MaxBuildRadius * MaxBuildRadius)
                return true;
        }
        return false;
    }

    // ── Placement ────────────────────────────────────────────────────

    private void TryPlaceBuilding()
    {
        if (!_isValidPlacement || _placingData is null || _economyManager is null) return;

        // Deduct resources
        if (!_economyManager.TryBuildBuilding(_localPlayerId, _placingData))
        {
            EventBus.Instance?.EmitInsufficientFunds(_localPlayerId);
            return;
        }

        // Create the building instance
        int buildingId = _nextBuildingId++;
        int gridX = (int)Mathf.Round(_ghostPosition.X);
        int gridY = (int)Mathf.Round(_ghostPosition.Z);

        var instance = new BuildingInstance();
        instance.Initialize(
            buildingId,
            _placingBuildingId,
            _placingData,
            _localPlayerId,
            gridX, gridY);

        AddChild(instance);
        _buildings.Add(buildingId, instance);

        // Reserve footprint in occupancy grid
        _occupancyGrid?.OccupyFootprint(
            gridX, gridY,
            _placingData.FootprintWidth, _placingData.FootprintHeight,
            OccupancyType.Building, buildingId, _localPlayerId);

        EventBus.Instance?.EmitBuildingPlaced(instance);
        EventBus.Instance?.EmitBuildCommandIssued(_placingBuildingId, _ghostPosition);

        GD.Print($"[BuildingPlacer] Placed {_placingBuildingId} at ({gridX}, {gridY}).");

        // Exit placement mode
        CancelPlacement();
    }

    // ── Building Lifecycle ───────────────────────────────────────────

    public void OnBuildingDestroyed(BuildingInstance building)
    {
        if (_buildings.ContainsKey(building.BuildingId))
        {
            _buildings.Remove(building.BuildingId);

            // Vacate occupancy
            _occupancyGrid?.VacateFootprint(
                building.GridX, building.GridY,
                building.Data?.FootprintWidth ?? 3,
                building.Data?.FootprintHeight ?? 3);

            // Unregister from economy
            if (building.IsConstructed && building.Data is not null)
                _economyManager?.OnBuildingDestroyed(building.PlayerId, building.Data);

            EventBus.Instance?.EmitBuildingDestroyed(building);
        }
    }

    // ── Raycasting ───────────────────────────────────────────────────

    private Vector3? RaycastGround(Vector2 screenPos)
    {
        if (_camera is null) return null;

        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 dir = _camera.ProjectRayNormal(screenPos);

        // Intersect with Y=0 ground plane
        if (Mathf.Abs(dir.Y) < 0.001f) return null;
        float t = -from.Y / dir.Y;
        if (t < 0) return null;
        return from + dir * t;
    }
}
