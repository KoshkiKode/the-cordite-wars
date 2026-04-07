using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.Units;

/// <summary>
/// Visual scene node representing a single unit in the Godot scene tree.
/// Bridges the deterministic simulation (FixedPoint) and rendering (float) worlds.
/// Contains the GLB model, collision shape for click detection, and selection circle.
/// </summary>
public partial class UnitNode3D : Node3D
{
    private const float GroundUnitCollisionHeight = 1.0f;
    private const float VehicleCollisionHeight = 1.5f;
    private const float AirUnitHoverHeight = 5.0f;

    // ── Simulation State (FixedPoint) ────────────────────────────────
    public int UnitId { get; private set; }
    public string UnitTypeId { get; private set; } = string.Empty;
    public string FactionId { get; private set; } = string.Empty;
    public int PlayerId { get; private set; }
    
    public FixedVector2 SimPosition { get; private set; }
    public FixedPoint SimFacing { get; private set; }
    public FixedPoint Health { get; private set; }
    public FixedPoint MaxHealth { get; private set; }
    public bool IsAlive { get; private set; } = true;

    // Fixed combat and movement traits
    public FixedPoint Radius { get; private set; }
    public FixedPoint ArmorValue { get; private set; }
    public ArmorType ArmorClass { get; private set; }
    public UnitCategory Category { get; private set; }
    public FixedPoint SightRange { get; private set; }
    public MovementProfile? MovementProfile { get; private set; }

    // ── Child Nodes ──────────────────────────────────────────────────
    private Node3D? _meshRoot;
    private Area3D? _collisionArea;
    private SelectionCircle? _selectionCircle;
    private bool _isAirUnit;
    private float _collisionRadius;

    /// <summary>
    /// Initializes the unit node with model, collision shape, and selection circle.
    /// Call once after instantiation, before adding to the scene tree.
    /// </summary>
    public void Initialize(
        int unitId,
        string unitTypeId,
        UnitData data,
        AssetEntry asset,
        Color teamColor,
        int playerId)
    {
        UnitId = unitId;
        UnitTypeId = unitTypeId;
        FactionId = data.FactionId;
        PlayerId = playerId;
        Health = data.MaxHealth;
        MaxHealth = data.MaxHealth;
        Radius = asset.CollisionRadius;
        ArmorValue = data.ArmorValue;
        ArmorClass = data.ArmorClass;
        Category = data.Category;
        SightRange = data.SightRange;
        MovementProfile = data.GetMovementProfile();

        _isAirUnit = asset.Domain == "Air";
        _collisionRadius = asset.CollisionRadius.ToFloat();

        Name = $"Unit_{unitId}_{unitTypeId}";

        // ── MeshRoot: load and configure the GLB model ──────────────
        _meshRoot = new Node3D();
        _meshRoot.Name = "MeshRoot";
        AddChild(_meshRoot);

        LoadModel(asset, teamColor);

        // ── CollisionArea: for selection/click detection ─────────────
        _collisionArea = new Area3D();
        _collisionArea.Name = "CollisionArea";
        _collisionArea.CollisionLayer = 2; // Unit selection layer
        _collisionArea.CollisionMask = 0;
        AddChild(_collisionArea);

        var collisionShape = new CollisionShape3D();
        collisionShape.Name = "CollisionShape";

        if (_isAirUnit)
        {
            var sphere = new SphereShape3D();
            sphere.Radius = _collisionRadius;
            collisionShape.Shape = sphere;
        }
        else
        {
            var cylinder = new CylinderShape3D();
            cylinder.Radius = _collisionRadius;
            cylinder.Height = IsVehicleCategory(data.Category)
                ? VehicleCollisionHeight
                : GroundUnitCollisionHeight;
            collisionShape.Shape = cylinder;
        }

        _collisionArea.AddChild(collisionShape);

        // ── SelectionCircle: visual ring matching collision radius ───
        _selectionCircle = new SelectionCircle();
        _selectionCircle.Name = "SelectionCircle";
        AddChild(_selectionCircle);
        _selectionCircle.Initialize(_collisionRadius);
    }

    /// <summary>
    /// Updates the Godot transform from simulation state.
    /// Sim X maps to Godot X, Sim Y maps to Godot Z. Godot Y is up.
    /// </summary>
    public void SyncFromSimulation(FixedVector2 simPos, FixedPoint simFacing, FixedPoint health)
    {
        SimPosition = simPos;
        SimFacing = simFacing;
        Health = health;

        float height = _isAirUnit ? AirUnitHoverHeight : 0.0f;
        Position = new Vector3(simPos.X.ToFloat(), height, simPos.Y.ToFloat());

        // Facing: simulation uses radians, Godot Rotation.Y is around the up axis
        Rotation = new Vector3(0.0f, simFacing.ToFloat(), 0.0f);
    }

    /// <summary>
    /// Show or hide the selection circle.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_selectionCircle is not null)
        {
            _selectionCircle.SetSelected(selected);
        }
    }

    /// <summary>
    /// Marks the unit as dead. Removes from scene after a short delay.
    /// </summary>
    public void Die()
    {
        IsAlive = false;

        // Hide selection circle immediately
        if (_selectionCircle is not null)
        {
            _selectionCircle.SetSelected(false);
        }

        // Scale down and remove after delay
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", Vector3.Zero, 0.5f);
        tween.TweenCallback(Callable.From(() => QueueFree()));
    }

    private void LoadModel(AssetEntry asset, Color teamColor)
    {
        if (_meshRoot is null)
            return;

        string modelPath = "res://" + asset.ModelPath;
        var packedScene = GD.Load<PackedScene>(modelPath);
        if (packedScene is null)
        {
            GD.PushWarning($"[UnitNode3D] Could not load model at '{modelPath}' for unit '{UnitTypeId}'.");
            CreateFallbackMesh(teamColor);
            return;
        }

        var instance = packedScene.Instantiate<Node3D>();
        _meshRoot.AddChild(instance);

        // Apply model scale from asset manifest
        float scale = asset.ModelScale.ToFloat();
        instance.Scale = new Vector3(scale, scale, scale);

        // Apply model rotation (degrees to radians around Y axis)
        float rotDeg = asset.ModelRotation.ToFloat();
        if (rotDeg != 0.0f)
        {
            instance.RotateY(Mathf.DegToRad(rotDeg));
        }

        // Apply cohesive shader material with team color
        CohesiveMaterial.ApplyToScene(instance, teamColor);
    }

    private void CreateFallbackMesh(Color teamColor)
    {
        if (_meshRoot is null)
            return;

        // Simple colored box as fallback when model can't load
        var meshInstance = new MeshInstance3D();
        var box = new BoxMesh();
        box.Size = new Vector3(_collisionRadius * 2, 1.0f, _collisionRadius * 2);
        meshInstance.Mesh = box;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = teamColor;
        meshInstance.MaterialOverride = mat;

        meshInstance.Position = new Vector3(0.0f, 0.5f, 0.0f);
        _meshRoot.AddChild(meshInstance);
    }

    private static bool IsVehicleCategory(UnitCategory category)
    {
        return category == UnitCategory.LightVehicle
            || category == UnitCategory.HeavyVehicle
            || category == UnitCategory.Tank
            || category == UnitCategory.APC
            || category == UnitCategory.Artillery;
    }
}
