using Godot;
using CorditeWars.Systems.Platform;

namespace CorditeWars.Game.Camera;

/// <summary>
/// Top-down RTS camera with WASD/arrow panning, mouse edge scrolling,
/// scroll wheel zoom, and middle mouse button rotation.
/// On mobile: pinch-to-zoom, two-finger drag to pan, handled via
/// <see cref="TouchInputHandler"/> signals.
/// Pure rendering code — uses float, no FixedPoint needed.
/// </summary>
public partial class RTSCamera : Camera3D
{
    // ── Zoom ─────────────────────────────────────────────────────────
    private const float ZoomMin = 10.0f;
    private const float ZoomMax = 80.0f;
    private const float ZoomStep = 3.0f;
    private const float ZoomLerpSpeed = 8.0f;
    private const float ReferenceZoomLevel = 30.0f;
    private const float TouchZoomMultiplier = 5.0f;

    // ── Pan ──────────────────────────────────────────────────────────
    private const float BasePanSpeed = 30.0f;
    private const float MinMovementThreshold = 0.001f;
    private const float EdgeScrollSpeedMultiplier = 0.5f;
    private const float EdgeScrollMargin = 20.0f;

    // ── Rotation ─────────────────────────────────────────────────────
    private const float RotateSpeed = 0.005f;

    // ── Camera Geometry ──────────────────────────────────────────────
    private const float CameraAngleDeg = 55.0f;

    // ── Touch Pan ─────────────────────────────────────────────────────
    private const float TouchPanSpeed = 0.15f;

    // ── State ────────────────────────────────────────────────────────
    private Vector3 _focusPoint = Vector3.Zero;
    private float _currentZoom = 30.0f;
    private float _targetZoom = 30.0f;
    private float _yaw;
    private bool _rotating;

    private TouchInputHandler? _touchHandler;

    public override void _Ready()
    {
        _focusPoint = Vector3.Zero;
        _currentZoom = 30.0f;
        _targetZoom = 30.0f;
        _yaw = 0.0f;

        // Set up touch input handler for mobile
        if (PlatformDetector.ShouldUseTouchInput())
        {
            _touchHandler = new TouchInputHandler();
            _touchHandler.Name = "TouchInputHandler";
            AddChild(_touchHandler);
            _touchHandler.PinchZoom += OnPinchZoom;
            _touchHandler.TwoFingerPan += OnTwoFingerPan;
        }

        UpdateCameraTransform();
    }

    public override void _ExitTree()
    {
        if (_touchHandler is not null)
        {
            _touchHandler.PinchZoom -= OnPinchZoom;
            _touchHandler.TwoFingerPan -= OnTwoFingerPan;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        HandleKeyboardPan(dt);
        HandleEdgeScroll(dt);
        SmoothZoom(dt);
        UpdateCameraTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            HandleMouseMotion(mouseMotion);
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        // Zoom
        if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
        {
            _targetZoom = Mathf.Clamp(_targetZoom - ZoomStep, ZoomMin, ZoomMax);
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
        {
            _targetZoom = Mathf.Clamp(_targetZoom + ZoomStep, ZoomMin, ZoomMax);
        }

        // Middle mouse rotate
        if (mouseButton.ButtonIndex == MouseButton.Middle)
        {
            _rotating = mouseButton.Pressed;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (_rotating)
        {
            _yaw -= mouseMotion.Relative.X * RotateSpeed;
        }
    }

    private void HandleKeyboardPan(float dt)
    {
        // Pan speed scales with zoom level (faster when zoomed out)
        float panSpeed = BasePanSpeed * (_currentZoom / ReferenceZoomLevel) * dt;

        Vector3 forward = new Vector3(Mathf.Sin(_yaw), 0.0f, Mathf.Cos(_yaw));
        // Godot's LookAt builds the camera basis with local +X = up × (pos−target), so
        // screen-right maps to world −(forward.Z, 0, −forward.X).  Negate to match.
        Vector3 right = new Vector3(-forward.Z, 0.0f, forward.X);

        Vector3 move = Vector3.Zero;

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            move += forward;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            move -= forward;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            move -= right;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            move += right;

        if (move.LengthSquared() > MinMovementThreshold)
        {
            _focusPoint += move.Normalized() * panSpeed;
        }
    }

    private void HandleEdgeScroll(float dt)
    {
        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;

        float panSpeed = BasePanSpeed * (_currentZoom / ReferenceZoomLevel) * dt * EdgeScrollSpeedMultiplier;

        Vector3 forward = new Vector3(Mathf.Sin(_yaw), 0.0f, Mathf.Cos(_yaw));
        Vector3 right = new Vector3(-forward.Z, 0.0f, forward.X);

        Vector3 move = Vector3.Zero;

        if (mousePos.X < EdgeScrollMargin)
            move -= right;
        else if (mousePos.X > viewportSize.X - EdgeScrollMargin)
            move += right;

        if (mousePos.Y < EdgeScrollMargin)
            move += forward;
        else if (mousePos.Y > viewportSize.Y - EdgeScrollMargin)
            move -= forward;

        if (move.LengthSquared() > MinMovementThreshold)
        {
            _focusPoint += move.Normalized() * panSpeed;
        }
    }

    private void SmoothZoom(float dt)
    {
        _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, ZoomLerpSpeed * dt);
    }

    private void UpdateCameraTransform()
    {
        // Camera orbits around focus point at the current yaw and fixed pitch
        float angleRad = Mathf.DegToRad(CameraAngleDeg);

        // Calculate offset from focus point
        float horizontalDist = _currentZoom * Mathf.Cos(angleRad);
        float verticalDist = _currentZoom * Mathf.Sin(angleRad);

        Vector3 offset = new Vector3(
            -Mathf.Sin(_yaw) * horizontalDist,
            verticalDist,
            -Mathf.Cos(_yaw) * horizontalDist);

        Position = _focusPoint + offset;
        LookAt(_focusPoint, Vector3.Up);
    }

    /// <summary>Current camera look-at center in world space (X/Z).</summary>
    public Vector3 FocusPoint => _focusPoint;

    /// <summary>Current zoom distance (world units).</summary>
    public float CurrentZoom => _currentZoom;

    /// <summary>
    /// Moves the camera focus to the given world position.
    /// </summary>
    public void SetFocusPoint(Vector3 point)
    {
        _focusPoint = new Vector3(point.X, 0.0f, point.Z);
    }

    // ── Touch Input Handlers ────────────────────────────────────────

    private void OnPinchZoom(float zoomDelta)
    {
        // Negative delta = fingers moving apart = zoom in (decrease target zoom)
        _targetZoom = Mathf.Clamp(_targetZoom - zoomDelta * ZoomStep * TouchZoomMultiplier, ZoomMin, ZoomMax);
    }

    private void OnTwoFingerPan(Vector2 panDelta)
    {
        float panScale = TouchPanSpeed * (_currentZoom / 30.0f);

        Vector3 forward = new Vector3(Mathf.Sin(_yaw), 0.0f, Mathf.Cos(_yaw));
        Vector3 right = new Vector3(-forward.Z, 0.0f, forward.X);

        // Screen space to world: X drag → right, Y drag → forward
        _focusPoint -= right * panDelta.X * panScale;
        _focusPoint -= forward * panDelta.Y * panScale;
    }
}
