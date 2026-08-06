using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The UI design tokens and widget builders — the single source of truth every surface answers
/// to. Tokens (palette, type scale, spacing, radius, motion) encode the dying-world identity
/// pinned in <c>docs/UI_STYLE.md</c> (ash neutrals, bone-pale text, ember accents — matched to
/// <c>docs/ART_STYLE.md</c>); the builders below compose them into the controls every panel
/// uses. Change a token here and the whole UI follows.
///
/// **Phase 37.5A** gave the tokens a material and a voice: three vendored OFL typefaces
/// (<see cref="DisplayFont"/> / <see cref="SerifFont"/> / <see cref="UiFont"/>), a grain shader
/// under every framed surface, an engraved brass double-rule frame, three depth levels
/// (<see cref="WellBg"/> → <see cref="PanelBg"/> → <see cref="CardBg"/>), and the semantic ramps
/// the UI had been going without (rarity, magic school, quest state, disposition).
/// </summary>
public static class UiTheme
{
    // --- Palette tokens (see docs/UI_STYLE.md §2) -----------------------------
    // Surfaces: warm charcoal ash, never blue-black. Three depths, and the ordering is the
    // whole point — a control is understood by whether it sits *in* the panel (a well: slots,
    // troughs, input fields) or *on* it (a card: an item row, a spell, a save slot).
    private static readonly Color WellBase = new(0.055f, 0.052f, 0.048f, 0.95f);
    private static readonly Color PanelBase = new(0.09f, 0.085f, 0.075f, 0.92f);
    private static readonly Color CardBase = new(0.135f, 0.126f, 0.112f, 0.95f);

    // Properties rather than fields since 37.5G: high contrast makes the surfaces fully opaque, and
    // a translucent panel over a bright, busy world is where this UI is least readable. Source-
    // compatible with every existing `UiTheme.PanelBg` read.
    public static Color WellBg => Opaque(WellBase);
    public static Color PanelBg => Opaque(PanelBase);
    public static Color CardBg => Opaque(CardBase);

    private static Color Opaque(Color surface) => HighContrast ? surface with { A = 1f } : surface;

    public static readonly Color PanelBorder = new(0.42f, 0.40f, 0.35f, 0.80f);
    public static readonly Color Trough = new(0.13f, 0.125f, 0.115f, 0.95f);

    // Text: bone pale primary, ash-grey secondary. Dim is tuned to hold WCAG AA (≥4.5:1)
    // on every surface it labels, including button faces (30.5K; pinned by UiContrastTests).
    public static readonly Color Text = new(0.79f, 0.75f, 0.68f);
    public static readonly Color Dim = new(0.58f, 0.56f, 0.50f);

    /// <summary>Unavailable controls and unmet requirements. **Deliberately not contrast-pinned:**
    /// WCAG exempts disabled controls, and a disabled row that reads as strongly as an enabled one
    /// is a worse failure than a dim one — the player clicks it. It stays perceivable (≈2.4:1 on
    /// <see cref="PanelBg"/>), never invisible, and disabled state is always carried by a second
    /// channel (a reason string, a struck price) rather than by colour alone.</summary>
    public static readonly Color Disabled = new(0.40f, 0.385f, 0.35f);

    // Accents: ember gold is THE accent (headers, highlights, focus); ember orange is
    // reserved for the hottest emphasis (crits, warnings, the Flamebearer thread).
    public static readonly Color Accent = new(0.85f, 0.64f, 0.25f);
    public static readonly Color AccentHot = new(0.91f, 0.45f, 0.17f);

    // Semantic feedback. Adapted for colour vision (37.5G): green-vs-red is the single most
    // confusable pair in the whole UI and it is the one carrying "this went well" vs "this did not".
    private static readonly Color GoodBase = new(0.55f, 0.68f, 0.44f);
    private static readonly Color BadBase = new(0.82f, 0.42f, 0.36f);

    public static Color Good => Adapt(GoodBase);
    public static Color Bad => Adapt(BadBase);

    // Resource bar fills.
    public static readonly Color Health = new(0.78f, 0.30f, 0.26f);
    public static readonly Color Stamina = new(0.80f, 0.66f, 0.30f);
    public static readonly Color Mana = new(0.42f, 0.56f, 0.76f);

    // The corruption identity — the art bible's corruption violet (ART_STYLE §2), used by
    // the gauge fill and the HUD vignette. The deep fill violet fails text contrast (2.8:1),
    // so corruption-tinted *text* uses the brighter CorruptionText instead (30.5K).
    public static readonly Color Corruption = new(0.48f, 0.30f, 0.55f);

    private static readonly Color CorruptionTextBase = new(0.68f, 0.48f, 0.76f);

    public static Color CorruptionText => Adapt(CorruptionTextBase);

    // --- Material tokens (37.5A) ------------------------------------------------
    // Brass is *material*, ember gold is *meaning* — they must never resolve to the same value
    // or the frame starts competing with the thing it frames. Brass is duller and browner than
    // Accent on purpose; if a frame ever needs to read as important, it gets an Ornament, not a
    // brighter rule.
    public static readonly Color Brass = new(0.55f, 0.44f, 0.26f);
    public static readonly Color BrassLit = new(0.68f, 0.56f, 0.34f);

    /// <summary>The dark inner rule that sits inside the brass one. Two rules of different
    /// values is the entire engraving trick — a single border of any colour reads as a box.</summary>
    public static readonly Color Engrave = new(0.045f, 0.042f, 0.038f, 0.90f);

    // --- Arcane identity (37.5A; the spellbook is the one screen that runs cold) ---
    public static readonly Color ArcaneGround = new(0.072f, 0.070f, 0.095f, 0.94f);
    public static readonly Color ArcaneSilver = new(0.62f, 0.66f, 0.74f);
    public static readonly Color GlyphLight = new(0.68f, 0.74f, 0.92f);

    // --- Semantic ramps (37.5A) --------------------------------------------------

    /// <summary>The item rarity ramp. Delegates to <see cref="ItemRarities.Color"/>, which is the
    /// authority — the world-space drop glow and trophy tint read the same values, so an item
    /// cannot look one rarity on the ground and another in the pack.</summary>
    public static Color RarityColor(ItemRarity rarity) => Adapt(ItemRarities.Color(rarity));

    /// <summary>The magic school ramp. Delegates to <see cref="Magic.SpellSchools.Color"/>, which is
    /// the authority — the same value tints the projectile in flight and names the school in the
    /// spellbook, so the two can never drift apart.</summary>
    public static Color SchoolColor(DamageType school) => Adapt(Magic.SpellSchools.Color(school));

    /// <summary>A faction standing's colour. Delegates to <c>ReputationTiers.Color</c> (the
    /// authority) through the colour-vision adaptation, which is why UI code should call this rather
    /// than the authority directly.</summary>
    public static Color ReputationColor(Factions.ReputationTier tier) =>
        Adapt(Factions.ReputationTiers.Color(tier));

    // Quest state. Main is the Flamebearer thread and therefore the ember accent itself; side
    // quests get a cool bone so the two never compete in the tracker.
    public static readonly Color QuestMain = Accent;
    public static readonly Color QuestSide = new(0.72f, 0.74f, 0.70f);

    public static Color QuestComplete => Good;

    public static Color QuestFailed => Bad;

    // Disposition (nameplates, faction standings, map markers).
    public static Color Friendly => Good;

    public static readonly Color Neutral = new(0.74f, 0.71f, 0.60f);

    public static Color Hostile => Bad;

    // --- Accessibility (37.5G) -------------------------------------------------

    private static Settings.Settings? Current =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings.Current
            : null;

    /// <summary>The player's colour-vision setting, or None before the service exists (boot, and the
    /// unit-test harness, which reads these tokens with no engine running).</summary>
    public static ColorVisionMode VisionMode => Current?.ColorVision ?? ColorVisionMode.None;

    /// <summary>Whether high-contrast mode is on.</summary>
    public static bool HighContrast => Current?.HighContrast ?? false;

    /// <summary>
    /// Adapts a **semantic** colour for the player's colour-vision setting.
    ///
    /// ⚠️ **Applied at the token layer, not in the builders, and that split is load-bearing.**
    /// Daltonization is not idempotent — adapting an already-adapted colour over-shifts it — so
    /// exactly one layer may apply it. Doing it here means anything reading a semantic token or one
    /// of the three domain ramps is covered wherever it ends up, including raw <c>ColorRect</c>
    /// pips that never touch a builder. The neutral tokens (<see cref="Text"/>, <see cref="Dim"/>,
    /// <see cref="Accent"/>) deliberately stay unadapted: they are near-achromatic, so adaptation
    /// would move them for no gain while changing the whole UI's character.
    /// </summary>
    public static Color Adapt(Color color) => ColorVision.Daltonize(color, VisionMode);

    // --- Type scale ----------------------------------------------------------
    // Caption is the legibility floor — 12 px at reference scale (30.5K; was 11, raised in
    // the min-spec/Steam Deck readability audit). Nothing renders smaller.
    public const int CaptionFontSize = 12;
    public const int BodyFontSize = 14;
    public const int HeaderFontSize = 16;
    public const int TitleFontSize = 20;
    public const int DisplayFontSize = 26;

    /// <summary>The top of the scale — a word thrown across the middle of the screen (PARRY, the
    /// combat feedback overlay). Added in 37.5B to retire a hard-coded 40; nothing but a
    /// full-screen shout should reach for it.</summary>
    public const int ShoutFontSize = 40;

    /// <summary>
    /// The seam every builder sizes text through. It returns the token unchanged today; Phase
    /// 37.5G multiplies by the player's text-scale setting **here**, so that setting lands as one
    /// edit rather than a sweep through every builder. Callers should never read the consts
    /// directly when building a control.
    /// </summary>
    public static int FontSize(int token)
    {
        float scale = Current?.TextScale ?? 1f;

        // Never below the 12 px legibility floor (UI_STYLE §3), even if the setting goes low: the
        // floor exists because of a real min-spec/Steam Deck readability audit, and a *text size*
        // control that can make text unreadable is not an accessibility feature.
        int scaled = Mathf.RoundToInt(token * Mathf.Clamp(scale, 0.85f, 1.5f));
        return Mathf.Max(CaptionFontSize, scaled);
    }

    // --- Spacing scale (px at reference scale) ---------------------------------
    public const int SpaceXs = 4;
    public const int SpaceSm = 6;
    public const int SpaceMd = 10;
    public const int SpaceLg = 16;
    public const int SpaceXl = 24;

    // --- Radii -----------------------------------------------------------------
    // Tight radii throughout: this world's surfaces are cut and bound, not moulded. A large
    // radius is the fastest way to make a fantasy panel read as a web app.
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

    /// <summary>The reduced-motion flag as the <c>motion</c> uniform the UI shaders take. Every
    /// animated shader multiplies its time term by this, so one setting stops the rune ring, the
    /// sigil drift and the heading shimmer together.</summary>
    public static float MotionUniform => MotionEnabled ? 1f : 0f;

    // --- Fonts (37.5A) -------------------------------------------------------
    // Three vendored SIL OFL faces (provenance in assets/CREDITS.md):
    //   Cinzel      — inscriptional Roman capitals; titles and headers. Carved, not calligraphic.
    //   EB Garamond — a book serif; dialogue bodies, item flavour, codex pages. For *reading*.
    //   Inter       — the interface face; body, captions, numbers. Carries the 12 px floor and
    //                 has tabular figures, so stat columns stop shimmering as digits change.
    //
    // Loaded lazily rather than in a static field initializer, and this matters: UiContrastTests
    // reads UiTheme's colour tokens from plain xUnit with **no engine running**, and an eager
    // GD.Load in a static initializer would run on first touch of any token and take the suite
    // down with it. Colours stay eager; fonts load only when a builder actually asks for one.
    private const string DisplayFontPath = "res://assets/fonts/Cinzel-Variable.ttf";
    private const string SerifFontPath = "res://assets/fonts/EBGaramond-Variable.ttf";
    private const string SerifItalicFontPath = "res://assets/fonts/EBGaramond-Italic-Variable.ttf";
    private const string UiFontPath = "res://assets/fonts/Inter-Variable.ttf";

    private static FontFile? _displayFont, _serifFont, _serifItalicFont, _uiFont;
    private static bool _triedDisplay, _triedSerif, _triedSerifItalic, _triedUi;

    /// <summary>Cinzel — screen titles, section headers, boss names, menu items.</summary>
    public static FontFile? DisplayFont => Load(ref _displayFont, ref _triedDisplay, DisplayFontPath);

    /// <summary>EB Garamond — prose meant to be read rather than scanned.</summary>
    public static FontFile? SerifFont => Load(ref _serifFont, ref _triedSerif, SerifFontPath);

    /// <summary>EB Garamond Italic — item flavour. Shipped rather than synthesised: a slanted
    /// upright looks exactly as cheap as it is.</summary>
    public static FontFile? SerifItalicFont => Load(ref _serifItalicFont, ref _triedSerifItalic, SerifItalicFontPath);

    /// <summary>Inter — body, captions, numbers, tooltips, settings.</summary>
    public static FontFile? UiFont => Load(ref _uiFont, ref _triedUi, UiFontPath);

    /// <summary>
    /// Loads a resource once and remembers the outcome, **including failure**. <c>GD.Load</c> can
    /// return null (CLAUDE.md §7) — a missing or unimported font must leave the UI rendering in
    /// Godot's default face rather than throwing, because a game that will not draw its menus is
    /// a far worse failure than one drawn in the wrong typeface. The <c>tried</c> flag is what
    /// stops a failed load being retried on every label built for the rest of the session.
    /// </summary>
    private static T? Load<T>(ref T? cached, ref bool tried, string path) where T : class
    {
        if (!tried)
        {
            tried = true;
            cached = ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
            if (cached is null)
            {
                Core.Diagnostics.Log.Warn($"UiTheme: could not load '{path}'; falling back to the engine default.");
            }
        }

        return cached;
    }

    /// <summary>The three type roles a piece of UI text can take.</summary>
    public enum FontRole
    {
        /// <summary>Inter. Body, captions, numbers — anything scanned.</summary>
        Interface,

        /// <summary>Cinzel. Titles and headers — anything carved.</summary>
        Display,

        /// <summary>EB Garamond. Prose — anything read.</summary>
        Serif,

        /// <summary>EB Garamond Italic. Flavour text.</summary>
        SerifItalic,
    }

    private static FontFile? FontFor(FontRole role) => role switch
    {
        FontRole.Display => DisplayFont,
        FontRole.Serif => SerifFont,
        FontRole.SerifItalic => SerifItalicFont,
        _ => UiFont,
    };

    /// <summary>Applies a type role and size to a control. A null font (unimported, or running
    /// under a test harness) simply leaves the engine default in place.</summary>
    public static void ApplyType(Control control, FontRole role, int sizeToken)
    {
        if (FontFor(role) is { } font)
        {
            control.AddThemeFontOverride("font", font);
        }

        control.AddThemeFontSizeOverride("font_size", FontSize(sizeToken));
    }

    // --- Shaders (37.5A) -----------------------------------------------------
    private const string GrainShaderPath = "res://assets/shaders/ui/ui_grain.gdshader";
    private static Shader? _grainShader;
    private static bool _triedGrain;

    private static Shader? GrainShader => Load(ref _grainShader, ref _triedGrain, GrainShaderPath);

    /// <summary>
    /// Gives a control the parchment/leather material read. The shader only *tints* what the
    /// stylebox already drew, so corners, borders and content margins stay Godot's job — see
    /// the header comment in <c>ui_grain.gdshader</c> for why that split is deliberate.
    ///
    /// Each call builds its own <see cref="ShaderMaterial"/> so a panel can tune its own
    /// weathering (the spellbook runs colder, the map runs lighter) without writing through to
    /// every other surface — the same <c>Duplicate()</c>-before-tinting rule the 3D materials
    /// follow (CLAUDE.md §8).
    /// </summary>
    public static void ApplyGrain(Control control, float grain = 0.35f, float fibre = 0.18f, float mottle = 0.22f, Color? tint = null)
    {
        // High contrast drops the material entirely. The grain is the single largest source of
        // low-amplitude noise on every surface, and it is the first thing to go for a player who
        // turned this on because the UI is hard to read.
        if (HighContrast || GrainShader is not { } shader)
        {
            control.Material = null;
            return;
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("grain_strength", grain);
        material.SetShaderParameter("fibre_strength", fibre);
        material.SetShaderParameter("mottle_strength", mottle);
        material.SetShaderParameter("weather_tint", (tint ?? Brass) with { A = 1f });
        control.Material = material;
    }

    // --- Builders -----------------------------------------------------------

    /// <summary>A framed panel: engraved brass rule over aged parchment. The screen ground.</summary>
    public static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        ApplyGrain(panel);
        return panel;
    }

    /// <summary>A recessed surface — item slots, troughs, input wells. Reads as cut *into* the
    /// panel: darker than its ground, with the bright rule on the inside rather than the outside.</summary>
    public static PanelContainer Well()
    {
        var well = new PanelContainer();
        well.AddThemeStyleboxOverride("panel", WellStyle());
        return well;
    }

    /// <summary>A raised surface — an item row, a spell, a save slot. Reads as sat *on* the panel.
    /// <paramref name="edge"/> paints a left spine in a semantic colour (rarity, school, quest
    /// state), which is how a list conveys category without a legend.</summary>
    public static PanelContainer Card(Color? edge = null)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", CardStyle(edge));
        return card;
    }

    /// <summary>
    /// The ground a full-screen overlay dims the world with. **Warm charcoal, not black and not
    /// blue-black** — 37.5B found seven hand-rolled scrims across the shell (main menu, pause,
    /// settings, save slots, character creator, loading, narration) at four different values, and
    /// six of the seven were blue-tinted (`0.02, 0.02, 0.04`), which is the one thing UI_STYLE §1
    /// rule 1 says a surface in this world must never be. They now come from here, so a screen
    /// cannot invent its own again.
    /// </summary>
    public static readonly Color ScrimBg = new(0.035f, 0.032f, 0.028f);

    /// <summary>A full-screen dimming layer. <paramref name="opacity"/> is the only knob a screen
    /// gets: 1.0 for a screen that replaces the world (menu, loading), ~0.9 for one that covers it
    /// (settings, save slots), ~0.55 for one the world should still read through (pause).</summary>
    public static ColorRect Scrim(float opacity = 0.92f)
    {
        var rect = new ColorRect { Color = ScrimBg with { A = opacity } };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>
    /// Insets a full-screen panel from the view edge, with a gutter that shrinks on narrow
    /// viewports (37.5G).
    ///
    /// ⚠️ **Measure the viewport, never the window.** `GetViewportRect()` is already in *logical*
    /// pixels — the content-scale factor the UI-scale setting drives has been applied — so a Steam
    /// Deck at 1280×800 with UI scale 1.5 reports 853×533, not 1280×800. The screens built in
    /// 37.5C/D used a flat 70 px gutter and fixed column widths against an assumed ~1900 px, and
    /// overflowed by 321 px and 167 px respectively in exactly that configuration.
    ///
    /// Call it from `Rebuild` as well as `BuildShell`: the setting can change mid-session, and
    /// offsets applied once at `_Ready` would keep a stale gutter until the game restarted.
    /// </summary>
    public static void ApplyScreenInset(Control shell)
    {
        float width = shell.GetViewportRect().Size.X;
        int gutter = width < 1100f ? SpaceLg : 70;

        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.OffsetLeft = gutter;
        shell.OffsetTop = gutter;
        shell.OffsetRight = -gutter;
        shell.OffsetBottom = -gutter;
    }

    /// <summary>The logical width a full-screen panel has to lay out inside, after its gutter and
    /// the standard padding. The number every adaptive column count should be derived from.</summary>
    public static float UsableWidth(Control shell)
    {
        float width = shell.GetViewportRect().Size.X;
        int gutter = width < 1100f ? SpaceLg : 70;
        return Mathf.Max(320f, width - (gutter * 2f) - 28f);
    }

    /// <summary>
    /// The logical height a centred panel may occupy. The Steam Deck at UI scale 1.5 reports a
    /// **533 px** logical viewport — short enough that fixed heights authored against a desktop
    /// window overflow vertically even when the width fits comfortably. Width is the obvious axis
    /// to check and height is the one that actually bites on a handheld.
    /// </summary>
    public static float UsableHeight(Control control) =>
        Mathf.Max(240f, control.GetViewportRect().Size.Y - (SpaceXl * 2f));

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

    /// <summary>A screen or panel title, in carved capitals.</summary>
    public static Label Title(string text)
    {
        var label = new Label { Text = text };
        ApplyType(label, FontRole.Display, TitleFontSize);
        label.AddThemeColorOverride("font_color", Accent);
        return label;
    }

    /// <summary>The biggest type in the game — boss names, level-up, the title screen.</summary>
    public static Label Display(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        ApplyType(label, FontRole.Display, DisplayFontSize);
        label.AddThemeColorOverride("font_color", color ?? Accent);
        return label;
    }

    public static Label Header(string text)
    {
        var label = new Label { Text = text };
        ApplyType(label, FontRole.Display, HeaderFontSize);
        label.AddThemeColorOverride("font_color", Accent);
        return label;
    }

    public static Label Body(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        ApplyType(label, FontRole.Interface, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    /// <summary>Prose in the book serif — dialogue bodies, codex pages, narration. Wraps by
    /// default, because everything this builder is for is a paragraph.</summary>
    public static Label Prose(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        ApplyType(label, FontRole.Serif, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    /// <summary>Item flavour and asides, in the serif italic.</summary>
    public static Label Flavour(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        ApplyType(label, FontRole.SerifItalic, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Dim);
        return label;
    }

    /// <summary>A small secondary line (slot numbers, hints, metadata).</summary>
    public static Label Caption(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        ApplyType(label, FontRole.Interface, CaptionFontSize);
        label.AddThemeColorOverride("font_color", color ?? Dim);
        return label;
    }

    /// <summary>A horizontal engraved rule — a dark groove under a brass highlight, which is why
    /// it is two lines and not one. Separates sections inside a panel.</summary>
    public static Control Divider()
    {
        var wrap = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrap.AddThemeConstantOverride("separation", 0);
        wrap.AddChild(Rule(Engrave with { A = 0.75f }, 1f));
        wrap.AddChild(Rule(BrassLit with { A = 0.28f }, 1f));
        return wrap;
    }

    /// <summary>A titled section break: a header with an engraved rule running out to the right.
    /// The workhorse for giving a long panel readable structure.</summary>
    public static Control SectionRule(string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", SpaceMd);
        row.AddChild(Header(text));

        Control rule = Divider();
        rule.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rule.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(rule);
        return row;
    }

    private static ColorRect Rule(Color color, float height)
    {
        return new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(0f, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
    }

    /// <summary>A small tinted pill — an affix, a status effect, a school tag, a filter. The
    /// label carries the colour; the ground stays near-neutral so a row of chips does not turn
    /// into a paint chart.</summary>
    public static PanelContainer Chip(string text, Color color) => Chip(text, color, out _);

    /// <summary>
    /// As <see cref="Chip(string, Color)"/>, with a second label after the text for a live value —
    /// a status effect's remaining seconds, a stack count. Handed back so the caller can update it
    /// in place each frame instead of rebuilding the chip, which is what the HUD's status row does.
    /// </summary>
    public static PanelContainer Chip(string text, Color color, out Label trailing)
    {
        PanelContainer chip = ChipShell(color);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", SpaceXs);
        row.AddChild(Caption(text, color));

        trailing = Caption("");
        row.AddChild(trailing);
        chip.AddChild(row);
        return chip;
    }

    private static PanelContainer ChipShell(Color color)
    {
        var box = new StyleBoxFlat { BgColor = WellBg, BorderColor = color with { A = 0.55f } };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(2);
        box.ContentMarginLeft = SpaceSm;
        box.ContentMarginRight = SpaceSm;

        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", box);
        return chip;
    }

    /// <summary>A square item slot: a recessed well sized to the icon grid. Callers add the icon
    /// (and a <see cref="RarityFrame"/> over it) as children.</summary>
    public static PanelContainer IconSlot(float size = 48f)
    {
        PanelContainer slot = Well();
        slot.CustomMinimumSize = new Vector2(size, size);
        return slot;
    }

    /// <summary>
    /// The **non-colour** half of the rarity signal: the slot frame thickens at Epic and above.
    /// Pure and separate from <see cref="RarityFrame"/> so it can be unit-tested — the test
    /// project forbids constructing Godot objects, so a <see cref="StyleBoxFlat"/> cannot be
    /// asserted on, and a redundancy channel nothing checks is a redundancy channel that quietly
    /// stops existing.
    /// </summary>
    public static int RarityBorderWidth(ItemRarity rarity) => rarity >= ItemRarity.Epic ? 2 : 1;

    /// <summary>
    /// The rarity treatment for a filled slot: a coloured rule around the well, plus a faint inner
    /// wash at Epic and above.
    ///
    /// Rarity is **never carried by colour alone** — see <see cref="RarityBorderWidth"/> for the
    /// second channel, and <see cref="ItemRarities.Color"/> for why the ramp's luminance climbs.
    /// </summary>
    public static StyleBoxFlat RarityFrame(ItemRarity rarity)
    {
        Color color = RarityColor(rarity);
        bool exalted = rarity >= ItemRarity.Epic;

        var box = new StyleBoxFlat
        {
            BgColor = exalted ? color with { A = 0.10f } : WellBg,
            BorderColor = color with { A = rarity == ItemRarity.Common ? 0.35f : 0.85f },
        };
        box.SetBorderWidthAll(RarityBorderWidth(rarity));
        box.SetCornerRadiusAll(RadiusSm);
        return box;
    }

    public static Button Action(string text)
    {
        var button = new Button { Text = text };
        ApplyInteractiveStyle(button);
        ApplyType(button, FontRole.Display, BodyFontSize);
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

    /// <summary>A labelled bar: caption above, bar below. The shape a stat/objective/progress
    /// readout takes everywhere outside the HUD's vitals (which have their own juiced widget).</summary>
    public static (VBoxContainer Root, Label Caption, ProgressBar Bar) Meter(string label, Color fill, float width = 168f)
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 2);

        Label caption = Caption(label);
        ProgressBar bar = Bar(fill, width);
        root.AddChild(caption);
        root.AddChild(bar);
        return (root, caption, bar);
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
        ApplyType(option, FontRole.Interface, BodyFontSize);
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

    /// <summary>
    /// The framed-panel stylebox (also used by transient widgets like toasts): parchment ground,
    /// a 2 px brass rule, and a dark expanded shadow standing in for the engraved groove that
    /// separates the rule from whatever is behind it.
    ///
    /// The engraved read comes from two values at different depths, not from one thicker border —
    /// a single rule of any colour reads as a box no matter how wide it is.
    /// </summary>
    public static StyleBoxFlat PanelStyle()
    {
        var box = new StyleBoxFlat
        {
            BgColor = PanelBg,
            BorderColor = HighContrast ? BrassLit : Brass with { A = 0.85f },
        };
        box.SetBorderWidthAll(HighContrast ? 3 : 2);
        box.SetCornerRadiusAll(RadiusLg);

        // The groove: a hard, un-blurred dark ring just outside the brass.
        box.ShadowColor = Engrave;
        box.ShadowSize = 1;
        return box;
    }

    /// <summary>The recessed stylebox — darker ground, and the bright edge on the *top* only, so
    /// the light reads as falling into a cut rather than off a raised lip.</summary>
    public static StyleBoxFlat WellStyle()
    {
        var box = new StyleBoxFlat { BgColor = WellBg, BorderColor = Engrave with { A = 0.85f } };
        box.SetBorderWidthAll(1);
        box.BorderWidthBottom = 0;
        box.SetCornerRadiusAll(RadiusSm);
        return box;
    }

    /// <summary>The raised-row stylebox. <paramref name="edge"/> paints the left spine that
    /// carries a row's category colour.</summary>
    public static StyleBoxFlat CardStyle(Color? edge = null)
    {
        var box = new StyleBoxFlat { BgColor = CardBg, BorderColor = edge ?? (BrassLit with { A = 0.22f }) };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = edge is null ? 1 : HighContrast ? 5 : 3;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(SpaceSm);
        box.ContentMarginLeft = SpaceMd;
        return box;
    }

    /// <summary>The shared normal/hover/pressed/focus styling for clickable controls. Focus
    /// draws an ember border — the visibility seam the gamepad navigation pass (30.5J) rides.</summary>
    private static void ApplyInteractiveStyle(Button button)
    {
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_focus_color", Accent);
        button.AddThemeColorOverride("font_disabled_color", Disabled);
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
