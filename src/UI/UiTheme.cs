using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The UI design tokens and widget builders (Phase 30.5A) — the single source of truth every
/// surface answers to. Tokens (palette, type scale, spacing, radius, motion) encode the
/// dying-world identity pinned in <c>docs/UI_STYLE.md</c> (ash neutrals, bone-pale text,
/// ember accents — matched to <c>docs/ART_STYLE.md</c>); the builders below compose them into
/// the controls every panel uses. Change a token here and the whole UI follows.
/// </summary>
public static class UiTheme
{
    // --- Palette tokens (see docs/UI_STYLE.md §2) -----------------------------
    // Surfaces: warm charcoal ash, never blue-black.
    public static readonly Color PanelBg = new(0.09f, 0.085f, 0.075f, 0.92f);
    public static readonly Color PanelBorder = new(0.42f, 0.40f, 0.35f, 0.80f);
    public static readonly Color Trough = new(0.13f, 0.125f, 0.115f, 0.95f);

    // Text: bone pale primary, ash-grey secondary. Dim is tuned to hold WCAG AA (≥4.5:1)
    // on every surface it labels, including button faces (30.5K; pinned by UiContrastTests).
    public static readonly Color Text = new(0.79f, 0.75f, 0.68f);
    public static readonly Color Dim = new(0.58f, 0.56f, 0.50f);

    // Accents: ember gold is THE accent (headers, highlights, focus); ember orange is
    // reserved for the hottest emphasis (crits, warnings, the Flamebearer thread).
    public static readonly Color Accent = new(0.85f, 0.64f, 0.25f);
    public static readonly Color AccentHot = new(0.91f, 0.45f, 0.17f);

    // Semantic feedback.
    public static readonly Color Good = new(0.55f, 0.68f, 0.44f);
    public static readonly Color Bad = new(0.82f, 0.42f, 0.36f);

    // Resource bar fills.
    public static readonly Color Health = new(0.78f, 0.30f, 0.26f);
    public static readonly Color Stamina = new(0.80f, 0.66f, 0.30f);
    public static readonly Color Mana = new(0.42f, 0.56f, 0.76f);

    // The corruption identity — the art bible's corruption violet (ART_STYLE §2), used by
    // the gauge fill and the HUD vignette. The deep fill violet fails text contrast (2.8:1),
    // so corruption-tinted *text* uses the brighter CorruptionText instead (30.5K).
    public static readonly Color Corruption = new(0.48f, 0.30f, 0.55f);
    public static readonly Color CorruptionText = new(0.68f, 0.48f, 0.76f);

    // --- Type scale ----------------------------------------------------------
    // Caption is the legibility floor — 12 px at reference scale (30.5K; was 11, raised in
    // the min-spec/Steam Deck readability audit). Nothing renders smaller.
    public const int CaptionFontSize = 12;
    public const int BodyFontSize = 14;
    public const int HeaderFontSize = 16;
    public const int TitleFontSize = 20;
    public const int DisplayFontSize = 26;

    // --- Spacing scale (px at reference scale) ---------------------------------
    public const int SpaceXs = 4;
    public const int SpaceSm = 6;
    public const int SpaceMd = 10;
    public const int SpaceLg = 16;
    public const int SpaceXl = 24;

    // --- Radii -----------------------------------------------------------------
    public const int RadiusSm = 3;
    public const int RadiusMd = 4;
    public const int RadiusLg = 6;

    // --- Motion tokens -----------------------------------------------------------
    // Durations in seconds; always route through Duration() so the reduced-motion
    // accessibility setting (Settings.ReducedMotion) collapses animation to instant.
    public const float DurationFast = 0.12f;
    public const float DurationBase = 0.20f;
    public const float DurationSlow = 0.35f;

    /// <summary>False while the player has reduced motion enabled in settings.</summary>
    public static bool MotionEnabled =>
        ServiceLocator.Instance is not { } locator ||
        !locator.TryGet(out SettingsService settings) ||
        !settings.Current.ReducedMotion;

    /// <summary>A motion duration honouring the reduced-motion setting (0 = instant).</summary>
    public static float Duration(float seconds) => MotionEnabled ? seconds : 0f;

    // --- Builders -----------------------------------------------------------

    /// <summary>A framed, semi-transparent panel with rounded corners.</summary>
    public static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        return panel;
    }

    /// <summary>The standard inner padding container panels wrap their content in.</summary>
    public static MarginContainer Padding(int amount = SpaceMd)
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

    /// <summary>A small secondary line (slot numbers, hints, metadata).</summary>
    public static Label Caption(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", CaptionFontSize);
        label.AddThemeColorOverride("font_color", color ?? Dim);
        return label;
    }

    /// <summary>A small keycap chip (e.g. the "E" in the interaction prompt): the key's label
    /// in a bordered well, sized to its content.</summary>
    public static PanelContainer KeyCap(string key) => KeyCap(key, out _);

    /// <summary>As <see cref="KeyCap(string)"/>, also handing back the glyph label so callers
    /// can refresh it live (device flips, rebinds — 30.5J).</summary>
    public static PanelContainer KeyCap(string key, out Label label)
    {
        var box = new StyleBoxFlat { BgColor = Trough, BorderColor = Dim };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(2);
        box.ContentMarginLeft = 7;
        box.ContentMarginRight = 7;

        var cap = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", box);

        label = Caption(key, Text);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        cap.AddChild(label);
        return cap;
    }

    public static Button Action(string text)
    {
        var button = new Button { Text = text };
        ApplyInteractiveStyle(button);
        button.Pressed += PlayUiClick; // Phase 31C: one seam gives every menu button its click
        return button;
    }

    /// <summary>Plays the shared UI click cue via the <c>AudioDirector</c> (Phase 31C). Safe before the
    /// director exists (title boot) — resolves each press, no-ops if unavailable.</summary>
    private static void PlayUiClick()
    {
        if (Core.Services.ServiceLocator.Instance is { } locator
            && locator.TryGet(out Audio.AudioDirector audio))
        {
            audio.PlayCue("ui.click");
        }
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
        bar.AddThemeStyleboxOverride("background", BarStyle(Trough));
        bar.AddThemeStyleboxOverride("fill", BarStyle(fill));
        return bar;
    }

    /// <summary>A labelled on/off switch (settings rows). Caller wires <c>Toggled</c>.</summary>
    public static CheckButton Toggle(bool value)
    {
        var check = new CheckButton { ButtonPressed = value };
        check.AddThemeColorOverride("font_color", Text);
        check.AddThemeColorOverride("font_hover_color", Accent);
        return check;
    }

    /// <summary>A horizontal value slider (volumes, sensitivity, UI scale). Caller wires
    /// <c>ValueChanged</c>/<c>DragEnded</c>.</summary>
    public static HSlider Slider(double min, double max, double step, double value, float width = 200f)
    {
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(width, 18f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        return slider;
    }

    /// <summary>An enumerated chooser (window mode, FPS cap, difficulty). Caller wires
    /// <c>ItemSelected</c>.</summary>
    public static OptionButton Dropdown(string[] options, int selected)
    {
        var option = new OptionButton();
        ApplyInteractiveStyle(option);
        for (int i = 0; i < options.Length; i++)
        {
            option.AddItem(options[i], i);
        }

        if (selected >= 0 && selected < options.Length)
        {
            option.Selected = selected;
        }

        return option;
    }

    /// <summary>A vertical scroll area + content list, the shape every panel body uses
    /// (30.5F). The scroll expands to fill its parent; the list grows with rows.</summary>
    public static (ScrollContainer Scroll, VBoxContainer List) ScrollList()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, // keep the focused row in view under gamepad/keyboard nav (30.5J)
        };

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return (scroll, list);
    }

    /// <summary>Clears a rebuilt container's children (the dirty-flag rebuild pattern).</summary>
    public static void ClearChildren(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    // --- Style boxes --------------------------------------------------------

    /// <summary>The framed-panel stylebox (also used by transient widgets like toasts).</summary>
    public static StyleBoxFlat PanelStyle()
    {
        var box = new StyleBoxFlat { BgColor = PanelBg, BorderColor = PanelBorder };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(RadiusLg);
        return box;
    }

    /// <summary>The shared normal/hover/pressed/focus styling for clickable controls. Focus
    /// draws an ember border — the visibility seam the gamepad navigation pass (30.5J) rides.</summary>
    private static void ApplyInteractiveStyle(Button button)
    {
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.16f, 0.15f, 0.13f, 0.95f)));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.23f, 0.21f, 0.18f, 0.98f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.11f, 0.10f, 0.09f, 0.98f)));

        StyleBoxFlat focus = ButtonStyle(new Color(0.16f, 0.15f, 0.13f, 0.95f));
        focus.BorderColor = Accent;
        focus.SetBorderWidthAll(1);
        button.AddThemeStyleboxOverride("focus", focus);

        // Hover/press/focus microinteraction (30.5I): a brief modulate ease layered over the
        // stylebox swap so interaction reads as a glow, not a hard state flip.
        button.MouseEntered += () => AnimateModulate(button, HoverModulate);
        button.MouseExited += () => AnimateModulate(button, Colors.White);
        button.FocusEntered += () => AnimateModulate(button, HoverModulate);
        button.FocusExited += () => AnimateModulate(button, Colors.White);
        button.ButtonDown += () => AnimateModulate(button, PressModulate);
        button.ButtonUp += () => AnimateModulate(button, button.IsHovered() ? HoverModulate : Colors.White);
    }

    // Slight brighten on hover/focus, slight sink on press (modulate may exceed 1 in 2D).
    private static readonly Color HoverModulate = new(1.10f, 1.09f, 1.06f);
    private static readonly Color PressModulate = new(0.90f, 0.90f, 0.90f);
    private const string ModulateTweenMeta = "ui_modulate_tween";

    /// <summary>Eases a control's modulate toward <paramref name="target"/> over
    /// <paramref name="seconds"/> (default <c>DurationFast</c>; instant under reduced motion).
    /// Kills any in-flight ease so rapid hover flicks never stack. Runs while the tree is
    /// paused (pause-menu buttons).</summary>
    public static void AnimateModulate(Control control, Color target, float seconds = DurationFast)
    {
        if (control.HasMeta(ModulateTweenMeta) &&
            control.GetMeta(ModulateTweenMeta).As<Tween>() is { } previous && previous.IsValid())
        {
            previous.Kill();
        }

        float duration = Duration(seconds);
        if (duration <= 0f || !control.IsInsideTree())
        {
            control.Modulate = target;
            return;
        }

        Tween tween = control.CreateTween();
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        tween.TweenProperty(control, "modulate", target, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        control.SetMeta(ModulateTweenMeta, tween);
    }

    private static StyleBoxFlat ButtonStyle(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(RadiusMd);
        box.SetContentMarginAll(SpaceXs);
        box.ContentMarginLeft = 9;
        box.ContentMarginRight = 9;
        return box;
    }

    /// <summary>The rounded bar stylebox (shared with <see cref="JuicedBar"/>).</summary>
    internal static StyleBoxFlat BarStyle(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(RadiusSm);
        return box;
    }
}
