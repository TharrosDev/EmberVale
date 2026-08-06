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
| `BodyFontSize` | 14 | default text |
| `HeaderFontSize` | 16 | section headers (ember gold) |
| `TitleFontSize` | 20 | screen/panel titles |
| `DisplayFontSize` | 26 | boss names, level-up, big moments |

**Always size through `UiTheme.FontSize(token)`, never the const directly.** It returns
the token unchanged today; Phase 37.5G multiplies by the player's text-scale setting
there, so that setting lands as one edit instead of a sweep through every builder.

Builders: `Title/Display/Header/Body/Prose/Flavour/Caption` — reach for these before raw
`Label`s. They pick the role for you.

## 4. Spacing & radius

Spacing scale (`SpaceXs..SpaceXl` = 4/6/10/16/24): use tokens for separations, paddings
and margins; `UiTheme.Padding()` defaults to `SpaceMd`. Radii: `RadiusSm` 3 (bars, wells,
chips), `RadiusMd` 4 (buttons), `RadiusLg` 6 (panels).

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

- `Panel()` / `Well()` / `Card(edge)` — the three depths. `Card`'s `edge` paints a left
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
- Rebuild-from-dirty-flag in `_Process`, never inside a button signal (CLAUDE.md §8).

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

## 8. Text rules

Every player-facing string goes through `Loc.T`/`Loc.TF` (`data/locale/strings.csv`) — no
literals in labels/buttons/toasts. Sentence case for body and actions; headers may be
short title case. Numbers the player compares (damage, weights, gold) stay unlocalised
digits.

## 9. Roadmap seams

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
- **37.5D** lifts magic out of the character sheet into `SpellbookPanel`, the one screen
  that runs cold (`ArcaneGround`, `ArcaneSilver`, the rune circle).
- **37.5E** rebuilds map, quest log, dialogue and bestiary.
- **37.5F** rebuilds the shell, and migrates the genuinely-modal `CanvasLayer` screens
  onto `UiPanel`.
- **37.5G** adds text scale, colourblind modes and high contrast, and audits
  responsiveness.

When those passes land, update this document — it must stay the single source of truth.
