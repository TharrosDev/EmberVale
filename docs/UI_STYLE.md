# Embervale — UI Style Guide

The source of truth for every UI surface. Tokens live in code at `src/UI/UiTheme.cs` and its
per-lane partials `src/UI/UiTheme.<Lane>.cs` (ornament at `src/UI/UiOrnament.cs`); this document
explains what they mean and how to use them. It answers to the art bible (`docs/ART_STYLE.md`): the UI is part of the same
dying world — **ash neutrals, bone-pale text, ember accents** — not a chrome layer
floating above it.

*Phase 30.5A established the token system. Phase 37.5A gave it a material and a voice:
three vendored typefaces, a grain shader, an engraved brass frame, three surface depths,
and the semantic ramps the UI had been going without. The 2026-10 UI upgrade ("banked embers")
turned boxed panels into cut plates with one lit edge and added the motion, sound, glyph, legend,
hub, HUD-option and icon seams; §13 is the map of all of it.*

## 1. Identity

The world is *beautiful but dying*; the UI reads like objects from it — scorched
parchment, cooled iron, a candle held up to both. Five rules follow:

1. **The UI is faded, not flat.** Surfaces are warm charcoal ash (never blue-black);
   text is bone pale (never pure white). Nothing on screen is fully saturated except
   accents.
2. **Ember is THE accent.** Ember gold (`Accent`) marks headers, highlights, selection
   and focus. Ember orange (`AccentHot`) is rationed for the hottest emphasis — crits,
   warnings, the Flamebearer thread. If everything glows, nothing does.
3. **Corruption is violet.** The corruption gauge, vignette and any corruption-tinted UI
   use the bible's corruption violet — the one cold accent allowed to compete with ember.
4. **Brass is material; gold is meaning.** (37.5A) The frame around a thing must never
   read as loud as the thing. `Brass`/`BrassLit` are duller and browner than `Accent` on
   purpose. A frame that needs to matter more gets an *ornament*, never a brighter rule.
5. **One lit edge per surface.** (2026-10) A surface is a cut iron plate: a ground, a cold `Rule`
   between the things on it, and a single `RuleLit` edge where the fire catches. Four bright
   borders make a box; on the live HUD there is often no ground at all, only a `Keyline`.

**Aged, not opulent.** Every material decision resolves that way. This is a world in
decline; its interfaces are well-made objects that have been used for a long time.

## 2. Palette tokens

*Every value below was regenerated from `src/UI/UiTheme.cs` and its lane partials on 2026-10-06. The
code is the authority: when a value here and a value there disagree, this table is the one to fix.*

### Surfaces — three depths

A control is understood by whether it sits *in* the panel or *on* it. Getting this
wrong is the fastest way to make a screen read as an undifferentiated wall of rows.

| Token | Value (sRGB) | Reads as | Use |
| ----- | ------------ | -------- | --- |
| `WellBg` | `0.030, 0.031, 0.030 @ 0.97` | cut **into** the panel | item slots, troughs, input wells, chip grounds |
| `PanelBg` | `0.060, 0.058, 0.052 @ 0.96` | the ground | every panel, plate and sheet surface |
| `CardBg` | `0.098, 0.092, 0.081 @ 0.94` | sat **on** the panel | item rows, spell cards, save slots |
| `Trough` | `= WellBg` | the empty part of a bar | every bar background (it once sat a rounding error from `CardBg` and an empty bar was invisible) |

All three are properties, not fields: high contrast returns them fully opaque.

### Text

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `Text` | `0.84, 0.80, 0.72` | primary text (bone pale) |
| `Dim` | `0.62, 0.60, 0.54` | secondary text |
| `Disabled` | `0.40, 0.385, 0.35` | unavailable controls, unmet requirements |

`Disabled` is **deliberately not AA-pinned** — WCAG exempts disabled controls, and a
greyed row that reads as strongly as a live one gets clicked. It is held in a band
(≥2:1, <4.5:1, and always below `Dim`) by `DisabledStaysPerceivableWithoutReachingAa`.
Disabled state must always carry a second channel too: a reason string, a struck price.

### Accents, feedback, vitals, corruption

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `Accent` | `0.85, 0.64, 0.25` | headers, highlight, selection (ember gold) |
| `AccentHot` | `0.91, 0.45, 0.17` | crits/warnings only (ember orange) |
| `Good` | `0.55, 0.68, 0.44` | semantic feedback, dead green (colour-vision adapted) |
| `Bad` | `0.82, 0.42, 0.36` | semantic feedback, ashen red (colour-vision adapted) |
| `Health` | `0.78, 0.30, 0.26` | health fill |
| `Stamina` | `0.80, 0.66, 0.30` | stamina fill |
| `Mana` | `0.42, 0.56, 0.76` | mana fill |
| `Poise` | `0.62, 0.66, 0.72` | poise fill on a target or enemy plate: cold steel beside the warm health red (`UiTheme.HudCombat.cs`) |
| `Corruption` | `0.48, 0.30, 0.55` | corruption gauge + vignette (violet, fills only) |
| `CorruptionText` | `0.68, 0.48, 0.76` | corruption-tinted **text** (the fill violet fails AA; colour-vision adapted) |

### Iron, brass and the plate tokens

A surface is a cut iron plate with **one** lit edge, not a box with four (the 2026-10 upgrade,
"banked embers"). The engraved read still comes from **two values at different depths**: a rule with
a dark groove or shadow behind it. A single border of any colour or width reads as a box.

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `PanelBorder` | `0.34, 0.36, 0.34 @ 0.84` | the iron rule round `Panel()`: 1 px, 2 px along the top |
| `Iron` | `0.28, 0.30, 0.29` | a well's edge, slider tracks, unlit perk connectors and pips |
| `IronLit` | `0.46, 0.47, 0.43` | a card's baseline, a band's edge, the high-contrast panel rule |
| `Ash` | `0.36, 0.34, 0.30` | the neutral ground of a settings or first-run sample swatch |
| `Brass` | `0.55, 0.44, 0.26` | material, never meaning: the grain's weathering tint, trade pins on the map, spent boss-phase pips |
| `BrassLit` | `0.68, 0.56, 0.34` | divider highlights, lit perk connectors, corner ornaments |
| `Engrave` | `0.045, 0.042, 0.038 @ 0.90` | the dark groove under a bright rule; the shadow under a hub plate |
| `Rule` | `0.30, 0.31, 0.29 @ 0.60` | the cold hairline between rows, columns and tabs on one surface |
| `RuleLit` | `0.66, 0.56, 0.40 @ 0.90` | the single lit edge of a plate or sheet; at most one per surface |
| `Keyline` | `0.020, 0.019, 0.017 @ 0.88` | the dark 1 px outline round HUD bars, icons, glyphs and text drawn on the live world |
| `FocusRing` | `0.98, 0.80, 0.42` | the focus indicator and nothing else; brighter than `Accent`, 3:1 or better on every surface (`UiContrastTests`) |
| `EmberGlow` | `0.95, 0.50, 0.16 @ 0.35` | translucent heat under a lit edge, a wipe or a lit perk route; never text |

### Button faces, scrims, arcane

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `ButtonFace` | `0.10, 0.095, 0.085 @ 0.82` | `Action` / `Dropdown` at rest |
| `ButtonFaceHover` | `0.18, 0.155, 0.115 @ 0.96` | hovered |
| `ButtonFacePressed` | `0.07, 0.065, 0.06 @ 0.98` | pressed |
| `ButtonFaceFocus` | `0.15, 0.13, 0.10 @ 0.98` | focused |
| `ScrimBg` | `0.035, 0.032, 0.028` | full-screen dimming behind an overlay screen; `Scrim(opacity)` sets the alpha |
| `ScrimHub` | `0.035, 0.032, 0.028 @ 0.90` | the one scrim every hub and trade screen shares, so stepping tabs never changes how much world shows; raised from 0.80 so the HUD behind a screen recedes |
| `ArcaneGround` | `0.072, 0.070, 0.095 @ 0.94` | the spellbook's cold ink-violet ground |
| `ArcaneSilver` | `0.62, 0.66, 0.74` | the spellbook's tarnished frame |
| `GlyphLight` | `0.68, 0.74, 0.92` | rune circles, sigils, glyph light |

The button faces are public so `UiContrastTests` reads the real values; it used to carry its own
copies, and they had drifted lighter than anything on screen.

### HUD grounds (`UiTheme.Hud.cs`)

Derived from the tokens above, so a retune of `PanelBg` or `Keyline` carries. High contrast makes
each ground opaque.

| Token | Value | Use |
| ----- | ----- | --- |
| `HudPlateBg` | `PanelBg @ 0.82` | a HUD plate (tracker, prompt, toast, hint, target plate, save chip) |
| `HudShadeBg` | `PanelBg @ 0.58` | a HUD shade: one line of text that must read over sky (clock, boss name) |
| `HudSlotBg` | `WellBg @ 0.62` | a hotbar cell; an empty cell keeps `HudSlotQuiet` (0.3) of it |
| `HudHalo` | `Keyline @ 0.38` | the soft halo behind HUD text, `HudHaloSize` 6 px wide |
| `HudInnerEdge` | `IronLit @ 0.55` | the lighter edge just inside a keyline |
| `HudChunk` | `Text @ 0.80` | the length of bar a hit has just removed, before it closes |
| `HudWipe` | `ScrimBg @ 0.72` | the dark wedge of a cooldown still to run |

`HudInkSize` is 3 px: the keyline outline `HudInk` gives HUD text.

### Map plot (`UiTheme.Knowledge.cs`)

The full map and the minimap share one view, so both draw from these.

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `MapDeep` | `0.030, 0.030, 0.027` | uncharted ground under the whole plot |
| `MapVellum` | `0.30, 0.275, 0.235 @ 0.62` | the tint the vellum texture is drawn through |
| `MapLandLow` / `MapLandHigh` | `0.150, 0.138, 0.116` / `0.262, 0.238, 0.196` | charted land at its lowest and highest; relief shades between |
| `MapLand` | `0.19, 0.172, 0.144 @ 0.86` | a cell's footprint where a region has no baked relief |
| `MapWater` | `0.095, 0.150, 0.185` | standing water |
| `MapRoad` | `0.62, 0.575, 0.49` | a road's worn core, over an `Engrave` shoulder |
| `MapTrack` | `0.47, 0.44, 0.38` | a track or lane |

### Semantic ramps (37.5A)

**Rarity** — `UiTheme.RarityColor` delegates to `ItemRarities.Color`
(`src/Items/ItemType.cs`), which is the single authority; the world-space drop glow and
trophy tint read the same values, so an item cannot look one rarity on the ground and
another in the pack.

| Tier | Value (sRGB) | Reads as |
| ---- | ------------ | -------- |
| Common | `0.60, 0.58, 0.52` | ash bone — near `Dim`, and meant to be |
| Uncommon | `0.52, 0.70, 0.47` | sage |
| Rare | `0.56, 0.72, 0.90` | cold steel |
| Epic | `0.84, 0.72, 0.95` | aged amethyst |
| Legendary | `0.99, 0.86, 0.55` | white-hot ember |

Three guarantees, pinned by `RarityRampTests`, and any retune must keep all three:
**luminance climbs strictly with rarity** (so the ramp orders in greyscale and survives
a colourblind player), **adjacent tiers stay ≥1.15:1 apart** (monotonic is not enough if
the steps are invisible), and **Legendary out-burns `Accent`** (or the rarest drop in the
game reads as no louder than a section header). This is why the ramp is paler than a
stock one: hue carries the flavour, luminance carries the rank.

Colour is never the only channel — `UiTheme.RarityBorderWidth` thickens the slot frame at
Epic and above, and rarity is always available as a word.

**Magic school** — `SpellSchools.Color(DamageType)` is the authority (`UiTheme.SchoolColor`
delegates). It tints the projectile in flight *and* names the school in the spellbook, so the
two cannot drift. Retuned in 37.5B off the stock saturated set: the old Necrotic failed AA at
~2.6:1, and the old Fire was the most saturated thing in a deliberately desaturated world.
Arcane is silver-blue rather than violet — violet is the corruption identity (§1), and arcane
is the spellbook's own school, so it takes the glyph light.

**Reputation** — `ReputationTiers.Color(ReputationTier)`, a seven-step **diverging** ramp
(hostile red ← bone → allied blue). It deliberately carries no luminance ordering: a standing
always renders beside its own tier name and value, so the colour is already redundant. Rarity
on a grid slot often has no such words, which is why that ramp must work with hue removed and
this one need not.

**Quest state** — `QuestMain` (= `Accent`; the main thread is the Flamebearer thread),
`QuestSide` (`0.72, 0.74, 0.70`, a cool bone), `QuestComplete` (= `Good`), `QuestFailed` (= `Bad`).

**Disposition** — `Friendly` (= `Good`), `Neutral` (`0.74, 0.71, 0.60`), `Hostile` (= `Bad`).

### The three domain authorities

Rarity, school and reputation ramps live with their **domain**, not in `UiTheme`, and
`UiTheme` reads from them:

| Ramp | Authority | Why it lives there |
| ---- | --------- | ------------------ |
| Rarity | `ItemRarities.Color` (`src/Items`) | also tints the world-space drop glow and trophy stand |
| School | `SpellSchools.Color` (`src/Magic`) | also tints projectiles, impact flashes, cast flares, status particles |
| Reputation | `ReputationTiers.Color` (`src/Factions`) | keeps `src/Factions` free of a `src/UI` dependency |

**One authority per ramp, always.** 37.5A briefly shipped a second school ramp inside
`UiTheme` — different values from the one that had tinted projectiles since Phase 12 — which
would have meant a firebolt that was one orange in flight and another in the spellbook. 37.5B
deleted it. If you find yourself writing a colour switch on a domain enum, check whether that
domain already owns one.

### Rules

No colour literals in panels — new needs become new tokens here first.
Environment-style saturation discipline applies: only accents may exceed ~40% saturation.

**Contrast is audited in code:** `UiContrastTests` pins every text-on-surface token pair
to WCAG AA (≥4.5:1) and bar fills to ≥3:1 — including every ramp colour on both `CardBg`
and `PanelBg`, and the spellbook's tokens on `ArcaneGround`. Retune a token and the suite
fails before the player squints. Text colour goes through font colour overrides, **never
whole-control `Modulate`** (modulate multiplies onto already-dim fonts and sinks below
readable — the 30.5K audit caught two).

## 3. Typography

Three vendored SIL OFL faces (provenance and licence rationale in `assets/CREDITS.md`;
the `OFL-*.txt` files beside them must not be deleted). Reach for a **role**, never a
font directly.

| Role | Face | Use |
| ---- | ---- | --- |
| `FontRole.Display` | **Cinzel** | titles, headers, boss names, menu items, buttons |
| `FontRole.Interface` | **Inter** | body, captions, numbers, tooltips, settings |
| `FontRole.Serif` | **EB Garamond** | prose meant to be *read* — dialogue, codex, narration |
| `FontRole.SerifItalic` | **EB Garamond Italic** | item flavour, asides |

Cinzel is inscriptional Roman capitals — **carved, not calligraphic**. It is the
illuminated-manuscript influence expressed as stone rather than as script, which is the
distinction that keeps the UI from reading as a wedding invitation. Inter carries body
text specifically because a Garamond cannot hold the 12 px floor, and its tabular figures
stop stat columns shimmering as digits change.

Fonts are loaded **lazily** and fall back to the engine default on failure. This is
load-bearing twice over: `UiContrastTests` reads tokens with no engine running, and a
missing font must never stop the game drawing its menus.

| Size token | px | Use |
| ---------- | -- | --- |
| `CaptionFontSize` | 12 | slot numbers, hints, metadata — the legibility **floor** |
| `BodyFontSize` | 15 | default text |
| `HeaderFontSize` | 18 | section headers (ember gold) |
| `TitleFontSize` | 24 | screen/panel titles |
| `DisplayFontSize` | 32 | boss names, level-up, big moments |
| `ShoutFontSize` | 40 | a word thrown across the screen (combat feedback); nothing else |

**Always size through `UiTheme.FontSize(token)`, never the const directly.** It applies the
player's text-scale setting, and not evenly: sizes up to body take all of the change, a header
85%, a title 70%, display and above half, and nothing falls under the 12 px floor
(`ScaledFontSize`, pinned by `UiTypeScaleTests`). A 32 px boss name at a flat 1.5x pushed layouts
off a handheld screen and bought the player nothing.

Two rules from the 2026-10 upgrade: **Cinzel is for a label of three words or fewer**; a longer
name (an item, a station, a trade page title) takes the interface face. And the `ReadableFont`
setting sets every role in Inter (`UiTheme.ResolveRole`), so never fetch a font file directly.

Builders: `Title/Display/Header/Body/Prose/Flavour/Caption` — reach for these before raw
`Label`s. They pick the role for you.

## 4. Spacing & radius

**Breathing room is a rule, not a mood.** The UI had drifted into packed screens: list rows 3 px apart,
stat lines 1 px apart, chips with 2 px of padding, scrollbars sitting on row borders. The 2026-10 pass
widened the scale and moved the gaps into tokens, so a screen follows it by using the shared widgets
and the roles below instead of a number. Legibility comes first: when in doubt, more room, never less.

Scale (px at reference scale, `UiTheme`): `Space2xs` 4 (a label over its caption, an icon beside its
text) / `SpaceXs` 6 (inside one control) / `SpaceSm` 10 (between related controls) / `SpaceMd` 16
(panel padding, between groups) / `SpaceLg` 24 (between sections, narrow gutter, HUD safe margin) /
`SpaceXl` 32 (around a modal's content on a bare screen). Steps are named by size, never by use.

Roles, which the shared widgets already apply:

| Role | Value | Where it lands |
| ---- | ----- | -------------- |
| `RowGap` | 8 | `ScrollList` separation (was 3) |
| `SectionGap` | `SpaceMd` | space above every `SectionRule` |
| `ChipGap` | `SpaceSm` | between chips |
| `GridGap` | `SpaceSm` | between cells of a stat or slot grid |
| `PanelPad` | `SpaceMd` | a full-screen panel's inner margin (`Padding` adds 2 at the sides) |
| `LineGap` | `Space2xs` | stacked text lines in one card or row, `Meter`, `CardButton` |
| `ControlHeight` | 44 | minimum height of an `Action`, tab or menu entry (was 38) |
| `ScrollGutter` | `SpaceMd` | clear space between a list and its scrollbar |
| `HudGap` | `SpaceMd` | between stacked HUD widgets (party over vitals) and between the HUD bar's cells (`SpaceLg`) |
| `CompactPadY` / `CompactPadX` | `SpaceSm` / `SpaceMd` | content margins of a HUD card, toast or hint (`UiTheme.Compact`) |

Rules: (1) no literal gap, margin or padding below 4 px in a panel; a literal is a reason to add a token.
(2) A card's own content margins are its padding, so never wrap a `Card` or `Band` child in a second
`Padding`; that doubles the left edge. (3) Rows in a list are separated by `RowGap`, sections by
`SectionGap`, and nothing sits flush against a panel edge or a scrollbar. (4) A control the player
presses is at least `ControlHeight` tall. (5) Every screen still has to fit 1280x720: a list that grows
past the panel scrolls (`ScrollList`), the frame never does. Radii: `RadiusSm` 1 (bars, wells, chips),
`RadiusMd` 2 (buttons), `RadiusLg` 2 (panels).

Rules from the items, trade and crafting pass (group 1):

- **A row is a text stack beside its verb.** Item, recipe, contract and trade rows are one `HBoxContainer` inside a `Card`: slot (if any), a text stack that expands (`LineGap` between its lines), then the button, `ShrinkCenter` against the whole stack. A button in the title line made its 44 px the row's top band and left dead air beside the title, and every recipe or contract row at least 100 px tall.
- **Chips and small button rows wrap.** Use `UiTheme.FlowRow()` (`ChipGap` across, `SpaceXs` down), never an `HBoxContainer` of chips: a plain row reports the sum of its chips as its minimum width, so one item with three affixes stretched the stash window past the viewport. A name in a row takes `TextOverrunBehavior.TrimEllipsis` plus a tooltip instead of widening its column.
- **One frame per thing.** A `Card` does not go inside a `Band` (two frames, two left spines, 32 px of width lost): the item detail and each trade column sit bare on the panel, and a list names itself with a `Header` above it. `Padding()` is for a screen's outer margin only; a card's content margins are its padding.
- **The first section of a container passes `first: true`** to `SectionRule`, so the gap above it is not added on top of the tab strip or the column above. Equipment and backpack headers do this, which is what lets the ten equipment rows fit 1280x720 without scrolling.
- **A fixed height is a bug.** Lists use `ScrollList()` and the shell's own floor (`ApplyWorkspace` / `ApplyScreenInset`) sets the minimum; a literal `CustomMinimumSize` height on a list or column is how the shop once overflowed the viewport.
- **Stat blocks are a row of columns.** The Progression sections (attributes, offence, defence, corruption, standing) are 320 px columns in a wrapping `FlowRow`, so three sit side by side at 1280 wide and fold under each other on a narrow handheld viewport. Stat grids use `GridGap` between rows.
- **Slots draw their frame.** `ItemSlot.Build` is not a flat `Button` (a flat button never paints its `normal` stylebox, so the rarity frame and the empty well were skipped). `ItemSlot.RowSize` (40) goes beside card rows, `ItemSlot.CompactSize` (34) beside two stacked lines with no taller control, `DefaultSize` in grids.

Panel pass 2 (journals, spellbook, map, dialogue, banner) turned those rules into patterns a list screen follows:

- **One frame per list.** A list of `Card`s sits on bare ground (a plain `VBoxContainer` with its own minimum width), never in a `Well`; a detail pane is one `Band` or `Card` whose scroll goes straight inside, with no second `Padding`.
- **`CardButton` pads once.** Its content margins are the card's own (`SpaceMd`, `SpaceLg` on the spine side), and hover and focus grow outward by those margins, so the focus ring sits on the card's edge instead of floating inside it as a second frame. It used to add a `Padding` as well: 26 px above and below one line of text, and a spellbook with six schools that ran off the page.
- **Rhythm inside a card or block.** A title and its caption, or a caption and its value, sit `LineGap` apart; a second line that is a different kind of thing (the chips under a dialogue choice) sits `SpaceSm` below; the next block sits `SpaceSm` to `RowGap` below that. Chips wrap at `ChipGap` both ways. Stat lines that belong together (a school's rank, bar and perks) are one `VBoxContainer` at `SpaceXs`.
- **Tabs and headers a player presses are `ControlHeight`**, not a literal 30 or 32.
- **Scroll before overflow.** A column that can outgrow the page (the spellbook's school list, the map rail, a dialogue's choices) scrolls through `ScrollList`; its minimum is built from `ControlHeight` and `RowGap` rows (`RailListMin` is three rows), never a pixel count, and a list that holds fewer rows sizes to them so a lone waypoint is not followed by a hole.
- **A footer row never pins its width.** Buttons, a readout and a hint share one `HBoxContainer`; the hint is the one `ExpandFill` child and wraps. A fixed label plus a spacer pushed the map's footer, and the whole panel with it, past the right edge at 1280 px.
- **Custom-drawn labels pad like tooltips**: `SpaceSm` at the sides, `SpaceXs` above and below the glyph box (the map's hover label).
- **The dialogue window is a lower third**: about three quarters of the view wide (720 to 1040 px, nearly the whole width on a handheld) and as tall as five option rows need, never more than 40% of the height. Its choices are one `RowGap` list on the right. The chapter banner's band is 24% of the height with `SpaceSm` between its lines.

**HUD, toasts and shell (pass 3).** (1) A HUD card is a `Band`/`Card` whose stylebox is its padding: call `UiTheme.Compact(card)` and add the content directly. The old `Padding` wrapped inside a stylebox that already had 16 px margins left about 26 px of dead space above and below every HUD card, and made the cards tall enough to collide. (2) Inside a card, group by distance: bars or rows of one kind sit `SpaceXs` apart, groups `SpaceSm` apart, a heading hugs its title at `LineGap`. A tracker objective is one block (text row, "Optional" tag on its own line, bar, hint) so its parts stay together and objectives stand apart. (3) Chips that can multiply (status effects) live in an `HFlowContainer` with `ChipGap`; a box row stretches the card. A chip's `trailing` label is hidden until used, since an empty label still takes its separation. (4) Toasts start under the tracker wherever it ends and stop above the minimap: `Notifications` reads `GameHud.TopRightBottom`/`BottomRightTop`, admits as many toasts as fit (1 to 3) and lets the oldest fade early if the tracker grows. Never place a toast stack with a fixed offset. (5) Anything centred above the hotbar (prompt, tutorial hint, placement strip) sits at `HudLayout.BottomClearance` or higher; that is in the scaled HUD's units, so a strip outside `HudLayout.Scaled` (the placement strip) converts it with `HudMetrics.ScreenClearance`. (6) A settings or slot list takes a row height of `ControlHeight` and `RowGap` between rows, scrolls inside the workspace frame, and gives the scroll a small floor so the frame keeps a visible margin from the window edge; a frame that is taller than its anchors only grows when something inside it has a large minimum height. (7) A shell button is `ControlHeight` tall; pause entries are not shrunk below it.

Check spacing by looking, not by arithmetic: the seven harnesses `--shellshots`, `--metashots`, `--hudshots`, `--combat-shots`, `--panelshots`, `--uishots` and `--tradeshots` capture every screen (§13.9 says what each covers). `EMBERVALE_RES=1920x1080` re-shoots at another window size (a 16:9 size lays out identically to 1280x720, because the project stretches `canvas_items`, so use `1280x800` with `EMBERVALE_SHOT_UISCALE=1.5` for the handheld's 853x533 logical view), `EMBERVALE_SLOT` picks the save and `EMBERVALE_USER_DIR` + `EMBERVALE_ARTIFACTS` keep a run's saves and PNGs out of the shared user folder.

Radii stay tight on purpose: this world's surfaces are cut and bound, not moulded. A
large radius is the fastest way to make a fantasy panel read as a web app.

## 5. Motion

Durations: `DurationFast` 0.12 s (hover/press feedback), `DurationBase` 0.20 s
(panel/value transitions), `DurationSlow` 0.35 s (screen transitions, banners).
**Always** route through `UiTheme.Duration(x)` — it returns 0 when the player has reduced
motion enabled, collapsing animation to instant. Easing: prefer ease-out for entrances,
ease-in for exits; no bounces (this world is tired).

Building blocks (use these, don't hand-roll):

- `UiMotion.EaseOut/EaseIn/Progress` — the pure curves (unit-tested); drive `_Process`
  timers with them.
- `UiTheme.AnimateModulate(control, target, seconds)` — a kill-previous, pause-proof
  modulate ease; the hover/press/focus feedback on every `Action`/`Dropdown` rides it.
- `UiPanel` fades its shell in on open for free; opening motion belongs there, not in
  subclasses. Closing is always instant — dismissal never lags input.
- `UiFx.FadeIn` / `FadeOut` / `Rise` / `Stagger` / `Pulse` for anything outside a `UiPanel`
  (shell sheets, HUD entrances, toasts), and `UiFx.HoldRing` for a hold to confirm. `DurationTab`
  (0.16 s) is a tab or hub-screen switch. §13.2 has the contract.
- Exits that reveal (the loading screen) fade out; entrances that cover are instant.
- **`UiTheme.MotionUniform`** (37.5A) is the same setting as a shader uniform. Every
  animated UI shader multiplies its time term by it, so one toggle stops the rune ring,
  the sigil drift and the heading shimmer together. A shader that animates without
  reading it is a bug.

Reduced motion removes *movement*, not *art*: the rune circle holds its start angle
rather than disappearing. The one exception is `InkShimmer`, which renders nothing —
a travelling highlight has nothing meaningful to show frozen.

## 6. Widgets

- `Panel()` / `Well()` / `Card(edge)` / `CardButton(edge, out input, out content)` — the depths.
  ⚠️ **A `Button` is not a `Container`.** It never grows to fit its children, so content anchored
  inside one collapses on top of itself. Anything clickable that holds more than a label uses
  `CardButton`, which is a `PanelContainer` (sizes to content) with a transparent button over it.
  ⚠️ **`Panel()` is a full screen, and nothing else.** It carries an iron rule (`PanelBorder`:
  1 px, 2 px along the top; `IronLit` and doubled under high contrast), a weighted hanging shadow
  *and its own grain `ShaderMaterial`*. A hub or trade screen swaps that stylebox for the cut
  plate (`UiTheme.ApplyHubPlate`: one `RuleLit` top edge, no box) and keeps the grain; a shell
  screen uses `UiTheme.Sheet` and no panel at all. Thirteen widgets across the overhaul had reused it
  as a generic box — status chips, save rows, toasts, the hotbar, the party strip, the tutorial
  hint, both debug overlays, and **five simultaneous `GameHud` widgets** (vitals, time/weather,
  quest tracker, event banner, interaction prompt). The HUD alone was rendering five brass frames
  and five grain shaders at once, more framing than the character screen uses. (The HUD has its
  own grounds since 2026-10: `HudBare`, `HudShade`, `HudPlate`; §13.4.)
  **A repeated or small widget takes `Card` (sits on) or `Well` (cut into). If you are reaching
  for a box, it is not a `Panel`.** `Card`'s `edge` paints a left
  spine in a semantic colour (rarity, school, quest state), which is how a list conveys
  category without a legend.
- `Padding()` — every framed surface's inner margin; modals set `UiState.MenuOpen`.
- `Title/Display/Header/Body/Prose/Flavour/Caption` — the seven text levels; colour via
  token parameters.
- `Divider()` / `SectionRule(text)` — engraved separators. `SectionRule` is the workhorse
  for giving a long panel readable structure.
- `Chip(text, color)` — a small tinted pill for an affix, status effect, school tag or
  filter. The *label* carries the colour and the ground stays near-neutral, so a row of
  chips does not become a paint chart.
- `IconSlot(size)` + `RarityFrame(rarity)` — the raw slot well and its rarity treatment.
  Most callers want **`ItemSlot.Build(instance, quantity, selected)`** instead, which composes
  the two with the item's picture (`ItemIcons.For`, else the category glyph), the stack count,
  its marks and a tooltip, and is a `Button` so it gets focus, hover and activation for free.
  `ItemSlot.Detail(instance, equipped, compare)` is its companion card; its anatomy and the
  compare view are in §13.5.
  ⚠️ A grid of these **must** call an explicit focus-neighbour pass, and that pass has to run
  *after* the grid is parented — `FocusNeighbor*` takes a NodePath and `GetPath()` throws
  outside the tree. Wiring it inside the builder errors every frame and still works under a
  mouse, which is the combination that ships.
- `Bar(fill)` / `Meter(label, fill)` — thin resource bar, and the captioned version that
  every non-HUD progress readout uses.
- `Action()` / `Dropdown()` — interactive controls share one style (normal/hover/pressed/
  **focus**); the ember focus border is the visibility layer the gamepad navigation
  (30.5J) rides. Never ship a control without a visible focus state.
- **Focus navigation (30.5J):** menus must work without a mouse. `UiPanel` grabs/restores
  focus for you (open + across rebuilds via `UiFocus`); a new standalone screen calls
  `UiFocus.GrabFirst` when it shows. Lists live in `ScrollList()` (sets `FollowFocus`).
  ui_cancel (Esc/B) closes a modal — opt out via `CloseOnCancel` only for panels with
  their own lifecycle. A **grid** must set explicit `FocusNeighbor*` or a d-pad walks the
  tab order instead of the grid.
- **Prompt glyphs:** any key hint, on the HUD or in a menu, is `UiGlyph.For(action)` (a keycap
  on keyboard, a drawn button on a pad) and is redrawn on `InputDeviceChangedEvent` and
  `InputBindingsChangedEvent` — never a hard-coded key name, because the player can rebind it.
- **Legend:** a screen's verbs go in its footer legend (`UiPanel.Legend`), not in a hint line of
  its own.
- Rebuild-from-dirty-flag in `_Process`, never inside a button signal (docs/RECIPES.md).

## 7. Material & ornament (37.5A)

**Material.** `UiTheme.ApplyGrain(control)` gives a surface the parchment/leather read.
`Panel()` applies it for you. The shader only *tints* what the stylebox already drew —
corners, borders and content margins stay Godot's job, because the engine already solves
rounding and 9-slicing and a shader reimplementation would be a worse copy that also has
to be told the rect size. Grain is sampled in **screen** pixels, not UV, so a toast and a
full-screen panel share one fibre density instead of stretching it. Each call builds its
own `ShaderMaterial`, so a screen can tune its own weathering without writing through to
every other surface.

**Ornament budget — the rule that stops this becoming clutter.** Decoration scales with
the **rarity of the moment**, not with the importance of the widget:

| Gets ornament | Gets none, forever |
| ------------- | ------------------ |
| the title, the spellbook, a screen's one opening wipe, a perk bought, a craft made, the death line | inventory rows, settings rows, quest objectives, tooltips, toasts, **the whole gameplay HUD** |

If every surface is ornamented the ornament stops meaning anything, and readability is
the first thing to go.

What the code spends today (amended 2026-10; grep `UiOrnament.` before adding a spend):

| Motif | Budget | Spent on |
| ----- | ------ | -------- |
| `EmberWipe()` | **one per screen**, under its heading | title, first-run setup, pause, death (drawn over the 0.8 s the frame darkens), creator, save slots, every trade page (replayed on open and on a craft), the perk tree (on a purchase) |
| `InkShimmer()` | one heading per screen | the title's wordmark, the spellbook's heading |
| `RuneCircle()`, `SigilField()` | the spellbook only | the one screen that runs cold |
| `CornerBrass()` | a rare-moment frame | no caller today; the boss frame, level-up and a Legendary drop may spend it and currently spend nothing |

Three clarifications the upgrade forced. **The settings screen has no ornament at all**, not even
a wipe. **The boss frame lost its box and gained none**: it is bare bars and inked text, and its
weight comes from size. **A keyline, a lit plate edge and a hairline are structure, not ornament**:
they are how a surface is built and are not rationed.

`UiOrnament` provides `EmberWipe()` (a rule drawn by a line of heat; `PlayEmberWipe` replays it),
`CornerBrass()` (four L-brackets, built from `ColorRect`s so they retint with the palette for
free — add as the panel's **last** child) and the three animated motifs: `RuneCircle()`,
`SigilField()`, `InkShimmer()`.

⚠️ **The motifs must live on a `ColorRect`, never on a `PanelContainer`.** A ColorRect's
UV is guaranteed to span 0..1 over its rect, and every one of these shaders does polar or
sweep maths in UV space. On a rounded panel the ring drifts off centre as the panel
resizes.

## 8. Accessibility (37.5G)

The Accessibility tab's settings, all round-tripping through `user://settings.tres` for free (they
are `[Export]` fields on a Godot `Resource`). 37.5G shipped the first three; the 2026-10 upgrade
added the last three rows and offers subtitles, text size, high contrast, reduced motion and colour
vision again in first-run setup.

| Setting | What it does |
| ------- | ------------ |
| `TextScale` (0.85-1.5) | Scales **glyphs only**, via `UiTheme.FontSize`, and bends: small sizes take all of it, large ones less (§3). Distinct from `UiScale`, which is the window's content-scale factor and magnifies panels and margins too - this is for a player who wants readable text without surrendering half the screen to chrome. |
| `ColorVision` | Daltonizes the semantic ramps (rarity, school, standing, good/bad). |
| `HighContrast` | Surfaces go fully opaque, the grain material is dropped entirely, panel, card and lit-edge rules thicken, and every HUD ground (bare groups included) becomes an opaque plate. |
| `ReducedMotion` | `UiTheme.Duration` returns 0 and `UiFx` lands on its final state; the title painting holds still; the credits do not roll by themselves; the dialogue typewriter is off. It never shortens a hold. |
| `SubtitlesEnabled`, `SubtitleSize` (small, medium, large), `SubtitleBackground` (plate opacity, 0 to 1), `SubtitleSpeakerNames` | The caption layer (§13.4). Only the on/off switch predates the upgrade, and nothing read it. |
| `ReadableFont` | Every font role resolves to Inter. |
| `HoldsToPresses` | A hold to confirm completes on one press; the destructive ones then ask through a prompt instead. |

⚠️ **`FontSize` floors at `CaptionFontSize` regardless of the setting.** The 12 px floor exists
because of a real min-spec/Steam Deck readability audit, and a *text size* control that can make
text unreadable is not an accessibility feature.

### Colour vision

`ColorVision` **daltonizes; it does not simulate.** Simulation shows a trichromat what a
colourblind viewer sees - a diagnostic tool, and actively the wrong thing to render, since it
would make the UI *less* distinguishable for the person who needs help. Daltonization measures
the information the viewer loses and redistributes it into channels they retain.

⚠️ **Applied at the token layer, not in the builders.** Daltonization is **not idempotent** -
adapting an already-adapted colour over-shifts it - so exactly one layer may apply it. The token
layer means anything reading a semantic token or one of the three domain ramps is covered
wherever it ends up, including raw `ColorRect` pips that never touch a builder. The neutral
tokens (`Text`, `Dim`, `Accent`) stay unadapted: near-achromatic, so adaptation would move them
for no gain while changing the UI's whole character.

⚠️ **UI only, never world art.** The world-space users of the same ramps - item drop glow,
trophy tint, spell projectiles, impact flashes - deliberately do not route through it. Recolouring
the world is a different and much larger decision than recolouring a label, and a fire spell that
stops looking like fire is a worse outcome than a hard-to-read chip.

`ColorVisionTests` assert the **property**, not the arithmetic: after adaptation, a confusable
pair must be further apart *under simulation* than it was before. Pinning matrix outputs would
only prove the numbers had not been retyped.

**Colour is never the only channel**, adaptation or not: rarity climbs in luminance and thickens
its frame, stat deltas carry the arrow glyphs, quest and objective state carry a tick, perk and
boss-phase rank are pips as well as numbers.

## 9. Responsiveness

⚠️ **Measure the viewport, never the window.** `GetViewportRect()` is already in *logical* pixels
- the content-scale factor `UiScale` drives has been applied - so a Steam Deck at 1280x800 with
UI scale 1.5 reports **853x533**, not 1280x800.

Use `UiTheme.ApplyScreenInset(shell)` for a full-screen panel (the gutter shrinks below 1100 px),
and derive any column count or fixed dimension from `UiTheme.UsableWidth` / `UsableHeight`. Call
them from `Rebuild` as well as `BuildShell`: the setting can change mid-session, and offsets
applied once at `_Ready` keep a stale gutter until the game restarts.

37.5C and 37.5D both authored fixed columns against an assumed ~1900 px and overflowed that
853 px viewport by **321 px** and **167 px**. Height is the axis that actually bites on a
handheld - 533 logical px is short enough that a panel whose *width* fits comfortably can still
run off the bottom.

Verified at 854x534, 1280x800, 1920x1080 and 3440x1440.

## 10. Text rules

Every player-facing string goes through `Loc.T`/`Loc.TF` (`data/locale/strings.csv`) — no
literals in labels/buttons/toasts. Sentence case for body and actions; headers may be
short title case. Numbers the player compares (damage, weights, gold) stay unlocalised
digits.

## 11. Roadmap seams

37.5A laid the foundation above. The passes that consume it:

- **37.5B** ✅ de-drifted the UI and rebuilt the HUD. The audit's headline number was wrong:
  most `new Color(1,1,1,a)` hits are the **alpha-fade idiom**, not palette literals, and many
  more were 3D world colours answering to ART_STYLE. The genuine drift was ~12 sites, and two
  real defects fell out of it — **seven hand-rolled scrims** at four values, six of them
  blue-black against §1 rule 1 (now `UiTheme.Scrim`), and three off-scale font sizes (40, 28,
  15). It also split `BossFrame` and `Nameplate` out of `GameHud` (975 → 812 lines) and gave
  the nameplate a **disposition spine**, which the HUD had never shown despite neutral-until-
  provoked factions existing since Phase 34.5.
- **37.5C** ✅ rebuilt the character sheet, inventory, storage and crafting onto a shared item
  vocabulary (`ItemSlot.Build` / `ItemSlot.Detail` / `ItemPresentation`).
  ⚠️ **There were no item icons** at 37.5C: `ItemResource.Icon` had existed since Phase 5,
  **no authored item set it and nothing read it**, so slots drew a *category glyph* (silhouette =
  category, colour = rarity, frame width = tier). The 2026-10 upgrade added painted archetype
  icons on one atlas, resolved from the item id (`ItemIcons`, §13.2). The glyph is still the
  fallback for an id nothing recognises, and an authored `Icon` still wins over both.
- **37.5D** ✅ lifted magic out of the character sheet into `SpellbookPanel` (`T` for tome) —
  the one screen that runs cold, and the only one spending all three motifs. It also surfaced
  two things the game had never shown: the **prepared-spell cycle order** that `Q`/`F` walk,
  and the **reactive combos** from `SpellCombo`'s rule table, live since Phase 29.5D and
  discoverable only by noticing a bigger number. Both read from the same authority combat uses.
  (The prepared row is gone since the 2026-10 spell wheel: `F` is the wheel now, the cycle order
  means nothing, and the row is the eight pin slots of §13.4 rule 11.)
  ⚠️ `ContentValidator` now gates the UI's fonts and shaders. Three of the four shaders only
  instantiate when a screen is *opened*, so a broken one appeared in no boot log, no `--play`
  run and no test — only in play, as "nothing is there".
- **37.5E** ✅ rebuilt map, quest log, dialogue and bestiary.
  ⚠️ **A journal section arrives with its state, never before it.** The Failed section was left out
  until `QuestStatus.Failed` existed (41B added both), because an empty heading is a permanent
  promise. Same call as the omitted Contracts and
  Exploration headings. Main/Side is real now: `QuestResource.IsMainQuest`, the field 37.5B
  refused to fake with a backwards "has a prerequisite" heuristic.
  ⚠️ **Quest markers were not on the map** at 37.5E, because quests carried no world position.
  The campaign overhaul changed that: an objective may name a place, and the map pins the current
  objective of every live quest (§12, *Map*). An objective that names only a template id still
  has no pin.
- **37.5F** ✅ rebuilt the shell. ⚠️ **The `UiPanel` migration was dropped deliberately.** The
  plan assumed the shell screens lacked the modal contract; they do not — `SettingsPanel`,
  `SaveSlotPanel`, `MainMenu`, `PauseMenu` and `CharacterCreator` all already call
  `UiState.Open` *and* `UiFocus.GrabFirst`. The migration's only remaining gain was the open
  fade, against a lifecycle rewrite (they are create-per-use factories; `UiPanel` is
  persistent-toggle) of the **only path into the game**, which no remote session can drive.
  Not worth it. Revisit if a shell screen ever needs the dirty-flag rebuild loop.
  ⚠️ **Third and fourth instances of the `Panel()`-as-generic-box trap**: save-slot rows and
  toasts were both built from `PanelStyle()`, so since 37.5A a six-slot list was six brass
  frames with six grain shaders, and every four-second toast carried a framed screen's chrome.
  Both are `Card` now. **When a small repeated widget needs a box, it is a `Card` or a `Well`,
  never a `Panel`.**
- **37.5G** ✅ shipped the three accessibility settings and the responsiveness audit - sections 8
  and 9 above are the result.
- **37.5H** ✅ the sweep: settings and character creation rebuilt, and every remaining player-facing
  widget brought onto the vocabulary. A coverage audit (does this file use *any* of `Card` /
  `Chip` / `SectionRule` / `ApplyType` / `Title` / `Prose` / `Well` / `Divider` / `ItemSlot`?) found
  **thirteen** widgets still on `Panel()`. **Phase 37.5 is complete (A-H).**

The 2026-10 UI upgrade came after all of them and reversed one call above: the shell screens are
still not `UiPanel`s, but they no longer sit on `Panel()` either. They are `UiTheme.Sheet`s (§13.6).

When a pass lands, update this document — it must stay the single source of truth.

## 12. Quest UI (campaign overhaul)

The quest surfaces (journal, tracker, toasts, chapter banner, compass, map pins, dialogue chips, boss
frame) share one vocabulary and one set of pure rules. Every decision that is not drawing lives in a
Godot-free class with xUnit coverage in `QuestUiRulesTests` (`JournalIndexRules`, `StageLogRules`,
`ObjectiveFocusRules`, `QuestNoticeCoalescer`, `ChapterBannerRules`, `DialogueConsequenceTags`,
`CompassRoutingRules`, `MapQuestPinRules`); the panels only lay the answers out.

**Tokens.** `QuestMain` (ember) is the main thread, `QuestSide` an errand, `QuestComplete`/`QuestFailed`
finished and lost. One colour means one thing on every surface: the journal spine, the tracker band
(retinted per quest), the compass chevron and the map badge all read `IsMainQuest` and pick the same pair.

**Colour is never the only channel.** Every state has a word, a shape or a glyph beside it:
- Objective state is a drawn mark (`MarkGlyph`, no font): a tick for *done*, a cross for *missed*, a
  filled diamond for *current*, a hollow diamond for *optional* (the tracker adds an "Optional" chip), a
  padlock for *locked*. The tracked quest carries the word "Tracked", a quest with news a small dot (a
  drawn square) and an asterisk on its tab.
- On the map a *filled* diamond is the main thread and an *outlined* one an errand; the ring is the
  tracked quest's alone. On the compass an *optional* objective is a hollow chevron.

**Journal.** Section tabs (Main Thread, Errands, Completed, Failed) are a `UiTabs` row under the title, exist only when they hold quests and step
with Z/C or LT/RT (`menu_sub_prev`/`menu_sub_next`; Q/E and LB/RB walk the hub's screens). Main groups under collapsible chapter headings (`chapter.<key>.title`, then
`pale.chapter.<key>.title`; a key-less group needs no heading). The detail card reads top to bottom: chips
(type, chapter, region, level), giver, prose (`DetailKey`, else the summary), Track, the stage log, rewards.
The stage log is ordered done, current (with its hint and its place), optional, locked; the place goes through
`ObjectiveNavigation.LocationId`, so Reach and Defend name their destination like every other type. Ledger
quests are listed under a folded "Ledger" row and are never tracked. Completed lists newest first and folds
after five. Prose is EB Garamond at an 80-character measure. Track is F or X and "Show on map" is V or R3
(`KnowledgeInput`); show-on-map opens the map on the place and then closes the journal, so the world never
unpauses between the two. There is no footer hint: the legend carries Section, Track and Show on map, each
only while it would do something.

**Tracker.** A chapter label above the title; the spine takes the quest's colour; optional rows carry the
Optional chip; the current objective's hint appears under it after `TrackerRules.HintDelaySeconds` (90 s)
on the same step; the header reads "Now tracking" for three seconds after the tracked quest changes. It
shows at most three objective lines, the current step first, then "+N more" (`TrackerFoldRules`), sits on
a `HudPlate` whose lit edge is the quest's colour, and steps aside for the boss bar on a narrow layout.

**Toasts.** One player action publishes several quest events; `QuestNoticeCoalescer` folds a frame's worth
into one toast per quest (completion beats failure beats start beats next objective beats "updated"; an
optional step met is its own toast). Each plays one of five cues (`ui.quest.started`, `.updated`,
`.completed`, `ui.chapter.title`, `ui.objective.optional`) when it is actually shown. A quest toast names
the journal key. A companion bark is a caption when subtitles are on and nothing pauses the world, and
otherwise a portrait-less toast: the line, then the speaker. Dwell, collapse and combat deferral are in §13.4.

**Chapter banner.** `ChapterBanner` shows an act line, the chapter title and a subtitle in the lower third, once
per save per chapter (`flag.chapter.<key>`), without pausing the world. It queues behind any menu, dialogue or
narration sequence (they all register with `UiState`). Reduced motion keeps the fade and drops the rise; the
fade is deliberately not routed through `UiTheme.Duration`, which would collapse it. A chapter with no title
text is skipped rather than drawn as a raw key.

**Dialogue.** Each choice carries chips for what it will do (`DialogueConsequenceTags`): a quest start, corruption
`+N`/`-N`, reputation, companion loyalty, guild join/rank, items, and a neutral "Story" chip for flags and story
cards. Choices are numbered 1-9; the last three lines of the conversation replay on H, E or RB (`menu_tab_next`); a line under the
speaker names the objective when this person is the tracked quest's live Talk target.

The window is a lower third: speaker in Cinzel and the line in EB Garamond on the left, options on the right.
The line writes itself at 48 characters a second (a tween on `VisibleRatio`), and the options are not built
until it finishes. Accept, a number key or a click finishes the line and chooses nothing; for 300 ms after a
line finishes by itself those presses are swallowed (`DialoguePaceRules.GraceMs`), so a press meant to skip
never picks option 1. A long line scrolls on the right stick or Page Up / Page Down, with a "More below" cue
under it. Each option leads with a mark: a filled diamond for plot, a hollow one for any other consequence, a
tick for already asked, a dash for leave. The typewriter is off under reduced motion and in any automated run.

**Compass.** The chevron takes the quest's colour. An objective whose place is in another realm points at this
realm's door toward it (`CompassRoutingRules.NextHop` over the region graph, skipping sealed realms) and labels
the realm; if no door resolves it falls back to the direct pointer.

**Map.** Quest pins show the current objective of every live non-ledger quest. Reveal is spoiler-safe
(`MapQuestReveal`): a quest reveals only the places its live objectives name and each later place when that
objective opens, never both branches of a fork.

The rail has two tabs, Place and Legend. Legend is the filter: one switch per pin group, and "Show
categories" unfolds one per category. On a pad a reticle sits at the plot's centre and the nearest pin within
56 px is ringed and named (`MapSnapRules`); X selects it and R3 sets the waypoint there, or clears it. Zoom is
on the sub-tab actions (Z/C, LT/RT) and there is no footer button row. Every fast-travel press opens a confirm
card (destination, fee, Travel / Stay here), and cancel backs out of the question without closing the map.
The plot is a dark smoked-vellum chart (tokens in §2): roads are a pale core over a dark shoulder, every label
is keylined, the player arrow is drawn above every pin, and a name that would lie on the arrow is moved, not
dropped. A scale bar replaces the graticule on the full map; the minimap shares the view and keeps it.

**Bestiary.** A list and a page, like the journal; categories step on the sub-tab actions. An unseen
creature is a sealed row (padlock, "Unrecorded", a hint). Sighted adds the name, the kill tally as the hero
number and a progress bar. Known adds "Resists" and "Open to" chips (`IconChip`: an icon and the school's
name, at most three) and the lore at the reading measure (`BestiaryFactRules`).

## 13. The 2026-10 UI upgrade: the vocabulary map

The upgrade ("banked embers") moved the UI from boxed panels on a picture to cut plates with one lit
edge, real icons, one motion and sound language, a hub strip with a footer legend, tabbed settings
with remapping, and a HUD the player can configure. Values live in code; this section is the map of
what exists and where. Palette values are in §2.

⚠️ **Built without the engine, then rendered by the harnesses.** The seven shot harnesses in §13.9
were run over this work, and nothing here has been played by a person. `docs/NOW.md` has the list of
what is unverified.

### 13.1 Index

| You need | Reach for | Lives in |
| -------- | --------- | -------- |
| A fade, rise, stagger or pulse | `UiFx` | `UiFx.cs` |
| A confirm for something irreversible | `UiFx.HoldRing` | `HoldRing.cs` |
| A sound for a press | `UiAudio.Play(UiCue)` or `UiTheme.Action(text, cue)` | `UiAudio.cs`, `UiAudioRules.cs` |
| A key or button picture | `UiGlyph.For(action)` | `UiGlyph.cs`, `UiGlyphRules.cs` |
| A footer of glyph and verb pairs | `UiPanel.Legend` override, or a `UiLegend` | `UiLegend.cs`, `LegendEntry.cs` |
| A hub screen, sub-tabs, a scrim | `UiPanel.Hub`, `OnSubTab`, `Dims` | `UiPanel.cs`, `HubStrip.cs`, `HubTab.cs` |
| A slider, toggle, dropdown, scroll list | the `UiTheme` builders (skinned by `UiSkin`) | `UiSkin.cs` |
| A shell screen's ground | `UiTheme.Sheet` | `UiTheme.cs` |
| A HUD group, plate, shade, slot | `HudBare`, `HudPlate`, `HudShade`, `HudSlotStyle` | `UiTheme.Hud.cs` |
| A HUD width or size | `HudMetrics`, `HudCoreMetrics` | same names |
| Whether a HUD element shows | `GameHud.Shows`, `GameHud.ElementMode`, `MarkChanged` | `GameHud.Options.cs`, `HudOptions.cs` |
| An item's picture | `ItemIcons.For(template)` | `ItemIcons.cs`, `ItemIconRules.cs` |
| An item slot or its detail card | `ItemSlot.Build`, `ItemSlot.Detail` | `ItemSlot.cs`, `ItemDetailCard.cs` |
| A hub page (journal, map, bestiary) | `UiTheme.HubPage` | `UiTheme.Knowledge.cs` |
| A trade page (vendor, crafting, storage) | `UiTheme.TradePage`, `TradeRow.Build` | `UiTheme.Trade.cs`, `TradeRow.cs` |
| A drawn mark instead of a typed glyph | `MarkGlyph`, `TradeMark`, `SessionGlyph`, `PerkNodeMark`, `HudIcon` | same names |
| A confirm prompt in the shell | `SessionPrompt.Open` | `SessionPrompt.cs` |
| A spell's mark (glyph on its school disc) as a control | `SpellDisc.Create(size, keylined)`, then `Display(spell, lit)`; null draws an empty socket | `SpellDisc.cs` |
| A spell's glyph or a school's emblem inside your own `_Draw` | `SpellGlyphs.Draw`, `DrawEmblem`, `DrawStrokes`; an unknown id draws `Fallback` | `SpellGlyphs.cs` |
| Which favourite slot a pin lands in, what the pin button says, the HUD's wheel hint and tap ghost | `SpellPinRules.Decide`, `SlotNumber`, `ShowsWheelHint`, `Ghost` | `SpellPinRules.cs` |
| The spell wheel's sizes, cell states and placement | `SpellWheelMetrics` (geometry is `SpellWheelRules` in `src/Magic`) | `SpellWheelMetrics.cs`, `SpellWheel.cs` |

A lane that needs a token or builder adds it to its own `UiTheme.<Lane>.cs` partial (§13.3), never to
`UiTheme.cs`. Every decision that is not drawing sits in a Godot-free `*Rules` class with xUnit cover.

### 13.2 Foundation seams

**Type.** `FontSize(token)` bends the text-scale setting: sizes up to body take all of the change, header 85%, title 70%, display and up half (`ScaledFontSize`, pinned by `UiTypeScaleTests`). The 12 px floor stays. The `ReadableFont` setting sets every role in Inter (`ResolveRole`). Cinzel is for a label of three words or fewer; a longer name takes the interface face (`ItemPresentation.UsesDisplayFace`, `UiTheme.SetTradeTitle`).

**Sheet.** `UiTheme.Sheet(width, scrimOpacity, centred)` returns `(Root, Column)`: a frameless column on a scrim with one `RuleLit` edge down its leading side, 8% in from the left or centred. The shell's ground (title, pause, death, settings, save slots, creator, first run) in place of `Panel()`.

**Motion** (`UiFx`). `FadeIn`, `FadeOut(node, then)`, `Rise`, `Stagger(container)`, `Pulse`. Each kills the run in flight on its node, runs while the tree is paused, ignores time scale and collapses under reduced motion to its final state. Call them after the node is in the tree. Closing a menu is still instant. `DurationTab` (0.16 s) is a tab or hub-screen switch. `UiOrnament.EmberWipe()` is the one opening flourish: a rule drawn by a line of heat, one per screen, replayed with `PlayEmberWipe`.

**Hold to confirm.** `UiFx.HoldRing(onComplete)` builds a `HoldRing` (0.9 s, `UiFx.HoldSeconds`); drive it with `Attach(button)`, `HoldAction` or `Press()`/`Release()`. For irreversible actions only: delete or overwrite a save, reset settings or bindings, respec, skip a narration. The `HoldsToPresses` setting makes one press complete it; settings and the slot browser then ask through a confirm prompt with Cancel focused instead. Reduced motion never shortens the hold. A ring on a focusable row must also release when its button loses focus (`button.FocusExited += ring.Release`), or a tap followed by a d-pad move completes it.

**Engine-drawn controls** (`UiSkin`). Sliders, check buttons and boxes, line edits, scrollbars, popup menus, tooltips and the default font are skinned by one code-built `Theme`: merged into the engine default theme at boot and whenever high contrast or text scale changes, and handed to each control by the `Slider`, `Toggle`, `Dropdown` and `ScrollList` builders. A hand-built control of one of those kinds calls `UiSkin.Apply(control)`. Icons are `assets/ui/icons/controls/*.svg` and carry their own colours.

**Sound** (`UiAudio`). An application-lifetime node under `ApplicationRoot`, so the title screen sounds like the menus in a session. `UiAudio.Play(UiCue cue, float pitch = 1f)` is static and safe anywhere. Cues: `Click` (every `UiTheme.Action` by default), `Focus`, `Confirm`, `Back`, `Tab`, `Open`, `Close`, `Denied`, `HoldTick`. `UiTheme.Action(text, cue)` takes the cue a press means. `UiPanel` plays `Open`/`Close` for modal panels and `Back` on cancel; `HoldRing` ticks as it fills and confirms when it closes. One sound per action: cues asked for within 50 ms coalesce and the one that means most is heard (`UiAudioRules.Priority`), so never guard a call yourself. Focus ticks only when a direction action is down, so a rebuild restoring focus is silent. It also holds the `music.title` bed while the game state is `MainMenu`, and low-passes the SFX and ambience buses while a menu pauses the world (`AudioBusLayout.SetMenuDuck`). All cue streams are procedural until recordings land at the paths in `AudioLibrary`.

**Glyphs** (`UiGlyph`). `UiGlyph.For(action)` returns a `Control`: on keyboard and mouse the existing `KeyCap` with `GameInput.PromptLabel`; on a pad a shape from `assets/ui/glyphs/` with the button's letter as live text on top. Families (`PadFamily`): Xbox letters, PlayStation symbols, and Generic, which shows face buttons by position because an unknown pad's letters cannot be trusted. A stick click reads L3 or R3 on every family. A glyph is a snapshot: redraw it on `InputDeviceChangedEvent` and `InputBindingsChangedEvent`. `GameInput.KeyLabel` names mouse buttons (LMB, RMB) and `PadLabel` the triggers (LT, RT).

**Legend** (`UiLegend`). Every `UiPanel` draws a footer row of glyph and verb pairs in its bottom gutter. Override `protected virtual IReadOnlyList<LegendEntry> Legend` and put your entries in front of `base.Legend` (which is Close, plus Switch screen on a hub screen); a `LegendEntry` is `(action, Loc.T(...) label, optional second action)`. It is read after every rebuild, so it may vary by tab, selection or device, and an entry is listed only while it would do something. A panel with a hint footer of its own moves those hints into the legend rather than show both. A screen that is not a `UiPanel` builds a `UiLegend` and calls `Set`, rebuilds it with its sheet (so it follows text size and contrast), and keeps it the last child so a prompt's scrim never dims it.

**Hub** (`HubStrip`). The five hub screens (`HubTab`: Character, Spellbook, Journal, Map, Bestiary) each override `protected virtual HubTab? Hub` and get the strip in their top gutter. `menu_tab_prev`/`menu_tab_next` (Q/E, LB/RB) step screens; `menu_sub_prev`/`menu_sub_next` (Z/C, LT/RT) call `protected virtual void OnSubTab(int delta)` on the open panel, which is where a screen steps its own tabs. Neither fires while a `LineEdit` has focus. Stepping opens the neighbour through `IHubHost` (`UICompositionRoot`) and then closes the current panel, so the world never unpauses in between. `protected virtual bool Dims` puts the shared `ScrimHub` behind a screen: hub screens do by default and the trade screens opt in.

**Reserved heights** (`UiChromeRules`). `UiTheme.ApplyScreenInset` leaves room for both strips. At 1280x720 and wider the 70 px gutter already holds them and no panel moved. On a narrow viewport (853x533 logical) the bottom inset grows from 24 to 32 px for the legend, and a hub screen's top inset from 24 to 44 px for a 36 px strip: a hub frame there is 457 px tall where it was 485. `ApplyWorkspace` panels are unchanged; their legend sits in the margin under them. Below `UiChromeRules.ShortHeight` (620 logical px) a view is "short": the hub strip takes its 36 px form and the title drops its seal and subtitle.

**HUD options** (`HudOptions.cs`, `GameHud.Options.cs`). `HudElement` is one entry per HUD element the player can configure (14 of them) and `HudElementMode` is `Always`, `Dynamic` or `Hidden`; both are append-only because their numbers are what `Settings.HudElementModes` saves. `HudPresets` holds the Full, Dynamic and Minimal tables and recognises a saved list as one of them or as Custom. An element shows only when two gates agree: `HudVisibility` (what the HUD mode allows) and `HudDynamicRules.Visible(element, mode, signals)` (what the player asked for). `GameHud` resolves the second once a frame, only while some element is Dynamic, and rewrites visibility only on the frame an answer changes. §13.4 has the rules a widget follows.

**HUD scale, opacity, safe zone** (`HudLayout`). The slots live in `HudLayout.Scaled`; `ApplyScale`, `ApplyOpacity` and `ApplySafeZone` act on it. `Overlay` is outside it: the reticles there are placed in screen coordinates. Anything added to the HUD goes in a slot, never straight under the layout root, or it will not scale. `GameHud.TopRightBottom` and `BottomRightTop` are global rects and stay screen coordinates under any scale.

**Item icons** (`ItemIcons`, `ItemIconRules`). `ItemIcons.For(template)` returns the item's authored `Icon`, else its archetype's cell on `assets/ui/icons/items/atlas.png`, else null and `ItemSlot` draws the category glyph. `ItemIconRules.Key(id, type, slot)` picks the archetype from the id: a category that shares one picture first, then the longest suffix of the name on a word boundary, then the slot, then the type. `ItemIconRules.Keys` is the list of pictures (79 on the shipped atlas); `ItemIconRulesTests` holds every item id to a key or the declared fallback.

**Atlas pipeline.** Paint one picture per key on flat black into `assets/ui/icons/items/src/<key>.png` (the folder carries a `.gdignore`), then run `godot --headless --path . --script res://tools/pack_ui_atlas.gd`. It scales each picture into a 128 px cell, ten columns, keys the black out with a soft ramp so edges do not fringe, and writes `atlas.png` and `atlas.json`. Import the atlas once afterwards. Provenance of every painting, emblem and icon is `assets/ui/PROVENANCE.md`.

**Composition.** `UICompositionRoot.Death` (`DeathScreen`) and `UICompositionRoot.Subtitles` (`SubtitleLayer`) are built with the session shell, `EnemyPlateLayer` is the `EnemyPlates` child of `CombatFeedbackOverlay`, and `CreditsScreen.Open(parent, onBack)` mirrors `SettingsPanel.Open`. A new screen of that kind is wired there, not from a panel.

### 13.3 Lane theme partials

| Partial | Builders and measurements |
| ------- | ------------------------- |
| `UiTheme.Hud.cs` | `HudBare()`, `HudPlate(edge)`, `HudShade()`, `HudInk(text)`, `HudSlotStyle(edge, groundAlpha)`, `HudPlateStyle(edge)`, `DrawKeyline(item, rect)`, `RefreshHud(root)`; the grounds in §2 |
| `UiTheme.HudCombat.cs` | `Poise`, `LockDotRadius`, `DrawLockDot(item, centre, alpha)` |
| `UiTheme.Items.cs` | `Plate(litEdge, width)`, `PlateStyle`, `PlateBody()`, `PlateBand(tint)`, `PlateFooter()`, `BadgeStyle()`, `DeltaArrow(delta, size, lowerIsBetter)`, `RarityEdgeWidth(rarity)` |
| `UiTheme.Knowledge.cs` | `HubPage(shell, title, out aside, tabs)`, `ApplyHubPlate(shell)`, `TabRail(tabs)`, `IconChip(icon, text, color)`, `Measure(prose, available)`, `MapLetteringFont`; the map tokens in §2 |
| `UiTheme.Trade.cs` | `TradePage(shell, icon, out title, out aside, out wipe, tabs)`, `SetTradeTitle`, `PurseReadout`, `TradeColumn(width, ...)`, `RowRule()`, `ColumnRule()`, `TradeRowStyle(edge)`, `PriceLedger(quote)`, `HeroFact(value, unit, color)` |
| `UiTheme.Settings.cs` | `SettingsRowStyle(focused)`; sheet, pane, control-column, binding-cell and prompt widths |
| `UiTheme.ShellFront.cs` | `Painting(name)`, `Cover(painting)`, `Shade(from, to, opacity, radial)`, `SheetOverPainting(root, painting)`, `SheetToRight(column, width)`, `TitleEntry(text, cue)`; title, splash, loading, credits and first-run measurements; `GenericPainting` |
| `UiTheme.ShellSession.cs` | `SessionAction(text, cue, lit)`, `SessionActionStyle`, `SessionRule()`; slot, creator, pause, death and narration measurements; `DurationDeath` |
| `UiTheme.Wheel.cs` | `WheelGlyphInk`, `WheelUnlitDisc(school)`, `WheelUnlitInk(school)`, `WheelLine`, `WheelLitLine`, `WheelBackdrop`, `WheelWell`, `WheelWellHover`, `WheelSchoolGround(school, lit)`, `WheelFanGround(school, lit)`, `WheelSocket`, `WheelLit`, `WheelPrevious`, `WheelPointer`, `WheelNameFont`, `WheelTextFont`, `StyleWheelReadout(box, edge)` |

### 13.4 HUD rules

The HUD is light chrome over a live world: it is built from four things and no boxes. `UiTheme.Panel()`
never appears on it.

| Ground | Builder | What takes it |
| ------ | ------- | ------------- |
| Bare | `HudBare()` | vitals, party strip, hotbar group, boss frame: keylined bars and inked text straight on the world |
| Shade | `HudShade()` | one line that must read over sky: the clock row, the boss name |
| Plate | `HudPlate(edge)` | a widget that is mostly reading: tracker (quest colour), prompt, event banner, tutorial hint, target plate (disposition), save chip, toasts. The edge is its one lit side |
| Slot | `HudSlotStyle(edge)` | a hotbar cell: a small keylined well |

1. **Text on the world is inked.** Wrap it in `UiTheme.HudInk(...)`: a 3 px keyline and a 6 px halo. An icon on the world is a `HudIcon` (keylined), not a bare `UiIcon`. Anything custom-drawn calls `DrawKeyline`.
2. **High contrast turns every ground opaque**, bare groups included. The pieces are tagged when built and `UiTheme.RefreshHud(root)` re-applies them when the setting changes, so a HUD widget does not cache the setting.
3. **Bars** are `JuicedBar` with the opt-ins `Keylined`, `LagChunk` (the removed length holds 0.35 s, then closes), `SetTicks(fractions)` and `Hatched`. All default off. Vitals notches: health at 15% and 30%, stamina at the winded-recovery fraction, mana at the prepared spell's cost, corruption at its tier boundaries.
4. **State has a shape as well as a colour.** The health mark becomes a warning mark when low, the stamina mark a padlock when winded, corruption is a hatched fill with its tier named.
5. **The hotbar cell** is square: 72 px, narrowing to 56 wide by 64 tall at a layout width of 853 (`HudCoreMetrics.HotbarCellWidth` / `HotbarCellHeight`). A painted icon is its own label; the name line shows only under the fallback glyph. States are `HotbarSlotState`: Empty, Ready, Cooling (a wipe, and the seconds for the last 9), Unusable (dimmed), Locked (keylined padlock), Depleted (a red count). An empty cell keeps 30% of the slot ground.
6. **The bottom bar is three cells with equal expanding sides**, so the hotbar sits on the centre line and does not move when the minimap hides. `HudCoreMetrics.BottomBarMinimum(w)` is held at or under `w` for every layout width from 853 to 2000 by test. Below 853 (a large HUD scale on a handheld) it can still overflow.
7. **Widths come from metrics, never literals.** `HudMetrics.VitalsWidth`, `TrackerWidth`, `CompassWidth` and `BossBarWidth` take `GameHud.LayoutWidth`: each is its old literal at 1280 and grows to a cap above (the boss bar is 42% of the width between 420 and 720). `HudCoreMetrics` narrows the hotbar cell and the minimap (186 to 140) between 980 and 853, caps the compass to the space between the tracker-width side columns (floor 200), and says when the boss bar would meet the tracker (under about 1100), where the tracker steps aside while a boss frame is up. Widgets refit on the layout's `Resized` signal and `GameHud.LayoutFitted`, never by polling.
8. **`HudLayout.BottomClearance` is 140.** Anything centred above the hotbar (prompt, tutorial hint, subtitles, placement strip) sits there or higher; a strip outside `HudLayout.Scaled` converts it with `HudMetrics.ScreenClearance`.
9. **The tracker shows at most three objective lines**, then "+N more" (`TrackerFoldRules`): the current step first, finished steps dropped first.
10. **A prompt is glyph, verb, noun** on a plate; an item pickup adds a second line for hold-to-gather. The split is `PromptRules.Split`, and a phrase that does not end in the focused name is shown whole.
11. **A spell is shown by its disc, everywhere.** The mark is the spell's glyph in `WheelGlyphInk` on a disc of `UiTheme.SchoolColor(school)`; a spell that cannot be cast right now (unaffordable, silenced, corruption-locked) is the unlit look, the glyph in the school's colour on a dark disc (`WheelUnlitDisc`, `WheelUnlitInk`), so colour is never the only signal. Sizes: 26 px keylined on the HUD spell row, 24 px on a spellbook card, 22 px in a pin slot, 18 px for the tap ghost; the wheel's own floor is 26 px. **The spell row** reads disc, cast key, name (through `SpellText.Name`), cost, state. Its wheel hint is a separate caption line ABOVE the row, never a badge in it (at the 286 px vitals minimum a badge leaves the name no room): the `cycle_spell` glyph with "Hold: wheel", then "Tap:" and a ghost disc with the previous spell's name. The line shows only with two or more spells known (`SpellPinRules.ShowsWheelHint`) and the ghost only when there is a spell to swap back to (`SpellPinRules.Ghost`). With presses in place of holds it reads "Press: wheel" and has no ghost, because there is no tap. It redraws on change, not on new subscriptions. **The spellbook's pin row** is eight numbered slots, 1 at the wheel's top wedge and clockwise (`SpellPinRules.SlotNumber`), in one row at a usable width of 1400 or more and two rows of four below. The caption over the slots is always present whatever it says, and a card's Prepare and Pin are always buttons ("Prepared" is a button that does nothing): the panel restores focus by child index across a rebuild, and a row that came and went would drop a pad's focus.
12. **The spell wheel is HUD, not a panel.** It is a `_Draw` control in `HudLayout.Overlay` with `ZIndex = 1` (so it draws over the scaled HUD), gated by `HudElement.SpellWheel`, and it never calls `UiState.Open`. Keylines are `WheelLine` / `WheelLitLine`. The readout (name, cost, state) is a plate UNDER the wheel, not in the centre: the dead zone is too small for three lines, and the centre holds a cancel mark, or the previous spell's glyph in toggled mode. A legend line under the readout names release, select and cancel with `UiGlyph`. The radius is `SpellWheelMetrics.Radius` (150 px base, 84 px floor) and has to fit the 853x533 handheld view; at 1280x720 and below the readout and legend sit over the hotbar on near-opaque grounds. Cooldowns and mana are sampled four times a second, so the pie wipe steps. Numerals keep the 12 px floor. Hover plays `UiCue` Focus and a selection Confirm.

**Dynamic modes.** A widget that writes its own `Visible` reads `GameHud.Shows(element)` at that write, so two owners never fight over one flag. A widget whose content just changed calls `GameHud.MarkChanged(element)`. A HUD element outside `GameHud` (damage numbers, enemy plates, toasts, subtitles) reads the static `GameHud.ElementMode(element)`. Holding `hud_recall` shows every Dynamic element.

| Element | A Dynamic element shows while |
| ------- | ----------------------------- |
| Vitals | in combat, a pool below max, changed in the last 4 s, or a menu is open |
| Hotbar | in combat, changed in the last 4 s, or a menu is open |
| Party | in combat, a pool below max, or changed |
| Crosshair, target plate, enemy plates | in combat or just changed (aiming at something marks them) |
| Compass, minimap, clock, quest tracker | just changed: a turn of 20 degrees, a destination moved 8 m, a discovery, a phase or weather change, a new step |
| Damage numbers, prompts, toasts, subtitles | always; each is already transient |

"In combat" lingers 6 s after a blow (`HudDynamicRules.CombatLingerSeconds`).

| Preset | Dynamic | Hidden |
| ------ | ------- | ------ |
| Full | none | none |
| Dynamic | vitals, hotbar, compass, minimap, clock, tracker, party, crosshair | none |
| Minimal | vitals, hotbar, crosshair | compass, minimap, clock, tracker, party, enemy plates, damage numbers |

**Combat and notices.**

| Surface | Rule | Authority |
| ------- | ---- | --------- |
| Boss frame | bare, name on a shade, keylined bar with a lag chunk at `HudMetrics.BossBarWidth`, keylined phase pips | `BossFrame` |
| Target plate | a plate whose lit edge is the disposition spine; `Nameplate.Naming` is who it shows while visible | `Nameplate` |
| Lock-on | acquire cue: a keylined ring closing onto a dot; the held mark is the same dot (`DrawLockDot`) | `LockOnCueLayer`, `GameHud.Context.cs` |
| Enemy plates | a pool of 8 world-anchored health and poise bars; claimed by a traded blow, aggro or a lock; released 6 s after the last blow; fade from 28 to 36 m; never for a boss or for whoever the target plate names; asleep with none up | `EnemyPlateLayer`, `EnemyPlateRules` |
| Damage numbers | `Settings.DamageNumberMode`: 0 off, 1 all hits, 2 your blows only, 3 crits and kills only | `DamageNumberRules` |
| Toasts | dwell is the greater of 3 s and 0.25 s a word, times the toast-duration setting; a repeat adds to a count ("×3") and restarts the dwell; in a fight only warnings show and the rest wait until the last opponent falls; Hidden still lets warnings through; a quest toast names the journal key | `ToastRules`, `ToastQueue`, `Notifications` |
| Subtitles | pages of two lines of about 40 characters, at most three waiting; size from the setting (body, header or title token), plate opacity from the setting; held while a menu pauses the world; `SubtitleLayer.TryShow` answers false when it will not caption, and the caller falls back | `SubtitleLayer`, `SubtitleRules` |
| Tutorial hint | a plate with a leading glyph, event-driven; stands down while a caption or a chapter card is up | `TutorialHint` |

Subtitles have two feeds today: companion barks (a toast instead when subtitles are off or a menu
is open) and authored boss intro lines. There is no voice acting to caption.

### 13.5 Panel family rules

| Pattern | Rule |
| ------- | ---- |
| Hub page | Character, Journal, Map and Bestiary start from `UiTheme.HubPage`: the panel ground with one `RuleLit` top edge and no box (`ApplyHubPlate`), `PanelPad`, a Cinzel title row with one line of context at its right end, an optional tab row, a hairline. The Spellbook takes the same geometry on its cold ground. Dialogue uses the same plate |
| Tab rail | Sub-tabs are a `UiTabs` strip stepped by `OnSubTab`. A strip that can outgrow its row sits in `UiTheme.TabRail` (scrolls sideways, scrollbar hidden). The settings strip fits by measurement instead: the active tab is never clipped, inactive ones may trim |
| Plate | A detail card is a `UiTheme.Plate(litEdge)`: card ground, one lit edge along the top in the semantic colour, no rounded box |
| Detail card anatomy | `ItemSlot.Detail`: band (name, then a type line with the rarity word), one hero number at display size with an arrow and signed delta against the worn piece, stat rows with drawn `DeltaArrow`s, affix chips, facts, set and unique text, flavour, then a footer (weight, value, glyph and verb actions). Every stat name wraps instead of trimming |
| Hero number | One per card or section: weapon damage, armour, amount restored, a contract's reward (`UiTheme.HeroFact`), a creature's kill tally. Never two on one card |
| Slot marks | Rarity is a frame colour, a thicker frame at Epic and up, ticks along the bottom edge (one per tier above Common) and a word in the tooltip. `ItemSlot.Marks`: `New` (a diamond pip, cleared when looked at) and `Equipped` (a lit left edge). Count and upgrade sit on a dark `BadgeStyle`; junk is struck through |
| Compare | `ItemSlot.CompareAction` (Shift, or L3 on a pad) flips the card to side by side: two equal columns, each stat name on its own caption line above the pair of values. It is built into the card, so inventory, vendor, storage and crafting all have it |
| Selection and accept | Selection follows focus without a rebuild. Accept on the selected cell steps into the detail pane's buttons; left from there returns to the cell. A second mouse click on selected gear equips it |
| Equipment column | Each equipment row is a full-height `CardButton`. Focusing one narrows the pack to what fits that slot; there is no separate slot-filter row |
| Trade page | Vendor, crafting, storage, appraisal and the contract board start from `UiTheme.TradePage`: hub plate, icon and title, purses or a skill readout at the right end, an ember wipe. `Dims => true`. Column widths come from `TradeRules` off `UsableWidth` |
| Trade rows | `TradeRow.Build`: a compact card that is its own verb. A click selects, a second click or accept acts. A refused row stays focusable and greyed, says why and plays `UiCue.Denied`. Names take a second line before they trim |
| Order bar | Quantity picker and verbs (buy, sell, craft, craft max, store, take) under the lists, wired back to the row it acts on (`TradeRow.WireOrderBar`). In crafting it sits under the ingredient and result columns so the recipe list runs the full height |
| Price ledger | `UiTheme.PriceLedger(quote)`: reason on the left, gold right-aligned, a hairline, the final price in ember. "No charge" is for a waived travel fee only; a sale that pays nothing reads `0g` |
| Ingredients and risks | `TradeMark` draws tick, cross or plus for enough, short or supplied; contract risks are chips with an icon and words |
| Perk nodes | `PerkNodeVisual`: Locked (padlock), Available (hollow diamond), Owned (filled diamond), Maxed (ringed diamond), each with its own frame; a capstone gets a bracket. The route from owned perks to the focused one is lit. Up and down follow prerequisite lines (`FocusTargetAlongEdges`) |
| Objective and option marks | `MarkGlyph`: tick done, cross missed, filled diamond current, hollow diamond optional, padlock locked. Dialogue options: filled diamond for plot, hollow for any other consequence, tick for already asked, dash for leave |
| Reading measure | Prose is EB Garamond held to about 80 characters through `UiTheme.Measure` (`JournalLayoutRules.ProseWidth`) |
| Scroll reset | A detail column returns to its head when the selection changes |

### 13.6 Shell rules

| Screen | Rule |
| ------ | ---- |
| Attended gate | `ShellFrontRules.Attended`: a real display, the ordinary user folder and no arguments after `--`. The boot splash and first-run setup wait for a button, so neither appears when it is false. Every gate, probe, capture and `--play` run fails it |
| Unattended | `ShellSessionRules.Unattended` is the same test for a session: the death screen is a no-op and death is the old same-frame respawn; a narration skips on a tap instead of a hold |
| First-run gate | `ShellFrontRules.FirstRun`: attended, no saves, and a settings file that is missing or younger than the process (the settings service writes it during a first boot). Asked once per process |
| Boot splash | Black, the seal, "Press any button". Once per process. It stops taking presses the moment it lifts |
| Title | A full-bleed painting (`TitleBackdrop`: slow drift and embers, both off under reduced motion or `StaticMenuBackground`), a `Sheet` moved right with `SheetToRight`, entries built with `TitleEntry`. The act painting is read from the newest save's chapter flags (`ShellFrontRules.ActFromSaveText`), once per save per process, never headless. The sheet is rebuilt whenever the menu is shown or the viewport resizes; an open quit prompt and the focused entry survive a resize |
| Sub-screens of the title | Slots, creator, settings and first run keep the title's painting under a 0.84 scrim (`SheetOverPainting`, `TitleSheetScrim`). The painting stays opaque while the rest fades in |
| Loading | `LoadingCoordinator` publishes `LoadingProgressEvent(RegionId, Step)` when a load opens and as each of its four stages clears (landing cell, collision, realm settle, placement), never per frame. The screen shows the destination's painting and name, a 2 px progress line and one card from `LoadingCardRules`: a tip, or lore for a realm the map has discovered. With no event it shows the generic painting and an indeterminate sweep. No hold, no press to continue. No card names the hidden realm, pinned by test |
| Save slots | A sheet of `Card` rows: thumbnail, drawn kind glyph and word, name, region, level, corruption pips and tier, playtime, local-time date; damaged, newer and backup badges. Thumbnails decode lazily, one a frame, for rows in view. Delete and overwrite are hold rings |
| Pause | A `Sheet` at scrim 0.55 with the tracked quest's current objective and time played; entries are `SessionAction`s in a list fitted to the measured header (`FitMenu`) |
| Creator | Step rail, options, preview. The preview is a turntable (right stick or drag) with three light rigs, a face framing for head slots, and the idle animation; Q/E or LB/RB step the rail; race and background tooltips wrap at 56 characters |
| Death | `DeathScreen.Begin()` from `PlayerHost`; the respawn itself still happens the same frame. A centred sheet at scrim 0.9 darkens over 0.8 s (`DurationDeath`), one Cinzel line, an ember wipe, and after 1.5 s "Rise" and "Load last save" (greyed with a reason when there is none). A pausing modal. A no-op when unattended, when no player is registered, outside `Playing`, or under another menu or lock |
| Narration | Ending cards get their ending's painting, chosen from the card script's own prefix, under a 0.6 wash. Skip is a hold ring; the timeline advances regardless, so zero input still finishes (`--story` depends on it). Pause opens a Resume / Skip `SessionPrompt` |
| Credits | `CreditsScreen.Open`: a roll in a window that stops above the legend and fades at its edges. 34 px a second, six times that while accept is held, up and down or the wheel by hand; under reduced motion it does not move by itself; the end returns to the title |

### 13.7 Settings

| Part | Rule |
| ---- | ---- |
| Tabs | `SettingsTab`: Graphics, Audio, Controls, Gameplay, Interface, Accessibility, on `menu_tab_prev`/`next`. `SettingsTabRules` maps every `[Export]` field of `Settings` to exactly one tab, checked by reflection in `SettingsTabRulesTests` |
| Open | `SettingsPanel.Open(parent, onBack, initialTab, backdrop)`. The title's Accessibility entry opens on that tab |
| Description pane | Driven by focus (hover also updates it): what the option does, and a preview for text size, subtitles, colour vision and the HUD diagram. Below 1000 logical px it folds under the list, three lines, no previews |
| Rows | `SettingsRowStyle`; every toggle, dropdown and slider is `ControlHeight`. The wheel scrolls the list and never steps a slider |
| Revert | A changed row shows a drawn restore-default button, also on `menu_sub_prev` (Z / LT) |
| Reset | "Reset tab" and "Reset all settings" are hold rings. A tab reset touches only that tab's fields; only the Controls tab reset takes bindings; "Reset all" keeps bindings and accessibility options (`SettingsService.ResetTab`, `ResetAllButBindingsAndAccessibility`) |
| Rebuild | The sheet rebuilds from a dirty flag, restoring focus and scroll. UI scale and text size apply when a drag ends; a key or pad step applies at once |
| Saved numbers | Every new field is append-only with a default that means "as before" (`docs/NOW.md` invariant 46). `DamageNumberMode` -1 follows the old bool. `SpellEffects` -1 follows the graphics preset and 0 to 4 are Performance to Ultra in visual order (not `RenderQuality`'s saved order); it is a Graphics-tab dropdown with its own "Follow preset" entry, outside the preset's Custom logic. `HudElement.SpellWheel` is 14 and stays Always under the Minimal preset |

**Remapping** (`InputBindingRules`, `GameInput.ApplyBindings`).

| Topic | Rule |
| ----- | ---- |
| What can be rebound | 31 actions (`InputBindingRules.Actions`), each with a keyboard cell and a pad cell. Fixed: pad movement (the left stick), the pad hotbar (the chord), pause, the menu tab actions, the look stick |
| Saved format | `Settings.KeyBindings` / `PadBindings`: one `action=binding` entry per remapped action. The binding is `key:E`, `mouse:Left`, `joy:A`, `axis:TriggerLeft:+` or `none`, in the engine's enum names so the file can be edited by hand. No entry means the default; an unreadable entry is ignored |
| Reserved inputs | Keyboard: Esc, Enter, keypad Enter, Tab, the arrows, F1 to F12, Meta, Print Screen. Pad: Start and Guide, and every axis except the two triggers |
| Listening | The prompt names its exit with a glyph: the pause button (Esc / Start), not B, because B is a bindable default. Clearing a binding is `menu_sub_next` (C / RT) on a focused cell |
| Conflicts | Swap, Unbind other or Cancel. Restoring one row's default also restores any remapped action its default now collides with (`InputBindingRules.Restore`), so half a swap never leaves two actions on one input |
| When it applies | At boot after `EnsureActions`, and when a saved list changes. It refuses while text entry or the hotbar chord has bindings parked, and the panel retries. With nothing remapped it changes nothing |
| Prompts follow | `KeyLabel`, `PadLabel`, `PromptLabel` and `UiGlyph` read the input map. `ApplyBindings` publishes `InputBindingsChangedEvent`, and also `InputDeviceChangedEvent` for the widgets that only listen for that |

### 13.8 Input

New actions (`GameInput`). None is `ui_`-prefixed, so `GameInput.Park` covers them during text entry.

| Action | Keyboard | Pad | Does |
| ------ | -------- | --- | ---- |
| `menu_tab_prev` / `menu_tab_next` | Q / E | LB / RB | step hub screens, settings tabs, the creator's rail |
| `menu_sub_prev` / `menu_sub_next` | Z / C | LT / RT | call `OnSubTab` on the open panel |
| `hud_recall` | N (hold) | none by default; bindable | show every Dynamic HUD element |

One existing action changed meaning with the 2026-10 spell wheel, and it is still one of the 31
rebindable actions: the count did not move.

| Action | Keyboard | Pad | Does |
| ------ | -------- | --- | ---- |
| `cycle_spell` | F | LB | hold: the spell wheel (mouse or right stick steers, release selects, Block cancels). Tap, under 0.16 s: the previous spell. Its binding row reads "Spell wheel (hold) / previous (tap)" (`settings.bind.action.cycle_spell`) |
| `cast` | Q | RB | unchanged: cast the prepared spell |

The id stays `cycle_spell` so saved bindings keep working and `KnowledgeInput` keeps borrowing it.
With presses in place of holds the action toggles the wheel, Attack or a second press selects, and
there is no tap. Draw its hint with `UiGlyph.For(GameInput.CycleSpell)` and say "hold" or "press"
from that setting (`hud.spell.wheel_hold` / `wheel_press`, `tutorial.spell_wheel_hold` / `_press`),
never a literal key. When the wheel cannot open (the element is hidden, no spells, a cast in
progress) the press steps to the next spell, which is what the action did before.

What the sub-tab actions and the borrowed inputs mean per screen:

| Screen | Input | Does |
| ------ | ----- | ---- |
| Character | Z / C, LT / RT | walk Pack, Materials, Progression, Perks, Guilds, wrapping |
| Journal | Z / C, LT / RT | step sections |
| Journal | F / X | track or untrack (`KnowledgeInput.Primary`) |
| Journal | V / R3 | show on map (`KnowledgeInput.Secondary`) |
| Map | Z / C, LT / RT | zoom; the rail's Place and Legend tabs are reached by focus |
| Map | X, R3 (pad only) | select the pin the cursor has snapped to; set the waypoint there, or clear it. The mouse keeps click and right-click |
| Bestiary, Spellbook | Z / C, LT / RT | step categories, step schools |
| Crafting | Z / C, LT / RT | Craft, Reforge, Salvage |
| Dialogue | E / RB (H still works) | history |
| Dialogue | right stick, Page Up / Page Down | scroll a long line |
| Item card (any screen) | Shift / L3 | compare side by side |
| Settings | Z / LT, C / RT | restore a row's default, clear a binding |
| Creator, trade detail | right stick | scroll the options or the detail column |

`KnowledgeInput` borrows play actions split by device (cycle spell and interact, camera and lock
on) because no key and button pair was free; a press only counts from the device its half belongs
to. The compare input borrows `sprint`. Dedicated menu actions would replace all three.

### 13.9 Harnesses

Every harness is a `ShotHarness` under `src/Debugging/` (excluded from a shipping build), drives real
screens through `*ForCapture` hooks, and checks the state of each shot in `ValidateShotState` before
it is written, so a frame is evidence only of the state it names.

| Flag | Harness | Covers |
| ---- | ------- | ------ |
| `--shellshots` | `ShellShots` | title per act, splash, first run, quit prompt, creator races, looks and backgrounds, each settings tab, remap listening and conflict, the narrow settings layout, slots from the title, loading per realm at two stages, credits |
| `--metashots` | `MetaShots` | pause sheet, death hold and options, creator steps, turntable and light rigs, slot rows and the delete hold, both ending paintings, the narration pause prompt |
| `--hudshots` | `HudShots` | vitals low and empty, statuses, party and hotbar states, tracker (tracked, hint, folded), waypoint on the compass, night and dawn, boss frame, menu open, toast stack, chapter banner, Dynamic and Minimal presets, HUD scale 0.85 and 1.25, safe zone |
| `--combat-shots` (alias `--combatshots`) | `CombatShots` | hit and crit numbers, guard, telegraphs, lock-on acquire and held, poise break, enemy plates, each damage-number mode, captions with and without a speaker, toast deferral, release and collapse, the boss bar at two layout widths |
| `--panelshots` | `PanelShots` | map at each zoom, legend, pin snap and travel confirm; journal sections and show on map; inventory, progression, materials; shop; dialogue tags, typing and complete; guilds; perk tree states and respec; bestiary stages; the spellbook on the hub |
| `--uishots` | `UiAuditShots` | pause, spellbook, bestiary, character Progression, crafting, storage, contracts, appraisal, save slots, new pips, compare (both views), slot filter, perk states, handheld inventory and perks |
| `--tradeshots` | `TradeShots` | vendor, compare, junk confirm, crafting columns and modes, storage, contracts, appraisal, three handheld frames |

`--guild-shots`, `--shrine-shots`, `--enemy-shots` and `--look-shots` predate the upgrade and are unchanged.

**Frames added by the 2026-10 spell wheel work.** They sit in the two existing harnesses, each
validating the state it names, and use `WheelShotFixtures` (`src/Debugging/WheelFixtureShots.cs`),
which teaches every player-learnable spell and puts the save's spell state back afterwards.

| Harness | Frame | Shows |
| ------- | ----- | ----- |
| `--hudshots` | `01a-spell-row` | the spell row with its disc, the wheel hint line and the tap ghost |
| `--hudshots` | `01b-wheel-favourite` | the wheel open, cursor on favourite 1, the readout naming it |
| `--hudshots` | `01c-wheel-school-fan` | the Fire wedge lit and its fan open past the rim, cursor on a Fire spell |
| `--hudshots` | `01d-wheel-cooling` | a favourite with a pie wipe and a seconds numeral |
| `--hudshots` | `01e-wheel-unaffordable` | mana empty: costed favourites unlit with their price, the HUD row's disc unlit |
| `--hudshots` | `01f-wheel-closed` | the wheel gone and the save's own spells back in the row |
| `--panelshots` | `29b-spellbook-pins` | the eight pin slots with slot 8 an empty socket |
| `--panelshots` | `29c-spellbook-pin-focused` | the focus ring on a card's Pin button |

No frame covers the presses-in-place-of-holds variant, high contrast or a HUD scale other than 1
for the wheel. ⚠️ `WheelShotFixtures.TeachEverything` selects a spell, which publishes
`SpellSelectedEvent` and completes the tutorial's wheel step if that hint is on screen during a run.

Three more harnesses came with that work and are not UI harnesses: `--spellshots`, `--camshots`
and `--vfxperf` (`SpellShots`, `CamShots`, `VfxPerfScenario`). They and their variables are in
`docs/NOW.md` → Commands. `SpellShots` and `CamShots` derive from `TimedShots`, a `ShotHarness`
that holds the frame counter until a shot's own condition is met (a wind-up at 60%, a bolt in
flight), which the fixed 30-frame hold cannot do.

| Variable | Does |
| -------- | ---- |
| `EMBERVALE_RES` | window size, `WIDTHxHEIGHT` (default 1280x720; `EMBERVALE_SHOT_SIZE` is an older alias) |
| `EMBERVALE_SHOT_UISCALE` | UI scale for the run; `1280x800` with `1.5` is the handheld's 853x533 logical view |
| `EMBERVALE_SLOT` | which save a session harness continues |
| `EMBERVALE_USER_DIR` | an isolated user folder (also what makes a run unattended, §13.6) |
| `EMBERVALE_ARTIFACTS` | where the PNGs go, one folder per flag |
| `EMBERVALE_FRAMES` | frames to settle before each shot (default 90) |

Capture hooks are public members named `...ForCapture`, plus read-only accessors the validators use.
The ones a harness depends on, and that a refactor must keep:

| Class | Hooks |
| ----- | ----- |
| `MainMenu` | `OpenSplashForCapture`, `OpenFirstRunForCapture`, `OpenCreditsForCapture`, `OpenSlotsForCapture`, `OpenCreatorForCapture`, `OpenSettingsForCapture`, `SetActForCapture`, `SettleForCapture`, `OpenQuitConfirmForCapture`, `CloseQuitConfirmForCapture` |
| `SettingsPanel` | `ShowTabForCapture`, `SetNarrowForCapture`, `ListenForCapture`, `ShowConflictForCapture`; the option list is the first `ScrollContainer` under the panel |
| `LoadingScreen`, `CreditsScreen` | `RegionForCapture`, `PaintingForCapture`, `StepForCapture`, `CardForCapture`; `SetScrollForCapture`, `OffsetForCapture` |
| `PauseMenu`, `DeathScreen` | `OpenForCapture`, `CloseForCapture`, `RequestLoad`; `BeginForCapture`, `ShowOptionsForCapture`, `EndForCapture` |
| `SaveSlotPanel`, `CharacterCreator` | `Configure`, `ShowRowsForCapture`, `HoldDeleteForCapture`, `ReleaseHoldForCapture`; `ShowStepForCapture`, `FocusSlotForCapture`, `SetRigForCapture`, `SetYawForCapture`, `SelectLookForCapture`, `SelectBackgroundForCapture` |
| `NarrationSequence` | `ShowCardForCapture`, `PauseForCapture`, `EndForCapture` |
| `GameHud`, `HotbarPanel` | `SettleDynamicForCapture`, `TrackerRowsForCapture`, `TrackerFoldedForCapture`, `SpellWheelHintForCapture`, `SpellGhostForCapture`; `SlotStateForCapture` |
| `SpellWheel`, `SpellbookPanel` | `OpenForCapture(cursor, toggled)`, `HoverForCapture`, `CloseForCapture`, `IsOpen`, `Hovered` (the cursor is in wheel units: rim 1, fan edge 1.35, +Y down; a capture open takes no gate, plays no sound and does not animate); `ShowUnpinnedForCapture`, `FocusPinForCapture`, `PinSlotsForCapture`, `PinFocusedForCapture` |
| `Notifications`, `SubtitleLayer`, `BossFrame`, `DamageNumberLayer` | `ClearShownForCapture`, `EndCombatForCapture`, `FlushLootForCapture`, `QueuedForCapture`, `ToastCountForCapture`; `ShowingForCapture`, `SpeakerShownForCapture`; `BarWidthForCapture`; `ClearForCapture` |
| `InventoryPanel`, `VendorPanel`, `CraftingPanel` | `FocusEquipmentSlotForCapture`, `CompareForCapture`; `CompareForCapture`, `ArmJunkSaleForCapture`; `ShowModeForCapture` |
| `MapScreen`, `QuestLogPanel`, `BestiaryPanel`, `DialoguePanel` | `ShowLegendForCapture`, `ShowPlaceForCapture`, `SnapForCapture`, `RequestTravelForCapture`, `CancelTravelForCapture`; `ShowSelectedOnMapForCapture`; `SelectForCapture`; `TypewriterForCapture`, `FinishLineForCapture` |

Three behaviours exist only so a harness is deterministic, and are worth knowing before debugging one:
the dialogue typewriter is off under any `--*shots` flag, when headless, or with `EMBERVALE_USER_DIR`
set; `TypewriterForCapture(true)` holds a line 40% written with no tween; and `PanelShots` does not
capture until the window is at the requested size and steady.
