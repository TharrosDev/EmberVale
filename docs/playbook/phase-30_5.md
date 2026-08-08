## Phase 30.5 — UI & HUD Overhaul `[P/F]`

> Take the functional UI (Phase 14/18) and the individual surfaces grown across 23–30 to
> **one cohesive, ship-quality** look. Build the design system first (30.5A), then HUD, then
> menus, then feel/input. All strings go through the Phase 24 `Loc` layer. Each sub-phase
> leaves the game buildable/playable; verify in-engine (build → `run_project`, CLAUDE.md §3).

- [x] **30.5A — Design tokens + `docs/UI_STYLE.md`** `[P]` ✅
  - **Goal:** the foundation every other surface answers to.
  - **Done:** `UiTheme` regrown into token groups with the public API preserved (all ~36
    consuming files untouched): palette retuned from generic blue-grey/gold to the art
    bible's identity (warm-charcoal `PanelBg`/`Trough`, bone-pale `Text`, ash `Dim`,
    ember-gold `Accent` + rationed ember-orange `AccentHot`, corruption **violet** per
    ART_STYLE §2); a five-step type scale (`Caption`→`Display`) with a new `Caption`
    builder; spacing (`SpaceXs..Xl`) and radius (`RadiusSm/Md/Lg`) scales consumed by the
    styleboxes; motion tokens (`DurationFast/Base/Slow`) behind `UiTheme.Duration()`, which
    collapses to 0 under the reduced-motion setting (the 30.5I guard, landed early); and a
    shared interactive style with an ember **focus** stylebox on `Action`/`Dropdown` — the
    visibility seam 30.5J's gamepad navigation rides. `docs/UI_STYLE.md` written as the
    source of truth (identity, tokens, type/spacing/motion rules, widget + text rules,
    roadmap seams). Build + 313 tests green; booted in-engine to the token-rendered main
    menu, no errors.
- [x] **30.5B — HUD architecture & layout system** `[F]` ✅
  - **Done:** `HudLayout` (`src/UI/HudLayout.cs`) — a full-screen, mouse-transparent root with
    anchored **slots** (`TopLeft`/`TopCenter`/`TopRight`/`BottomLeft`/`BottomCenter` stacks +
    a free `Overlay` layer) inside a single safe-area margin (`SafeMargin`, token `SpaceLg`),
    replacing every per-widget anchor/offset in `GameHud`. The top-centre widgets (compass,
    boss bar, event banner, nameplate) now stack with automatic spacing — hidden widgets
    collapse, fixing the hand-tuned y-offsets (6/14/40/66) that visually collided when
    several showed at once. **UI scale made real:** the previously-dead `Settings.UiScale`
    (0.75–1.5 slider, Phase 24F) now drives `Window.ContentScaleFactor` in
    `SettingsService.ApplyGraphics` — with the project's `canvas_items` stretch this scales
    every 2D surface live, no per-widget math. **Bring-up findings (live maintainer
    playtest):** anchors applied *after* tree entry resolved against a stale zero rect and
    dumped every widget at the origin — `HudLayout` now builds fully in its constructor
    ("build detached, then add", §6). The bottom edge became a full-width **flow bar**
    (vitals left, twin spacers centring a `BottomDock` the quick-use hotbar parents into via
    `HotbarPanel.Dock`) after the maintainer hit the hotbar/vitals overlap at 1.5× UI scale —
    flow siblings can't overlap at any scale/resolution (hotbar cells 112→88 px, and its
    visibility toggle moved from the layer to the docked panel). Placement sweep across all
    other surfaces: toasts moved top-centre→top-right (the centre column belongs to the
    boss/banner/nameplate stack), the F3 DebugHud readout dropped below the clock widget and
    its controls hint moved to bottom-right, the journal overlay dropped below the clock.
    Build + 313 tests green; verified across three live maintainer play sessions.
- [x] **30.5C — Core HUD widgets rebuilt** `[F/P]` ✅
  - **Done:** `JuicedBar` (`src/UI/JuicedBar.cs`) — a themed resource bar with value-change
    juice: rises instantly (heals feel responsive), drains with a ~0.9/s lag so hits read as
    a chunk sliding off, and pulses the fill white-hot for 0.25 s on a drop; honours reduced
    motion (snaps, no pulse) and exposes `Snap()` for subject changes. Drives the three
    vitals, the nameplate (snaps when the aimed-at target changes so lag never animates
    across subjects) and the boss bar (snaps full on encounter start). **Prepared-spell
    widget:** school-tinted spell name + READY(accent)/charging/channeling/`N.Ns` state
    caption + a thin school-tinted recovery bar that fills while the spell cools down —
    replacing the old footer text blob (footer now shows level only). **Status-effect
    chips:** the text line became per-effect chips (buff = dead-green, affliction = school
    colour, live countdown); the row rebuilds only on set-signature change, timers update
    in place. Crosshair joined the palette (bone-pale token). Build + 313 tests green;
    verified during a live maintainer combat session (goblin fight: damage drain/pulse and
    nameplate exercised, no errors).
- [x] **30.5D — Wayfinding HUD** `[F/P]` ✅
  - **Done:** the wayfinding surfaces unified on the 30.5A/B system. **Interaction prompt:**
    a real keycap chip (`UiTheme.KeyCap`) + prompt text, the cap's glyph resolved live from
    the InputMap via `GameInput.KeyLabel` (remap-proof — the Phase 54 seam). **Quest
    tracker:** the per-frame StringBuilder blob became structured rows (accent title, one
    caption per objective, complete objectives tick to dead-green with ✓) rebuilt only when
    the quest id/progress signature changes. **World-event banner:** the countdown split
    into its own readout that heats to ember orange (`AccentHot`) in the final 10 s.
    Compass and nameplate were already on-system (30.5B/C); toasts were restyled/moved in
    30.5B and keep their fade (slide motion is 30.5I). Build + 313 tests green; verified in
    a live maintainer session (goblin kills advanced the tracker rows; loot prompts used
    the keycap chip; no errors).
- [x] **30.5E — Combat & boss HUD** `[F/P]` ✅
  - **Done:** combat feedback unified on the identity. **Crosshair hit-marker:** landing
    damage kicks the reticle arms outward for 0.18 s — ember orange (`AccentHot`) on crits,
    bone pale otherwise — so hits confirm at the point of aim; suppressed under reduced
    motion, and the reticle only redraws while a pop decays. **Screen-flash tints**
    (`CombatFeedbackFx`, still pure/unit-tested) retuned from generic gold/steel-blue to the
    dying-world identity: crit = ember orange, block = cold steel, stagger = ashen red,
    parry = bright ember-gold pop; under reduced motion the full-screen flash is suppressed
    (photosensitivity) and the state word alone shows. **Lock-on reticle** breathes
    (0.8–1.0 alpha sine) so the lock reads live, motion-gated. Boss bar was juiced in 30.5C
    (drain lag + snap-full on encounter start); the corruption vignette moved to the
    bible's violet in 30.5A. Build + 313 tests green; verified in a live maintainer combat
    session (damage dealt and taken, no errors).
- [x] **30.5F — Panel & screen framework** `[F]` ✅
  - **Done:** `UiPanel` (`src/UI/UiPanel.cs`) — the reusable panel shell every screen builds
    on: themed frame, optional toggle input action, the **modal contract** (register with
    `UiState` + free/recapture the mouse; non-modal overlays skip it via `Modal => false`),
    and the rebuild-from-a-dirty-flag loop (`MarkDirty` → at most one `Rebuild` per frame,
    never inside a button signal). Subclasses implement `BuildShell` (static layout, once)
    and `Rebuild` (dynamic rows). **`UiTabs`** — the shared tab strip (accent-highlighted
    active tab, `TabChanged` event). **`UiTheme.ScrollList`/`ClearChildren`** — the list
    body + rebuild-clear helpers every panel repeats. **Proving port:** `InventoryPanel`
    (the richest panel: modal + tabs + scroll list) rebuilt on the framework with ~80 lines
    of hand-rolled plumbing deleted, feature-parity. *Scope note:* item tooltips stay on
    Godot's native `TooltipText` (already themed-adjacent, zero code); a screen/route
    manager was deliberately skipped — the four meta screens (menu/creator/slots/loading)
    are wired directly by the bootstrap and a router would be speculative structure until
    30.5J's gamepad focus needs a screen stack. Build + 313 tests green; verified in a long
    live maintainer session (combat, region travel, saves; no errors).
- [x] **30.5G — Inventory / character / equipment / perks panels rebuilt** `[F/P]` ✅
  - **Done:** the sub-phase was authored before these surfaces merged into the tabbed
    character screen (maintainer note), so 30.5G = the four **tab pages** (Gear incl.
    equipment+backpack, Spells, Progression, Perks) fully on the 30.5F framework + tokens.
    The panel shell/tabs/scroll ported in 30.5F's proving port; this pass finished the
    content: the last colour literals tokenized (dread → `Corruption`, affix lines →
    `Caption` in `Good`), row spacing on the spacing scale, and a **level-XP progress bar**
    added to the Progression tab (accent fill, the same glanceable shape as the corruption
    gauge below it — previously XP was numbers-only while corruption had a bar).
    Feature-parity everywhere else. Build + 313 tests green; verified in a live maintainer
    session (post-load full-vitals confirmed at legendary-boosted max HP; no errors).
- [x] **30.5H — Crafting / dialogue / journal / map panels rebuilt** `[F/P]` ✅
  - **Done:** all four remaining panels ported onto the 30.5F `UiPanel` framework, deleting
    their duplicated modal/mouse/dirty-flag plumbing. **CraftingPanel** — modal; the
    craft/salvage switch moved from rebuilt-per-frame disabled-button rows into a static
    `UiTabs` strip in the shell (resets to Craft on open), title now a static header updated
    per station; keeps its E-to-close with the just-opened swallow via a `_Process` override
    around the base. **DialoguePanel** — keeps its modal behaviour (per spec); event-driven
    open/close and the `DialogueEndedEvent` publish unchanged. **QuestLogPanel (journal)** —
    stays **non-modal** (`Modal => false`, per spec), J-toggle via the base's `ToggleAction`;
    its literal colours tokenized (title `Accent`, done rows `Good`, pending `Text`).
    **MapScreen** — modal (Phase 25G fast-travel buttons need the mouse; the spec's
    "non-modal" predates 25G), M-toggle via the base; revision-diff refresh kept as a
    `_Process` override that `MarkDirty()`s. Build + 313 tests green; booted in-engine to
    the world with all panels constructed, no errors.
- [x] **30.5I — Motion & microinteractions** `[F/P]` ✅
  - **Done:** the motion pass on the 30.5A tokens, all of it behind the reduced-motion guard
    (`UiTheme.Duration`/`MotionEnabled` collapse everything to instant/static). **`UiMotion`**
    (`src/UI/UiMotion.cs`) — the pure ease-out/ease-in/progress curves, unit-tested like
    `CompassMath`. **Panel transitions:** `UiPanel.SetOpen` fades the shell in (ease-out,
    `DurationBase`) for every panel on the framework — closing stays instant so dismissal never
    lags input; the pause menu fades in the same way, and the loading screen now fades *out*
    (`DurationSlow` ease-in exit) to reveal the arrival while still covering instantly on entry.
    **Toasts:** the slide-in deferred from 30.5B — `Toast` became a margin wrapper around the
    chip (+s/−s margins shift it without fighting the stack container) sliding 24 px in from
    the right while fading up. **Hover/press:** `UiTheme.ApplyInteractiveStyle` layers a
    modulate ease (`AnimateModulate`, kill-previous, pause-proof) over the stylebox swap —
    brighten on hover/focus (the 30.5J seam), sink on press, on every `Action`/`Dropdown`.
    **Value changes:** damage juice already landed in 30.5C/E (drain lag, pulse, hit-marker);
    this pass added the XP/level beats — a "+N XP" pop under the level footer that accumulates
    rapid gains then fades, and a centred Display-size "Level N" flourish (fade in → hold →
    fade out with a slight rise; ember gold). Build + 317 tests green; verified in a live
    maintainer session (pause/settings cycles, two region transitions with the loading fade,
    pickups; no errors).
- [x] **30.5J — Gamepad & focus navigation** `[F]` ✅
  - **Done:** controller/keyboard drive every menu; no menu is mouse-only. **Input layer:**
    `GameInput.BindGamepad` — Start=pause, Y=inventory, Select=journal, D-Up=map, X=interact,
    and the left stick mapped onto the built-in `ui_*` focus actions (D-pad/A/B are engine
    defaults); full gameplay-on-controller stays Phase 54. **Focus system:** `UiFocus`
    (`src/UI/UiFocus.cs`) — grab-first-focusable on open, and a child-index **path
    record/restore across the dirty-flag rebuild** so a rebuild never strands focus; wired
    into `UiPanel` (grab on open, restore on every rebuild), the pause menu, settings, main
    menu (re-grabs whenever a sub-screen restores it), save slots (deferred re-grab after
    refresh) and the character creator. `ScrollList`/settings scrolls set `FollowFocus`.
    **Cancel routing:** ui_cancel (Esc/B) closes modal `UiPanel`s (dialogue opts out — it ends
    via choices), backs out of settings/slots/creator (creator ignores it while a text field
    has focus), resumes from the pause menu; a `LastCancelCloseFrame` stamp keeps one Esc
    press from both closing a panel and opening the pause menu. **Device-aware glyphs:**
    `InputDevice` (fed by `GameManager._Input`) tracks the active device and publishes
    `InputDeviceChangedEvent` (mouse *motion* deliberately ignored); `GameInput.PromptLabel`
    resolves key vs pad glyphs live from the InputMap and the HUD keycap swaps "E" ↔ "X" on
    device flip. The pad-button label map is pure and pinned by tests (317 → 327). Build +
    tests green; booted in-engine to the token-rendered main menu with the focus grab live,
    no errors. Full-loop gamepad play verified next maintainer session.
- [x] **30.5K — UI scale & legibility pass** `[F/P]` ✅
  - **Done:** the global UI-scale option already landed real in 30.5B (`Settings.UiScale`
    0.75–1.5 → `ContentScaleFactor`), and the 1280×720 `canvas_items`/expand base means Steam
    Deck (1280×800) renders at reference scale — so this pass was the **audit**, made
    executable. **`UiContrast`** (`src/UI/UiContrast.cs`) — pure WCAG luminance/ratio math;
    **`UiContrastTests`** pins every text-bearing token pair ≥4.5:1 (AA) and bar fills ≥3:1,
    so a palette retune can never silently regress readability. The audit caught and fixed:
    `Dim` below AA on button faces (retuned 0.55/0.53/0.47 → 0.58/0.56/0.50); the deep
    corruption violet used as *text* at 2.8:1 (new `CorruptionText` token for text, the
    bible's fill violet untouched for gauge/vignette); and two **double-dim modulate bugs**
    (`UiTabs` inactive tabs and the HUD spell-state caption multiplied `Dim` onto already-dim
    fonts, ~2.9:1) — both now swap font colour, and the style guide bans whole-control
    modulate for text. Font sizing: `CaptionFontSize` 11 → 12, the pinned legibility floor.
    Build + 344 tests green (17 new); booted in-engine clean. **Phase 30.5 complete.**

---
