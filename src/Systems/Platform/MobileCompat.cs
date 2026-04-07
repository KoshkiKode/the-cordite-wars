using System;
using Godot;
using CorditeWars.Systems.Graphics;

namespace CorditeWars.Systems.Platform;

/// <summary>
/// Detects the current platform and exposes helper queries.
/// All properties are evaluated once and cached.
/// </summary>
public static class PlatformDetector
{
    private static bool _initialized;
    private static bool _isAndroid;
    private static bool _isIOS;
    private static bool _isDesktop;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        string osName = OS.GetName();
        _isAndroid = osName == "Android";
        _isIOS = osName == "iOS";
        _isDesktop = !_isAndroid && !_isIOS;
    }

    public static bool IsAndroid { get { EnsureInitialized(); return _isAndroid; } }
    public static bool IsIOS { get { EnsureInitialized(); return _isIOS; } }
    public static bool IsDesktop { get { EnsureInitialized(); return _isDesktop; } }
    public static bool IsMobile { get { EnsureInitialized(); return _isAndroid || _isIOS; } }

    /// <summary>
    /// Returns the correct Godot user:// save directory. user:// is
    /// cross-platform in Godot and maps to app-specific storage on
    /// Android/iOS, so this always returns "user://saves".
    /// </summary>
    public static string GetPlatformSaveDirectory() => "user://saves";

    /// <summary>
    /// Returns the recommended max texture size for the current quality tier.
    /// </summary>
    public static int GetMaxTextureSize()
    {
        QualityManager? qm = QualityManager.Instance;
        if (qm is null)
        {
            return IsMobile ? 512 : 1024;
        }

        return qm.CurrentTier switch
        {
            QualityTier.Potato => 512,
            QualityTier.Low => 1024,
            QualityTier.Medium => 2048,
            QualityTier.High => 4096,
            _ => 1024
        };
    }

    /// <summary>
    /// Returns true if the current platform should use touch input.
    /// </summary>
    public static bool ShouldUseTouchInput()
    {
        if (IsMobile) return true;

        // Desktop touchscreens: check if the device supports touch
        return DisplayServer.IsTouchscreenAvailable();
    }
}

/// <summary>
/// Processes touch input and translates multi-touch gestures into
/// camera/interaction commands. Attach as a child of the scene root
/// or the RTS camera node.
///
/// Touch mapping:
///   - Single tap → left click (select)
///   - Two-finger tap → right click (move/attack command)
///   - Pinch → zoom
///   - Two-finger drag → pan camera
///   - Long press → context menu / attack-move
///   - Swipe from screen edge → open minimap/command panel
///
/// This is rendering/input code — floats are OK.
/// </summary>
public partial class TouchInputHandler : Node
{
    // ── Configuration ────────────────────────────────────────────────

    /// <summary>Time in seconds a finger must be held without moving to trigger long press.</summary>
    private const float LongPressThreshold = 0.6f;

    /// <summary>Max distance (px) a finger can move and still count as a tap.</summary>
    private const float TapDistanceThreshold = 20.0f;

    /// <summary>Max time (sec) between touch-down and touch-up for a single tap.</summary>
    private const float TapTimeThreshold = 0.3f;

    /// <summary>Max time (sec) between two taps for a double-tap.</summary>
    private const float DoubleTapTimeThreshold = 0.4f;

    /// <summary>Max time (sec) between two fingers touching down for a two-finger tap.</summary>
    private const float TwoFingerTapWindow = 0.15f;

    /// <summary>Pixels from screen edge that count as an edge swipe.</summary>
    private const float EdgeSwipeMargin = 40.0f;

    /// <summary>Minimum swipe distance (px) to trigger edge swipe action.</summary>
    private const float EdgeSwipeMinDistance = 80.0f;

    // ── Signals ──────────────────────────────────────────────────────

    [Signal] public delegate void TapEventHandler(Vector2 screenPosition);
    [Signal] public delegate void DoubleTapEventHandler(Vector2 screenPosition);
    [Signal] public delegate void TwoFingerTapEventHandler(Vector2 screenPosition);
    [Signal] public delegate void LongPressEventHandler(Vector2 screenPosition);
    [Signal] public delegate void PinchZoomEventHandler(float zoomDelta);
    [Signal] public delegate void TwoFingerPanEventHandler(Vector2 panDelta);
    [Signal] public delegate void EdgeSwipeEventHandler(string edge);

    // ── Internal State ───────────────────────────────────────────────

    // Per-finger tracking (index 0 and 1 for two-finger gestures)
    private readonly TouchFinger[] _fingers = new TouchFinger[2];
    private int _activeTouchCount;

    // Pinch/pan gesture state
    private float _previousPinchDistance;
    private Vector2 _previousMidpoint;
    private bool _multiTouchActive;

    // Tap detection
    private float _lastTapTime;
    private Vector2 _lastTapPosition;
    private bool _longPressEmitted;

    // Two-finger tap detection
    private float _secondFingerDownTime;

    // Edge swipe tracking
    private Vector2 _edgeSwipeStart;
    private bool _trackingEdgeSwipe;
    private string _edgeSwipeEdge = string.Empty;

    private struct TouchFinger
    {
        public bool Active;
        public int Index;
        public Vector2 StartPosition;
        public Vector2 CurrentPosition;
        public float StartTime;
    }

    public override void _Ready()
    {
        _fingers[0] = new TouchFinger();
        _fingers[1] = new TouchFinger();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!PlatformDetector.ShouldUseTouchInput())
            return;

        if (@event is InputEventScreenTouch touchEvent)
        {
            HandleScreenTouch(touchEvent);
        }
        else if (@event is InputEventScreenDrag dragEvent)
        {
            HandleScreenDrag(dragEvent);
        }
    }

    public override void _Process(double delta)
    {
        if (!PlatformDetector.ShouldUseTouchInput())
            return;

        float now = (float)Time.GetTicksMsec() / 1000.0f;

        // Check for long press on finger 0
        if (_activeTouchCount == 1 && _fingers[0].Active && !_longPressEmitted)
        {
            float heldTime = now - _fingers[0].StartTime;
            float movedDist = _fingers[0].CurrentPosition.DistanceTo(_fingers[0].StartPosition);

            if (heldTime >= LongPressThreshold && movedDist < TapDistanceThreshold)
            {
                _longPressEmitted = true;
                EmitSignal(SignalName.LongPress, _fingers[0].CurrentPosition);
            }
        }
    }

    // ── Touch Handling ───────────────────────────────────────────────

    private void HandleScreenTouch(InputEventScreenTouch touch)
    {
        float now = (float)Time.GetTicksMsec() / 1000.0f;

        if (touch.Pressed)
        {
            OnFingerDown(touch.Index, touch.Position, now);
        }
        else
        {
            OnFingerUp(touch.Index, touch.Position, now);
        }
    }

    private void OnFingerDown(int fingerIndex, Vector2 position, float time)
    {
        int slot = -1;
        if (!_fingers[0].Active)
            slot = 0;
        else if (!_fingers[1].Active)
            slot = 1;

        if (slot < 0) return; // Only track two fingers

        _fingers[slot] = new TouchFinger
        {
            Active = true,
            Index = fingerIndex,
            StartPosition = position,
            CurrentPosition = position,
            StartTime = time
        };

        _activeTouchCount++;
        _longPressEmitted = false;

        // If second finger just came down, record time for two-finger tap detection
        if (_activeTouchCount == 2)
        {
            _secondFingerDownTime = time;
            float dist = _fingers[0].CurrentPosition.DistanceTo(_fingers[1].CurrentPosition);
            _previousPinchDistance = dist;
            _previousMidpoint = (_fingers[0].CurrentPosition + _fingers[1].CurrentPosition) * 0.5f;
            _multiTouchActive = true;
        }

        // Check for edge swipe start
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        if (position.X < EdgeSwipeMargin)
        {
            _trackingEdgeSwipe = true;
            _edgeSwipeStart = position;
            _edgeSwipeEdge = "left";
        }
        else if (position.X > viewportSize.X - EdgeSwipeMargin)
        {
            _trackingEdgeSwipe = true;
            _edgeSwipeStart = position;
            _edgeSwipeEdge = "right";
        }
        else if (position.Y < EdgeSwipeMargin)
        {
            _trackingEdgeSwipe = true;
            _edgeSwipeStart = position;
            _edgeSwipeEdge = "top";
        }
        else if (position.Y > viewportSize.Y - EdgeSwipeMargin)
        {
            _trackingEdgeSwipe = true;
            _edgeSwipeStart = position;
            _edgeSwipeEdge = "bottom";
        }
        else
        {
            _trackingEdgeSwipe = false;
        }
    }

    private void OnFingerUp(int fingerIndex, Vector2 position, float time)
    {
        int slot = GetSlotForFinger(fingerIndex);
        if (slot < 0) return;

        TouchFinger finger = _fingers[slot];
        float heldTime = time - finger.StartTime;
        float movedDist = position.DistanceTo(finger.StartPosition);

        // Edge swipe detection
        if (_trackingEdgeSwipe && _activeTouchCount == 1)
        {
            float swipeDist = position.DistanceTo(_edgeSwipeStart);
            if (swipeDist >= EdgeSwipeMinDistance)
            {
                EmitSignal(SignalName.EdgeSwipe, _edgeSwipeEdge);
            }
            _trackingEdgeSwipe = false;
        }

        // Two-finger tap: both fingers released quickly, neither moved far
        if (_activeTouchCount == 2 && _multiTouchActive)
        {
            float timeSincePair = time - _secondFingerDownTime;
            int otherSlot = slot == 0 ? 1 : 0;
            float otherMovedDist = _fingers[otherSlot].CurrentPosition.DistanceTo(_fingers[otherSlot].StartPosition);

            if (timeSincePair < TapTimeThreshold && movedDist < TapDistanceThreshold && otherMovedDist < TapDistanceThreshold)
            {
                Vector2 midpoint = (finger.StartPosition + _fingers[otherSlot].StartPosition) * 0.5f;
                EmitSignal(SignalName.TwoFingerTap, midpoint);
            }
        }

        // Single tap detection (only if single finger, didn't move much, short hold)
        if (_activeTouchCount == 1 && !_multiTouchActive && !_longPressEmitted)
        {
            if (heldTime < TapTimeThreshold && movedDist < TapDistanceThreshold)
            {
                // Check for double-tap
                if (time - _lastTapTime < DoubleTapTimeThreshold &&
                    position.DistanceTo(_lastTapPosition) < TapDistanceThreshold)
                {
                    EmitSignal(SignalName.DoubleTap, position);
                    _lastTapTime = 0; // Reset to prevent triple-tap
                }
                else
                {
                    EmitSignal(SignalName.Tap, position);
                    _lastTapTime = time;
                    _lastTapPosition = position;
                }
            }
        }

        _fingers[slot].Active = false;
        _activeTouchCount--;

        if (_activeTouchCount <= 0)
        {
            _activeTouchCount = 0;
            _multiTouchActive = false;
        }
    }

    private void HandleScreenDrag(InputEventScreenDrag drag)
    {
        int slot = GetSlotForFinger(drag.Index);
        if (slot < 0) return;

        _fingers[slot].CurrentPosition = drag.Position;

        // Two-finger gestures: pinch zoom + pan
        if (_activeTouchCount >= 2 && _fingers[0].Active && _fingers[1].Active)
        {
            Vector2 pos0 = _fingers[0].CurrentPosition;
            Vector2 pos1 = _fingers[1].CurrentPosition;

            float currentDistance = pos0.DistanceTo(pos1);
            Vector2 currentMidpoint = (pos0 + pos1) * 0.5f;

            // Pinch zoom
            if (_previousPinchDistance > 0.01f)
            {
                float zoomDelta = (currentDistance - _previousPinchDistance) / _previousPinchDistance;
                if (Math.Abs(zoomDelta) > 0.001f)
                {
                    EmitSignal(SignalName.PinchZoom, zoomDelta);
                }
            }

            // Two-finger pan
            Vector2 panDelta = currentMidpoint - _previousMidpoint;
            if (panDelta.LengthSquared() > 0.01f)
            {
                EmitSignal(SignalName.TwoFingerPan, panDelta);
            }

            _previousPinchDistance = currentDistance;
            _previousMidpoint = currentMidpoint;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private int GetSlotForFinger(int fingerIndex)
    {
        if (_fingers[0].Active && _fingers[0].Index == fingerIndex) return 0;
        if (_fingers[1].Active && _fingers[1].Index == fingerIndex) return 1;
        return -1;
    }
}
