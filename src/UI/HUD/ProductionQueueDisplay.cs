using Godot;
using System.Collections.Generic;
using UnnamedRTS.Core;
using UnnamedRTS.Game.Buildings;
using UnnamedRTS.UI.Input;

namespace UnnamedRTS.UI.HUD;

/// <summary>
/// Shows current production queue for selected building.
/// Progress bar on current unit, queue icons with cancel buttons.
/// </summary>
public partial class ProductionQueueDisplay : PanelContainer
{
    private SelectionManager? _selectionManager;

    // Tracked building production queue
    private ProductionQueue? _trackedQueue;

    // UI elements
    private VBoxContainer? _content;
    private Label? _titleLabel;
    private ProgressBar? _progressBar;
    private Label? _progressLabel;
    private HBoxContainer? _queueIcons;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(SelectionManager selectionManager)
    {
        _selectionManager = selectionManager;
        Name = "ProductionQueueDisplay";

        // Position above command card (bottom-right, higher up)
        AnchorLeft = 1;
        AnchorTop = 1;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = -260;
        OffsetTop = -240;
        OffsetRight = -8;
        OffsetBottom = -185;

        CustomMinimumSize = new Vector2(250, 50);

        // Dark panel
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.102f, 0.102f, 0.141f, 0.90f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.165f, 0.165f, 0.227f);
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", style);

        BuildUI();

        Visible = false;
    }

    private void BuildUI()
    {
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        AddChild(_content);

        _titleLabel = new Label();
        _titleLabel.Text = "Production";
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _content.AddChild(_titleLabel);

        // Progress bar for current production
        var progressContainer = new HBoxContainer();
        progressContainer.AddThemeConstantOverride("separation", 8);
        _content.AddChild(progressContainer);

        _progressBar = new ProgressBar();
        _progressBar.CustomMinimumSize = new Vector2(160, 14);
        _progressBar.MaxValue = 100;
        _progressBar.ShowPercentage = false;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.165f, 0.165f, 0.227f);
        _progressBar.AddThemeStyleboxOverride("background", bgStyle);

        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = new Color(0.29f, 0.62f, 0.80f); // Accent #4A9ECC
        _progressBar.AddThemeStyleboxOverride("fill", fillStyle);
        progressContainer.AddChild(_progressBar);

        _progressLabel = new Label();
        _progressLabel.AddThemeColorOverride("font_color", new Color(0.533f, 0.533f, 0.627f));
        _progressLabel.AddThemeFontSizeOverride("font_size", 12);
        progressContainer.AddChild(_progressLabel);

        // Queue icons row
        _queueIcons = new HBoxContainer();
        _queueIcons.AddThemeConstantOverride("separation", 4);
        _content.AddChild(_queueIcons);
    }

    // ── Public API ───────────────────────────────────────────────────

    public void TrackQueue(ProductionQueue? queue)
    {
        _trackedQueue = queue;
        Visible = queue is not null;
    }

    // ── Update ───────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_trackedQueue is null)
        {
            Visible = false;
            return;
        }

        if (!_trackedQueue.IsProducing && _trackedQueue.QueueCount == 0)
        {
            Visible = false;
            return;
        }

        Visible = true;

        // Update progress bar
        if (_trackedQueue.IsProducing)
        {
            float percent = _trackedQueue.ProgressPercent * 100f;
            _progressBar!.Value = percent;
            _titleLabel!.Text = $"Building: {_trackedQueue.CurrentUnitTypeId}";
            _progressLabel!.Text = $"{(int)percent}%";
        }
        else
        {
            _progressBar!.Value = 0;
            _titleLabel!.Text = "Production";
            _progressLabel!.Text = string.Empty;
        }

        // Update queue icons
        UpdateQueueIcons();
    }

    private void UpdateQueueIcons()
    {
        if (_queueIcons is null || _trackedQueue is null) return;

        // Clear existing
        for (int i = _queueIcons.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _queueIcons.GetChild(i);
            _queueIcons.RemoveChild(child);
            child.QueueFree();
        }

        // Add queue entries
        var queued = _trackedQueue.GetQueuedUnitTypes();
        for (int i = 0; i < queued.Count; i++)
        {
            var btn = new Button();
            btn.Text = queued[i].Length > 4 ? queued[i][..4] : queued[i];
            btn.CustomMinimumSize = new Vector2(40, 24);
            btn.AddThemeColorOverride("font_color", new Color(0.878f, 0.878f, 0.910f));
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.TooltipText = $"Cancel {queued[i]}";

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = new Color(0.102f, 0.102f, 0.141f);
            normalStyle.BorderWidthBottom = 1;
            normalStyle.BorderColor = new Color(0.165f, 0.165f, 0.227f);
            btn.AddThemeStyleboxOverride("normal", normalStyle);

            int cancelIndex = i;
            btn.Pressed += () => _trackedQueue.RemoveFromQueue(cancelIndex);

            _queueIcons.AddChild(btn);
        }
    }
}
