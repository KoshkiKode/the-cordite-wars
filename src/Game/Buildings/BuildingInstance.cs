using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;
using CorditeWars.Game.World;

namespace CorditeWars.Game.Buildings;

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
    private Node3D?          _modelRoot;
    private float _targetScaleY;

    // Neutral cohesive-shader base colors applied to all loaded building models.
    // Using a neutral mid-grey keeps the faction cel-shading readable without
    // clashing with the building's own albedo tones.
    private static readonly Color BuildingBaseColor    = new Color(0.55f, 0.55f, 0.60f);
    private static readonly Color BuildingFactionColor = new Color(0.40f, 0.40f, 0.45f);

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int buildingId,
        string buildingTypeId,
        BuildingData data,
        int playerId,
        int gridX,
        int gridY,
        BuildingModelEntry? modelEntry = null)
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

        CreateVisuals(data, modelEntry);
    }

    /// <summary>
    /// Restores building state from a save file. Called after Initialize().
    /// </summary>
    public void RestoreState(FixedPoint health, bool isConstructed, FixedPoint constructionProgress)
    {
        Health = health;
        IsConstructed = isConstructed;
        ConstructionProgress = constructionProgress;
    }

    // ── Visuals ──────────────────────────────────────────────────────

    private void CreateVisuals(BuildingData data, BuildingModelEntry? modelEntry)
    {
        // Outer wrapper — this node is what we scale during construction
        _modelRoot = new Node3D();
        _modelRoot.Name = "ModelRoot";
        AddChild(_modelRoot);

        bool loadedModel = false;

        if (modelEntry is not null && !string.IsNullOrEmpty(modelEntry.ModelPath))
        {
            string fullPath = "res://" + modelEntry.ModelPath;
            var packed = GD.Load<PackedScene>(fullPath);
            if (packed is not null)
            {
                var instance = packed.Instantiate<Node3D>();
                _modelRoot.AddChild(instance);

                float scale = modelEntry.ModelScale > FixedPoint.Zero
                    ? modelEntry.ModelScale.ToFloat()
                    : 1.0f;
                instance.Scale = new Vector3(scale, scale, scale);

                float rotDeg = modelEntry.ModelRotation.ToFloat();
                if (rotDeg != 0f)
                    instance.RotateY(Mathf.DegToRad(rotDeg));

                // Apply the cohesive cel-shader using a neutral grey base so the
                // faction colour (supplied later if needed) comes through cleanly.
                CohesiveMaterial.ApplyToScene(instance, BuildingBaseColor, BuildingFactionColor);

                loadedModel = true;
            }
            else
            {
                GD.PushWarning(
                    $"[BuildingInstance] Could not load model '{fullPath}' for '{BuildingTypeId}'. " +
                    "Falling back to box mesh.");
            }
        }

        if (!loadedModel)
        {
            // Fallback: coloured box whose dimensions match the footprint
            _meshInstance = new MeshInstance3D();
            var boxMesh = new BoxMesh();
            boxMesh.Size = new Vector3(
                data.FootprintWidth,
                3f,
                data.FootprintHeight);
            _meshInstance.Mesh = boxMesh;
            _meshInstance.Position = new Vector3(0f, 1.5f, 0f);

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.4f, 0.4f, 0.5f);
            _meshInstance.MaterialOverride = mat;

            _modelRoot.AddChild(_meshInstance);
        }

        // Construction animation: start collapsed, grow to full height
        _targetScaleY = 1f;
        _modelRoot.Scale = new Vector3(1f, 0.05f, 1f);
    }

    // ── Per-frame visual update ───────────────────────────────────────

    public override void _Process(double delta)
    {
        SyncVisuals();
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
        if (_modelRoot is null) return;

        if (!IsConstructed)
        {
            // Scale Y from near-zero to 1 based on construction progress
            float progress = BuildTime > FixedPoint.Zero
                ? Mathf.Clamp(ConstructionProgress.ToFloat() / BuildTime.ToFloat(), 0.05f, 1f)
                : 1f;
            _modelRoot.Scale = new Vector3(1f, progress, 1f);
        }
        else
        {
            _modelRoot.Scale = Vector3.One;
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
        if (_modelRoot is not null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_modelRoot, "scale", Vector3.Zero, 0.5f);
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
