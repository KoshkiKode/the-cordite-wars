using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Factions;
using UnnamedRTS.Systems.Pathfinding;

namespace UnnamedRTS.Game.World;

// ═══════════════════════════════════════════════════════════════════════════════
// MAP EDITOR — Full in-engine editor for creating and editing maps
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Available editor tools.
/// </summary>
public enum EditorTool
{
    None = 0,
    TerrainRaise,
    TerrainLower,
    TerrainSmooth,
    TerrainFlatten,
    BiomeBrush,
    RiverTool,
    BridgeTool,
    PropPlacer,
    StructurePlacer,
    CorditeNodePlacer,
    StartingPositionPlacer,
    Eraser
}

/// <summary>
/// Represents a single undoable editor action.
/// </summary>
public sealed class EditorAction
{
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    // Snapshot of affected data before the action
    public byte[] BeforeState { get; init; }
    // Snapshot of affected data after the action
    public byte[] AfterState { get; init; }

    // Action-specific metadata
    public int AffectedX { get; init; }
    public int AffectedY { get; init; }
    public int AffectedRadius { get; init; }
}

/// <summary>
/// Full in-engine map editor. Supports terrain brushes, biome painting,
/// river/bridge tools, prop/structure/cordite/starting position placement,
/// and an eraser. Maintains an undo stack of 50 actions.
/// All edits are stored as standard MapData format.
/// </summary>
public partial class MapEditor : Node3D
{
    // ── Constants ──────────────────────────────────────────────────────────
    private const int MaxUndoActions = 50;
    private const string CustomMapDirectory = "res://data/maps/custom";
    private const string CustomMapDirectoryUser = "user://maps/custom";

    // ── State ──────────────────────────────────────────────────────────────
    private int _mapWidth;
    private int _mapHeight;
    private string _mapBiome = "temperate";
    private string _mapId = "custom_map";
    private string _mapDisplayName = "Custom Map";
    private string _mapDescription = "";
    private string _mapAuthor = "Player";
    private int _maxPlayers = 4;

    // Elevation grid (float for editor use)
    private float[] _elevation;

    // Biome per-cell (stored as string for flexibility)
    private string[] _biomeMap;

    // Placed objects
    private readonly SortedList<int, PropPlacement> _props = new();
    private readonly SortedList<int, StructurePlacement> _structures = new();
    private readonly SortedList<int, CorditeNodeData> _corditeNodes = new();
    private readonly SortedList<int, StartingPosition> _startingPositions = new();
    private readonly List<TerrainFeature> _terrainFeatures = new();
    private int _nextObjectId;

    // Current tool state
    public EditorTool CurrentTool { get; private set; } = EditorTool.None;
    public int BrushSize { get; set; } = 5;
    public float BrushIntensity { get; set; } = 0.5f;
    public string SelectedBiome { get; set; } = "temperate";
    public string SelectedPropId { get; set; } = "";
    public string SelectedPropCategory { get; set; } = "";
    public string SelectedStructureId { get; set; } = "";
    public int SelectedCorditeAmount { get; set; } = 10000;

    // River tool state (collecting click points)
    private readonly List<int[]> _riverPoints = new();

    // Undo/Redo stacks
    private readonly SortedList<int, EditorAction> _undoStack = new();
    private readonly SortedList<int, EditorAction> _redoStack = new();
    private int _undoNextKey;
    private int _redoNextKey;

    // Rendering
    private TerrainRenderer _terrainRenderer;
    private WaterRenderer _waterRenderer;
    private PropPlacer _propPlacer;

    // Signals
    [Signal] public delegate void MapModifiedEventHandler();
    [Signal] public delegate void ToolChangedEventHandler(int tool);
    [Signal] public delegate void UndoStackChangedEventHandler(int undoCount, int redoCount);

    // ── JSON Serialization ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        opts.Converters.Add(new FixedPointJsonConverter());
        opts.Converters.Add(new NullableFixedPointJsonConverter());
        return opts;
    }

    // ── Initialization ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        _terrainRenderer = new TerrainRenderer();
        AddChild(_terrainRenderer);

        _waterRenderer = new WaterRenderer();
        AddChild(_waterRenderer);

        _propPlacer = new PropPlacer();
        AddChild(_propPlacer);
    }

    /// <summary>
    /// Creates a new blank map with given dimensions and biome.
    /// </summary>
    public void NewMap(int width, int height, string biome)
    {
        _mapWidth = Math.Clamp(width, 64, 512);
        _mapHeight = Math.Clamp(height, 64, 512);
        _mapBiome = biome ?? "temperate";
        _mapId = $"custom_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        _mapDisplayName = "New Map";
        _mapDescription = "";
        _mapAuthor = "Player";
        _maxPlayers = 4;

        // Initialize grids
        int totalCells = _mapWidth * _mapHeight;
        _elevation = new float[totalCells];
        _biomeMap = new string[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            _biomeMap[i] = _mapBiome;
        }

        // Clear placed objects
        _props.Clear();
        _structures.Clear();
        _corditeNodes.Clear();
        _startingPositions.Clear();
        _terrainFeatures.Clear();
        _nextObjectId = 0;

        // Clear undo/redo
        ClearUndoRedo();

        // Regenerate visuals
        RegenerateTerrain();

        GD.Print($"[MapEditor] New map created: {_mapWidth}x{_mapHeight}, biome={_mapBiome}");
    }

    // ── Tool Selection ─────────────────────────────────────────────────────

    public void SetTool(EditorTool tool)
    {
        // If switching away from river tool, finalize the river
        if (CurrentTool == EditorTool.RiverTool && tool != EditorTool.RiverTool)
        {
            FinalizeRiver();
        }

        CurrentTool = tool;
        EmitSignal(SignalName.ToolChanged, (int)tool);
    }

    // ── Terrain Brush Operations ───────────────────────────────────────────

    /// <summary>
    /// Applies the current terrain brush at the given world position.
    /// Called during mouse drag.
    /// </summary>
    public void ApplyTerrainBrush(int centerX, int centerY)
    {
        if (_elevation == null) return;

        // Snapshot before
        byte[] before = SnapshotElevation(centerX, centerY, BrushSize);

        switch (CurrentTool)
        {
            case EditorTool.TerrainRaise:
                ApplyElevationChange(centerX, centerY, BrushSize, BrushIntensity);
                break;
            case EditorTool.TerrainLower:
                ApplyElevationChange(centerX, centerY, BrushSize, -BrushIntensity);
                break;
            case EditorTool.TerrainSmooth:
                ApplySmoothBrush(centerX, centerY, BrushSize);
                break;
            case EditorTool.TerrainFlatten:
                ApplyFlattenBrush(centerX, centerY, BrushSize);
                break;
            default:
                return;
        }

        // Snapshot after and push undo
        byte[] after = SnapshotElevation(centerX, centerY, BrushSize);
        PushUndo(new EditorAction
        {
            Type = "terrain",
            Description = $"{CurrentTool} at ({centerX},{centerY})",
            BeforeState = before,
            AfterState = after,
            AffectedX = centerX,
            AffectedY = centerY,
            AffectedRadius = BrushSize
        });

        EmitSignal(SignalName.MapModified);
    }

    private void ApplyElevationChange(int cx, int cy, int radius, float amount)
    {
        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);
        float rSq = radius * radius;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distSq = dx * dx + dy * dy;
                if (distSq > rSq) continue;

                float dist = MathF.Sqrt(distSq);
                float falloff = 1f - (dist / radius);
                falloff = falloff * falloff;

                _elevation[y * _mapWidth + x] += amount * falloff;
            }
        }
    }

    private void ApplySmoothBrush(int cx, int cy, int radius)
    {
        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);
        float rSq = radius * radius;

        // Compute target averages first
        float[] targets = new float[(maxX - minX + 1) * (maxY - minY + 1)];
        int ti = 0;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distSq = dx * dx + dy * dy;

                if (distSq > rSq)
                {
                    targets[ti++] = _elevation[y * _mapWidth + x];
                    continue;
                }

                // Average of neighbors
                float sum = 0;
                int count = 0;
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(_mapHeight - 1, y + 1); ny++)
                {
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(_mapWidth - 1, x + 1); nx++)
                    {
                        sum += _elevation[ny * _mapWidth + nx];
                        count++;
                    }
                }

                float avg = sum / count;
                float dist = MathF.Sqrt(distSq);
                float falloff = 1f - (dist / radius);
                float current = _elevation[y * _mapWidth + x];
                targets[ti++] = current + (avg - current) * falloff * BrushIntensity;
            }
        }

        // Apply
        ti = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                _elevation[y * _mapWidth + x] = targets[ti++];
            }
        }
    }

    private void ApplyFlattenBrush(int cx, int cy, int radius)
    {
        // Flatten to the center height
        float targetHeight = _elevation[cy * _mapWidth + cx];
        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);
        float rSq = radius * radius;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distSq = dx * dx + dy * dy;
                if (distSq > rSq) continue;

                float dist = MathF.Sqrt(distSq);
                float falloff = 1f - (dist / radius);
                float current = _elevation[y * _mapWidth + x];
                _elevation[y * _mapWidth + x] = current + (targetHeight - current) * falloff * BrushIntensity;
            }
        }
    }

    // ── Biome Brush ────────────────────────────────────────────────────────

    public void ApplyBiomeBrush(int centerX, int centerY)
    {
        if (_biomeMap == null) return;

        int radius = BrushSize;
        int minX = Math.Max(0, centerX - radius);
        int maxX = Math.Min(_mapWidth - 1, centerX + radius);
        int minY = Math.Max(0, centerY - radius);
        int maxY = Math.Min(_mapHeight - 1, centerY + radius);
        float rSq = radius * radius;

        byte[] before = SnapshotBiome(centerX, centerY, radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - centerX;
                float dy = y - centerY;
                if (dx * dx + dy * dy <= rSq)
                {
                    _biomeMap[y * _mapWidth + x] = SelectedBiome;
                }
            }
        }

        byte[] after = SnapshotBiome(centerX, centerY, radius);
        PushUndo(new EditorAction
        {
            Type = "biome",
            Description = $"Biome {SelectedBiome} at ({centerX},{centerY})",
            BeforeState = before,
            AfterState = after,
            AffectedX = centerX,
            AffectedY = centerY,
            AffectedRadius = radius
        });

        EmitSignal(SignalName.MapModified);
    }

    // ── River Tool ─────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a point to the current river being drawn.
    /// </summary>
    public void AddRiverPoint(int x, int y)
    {
        _riverPoints.Add(new[] { x, y });
    }

    /// <summary>
    /// Finalizes the current river (converts collected points to a TerrainFeature).
    /// </summary>
    public void FinalizeRiver()
    {
        if (_riverPoints.Count < 2)
        {
            _riverPoints.Clear();
            return;
        }

        var points = new int[_riverPoints.Count][];
        for (int i = 0; i < _riverPoints.Count; i++)
        {
            points[i] = _riverPoints[i];
        }

        var feature = new TerrainFeature
        {
            Type = "river",
            Points = points
        };

        _terrainFeatures.Add(feature);

        PushUndo(new EditorAction
        {
            Type = "river_add",
            Description = $"River with {_riverPoints.Count} points"
        });

        _riverPoints.Clear();
        EmitSignal(SignalName.MapModified);
    }

    /// <summary>
    /// Returns current river points being drawn (for preview).
    /// </summary>
    public List<int[]> GetRiverPreviewPoints()
    {
        return _riverPoints;
    }

    // ── Bridge Tool ────────────────────────────────────────────────────────

    public void PlaceBridge(int x, int y, float rotation)
    {
        var bridge = new StructurePlacement
        {
            ModelId = "bridge",
            X = FixedPoint.FromInt(x),
            Y = FixedPoint.FromInt(y),
            Rotation = FixedPoint.FromFloat(rotation),
            Scale = FixedPoint.One
        };

        int id = _nextObjectId++;
        _structures.Add(id, bridge);

        PushUndo(new EditorAction
        {
            Type = "bridge_add",
            Description = $"Bridge at ({x},{y})",
            AffectedX = x,
            AffectedY = y
        });

        EmitSignal(SignalName.MapModified);
    }

    // ── Prop Placement ─────────────────────────────────────────────────────

    public int PlaceProp(float x, float y, float rotation, float scale)
    {
        if (string.IsNullOrEmpty(SelectedPropId)) return -1;

        var prop = new PropPlacement
        {
            ModelId = SelectedPropId,
            X = FixedPoint.FromFloat(x),
            Y = FixedPoint.FromFloat(y),
            Rotation = FixedPoint.FromFloat(rotation),
            Scale = FixedPoint.FromFloat(scale)
        };

        int id = _nextObjectId++;
        _props.Add(id, prop);

        PushUndo(new EditorAction
        {
            Type = "prop_add",
            Description = $"Prop {SelectedPropId} at ({x:F1},{y:F1})",
            AffectedX = (int)x,
            AffectedY = (int)y
        });

        EmitSignal(SignalName.MapModified);
        return id;
    }

    // ── Structure Placement ────────────────────────────────────────────────

    public int PlaceStructure(float x, float y, float rotation, float scale)
    {
        if (string.IsNullOrEmpty(SelectedStructureId)) return -1;

        var structure = new StructurePlacement
        {
            ModelId = SelectedStructureId,
            X = FixedPoint.FromFloat(x),
            Y = FixedPoint.FromFloat(y),
            Rotation = FixedPoint.FromFloat(rotation),
            Scale = FixedPoint.FromFloat(scale)
        };

        int id = _nextObjectId++;
        _structures.Add(id, structure);

        PushUndo(new EditorAction
        {
            Type = "structure_add",
            Description = $"Structure {SelectedStructureId} at ({x:F1},{y:F1})",
            AffectedX = (int)x,
            AffectedY = (int)y
        });

        EmitSignal(SignalName.MapModified);
        return id;
    }

    // ── Cordite Node Placement ─────────────────────────────────────────────

    public int PlaceCorditeNode(int x, int y, int amount)
    {
        int id = _nextObjectId++;
        var node = new CorditeNodeData
        {
            NodeId = id.ToString(),
            X = x,
            Y = y,
            Amount = amount > 0 ? amount : SelectedCorditeAmount
        };

        _corditeNodes.Add(id, node);

        PushUndo(new EditorAction
        {
            Type = "cordite_add",
            Description = $"Cordite {amount} at ({x},{y})",
            AffectedX = x,
            AffectedY = y
        });

        EmitSignal(SignalName.MapModified);
        return id;
    }

    // ── Starting Position Placement ────────────────────────────────────────

    public int PlaceStartingPosition(int x, int y, float facing)
    {
        int playerId = _startingPositions.Count;
        if (playerId >= 6)
        {
            GD.PushWarning("[MapEditor] Maximum 6 starting positions.");
            return -1;
        }

        int id = _nextObjectId++;
        var pos = new StartingPosition
        {
            PlayerId = playerId,
            X = x,
            Y = y,
            Facing = FixedPoint.FromFloat(facing)
        };

        _startingPositions.Add(id, pos);

        PushUndo(new EditorAction
        {
            Type = "startpos_add",
            Description = $"Start P{playerId} at ({x},{y})",
            AffectedX = x,
            AffectedY = y
        });

        EmitSignal(SignalName.MapModified);
        return id;
    }

    // ── Eraser ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Erases the nearest object within eraser radius at the given position.
    /// </summary>
    public void EraseAt(float x, float y, float radius)
    {
        float rSq = radius * radius;

        // Check props
        for (int i = _props.Count - 1; i >= 0; i--)
        {
            PropPlacement prop = _props.Values[i];
            float dx = prop.X.ToFloat() - x;
            float dy = prop.Y.ToFloat() - y;
            if (dx * dx + dy * dy <= rSq)
            {
                int key = _props.Keys[i];
                _props.RemoveAt(i);
                PushUndo(new EditorAction
                {
                    Type = "prop_erase",
                    Description = $"Erased prop {prop.ModelId}",
                    AffectedX = (int)x,
                    AffectedY = (int)y
                });
                EmitSignal(SignalName.MapModified);
                return;
            }
        }

        // Check structures
        for (int i = _structures.Count - 1; i >= 0; i--)
        {
            StructurePlacement s = _structures.Values[i];
            float dx = s.X.ToFloat() - x;
            float dy = s.Y.ToFloat() - y;
            if (dx * dx + dy * dy <= rSq)
            {
                _structures.RemoveAt(i);
                PushUndo(new EditorAction
                {
                    Type = "structure_erase",
                    Description = $"Erased structure {s.ModelId}",
                    AffectedX = (int)x,
                    AffectedY = (int)y
                });
                EmitSignal(SignalName.MapModified);
                return;
            }
        }

        // Check cordite nodes
        for (int i = _corditeNodes.Count - 1; i >= 0; i--)
        {
            CorditeNodeData cn = _corditeNodes.Values[i];
            float dx = cn.X - x;
            float dy = cn.Y - y;
            if (dx * dx + dy * dy <= rSq)
            {
                _corditeNodes.RemoveAt(i);
                PushUndo(new EditorAction
                {
                    Type = "cordite_erase",
                    Description = $"Erased cordite node",
                    AffectedX = (int)x,
                    AffectedY = (int)y
                });
                EmitSignal(SignalName.MapModified);
                return;
            }
        }

        // Check starting positions
        for (int i = _startingPositions.Count - 1; i >= 0; i--)
        {
            StartingPosition sp = _startingPositions.Values[i];
            float dx = sp.X - x;
            float dy = sp.Y - y;
            if (dx * dx + dy * dy <= rSq)
            {
                _startingPositions.RemoveAt(i);
                PushUndo(new EditorAction
                {
                    Type = "startpos_erase",
                    Description = $"Erased starting position",
                    AffectedX = (int)x,
                    AffectedY = (int)y
                });
                EmitSignal(SignalName.MapModified);
                return;
            }
        }
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────

    private void PushUndo(EditorAction action)
    {
        // Clear redo stack on new action
        _redoStack.Clear();
        _redoNextKey = 0;

        // Enforce max undo size
        while (_undoStack.Count >= MaxUndoActions)
        {
            _undoStack.RemoveAt(0);
        }

        _undoStack.Add(_undoNextKey++, action);
        EmitSignal(SignalName.UndoStackChanged, _undoStack.Count, _redoStack.Count);
    }

    public bool CanUndo() => _undoStack.Count > 0;
    public bool CanRedo() => _redoStack.Count > 0;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        int lastIdx = _undoStack.Count - 1;
        EditorAction action = _undoStack.Values[lastIdx];
        _undoStack.RemoveAt(lastIdx);

        // Apply the before state
        if (action.Type == "terrain" && action.BeforeState != null)
        {
            RestoreElevationSnapshot(action.BeforeState, action.AffectedX, action.AffectedY, action.AffectedRadius);
        }
        else if (action.Type == "biome" && action.BeforeState != null)
        {
            RestoreBiomeSnapshot(action.BeforeState, action.AffectedX, action.AffectedY, action.AffectedRadius);
        }
        // For object add/erase, a full undo would need to store the object itself
        // Simplified: we regenerate from the current state after undo

        _redoStack.Add(_redoNextKey++, action);
        EmitSignal(SignalName.UndoStackChanged, _undoStack.Count, _redoStack.Count);
        EmitSignal(SignalName.MapModified);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        int lastIdx = _redoStack.Count - 1;
        EditorAction action = _redoStack.Values[lastIdx];
        _redoStack.RemoveAt(lastIdx);

        // Apply the after state
        if (action.Type == "terrain" && action.AfterState != null)
        {
            RestoreElevationSnapshot(action.AfterState, action.AffectedX, action.AffectedY, action.AffectedRadius);
        }
        else if (action.Type == "biome" && action.AfterState != null)
        {
            RestoreBiomeSnapshot(action.AfterState, action.AffectedX, action.AffectedY, action.AffectedRadius);
        }

        _undoStack.Add(_undoNextKey++, action);
        EmitSignal(SignalName.UndoStackChanged, _undoStack.Count, _redoStack.Count);
        EmitSignal(SignalName.MapModified);
    }

    private void ClearUndoRedo()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _undoNextKey = 0;
        _redoNextKey = 0;
        EmitSignal(SignalName.UndoStackChanged, 0, 0);
    }

    // ── Snapshots (for undo) ───────────────────────────────────────────────

    private byte[] SnapshotElevation(int cx, int cy, int radius)
    {
        if (_elevation == null) return Array.Empty<byte>();

        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        byte[] data = new byte[w * h * 4];

        int idx = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                byte[] floatBytes = BitConverter.GetBytes(_elevation[y * _mapWidth + x]);
                data[idx++] = floatBytes[0];
                data[idx++] = floatBytes[1];
                data[idx++] = floatBytes[2];
                data[idx++] = floatBytes[3];
            }
        }

        return data;
    }

    private void RestoreElevationSnapshot(byte[] data, int cx, int cy, int radius)
    {
        if (_elevation == null || data == null || data.Length == 0) return;

        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);

        int idx = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (idx + 4 > data.Length) return;
                _elevation[y * _mapWidth + x] = BitConverter.ToSingle(data, idx);
                idx += 4;
            }
        }
    }

    private byte[] SnapshotBiome(int cx, int cy, int radius)
    {
        if (_biomeMap == null) return Array.Empty<byte>();

        // Pack biome names as indices for efficiency
        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        byte[] data = new byte[w * h];

        int idx = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                data[idx++] = BiomeToIndex(_biomeMap[y * _mapWidth + x]);
            }
        }

        return data;
    }

    private void RestoreBiomeSnapshot(byte[] data, int cx, int cy, int radius)
    {
        if (_biomeMap == null || data == null || data.Length == 0) return;

        int minX = Math.Max(0, cx - radius);
        int maxX = Math.Min(_mapWidth - 1, cx + radius);
        int minY = Math.Max(0, cy - radius);
        int maxY = Math.Min(_mapHeight - 1, cy + radius);

        int idx = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (idx >= data.Length) return;
                _biomeMap[y * _mapWidth + x] = IndexToBiome(data[idx++]);
            }
        }
    }

    private static byte BiomeToIndex(string biome)
    {
        if (biome == "temperate") return 0;
        if (biome == "desert") return 1;
        if (biome == "rocky") return 2;
        if (biome == "coastal") return 3;
        if (biome == "volcanic") return 4;
        return 0;
    }

    private static string IndexToBiome(byte index)
    {
        if (index == 0) return "temperate";
        if (index == 1) return "desert";
        if (index == 2) return "rocky";
        if (index == 3) return "coastal";
        if (index == 4) return "volcanic";
        return "temperate";
    }

    // ── Export to MapData ───────────────────────────────────────────────────

    /// <summary>
    /// Exports the current editor state as a MapData object.
    /// </summary>
    public MapData ExportMap()
    {
        // Build elevation zones from the elevation grid
        var elevationZones = BuildElevationZones();

        // Collect props
        var props = new PropPlacement[_props.Count];
        for (int i = 0; i < _props.Count; i++)
            props[i] = _props.Values[i];

        // Collect structures
        var structures = new StructurePlacement[_structures.Count];
        for (int i = 0; i < _structures.Count; i++)
            structures[i] = _structures.Values[i];

        // Collect cordite nodes
        var corditeNodes = new CorditeNodeData[_corditeNodes.Count];
        for (int i = 0; i < _corditeNodes.Count; i++)
            corditeNodes[i] = _corditeNodes.Values[i];

        // Collect starting positions
        var startingPositions = new StartingPosition[_startingPositions.Count];
        for (int i = 0; i < _startingPositions.Count; i++)
            startingPositions[i] = _startingPositions.Values[i];

        // Collect terrain features
        var terrainFeatures = new TerrainFeature[_terrainFeatures.Count];
        for (int i = 0; i < _terrainFeatures.Count; i++)
            terrainFeatures[i] = _terrainFeatures[i];

        return new MapData
        {
            Id = _mapId,
            DisplayName = _mapDisplayName,
            Description = _mapDescription,
            Author = _mapAuthor,
            MaxPlayers = _maxPlayers,
            Width = _mapWidth,
            Height = _mapHeight,
            Biome = _mapBiome,
            StartingPositions = startingPositions,
            CorditeNodes = corditeNodes,
            TerrainFeatures = terrainFeatures,
            Props = props,
            Structures = structures,
            ElevationZones = elevationZones
        };
    }

    /// <summary>
    /// Saves the current map to data/maps/custom/ directory.
    /// </summary>
    public bool SaveMap(string filename)
    {
        MapData data = ExportMap();
        string json = JsonSerializer.Serialize(data, JsonOptions);

        // Ensure custom map directory exists
        string dirPath = CustomMapDirectory;
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(dirPath)))
        {
            dirPath = CustomMapDirectoryUser;
            var dir = DirAccess.Open("user://");
            if (dir != null)
            {
                dir.MakeDirRecursive("maps/custom");
            }
        }

        string filePath = $"{dirPath}/{filename}.json";
        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PrintErr($"[MapEditor] Failed to save map: {FileAccess.GetOpenError()}");
            return false;
        }

        file.StoreString(json);
        GD.Print($"[MapEditor] Map saved to {filePath}");
        return true;
    }

    /// <summary>
    /// Loads a map from file for editing.
    /// </summary>
    public bool LoadMap(string filePath)
    {
        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"[MapEditor] Failed to load map: {FileAccess.GetOpenError()}");
            return false;
        }

        string json = file.GetAsText();
        MapData data;
        try
        {
            data = JsonSerializer.Deserialize<MapData>(json, JsonOptions);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MapEditor] Failed to parse map JSON: {e.Message}");
            return false;
        }

        if (data == null) return false;

        ImportMapData(data);
        GD.Print($"[MapEditor] Loaded map: {data.DisplayName} ({data.Width}x{data.Height})");
        return true;
    }

    /// <summary>
    /// Imports a MapData object into the editor for editing.
    /// </summary>
    public void ImportMapData(MapData data)
    {
        _mapWidth = data.Width;
        _mapHeight = data.Height;
        _mapBiome = data.Biome ?? "temperate";
        _mapId = data.Id ?? "imported";
        _mapDisplayName = data.DisplayName ?? "Imported Map";
        _mapDescription = data.Description ?? "";
        _mapAuthor = data.Author ?? "Unknown";
        _maxPlayers = data.MaxPlayers;

        // Initialize grids
        int totalCells = _mapWidth * _mapHeight;
        _elevation = new float[totalCells];
        _biomeMap = new string[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            _biomeMap[i] = _mapBiome;
        }

        // Rebuild elevation from elevation zones
        if (data.ElevationZones != null)
        {
            for (int i = 0; i < data.ElevationZones.Length; i++)
            {
                ElevationZone zone = data.ElevationZones[i];
                float zoneHeight = zone.Height.ToFloat();
                int cx = zone.CenterX;
                int cy = zone.CenterY;
                int radius = zone.Radius;
                if (radius <= 0) continue;

                int minX = Math.Max(0, cx - radius);
                int maxX = Math.Min(_mapWidth - 1, cx + radius);
                int minY = Math.Max(0, cy - radius);
                int maxY = Math.Min(_mapHeight - 1, cy + radius);
                float radiusSq = radius * radius;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x - cx;
                        float dy = y - cy;
                        float distSq = dx * dx + dy * dy;
                        if (distSq >= radiusSq) continue;

                        float dist = MathF.Sqrt(distSq);
                        float t = dist / radius;
                        float falloff = 0.5f * (1f + MathF.Cos(t * MathF.PI));
                        _elevation[y * _mapWidth + x] += zoneHeight * falloff;
                    }
                }
            }
        }

        // Import objects
        _props.Clear();
        _structures.Clear();
        _corditeNodes.Clear();
        _startingPositions.Clear();
        _terrainFeatures.Clear();
        _nextObjectId = 0;

        if (data.Props != null)
        {
            for (int i = 0; i < data.Props.Length; i++)
            {
                _props.Add(_nextObjectId++, data.Props[i]);
            }
        }

        if (data.Structures != null)
        {
            for (int i = 0; i < data.Structures.Length; i++)
            {
                _structures.Add(_nextObjectId++, data.Structures[i]);
            }
        }

        if (data.CorditeNodes != null)
        {
            for (int i = 0; i < data.CorditeNodes.Length; i++)
            {
                _corditeNodes.Add(_nextObjectId++, data.CorditeNodes[i]);
            }
        }

        if (data.StartingPositions != null)
        {
            for (int i = 0; i < data.StartingPositions.Length; i++)
            {
                _startingPositions.Add(_nextObjectId++, data.StartingPositions[i]);
            }
        }

        if (data.TerrainFeatures != null)
        {
            for (int i = 0; i < data.TerrainFeatures.Length; i++)
            {
                _terrainFeatures.Add(data.TerrainFeatures[i]);
            }
        }

        ClearUndoRedo();
    }

    // ── Properties Access ──────────────────────────────────────────────────

    public int MapWidth => _mapWidth;
    public int MapHeight => _mapHeight;
    public string MapBiome => _mapBiome;
    public string MapId => _mapId;
    public string MapDisplayName => _mapDisplayName;
    public int MaxPlayers => _maxPlayers;

    public void SetMapMetadata(string displayName, string description, string author, int maxPlayers)
    {
        _mapDisplayName = displayName ?? _mapDisplayName;
        _mapDescription = description ?? _mapDescription;
        _mapAuthor = author ?? _mapAuthor;
        _maxPlayers = Math.Clamp(maxPlayers, 2, 6);
    }

    public int PropCount => _props.Count;
    public int StructureCount => _structures.Count;
    public int CorditeNodeCount => _corditeNodes.Count;
    public int StartingPositionCount => _startingPositions.Count;

    // ── Terrain Regeneration ───────────────────────────────────────────────

    /// <summary>
    /// Regenerates the visual terrain from current editor state.
    /// Call after modifying elevation or loading a map.
    /// </summary>
    public void RegenerateTerrain()
    {
        MapData data = ExportMap();
        _terrainRenderer.Generate(data);
        _waterRenderer.Generate(data, _terrainRenderer);
    }

    // ── Elevation Zone Export ───────────────────────────────────────────────

    private ElevationZone[] BuildElevationZones()
    {
        if (_elevation == null) return Array.Empty<ElevationZone>();

        // Find clusters of non-zero elevation and approximate with circles
        // Simple approach: sample grid and create zones for significant elevation areas
        var zones = new List<ElevationZone>();
        bool[] visited = new bool[_mapWidth * _mapHeight];

        int step = Math.Max(1, Math.Min(_mapWidth, _mapHeight) / 32);

        for (int y = 0; y < _mapHeight; y += step)
        {
            for (int x = 0; x < _mapWidth; x += step)
            {
                int idx = y * _mapWidth + x;
                if (visited[idx]) continue;
                if (MathF.Abs(_elevation[idx]) < 0.1f) continue;

                // Found significant elevation — create a zone
                float maxHeight = _elevation[idx];
                int radius = step;

                // Expand radius while elevation is significant
                while (radius < _mapWidth / 4)
                {
                    bool hasSignificant = false;
                    int checkX = x + radius;
                    int checkY = y + radius;
                    if (checkX < _mapWidth && checkY < _mapHeight)
                    {
                        if (MathF.Abs(_elevation[checkY * _mapWidth + checkX]) > 0.1f)
                            hasSignificant = true;
                    }
                    if (!hasSignificant) break;
                    radius += step;
                }

                zones.Add(new ElevationZone
                {
                    Type = maxHeight > 0 ? "hill" : "valley",
                    CenterX = x,
                    CenterY = y,
                    Radius = radius,
                    Height = FixedPoint.FromFloat(maxHeight)
                });

                // Mark area as visited
                for (int vy = Math.Max(0, y - radius); vy < Math.Min(_mapHeight, y + radius); vy++)
                {
                    for (int vx = Math.Max(0, x - radius); vx < Math.Min(_mapWidth, x + radius); vx++)
                    {
                        visited[vy * _mapWidth + vx] = true;
                    }
                }
            }
        }

        var result = new ElevationZone[zones.Count];
        for (int i = 0; i < zones.Count; i++)
            result[i] = zones[i];

        return result;
    }
}
