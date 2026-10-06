using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The skin for the controls Godot draws itself: sliders, check buttons and boxes, line edits,
/// scrollbars, popup menus, tooltips and the default font. Everything <see cref="UiTheme"/> builds
/// is styled by per-control overrides; these were the pieces left wearing the engine's grey, and
/// two of them (the dropdown's popup and every tooltip) are separate windows no override reaches.
///
/// It is one <see cref="Theme"/> built in code from <see cref="UiTheme"/> tokens, used two ways:
/// <list type="bullet">
/// <item><see cref="Install"/> merges it into the engine's default theme, which is what popups,
/// tooltips and any control nobody styled resolve against. A project theme on the root would not
/// do: theme inheritance stops at a <see cref="CanvasLayer"/>, and every screen here is one.</item>
/// <item><see cref="Apply{T}"/> hands the same theme to one control. The <c>Slider</c>,
/// <c>Toggle</c>, <c>Dropdown</c> and <c>ScrollList</c> builders do this, so those controls hold
/// the look even if the default-theme merge does not land.</item>
/// </list>
///
/// The skin depends on two settings (high contrast, text scale). <see cref="Install"/> is called
/// again whenever settings are applied and rebuilds only when one of those moved: a rebuild
/// notifies every control in the tree, and settings are applied on every tick of a slider drag.
///
/// The icons under <c>assets/ui/icons/controls/</c> carry their colours (a theme icon cannot be
/// tinted), in the palette's ember and bone. A missing icon leaves the engine's own in place.
/// </summary>
public static class UiSkin
{
    private const string IconFolder = "res://assets/ui/icons/controls/";

    private static readonly Dictionary<string, Texture2D?> Icons = new();
    private static bool _warnedMissingIcon;

    private static Theme? _theme;
    private static bool _installed;
    private static bool _highContrast;
    private static int _bodySize;

    /// <summary>The skin as a theme, current for the player's settings.</summary>
    public static Theme Theme
    {
        get
        {
            Refresh();
            return _theme!;
        }
    }

    /// <summary>Puts the skin into the engine's default theme. Safe to call repeatedly; it does
    /// nothing when the skin has not changed since the last call.</summary>
    public static void Install()
    {
        bool changed = Refresh();
        if (_installed && !changed)
        {
            return;
        }

        if (ThemeDB.GetDefaultTheme() is not { } engine)
        {
            return;
        }

        engine.MergeWith(_theme);
        _installed = true;
    }

    /// <summary>Gives one control the skin directly, whatever the default theme holds.</summary>
    public static T Apply<T>(T control)
        where T : Control
    {
        control.Theme = Theme;
        return control;
    }

    /// <summary>As <see cref="Apply{T}"/> for a popup, which is a window and not a control.</summary>
    public static void Apply(Window window) => window.Theme = Theme;

    /// <summary>Rebuilds the skin if a setting it reads has moved. True when it was rebuilt.</summary>
    private static bool Refresh()
    {
        bool highContrast = UiTheme.HighContrast;
        int bodySize = UiTheme.FontSize(UiTheme.BodyFontSize);
        if (_theme != null && highContrast == _highContrast && bodySize == _bodySize)
        {
            return false;
        }

        _highContrast = highContrast;
        _bodySize = bodySize;

        // Built detached and merged across in one step, so the controls already holding the theme
        // hear about it twice (clear, merge) and not once per item.
        var fresh = new Theme();
        Fill(fresh);

        _theme ??= new Theme();
        _theme.Clear();
        _theme.MergeWith(fresh);
        return true;
    }

    private static void Fill(Theme theme)
    {
        int line = UiTheme.HighContrast ? 2 : 1;
        int bodySize = UiTheme.FontSize(UiTheme.BodyFontSize);
        Font? font = UiTheme.UiFont;
        if (font != null)
        {
            theme.DefaultFont = font;
        }

        StyleBoxFlat focus = Outline(UiTheme.FocusRing, line);

        // --- Slider: a cut trough, the filled length in ember, a diamond plate to hold ---
        StyleBoxFlat track = Flat(UiTheme.Trough, UiTheme.Iron, line);
        track.SetContentMarginAll(3f);
        StyleBoxFlat filled = Flat(UiTheme.Accent.Darkened(0.28f), UiTheme.Iron, line);
        filled.SetContentMarginAll(3f);
        StyleBoxFlat filledLit = Flat(UiTheme.Accent, UiTheme.Iron, line);
        filledLit.SetContentMarginAll(3f);

        theme.SetStylebox("slider", "HSlider", track);
        theme.SetStylebox("grabber_area", "HSlider", filled);
        theme.SetStylebox("grabber_area_highlight", "HSlider", filledLit);
        SetIcon(theme, "grabber", "HSlider", "slider_grabber");
        SetIcon(theme, "grabber_highlight", "HSlider", "slider_grabber_hot");
        SetIcon(theme, "grabber_disabled", "HSlider", "slider_grabber_disabled");

        // --- Check button (the settings switch) and check box ---
        foreach (string type in new[] { "CheckButton", "CheckBox" })
        {
            theme.SetColor("font_color", type, UiTheme.Text);
            theme.SetColor("font_pressed_color", type, UiTheme.Text);
            theme.SetColor("font_hover_color", type, UiTheme.Accent);
            theme.SetColor("font_hover_pressed_color", type, UiTheme.Accent);
            theme.SetColor("font_focus_color", type, UiTheme.Accent);
            theme.SetColor("font_disabled_color", type, UiTheme.Disabled);
            theme.SetStylebox("focus", type, focus);
            theme.SetFontSize("font_size", type, bodySize);
        }

        foreach (string suffix in new[] { string.Empty, "_mirrored" })
        {
            SetIcon(theme, "checked" + suffix, "CheckButton", "toggle_on");
            SetIcon(theme, "unchecked" + suffix, "CheckButton", "toggle_off");
            SetIcon(theme, "checked_disabled" + suffix, "CheckButton", "toggle_on_disabled");
            SetIcon(theme, "unchecked_disabled" + suffix, "CheckButton", "toggle_off_disabled");
        }

        // A popup menu draws the same four marks beside its checkable items, so they are set as one family.
        foreach (string type in new[] { "CheckBox", "PopupMenu" })
        {
            SetIcon(theme, "checked", type, "check_on");
            SetIcon(theme, "unchecked", type, "check_off");
            SetIcon(theme, "checked_disabled", type, "check_on_disabled");
            SetIcon(theme, "unchecked_disabled", type, "check_off_disabled");
            SetIcon(theme, "radio_checked", type, "radio_on");
            SetIcon(theme, "radio_unchecked", type, "radio_off");
            SetIcon(theme, "radio_checked_disabled", type, "radio_on_disabled");
            SetIcon(theme, "radio_unchecked_disabled", type, "radio_off_disabled");
        }

        // --- Line edit: an input well ---
        StyleBoxFlat field = UiTheme.WellStyle();
        field.SetContentMarginAll(UiTheme.SpaceXs);
        field.ContentMarginLeft = UiTheme.SpaceSm;
        field.ContentMarginRight = UiTheme.SpaceSm;
        StyleBoxFlat fieldLocked = UiTheme.WellStyle();
        fieldLocked.BgColor = UiTheme.PanelBg;
        fieldLocked.SetContentMarginAll(UiTheme.SpaceXs);
        fieldLocked.ContentMarginLeft = UiTheme.SpaceSm;
        fieldLocked.ContentMarginRight = UiTheme.SpaceSm;

        theme.SetStylebox("normal", "LineEdit", field);
        theme.SetStylebox("read_only", "LineEdit", fieldLocked);
        theme.SetStylebox("focus", "LineEdit", focus);
        theme.SetColor("font_color", "LineEdit", UiTheme.Text);
        theme.SetColor("font_uneditable_color", "LineEdit", UiTheme.Dim);
        theme.SetColor("font_placeholder_color", "LineEdit", UiTheme.Dim);
        theme.SetColor("font_selected_color", "LineEdit", UiTheme.Text);
        theme.SetColor("selection_color", "LineEdit", UiTheme.Accent with { A = 0.35f });
        theme.SetColor("caret_color", "LineEdit", UiTheme.FocusRing);
        theme.SetColor("clear_button_color", "LineEdit", UiTheme.Dim);
        theme.SetColor("clear_button_color_pressed", "LineEdit", UiTheme.Accent);
        theme.SetFontSize("font_size", "LineEdit", bodySize);

        // --- Scrollbars: a thin groove and an iron runner that lights under the hand ---
        foreach (string type in new[] { "VScrollBar", "HScrollBar" })
        {
            StyleBoxFlat groove = Flat(UiTheme.WellBg with { A = 0.6f }, null, 0);
            groove.SetContentMarginAll(4f);
            theme.SetStylebox("scroll", type, groove);
            theme.SetStylebox("scroll_focus", type, groove);
            theme.SetStylebox("grabber", type, Flat(UiTheme.Iron, null, 0));
            theme.SetStylebox("grabber_highlight", type, Flat(UiTheme.IronLit, null, 0));
            theme.SetStylebox("grabber_pressed", type, Flat(UiTheme.Accent, null, 0));
        }

        // --- Popup menu (every dropdown's list). A window of its own, so its ground is opaque. ---
        StyleBoxFlat menu = Flat(UiTheme.PanelBg with { A = 1f }, UiTheme.IronLit, line);
        menu.BorderWidthTop = line + 1;
        menu.SetContentMarginAll(UiTheme.SpaceXs);

        StyleBoxFlat menuHover = Flat(UiTheme.ButtonFaceHover with { A = 1f }, UiTheme.Accent, 0);
        menuHover.BorderWidthLeft = UiTheme.HighContrast ? 4 : 2;

        theme.SetStylebox("panel", "PopupMenu", menu);
        theme.SetStylebox("hover", "PopupMenu", menuHover);
        theme.SetColor("font_color", "PopupMenu", UiTheme.Text);
        theme.SetColor("font_hover_color", "PopupMenu", UiTheme.Accent);
        theme.SetColor("font_disabled_color", "PopupMenu", UiTheme.Disabled);
        theme.SetColor("font_accelerator_color", "PopupMenu", UiTheme.Dim);
        theme.SetColor("font_separator_color", "PopupMenu", UiTheme.Dim);
        theme.SetConstant("v_separation", "PopupMenu", UiTheme.SpaceMd);
        theme.SetConstant("h_separation", "PopupMenu", UiTheme.SpaceSm);
        theme.SetConstant("item_start_padding", "PopupMenu", UiTheme.SpaceSm);
        theme.SetConstant("item_end_padding", "PopupMenu", UiTheme.SpaceSm);
        theme.SetFontSize("font_size", "PopupMenu", bodySize);

        // The dropdown's own arrow, tinted with its text so it heats on hover and focus.
        SetIcon(theme, "arrow", "OptionButton", "dropdown_arrow");
        theme.SetConstant("modulate_arrow", "OptionButton", 1);

        // --- Tooltips: also their own window ---
        StyleBoxFlat tip = Flat(UiTheme.CardBg with { A = 1f }, UiTheme.IronLit, line);
        tip.SetContentMarginAll(UiTheme.SpaceXs);
        tip.ContentMarginLeft = UiTheme.SpaceSm;
        tip.ContentMarginRight = UiTheme.SpaceSm;

        theme.SetStylebox("panel", "TooltipPanel", tip);
        theme.SetColor("font_color", "TooltipLabel", UiTheme.Text);
        theme.SetFontSize("font_size", "TooltipLabel", bodySize);

        if (font != null)
        {
            foreach (string type in new[] { "CheckButton", "CheckBox", "LineEdit", "PopupMenu", "TooltipLabel" })
            {
                theme.SetFont("font", type, font);
            }
        }
    }

    private static StyleBoxFlat Flat(Color fill, Color? border, int width)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border ?? fill };
        box.SetBorderWidthAll(width);
        box.SetCornerRadiusAll(UiTheme.RadiusSm);
        return box;
    }

    /// <summary>A focus ring: nothing but its edge, drawn over whatever the control already drew.</summary>
    private static StyleBoxFlat Outline(Color color, int width)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0f), BorderColor = color, DrawCenter = false };
        box.SetBorderWidthAll(width);
        box.SetCornerRadiusAll(UiTheme.RadiusSm);
        return box;
    }

    private static void SetIcon(Theme theme, string name, string type, string file)
    {
        if (Icon(file) is { } icon)
        {
            theme.SetIcon(name, type, icon);
        }
    }

    private static Texture2D? Icon(string file)
    {
        if (Icons.TryGetValue(file, out Texture2D? cached))
        {
            return cached;
        }

        string path = $"{IconFolder}{file}.svg";
        Texture2D? loaded = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (loaded is null && !_warnedMissingIcon)
        {
            // Once, not per icon: an unimported folder misses all of them together.
            _warnedMissingIcon = true;
            Core.Diagnostics.Log.Warn($"UiSkin: could not load '{path}'; controls keep the engine's icons.");
        }

        Icons[file] = loaded;
        return loaded;
    }
}
