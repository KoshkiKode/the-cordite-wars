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
    private TerrainGrid? _terrainGrid;
    private TerrainRenderer? _terrainRenderer;

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

    // Track placed buildings.
    // IDs start at 100_001 to avoid collisions with mobile unit IDs (which
    // start at 1 in UnitSpawner).  Pre-placed HQ buildings use negative IDs;
    // they are created by GameSession and registered here via RegisterExternalBuilding()
    // so they appear in GetAllBuildings() queries (minimap, objectives, simulation).
    private readonly SortedList<int, BuildingInstance> _buildings = new();
    private int _nextBuildingId = 100_001;

    // HQ positions per player for build radius check
    private readonly SortedList<int, List<FixedVector2>> _hqPositions = new();

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int localPlayerId,
        OccupancyGrid occupancyGrid,
        EconomyManager economyManager,
        BuildingRegistry buildingRegistry,
        BuildingManifest buildingManifest,
        Camera3D camera,
        TerrainGrid? terrainGrid = null,
        TerrainRenderer? terrainRenderer = null)
    {
        _localPlayerId = localPlayerId;
        _occupancyGrid = occupancyGrid;
        _economyManager = economyManager;
        _buildingRegistry = buildingRegistry;
        _buildingManifest = buildingManifest;
        _camera = camera;
        _terrainGrid = terrainGrid;
        _terrainRenderer = terrainRenderer;
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
    /// Places a building for an AI player without going through the cursor/ghost system.
    /// Deducts the resource cost, creates the BuildingInstance, adds it to the scene,
    /// and reserves its footprint in the occupancy grid.
    /// Returns <c>true</c> if the building was placed; <c>false</c> if the position is
    /// occupied, the player cannot afford it, or the building type is unknown.
    /// </summary>
    public bool PlaceBuildingForAI(string buildingTypeId, int playerId, int gridX, int gridY)
    {
        if (_buildingRegistry is null || _economyManager is null) return false;
        if (!_buildingRegistry.HasBuilding(buildingTypeId)) return false;

        BuildingData data = _buildingRegistry.GetBuilding(buildingTypeId);

        // Reject if the footprint is already occupied
        if (_occupancyGrid is not null &&
            !_occupancyGrid.IsFootprintFree(gridX, gridY, data.FootprintWidth, data.FootprintHeight))
            return false;

        // Deduct cost — rejects if not affordable
        if (!_economyManager.TryBuildBuilding(playerId, data))
            return false;

        int buildingId = _nextBuildingId++;

        BuildingModelEntry? modelEntry = _buildingManifest?.HasEntry(buildingTypeId) == true
            ? _buildingManifest.GetEntry(buildingTypeId)
            : null;

        var instance = new BuildingInstance();
        instance.Initialize(buildingId, buildingTypeId, data, playerId, gridX, gridY, modelEntry);

        if (_terrainRenderer is not null)
        {
            float terrainY = _terrainRenderer.GetElevationAtWorld(gridX, gridY);
            instance.Position = new Vector3(gridX, terrainY, gridY);
        }

        AddChild(instance);
        _buildings.Add(buildingId, instance);

        _occupancyGrid?.OccupyFootprint(
            gridX, gridY,
            data.FootprintWidth, data.FootprintHeight,
            OccupancyType.Building, buildingId, playerId);

        EventBus.Instance?.EmitBuildingPlaced(instance);

        GD.Print($"[BuildingPlacer] AI placed {buildingTypeId} (id={buildingId}) at ({gridX}, {gridY}) for player {playerId}.");
        return true;
    }

    /// <summary>
    /// Registers a building that was created and placed externally (e.g. a pre-placed
    /// HQ spawned by <see cref="CorditeWars.Game.GameSession"/> before the BuildingPlacer
    /// was initialised) so it appears in <see cref="GetAllBuildings"/> queries used by
    /// the minimap, mission-objective context, and simulation tick.
    /// <para>
    /// The building node must already be in the scene tree. This method does NOT
    /// add it to the scene, modify the occupancy grid, or deduct resources.
    /// </para>
    /// </summary>
    public void RegisterExternalBuilding(BuildingInstance building)
    {
        if (building == null || !GodotObject.IsInstanceValid(building)) return;
        if (_buildings.ContainsKey(building.BuildingId)) return;
        _buildings.Add(building.BuildingId, building);
    }

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

        // Snap to terrain surface
        if (_terrainRenderer is not null)
        {
            float terrainY = _terrainRenderer.GetElevationAtWorld(gridX, gridY);
            instance.Position = new Vector3(gridX, terrainY, gridY);
        }

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

        float terrainY = _terrainRenderer?.GetElevationAtWorld(snappedX, snappedZ) ?? 0f;
        _ghostPosition = new Vector3(snappedX, terrainY, snappedZ);
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

        // For buildings that require water access (e.g., Shipyard), verify that
        // at least one cell adjacent to the footprint is Water or DeepWater.
        if (_placingData.RequiresWaterAccess && !IsAdjacentToWater(gridX, gridY, fw, fh))
            return false;

        // Check if player can afford it
        PlayerEconomy? economy = _economyManager.GetPlayer(_localPlayerId);
        if (economy is null) return false;
        if (!economy.CanAfford(_placingData.Cost, _placingData.SecondaryCost))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if any cell immediately surrounding the given footprint is
    /// Water or DeepWater terrain.  Used to enforce coastal placement for Shipyards.
    /// Returns true if TerrainGrid is not available (permissive fallback).
    /// </summary>
    private bool IsAdjacentToWater(int gridX, int gridY, int footprintW, int footprintH)
    {
        if (_terrainGrid is null)
            return true; // No terrain data — allow placement (fail-open)

        // Check a 1-cell border around the entire footprint
        int x0 = gridX - 1;
        int y0 = gridY - 1;
        int x1 = gridX + footprintW;
        int y1 = gridY + footprintH;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                // Skip cells inside the footprint itself
                if (x >= gridX && x < gridX + footprintW &&
                    y >= gridY && y < gridY + footprintH)
                    continue;

                if (!_terrainGrid.IsInBounds(x, y)) continue;

                TerrainCell cell = _terrainGrid.GetCellSafe(x, y);
                if (cell.Type == TerrainType.Water || cell.Type == TerrainType.DeepWater)
                    return true;
            }
        }

        return false;
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

        BuildingModelEntry? modelEntry = _buildingManifest?.HasEntry(_placingBuildingId) == true
            ? _buildingManifest.GetEntry(_placingBuildingId)
            : null;

        var instance = new BuildingInstance();
        instance.Initialize(
            buildingId,
            _placingBuildingId,
            _placingData,
            _localPlayerId,
            gridX, gridY,
            modelEntry);

        // Snap to terrain surface so the building sits on the ground mesh
        if (_terrainRenderer is not null)
        {
            float terrainY = _terrainRenderer.GetElevationAtWorld(gridX, gridY);
            instance.Position = new Vector3(gridX, terrainY, gridY);
        }

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
