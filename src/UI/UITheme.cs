using Godot;

namespace UnnamedRTS.UI;

/// <summary>
/// Static helper that creates consistent StyleBoxFlat, fonts, and colors for all UI elements.
/// Dark military sci-fi palette — "Cordite Wars" aesthetic.
/// </summary>
public static class UITheme
{
    // ── Color Palette ────────────────────────────────────────────────────
    public static readonly Color Background      = new("#0D0D12");
    public static readonly Color Surface         = new("#1A1A24");
    public static readonly Color SurfaceHover    = new("#252535");
    public static readonly Color Border          = new("#2A2A3A");
    public static readonly Color BorderHighlight = new("#3A5A7A");
    public static readonly Color TextPrimary     = new("#E0E0E8");
    public static readonly Color TextSecondary   = new("#8888A0");
    public static readonly Color TextMuted       = new("#555570");
    public static readonly Color Accent          = new("#4A9ECC");
    public static readonly Color AccentHover     = new("#5BB8E8");
    public static readonly Color AccentWarm      = new("#CC8844");
    public static readonly Color ErrorColor      = new("#CC4444");
    public static readonly Color SuccessColor    = new("#44AA44");

    // ── Faction Colors ───────────────────────────────────────────────────
    public static readonly Color FactionValkyr    = new("#2196F3");
    public static readonly Color FactionKragmore  = new("#F44336");
    public static readonly Color FactionBastion   = new("#FFC107");
    public static readonly Color FactionArcloft   = new("#00BCD4");
    public static readonly Color FactionIronmarch = new("#4CAF50");
    public static readonly Color FactionStormrend = new("#9C27B0");

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

    public static Color GetFactionColorById(string factionId)
    {
        for (int i = 0; i < FactionIds.Length; i++)
        {
            if (FactionIds[i] == factionId)
                return GetFactionColor(i);
        }
        return Accent;
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
