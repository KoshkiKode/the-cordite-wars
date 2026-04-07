using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Camera;
using CorditeWars.Game.Units;

namespace CorditeWars.UI.Input;

/// <summary>
/// Handles all unit selection mechanics: click, box select, shift-add,
/// double-click same type, Ctrl+1-9 control groups, deselect on empty ground.
/// </summary>
public partial class SelectionManager : Node
{
    // ── State ────────────────────────────────────────────────────────

    private readonly SortedList<int, UnitNode3D> _selected = new();
    private readonly SortedList<int, UnitNode3D>[] _controlGroups = new SortedList<int, UnitNode3D>[10];

    private int _localPlayerId;
    private UnitSpawner? _unitSpawner;
    private Camera3D? _camera;

    // Box-select drag state
    private bool _isDragging;
    private Vector2 _dragStart;
    private Vector2 _dragEnd;
    private const float DragThreshold = 8f;

    // Double-click detection
    private double _lastClickTime;
    private int _lastClickedUnitId = -1;
    private const double DoubleClickWindow = 0.35;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(int localPlayerId, UnitSpawner unitSpawner, Camera3D camera)
    {
        _localPlayerId = localPlayerId;
        _unitSpawner = unitSpawner;
        _camera = camera;

        for (int i = 0; i < 10; i++)
            _controlGroups[i] = new SortedList<int, UnitNode3D>();
    }

    // ── Public API ───────────────────────────────────────────────────

    public IList<UnitNode3D> GetSelectedUnits() => _selected.Values;
    public int SelectedCount => _selected.Count;

    public List<int> GetSelectedUnitIds()
    {
        var ids = new List<int>(_selected.Count);
        for (int i = 0; i < _selected.Count; i++)
            ids.Add(_selected.Keys[i]);
        return ids;
    }

    public bool HasSelection => _selected.Count > 0;

    // ── Input Processing ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_camera is null || _unitSpawner is null) return;

        if (@event is InputEventMouseButton mouseBtn)
            HandleMouseButton(mouseBtn);
        else if (@event is InputEventMouseMotion mouseMotion)
            HandleMouseMotion(mouseMotion);
        else if (@event is InputEventKey keyEvent)
            HandleKeyInput(keyEvent);
    }

    private void HandleMouseButton(InputEventMouseButton mouseBtn)
    {
        if (mouseBtn.ButtonIndex != MouseButton.Left) return;

        if (mouseBtn.Pressed)
        {
            _isDragging = true;
            _dragStart = mouseBtn.Position;
            _dragEnd = mouseBtn.Position;
        }
        else // Released
        {
            bool isShift = mouseBtn.ShiftPressed;
            float dragDist = _dragStart.DistanceTo(mouseBtn.Position);

            if (dragDist < DragThreshold)
            {
                // Click select
                HandleClickSelect(mouseBtn.Position, isShift);
            }
            else
            {
                // Box select
                _dragEnd = mouseBtn.Position;
                HandleBoxSelect(isShift);
            }

            _isDragging = false;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (_isDragging)
            _dragEnd = mouseMotion.Position;
    }

    private void HandleKeyInput(InputEventKey keyEvent)
    {
        if (!keyEvent.Pressed) return;

        // Control groups: Ctrl+1-9 to assign, 1-9 to recall
        int groupIndex = GetGroupIndexFromKey(keyEvent.Keycode);
        if (groupIndex >= 0)
        {
            if (keyEvent.CtrlPressed)
                AssignControlGroup(groupIndex);
            else
                RecallControlGroup(groupIndex, keyEvent.IsEcho());
        }
    }

    // ── Click Select ─────────────────────────────────────────────────

    private void HandleClickSelect(Vector2 screenPos, bool shiftHeld)
    {
        UnitNode3D? hitUnit = RaycastForUnit(screenPos);

        if (hitUnit is null)
        {
            // Clicked empty ground — deselect
            if (!shiftHeld)
                ClearSelection();
            return;
        }

        // Only select own units
        if (hitUnit.FactionId != GetLocalFactionId()) return;

        // Check double-click
        double now = Time.GetTicksMsec() / 1000.0;
        if (hitUnit.UnitId == _lastClickedUnitId && (now - _lastClickTime) < DoubleClickWindow)
        {
            SelectAllOfType(hitUnit.UnitTypeId);
            _lastClickedUnitId = -1;
            return;
        }
        _lastClickTime = now;
        _lastClickedUnitId = hitUnit.UnitId;

        if (shiftHeld)
        {
            // Toggle selection
            if (_selected.ContainsKey(hitUnit.UnitId))
                RemoveFromSelection(hitUnit);
            else
                AddToSelection(hitUnit);
        }
        else
        {
            ClearSelection();
            AddToSelection(hitUnit);
        }
    }

    // ── Box Select ───────────────────────────────────────────────────

    private void HandleBoxSelect(bool shiftHeld)
    {
        if (_unitSpawner is null || _camera is null) return;

        if (!shiftHeld)
            ClearSelection();

        Rect2 box = new Rect2(
            Mathf.Min(_dragStart.X, _dragEnd.X),
            Mathf.Min(_dragStart.Y, _dragEnd.Y),
            Mathf.Abs(_dragEnd.X - _dragStart.X),
            Mathf.Abs(_dragEnd.Y - _dragStart.Y));

        string localFaction = GetLocalFactionId();
        var allUnits = _unitSpawner.GetAllUnits();
        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitNode3D unit = allUnits[i];
            if (!unit.IsAlive) continue;
            if (unit.FactionId != localFaction) continue;

            Vector2 screenPos = _camera.UnprojectPosition(unit.GlobalPosition);
            if (box.HasPoint(screenPos))
                AddToSelection(unit);
        }
    }

    // ── Double-Click Same Type ───────────────────────────────────────

    private void SelectAllOfType(string unitTypeId)
    {
        if (_unitSpawner is null || _camera is null) return;

        ClearSelection();

        Rect2 viewport = GetViewport().GetVisibleRect();
        string localFaction = GetLocalFactionId();
        var allUnits = _unitSpawner.GetAllUnits();

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitNode3D unit = allUnits[i];
            if (!unit.IsAlive) continue;
            if (unit.FactionId != localFaction) continue;
            if (unit.UnitTypeId != unitTypeId) continue;

            Vector2 screenPos = _camera.UnprojectPosition(unit.GlobalPosition);
            if (viewport.HasPoint(screenPos))
                AddToSelection(unit);
        }
    }

    // ── Control Groups ───────────────────────────────────────────────

    private void AssignControlGroup(int index)
    {
        _controlGroups[index].Clear();
        for (int i = 0; i < _selected.Count; i++)
            _controlGroups[index].Add(_selected.Keys[i], _selected.Values[i]);
    }

    private double _lastGroupRecallTime;
    private int _lastGroupRecallIndex = -1;

    private void RecallControlGroup(int index, bool isEcho)
    {
        if (isEcho) return;

        var group = _controlGroups[index];
        if (group.Count == 0) return;

        // Double-tap: center camera on group
        double now = Time.GetTicksMsec() / 1000.0;
        if (index == _lastGroupRecallIndex && (now - _lastGroupRecallTime) < DoubleClickWindow)
        {
            CenterCameraOnGroup(group);
            _lastGroupRecallIndex = -1;
            return;
        }
        _lastGroupRecallTime = now;
        _lastGroupRecallIndex = index;

        // Recall: select the group
        ClearSelection();

        // Clean dead units from group
        var deadKeys = new List<int>();
        for (int i = 0; i < group.Count; i++)
        {
            if (!group.Values[i].IsAlive)
                deadKeys.Add(group.Keys[i]);
        }
        for (int i = 0; i < deadKeys.Count; i++)
            group.Remove(deadKeys[i]);

        for (int i = 0; i < group.Count; i++)
            AddToSelection(group.Values[i]);
    }

    private void CenterCameraOnGroup(SortedList<int, UnitNode3D> group)
    {
        if (group.Count == 0) return;

        Vector3 center = Vector3.Zero;
        int alive = 0;
        for (int i = 0; i < group.Count; i++)
        {
            if (group.Values[i].IsAlive)
            {
                center += group.Values[i].GlobalPosition;
                alive++;
            }
        }

        if (alive > 0)
        {
            center /= alive;
            var camera = GetViewport().GetCamera3D() as RTSCamera;
            camera?.SetFocusPoint(center);
        }
    }

    // ── Selection Helpers ────────────────────────────────────────────

    private void AddToSelection(UnitNode3D unit)
    {
        if (_selected.ContainsKey(unit.UnitId)) return;
        _selected.Add(unit.UnitId, unit);
        unit.SetSelected(true);
        EventBus.Instance?.EmitUnitSelected(unit);
        NotifySelectionChanged();
    }

    private void RemoveFromSelection(UnitNode3D unit)
    {
        if (!_selected.ContainsKey(unit.UnitId)) return;
        _selected.Remove(unit.UnitId);
        unit.SetSelected(false);
        EventBus.Instance?.EmitUnitDeselected(unit);
        NotifySelectionChanged();
    }

    public void ClearSelection()
    {
        for (int i = _selected.Count - 1; i >= 0; i--)
            _selected.Values[i].SetSelected(false);
        _selected.Clear();
        EventBus.Instance?.EmitSelectionCleared();
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        var ids = new int[_selected.Count];
        for (int i = 0; i < _selected.Count; i++)
            ids[i] = _selected.Keys[i];
        EventBus.Instance?.EmitSelectionChanged(ids);
    }

    // ── Raycasting ───────────────────────────────────────────────────

    private UnitNode3D? RaycastForUnit(Vector2 screenPos)
    {
        if (_camera is null) return null;

        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 dir = _camera.ProjectRayNormal(screenPos);
        Vector3 to = from + dir * 1000f;

        var spaceState = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 0b0100; // Layer 3 (units use layer 2 = bit 2)
        query.CollideWithAreas = true;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0) return null;

        Node collider = (Node)result["collider"];
        // Walk up from Area3D to UnitNode3D
        Node? current = collider;
        while (current is not null)
        {
            if (current is UnitNode3D unit)
                return unit;
            current = current.GetParent();
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private int GetGroupIndexFromKey(Key keycode)
    {
        if (keycode >= Key.Key1 && keycode <= Key.Key9)
            return (int)(keycode - Key.Key1);
        if (keycode == Key.Key0)
            return 9;
        return -1;
    }

    private string GetLocalFactionId()
    {
        // Look up from unit spawner — local player's faction is determined by their units
        if (_unitSpawner is null) return string.Empty;
        var allUnits = _unitSpawner.GetAllUnits();
        // Convention: playerId is encoded via spawn context, faction is stored on unit
        // For now we trust that local player owns units with their faction
        return allUnits.Count > 0 ? allUnits[0].FactionId : string.Empty;
    }

    // ── Box Select Drawing (called from HUD) ─────────────────────────

    public bool IsDragging => _isDragging;
    public Rect2 GetDragRect()
    {
        return new Rect2(
            Mathf.Min(_dragStart.X, _dragEnd.X),
            Mathf.Min(_dragStart.Y, _dragEnd.Y),
            Mathf.Abs(_dragEnd.X - _dragStart.X),
            Mathf.Abs(_dragEnd.Y - _dragStart.Y));
    }
}
