# Embervale — UI Style Guide

The source of truth for every UI surface. Tokens live in code at `src/UI/UiTheme.cs`
(ornament at `src/UI/UiOrnament.cs`); this document explains what they mean and how to
use them. It answers to the art bible (`docs/ART_STYLE.md`): the UI is part of the same
dying world — **ash neutrals, bone-pale text, ember accents** — not a chrome layer
floating above it.

*Phase 30.5A established the token system. Phase 37.5A gave it a material and a voice:
three vendored typefaces, a grain shader, an engraved brass frame, three surface depths,
and the semantic ramps the UI had been going without.*

## 1. Identity

The world is *beautiful but dying*; the UI reads like objects from it — scorched
parchment, cooled iron, a candle held up to both. Four rules follow:

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

**Aged, not opulent.** Every material decision resolves that way. This is a world in
decline; its interfaces are well-made objects that have been used for a long time.

## 2. Palette tokens

### Surfaces — three depths

A control is understood by whether it sits *in* the panel or *on* it. Getting this
wrong is the fastest way to make a screen read as an undifferentiated wall of rows.

| Token | Value (sRGB) | Reads as | Use |
| ----- | ------------ | -------- | --- |
| `WellBg` | `0.055, 0.052, 0.048 @ 0.95` | cut **into** the panel | item slots, troughs, input wells, chip grounds |
| `PanelBg` | `0.09, 0.085, 0.075 @ 0.92` | the ground | every panel/toast surface |
| `CardBg` | `0.135, 0.126, 0.112 @ 0.95` | sat **on** the panel | item rows, spell cards, save slots |
| `Trough` | `0.13, 0.125, 0.115 @ 0.95` | — | bar backgrounds (predates the depth scale; kept) |

### Text

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `Text` | `0.79, 0.75, 0.68` | primary text (bone pale) |
| `Dim` | `0.58, 0.56, 0.50` | secondary text |
| `Disabled` | `0.40, 0.385, 0.35` | unavailable controls, unmet requirements |

`Disabled` is **deliberately not AA-pinned** — WCAG exempts disabled controls, and a
greyed row that reads as strongly as a live one gets clicked. It is held in a band
(≥2:1, <4.5:1, and always below `Dim`) by `DisabledStaysPerceivableWithoutReachingAa`.
Disabled state must always carry a second channel too: a reason string, a struck price.

### Accents, feedback, vitals, corruption

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `Accent` | `0.85, 0.64, 0.25` | headers, highlight, focus (ember gold) |
| `AccentHot` | `0.91, 0.45, 0.17` | crits/warnings only (ember orange) |
| `Good` / `Bad` | dead green / ashen red | semantic feedback |
| `Health` / `Stamina` / `Mana` | warm red / gold / desaturated blue | vitals fills |
| `Corruption` | `0.48, 0.30, 0.55` | corruption gauge + vignette (violet, fills only) |
| `CorruptionText` | `0.68, 0.48, 0.76` | corruption-tinted **text** (the fill violet fails AA) |

### Material and arcane (37.5A)

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `Brass` | `0.55, 0.44, 0.26` | the 2 px panel rule |
| `BrassLit` | `0.68, 0.56, 0.34` | divider highlights, corner ornaments |
| `Engrave` | `0.045, 0.042, 0.038 @ 0.90` | the dark groove under every bright rule |
| `ScrimBg` | `0.035, 0.032, 0.028` | full-screen dimming behind an overlay screen |
| `ArcaneGround` | `0.072, 0.070, 0.095 @ 0.94` | the spellbook's cold ink-violet ground |
| `ArcaneSilver` | `0.62, 0.66, 0.74` | the spellbook's tarnished frame |
| `GlyphLight` | `0.68, 0.74, 0.92` | rune circles, sigils, glyph light |

The engraved read comes from **two values at different depths** — a bright rule with a
dark groove behind it. A single border of any colour or width reads as a box.

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
`QuestSide`, `QuestComplete` (= `Good`), `QuestFailed` (= `Bad`).

**Disposition** — `Friendly` (= `Good`), `Neutral`, `Hostile` (= `Bad`).

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

**Always size through `UiTheme.FontSize(token)`, never the const directly.** It returns
the token unchanged today; Phase 37.5G multiplies by the player's text-scale setting
there, so that setting lands as one edit instead of a sweep through every builder.

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
- **The dialogue window is 60% of the viewport (320 to 560 px)** and its choices are one `RowGap` list; the chapter banner's band is 24% of the height with `SpaceSm` between its lines.

**HUD, toasts and shell (pass 3).** (1) A HUD card is a `Band`/`Card` whose stylebox is its padding: call `UiTheme.Compact(card)` and add the content directly. The old `Padding` wrapped inside a stylebox that already had 16 px margins left about 26 px of dead space above and below every HUD card, and made the cards tall enough to collide. (2) Inside a card, group by distance: bars or rows of one kind sit `SpaceXs` apart, groups `SpaceSm` apart, a heading hugs its title at `LineGap`. A tracker objective is one block (text row, "Optional" tag on its own line, bar, hint) so its parts stay together and objectives stand apart. (3) Chips that can multiply (status effects) live in an `HFlowContainer` with `ChipGap`; a box row stretches the card. A chip's `trailing` label is hidden until used, since an empty label still takes its separation. (4) Toasts start under the tracker wherever it ends and stop above the minimap: `Notifications` reads `GameHud.TopRightBottom`/`BottomRightTop`, admits as many toasts as fit (1 to 3) and lets the oldest fade early if the tracker grows. Never place a toast stack with a fixed offset. (5) Anything centred above the hotbar (prompt, tutorial hint, placement strip) sits at `HudLayout.BottomClearance` or higher. (6) A settings or slot list takes a row height of `ControlHeight` and `RowGap` between rows, scrolls inside the workspace frame, and gives the scroll a small floor so the frame keeps a visible margin from the window edge; a frame that is taller than its anchors only grows when something inside it has a large minimum height. (7) A shell button is `ControlHeight` tall; pause entries are not shrunk below it.

Check spacing by looking, not by arithmetic: `godot --path . -- --uishots` (pause, spellbook, bestiary, character Progression, crafting, storage, contracts, appraisal, save slots), `--panelshots`, `--hudshots` and `--shellshots` capture every screen. `EMBERVALE_RES=1920x1080` re-shoots at another window size (a 16:9 size lays out identically to 1280x720, because the project stretches `canvas_items`, so use `1280x800` for the handheld aspect), `EMBERVALE_SLOT` picks the save and `EMBERVALE_USER_DIR` + `EMBERVALE_ARTIFACTS` keep a run's saves and PNGs out of the shared user folder.

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
  ⚠️ **`Panel()` is a full screen, and nothing else.** It carries a 2 px brass rule, an engraved
  shadow *and its own grain `ShaderMaterial`*. Thirteen widgets across the overhaul had reused it
  as a generic box — status chips, save rows, toasts, the hotbar, the party strip, the tutorial
  hint, both debug overlays, and **five simultaneous `GameHud` widgets** (vitals, time/weather,
  quest tracker, event banner, interaction prompt). The HUD alone was rendering five brass frames
  and five grain shaders at once, more framing than the character screen uses.
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
  the two with the category glyph, the stack count and a tooltip, and is a `Button` so it gets
  focus, hover and activation for free. `ItemSlot.Detail(instance, equipped, compare)` is its
  companion card: rarity-coloured name, meta line, affix chips, stat deltas, flavour.
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
- **Prompt glyphs:** any on-HUD key hint uses `GameInput.PromptLabel` (device-aware) and
  refreshes on `InputDeviceChangedEvent` — never a hard-coded key name.
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
| main menu, boss frame, spellbook, level-up, a Legendary drop | inventory rows, settings toggles, quest objectives, tooltips, toasts |

If every surface is ornamented the ornament stops meaning anything, and readability is
the first thing to go.

`UiOrnament` provides `CornerBrass()` (four L-brackets, built from `ColorRect`s so they
retint with the palette for free — add as the panel's **last** child) and the three
animated motifs: `RuneCircle()`, `SigilField()`, `InkShimmer()`.

⚠️ **The motifs must live on a `ColorRect`, never on a `PanelContainer`.** A ColorRect's
UV is guaranteed to span 0..1 over its rect, and every one of these shaders does polar or
sweep maths in UV space. On a rounded panel the ring drifts off centre as the panel
resizes.

## 8. Accessibility (37.5G)

Three settings, all round-tripping through `user://settings.tres` for free (they are `[Export]`
fields on a Godot `Resource`).

| Setting | What it does |
| ------- | ------------ |
| `TextScale` (0.85-1.5) | Scales **glyphs only**, via `UiTheme.FontSize`. Distinct from `UiScale`, which is the window's content-scale factor and magnifies panels and margins too - this is for a player who wants readable text without surrendering half the screen to chrome. |
| `ColorVision` | Daltonizes the semantic ramps (rarity, school, standing, good/bad). |
| `HighContrast` | Surfaces go fully opaque, the grain material is dropped entirely, panel and card rules thicken. |

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
  ⚠️ **There are no item icons.** `ItemResource.Icon` has existed since Phase 5 and, at 37.5C,
  **no authored item set it and nothing read it** — so a literal icon grid would have been 26
  empty boxes. Slots show a *category glyph* instead (silhouette = category, colour = rarity,
  frame width = tier), and `ItemSlot` prefers a real `Icon` the moment one is authored. That is
  a floor, not a ceiling: the art phase is a data drop with no code change.
- **37.5D** ✅ lifted magic out of the character sheet into `SpellbookPanel` (`T` for tome) —
  the one screen that runs cold, and the only one spending all three motifs. It also surfaced
  two things the game had never shown: the **prepared-spell cycle order** that `Q`/`F` walk,
  and the **reactive combos** from `SpellCombo`'s rule table, live since Phase 29.5D and
  discoverable only by noticing a bigger number. Both read from the same authority combat uses.
  ⚠️ `ContentValidator` now gates the UI's fonts and shaders. Three of the four shaders only
  instantiate when a screen is *opened*, so a broken one appeared in no boot log, no `--play`
  run and no test — only in play, as "nothing is there".
- **37.5E** ✅ rebuilt map, quest log, dialogue and bestiary.
  ⚠️ **A journal section arrives with its state, never before it.** The Failed section was left out
  until `QuestStatus.Failed` existed (41B added both), because an empty heading is a permanent
  promise. Same call as the omitted Contracts and
  Exploration headings. Main/Side is real now: `QuestResource.IsMainQuest`, the field 37.5B
  refused to fake with a backwards "has a prerequisite" heuristic.
  ⚠️ **Quest markers are not on the map**, because quests carry no world position — Kill and
  Collect objectives name a template id, not a place. The hierarchy that *is* real is
  waypoints > regions > POIs, and fast-travel nodes had never been plotted at all despite
  carrying a `Position` since Phase 25G.
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

When those passes land, update this document — it must stay the single source of truth.

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
- *Done* steps carry a text tick (`questui.tick_line`), *locked* ones a padlock, *optional* ones an
  "Optional" chip (`UiTheme.Chip`), the tracked quest the word "Tracked", a quest with news a small dot
  (a drawn square, no font) and an asterisk on its tab.
- On the map a *filled* diamond is the main thread and an *outlined* one an errand; the ring is the
  tracked quest's alone. On the compass an *optional* objective is a hollow chevron.

**Journal.** Section tabs (Main Thread, Errands, Completed, Failed) exist only when they hold quests and step
with Q/E or LB/RB. Main groups under collapsible chapter headings (`chapter.<key>.title`, then
`pale.chapter.<key>.title`; a key-less group needs no heading). The detail card reads top to bottom: chips
(type, chapter, region, level), giver, prose (`DetailKey`, else the summary), Track, the stage log, rewards.
The stage log is ordered done, current (with its hint and its place), optional, locked; the place goes through
`ObjectiveNavigation.LocationId`, so Reach and Defend name their destination like every other type. Ledger
quests are listed under a folded "Ledger" row and are never tracked. Completed lists newest first and folds
after five.

**Tracker.** A chapter label above the title; the spine takes the quest's colour; optional rows carry the
Optional chip; the current objective's hint appears under it after `TrackerRules.HintDelaySeconds` (90 s)
on the same step; the header reads "Now tracking" for three seconds after the tracked quest changes.

**Toasts.** One player action publishes several quest events; `QuestNoticeCoalescer` folds a frame's worth
into one toast per quest (completion beats failure beats start beats next objective beats "updated"; an
optional step met is its own toast). Each plays one of five cues (`ui.quest.started`, `.updated`,
`.completed`, `ui.chapter.title`, `ui.objective.optional`) when it is actually shown. Companion barks are a
portrait-less toast: the line, then the speaker.

**Chapter banner.** `ChapterBanner` shows an act line, the chapter title and a subtitle in the lower third, once
per save per chapter (`flag.chapter.<key>`), without pausing the world. It queues behind any menu, dialogue or
narration sequence (they all register with `UiState`). Reduced motion keeps the fade and drops the rise; the
fade is deliberately not routed through `UiTheme.Duration`, which would collapse it. A chapter with no title
text is skipped rather than drawn as a raw key.

**Dialogue.** Each choice carries chips for what it will do (`DialogueConsequenceTags`): a quest start, corruption
`+N`/`-N`, reputation, companion loyalty, guild join/rank, items, and a neutral "Story" chip for flags and story
cards. Choices are numbered 1-9; the last three lines of the conversation replay on H or RB; a line under the
speaker names the objective when this person is the tracked quest's live Talk target.

**Compass.** The chevron takes the quest's colour. An objective whose place is in another realm points at this
realm's door toward it (`CompassRoutingRules.NextHop` over the region graph, skipping sealed realms) and labels
the realm; if no door resolves it falls back to the direct pointer.

**Map.** Quest pins show the current objective of every live non-ledger quest. Reveal is spoiler-safe
(`MapQuestReveal`): a quest reveals only the places its live objectives name and each later place when that
objective opens, never both branches of a fork. The legend names only the pin kinds on screen.

## 13. Foundation for the 2026-10 UI upgrade

What the foundation pass added for the lanes to build on. Values live in code; this is the map.

**Tokens** (`UiTheme`). A surface is a cut plate with one lit edge, not a box with four.

| Token | Use |
| ----- | --- |
| `Rule` | the cold hairline between rows, columns and tabs on one surface |
| `RuleLit` | the single lit edge of a plate or sheet; at most one per surface |
| `Keyline` | the dark 1 px outline around HUD bars, icons and glyphs drawn on the live world |
| `FocusRing` | the focus indicator only; brighter than `Accent`, 3:1 or better on every surface (`UiContrastTests`) |
| `EmberGlow` | translucent heat under a lit edge or a wipe; never text |
| `ScrimHub` | the one scrim all five hub screens share |
| `ButtonFace`, `ButtonFaceHover`, `ButtonFacePressed`, `ButtonFaceFocus` | the faces `Action` and `Dropdown` draw, public so the contrast audit reads the real values |
| `DurationTab` (0.16 s) | a tab or hub-screen switch |

**Type.** `FontSize(token)` bends the text-scale setting: sizes up to body take all of the change, header 85%, title 70%, display and up half (`ScaledFontSize`, pinned by `UiTypeScaleTests`). The 12 px floor stays. The `ReadableFont` setting sets every role in Inter (`ResolveRole`).

**Sheet.** `UiTheme.Sheet(width, scrimOpacity, centred)` returns `(Root, Column)`: a frameless column on a scrim with one `RuleLit` edge. The shell's ground (title, pause, death) in place of `Panel()`.

**Motion** (`UiFx`). `FadeIn`, `FadeOut(node, then)`, `Rise`, `Stagger(container)`, `Pulse`. Each kills the run in flight on its node, runs while the tree is paused, ignores time scale and collapses under reduced motion to its final state. Call them after the node is in the tree. Closing a menu is still instant. `UiOrnament.EmberWipe()` is the one opening flourish: a rule drawn by a line of heat, one per screen.

**Hold to confirm.** `UiFx.HoldRing(onComplete)` builds a `HoldRing`; drive it with `Attach(button)`, `HoldAction` or `Press()`/`Release()`. For irreversible actions only. The `HoldsToPresses` setting makes one press complete it; reduced motion never shortens the hold.

**Engine-drawn controls** (`UiSkin`). Sliders, check buttons and boxes, line edits, scrollbars, popup menus, tooltips and the default font are skinned by one code-built `Theme`: merged into the engine default theme at boot and whenever high contrast or text scale changes, and handed to each control by the `Slider`, `Toggle`, `Dropdown` and `ScrollList` builders. A hand-built control of one of those kinds calls `UiSkin.Apply(control)`. Icons are `assets/ui/icons/controls/*.svg` and carry their own colours.
