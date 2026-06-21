using Godot;

namespace Embervale.UI;

/// <summary>
/// One place for the look-and-feel of the (still debug-grade) overlay UI: a shared
/// palette plus small builders for the controls every panel uses — a framed panel, a
/// header, a body line, an action button and a coloured resource bar. Centralising it
/// here keeps the HUD, character screen, journal and dialogue window visually
/// consistent and makes a later full UI pass a matter of changing one file.
/// </summary>
public static class UiTheme
{
    // --- Palette ------------------------------------------------------------
    public static readonly Color PanelBg = new(0.07f, 0.08f, 0.11f, 0.90f);
    public static readonly Color PanelBorder = new(0.30f, 0.34f, 0.44f, 0.85f);
    public static readonly Color Accent = new(0.95f, 0.82f, 0.42f); // gold headers
    public static readonly Color Text = new(0.88f, 0.90f, 0.94f);
    public static readonly Color Dim = new(0.60f, 0.64f, 0.72f);
    public static readonly Color Good = new(0.55f, 0.80f, 0.55f);
    public static readonly Color Bad = new(0.86f, 0.46f, 0.46f);

    // Resource bar fills.
    public static readonly Color Health = new(0.80f, 0.28f, 0.30f);
    public static readonly Color Stamina = new(0.78f, 0.66f, 0.28f);
    public static readonly Color Mana = new(0.34f, 0.55f, 0.86f);

    public const int HeaderFontSize = 16;
    public const int BodyFontSize = 14;

    // --- Builders -----------------------------------------------------------

    /// <summary>A framed, semi-transparent panel with rounded corners.</summary>
    public static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        return panel;
    }

    /// <summary>The standard inner padding container panels wrap their content in.</summary>
    public static MarginContainer Padding(int amount = 10)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", amount + 2);
        margin.AddThemeConstantOverride("margin_right", amount + 2);
        margin.AddThemeConstantOverride("margin_top", amount);
        margin.AddThemeConstantOverride("margin_bottom", amount);
        return margin;
    }

    public static Label Header(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", HeaderFontSize);
        label.AddThemeColorOverride("font_color", Accent);
        return label;
    }

    public static Label Body(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    public static Button Action(string text)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.16f, 0.18f, 0.23f, 0.95f)));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.22f, 0.25f, 0.31f, 0.98f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.12f, 0.14f, 0.18f, 0.98f)));
        return button;
    }

    /// <summary>A thin coloured resource bar (0..1) with a dark trough.</summary>
    public static ProgressBar Bar(Color fill, float width = 168f)
    {
        var bar = new ProgressBar
        {
            MinValue = 0d,
            MaxValue = 1d,
            Value = 1d,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, 13f),
        };
        bar.AddThemeStyleboxOverride("background", BarStyle(new Color(0.14f, 0.15f, 0.18f, 0.95f)));
        bar.AddThemeStyleboxOverride("fill", BarStyle(fill));
        return bar;
    }

    // --- Style boxes --------------------------------------------------------

    private static StyleBoxFlat PanelStyle()
    {
        var box = new StyleBoxFlat { BgColor = PanelBg, BorderColor = PanelBorder };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(6);
        return box;
    }

    private static StyleBoxFlat ButtonStyle(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(4);
        box.SetContentMarginAll(4);
        box.ContentMarginLeft = 9;
        box.ContentMarginRight = 9;
        return box;
    }

    private static StyleBoxFlat BarStyle(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(3);
        return box;
    }
}
