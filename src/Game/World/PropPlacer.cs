using System;
using System.Collections.Generic;
using Godot;
using UnnamedRTS.Core;
using UnnamedRTS.Systems.Pathfinding;

namespace UnnamedRTS.Game.World;

/// <summary>
/// Tracks runtime state for a destructible prop.
/// </summary>
public sealed class DestructibleProp
{
    public Node3D Node { get; set; }
    public string ModelId { get; set; }
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
        TerrainRenderer terrainRenderer, OccupancyGrid occupancyGrid)
    {
        _destructibles.Clear();
        _nextPropId = 0;

        // Place props
        if (mapData.Props != null)
        {
            for (int i = 0; i < mapData.Props.Length; i++)
            {
                PlaceProp(mapData.Props[i], manifest, terrainRenderer, occupancyGrid);
            }
        }

        // Place structures
        if (mapData.Structures != null)
        {
            for (int i = 0; i < mapData.Structures.Length; i++)
            {
                PlaceStructure(mapData.Structures[i], manifest, terrainRenderer, occupancyGrid);
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
        if (!_destructibles.TryGetValue(propId, out DestructibleProp prop))
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
    public DestructibleProp GetDestructible(int propId)
    {
        return _destructibles.TryGetValue(propId, out DestructibleProp prop) ? prop : null;
    }

    // ── Placement Logic ────────────────────────────────────────────────────

    private void PlaceProp(PropPlacement prop, TerrainManifest manifest,
        TerrainRenderer terrainRenderer, OccupancyGrid occupancyGrid)
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

        float rotation = prop.Rotation.ToFloat();
        float scale = prop.Scale.ToFloat();
        float modelScale = entry.ModelScale.ToFloat();
        float finalScale = scale * modelScale;

        Node3D instance = LoadModel(entry.ModelPath);
        if (instance == null)
        {
            // Create placeholder cube
            instance = CreatePlaceholder(finalScale);
        }

        instance.Position = new Vector3(worldX, worldY, worldZ);
        instance.Rotation = new Vector3(0, rotation, 0);
        instance.Scale = new Vector3(finalScale, finalScale, finalScale);
        AddChild(instance);

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
        TerrainRenderer terrainRenderer, OccupancyGrid occupancyGrid)
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

        float rotation = structure.Rotation.ToFloat();
        float scale = structure.Scale.ToFloat();
        float modelScale = entry.ModelScale.ToFloat();
        float finalScale = scale * modelScale;

        Node3D instance = LoadModel(entry.ModelPath);
        if (instance == null)
        {
            instance = CreatePlaceholder(finalScale);
        }

        instance.Position = new Vector3(worldX, worldY, worldZ);
        instance.Rotation = new Vector3(0, rotation, 0);
        instance.Scale = new Vector3(finalScale, finalScale, finalScale);
        AddChild(instance);

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

    private static Node3D LoadModel(string modelPath)
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

    private static Node3D CreatePlaceholder(float scale)
    {
        var mesh = new BoxMesh();
        mesh.Size = new Vector3(1, 1, 1);
        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = mesh;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.8f, 0.2f, 0.8f, 0.7f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
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
}
