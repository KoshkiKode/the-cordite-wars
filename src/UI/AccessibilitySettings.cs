using Godot;

namespace UnnamedRTS.UI;

/// <summary>
/// Manages accessibility settings: high contrast mode and UI scale adjustments.
/// Persists to user://settings.cfg under [Accessibility].
/// </summary>
public sealed class AccessibilitySettings
{
    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "Accessibility";

    public static AccessibilitySettings? Instance { get; private set; }

    // ── Contrast Modes ──────────────────────────────────────────────

    public enum ContrastMode
    {
        /// <summary>Default dark sci-fi palette.</summary>
        Normal,
        /// <summary>Higher contrast: brighter text, stronger borders, bolder accents.</summary>
        High,
        /// <summary>Maximum contrast: near-white text on near-black, vivid accent colors.</summary>
        Maximum,
    }

    public static readonly string[] ContrastModeNames = { "Normal", "High", "Maximum" };

    // ── State ────────────────────────────────────────────────────────

    private ContrastMode _contrastMode = ContrastMode.Normal;

    /// <summary>Current contrast mode. Changing this fires <see cref="ContrastModeChanged"/>.</summary>
    public ContrastMode CurrentContrastMode
    {
        get => _contrastMode;
        set
        {
            if (_contrastMode == value) return;
            _contrastMode = value;
            ApplyContrastMode();
            ContrastModeChanged?.Invoke(_contrastMode);
        }
    }

    /// <summary>Raised when the contrast mode changes. Listeners should refresh their colors.</summary>
    public event System.Action<ContrastMode>? ContrastModeChanged;

    // ── Lifecycle ────────────────────────────────────────────────────

    public AccessibilitySettings()
    {
        Instance = this;
        Load();
        ApplyContrastMode();
    }

    // ── High Contrast Color Overrides ────────────────────────────────

    /// <summary>
    /// Applies the current contrast palette to <see cref="UITheme"/> static fields.
    /// Callers should re-style their UI after this runs.
    /// </summary>
    private void ApplyContrastMode()
    {
        switch (_contrastMode)
        {
            case ContrastMode.Normal:
                UITheme.SetPalette(
                    background:      new Color("#0D0D12"),
                    surface:         new Color("#1A1A24"),
                    surfaceHover:    new Color("#252535"),
                    border:          new Color("#2A2A3A"),
                    borderHighlight: new Color("#3A5A7A"),
                    textPrimary:     new Color("#E0E0E8"),
                    textSecondary:   new Color("#8888A0"),
                    textMuted:       new Color("#555570"),
                    accent:          new Color("#4A9ECC"),
                    accentHover:     new Color("#5BB8E8"),
                    accentWarm:      new Color("#CC8844"),
                    errorColor:      new Color("#CC4444"),
                    successColor:    new Color("#44AA44"));
                break;

            case ContrastMode.High:
                UITheme.SetPalette(
                    background:      new Color("#000000"),
                    surface:         new Color("#121218"),
                    surfaceHover:    new Color("#1E1E2E"),
                    border:          new Color("#4A4A6A"),
                    borderHighlight: new Color("#5A8ABB"),
                    textPrimary:     new Color("#F5F5FF"),
                    textSecondary:   new Color("#B0B0CC"),
                    textMuted:       new Color("#7878A0"),
                    accent:          new Color("#55BBEE"),
                    accentHover:     new Color("#77DDFF"),
                    accentWarm:      new Color("#EEAA55"),
                    errorColor:      new Color("#FF5555"),
                    successColor:    new Color("#55DD55"));
                break;

            case ContrastMode.Maximum:
                UITheme.SetPalette(
                    background:      new Color("#000000"),
                    surface:         new Color("#0A0A0A"),
                    surfaceHover:    new Color("#1A1A1A"),
                    border:          new Color("#FFFFFF"),
                    borderHighlight: new Color("#FFFFFF"),
                    textPrimary:     new Color("#FFFFFF"),
                    textSecondary:   new Color("#DDDDDD"),
                    textMuted:       new Color("#AAAAAA"),
                    accent:          new Color("#00DDFF"),
                    accentHover:     new Color("#44EEFF"),
                    accentWarm:      new Color("#FFCC00"),
                    errorColor:      new Color("#FF0000"),
                    successColor:    new Color("#00FF00"));
                break;
        }

        GD.Print($"[Accessibility] Contrast mode set to {_contrastMode}.");
    }

    // ── High Contrast Selection Box Colors ──────────────────────────

    /// <summary>
    /// Returns (fillColor, borderColor) for the box-select overlay,
    /// adjusted for the current contrast mode.
    /// </summary>
    public (Color fill, Color border) GetSelectionBoxColors()
    {
        return _contrastMode switch
        {
            ContrastMode.High => (
                new Color(0.33f, 0.73f, 1.0f, 0.25f),
                new Color(0.33f, 0.73f, 1.0f, 1.0f)),
            ContrastMode.Maximum => (
                new Color(1.0f, 1.0f, 0.0f, 0.3f),
                new Color(1.0f, 1.0f, 0.0f, 1.0f)),
            _ => (
                new Color(0.29f, 0.62f, 0.80f, 0.15f),
                new Color(0.29f, 0.62f, 0.80f, 0.8f)),
        };
    }

    // ── Persistence ──────────────────────────────────────────────────

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);

        cfg.SetValue(Section, "contrast_mode", (int)_contrastMode);

        var err = cfg.Save(SettingsPath);
        if (err != Error.Ok)
            GD.PrintErr($"[Accessibility] Failed to save: {err}");
    }

    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return;

        if (cfg.HasSectionKey(Section, "contrast_mode"))
            _contrastMode = (ContrastMode)(int)cfg.GetValue(Section, "contrast_mode", 0);
    }
}
