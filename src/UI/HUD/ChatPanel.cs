using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.UI;

namespace CorditeWars.UI.HUD;

/// <summary>
/// In-game chat overlay. Shows a scrollable message log and a text-entry
/// field for typing new messages. Supports player-name and team-color
/// formatting.
///
/// Press Enter to open/send a message. Press Escape to dismiss.
/// Chat messages are also published via <see cref="EventBus.ChatMessageReceived"/>
/// so a NetworkManager can relay them in multiplayer.
/// </summary>
public partial class ChatPanel : CanvasLayer
{
    // ── Configuration ────────────────────────────────────────────────

    private const int MaxMessages = 50;
    private const float MessageFadeSeconds = 12f;

    // ── State ────────────────────────────────────────────────────────

    private int _localPlayerId;
    private string _localPlayerName = "Commander";
    private Color _localPlayerColor = new Color(0.3f, 0.75f, 1f);

    // ── UI nodes ─────────────────────────────────────────────────────

    private ScrollContainer? _scrollContainer;
    private VBoxContainer? _messageBox;
    private LineEdit? _inputLine;
    private PanelContainer? _inputPanel;
    private bool _inputOpen;

    private readonly List<(Label label, double spawnTime)> _messages = new();

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(int localPlayerId, string playerName, Color playerColor)
    {
        _localPlayerId   = localPlayerId;
        _localPlayerName = playerName;
        _localPlayerColor = playerColor;

        Name  = "ChatPanel";
        Layer = 20;

        BuildUI();

        // Subscribe to incoming chat messages (e.g., from network relay)
        EventBus.Instance?.Connect(EventBus.SignalName.ChatMessageReceived,
            Callable.From<int, string, string>(OnChatMessageReceived));
    }

    // ── Godot lifecycle ──────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (!_inputOpen && keyEvent.Keycode == Key.Enter)
            {
                OpenInput();
                GetViewport().SetInputAsHandled();
            }
            else if (_inputOpen && keyEvent.Keycode == Key.Escape)
            {
                CloseInput();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        // Fade out old messages that aren't in the active scroll log
        // (Only visible in non-input mode — when input opens all are visible)
        if (_inputOpen) return;
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var (label, spawnTime) in _messages)
        {
            double age = now - spawnTime;
            if (age > MessageFadeSeconds)
            {
                float alpha = System.MathF.Max(0f, 1f - (float)(age - MessageFadeSeconds));
                label.Modulate = new Color(1, 1, 1, alpha);
            }
            else
            {
                label.Modulate = Colors.White;
            }
        }
    }

    // ── UI Construction ──────────────────────────────────────────────

    private void BuildUI()
    {
        // Message log — bottom-left above minimap, only visible 200px wide
        var logContainer = new PanelContainer();
        logContainer.AnchorLeft   = 0;
        logContainer.AnchorTop    = 1;
        logContainer.AnchorRight  = 0;
        logContainer.AnchorBottom = 1;
        logContainer.OffsetLeft   = 8;
        logContainer.OffsetRight  = 280;
        logContainer.OffsetTop    = -380;
        logContainer.OffsetBottom = -168; // above minimap
        var logBg = new StyleBoxFlat();
        logBg.BgColor = new Color(0, 0, 0, 0); // transparent by default
        logContainer.AddThemeStyleboxOverride("panel", logBg);
        AddChild(logContainer);

        _scrollContainer = new ScrollContainer();
        _scrollContainer.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        logContainer.AddChild(_scrollContainer);

        _messageBox = new VBoxContainer();
        _messageBox.AddThemeConstantOverride("separation", 2);
        _messageBox.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        _scrollContainer.AddChild(_messageBox);

        // Input panel — hidden until Enter is pressed
        _inputPanel = new PanelContainer();
        _inputPanel.AnchorLeft   = 0;
        _inputPanel.AnchorTop    = 1;
        _inputPanel.AnchorRight  = 0;
        _inputPanel.AnchorBottom = 1;
        _inputPanel.OffsetLeft   = 8;
        _inputPanel.OffsetRight  = 360;
        _inputPanel.OffsetTop    = -132;
        _inputPanel.OffsetBottom = -104;
        var inputBg = UITheme.MakePanel();
        _inputPanel.AddThemeStyleboxOverride("panel", inputBg);
        _inputPanel.Visible = false;
        AddChild(_inputPanel);

        var inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 6);
        _inputPanel.AddChild(inputRow);

        var chatLabel = new Label { Text = "Say: " };
        UITheme.StyleLabel(chatLabel, UITheme.FontSizeSmall, UITheme.Accent);
        inputRow.AddChild(chatLabel);

        _inputLine = new LineEdit();
        _inputLine.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        _inputLine.PlaceholderText = "Type message and press Enter…";
        _inputLine.AddThemeFontSizeOverride("font_size", UITheme.FontSizeSmall);
        _inputLine.TextSubmitted += OnTextSubmitted;
        inputRow.AddChild(_inputLine);
    }

    // ── Input Handling ───────────────────────────────────────────────

    private void OpenInput()
    {
        _inputOpen = true;
        _inputPanel!.Visible = true;
        _inputLine?.Clear();
        _inputLine?.GrabFocus();

        // Make message log background visible when typing
        if (_scrollContainer?.GetParent() is PanelContainer lc)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0, 0, 0, 0.6f);
            style.SetCornerRadiusAll(4);
            lc.AddThemeStyleboxOverride("panel", style);
        }

        // Show all messages (un-faded) while input is open
        foreach (var (label, _) in _messages)
            label.Modulate = Colors.White;
    }

    private void CloseInput()
    {
        _inputOpen = false;
        _inputPanel!.Visible = false;

        // Restore transparent bg
        if (_scrollContainer?.GetParent() is PanelContainer lc)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0, 0, 0, 0);
            lc.AddThemeStyleboxOverride("panel", style);
        }
    }

    private void OnTextSubmitted(string text)
    {
        string trimmed = text.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            // Post locally
            PostLocalMessage(_localPlayerName, trimmed, _localPlayerColor);

            // Broadcast to other players via EventBus (network relay picks it up)
            EventBus.Instance?.EmitChatMessageSent(_localPlayerId, _localPlayerName, trimmed);
        }
        CloseInput();
    }

    // ── Message Rendering ────────────────────────────────────────────

    private void OnChatMessageReceived(int senderId, string senderName, string message)
    {
        // Don't echo our own message (already posted in OnTextSubmitted)
        if (senderId == _localPlayerId) return;

        // Pick a colour per sender ID (consistent but distinct)
        Color color = senderId switch
        {
            2 => new Color(1f, 0.65f, 0.1f),   // orange
            3 => new Color(0.5f, 1f, 0.5f),    // green
            4 => new Color(1f, 0.4f, 0.4f),    // red
            _ => UITheme.TextSecondary
        };
        PostLocalMessage(senderName, message, color);
    }

    private void PostLocalMessage(string sender, string text, Color nameColor)
    {
        if (_messageBox is null) return;

        var lbl = new Label();
        lbl.Text = $"[{sender}] {text}";
        lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lbl.AddThemeFontSizeOverride("font_size", UITheme.FontSizeSmall);
        lbl.AddThemeColorOverride("font_color", UITheme.TextPrimary);
        _messageBox.AddChild(lbl);

        double now = Time.GetTicksMsec() / 1000.0;
        _messages.Add((lbl, now));

        // Trim old messages
        while (_messages.Count > MaxMessages)
        {
            var (old, _) = _messages[0];
            _messages.RemoveAt(0);
            old.QueueFree();
        }

        // Scroll to bottom
        CallDeferred(MethodName._ScrollToBottom);
    }

    private void _ScrollToBottom()
    {
        if (_scrollContainer is null) return;
        _scrollContainer.ScrollVertical = (int)_scrollContainer.GetVScrollBar().MaxValue;
    }
}
