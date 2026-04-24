using System;
using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Systems.Graphics;
using CorditeWars.Systems.Pathfinding;

namespace CorditeWars.Game.World;

/// <summary>
/// Tracks runtime state for a destructible prop.
/// </summary>
public sealed class DestructibleProp
{
    public Node3D Node { get; set; } = null!;
    public string ModelId { get; set; } = string.Empty;
    public int MaxHealth { get; set; }
    public int CurrentHealth { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
}

/// <summary>
/// Instantiates all map props and structures from MapData.
/// Loads models from TerrainManifest, places at position/rotation/scale,
/// creates CollisionShape3D for non-passable props, registers in OccupancyGrid,
/// tracks destructible prop health.
/// </summary>
public partial class PropPlacer : Node3D
{
    private readonly SortedList<int, DestructibleProp> _destructibles = new();
    private int _nextPropId;

    /// <summary>
    /// Places all props and structures from the map data.
    /// </summary>
    public void PlaceAll(MapData mapData, TerrainManifest manifest,
        TerrainRenderer terrainRenderer, OccupancyGrid? occupancyGrid,
        QualityTier tier = QualityTier.Medium)
    {
        // Remove any previously placed prop/structure nodes so that repeated calls
        // (e.g. map reload or editor regeneration) do not accumulate stale geometry.
        foreach (Node child in GetChildren())
            child.QueueFree();

        _destructibles.Clear();
        _nextPropId = 0;

        bool useShadowBlobs = tier >= QualityTier.Medium;

        // Place props
        if (mapData.Props != null)
        {
            for (int i = 0; i < mapData.Props.Length; i++)
            {
                PlaceProp(mapData.Props[i], manifest, terrainRenderer, occupancyGrid, useShadowBlobs);
            }
        }

        // Place structures
        if (mapData.Structures != null)
        {
            for (int i = 0; i < mapData.Structures.Length; i++)
            {
                PlaceStructure(mapData.Structures[i], manifest, terrainRenderer, occupancyGrid, useShadowBlobs);
            }
        }

        GD.Print($"[PropPlacer] Placed {_nextPropId} props/structures, " +
                 $"{_destructibles.Count} destructible.");
    }

    /// <summary>
    /// Applies damage to a destructible prop. Returns true if destroyed.
    /// </summary>
    public bool DamageProp(int propId, int damage, OccupancyGrid occupancyGrid)
    {
        if (!_destructibles.TryGetValue(propId, out DestructibleProp? prop))
            return false;

        prop.CurrentHealth -= damage;
        if (prop.CurrentHealth <= 0)
        {
            DestroyProp(propId, prop, occupancyGrid);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns destructible prop info, or null if not found.
    /// </summary>
    public DestructibleProp? GetDestructible(int propId)
    {
        return _destructibles.TryGetValue(propId, out DestructibleProp? prop) ? prop : null;
    }

    // ── Placement Logic ────────────────────────────────────────────────────

    private void PlaceProp(PropPlacement prop, TerrainManifest manifest,
        TerrainRenderer terrainRenderer, OccupancyGrid? occupancyGrid, bool useShadowBlobs)
    {
        TerrainModelEntry entry;
        try
        {
            entry = manifest.FindEntry(prop.ModelId);
        }
        catch (KeyNotFoundException)
        {
            GD.PushWarning($"[PropPlacer] Model '{prop.ModelId}' not found in manifest, skipping.");
            return;
        }

        float worldX = prop.X.ToFloat();
        float worldZ = prop.Y.ToFloat();
        float worldY = terrainRenderer != null
            ? terrainRenderer.GetElevationAtWorld(worldX, worldZ)
            : 0f;

        float rotation = DegreesToRadians(prop.Rotation.ToFloat());
        float scale = prop.Scale.ToFloat();
        float modelScale = entry.ModelScale.ToFloat();
        float finalScale = scale * modelScale;

        Node3D? instance = LoadModel(entry.ModelPath);
        if (instance == null)
        {
            // Try procedural model definition before falling back to the generic placeholder
            instance = ProceduralModelLoader.TryLoad(prop.ModelId)
                       ?? CreatePlaceholder(prop.ModelId, finalScale);
        }

        instance.Position = new Vector3(worldX, worldY, worldZ);
        instance.Rotation = new Vector3(0, rotation, 0);
        instance.Scale = new Vector3(finalScale, finalScale, finalScale);
        AddChild(instance);

        // Spawn shadow blob on Medium/High quality (skipped on Potato/Low to save draw calls)
        if (useShadowBlobs)
        {
            float shadowRadius = Math.Max(0.3f, entry.CollisionRadius.ToFloat() * finalScale * 1.3f);
            SpawnShadowBlob(worldX, worldY, worldZ, shadowRadius);
        }

        int propId = _nextPropId++;

        // Create collision for non-passable props
        if (!entry.Passable)
        {
            AddCollision(instance, entry.CollisionRadius.ToFloat() * finalScale);

            // Register in occupancy grid
            if (occupancyGrid != null)
            {
                int gx = (int)worldX;
                int gy = (int)worldZ;
                occupancyGrid.OccupyCell(gx, gy, OccupancyType.Building, propId, -1);
            }
        }

        // Track destructible props
        if (entry.Destructible && entry.Health > 0)
        {
            _destructibles.Add(propId, new DestructibleProp
            {
                Node = instance,
                ModelId = prop.ModelId,
                MaxHealth = entry.Health,
                CurrentHealth = entry.Health,
                GridX = (int)worldX,
                GridY = (int)worldZ
            });
        }
    }

    private void PlaceStructure(StructurePlacement structure, TerrainManifest manifest,
        TerrainRenderer terrainRenderer, OccupancyGrid? occupancyGrid, bool useShadowBlobs)
    {
        TerrainModelEntry entry;
        try
        {
            entry = manifest.FindEntry(structure.ModelId);
        }
        catch (KeyNotFoundException)
        {
            GD.PushWarning($"[PropPlacer] Structure model '{structure.ModelId}' not found, skipping.");
            return;
        }

        float worldX = structure.X.ToFloat();
        float worldZ = structure.Y.ToFloat();
        float worldY = terrainRenderer != null
            ? terrainRenderer.GetElevationAtWorld(worldX, worldZ)
            : 0f;

        float rotation = DegreesToRadians(structure.Rotation.ToFloat());
        float scale = structure.Scale.ToFloat();
        float modelScale = entry.ModelScale.ToFloat();
        float finalScale = scale * modelScale;

        Node3D? instance = LoadModel(entry.ModelPath);
        if (instance == null)
        {
            // Try procedural model definition before falling back to the generic placeholder
            instance = ProceduralModelLoader.TryLoad(structure.ModelId)
                       ?? CreatePlaceholder(structure.ModelId, finalScale);
        }

        instance.Position = new Vector3(worldX, worldY, worldZ);
        instance.Rotation = new Vector3(0, rotation, 0);
        instance.Scale = new Vector3(finalScale, finalScale, finalScale);
        AddChild(instance);

        // Spawn shadow blob on Medium/High quality only
        if (useShadowBlobs)
        {
            float structShadowRadius = Math.Max(0.4f, entry.CollisionRadius.ToFloat() * finalScale * 1.4f);
            SpawnShadowBlob(worldX, worldY, worldZ, structShadowRadius);
        }

        int propId = _nextPropId++;

        // Structures always have collision
        AddCollision(instance, entry.CollisionRadius.ToFloat() * finalScale);

        if (occupancyGrid != null)
        {
            int gx = (int)worldX;
            int gy = (int)worldZ;
            occupancyGrid.OccupyCell(gx, gy, OccupancyType.Building, propId, -1);
        }

        // Track destructible structures
        if (entry.Destructible && entry.Health > 0)
        {
            _destructibles.Add(propId, new DestructibleProp
            {
                Node = instance,
                ModelId = structure.ModelId,
                MaxHealth = entry.Health,
                CurrentHealth = entry.Health,
                GridX = (int)worldX,
                GridY = (int)worldZ
            });
        }
    }

    // ── Model Loading ──────────────────────────────────────────────────────

    private static Node3D? LoadModel(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath)) return null;

        string resPath = modelPath.StartsWith("res://") ? modelPath : $"res://{modelPath}";

        if (!ResourceLoader.Exists(resPath))
            return null;

        var scene = GD.Load<PackedScene>(resPath);
        if (scene == null) return null;

        var instance = scene.Instantiate<Node3D>();
        return instance;
    }

    private static Node3D CreatePlaceholder(string modelId, float scale)
    {
        Mesh mesh;
        Color color;

        if (modelId.StartsWith("tree_", StringComparison.OrdinalIgnoreCase))
        {
            var cyl = new CylinderMesh();
            cyl.TopRadius    = 0.0f;
            cyl.BottomRadius = 0.3f;
            cyl.Height       = 1.5f;
            mesh  = cyl;
            color = new Color(0.1f, 0.4f, 0.1f);
        }
        else if (modelId.StartsWith("rock_", StringComparison.OrdinalIgnoreCase))
        {
            var sph = new SphereMesh();
            sph.RadialSegments = 8;
            sph.Rings          = 4;
            mesh  = sph;
            color = new Color(0.4f, 0.4f, 0.4f);
        }
        else if (modelId.StartsWith("ruin_", StringComparison.OrdinalIgnoreCase) ||
                 modelId.StartsWith("structure_", StringComparison.OrdinalIgnoreCase) ||
                 modelId.StartsWith("wall_", StringComparison.OrdinalIgnoreCase))
        {
            var box = new BoxMesh();
            box.Size = new Vector3(1f, 1.2f, 1f);
            mesh  = box;
            color = new Color(0.5f, 0.35f, 0.2f);
        }
        else
        {
            var box = new BoxMesh();
            box.Size = new Vector3(0.8f, 0.8f, 0.8f);
            mesh  = box;
            color = new Color(0.8f, 0.2f, 0.8f);
        }

        var mat = new StandardMaterial3D();
        mat.AlbedoColor  = color with { A = 0.75f };
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;

        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh             = mesh;
        meshInstance.MaterialOverride = mat;

        var parent = new Node3D();
        parent.AddChild(meshInstance);
        return parent;
    }

    private static void AddCollision(Node3D instance, float radius)
    {
        if (radius <= 0.01f) radius = 0.5f;

        var body = new StaticBody3D();
        body.CollisionLayer = 2; // prop collision layer
        body.CollisionMask = 0;

        var shape = new SphereShape3D();
        shape.Radius = radius;
        var collisionShape = new CollisionShape3D();
        collisionShape.Shape = shape;
        body.AddChild(collisionShape);

        instance.AddChild(body);
    }

    /// <summary>
    /// Spawns a flat, dark, semi-transparent disc at ground level to simulate
    /// an ambient shadow blob beneath props and structures.
    /// These are inexpensive and give depth to the scene even without real-time shadows.
    /// </summary>
    private void SpawnShadowBlob(float worldX, float worldY, float worldZ, float radius)
    {
        var disc = new CylinderMesh();
        disc.TopRadius    = radius;
        disc.BottomRadius = radius;
        disc.Height       = 0.04f;
        disc.RadialSegments = 16;
        disc.Rings          = 1;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor  = new Color(0.0f, 0.0f, 0.0f, 0.55f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode     = BaseMaterial3D.CullModeEnum.Disabled;

        var mesh = new MeshInstance3D();
        mesh.Mesh             = disc;
        mesh.MaterialOverride = mat;
        mesh.CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off;
        // Render slightly above the terrain surface to prevent z-fighting
        mesh.Position = new Vector3(worldX, worldY + 0.02f, worldZ);

        AddChild(mesh);
    }

    private void DestroyProp(int propId, DestructibleProp prop, OccupancyGrid occupancyGrid)
    {
        // Vacate occupancy
        if (occupancyGrid != null)
        {
            occupancyGrid.VacateCell(prop.GridX, prop.GridY);
        }

        // Remove from scene
        if (prop.Node != null && IsInstanceValid(prop.Node))
        {
            prop.Node.QueueFree();
        }

        _destructibles.Remove(propId);
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180.0f;
}
