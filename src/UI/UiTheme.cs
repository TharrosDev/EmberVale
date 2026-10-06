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
public static partial class UiTheme
{
    // --- Palette tokens (see docs/UI_STYLE.md §2) -----------------------------
    // Surfaces: warm charcoal ash, never blue-black. Three depths, and the ordering is the
    // whole point — a control is understood by whether it sits *in* the panel (a well: slots,
    // troughs, input fields) or *on* it (a card: an item row, a spell, a save slot).
    private static readonly Color WellBase = new(0.030f, 0.031f, 0.030f, 0.97f);
    private static readonly Color PanelBase = new(0.060f, 0.058f, 0.052f, 0.96f);
    private static readonly Color CardBase = new(0.098f, 0.092f, 0.081f, 0.94f);

    // Properties rather than fields since 37.5G: high contrast makes the surfaces fully opaque, and
    // a translucent panel over a bright, busy world is where this UI is least readable. Source-
    // compatible with every existing `UiTheme.PanelBg` read.
    public static Color WellBg => Opaque(WellBase);
    public static Color PanelBg => Opaque(PanelBase);
    public static Color CardBg => Opaque(CardBase);

    private static Color Opaque(Color surface) => HighContrast ? surface with { A = 1f } : surface;

    public static readonly Color PanelBorder = new(0.34f, 0.36f, 0.34f, 0.84f);
    public static readonly Color Iron = new(0.28f, 0.30f, 0.29f);
    public static readonly Color IronLit = new(0.46f, 0.47f, 0.43f);
    public static readonly Color Ash = new(0.36f, 0.34f, 0.30f);

    /// <summary>
    /// The empty part of every bar in the game.
    ///
    /// ⚠️ <b>This used to be `0.13, 0.125, 0.115` — within a rounding error of <see cref="CardBg"/>'s
    /// `0.135, 0.126, 0.112`, which is the surface almost every bar sits on.</b> The result was that a
    /// bar's empty track was *invisible*: a health bar at 122/664 read as a short red nub floating in
    /// a card rather than as a nearly-empty gauge, so the player could not tell a low bar from a short
    /// one. It survived every check this repo has because nothing renders a colour comparison, and it
    /// was found the first time 39.5B captured the HUD and looked at it.
    ///
    /// Now the depth scale's own answer: `UI_STYLE.md` §2 lists **troughs** under `WellBg` — "cut
    /// into the panel" — and flagged this token as "predates the depth scale; kept". It no longer
    /// predates anything.
    /// </summary>
    public static Color Trough => WellBg;

    // Text: bone pale primary, ash-grey secondary. Dim is tuned to hold WCAG AA (≥4.5:1)
    // on every surface it labels, including button faces (30.5K; pinned by UiContrastTests).
    public static readonly Color Text = new(0.84f, 0.80f, 0.72f);
    public static readonly Color Dim = new(0.62f, 0.60f, 0.54f);

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

    // --- Plate tokens (banked embers) -------------------------------------------
    // A surface is a cut iron plate with ONE lit edge, not a box with four. Rule is the cold seam
    // between two things on the same plate; RuleLit is the single edge the fire catches, and a
    // surface gets at most one of it. Keyline is the dark outline that holds a bar or a glyph
    // together over the live world, where there is no plate behind it at all.

    /// <summary>The cold hairline between rows, columns and tabs on one surface.</summary>
    public static readonly Color Rule = new(0.30f, 0.31f, 0.29f, 0.60f);

    /// <summary>The one lit edge of a plate or sheet: iron with the fire on it. Warmer than
    /// <see cref="IronLit"/>, duller than <see cref="Accent"/>, so it reads as light and not as meaning.</summary>
    public static readonly Color RuleLit = new(0.66f, 0.56f, 0.40f, 0.90f);

    /// <summary>The dark 1 px outline around HUD bars, icons and glyphs drawn straight on the world.</summary>
    public static readonly Color Keyline = new(0.020f, 0.019f, 0.017f, 0.88f);

    /// <summary>
    /// The focus indicator, and nothing else. Brighter than <see cref="Accent"/> on purpose: hover,
    /// selection and headers are all ember gold, so a focus ring in the same gold is the one state a
    /// controller player cannot find. Held at 3:1 or better against every surface and button face a
    /// focused control can sit on (WCAG 2.2 non-text contrast; pinned by <c>UiContrastTests</c>).
    /// </summary>
    public static readonly Color FocusRing = new(0.98f, 0.80f, 0.42f);

    /// <summary>The breath of heat under a lit edge, an ember wipe or a held ring. Always drawn
    /// translucent and never as text: it is ember orange with most of the fire let out.</summary>
    public static readonly Color EmberGlow = new(0.95f, 0.50f, 0.16f, 0.35f);

    // The faces UiTheme.Action / Dropdown draw (ApplyInteractiveStyle). Public so the contrast audit
    // reads the real values: it used to carry its own copies, and they had drifted to a lighter grey
    // than anything on screen.
    public static readonly Color ButtonFace = new(0.10f, 0.095f, 0.085f, 0.82f);
    public static readonly Color ButtonFaceHover = new(0.18f, 0.155f, 0.115f, 0.96f);
    public static readonly Color ButtonFacePressed = new(0.07f, 0.065f, 0.06f, 0.98f);
    public static readonly Color ButtonFaceFocus = new(0.15f, 0.13f, 0.10f, 0.98f);

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

    /// <summary>Whether the player asked for the plain interface face everywhere (see <see cref="ResolveRole"/>).</summary>
    public static bool ReadableFont => Current?.ReadableFont ?? false;

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
    public const int BodyFontSize = 15;
    public const int HeaderFontSize = 18;
    public const int TitleFontSize = 24;
    public const int DisplayFontSize = 32;

    /// <summary>The top of the scale — a word thrown across the middle of the screen (PARRY, the
    /// combat feedback overlay). Added in 37.5B to retire a hard-coded 40; nothing but a
    /// full-screen shout should reach for it.</summary>
    public const int ShoutFontSize = 40;

    /// <summary>
    /// The seam every builder sizes text through: the token under the player's text-scale setting
    /// (see <see cref="ScaledFontSize"/>). Callers should never read the consts directly when
    /// building a control.
    /// </summary>
    public static int FontSize(int token) => ScaledFontSize(token, Current?.TextScale ?? 1f);

    /// <summary>
    /// <see cref="FontSize"/> for an explicit text scale. Pure, so the curve is unit-tested.
    ///
    /// The scale is not applied evenly. Body and caption text take all of it, because they are what
    /// the setting is for; a header takes 85% of the change, a title 70% and display type half. A
    /// 32 px boss name is already readable, and at a flat 1.5x it became 48 px and pushed the
    /// layouts built around it off a handheld screen while buying the player nothing.
    /// </summary>
    public static int ScaledFontSize(int token, float textScale)
    {
        float delta = Mathf.Clamp(textScale, 0.85f, 1.5f) - 1f;

        // Never below the 12 px legibility floor (UI_STYLE §3), even if the setting goes low: the
        // floor exists because of a real min-spec/Steam Deck readability audit, and a *text size*
        // control that can make text unreadable is not an accessibility feature.
        int scaled = Mathf.RoundToInt(token * (1f + (delta * TextScaleShare(token))));
        return Mathf.Max(CaptionFontSize, scaled);
    }

    /// <summary>How much of the text-scale change a size takes: all of it up to body, 85% at header,
    /// 70% at title, half from display up, and a straight line between those for an off-scale size.</summary>
    private static float TextScaleShare(int token)
    {
        if (token <= BodyFontSize)
        {
            return 1f;
        }

        if (token <= HeaderFontSize)
        {
            return Mathf.Lerp(1f, 0.85f, (token - BodyFontSize) / (float)(HeaderFontSize - BodyFontSize));
        }

        if (token <= TitleFontSize)
        {
            return Mathf.Lerp(0.85f, 0.70f, (token - HeaderFontSize) / (float)(TitleFontSize - HeaderFontSize));
        }

        if (token < DisplayFontSize)
        {
            return Mathf.Lerp(0.70f, 0.50f, (token - TitleFontSize) / (float)(DisplayFontSize - TitleFontSize));
        }

        return 0.50f;
    }

    // --- Spacing scale (px at reference scale) ---------------------------------
    // Widened in the 2026-10 breathing-room pass (was 5/8/12/18/28 and the UI read as packed). The
    // steps are named by size, never by use, so a screen picks the rung that matches the gap it means.
    // Everything below the roles is derived from these, so a future retune is one edit.
    public const int Space2xs = 4; // hairline: a label and the line under it, an icon and its text
    public const int SpaceXs = 6;  // inside a control: a chip's items, a tab's padding
    public const int SpaceSm = 10; // between related controls; a card's vertical padding
    public const int SpaceMd = 16; // panel padding; a card's side padding; between groups
    public const int SpaceLg = 24; // between sections; the narrow-viewport gutter; the HUD safe margin
    public const int SpaceXl = 32; // around a modal's content on a bare screen

    // --- Spacing roles ------------------------------------------------------------
    // What the shared widgets use, so a panel that reaches for the role instead of a number follows
    // the scale without knowing it. A panel with a literal gap has opted out of the system.

    /// <summary>Vertical gap between rows or cards in a list (<see cref="ScrollList"/>). Was 3, which
    /// is why every list read as one slab with hairlines.</summary>
    public const int RowGap = 8;

    /// <summary>Extra space above a titled section (<see cref="SectionRule"/>), on top of the
    /// container's own separation, so a header belongs to what follows it and not to what precedes it.</summary>
    public const int SectionGap = SpaceMd;

    /// <summary>Gap between chips, in a row or wrapped.</summary>
    public const int ChipGap = SpaceSm;

    /// <summary>Gap between cells of a slot or stat grid.</summary>
    public const int GridGap = SpaceSm;

    /// <summary>Inner margin of a full-screen panel's frame (<see cref="Padding"/> adds 2 on the sides).</summary>
    public const int PanelPad = SpaceMd;

    /// <summary>Gap between stacked text lines inside one card or row (a title over its caption).</summary>
    public const int LineGap = Space2xs;

    /// <summary>Minimum height of a button, tab or menu entry: comfortable for a controller cursor and
    /// a thumb on a handheld. Was 38.</summary>
    public const int ControlHeight = 44;

    /// <summary>Clear space kept between a scrolling list and its scrollbar.</summary>
    public const int ScrollGutter = SpaceMd;

    /// <summary>Gap between the stacked widgets of one HUD corner (party card over vitals, toasts under the
    /// tracker) and between the HUD bar's cells. HUD cards sit on the live world, so they need more air
    /// between them than the rows inside one panel do.</summary>
    public const int HudGap = SpaceMd;

    /// <summary>Content margins of a HUD card, toast or hint (<see cref="Compact(StyleBoxFlat)"/>): tighter
    /// than a list card's <see cref="SpaceMd"/> all round, because these are glanced at, not read.</summary>
    public const int CompactPadY = SpaceSm;

    /// <summary>See <see cref="CompactPadY"/>.</summary>
    public const int CompactPadX = SpaceMd;

    // --- Radii -----------------------------------------------------------------
    // Tight radii throughout: this world's surfaces are cut and bound, not moulded. A large
    // radius is the fastest way to make a fantasy panel read as a web app.
    public const int RadiusSm = 1;
    public const int RadiusMd = 2;
    public const int RadiusLg = 2;

    // --- Motion tokens -----------------------------------------------------------
    // Durations in seconds; always route through Duration() so the reduced-motion
    // accessibility setting (Settings.ReducedMotion) collapses animation to instant.
    public const float DurationFast = 0.12f;
    public const float DurationBase = 0.20f;
    public const float DurationSlow = 0.35f;

    /// <summary>A tab or hub-screen switch: quicker than a panel opening, because the player is
    /// already inside the screen and asked for the next page of it.</summary>
    public const float DurationTab = 0.16f;

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

    /// <summary>
    /// The role a piece of text is actually set in. With the readable-font setting on, the carved
    /// capitals and both book serifs give way to the interface face: Cinzel has no lower case and
    /// a Garamond is thin at small sizes, and both are what a dyslexic or low-vision player asks to
    /// be rid of. Sizes, colours and layout are untouched. Pure, so the rule is unit-tested.
    /// </summary>
    public static FontRole ResolveRole(FontRole role, bool readable) => readable ? FontRole.Interface : role;

    private static FontFile? FontFor(FontRole role) => ResolveRole(role, ReadableFont) switch
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

    /// <summary>A low-chrome authored band for HUD readouts and dense list rows. It carries
    /// hierarchy through a semantic edge and baseline instead of enclosing every fact in a box.</summary>
    public static PanelContainer Band(Color? edge = null)
    {
        var band = new PanelContainer();
        StyleBoxFlat style = CardStyle(edge);
        style.BgColor = CardBg with { A = 0.72f };
        style.BorderColor = edge ?? (IronLit with { A = 0.32f });
        style.BorderWidthBottom = 1;
        band.AddThemeStyleboxOverride("panel", style);
        return band;
    }

    /// <summary>An icon and two-level text lockup used by HUD facts, transactions and alerts.</summary>
    public static HBoxContainer IconLabel(UiIcon.Kind icon, string primary, string? secondary = null, Color? tint = null)
    {
        Color color = tint ?? Text;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", SpaceSm);
        row.AddChild(UiIcon.Create(icon, 20f, color));

        var copy = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        copy.AddThemeConstantOverride("separation", LineGap);
        copy.AddChild(Body(primary));
        if (!string.IsNullOrEmpty(secondary))
        {
            copy.AddChild(Caption(secondary, Dim));
        }
        row.AddChild(copy);
        return row;
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

    /// <summary>The scrim behind the in-game hub (character, spellbook, journal, map, bestiary):
    /// <see cref="ScrimBg"/> at the one opacity all five share, so switching tabs never changes how
    /// much of the paused world shows through. Flat on purpose; there is no blur.</summary>
    public static readonly Color ScrimHub = new(0.035f, 0.032f, 0.028f, 0.80f);

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
    /// A frameless sheet: a column of content laid straight on a scrim, with one lit rule down its
    /// leading edge. The shell's ground (title, pause, death) in place of a boxed panel on a picture.
    ///
    /// <c>Root</c> is full-rect and goes under the screen's layer; <c>Column</c> takes the content.
    /// The column is <paramref name="width"/> wide and vertically centred, 8% in from the left edge
    /// (so it tracks the viewport rather than hugging an ultrawide's border) or centred when
    /// <paramref name="centred"/> is set. The scrim stops the mouse; pass an opacity of 0 for a
    /// sheet over a painted backdrop that needs no dimming.
    /// </summary>
    public static (Control Root, VBoxContainer Column) Sheet(float width = 420f, float scrimOpacity = 0.72f, bool centred = false)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect scrim = Scrim(scrimOpacity);
        scrim.MouseFilter = Control.MouseFilterEnum.Stop;
        root.AddChild(scrim);

        float anchor = centred ? 0.5f : 0.08f;
        var frame = new MarginContainer
        {
            AnchorLeft = anchor,
            AnchorRight = anchor,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = centred ? -width * 0.5f : 0f,
            OffsetRight = centred ? width * 0.5f : width,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddThemeConstantOverride("margin_top", SpaceXl);
        frame.AddThemeConstantOverride("margin_bottom", SpaceXl);
        root.AddChild(frame);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", SpaceLg);
        frame.AddChild(row);

        row.AddChild(new ColorRect
        {
            Color = HighContrast ? RuleLit with { A = 1f } : RuleLit,
            CustomMinimumSize = new Vector2(HighContrast ? 3f : 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        });

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        column.AddThemeConstantOverride("separation", SpaceSm);
        row.AddChild(column);
        return (root, column);
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
        Vector2 view = shell.GetViewportRect().Size;
        int gutter = UiChromeRules.Gutter(view.X);

        // A UiPanel draws its footer legend in the bottom gutter, beside this shell. The gutter is
        // already tall enough on a desktop viewport; on a narrow one the shell gives up the few
        // pixels the legend's row needs so the two never overlap.
        // A hub screen draws the hub strip in the top gutter on the same terms.
        UiPanel? panel = shell.GetParent() as UiPanel;
        bool legend = panel != null;
        bool hub = panel is { ReservesHub: true };

        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.OffsetLeft = gutter;
        shell.OffsetTop = UiChromeRules.TopInset(gutter, hub, view.Y);
        shell.OffsetRight = -gutter;
        shell.OffsetBottom = -UiChromeRules.BottomInset(gutter, legend);
    }

    /// <summary>A responsive authored workspace: wider than a dialog, quieter than full-screen.</summary>
    public static void ApplyWorkspace(Control shell, float widthFraction = 0.72f)
    {
        float side = (1f - Mathf.Clamp(widthFraction, 0.55f, 0.92f)) * 0.5f;
        shell.AnchorLeft = side;
        shell.AnchorRight = 1f - side;
        shell.AnchorTop = 0.08f;
        shell.AnchorBottom = 0.92f;
        shell.OffsetLeft = 0f;
        shell.OffsetRight = 0f;
        shell.OffsetTop = 0f;
        shell.OffsetBottom = 0f;
        shell.GrowHorizontal = Control.GrowDirection.Both;
        shell.GrowVertical = Control.GrowDirection.Both;
        shell.CustomMinimumSize = new Vector2(0f, 360f);
    }

    /// <summary>The logical width a full-screen panel has to lay out inside, after its gutter and
    /// the standard padding. The number every adaptive column count should be derived from.</summary>
    public static float UsableWidth(Control shell)
    {
        float width = shell.GetViewportRect().Size.X;
        int gutter = UiChromeRules.Gutter(width);
        return Mathf.Max(320f, width - (gutter * 2f) - ((PanelPad + 2f) * 2f));
    }

    /// <summary>
    /// The logical height a centred panel may occupy. The Steam Deck at UI scale 1.5 reports a
    /// **533 px** logical viewport — short enough that fixed heights authored against a desktop
    /// window overflow vertically even when the width fits comfortably. Width is the obvious axis
    /// to check and height is the one that actually bites on a handheld.
    /// </summary>
    public static float UsableHeight(Control control) =>
        Mathf.Max(240f, control.GetViewportRect().Size.Y - (SpaceXl * 2f));

    /// <summary>
    /// A clickable card: content that sizes itself, with a transparent button laid over it for
    /// input, hover and focus.
    ///
    /// ⚠️ **Use this instead of putting children inside a <see cref="Button"/>.** A `Button` is not
    /// a `Container` — it never grows to fit its children, so anchored content inside one collapses
    /// onto itself. 37.5H shipped exactly that bug twice: race cards declared `(160, 0)` had *zero*
    /// height and every label drew on top of the next, and the spellbook's school rows were pinned
    /// to a hand-guessed 44 px that two lines of text overran the moment the text-scale setting
    /// moved. A `PanelContainer` sizes to its content; the button only has to catch the clicks.
    ///
    /// The button is added last so it sits above the content and takes the input, and its normal
    /// state is fully transparent so the card underneath supplies the whole look.
    /// </summary>
    public static PanelContainer CardButton(Color? edge, out Button input, out VBoxContainer content, StyleBoxFlat? frame = null)
    {
        var card = new PanelContainer();
        // A caller with a smaller card (a grid node) passes its own frame so the hover and focus rings below are
        // sized from the margins that card really has, not from a list row's.
        StyleBoxFlat style = frame ?? CardStyle(edge);
        card.AddThemeStyleboxOverride("panel", style);

        // The card's own content margins are the padding. A second Padding inside it doubled every edge
        // (26 px above and below one line of text), and because a PanelContainer insets its children by
        // those margins, the button's focus ring was drawn inside the card as a second frame.
        content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", LineGap);
        card.AddChild(content);

        input = new Button { Flat = true, FocusMode = Control.FocusModeEnum.All };

        var clear = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0f) };
        var hover = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.05f) };
        hover.SetCornerRadiusAll(RadiusSm);

        var focus = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0f), BorderColor = Accent };
        focus.SetBorderWidthAll(1);
        focus.SetCornerRadiusAll(RadiusSm);

        // The button fills the card's content box, so hover and focus grow outward by the card's margins
        // to cover the whole card and sit on its edge instead of floating inside it.
        foreach (StyleBoxFlat box in new[] { hover, focus })
        {
            box.ExpandMarginLeft = style.ContentMarginLeft;
            box.ExpandMarginRight = style.ContentMarginRight;
            box.ExpandMarginTop = style.ContentMarginTop;
            box.ExpandMarginBottom = style.ContentMarginBottom;
        }

        input.AddThemeStyleboxOverride("normal", clear);
        input.AddThemeStyleboxOverride("hover", hover);
        input.AddThemeStyleboxOverride("pressed", hover);
        input.AddThemeStyleboxOverride("focus", focus);
        card.AddChild(input);

        // The button only fills the card's content box, so a click on the card's outer band would do nothing
        // while the hover ring promises the whole card: forward a left click anywhere on the card to the button.
        Button forwardTo = input;
        card.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !forwardTo.Disabled)
            {
                forwardTo.GrabFocus();
                forwardTo.EmitSignal(BaseButton.SignalName.Pressed);
            }
        };
        return card;
    }

    /// <summary>Gives a card or band the compact HUD margins. The stylebox is the padding, so the content is
    /// added straight to the card: wrapping it in <see cref="Padding"/> as well doubles every edge.</summary>
    public static StyleBoxFlat Compact(StyleBoxFlat box)
    {
        box.ContentMarginTop = CompactPadY;
        box.ContentMarginBottom = CompactPadY;
        box.ContentMarginLeft = CompactPadX;
        box.ContentMarginRight = CompactPadX;
        return box;
    }

    /// <summary><see cref="Compact(StyleBoxFlat)"/> for a <see cref="Card"/> or <see cref="Band"/> already built.</summary>
    public static PanelContainer Compact(PanelContainer card)
    {
        if (card.GetThemeStylebox("panel") is StyleBoxFlat box)
        {
            Compact(box);
        }

        return card;
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
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Pass };
        ApplyType(label, FontRole.Interface, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    /// <summary>Prose in the book serif — dialogue bodies, codex pages, narration. Wraps by
    /// default, because everything this builder is for is a paragraph.</summary>
    public static Label Prose(string text, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        ApplyType(label, FontRole.Serif, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    /// <summary>Item flavour and asides, in the serif italic.</summary>
    public static Label Flavour(string text, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        ApplyType(label, FontRole.SerifItalic, BodyFontSize);
        label.AddThemeColorOverride("font_color", color ?? Dim);
        return label;
    }

    /// <summary>
    /// A small secondary line (slot numbers, hints, metadata).
    ///
    /// ⚠️ <b>Every text builder above and below sets <see cref="Control.MouseFilterEnum.Pass"/>, and
    /// that one word is why a tooltip on a label works at all</b> (Phase 38U). A Godot 4
    /// <see cref="Label"/> defaults its filter to <c>Ignore</c> — unlike every other Control — so it is
    /// never the node under the cursor and <c>TooltipText</c> on one is <em>silently</em> dead. Every
    /// <c>label.TooltipText</c> in this repo had been dead since it was written: the vendor row's item
    /// description, and both of <c>InventoryPanel</c>'s. <c>Pass</c> rather than <c>Stop</c> because a
    /// label is decoration — it must become hoverable without becoming clickable, or a label laid over
    /// a card would start eating presses meant for what is behind it.
    /// </summary>
    public static Label Caption(string text, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Pass };
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
        wrap.AddChild(RuleRect(Engrave with { A = 0.75f }, 1f));
        wrap.AddChild(RuleRect(BrassLit with { A = 0.28f }, 1f));
        return wrap;
    }

    /// <summary>A titled section break: a header with an engraved rule running out to the right.
    /// The workhorse for giving a long panel readable structure. The first section of a container
    /// passes <c>first: true</c> so it does not add a gap above itself.</summary>
    public static Control SectionRule(string text, bool first = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", SpaceMd);
        row.AddChild(Header(text));

        Control rule = Divider();
        rule.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rule.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(rule);

        // The space above is part of the section, so every caller gets a gap without adding a spacer.
        var section = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        section.AddThemeConstantOverride("margin_top", first ? 0 : SectionGap);
        section.AddChild(row);
        return section;
    }

    /// <summary>A row of chips or small buttons that wraps instead of widening its parent: <see cref="ChipGap"/>
    /// across, <see cref="SpaceXs"/> down. A plain <c>HBoxContainer</c> of chips reports the sum of their widths as
    /// its minimum, so three affixes on one item could stretch a whole panel past the viewport.</summary>
    public static HFlowContainer FlowRow()
    {
        var row = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("h_separation", ChipGap);
        row.AddThemeConstantOverride("v_separation", SpaceXs);
        return row;
    }

    private static ColorRect RuleRect(Color color, float height)
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

        // Hidden until a caller wants it: an empty label still takes its separation, which padded every
        // plain chip with a dead SpaceXs on its right edge.
        trailing = Caption("");
        trailing.Visible = false;
        row.AddChild(trailing);
        chip.AddChild(row);
        return chip;
    }

    private static PanelContainer ChipShell(Color color)
    {
        var box = new StyleBoxFlat { BgColor = WellBg, BorderColor = color with { A = 0.55f } };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(Space2xs);
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

    /// <summary>The standard button. <paramref name="cue"/> is what a press sounds like: the plain
    /// click unless the caller knows the press means more (<see cref="UiCue.Confirm"/> for a
    /// commit, <see cref="UiCue.Back"/> for a way out).</summary>
    public static Button Action(string text, UiCue cue = UiCue.Click)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0f, ControlHeight),
        };
        ApplyInteractiveStyle(button);
        ApplyType(button, FontRole.Display, BodyFontSize);
        // Phase 31C: one seam gives every menu button its click.
        if (cue == UiCue.Click)
        {
            button.Pressed += PlayUiClick;
        }
        else
        {
            button.Pressed += () => UiAudio.Play(cue);
        }

        return button;
    }

    /// <summary>Plays the shared UI click through <see cref="UiAudio"/>, which lives as long as the
    /// application does, so a button on the title screen sounds like one inside a session.</summary>
    private static void PlayUiClick() => UiAudio.Play(UiCue.Click);

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
        root.AddThemeConstantOverride("separation", LineGap);

        Label caption = Caption(label);
        ProgressBar bar = Bar(fill, width);
        root.AddChild(caption);
        root.AddChild(bar);
        return (root, caption, bar);
    }

    /// <summary>A labelled on/off switch (settings rows). Caller wires <c>Toggled</c>. The switch
    /// itself, like the slider's track and the dropdown's list, is drawn from <see cref="UiSkin"/>.</summary>
    public static CheckButton Toggle(bool value)
    {
        var check = UiSkin.Apply(new CheckButton { ButtonPressed = value });
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
            CustomMinimumSize = new Vector2(width, 20f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        return UiSkin.Apply(slider);
    }

    /// <summary>An enumerated chooser (window mode, FPS cap, difficulty). Caller wires
    /// <c>ItemSelected</c>.</summary>
    public static OptionButton Dropdown(string[] options, int selected)
    {
        // The list it drops is a window of its own: no override on the button reaches it, so it is
        // handed the skin directly as well as through the default theme (UiSkin).
        var option = UiSkin.Apply(new OptionButton());
        UiSkin.Apply(option.GetPopup());
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
        UiSkin.Apply(scroll); // its scrollbars, and any default-themed control in its rows

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", RowGap);

        // The scrollbar overlays the right edge of the content, so without a gutter it sits on top of
        // every row's border. The margin is inside the scroll, so the bar stays at the panel edge.
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", ScrollGutter);
        gutter.AddChild(list);
        scroll.AddChild(gutter);
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
            BorderColor = HighContrast ? IronLit : PanelBorder,
        };
        box.SetBorderWidthAll(HighContrast ? 2 : 1);
        box.BorderWidthTop = HighContrast ? 4 : 2;
        box.SetCornerRadiusAll(RadiusLg);

        // A weighted hanging shadow makes the surface feel like forged plate instead of a web card.
        box.ShadowColor = new Color(0f, 0f, 0f, HighContrast ? 0.74f : 0.55f);
        box.ShadowSize = HighContrast ? 8 : 14;
        box.ShadowOffset = new Vector2(0f, 6f);
        return box;
    }

    /// <summary>The recessed stylebox — darker ground, and the bright edge on the *top* only, so
    /// the light reads as falling into a cut rather than off a raised lip.</summary>
    public static StyleBoxFlat WellStyle()
    {
        var box = new StyleBoxFlat { BgColor = WellBg, BorderColor = Iron with { A = 0.92f } };
        box.SetBorderWidthAll(1);
        box.BorderWidthTop = 2;
        box.SetCornerRadiusAll(RadiusSm);
        return box;
    }

    /// <summary>The raised-row stylebox. <paramref name="edge"/> paints the left spine that
    /// carries a row's category colour.</summary>
    public static StyleBoxFlat CardStyle(Color? edge = null)
    {
        var box = new StyleBoxFlat { BgColor = CardBg, BorderColor = edge ?? (IronLit with { A = 0.26f }) };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = edge is null ? 0 : HighContrast ? 4 : 2;
        box.BorderWidthBottom = 1;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(SpaceMd);
        box.ContentMarginLeft = edge is null ? SpaceMd : SpaceLg;
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
        button.AddThemeStyleboxOverride("normal", ButtonStyle(ButtonFace));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(ButtonFaceHover, Accent));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(ButtonFacePressed, AccentHot));

        StyleBoxFlat focus = ButtonStyle(ButtonFaceFocus, Accent);
        focus.BorderColor = Accent;
        focus.BorderWidthLeft = 3;
        focus.BorderWidthBottom = 1;
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

    private static StyleBoxFlat ButtonStyle(Color color, Color? edge = null)
    {
        var box = new StyleBoxFlat { BgColor = color, BorderColor = edge ?? (Iron with { A = 0.7f }) };
        box.SetCornerRadiusAll(RadiusMd);
        box.BorderWidthLeft = edge is null ? 1 : 3;
        box.BorderWidthBottom = 1;
        box.SetContentMarginAll(SpaceSm);
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceMd;
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
