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

    /// <summary>Current combat stance. Synced from SimUnit each tick.</summary>
    public CorditeWars.Systems.Pathfinding.UnitStance Stance { get; private set; }
    /// <summary>Accumulated experience points. Synced from SimUnit each tick.</summary>
    public int XP { get; private set; }
    /// <summary>Veterancy level. Synced from SimUnit each tick.</summary>
    public CorditeWars.Systems.Pathfinding.VeterancyLevel Veterancy { get; private set; }

    // Fixed combat and movement traits
    public FixedPoint Radius { get; private set; }
    public FixedPoint ArmorValue { get; private set; }
    public ArmorType ArmorClass { get; private set; }
    public UnitCategory Category { get; private set; }
    public FixedPoint SightRange { get; private set; }
    public MovementProfile? MovementProfile { get; private set; }

    /// <summary>True if this unit type has inherent stealth capability.</summary>
    public bool IsStealthUnit { get; private set; }

    /// <summary>True if this unit can detect stealthed enemies within its sight range.</summary>
    public bool IsDetector { get; private set; }

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
        Color factionBaseColor,
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
        IsStealthUnit = data.IsStealthed;
        IsDetector = data.IsDetector;

        _isAirUnit = asset.Domain == "Air";
        _collisionRadius = asset.CollisionRadius.ToFloat();
        float collisionHeight = asset.CollisionHeight.ToFloat();

        Name = $"Unit_{unitId}_{unitTypeId}";

        // ── MeshRoot: load and configure the GLB model ──────────────
        _meshRoot = new Node3D();
        _meshRoot.Name = "MeshRoot";
        AddChild(_meshRoot);

        LoadModel(asset, teamColor, factionBaseColor);

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
            cylinder.Height = collisionHeight;
            collisionShape.Shape = cylinder;
        }

        _collisionArea.AddChild(collisionShape);

        // ── SelectionCircle: visual ring in faction primary color ─────
        _selectionCircle = new SelectionCircle();
        _selectionCircle.Name = "SelectionCircle";
        AddChild(_selectionCircle);
        _selectionCircle.Initialize(_collisionRadius, teamColor);
    }

    /// <summary>
    /// Updates the Godot transform from simulation state.
    /// Sim X maps to Godot X, Sim Y maps to Godot Z. Godot Y is up.
    /// </summary>
    public void SyncFromSimulation(FixedVector2 simPos, FixedPoint simFacing, FixedPoint health,
        CorditeWars.Systems.Pathfinding.UnitStance stance = CorditeWars.Systems.Pathfinding.UnitStance.Aggressive,
        int xp = 0,
        CorditeWars.Systems.Pathfinding.VeterancyLevel veterancy = CorditeWars.Systems.Pathfinding.VeterancyLevel.Recruit,
        float terrainY = 0f)
    {
        SimPosition = simPos;
        SimFacing = simFacing;
        Health = health;
        Stance = stance;
        XP = xp;
        Veterancy = veterancy;

        float height = _isAirUnit ? AirUnitHoverHeight : terrainY;
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

    /// <summary>
    /// Updates the visual appearance of this unit to reflect its stealth state.
    /// <list type="bullet">
    ///   <item>
    ///     <b>Own unit stealthed</b> — semi-transparent (40 % opacity) so the
    ///     owner can still see and control the unit while it is hidden from enemies.
    ///   </item>
    ///   <item>
    ///     <b>Enemy unit stealthed</b> — completely hidden (<c>Visible = false</c>).
    ///     The unit reappears when detected or when it fires.
    ///   </item>
    ///   <item>
    ///     <b>Not stealthed</b> — fully opaque and visible.
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="stealthed">Whether the unit is currently in stealth.</param>
    /// <param name="isOwnUnit">True if this unit belongs to the local player.</param>
    public void SetStealthed(bool stealthed, bool isOwnUnit)
    {
        if (!stealthed)
        {
            Visible = true;
            SetMeshAlpha(1.0f);
            return;
        }

        if (isOwnUnit)
        {
            // Owner can see their own stealthed unit as a ghost
            Visible = true;
            SetMeshAlpha(0.4f);
        }
        else
        {
            // Hide enemy stealthed units entirely
            Visible = false;
        }
    }

    /// <summary>
    /// Sets the alpha (opacity) on all surface override materials in the mesh
    /// hierarchy. Used to render own stealthed units as a ghost.
    /// </summary>
    private void SetMeshAlpha(float alpha)
    {
        if (_meshRoot is null) return;
        ApplyAlphaToNode(_meshRoot, alpha);
    }

    private static void ApplyAlphaToNode(Node node, float alpha)
    {
        if (node is MeshInstance3D mi)
        {
            int surfaceCount = mi.Mesh?.GetSurfaceCount() ?? 0;
            for (int s = 0; s < surfaceCount; s++)
            {
                Material? mat = mi.GetSurfaceOverrideMaterial(s);
                if (mat is ShaderMaterial shaderMat)
                {
                    // The cohesive_flat shader reads ALPHA from base_color.a
                    Variant existing = shaderMat.GetShaderParameter("base_color");
                    if (existing.VariantType == Variant.Type.Color)
                    {
                        Color c = existing.AsColor();
                        shaderMat.SetShaderParameter("base_color", new Color(c.R, c.G, c.B, alpha));
                    }
                }
                else if (mat is BaseMaterial3D baseMat)
                {
                    baseMat.AlbedoColor = new Color(
                        baseMat.AlbedoColor.R,
                        baseMat.AlbedoColor.G,
                        baseMat.AlbedoColor.B,
                        alpha);
                    baseMat.Transparency = alpha < 1.0f
                        ? BaseMaterial3D.TransparencyEnum.Alpha
                        : BaseMaterial3D.TransparencyEnum.Disabled;
                }
            }
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
            ApplyAlphaToNode(node.GetChild(i), alpha);
    }

    private void LoadModel(AssetEntry asset, Color teamColor, Color factionBaseColor)
    {
        if (_meshRoot is null)
            return;

        string modelPath = "res://" + asset.ModelPath;
        var packedScene = GD.Load<PackedScene>(modelPath);
        if (packedScene is null)
        {
            GD.PushWarning($"[UnitNode3D] Could not load model at '{modelPath}' for unit '{UnitTypeId}'.");
            CreateFallbackMesh(teamColor, factionBaseColor);
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

        // Use faction primary color as rim/glow color so units read as distinctly faction-colored
        // from the typical top-down RTS camera angle.
        // team_color_strength is higher for small units (infantry) which are harder to read.
        float teamColorStrength = GetTeamColorStrength(Category);
        CohesiveMaterial.ApplyToScene(instance, teamColor, factionBaseColor, teamColor, teamColorStrength);
    }

    /// <summary>
    /// Returns the team-color tint strength for a given unit category.
    /// Small units (infantry) get the highest value so they remain clearly
    /// faction-colored at typical RTS top-down viewing distances.
    /// </summary>
    private static float GetTeamColorStrength(UnitCategory category) => category switch
    {
        UnitCategory.Infantry   => 0.50f,   // small; needs strong tint to read at distance
        UnitCategory.Support    => 0.42f,   // support/utility infantry-scale
        UnitCategory.Special    => 0.42f,
        UnitCategory.Defense    => 0.38f,   // static emplacements — medium prominence
        UnitCategory.LightVehicle => 0.35f,
        UnitCategory.APC        => 0.32f,
        UnitCategory.Helicopter => 0.32f,
        UnitCategory.Jet        => 0.30f,
        _ => 0.28f  // HeavyVehicle, Tank, Artillery — large, already readable
    };

    private void CreateFallbackMesh(Color teamColor, Color factionBaseColor)
    {
        if (_meshRoot is null)
            return;

        // Colored box blending team and faction base colors as fallback
        var meshInstance = new MeshInstance3D();
        var box = new BoxMesh();
        box.Size = new Vector3(_collisionRadius * 2, 1.0f, _collisionRadius * 2);
        meshInstance.Mesh = box;

        // Blend faction base color with team color using the shared BlendWithFaction helper
        Color blended = CohesiveMaterial.BlendWithFaction(teamColor, factionBaseColor);
        blended.A = 1.0f;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = blended;
        meshInstance.MaterialOverride = mat;

        meshInstance.Position = new Vector3(0.0f, 0.5f, 0.0f);
        _meshRoot.AddChild(meshInstance);
    }
}
