using Godot;
using System.Collections.Generic;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Units;
using UnnamedRTS.Systems.Networking;

namespace UnnamedRTS.UI.Input;

/// <summary>
/// Handles right-click commands, hotkeys, and shift-queue orders.
/// Issues GameCommands through CommandBuffer for deterministic simulation.
/// </summary>
public partial class CommandInput : Node
{
    private SelectionManager? _selectionManager;
    private CommandBuffer? _commandBuffer;
    private UnitSpawner? _unitSpawner;
    private Camera3D? _camera;
    private int _localPlayerId;

    // Attack-move mode
    private bool _attackMoveMode;

    // Patrol mode: waiting for second click
    private bool _patrolMode;
    private FixedVector2 _patrolStart;

    // Input delay for deterministic networking
    private const int InputDelay = 6;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int localPlayerId,
        SelectionManager selectionManager,
        CommandBuffer commandBuffer,
        UnitSpawner unitSpawner,
        Camera3D camera)
    {
        _localPlayerId = localPlayerId;
        _selectionManager = selectionManager;
        _commandBuffer = commandBuffer;
        _unitSpawner = unitSpawner;
        _camera = camera;
    }

    // ── Input Processing ─────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_selectionManager is null || _commandBuffer is null) return;
        if (!_selectionManager.HasSelection) return;

        if (@event is InputEventMouseButton mouseBtn)
            HandleMouseButton(mouseBtn);
        else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            HandleKeyInput(keyEvent);
    }

    private void HandleMouseButton(InputEventMouseButton mouseBtn)
    {
        if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
        {
            HandleRightClick(mouseBtn.Position, mouseBtn.ShiftPressed);
            GetViewport().SetInputAsHandled();
        }
        else if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
        {
            if (_attackMoveMode)
            {
                HandleAttackMoveClick(mouseBtn.Position, mouseBtn.ShiftPressed);
                _attackMoveMode = false;
                GetViewport().SetInputAsHandled();
            }
            else if (_patrolMode)
            {
                HandlePatrolClick(mouseBtn.Position, mouseBtn.ShiftPressed);
                _patrolMode = false;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void HandleKeyInput(InputEventKey keyEvent)
    {
        switch (keyEvent.Keycode)
        {
            case Key.A:
                _attackMoveMode = true;
                _patrolMode = false;
                break;

            case Key.S:
                IssueStopCommand();
                break;

            case Key.H:
                IssueHoldCommand();
                break;

            case Key.P:
                _patrolMode = true;
                _attackMoveMode = false;
                // First waypoint is current position — next click sets destination
                if (_selectionManager!.SelectedCount > 0)
                {
                    var units = _selectionManager.GetSelectedUnits();
                    _patrolStart = units[0].SimPosition;
                }
                break;

            case Key.Escape:
                _attackMoveMode = false;
                _patrolMode = false;
                break;
        }
    }

    // ── Right-Click Commands ─────────────────────────────────────────

    private void HandleRightClick(Vector2 screenPos, bool shiftQueue)
    {
        if (_camera is null || _selectionManager is null) return;

        // Check if clicking on an enemy unit
        UnitNode3D? target = RaycastForUnit(screenPos);
        if (target is not null && target.FactionId != GetLocalFactionId() && target.IsAlive)
        {
            IssueAttackCommand(target.UnitId, shiftQueue);
            EventBus.Instance?.EmitUnitOrdered(
                _selectionManager.GetSelectedUnitIds().ToArray(), "attack");
            return;
        }

        // Otherwise: move command to ground position
        Vector3? worldPos = RaycastGround(screenPos);
        if (worldPos.HasValue)
        {
            FixedVector2 fixedTarget = new FixedVector2(
                FixedPoint.FromFloat(worldPos.Value.X),
                FixedPoint.FromFloat(worldPos.Value.Z));
            IssueMoveCommand(fixedTarget, shiftQueue);
            EventBus.Instance?.EmitUnitOrdered(
                _selectionManager.GetSelectedUnitIds().ToArray(), "move");
        }
    }

    private void HandleAttackMoveClick(Vector2 screenPos, bool shiftQueue)
    {
        Vector3? worldPos = RaycastGround(screenPos);
        if (worldPos.HasValue)
        {
            FixedVector2 fixedTarget = new FixedVector2(
                FixedPoint.FromFloat(worldPos.Value.X),
                FixedPoint.FromFloat(worldPos.Value.Z));
            IssueAttackMoveCommand(fixedTarget, shiftQueue);
            EventBus.Instance?.EmitUnitOrdered(
                _selectionManager!.GetSelectedUnitIds().ToArray(), "attackmove");
        }
    }

    private void HandlePatrolClick(Vector2 screenPos, bool shiftQueue)
    {
        Vector3? worldPos = RaycastGround(screenPos);
        if (worldPos.HasValue)
        {
            FixedVector2 patrolEnd = new FixedVector2(
                FixedPoint.FromFloat(worldPos.Value.X),
                FixedPoint.FromFloat(worldPos.Value.Z));

            var waypoints = new List<FixedVector2>(2);
            waypoints.Add(_patrolStart);
            waypoints.Add(patrolEnd);

            IssuePatrolCommand(waypoints, shiftQueue);
            EventBus.Instance?.EmitUnitOrdered(
                _selectionManager!.GetSelectedUnitIds().ToArray(), "patrol");
        }
    }

    // ── Command Issuance ─────────────────────────────────────────────

    private ulong GetScheduledTick()
    {
        var gm = GetNodeOrNull<GameManager>("/root/GameManager");
        return (gm?.CurrentTick ?? 0) + InputDelay;
    }

    private void IssueMoveCommand(FixedVector2 target, bool queue)
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new MoveCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            TargetPosition = target,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
        EventBus.Instance?.EmitMoveCommandIssued(target.ToVector3());
    }

    private void IssueAttackCommand(int targetUnitId, bool queue)
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new AttackCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            TargetUnitId = targetUnitId,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
    }

    private void IssueAttackMoveCommand(FixedVector2 target, bool queue)
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new AttackMoveCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            TargetPosition = target,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
    }

    private void IssueStopCommand()
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new StopCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
        EventBus.Instance?.EmitUnitOrdered(unitIds.ToArray(), "stop");
    }

    private void IssueHoldCommand()
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new HoldPositionCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
        EventBus.Instance?.EmitUnitOrdered(unitIds.ToArray(), "hold");
    }

    private void IssuePatrolCommand(List<FixedVector2> waypoints, bool queue)
    {
        if (_selectionManager is null || _commandBuffer is null) return;

        var unitIds = _selectionManager.GetSelectedUnitIds();
        if (unitIds.Count == 0) return;

        var cmd = new PatrolCommand
        {
            ScheduledTick = GetScheduledTick(),
            PlayerId = _localPlayerId,
            Waypoints = waypoints,
            UnitIds = unitIds
        };
        _commandBuffer.AddCommand(cmd);
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
        query.CollisionMask = 0b0100; // Unit collision layer
        query.CollideWithAreas = true;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0) return null;

        Node collider = (Node)result["collider"];
        Node? current = collider;
        while (current is not null)
        {
            if (current is UnitNode3D unit)
                return unit;
            current = current.GetParent();
        }
        return null;
    }

    private Vector3? RaycastGround(Vector2 screenPos)
    {
        if (_camera is null) return null;

        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 dir = _camera.ProjectRayNormal(screenPos);
        Vector3 to = from + dir * 1000f;

        var spaceState = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 0b0001; // Ground layer
        query.CollideWithBodies = true;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            // Fallback: intersect with Y=0 plane
            if (Mathf.Abs(dir.Y) < 0.001f) return null;
            float t = -from.Y / dir.Y;
            if (t < 0) return null;
            return from + dir * t;
        }

        return (Vector3)result["position"];
    }

    private string GetLocalFactionId()
    {
        if (_unitSpawner is null) return string.Empty;
        var allUnits = _unitSpawner.GetAllUnits();
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].IsAlive)
                return allUnits[i].FactionId;
        }
        return string.Empty;
    }

    // ── Public Mode State ────────────────────────────────────────────

    public bool IsAttackMoveMode => _attackMoveMode;
    public bool IsPatrolMode => _patrolMode;

    public void CancelModes()
    {
        _attackMoveMode = false;
        _patrolMode = false;
    }
}
