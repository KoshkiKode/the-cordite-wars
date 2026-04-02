using Godot;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Economy;

namespace UnnamedRTS.Game.Buildings;

/// <summary>
/// Runtime building node. Handles construction progress (model scales up),
/// health, and registration on complete/destroy. Simulation code: FixedPoint only.
/// </summary>
public partial class BuildingInstance : Node3D
{
    // ── Identity ─────────────────────────────────────────────────────

    public int BuildingId { get; private set; }
    public string BuildingTypeId { get; private set; } = string.Empty;
    public int PlayerId { get; private set; }
    public int GridX { get; private set; }
    public int GridY { get; private set; }
    public BuildingData? Data { get; private set; }

    // ── Sim State (FixedPoint) ───────────────────────────────────────

    public FixedPoint Health { get; private set; }
    public FixedPoint MaxHealth { get; private set; }
    public bool IsConstructed { get; private set; }
    public FixedPoint ConstructionProgress { get; private set; }
    public FixedPoint BuildTime { get; private set; }

    // ── Rally Point ──────────────────────────────────────────────────

    public FixedVector2 RallyPoint { get; set; }

    // ── Visuals ──────────────────────────────────────────────────────

    private MeshInstance3D? _meshInstance;
    private float _targetScaleY;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int buildingId,
        string buildingTypeId,
        BuildingData data,
        int playerId,
        int gridX,
        int gridY)
    {
        BuildingId = buildingId;
        BuildingTypeId = buildingTypeId;
        Data = data;
        PlayerId = playerId;
        GridX = gridX;
        GridY = gridY;

        MaxHealth = data.MaxHealth;
        // Start with 10% health during construction
        Health = data.MaxHealth * FixedPoint.FromInt(1) / FixedPoint.FromInt(10);
        BuildTime = data.BuildTime;
        ConstructionProgress = FixedPoint.Zero;
        IsConstructed = false;

        // Default rally point: offset from building center
        RallyPoint = new FixedVector2(
            FixedPoint.FromInt(gridX + data.FootprintWidth + 2),
            FixedPoint.FromInt(gridY));

        Name = $"Building_{buildingId}_{buildingTypeId}";
        GlobalPosition = new Vector3(gridX, 0f, gridY);

        CreateVisuals(data);
    }

    // ── Visuals ──────────────────────────────────────────────────────

    private void CreateVisuals(BuildingData data)
    {
        _meshInstance = new MeshInstance3D();
        var boxMesh = new BoxMesh();
        boxMesh.Size = new Vector3(
            data.FootprintWidth,
            3f,
            data.FootprintHeight);
        _meshInstance.Mesh = boxMesh;
        _meshInstance.Position = new Vector3(0, 1.5f, 0);

        // Team color material
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.4f, 0.4f, 0.5f);
        _meshInstance.MaterialOverride = mat;

        AddChild(_meshInstance);

        // Start with Y scale at 0 — will grow during construction
        _targetScaleY = 1f;
        _meshInstance.Scale = new Vector3(1f, 0.05f, 1f);
    }

    // ── Simulation Tick ──────────────────────────────────────────────

    /// <summary>
    /// Called each simulation tick. Advances construction if not yet built.
    /// Uses FixedPoint for determinism.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        if (IsConstructed) return;
        if (BuildTime <= FixedPoint.Zero) return;

        ConstructionProgress = ConstructionProgress + deltaTime;

        // Health scales linearly during construction
        FixedPoint fraction = ConstructionProgress / BuildTime;
        if (fraction > FixedPoint.One)
            fraction = FixedPoint.One;

        FixedPoint minHealth = MaxHealth / FixedPoint.FromInt(10);
        Health = minHealth + (MaxHealth - minHealth) * fraction;

        if (ConstructionProgress >= BuildTime)
        {
            CompleteConstruction();
        }
    }

    /// <summary>
    /// Sync visual state from simulation. Called from rendering loop.
    /// </summary>
    public void SyncVisuals()
    {
        if (_meshInstance is null) return;

        if (!IsConstructed)
        {
            // Scale Y from 0 to 1 based on construction progress
            float progress = BuildTime > FixedPoint.Zero
                ? Mathf.Clamp(ConstructionProgress.ToFloat() / BuildTime.ToFloat(), 0.05f, 1f)
                : 1f;
            _meshInstance.Scale = new Vector3(1f, progress, 1f);
        }
        else
        {
            _meshInstance.Scale = Vector3.One;
        }
    }

    // ── Construction Complete ─────────────────────────────────────────

    private void CompleteConstruction()
    {
        IsConstructed = true;
        Health = MaxHealth;
        ConstructionProgress = BuildTime;

        EventBus.Instance?.EmitBuildingCompleted(this);

        GD.Print($"[BuildingInstance] {BuildingTypeId} (id={BuildingId}) construction complete.");
    }

    // ── Damage & Destruction ─────────────────────────────────────────

    public void TakeDamage(FixedPoint damage)
    {
        Health = Health - damage;
        if (Health <= FixedPoint.Zero)
        {
            Health = FixedPoint.Zero;
            Destroy();
        }
    }

    private void Destroy()
    {
        GD.Print($"[BuildingInstance] {BuildingTypeId} (id={BuildingId}) destroyed.");
        EventBus.Instance?.EmitBuildingDestroyed(this);

        // Visual death: tween to zero and free
        if (_meshInstance is not null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_meshInstance, "scale", Vector3.Zero, 0.5f);
            tween.TweenCallback(Callable.From(QueueFree));
        }
        else
        {
            QueueFree();
        }
    }

    // ── Queries ──────────────────────────────────────────────────────

    public float HealthPercent
    {
        get
        {
            if (MaxHealth <= FixedPoint.Zero) return 0f;
            return Health.ToFloat() / MaxHealth.ToFloat();
        }
    }

    public float ConstructionPercent
    {
        get
        {
            if (BuildTime <= FixedPoint.Zero) return 1f;
            return Mathf.Clamp(ConstructionProgress.ToFloat() / BuildTime.ToFloat(), 0f, 1f);
        }
    }
}
