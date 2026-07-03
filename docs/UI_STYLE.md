# Embervale — UI Style Guide (Phase 30.5A)

The source of truth for every UI surface. Tokens live in code at
`src/UI/UiTheme.cs`; this document explains what they mean and how to use them.
It answers to the art bible (`docs/ART_STYLE.md`): the UI is part of the same
dying world — **ash neutrals, bone-pale text, ember accents** — not a chrome
layer floating above it.

## 1. Identity

The world is *beautiful but dying*; the UI reads like objects from it — scorched
parchment, cooled iron, a candle held up to both. Three rules follow:

1. **The UI is faded, not flat.** Surfaces are warm charcoal ash (never
   blue-black); text is bone pale (never pure white). Nothing on screen is fully
   saturated except accents.
2. **Ember is THE accent.** Ember gold (`Accent`) marks headers, highlights,
   selection and focus. Ember orange (`AccentHot`) is rationed for the hottest
   emphasis — crits, warnings, the Flamebearer thread. If everything glows,
   nothing does.
3. **Corruption is violet.** The corruption gauge, vignette and any
   corruption-tinted UI use the bible's corruption violet — the one cold accent
   allowed to compete with ember.

## 2. Palette tokens

| Token | Value (sRGB) | Use |
| ----- | ------------ | --- |
| `PanelBg` | `0.09, 0.085, 0.075 @ 0.92` | every panel/toast surface |
| `PanelBorder` | `0.42, 0.40, 0.35 @ 0.80` | 1 px panel frames |
| `Trough` | `0.13, 0.125, 0.115 @ 0.95` | bar backgrounds, wells |
| `Text` | `0.79, 0.75, 0.68` | primary text (bone pale) |
| `Dim` | `0.58, 0.56, 0.50` | secondary text, disabled |
| `Accent` | `0.85, 0.64, 0.25` | headers, highlight, focus (ember gold) |
| `AccentHot` | `0.91, 0.45, 0.17` | crits/warnings only (ember orange) |
| `Good` / `Bad` | dead green / ashen red | semantic feedback |
| `Health` / `Stamina` / `Mana` | warm red / gold / desaturated blue | vitals fills |
| `Corruption` | `0.48, 0.30, 0.55` | corruption gauge + vignette (violet, fills only) |
| `CorruptionText` | `0.68, 0.48, 0.76` | corruption-tinted **text** (the fill violet fails AA) |

Rules: no colour literals in panels — new needs become new tokens here first.
Environment-style saturation discipline applies: only accents may exceed ~40%
saturation.

**Contrast is audited in code (30.5K):** `UiContrastTests` pins every text-on-
surface token pair to WCAG AA (≥4.5:1) and bar fills to ≥3:1 — retune a token
and the suite fails before the player squints. Text colour goes through font
colour overrides, **never whole-control `Modulate`** (modulate multiplies onto
already-dim fonts and sinks below readable — the audit caught two).

## 3. Type scale

| Token | px | Use |
| ----- | -- | --- |
| `CaptionFontSize` | 12 | slot numbers, hints, metadata — the legibility **floor** (30.5K) |
| `BodyFontSize` | 14 | default text |
| `HeaderFontSize` | 16 | section headers (ember gold) |
| `TitleFontSize` | 20 | screen/panel titles |
| `DisplayFontSize` | 26 | boss names, level-up, big moments |

Builders: `UiTheme.Caption/Body/Header` — reach for these before raw `Label`s.

## 4. Spacing & radius

Spacing scale (`SpaceXs..SpaceXl` = 4/6/10/16/24): use tokens for separations,
paddings and margins; `UiTheme.Padding()` defaults to `SpaceMd`. Radii:
`RadiusSm` 3 (bars), `RadiusMd` 4 (buttons), `RadiusLg` 6 (panels).

## 5. Motion

Durations: `DurationFast` 0.12 s (hover/press feedback), `DurationBase` 0.20 s
(panel/value transitions), `DurationSlow` 0.35 s (screen transitions, banners).
**Always** route through `UiTheme.Duration(x)` — it returns 0 when the player
has reduced motion enabled, collapsing animation to instant. Easing: prefer
ease-out for entrances, ease-in for exits; no bounces (this world is tired).

The 30.5I building blocks (use these, don't hand-roll):

- `UiMotion.EaseOut/EaseIn/Progress` — the pure curves (unit-tested); drive
  `_Process` timers with them.
- `UiTheme.AnimateModulate(control, target, seconds)` — a kill-previous,
  pause-proof modulate ease; the hover/press/focus feedback on every
  `Action`/`Dropdown` rides it.
- `UiPanel` fades its shell in on open for free; opening motion belongs there,
  not in subclasses. Closing is always instant — dismissal never lags input.
- Exits that reveal (the loading screen) fade out; entrances that cover are
  instant.

## 6. Widgets

- `Panel()` + `Padding()` — every framed surface; modals set `UiState.MenuOpen`.
- `Header/Body/Caption` — the three text levels; colour via token parameters.
- `Action()` / `Dropdown()` — interactive controls share one style (normal/
  hover/pressed/**focus**); the ember focus border is the visibility layer the
  gamepad navigation (30.5J) rides. Never ship a control without a visible
  focus state.
- **Focus navigation (30.5J):** menus must work without a mouse. `UiPanel`
  grabs/restores focus for you (open + across rebuilds via `UiFocus`); a new
  standalone screen calls `UiFocus.GrabFirst` when it shows. Lists live in
  `ScrollList()` (sets `FollowFocus`). ui_cancel (Esc/B) closes a modal —
  opt out via `CloseOnCancel` only for panels with their own lifecycle.
- **Prompt glyphs:** any on-HUD key hint uses `GameInput.PromptLabel` (device-
  aware) and refreshes on `InputDeviceChangedEvent` — never a hard-coded key
  name.
- `Bar(fill)` — thin resource bar on the shared `Trough`.
- Rebuild-from-dirty-flag in `_Process`, never inside a button signal
  (CLAUDE.md §8).

## 7. Text rules

Every player-facing string goes through `Loc.T`/`Loc.TF` (`data/locale/
strings.csv`) — no literals in labels/buttons/toasts. Sentence case for body
and actions; headers may be short title case. Numbers the player compares
(damage, weights, gold) stay unlocalised digits.

## 8. Roadmap seams

30.5B builds the HUD layout/scale container on these tokens; 30.5C–H rebuild
widgets and panels onto them; 30.5I adds motion using §5; 30.5J rides the focus
styles in §6; 30.5K tunes the type scale globally. When those passes land,
update this document — it must stay the single source of truth.
