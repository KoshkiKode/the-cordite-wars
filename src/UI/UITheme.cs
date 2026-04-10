using Godot;

namespace CorditeWars.UI;

/// <summary>
/// Static helper that creates consistent StyleBoxFlat, fonts, and colors for all UI elements.
/// Dark military sci-fi palette — "Cordite Wars" aesthetic.
/// </summary>
public static class UITheme
{
    // ── Color Palette (mutable for accessibility contrast modes) ────────
    public static Color Background      = new("#0D0D12");
    public static Color Surface         = new("#1A1A24");
    public static Color SurfaceHover    = new("#252535");
    public static Color Border          = new("#2A2A3A");
    public static Color BorderHighlight = new("#3A5A7A");
    public static Color TextPrimary     = new("#E0E0E8");
    public static Color TextSecondary   = new("#8888A0");
    public static Color TextMuted       = new("#555570");
    public static Color Accent          = new("#4A9ECC");
    public static Color AccentHover     = new("#5BB8E8");
    public static Color AccentWarm      = new("#CC8844");
    public static Color ErrorColor      = new("#CC4444");
    public static Color SuccessColor    = new("#44AA44");

    /// <summary>
    /// Replace the entire UI color palette. Called by <see cref="AccessibilitySettings"/>
    /// when contrast mode changes.
    /// </summary>
    public static void SetPalette(
        Color background, Color surface, Color surfaceHover,
        Color border, Color borderHighlight,
        Color textPrimary, Color textSecondary, Color textMuted,
        Color accent, Color accentHover, Color accentWarm,
        Color errorColor, Color successColor)
    {
        Background      = background;
        Surface         = surface;
        SurfaceHover    = surfaceHover;
        Border          = border;
        BorderHighlight = borderHighlight;
        TextPrimary     = textPrimary;
        TextSecondary   = textSecondary;
        TextMuted       = textMuted;
        Accent          = accent;
        AccentHover     = accentHover;
        AccentWarm      = accentWarm;
        ErrorColor      = errorColor;
        SuccessColor    = successColor;
    }

    // ── Faction Colors ───────────────────────────────────────────────────
    // Each faction has three colors: Primary, Secondary (darker), Accent (bright trim)

    // Valkyr Command — sleek high-tech air force; electric cobalt and silver
    public static readonly Color FactionValkyr         = new("#2196F3"); // cobalt blue
    public static readonly Color FactionValkyrSecondary = new("#0D47A1"); // deep navy
    public static readonly Color FactionValkyrAccent    = new("#B3E5FC"); // ice blue

    // Kragmore Clans — heavy industrial ground force; rust red and iron grey
    public static readonly Color FactionKragmore         = new("#D32F2F"); // rust red
    public static readonly Color FactionKragmoreSecondary = new("#4E342E"); // dark iron
    public static readonly Color FactionKragmoreAccent    = new("#FF8A65"); // burnt orange

    // Bastion Republic — fortified and armoured defenders; amber gold and stone grey
    public static readonly Color FactionBastion         = new("#F9A825"); // amber gold
    public static readonly Color FactionBastionSecondary = new("#5D4037"); // dark tan
    public static readonly Color FactionBastionAccent    = new("#FFF8E1"); // pale ivory

    // Arcloft Syndicate — stealth drone operators; teal cyan and gunmetal
    public static readonly Color FactionArcloft         = new("#00ACC1"); // teal cyan
    public static readonly Color FactionArcloftSecondary = new("#006064"); // deep teal
    public static readonly Color FactionArcloftAccent    = new("#B2EBF2"); // pale cyan

    // Ironmarch Union — engineering trench corps; olive green and khaki
    public static readonly Color FactionIronmarch         = new("#558B2F"); // olive green
    public static readonly Color FactionIronmarchSecondary = new("#1B5E20"); // dark forest
    public static readonly Color FactionIronmarchAccent    = new("#DCEDC8"); // pale sage

    // Stormrend Accord — lightning energy mercenaries; vivid purple and electric yellow
    public static readonly Color FactionStormrend         = new("#7B1FA2"); // deep purple
    public static readonly Color FactionStormrendSecondary = new("#F57F17"); // electric amber
    public static readonly Color FactionStormrendAccent    = new("#E1BEE7"); // pale lavender

    public static readonly string[] FactionIds = { "valkyr", "kragmore", "bastion", "arcloft", "ironmarch", "stormrend" };
    public static readonly string[] FactionNames = { "Valkyr", "Kragmore", "Bastion", "Arcloft", "Ironmarch", "Stormrend" };

    public static Color GetFactionColor(int index)
    {
        return index switch
        {
            0 => FactionValkyr,
            1 => FactionKragmore,
            2 => FactionBastion,
            3 => FactionArcloft,
            4 => FactionIronmarch,
            5 => FactionStormrend,
            _ => Accent
        };
    }

    public static Color GetFactionSecondaryColor(int index)
    {
        return index switch
        {
            0 => FactionValkyrSecondary,
            1 => FactionKragmoreSecondary,
            2 => FactionBastionSecondary,
            3 => FactionArcloftSecondary,
            4 => FactionIronmarchSecondary,
            5 => FactionStormrendSecondary,
            _ => Border
        };
    }

    public static Color GetFactionAccentColor(int index)
    {
        return index switch
        {
            0 => FactionValkyrAccent,
            1 => FactionKragmoreAccent,
            2 => FactionBastionAccent,
            3 => FactionArcloftAccent,
            4 => FactionIronmarchAccent,
            5 => FactionStormrendAccent,
            _ => AccentHover
        };
    }

    public static Color GetFactionColorById(string factionId)
    {
        for (int i = 0; i < FactionIds.Length; i++)
        {
            if (FactionIds[i] == factionId)
                return GetFactionColor(i);
        }
        return Accent;
    }

    public static Color GetFactionSecondaryColorById(string factionId)
    {
        for (int i = 0; i < FactionIds.Length; i++)
        {
            if (FactionIds[i] == factionId)
                return GetFactionSecondaryColor(i);
        }
        return Border;
    }

    public static Color GetFactionAccentColorById(string factionId)
    {
        for (int i = 0; i < FactionIds.Length; i++)
        {
            if (FactionIds[i] == factionId)
                return GetFactionAccentColor(i);
        }
        return AccentHover;
    }

    // ── Fonts ────────────────────────────────────────────────────────────

    public static int FontSizeTitle     = 48;
    public static int FontSizeSubtitle  = 28;
    public static int FontSizeHeading   = 24;
    public static int FontSizeLarge     = 20;
    public static int FontSizeNormal    = 16;
    public static int FontSizeSmall     = 14;

    // ── StyleBox Factories ───────────────────────────────────────────────

    public static StyleBoxFlat MakePanel()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Surface;
        sb.BorderColor = Border;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(12);
        return sb;
    }

    public static StyleBoxFlat MakePanelNoBorder()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Surface;
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(12);
        return sb;
    }

    public static StyleBoxFlat MakeBackground()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Background;
        return sb;
    }

    public static StyleBoxFlat MakeButtonNormal()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Surface;
        sb.BorderColor = Border;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(8);
        sb.ContentMarginLeft = 20;
        sb.ContentMarginRight = 20;
        return sb;
    }

    public static StyleBoxFlat MakeButtonHover()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = SurfaceHover;
        sb.BorderColor = BorderHighlight;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(8);
        sb.ContentMarginLeft = 20;
        sb.ContentMarginRight = 20;
        return sb;
    }

    public static StyleBoxFlat MakeButtonPressed()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Accent;
        sb.BorderColor = AccentHover;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(8);
        sb.ContentMarginLeft = 20;
        sb.ContentMarginRight = 20;
        return sb;
    }

    public static StyleBoxFlat MakeAccentButtonNormal()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Accent;
        sb.BorderColor = AccentHover;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(10);
        sb.ContentMarginLeft = 24;
        sb.ContentMarginRight = 24;
        return sb;
    }

    public static StyleBoxFlat MakeAccentButtonHover()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = AccentHover;
        sb.BorderColor = new Color(AccentHover, 1.0f);
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(10);
        sb.ContentMarginLeft = 24;
        sb.ContentMarginRight = 24;
        return sb;
    }

    public static StyleBoxFlat MakeAccentButtonPressed()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = new Color(Accent.R * 0.8f, Accent.G * 0.8f, Accent.B * 0.8f, 1.0f);
        sb.BorderColor = Accent;
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(10);
        sb.ContentMarginLeft = 24;
        sb.ContentMarginRight = 24;
        return sb;
    }

    public static StyleBoxFlat MakeProgressBarBg()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = new Color("#0A0A10");
        sb.BorderColor = Border;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        return sb;
    }

    public static StyleBoxFlat MakeProgressBarFill()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Accent;
        sb.SetCornerRadiusAll(4);
        return sb;
    }

    public static StyleBoxFlat MakeTabNormal()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Background;
        sb.BorderColor = Border;
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(0);
        sb.CornerRadiusTopLeft = 4;
        sb.CornerRadiusTopRight = 4;
        sb.SetContentMarginAll(8);
        sb.ContentMarginLeft = 16;
        sb.ContentMarginRight = 16;
        return sb;
    }

    public static StyleBoxFlat MakeTabSelected()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Surface;
        sb.BorderColor = Accent;
        sb.BorderWidthTop = 2;
        sb.BorderWidthLeft = 1;
        sb.BorderWidthRight = 1;
        sb.BorderWidthBottom = 0;
        sb.SetCornerRadiusAll(0);
        sb.CornerRadiusTopLeft = 4;
        sb.CornerRadiusTopRight = 4;
        sb.SetContentMarginAll(8);
        sb.ContentMarginLeft = 16;
        sb.ContentMarginRight = 16;
        return sb;
    }

    public static StyleBoxFlat MakeSliderBg()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = new Color("#0A0A10");
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginTop = 4;
        sb.ContentMarginBottom = 4;
        return sb;
    }

    public static StyleBoxFlat MakeSliderFill()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = Accent;
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginTop = 4;
        sb.ContentMarginBottom = 4;
        return sb;
    }

    public static StyleBoxFlat MakeSliderGrabber()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = TextPrimary;
        sb.SetCornerRadiusAll(8);
        return sb;
    }

    public static StyleBoxFlat MakeFactionCard(Color factionColor, bool selected)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = selected ? new Color(factionColor.R, factionColor.G, factionColor.B, 0.15f) : Surface;
        sb.BorderColor = selected ? factionColor : Border;
        sb.SetBorderWidthAll(selected ? 2 : 1);
        sb.SetCornerRadiusAll(6);
        sb.SetContentMarginAll(12);
        return sb;
    }

    // ── Styling Helpers ──────────────────────────────────────────────────

    public static void StyleButton(Button btn)
    {
        btn.AddThemeStyleboxOverride("normal", MakeButtonNormal());
        btn.AddThemeStyleboxOverride("hover", MakeButtonHover());
        btn.AddThemeStyleboxOverride("pressed", MakeButtonPressed());
        btn.AddThemeStyleboxOverride("focus", MakeButtonHover());
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", AccentHover);
        btn.AddThemeColorOverride("font_pressed_color", TextPrimary);
        btn.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    public static void StyleAccentButton(Button btn)
    {
        btn.AddThemeStyleboxOverride("normal", MakeAccentButtonNormal());
        btn.AddThemeStyleboxOverride("hover", MakeAccentButtonHover());
        btn.AddThemeStyleboxOverride("pressed", MakeAccentButtonPressed());
        btn.AddThemeStyleboxOverride("focus", MakeAccentButtonHover());
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", TextPrimary);
        btn.AddThemeColorOverride("font_pressed_color", TextPrimary);
        btn.AddThemeFontSizeOverride("font_size", FontSizeLarge);
    }

    public static void StyleMenuButton(Button btn)
    {
        var normal = MakeButtonNormal();
        normal.ContentMarginTop = 12;
        normal.ContentMarginBottom = 12;
        normal.ContentMarginLeft = 28;
        normal.ContentMarginRight = 28;

        var hover = MakeButtonHover();
        hover.ContentMarginTop = 12;
        hover.ContentMarginBottom = 12;
        hover.ContentMarginLeft = 32;
        hover.ContentMarginRight = 28;
        hover.BorderColor = Accent;

        var pressed = MakeButtonPressed();
        pressed.ContentMarginTop = 12;
        pressed.ContentMarginBottom = 12;
        pressed.ContentMarginLeft = 32;
        pressed.ContentMarginRight = 28;

        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeStyleboxOverride("focus", hover);
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", AccentHover);
        btn.AddThemeColorOverride("font_pressed_color", TextPrimary);
        btn.AddThemeFontSizeOverride("font_size", FontSizeHeading);
    }

    public static void StyleLabel(Label lbl, int fontSize, Color color)
    {
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", color);
    }

    public static void StyleSlider(HSlider slider)
    {
        slider.AddThemeStyleboxOverride("slider", MakeSliderBg());
        slider.AddThemeStyleboxOverride("grabber_area", MakeSliderFill());
        slider.AddThemeStyleboxOverride("grabber_area_highlight", MakeSliderFill());
    }

    public static void StyleProgressBar(ProgressBar bar)
    {
        bar.AddThemeStyleboxOverride("background", MakeProgressBarBg());
        bar.AddThemeStyleboxOverride("fill", MakeProgressBarFill());
    }

    public static void StyleCheckBox(CheckBox cb)
    {
        cb.AddThemeColorOverride("font_color", TextPrimary);
        cb.AddThemeColorOverride("font_hover_color", AccentHover);
        cb.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    public static void StyleOptionButton(OptionButton opt)
    {
        opt.AddThemeStyleboxOverride("normal", MakeButtonNormal());
        opt.AddThemeStyleboxOverride("hover", MakeButtonHover());
        opt.AddThemeStyleboxOverride("pressed", MakeButtonPressed());
        opt.AddThemeStyleboxOverride("focus", MakeButtonHover());
        opt.AddThemeColorOverride("font_color", TextPrimary);
        opt.AddThemeColorOverride("font_hover_color", AccentHover);
        opt.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    public static void StyleTabContainer(TabContainer tabs)
    {
        tabs.AddThemeStyleboxOverride("tab_unselected", MakeTabNormal());
        tabs.AddThemeStyleboxOverride("tab_selected", MakeTabSelected());
        tabs.AddThemeStyleboxOverride("tab_hovered", MakeTabSelected());
        tabs.AddThemeStyleboxOverride("panel", MakePanel());
        tabs.AddThemeColorOverride("font_selected_color", Accent);
        tabs.AddThemeColorOverride("font_unselected_color", TextSecondary);
        tabs.AddThemeColorOverride("font_hovered_color", AccentHover);
        tabs.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }
}
