## Phase 37.5 — AAA Fantasy UI Overhaul `[F/C]`

> **Why an interstitial.** Phase 30.5 built a real design *system* (tokens, `UiPanel`, focus
> restore, WCAG pins, `Loc`) and the audit found nothing to re-architect. What the UI lacked was
> **craft**: no font assets at all, no textures or shaders, flat 1 px boxes, and no ramp for
> rarity/school/quest/disposition. This phase is material and hierarchy, not plumbing — which is
> exactly why it stayed out of 30.5 and why it lands before 38A rather than after: every screen
> Phase 38+ adds should be born in the new language instead of being retrofitted later.

- [x] **37.5A — Foundation: type, material, tokens** `[F]`
  - **Done when:** every screen renders in the new typefaces on the new surfaces, with the
    semantic ramps available, and nothing has moved. ✅
  - Vendored **Cinzel / EB Garamond / Inter** (SIL OFL 1.1) to `assets/fonts/` with their
    `OFL-*.txt` — the project's first non-CC0 assets, and the licence files must travel with
    them. Wired as `FontRole` (Display / Serif / SerifItalic / Interface), loaded **lazily**
    with a fallback to the engine default.
    ⚠️ Lazy is load-bearing: `UiContrastTests` reads `UiTheme` tokens from xUnit with **no
    engine running**, so an eager `GD.Load` in a static initializer would take the suite down
    on first touch of any colour.
  - Four `assets/shaders/ui/` shaders: `ui_grain` (the material under every panel),
    `rune_circle`, `sigil_drift`, `ink_shimmer`. All read one `motion` uniform fed from
    `UiTheme.MotionUniform`, so reduced motion stops them together.
    ⚠️ The three motifs must sit on a **`ColorRect`**, never a `PanelContainer` — their polar
    and sweep maths need UV to span 0..1, which a rounded stylebox does not guarantee.
  - Surface depths `WellBg` / `PanelBg` / `CardBg`; material tokens `Brass` / `BrassLit` /
    `Engrave`; arcane tokens for 37.5D's cold screen. The engraved read is **two values at
    different depths**, not one thicker border.
  - **Retuned the rarity ramp.** The pre-existing `ItemRarities.Color` was stock saturated MMO
    green/blue/purple/orange — a direct breach of UI_STYLE §2 ("only accents may exceed ~40%
    saturation") that had been live since Phase 7. It is now an ash-world ramp with three
    properties pinned by `RarityRampTests`: luminance climbs strictly with rarity, adjacent
    tiers stay ≥1.15:1 apart, and Legendary out-burns `Accent`.
    ⚠️ Keep `ItemRarities.Color` as the **single authority** — `UiTheme.RarityColor` delegates
    to it, and `ItemPickupFactory`/`TrophyStandComponent` tint world-space meshes from the same
    values. Two copies means a drop that looks Epic on the ground and Rare in the pack.
  - Rarity is never colour alone: `UiTheme.RarityBorderWidth` is the second channel, kept pure
    (and therefore testable) because the test project forbids constructing Godot objects — a
    redundancy channel nothing checks quietly stops existing.
  - New builders so panels stop hand-rolling: `Well`/`Card`/`Divider`/`SectionRule`/`Chip`/
    `IconSlot`/`RarityFrame`/`Meter`/`Title`/`Display`/`Prose`/`Flavour`, plus
    `UiTheme.FontSize(token)` as the single seam 37.5G's text-scale setting lands in.
  - `UiOrnament` holds the decorative layer and the **ornament budget**: decoration scales with
    the rarity of the *moment* (menu, boss frame, spellbook, Legendary drop), never with the
    importance of the widget. Rows, toggles and objectives get none, forever.
- [x] **37.5B — De-drift, then the HUD** `[F]` ✅
  - ⚠️ **The plan's "~104 drift sites" was a bad count** and the correction is the useful part.
    Most `new Color(1f, 1f, 1f, a)` hits are the **alpha-fade idiom** — the ordinary way to fade
    a Godot control — not palette literals; rewriting them into tokens would have been strictly
    worse. Many more were **3D world colours** (`SkyController`, `ImpactEffect`, the factories)
    which answer to ART_STYLE, not to UI tokens. Genuine UI drift was **~12 sites**. Grep counts
    are not audits.
  - Two real defects surfaced from those 12:
    - **Seven hand-rolled scrims** across the shell at four different values, and **six were
      blue-black** (`0.02, 0.02, 0.04`) — the one thing UI_STYLE §1 rule 1 forbids. Now
      `UiTheme.Scrim(opacity)`, warm charcoal, one knob.
    - **Three off-scale font sizes**: 40 (combat shout), 28 (menu title), 15 (dialogue body).
      The 40 earned a real token (`ShoutFontSize`); the other two collapsed onto the scale.
  - ⚠️ **Fixed a bug 37.5A introduced.** 37.5A added `UiTheme.SchoolColor` with its own values
    without checking that `SpellSchools.Color` had existed since Phase 12 and drove every
    projectile, flash and particle. Two authorities, different values. `SpellSchools` is now the
    single one and `UiTheme` delegates — the same rule 37.5A had *written down* for rarity and
    then broken for schools. `ReputationTiers.Color` was retuned and pinned in the same pass.
  - Split `BossFrame` (240 lines) and `Nameplate` (115) out of `GameHud` (975 → 812). Both own
    their own state; `BossFrame` also took its three event subscriptions and its update loop, so
    `GameHud` no longer forwards to an `UpdateBoss` it does not otherwise touch.
  - The boss frame is the one HUD element spending ornament: corner brass, the display face, and
    **phase pips** (current phase hot, cleared phases brass, unreached engraved) beside the
    phase line rather than instead of it.
  - `Nameplate` gained a **disposition spine** — hostile/neutral/friendly off the combat teams.
    The HUD had never shown it, which stopped being acceptable at Phase 34.5, when the Frostfang
    clans and the Ancient dragon made "is this thing hostile?" unanswerable from the model.
  - ⚠️ **Caught a 37.5A regression before it shipped:** status chips were built from
    `UiTheme.Panel()`, so after 37.5A each one carried a 2 px brass rule *and its own grain
    `ShaderMaterial`* — five effects meant five framed screens' worth of chrome for five words.
    They are `UiTheme.Chip` now. Watch for this pattern wherever a small widget reused `Panel()`
    as a generic box; 37.5C and 37.5E will hit it again.
  - Quest tracker: objective bars under any objective counting past one.
    ⚠️ **`QuestResource` has no main/side field.** A first pass tinted by "has a prerequisite",
    which is invented *and backwards* — a prerequisite chains a quest, it does not demote it.
    The tracked quest simply takes `QuestMain`. 37.5E needs the real distinction for the log's
    Main/Side split and will have to add the field.
- [x] **37.5C — Character sheet, inventory, equipment, storage, crafting** `[F]` ✅
  - ⚠️ **The game has no item icons and the plan assumed it did.** `ItemResource.Icon` has been
    on the resource since Phase 5, **0 of 26 authored items set it, and nothing in the codebase
    read it** — dead scaffolding. A literal icon grid would have been 26 empty boxes, strictly
    worse than the text list it replaced. Slots show a **category glyph** instead: silhouette
    says category, colour says rarity, frame width says tier. `ItemSlot` prefers a real `Icon`
    whenever one is authored, so the eventual art phase is a data drop with no code change.
    The glyphs are deliberately plain Geometric Shapes, not pictographs — a missing glyph is a
    .notdef box, and an inventory full of tofu is worse than a plain triangle.
  - Gear tab is now three columns (worn slots | backpack grid | detail pane) rather than one
    scrolling text list. The list could not express the two things the screen most needed to
    say — what an item *is* at a glance, and whether picking it up is an upgrade.
  - `ItemPresentation` holds the pure logic and **takes plain values, never `ItemInstance`**,
    because `ItemInstance` wraps a Godot `Resource` and the test project forbids those. That is
    the same trap `RarityFrame` hit in 37.5A; designing around it up front is why the comparison
    maths has 8 tests instead of none. A sign error there tells the player a downgrade is an
    upgrade and looks entirely reasonable on screen.
  - ⚠️ **`Compare` sums each side by stat before subtracting.** An item can carry one stat from
    its template bonus *and* from a rolled affix (+2 Power sword with a "+3 Power" prefix);
    comparing entry-by-entry reports two deltas for one stat and gets the sign wrong when they
    disagree. It also ignores `ModifierType` — everything in the game is flat today, and adding
    a flat +5 to a +5% would be arithmetic nonsense presented as fact. If percentage gear is
    ever authored, split the rows rather than summing.
  - ⚠️ **`FocusNeighbor*` needs the node in the tree.** The first pass wired grid neighbours
    inside the grid builder, whose result is parented only *after* it returns, so `GetPath()`
    threw on every cell every frame — and the grid still worked perfectly under a mouse. Caught
    by `--play`, not by the build or the tests. The pass now runs at the end of `BuildGear`.
  - ⚠️ **The detail pane re-checks that the selected item is still held.** The selection can be
    invalidated from outside the panel entirely (a stash transfer, a salvage, a quest turn-in),
    and there is no event for "the thing you had selected left your pack" — without the check
    the pane offers Use on a potion that is already gone. Matched by *reference*: two rolled
    items can share a template and a name while carrying different affixes.
  - Storage and crafting took the same slot/card vocabulary. Crafting ingredients are now chips
    reading `have/need` instead of indented "(have 2)" lines that made the player do the
    subtraction that decides whether they can craft at all.
    `StoragePanel.Transfer`'s stackable-vs-instance branch was left untouched, as planned.
- [x] **37.5C2 — The stat block, progression and perks** `[F]` ✅
  - ⚠️ **Added because the phase plan had a hole, found by the maintainer, not by me.** 37.5C's
    scope line said "character sheet" and I read that as the Gear tab; Progression and Perks were
    in no phase at all, and 37.5D–G are magic/map/quests/shell/accessibility. On the plan as
    written they would never have been touched.
  - ⚠️ **The game had never shown the player a single stat.** `InventoryPanel` had no
    `StatsComponent` reference, so Armor, Physical Power, Spell Power, Crit Chance, Move Speed and
    all six Phase 34E resistances existed on the player and appeared nowhere. This also left
    37.5C's comparison half-blind — it could say a sword was +6 Armor while the player had no way
    to discover what their Armor was. Adding the readout was **new feature work**, not a restyle.
  - **Defence stats show their derived mitigation**, not just the raw number: "Armor 8" is opaque,
    the curve is hyperbolic, and a player cannot infer either that it removes ~7% or that doubling
    it is not double the benefit. The percentage comes from `CombatMath.ArmorMultiplier` itself, so
    the screen cannot drift from the damage pipeline.
  - ⚠️ **Do not clamp the mitigation display.** A test asserting "reduction never reaches 100%"
    failed at `float.MaxValue/2`, where `100/(100+x)` underflows to 0 — but it underflows *inside
    `CombatMath`*, so combat would grant that immunity too. Clamping in the UI would make the
    character screen disagree with combat, which is the one thing this readout must never do. The
    test was narrowed to the reachable domain (six orders of magnitude above any rollable stat) and
    the real ceiling recorded. If a stat ever approaches ~1e9, fix the curve, not the label.
  - `StatsPresentationTests` includes a **coverage guard**: every non-resource `StatType` must be
    displayed somewhere. Adding a stat without deciding where it appears now fails the build rather
    than silently repeating the gap this sub-phase exists to close.
  - Progression is a level card (badge, XP meter, unspent points as chips — nothing shown when
    there is nothing to spend) over the stat sections. Perks are cards with **rank pips** beside
    the "2/3" caption, and a spine colour for finished / available / out of reach; a locked perk
    names *which* refusal applies, the same rule Phase 37's property prompts follow.

- [x] **37.5D — The Magic screen** `[F/C]` ✅
  - `SpellbookPanel` is its own `UiPanel` on a new `Spellbook` action. ⚠️ Bound to **`T`** for
    "tome", not the conventional `K` — `K` is already the dev reputation control in
    `GameBootstrap`'s raw `_Input`, and a player-facing action sharing it would fire both.
  - `InventoryPanel` drops to three tabs. The screen runs **cold**: ink-violet vellum, tarnished
    silver frame, glyph-blue light, and a much finer grain tinted toward the glyph colour. It is
    the only surface in the game that is not ash and ember, and that contrast is the point — the
    player should know which screen they are on before reading a word.
  - It spends the whole ornament budget (rune circle behind the school ring, sigil field across
    the ground, shimmer on the title) and nothing else may.
  - **Two things the game had never shown, both now read from the authority combat uses:**
    - The **prepared cycle**. `Q` casts "the selected spell" and `F` cycles it, and no screen ever
      said what that order was or where in it you were — the HUD shows only the current name. A
      caster with six spells was cycling blind.
    - The **reactive combos** (`SpellCombo.ForSchool`). Shatter and Thermal Shock have been live
      since Phase 29.5D and were discoverable only by casting lightning into a chilled enemy and
      noticing the number was bigger. `SpellCombo.Rules` gained a public accessor rather than the
      screen keeping its own copy — a documented combo that does not fire is worse than an
      undocumented one, because the player builds around it.
  - ⚠️ **`ContentValidator` now gates the UI's fonts and shaders**, and the obvious version of
    that check is worthless: **`GD.Load<Shader>` returns a perfectly non-null `Shader` for source
    that does not parse at all.** Godot prints the compile error and hands back the resource, and
    there is no public "did this compile" API. Verified by feeding it a file containing "this is
    not glsl" — it loaded, passed a null check, and `--validate` said PASS.
    The seam that works is `GetShaderUniformList()`: a shader that failed to parse exposes none.
    Every UI shader declares several by design; if one ever legitimately has no uniforms, exempt
    it explicitly rather than weakening the check. Proven both ways — broken → `validate: FAIL`,
    restored → `PASS`.
    This matters because **three of the four UI shaders only instantiate when a screen is opened**,
    so a broken one shows up in no boot log, no `--play` run and no test — only in play, on one
    screen, as "nothing is there".
- [x] **37.5E — Map, quest log, dialogue, bestiary** `[F]` ✅
  - **The journal.** Added `QuestResource.IsMainQuest` — the field 37.5B refused to fake — and
    marked the warband chain (bounty → forge → remedies → heart) as the slice's main thread. The
    HUD tracker now reads it too, replacing the placeholder 37.5B left.
    ⚠️ **No Failed section**, and this is not an oversight: `QuestStatus` has exactly two members,
    Active and Completed. Nothing in the game can fail a quest, so the heading would be a
    permanently empty promise — the same call as the omitted Contracts and Exploration headings.
    Add it when the state exists, not before.
  - **The map.** Fast-travel nodes are plotted for the first time; they have carried a `Position`
    since Phase 25G and were only ever *listed*, so the map named places you could travel to
    without showing you where they were. Hierarchy is waypoints > regions > POIs, drawn weakest
    first so overlap reinforces it. Region labels are drawn on the plot (the view's "pure shapes,
    no font" note predates 37.5A shipping fonts at all), and the player is an arrow, not a dot —
    orientation is the one thing that makes a map usable *while walking*.
    ⚠️ **Filters do not re-fit the plot.** The bounds are computed over every known point whether
    filtered or not; a map that zooms when you hide a pin is a map you cannot read.
    ⚠️ **No quest markers**, because quests have no world position — Kill/Collect objectives name
    a template id, not a place. Inventing one would mean guessing where an enemy type lives.
  - **Dialogue** is the illuminated page: carved speaker, book-serif body, choices as engraved
    cards with a spine that marks a quest-starting or conversation-ending choice apart from
    ordinary talk. The whole card is the button, so the gamepad focus rect matches the click target.
  - **The bestiary** is a codex: a progress meter over cards that are sealed, part-written or
    complete. The three-stage staging has been in `BestiaryStage` since 34G and the old list spent
    it on three differently-worded text lines.
  - ⚠️ **I introduced duplicate locale keys and `--validate` caught them** (`bestiary.sighted` and
    `bestiary.unknown` already existed, with a *different format arity* — `Loc.TF` would have
    produced broken text). The guard lives in `LocaleAudit.Audit`, not in `ContentValidator`, which
    is why grepping the validator for "duplicate" finds nothing. Run `--validate` after touching
    `strings.csv`; it is not optional and it is faster than reading the file.
- [x] **37.5F — The shell** `[F]` ✅
  - ⚠️ **The `UiPanel` migration was dropped, deliberately, and the plan was wrong to assume it.**
    The premise was that these screens hand-roll what `UiPanel` provides. They do not: all five
    (`SettingsPanel`, `SaveSlotPanel`, `MainMenu`, `PauseMenu`, `CharacterCreator`) already call
    `UiState.Open` **and** `UiFocus.GrabFirst`. The only remaining gain was the open fade, against
    a lifecycle rewrite — they are create-per-use static factories, `UiPanel` is a persistent
    toggle — of **the only path into the game**, which no remote session can drive to test.
    Check what a screen already does before migrating it for what it supposedly lacks.
  - ⚠️ **The `Panel()`-as-generic-box trap, instances three and four.** Save-slot rows and toasts
    were both built from `PanelStyle()`, so since 37.5A a six-slot list rendered six brass frames
    with six grain `ShaderMaterial`s stacked vertically — competing with each other *and* with the
    panel containing them — and every four-second toast carried a full framed screen's chrome for
    one line of text. Both are `Card` now. **A small repeated widget takes a `Card` or a `Well`,
    never a `Panel`.** 37.5B caught the first (status chips) and predicted the rest; that
    prediction was right twice.
  - **Notification colour moved from the text to the spine.** Colouring the words meant a `Dim`
    autosave notice rendered as dim text on a dark chip — the least readable thing on screen,
    carrying information the player may well want. The spine says which kind of thing happened;
    the words stay `Text` and stay legible.
  - **Save slots became cards.** Region, level and corruption tier were one crammed string with
    five facts at equal weight; they are now a title, two chips and a caption, with the card's
    spine carrying corruption — the one thing about a save that is a *state* rather than a
    statistic, and what a returning player orients on. `slots.entry` was retired and the now
    orphaned key removed from the catalogue.
  - **The main menu is the highest-ornament screen in the game**: corner brass and the ink
    shimmer on the title, which it shares with the spellbook and nothing else.
  - ✅ **This is the one phase whose headline screen is genuinely verified.** `run_project` lands
    on the main menu, so the shimmer shader and the brass brackets were actually instantiated and
    rendered, not merely compiled — unlike every panel in 37.5C–E, which need a keypress to open.
- [x] **37.5G — Accessibility & responsiveness** `[F]` ✅ — **Phase 37.5 complete.**
  - `TextScale`, `ColorVision` and `HighContrast` on `Settings`. Persistence came free: they are
    `[Export]` fields on a Godot `Resource` and `SettingsService` saves the whole resource.
  - ⚠️ **`UiTheme.FontSize` floors at the 12 px caption size** whatever the setting says. That
    floor came from a real min-spec/Steam Deck audit, and a text-size control that can make text
    unreadable is not an accessibility feature.
  - **`ColorVision` daltonizes, it does not simulate.** Simulating would render the UI as a
    colourblind viewer sees it — a diagnostic view, and precisely the wrong thing to ship, since it
    makes things *less* distinguishable for the person who needs help.
    ⚠️ **Applied at the token layer, never in the builders**, because daltonization is **not
    idempotent** — adapting twice over-shifts. One layer only. The token layer also covers raw
    `ColorRect` pips that never touch a builder. Neutral tokens stay unadapted.
    ⚠️ **World art is never adapted.** `ItemPickupFactory`, `TrophyStandComponent`, spell
    projectiles and impact flashes read the same authorities *without* going through `UiTheme`, and
    that is deliberate — a fire spell that stops looking like fire is worse than a hard-to-read chip.
  - The tests assert the **property**: a confusable pair must be further apart *under simulation*
    after adaptation than before. Pinning matrix outputs would only prove nobody retyped them.
  - ⚠️ **Responsiveness: measure the viewport, never the window.** `GetViewportRect()` is already
    in logical pixels, so a Steam Deck at 1280×800 with UI scale 1.5 reports **853×533**. 37.5C and
    37.5D had both authored fixed columns against an assumed ~1900 px and overflowed by **321 px**
    and **167 px** in exactly that configuration. `UiTheme.ApplyScreenInset` / `UsableWidth` /
    `UsableHeight` are the seams; call them from `Rebuild` too, or a mid-session scale change keeps
    a stale gutter until restart.
  - ⚠️ **Height is the axis that bites on a handheld.** 533 logical px is short enough that a panel
    whose width fits comfortably still runs off the bottom — the map's fixed 500×320 plot and the
    crafting window's fixed 500 height both did.
  - ⚠️ **37.5C's claim that a fixed grid column count was needed for focus restore was wrong.**
    `UiFocus` walks child *indices*, and a grid's child order does not change when it wraps
    differently — only the visual rows do. The column count is derived from the viewport now.
  - Verified at 854×534, 1280×800, 1920×1080 and 3440×1440.

- [x] **37.5H — The sweep: settings, character creation, and everything missed** `[F]` ✅
  - Added after the maintainer asked for a once-over. A coverage audit — does each file use *any*
    of `Card`/`Chip`/`SectionRule`/`ApplyType`/`Title`/`Prose`/`Well`/`Divider`/`ItemSlot`? — found
    **13 files with zero usage**. Two of them were whole screens.
  - **`SettingsPanel`** was never rebuilt, only added to. Section headings were plain accent body
    labels — the same weight as the setting names beneath them, so thirty rows read as one
    undifferentiated column. They are engraved `SectionRule`s now, and the panel height is
    viewport-relative (the fixed 420 px scroll plus title and Back overflowed a 533 px viewport).
  - ⚠️ **Accessibility controls now carry an explanation line.** "Colour Vision" as a bare dropdown
    label asks the player to already know what deuteranopia is *and* what the game intends to do
    about it. A settings screen is exactly where that sentence belongs.
  - **`CharacterCreator`**: race picking is a card grid, not a dropdown. The dropdown made six races
    look like a settings value — pick blind, then read a paragraph that reflowed underneath to
    learn what you chose, and comparing two meant flipping back and forth from memory. This is the
    first decision the player makes and the only one they can never revise, so all six sit on
    screen with their stat trades as signed chips.
  - ⚠️ **The `Panel()`-as-generic-box trap, final tally: thirteen.** 37.5B named the pattern, fixed
    the status chips — **and left five `GameHud` widgets on `Panel()` in the same file it was
    editing**: vitals, time/weather, quest tracker, event banner, interaction prompt. All five are
    on screen simultaneously, so the HUD was rendering five brass frames, five engraved shadows and
    **five grain `ShaderMaterial`s** at once, more framing than the character screen uses. Also
    caught: `HotbarPanel`, `PartyWidget`, `TutorialHint`, and both `DebugHud` panels.
    **Naming a pattern is not the same as sweeping for it.** The sweep is what found the instances.
  - `UiTabs` gained an ember underline on the active tab. Colour alone made it a slightly brighter
    button in a row of buttons — a weak signal at the top of a screen whose whole job is saying
    where you are, and one that vanished entirely under a colourblind setting.
  - **Final state:** every remaining `UiTheme.Panel()` is a genuine full screen — main menu, pause,
    settings, save slots, character creator, loading, the `UiPanel` framework, and the two dev
    overlays. Verified by audit, not by memory.
  - **Return to Main Menu** (maintainer request). Until now the only way out of a session was
    closing the process. The pause menu clears `UiState`, moves the state out of `Paused` so
    `RefreshPause` settles on an unpaused tree, and reloads the scene deferred.
    ⚠️ **`--play` had to become a once-per-process latch.** `ShowMainMenu` runs again on every
    return, so the dev flag re-fired and dropped the player straight back into the save they had
    just left — which looks exactly like "return to main menu just reloads the world", and is only
    reachable from a `--play` launch, which is how it hid from a normal play-test.
    An explicit in-place teardown was written and then **not shipped**: the reload works, and
    swapping in ~100 lines of untested teardown across 25 world-owned nodes is a regression risk on
    a path no remote session can drive. Revisit only if the reload proves insufficient.
  - ⚠️ **Overlap bug, mine, twice: a `Button` is not a `Container`.** It never grows to fit its
    children, so anchored content inside one collapses onto itself. The race cards were declared
    `(160, 0)` — **zero height**, every label drawing over the next — and the spellbook's school
    rows were pinned to a hand-guessed 44 px that two lines overran as soon as the 37.5G text-scale
    setting moved off 1.0. `UiTheme.CardButton` is the fix: a `PanelContainer` (which *is* a
    container, and sizes to content) with a transparent button laid over it for input and focus.
    **Never put content inside a `Button`; use `CardButton`.**
  - Settings rows share one fixed right-hand control column. They were sized by their own content,
    so a long dropdown value pushed its label around while a checkbox left a gap — thirty rows of
    that reads as a ragged edge, and the widest dropdown squeezed its label until the two collided.
    The long colour-vision option names moved into the explanation line, where there is room.
  - Both screens now scroll and are viewport-relative; six race cards plus a summary and two fields
    do not fit the 533 px logical viewport, and neither did the settings list.
  - Not overhauled, deliberately: world VFX (`SpellFlash`, `ImpactEffect`, `WeaponTrailComponent`,
    `StatusEffectVfxComponent`, `TelegraphRing`, the placement ghost). Those answer to
    `ART_STYLE.md`, not to UI tokens, and recolouring them is a separate decision — the same line
    37.5G drew for colour-vision adaptation. There are **no floating damage numbers** in the game;
    `CombatFeedbackFx` is a screen-edge flash, and it was already on the tokens.

---

> ### Phase 37.5 retrospective — what the seven sub-phases actually found
>
> The overhaul's headline work was type, material and hierarchy. The **defects** it surfaced were
> mostly things that had shipped and stayed invisible for many phases:
>
> | Found | Live since |
> | --- | --- |
> | The character screen showed **no player stats at all** | always |
> | Item rarity used **stock saturated MMO colours**, against UI_STYLE §2 | Phase 7 |
> | Reactive spell **combos were documented nowhere** | Phase 29.5D |
> | Fast-travel nodes had a position and were **never plotted** | Phase 25G |
> | The HUD never showed **whether a target was hostile** | Phase 34.5 |
> | The **prepared-spell cycle order** was shown nowhere | Phase 12 |
> | Seven hand-rolled scrims, six of them **blue-black** against §1 rule 1 | various |
> | **Five HUD widgets** each carrying a brass frame and a grain shader, at once | Phase 37.5A |
>
> Three lessons worth carrying forward:
> 1. **Check what already exists before adding it.** 37.5A shipped a second school-colour ramp
>    beside one that had tinted every projectile since Phase 12. 37.5F nearly rewrote a lifecycle
>    to add a contract the screens already had.
> 2. **`Panel()` is not a generic box** — and naming a trap is not sweeping for it. 37.5B named
>    this one, fixed a single instance, and left five more in the very file it was editing. The
>    final count across the overhaul was **thirteen**, and only a systematic coverage audit in
>    37.5H found them. Grep for the pattern; do not trust the memory of having fixed it.
> 3. **A check that cannot fail is not a check.** The first shader validator passed a file
>    containing "this is not glsl"; the first grid-focus pass errored every frame *and worked
>    perfectly under a mouse*. Both were caught only by deliberately breaking them.

---
