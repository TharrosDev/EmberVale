# NOW — where the project is

**This is the single source of project state. Rewrite it; do not append to it.** History lives in
[`HISTORY.md`](HISTORY.md) and git; what is left is [`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md) §4.
Developer SDK: [`TOOLING.md`](TOOLING.md) (`python tools/embervale.py`). Every mechanic in the game,
one line each: [`MECHANICS.md`](MECHANICS.md).

## Where we are (2026-10-06, spell effects, spell wheel and camera)

The game is complete from New Game to credits. The finish run's contract and id registry are
[`playbook/finish.md`](playbook/finish.md).

### Spell effects, the spell wheel and the camera (`claude/magic-vfx`)

The newest work is three things asked for together: every spell made spectacular and scalable
across the graphics tiers, a spell wheel on the hold of `F`, and the first- and third-person
camera fixed. It was built in two waves of lanes (a foundation, then effects core, wheel, camera
and animation, audio and harnesses; then a look pass, two recipe lanes, HUD and spellbook, and
camera fixes), each with a review-fix pass. It changes no gameplay number, hit, aim, cooldown or
cost. It adds two save keys under the existing `spells` record, one settings field, one HUD
element and one tutorial step (all append-only), and it changes what the `cycle_spell` action
does. ⚠️ **The lanes wrote it without launching the engine. The orchestrator then rendered every
spell, the camera, the HUD and the panels through the harnesses, and the lanes fixed what the
frames showed. Nobody has played it, and nobody has seen an effect move.** A still frame proves
what one instant looks like; it proves nothing about motion, timing, sound or feel.

- **Spell effects.** One facade (`SpellVfx`), one session director (`SpellVfxDirector`), pooled
  blocks (flare, particle burst, lightning ribbon, shell, ground disc, ground mark, distortion,
  one screen flash), seven shaders in `assets/shaders/vfx` and code-built textures. All 30 spells
  have an authored recipe and special-case hooks: wind-up at the hand, release, travel, impact and
  what lingers, plus status auras. Five tiers (`Settings.SpellEffects`, following the graphics
  preset by default) differ in what a blast is built from, not only in particle counts.
  [`RENDERING.md`](RENDERING.md#spell-effects) has the table and
  [`ARCHITECTURE.md`](ARCHITECTURE.md#213-magic-srcmagic) the code map.
- **What the first renders taught.** The first pass was white-outs: stacked soft additive quads
  clip to white however faint each is. The coverage governor (`VfxCoverageRules`) came out of
  that, and so did building every blast from structure. Pale square patches round frost effects
  were ground-mark decals tinting the effect quads above them, which is why render layer 12 is
  reserved for effects and decals are masked off it. A muddy orange full-screen tint on spell
  crits was the combat hit flash, which spell hits now skip.
- **Spell wheel.** Hold `F` (pad `LB`, right stick): eight favourites on the inner ring, six
  schools on the outer, the hovered school's spells fanned out past the rim. A tap swaps back to
  the previous spell. The world keeps running; only look, lock-on, attack, block and cast are lent
  to the wheel (`PlayGate.WheelOpen`). The HUD spell row gained the spell's glyph, a wheel hint
  and a ghost of the tap target; the spellbook's prepared row became eight pin slots with Prepare
  and Pin on every known card. 25 code-drawn spell glyphs and six school emblems (`SpellGlyphs`).
- **Camera and animation.** The first-person "glitch" was the camera stepping at the physics rate
  under a view that turned per mouse event (`PlayerVisualSmoother`). The head in frame is cut out
  by the body shader with two spheres, and the eye is anchored to the resting head with a look-down
  reach. "Third person shows the character's front" was not the camera: the shared idle rested 40
  degrees off forward, the loops carried baked root travel, the blend points sat at speeds nobody
  moves at, and `MountComponent` yawed the body mesh half a turn on every load. The library is
  rebuilt with travel stripped, a calmed idle, squared and mirrored strafes; the gait blend is 2D
  and normalised by run speed (this reaches every Meshy humanoid); the mount's base yaw is fixed.
  The player's body, which had been drawn as bare metal, takes its atlas as emission again.
- **Spell audio.** 25 synthesised cues from `tools/gen_spell_sfx.py`, routed by `AudioDirector`
  through the pure `SpellAudio`, and a hard limiter on the SFX bus.
- **Harnesses.** `--spellshots`, `--camshots`, `--vfxperf`, `tools/facing_probe.gd`, HUD frames
  `01a` to `01f` and panel frames `29b` and `29c`; see Commands.

**Unverified (spell effects, wheel and camera).** The harness frames exist and were read; see
Verification. Everything below is what they cannot show.

- **No human has played it.** Not one cast, wheel pick or camera swap has been made by a person
  in a session.
- **The wheel's feel with a real mouse and a real pad.** 150 px of mouse travel to the rim at
  every HUD scale, the right stick's two thresholds, the stick at rest returning the cursor to the
  centre (which cancels), the 0.16 s tap window, the fan latching, and the presses-in-place-of-holds
  flow were all reasoned from code. No frame
  covers that variant, high contrast, or a HUD scale other than 1 for the wheel.
- **No spell sound has been heard by a person.** The 25 cues pass numeric checks only (length,
  peak, DC, fades, sub-bass share, tail decay, spectral centroid). The mix, the channel and zone
  rate limits, the riser ending on the release, the thunder threshold and the SFX limiter are
  rules under test, not sound anyone listened to. A Kindle detonation has no blast of its own.
- **The effects were judged from still frames only. There has been no motion review.** How a
  blast pops and decays, whether lightning flickers or strobes, whether rings and snow travel the
  right way, whether a trail reads at speed, the 0.25 s settle of a bolt from the hand, the eye
  smoothing at a sprint, the yaw lag, the casting arm's 0.18 s raise against a 0.25 s wind-up and
  the wheel's open and close: none has been watched.
- **Performance was measured in one scenario on one machine.** `--vfxperf`: eight casters looping
  the eight heaviest spells, Intel Iris Xe, 1280x720. Steady frame time p50 against the same
  staged scene before any cast:

  | Tier | Baseline p50 ms | Steady p50 ms |
  | --- | ---: | ---: |
  | Performance | 8.8 | 13.7 |
  | Medium | 8.6 | 18.7 |
  | Ultra | 12.7 | 18.9 |

  Medium and Ultra are over the 16.67 ms frame on this machine in that scenario. Nothing else was
  measured: not Low or High, not a single caster in ordinary play, not a crowd of afflicted enemies
  (status auras sit outside the live-effect budget), not first-cast hitch on a cold shader cache,
  not another GPU. `vfx_sprite` and `vfx_flow` declare a depth-texture sampler on every tier.
- **Steam Deck hardware is untested**, for the effects' cost and for the wheel at 853x533.
- **Tuned by eye from stills, or not at all.** Every size, lifetime, density and colour in the
  recipes; the governor's numbers, which assume a 70 degree vertical field of view at 16:9;
  whether Performance, with glow off, still reads hot; the live-effect budgets against richer
  blasts (several casters at once recycle effects mid-life); the first-person eye height and the
  eye-follow fractions; the strafe's arms after the chest was squared; the 1.12 aim shoulder.
- **Known limits left as they are.** Ground discs and floor rings are flat quads and clip into
  slopes. Stormbrand's sky bolt starts 8 m overhead and passes through a ceiling. The first-person
  casting point is fixed in the view, so a wall the player is hugging can hide it, and first
  person is detected by camera distance from the head, so a mounted or rolling caster falls back
  to the hand bone. The eye cut-out is capped at 0.20 m, so past roughly a 100 degree field of
  view the near plane's far corners are outside it. `vfx_ice` is not in the validator's shader
  list. The wheel's readout and legend overlap the hotbar at 1280x720 and below. An 18 px ghost
  disc may read as a coloured dot.
- **Not part of the run recorded here.** The master world bake check, the export build with
  `check_shipping_assembly.py` on the merged branch, the negative battery and world quality.

### The UI, HUD and meta-shell upgrade (`claude/ui-upgrade`)

Before it came a visual and mechanical upgrade of every screen and of the HUD ("banked
embers"): one foundation, eight lanes in two waves (settings and input, HUD core, items,
knowledge panels; then shell front, shell session, HUD combat, trade), each with a review-fix
pass, then three polish lanes driven by reading screenshots. It changes no save format. It adds
settings fields (append-only, each defaulting to the old behaviour), five input actions and one
gameplay rule: the difficulty setting now scales damage. ⚠️ **The lanes wrote it without launching
the engine. The orchestrator then ran the gates and rendered every screen through seven shot
harnesses, and nobody has played it.** A harness frame proves a screen draws in the state it
names; it proves nothing about input, feel, sound or balance. The map of the code is
[`UI_STYLE.md`](UI_STYLE.md) §13; one line per mechanic is in [`MECHANICS.md`](MECHANICS.md).

- **Foundation.** `UiFx` (fades, rise, stagger, pulse, the hold ring), `UiAudio` (nine cues, a
  title music bed, a low-pass while a menu pauses the world), `UiGlyph` (keycaps and drawn pad
  buttons in three families), a footer legend on every `UiPanel`, a hub strip over Character,
  Spellbook, Journal, Map and Bestiary, `UiSkin` for the engine-drawn controls, `UiTheme.Sheet`,
  the plate tokens, a bent text-scale curve, HUD options (`HudOptions`, `HudMetrics`) and item
  icons (`ItemIcons`, `tools/pack_ui_atlas.gd`). `UiTheme` is a partial class and each lane owns
  one `UiTheme.<Lane>.cs`.
- **Shell.** A boot splash, first-run accessibility setup, a title on a painting that follows the
  newest save's act, Continue naming its save, a quit confirm, a scrolling credits screen, a
  loading screen with the realm's painting, a progress line (`LoadingProgressEvent`) and a tip or
  lore card, richer save rows with hold to delete and overwrite, a pause sheet with the tracked
  objective, a three-column creator with a turntable preview, a death screen, and ending cards
  over paintings with hold to skip.
- **Settings and input.** Six tabs with a description pane and previews, per-row restore, hold to
  reset, key and gamepad remapping for 31 actions (`InputBindingRules`,
  `GameInput.ApplyBindings`), pad look sensitivity per axis, HUD presets and per-element modes,
  HUD scale, opacity and safe zone, subtitle options, a readable-font switch and presses in place
  of holds.
- **Difficulty is real now.** Story, Normal and Hard scale the damage of a blow landing on the
  player by 0.6, 1 and 1.35 at the one place a blow is resolved (`CombatComponent.ReceiveDamage`
  through `SettingsService.IncomingDamageScale`). Damage over time, falls and reflected damage
  bypass that point and are not scaled; nothing else moves. Normal multiplies by exactly 1, so
  every gate and balance number measured there is unchanged.
- **HUD.** No boxes: bars and text sit keylined on the world, readers take a translucent plate
  with one lit edge. Vitals with notches, a damage chunk and a corruption row; a hotbar with item
  pictures, cooldown seconds and locked, unusable and run-out states; a three-line tracker; glyph
  prompts; widths from the layout width with a narrow layout down to 853; Dynamic and Minimal
  presets and a hold-to-recall key (`N`).
- **Combat HUD and notices.** A bare boss bar with a damage chunk, a target plate, up to eight
  pooled enemy plates, four damage-number modes, toasts that dwell by word count, collapse
  repeats and wait out a fight, and a subtitle layer for companion barks and boss intro lines.
- **Items and character.** Painted icons (79 archetypes on one atlas, chosen from the item id),
  new, worn, locked and junk marks, a detail card with one hero number and signed deltas, a
  side-by-side compare, an equipment column that filters the pack, and a perk tree with four node
  states, a lit route and d-pad movement along prerequisite lines.
- **Trade.** Vendor, crafting, storage, appraisal and the contract board on one page family:
  shared rows, an order bar, a price ledger, both purses, sell all junk behind a confirm, craft
  max, and compare everywhere.
- **Knowledge panels.** Journal tabs, drawn objective marks and Show on map; a darker map with a
  scale bar, a legend that filters pins, a pad cursor that snaps to pins and a fast-travel
  confirm; bestiary pages by how much is known; a lower-third dialogue window with a typewriter
  and option marks.
- **Art.** Thirteen paintings (four title, six loading, two endings, credits), the seal, the item
  atlas, nineteen pad glyph shapes and sixteen control icons. Provenance is
  `assets/ui/PROVENANCE.md`.

**Unverified (UI upgrade).** The gates and the shot harnesses ran; see Verification. Everything
below is what they cannot show.

- **No human has played it.** Not one screen has been opened by a person in a session, and no
  flow has been walked end to end: title to creator to world, death to Rise, a shop visit, a
  remap, a full conversation.
- **No gamepad was physically driven.** Every focus route was reasoned from code: hub and sub-tab
  stepping, the two-press accept into the inventory's detail pane, Left round to a trade order
  bar, wrapped action rows, the map cursor and its snap, right-stick scrolling in dialogue, the
  creator and trade details, hold rings releasing on focus loss. The pad glyph shapes for each
  family were never seen beside a real controller.
- **Remapping never met a real device.** Listening, trigger capture, the quiet window after a
  capture, the conflict prompt's focus trap and the retry while bindings are parked are unit-
  tested as rules and unobserved as input. Known hole: text entry does not park the hotbar
  actions, so a hotbar slot rebound to a letter fires while typing in a search field.
- **No audio cue has been heard.** All nine cues and the title bed are procedural. Coalescing,
  the focus tick rate, the hold tick's rising pitch and the menu low-pass are rules under test,
  not sound anyone listened to.
- **High contrast and reduced motion were not captured.** Both are implemented on every new
  surface and neither was rendered. High contrast turns bare HUD groups into padded plates, so
  the vitals, clock and party shift when it is toggled mid-session, and the minimap frame's inner
  edge is styled once and does not follow the toggle.
- **No HUD performance measurement.** The plan's before and after (a perf scenario and a HUD
  draw-call count) was not run. New per-frame or per-change costs nobody timed: enemy plates
  unprojected each frame while any is up, keylined text with a halo, icon keylines drawn as eight
  stamps, the creator preview rendering every frame while its idle loops.
- **Steam Deck hardware is untested.** 1280x800 at UI scale 1.5 is a window on a laptop, not the
  device. Known limits at that size: a HUD scale above 1 lays out narrower than 853 and the
  bottom bar can overflow, and a text scale above 1 widens the hotbar cells again. 3440x1440 was
  in the plan's matrix and was not shot.
- **Feel that only play can judge.** HUD ink weight (3 px keyline, 6 px halo) and plate opacity
  (0.82) over a bright sky; whether a 20 degree turn raises the Dynamic compass too often; the
  typewriter's 48 characters a second and its 300 ms grace; toast deferral in a long fight; the
  death screen's 1.5 s before its options; the small shift when a sub-screen opens over the
  drifting title painting; the Story and Hard multipliers, which were chosen and never balanced.
- **Left as they are, on purpose or for want of a frozen file.** Track, Show on map and compare
  borrow play actions (F / X, V / R3, Shift / L3) instead of dedicated menu actions. A
  keyboard-only player with no mouse cannot scroll the creator's race and background prose. Any
  launch with an argument after `--`, `--play` included, counts as unattended: no death screen
  and a tap skips narration; an isolated user folder also turns the dialogue typewriter off. The
  title reads its act from chapter flags, so a save that predates them shows act 1, and a save
  row names the region, not the chapter. First-run setup is skipped for good if the first boot is
  quit before it. Unseen bestiary entries show a padlock, not silhouette art. Enemy plates treat
  Always and Dynamic alike. The vitals and hotbar stay up behind the dialogue window. Two price
  lines and one map description keep an em dash because a negative test and a generator match
  their text.
- **Harness notes.** `PanelShots` waits for the window to reach the requested size before it
  shoots; why the window once opened oversized at 1280x800 was never root-caused. A polish lane
  read "1 event handler(s) survived application teardown" at the end of two harness logs and did
  not trace it.
- **Not part of the run recorded here.** The master world bake check, the export build with
  `check_shipping_assembly.py` on the merged branch, the negative battery and world quality.

### The performance pass (`claude/perf-integration`)

Before it came an optimization pass: six lanes (load, rendering, streaming, assets, CPU, HUD)
merged, then review fixes. It changes no gameplay rule and no save format; the only new features
are graphics settings. ⚠️ **It was written without launching the engine, and nothing in it has
been measured.** It compiles. No frame time, memory figure or load time below is claimed to have
improved: the numbers are **pending the single measurement run that follows this branch**, and
the before-numbers are in the Verification section.

The baseline said where the cost is not: the static world already rendered inside 16.67 ms almost
everywhere at Medium. So the pass went after video memory, per-frame CPU work, streaming hitches
and the missing low end, in that order of suspicion.

- **Graphics settings.** A **Performance** preset below Low (saved as `4`; the saved ints are
  append-only), Low rebalanced between it and Medium, and Medium, High and Ultra authoring none
  of the new fields. An Advanced Graphics section (render scale, upscaling, anti-aliasing, shadow
  quality, ambient occlusion, volumetric fog, glow) stores per-control overrides whose defaults
  mean "follow the preset". A fresh install picks its preset from the adapter, memory and thread
  count; a 40 FPS cap exists; an unpaced menu holds 60. [`RENDERING.md`](RENDERING.md) has the
  tier tables and what scales.
- **The tier reaches the world.** `WorldQualityScale` carries draw distance (the Far radius,
  scatter ranges and biome cull only), ground-cover density and the enemy shadow distance.
  Nothing reads it while it is 1 / 1 / uncut.
- **Video memory.** 52 model texture `.import` files were capped and compressed to a budget by
  asset class (`assets.py audit-weight`); the estimate went from 266 MB to 56 MB and is an
  estimate. Each realm now holds one copy of each scatter source mesh instead of one per cell,
  and a cell's `PackedScene` is released once instantiated.
- **Streaming.** The reconcile sweep runs on change, activation checks its deadline inside a
  cell, play frames either instantiate or activate, and the loading gate waits up to 10 s for the
  realm to settle behind the loading screen.
- **Per-frame CPU.** Idle components stop ticking (stats, statuses, combat, hit reaction, trail,
  telegraph, schedule), the animation mixer is stepped by its component with a 40 m level of
  detail, `EventBus.Publish` no longer copies or allocates, `Loc.T` caches hits, spell flashes are
  pooled, and input and animation paths stopped building a `StringName` per call.
- **HUD.** Labels are written when their value changes, closed panels and settled bars do not
  process, the compass and minimap repaint when what they show has moved, and the F4 overlay
  refreshes at 4 Hz with video memory, managed heap, allocation rate and collection counts.
- **Boot and build.** The boot-time content validator runs on headless boots only, and the Debug
  configuration (the one the editor binary always loads) now compiles optimized.

**Unverified (performance pass).** Everything, in the sense that matters for a performance pass.

- **No measurement.** Not one frame, hitch, megabyte or second was sampled after the baseline.
  Every "no longer", "stops" and "instead of" above is a statement about code read, not about
  time saved, and some of it may not show up in a frame time at all.
- **No engine gate, import or bake.** `--validate`, `--lifecycle`, `--story`, the probes, the
  full unit suite, the Godot import and the master bake were all left to the central run. Until
  the import and a **full** bake happen, the texture caps, the shadowless ground cover, the
  distant-tier LOD bias and the shared scatter meshes are not in the game, and
  `world_bake.py --check` reports the world stale (this pass edited `src/World/` and model
  `.import` files, which are shared bake inputs).
- **Nothing was rendered.** The Performance tier, the rebalanced Low, the Advanced Graphics
  controls, FSR, MSAA and TAA switched at runtime, and the capped textures at eye level have
  never been on a screen. Low was 55 m shadows and 0.25 particles and is now 60 m, two cascades
  and 0.35, so its captures will differ. Three changes also reach Medium, High and Ultra: capped
  and compressed textures, ground cover without shadows, and the distant scatter LOD bias.
  Reference captures predate all of it.
- **Auto-detect never met a real adapter.** `GraphicsAutoDetect` is unit-tested against adapter
  strings; what this laptop's driver actually reports is unobserved.
- **Tick sleeping was reviewed by reading.** A missed wake would look like a bar that stops
  regenerating, a status that never expires, a schedule that never leaves, or a telegraph that
  never draws, after a load in particular. Nobody has looked.
- **The manual animation mixer.** Every animated body is now stepped from
  `CharacterAnimationComponent._Process`. Hit windows, root motion, a paused tree, the 40 m
  switch and the action-clip gate are unobserved.
- **HUD on-change writes.** The failure mode is a stale label after a load, a locale change or a
  settings change (invariant 10). Unobserved, and panel-shot baselines may differ.
- **Loading.** The settle wait can add up to 10 s of loading screen and was never timed; whether
  alternating instantiate and activate frames lengthens a stream-in during play is unknown.
- **`EventBus`.** Reentrant subscribe and unsubscribe during a dispatch were reasoned through,
  not exercised; `--lifecycle` subscriber counts are the first real test.
- **Phase 57 certification was not run.** This pass is not it.

### The item, crafting and save upgrade

Before it came the item, crafting and save upgrade on `claude/ics-integration` (nine lanes on
one foundation, merged, then two review-fix passes). ⚠️ **It was written without launching the
engine.** It compiles and its generators are clean; the first five bullets describe what the code
does and "Unverified" below them says what nobody has seen happen. The performance baseline
(`artifacts/perf-baseline/README.md`) records the build, 4,695 unit tests, `--validate`,
`--lifecycle` and `--story` passing on `main` at `cc9735de` with this upgrade in it; play,
renders and balance remain as listed.

- **Items and equipment.** 373 items (`data/items`), 296 of them generated from
  `tools/items/catalogue.py` by `tools/gen_items.py`: six tiers of gear, one per realm, on one stat
  budget that `ItemBudget` (C#) and the generator both compute. Items carry an item level, a tier
  and a required level; instances carry workmanship, an upgrade level, the level they were rolled
  at, and lock and junk marks. Eight item sets and 22 unique effects over ten kinds are re-derived
  from what is worn (`EquipmentComponent.RefreshDerived`, `UniqueEffectsComponent`) and never
  saved. Two-handed weapons, lossless swaps, whole-stack ammunition that the bow spends, and five
  consumable effect kinds with shared cooldown groups all refuse before they take anything.
- **Inventory.** The player's plain materials live in an uncapped material bag beside the pack
  (`InventoryComponent.UseMaterialBag`, set only in `PlayerFactory`); every count, removal, sale,
  craft, appraisal and impound reads pack and bag together. Search, slot filter, sorts, split,
  partial moves, drop, junk and a session-only buyback shelf are in the panels.
- **Loot.** 52 loot tables: 21 family and shop tables, 24 generated tier pools and 7 Flamebearer
  chest tables. A drop is rolled for the realm it happens in at an item level near the looter's
  (`LootTiers`), with pity (`PityRules`, saved in `LootLedger`), 43 affixes that scale with item
  level, and signature affixes on Epic and Legendary rolls. Each Flamebearer leaves a persistent
  reward chest whose roll is seeded per save and whose signature legendary drops once per save; a
  chest keeps what it spilled across a save.
- **Crafting.** 82 recipes (67 generated by `tools/gen_recipes.py`), a crafting skill of ten ranks
  that gates recipe tiers, workmanship on crafted gear, bulk orders, recipe scrolls, one shared
  salvage plan, and reforging at a forge: reroll an affix, upgrade to +5, promote Uncommon to Rare
  or Rare to Epic. Every reforge fee is above the sale value it adds and `--validate` has an arm
  that proves it. Sockets were not built and stay struck.
- **Save.** Format 4: a SHA-256 checksum over `objects`, one backup generation per slot that a
  load falls back to, stable set-piece keys, and migrations from v1 under xUnit with golden
  fixtures. A quick slot, three manual slots and a three-slot autosave ring; `F5` always writes the
  quick slot and the pause menu's Save never guesses a slot. A boss fight and a conversation hold a
  save block. In windowed play writes and thumbnails run on `SaveWriteQueue`; gates and tooling
  write inline. Autosave waits for a quiet moment. The contract is
  [`SAVE_FORMAT.md`](SAVE_FORMAT.md).

**Unverified (item, crafting and save upgrade).** Honest list; do not read the bullets above as
tested behaviour.

- **Engine gates.** No lane ran `--validate`, `--lifecycle`, `--story`, the save-audit or
  save-reload probes, the import or the full unit suite. Their results belong in the Verification
  table below and are marked pending there. In particular the `ItemValidator` arms, the generated
  `.tres` files (typed arrays, headers without a uid) and the new player-side components'
  teardown have never executed.
- **Balance, all of it.** The stat budget and weapon damage curves, potion magnitudes, set bonuses,
  unique-effect numbers, drop weights, pity thresholds, affix growth (a flat affix is 3.45 times
  its authored value at item level 50), crafting rank thresholds, workmanship odds, the reforge
  prices and ingot counts, shop stock and the autosave timings are first-pass arithmetic. Reforge
  cannot be sold at a profit by construction; whether it is affordable or worth using is unknown.
  Own-bench crafting has no `--validate` price arm ([`DESIGN.md`](DESIGN.md) §6).
- **No human has played it.** Not the catalogue, a boss chest, a set, a unique effect in a fight,
  the bow with real arrows, a reforge, a commission, or a save and reload through the new browser.
- **Nothing was rendered.** The crafting window, the Materials tab, the vendor and storage panels,
  the slot browser and its badges, the save indicator, the hotbar cooldown sweep, loot toasts, the
  rarity beam, and the five generated weapon models (`wpn_axe_iron`, `wpn_mace_iron`,
  `wpn_spear_iron`, `wpn_staff_oak`, `wpn_greatsword_iron`: checked against the coordinate contract
  and in Blender renders only, never in a hand in the engine) have never been on a screen.
  Panel-shot baselines will differ. ⚠️ The world bake manifest is stale since those models landed
  (model imports are shared bake inputs): run `python tools/world_bake.py --bake` once.
- **Input.** The pad hotbar chord shares `LT` with block; search fields swap input bindings while
  focused. Neither has been held in a hand.
- **Old saves.** The v3 to v4 step, the material bag migration and the widened trainer lesson are
  covered by unit fixtures or by reading, not by loading a real pre-upgrade save.
- **Threads and exit.** The off-thread image resize and encode, the main-thread completion post
  while paused, and the flush on window close are reviewed against the API, not observed.

The state of everything else, unchanged by that work:

- **Runtime audit.** Save capture now refuses partial snapshots; F9 and pause Load rebuild a fresh
  session with the saved character identity and region. Streaming follows live focus, owns transient
  actors and refuses unavailable prepared data or unsafe encounter placement. Magic checks damage
  callbacks against cast lifetime, including immediate caster respawn, and uses actor-aware cover
  queries. Shared entity/stat paths remove repeated managed allocation. Findings, measurements,
  regression coverage and remaining boundaries are in [`RUNTIME_AUDIT.md`](RUNTIME_AUDIT.md).
- **Magic.** The upgrade is integrated: 25 player spells and five enemy spells, committed cast
  wind-up/release/recovery, held channels, placed ground spells, breakable barriers, dash, school
  identities, controls/wards/DoTs, combos, learning and mastery. Death and live load cancel casts and
  transient deliveries; damage outcomes are captured before death handlers can respawn an actor.
  Prepared tome placements are included in the master world bake. The implementation and probe
  responsibilities are in [`playbook/magic.md`](playbook/magic.md); the current verification evidence
  is below. Human play-through and balance remain open.
- **Realms.** All five realms plus the Celestial Realm exist as generated regions in disjoint atlas
  bands ([`WORLD_ATLAS.md`](WORLD_ATLAS.md)): the Ember Crown (52 cells) and Frostfang Reach (36), and
  the compact finish-run realms — the **Ashen Wilds** (16: Last Hearth, the Ash Hunters' station, the
  Beast Lord), the **Sunspire Dominion** (20: Saffra Wells, the great library with Archivist Seren
  Adaru, the Crimson Mission, the Crimson Prophet), the **Pale Concord** (12, hidden until
  `flag.pale_concord_revealed`: Vesperhold's undying residents, the Hollow Court, the Hollow Queen) and
  the **Celestial Realm** (9: the Knight's Gate, the Ash Throne, the Ashen Knight and Morthul). The
  Ember Crown has one door per neighbour; the Sunspire library forecourt holds the story doors to the
  Pale Concord and the Celestial Realm, each gated by its destination's `UnlockFlagId`.
- **Flamebearers.** All seven are data (`data/bosses`, `data/enemies`) summoned from
  `BossSummonComponent` braziers. Each defeat offers its ember (absorb = +25 corruption and
  `flag.<boss>_absorbed`) and drops a relic; Morthul's defeat leads to the throne instead. All seven
  have their own adopted Meshy bodies (as does Archivist Seren Adaru); capsules were fitted at
  adoption. Storm Tyrant retargeting is corrected and its geometry was reviewed from six angles; the other
  new boss bodies still need eye-level review.
- **Visions.** Closing a Flamebearer's absorb conversation (either choice) plays three narration cards
  of how that champion fell (`VisionSequence`, keys `vision.<name>.1..3`), once per save
  (`flag.vision.<name>`). Morthul has none; the throne choice follows him.
- **Backgrounds.** New Game offers eight soft-nudge backgrounds (`data/backgrounds`, `BackgroundApplier`;
  Wayfarer is the no-op default) in the creator: a free perk rank (one of the original six perks until the
  perk catalogue lands), a kit capped at 80 gold, a flag read by an Elder/Innkeeper/Smith dialogue line, a
  standing tweak and at most +1 on a stat. `--lifecycle` plays a non-default race and background through
  New Game and Load. Human play-through and balance remain open.
- **Appearance.** The creator offers skin, hair, eye, ember-glow and build swatches per race (`data/appearance`,
  `tools/gen_appearance.py`) with a live preview. `player_body.gdshader` tints regions of the one baked atlas
  through `chr_player_base_mask.png` (`tools/gen_player_mask.py`; re-run and LOOK at `--preview` if the model or
  texture changes); `PlayerAppearance.Apply` is shared by `PlayerFactory` and the preview. The player body's
  glTF material is `Material_1`, so `CorruptionAppearanceController`'s old "skin" name test never matched it;
  it now drives the shader. Dev command `look [ids]`; `-- --shellshots` (creator) and `-- --look-shots` (in-world, tier 0 and corrupted) render it. Human play-through of every swatch is still open.
- **Story (campaign overhaul, 2026-10-04).** The main story is 30 missions in four acts, generated by
  `tools/gen_campaign.py` from `tools/campaign/specs/` (the spec manual is
  [`playbook/campaign-authoring.md`](playbook/campaign-authoring.md), the design, id registry and beat sheet
  [`playbook/campaign.md`](playbook/campaign.md)). **Act I** (Ember Crown): the opening is three cards and
  then play (`quest.main.smoke_over_the_square`, the Ashfall pass, the Crossway whispers, the Citadel
  approach) wrapped around the four legacy `quest.warband.*` quests, ending at the Iron King. **Act II**
  is three realm arcs in any order (Frostfang, Ashen Wilds, Sunspire: hook, investigation, fork, Flamebearer,
  aftermath; `quest.main.gathering` stays as a hidden ledger umbrella), each Flamebearer brazier gated by its
  arc (`flag.arc.*_ready`). The three kills reveal the hidden realm on screen, and the **Pale Concord**
  arc (`pale_door`, `vesperhold`, `queens_count`, `hidden`) ends with the Hollow Queen. **Act III** is a
  playable investigation at the Sunspire library (`sundering_pages`, `deep_stacks`, `truth`: the reading
  needs the codex and sets `flag.celestial_gate_open`). **Act IV** is five missions across the Celestial
  Realm (landing, Godfall and the Fallen Choir, the Sundered Stair, the Knight and Morthul, the throne).
  Six forks (`flag.fork.*`) carry consequences in-arc, at the Celestial landing (ally NPC clusters with
  once-only boons) and in the ending (one epilogue card per decided fork); the Ashen Knight is met ten
  times (`flag.rival.*`). Missions chain `flag.main.<slug>_done` -> next `AutoStartFlagId`; a held completion
  flag blocks auto-start; `CampaignCatchUp` maps legacy-save flags onto the new chain on load. The
  throne gate is unchanged (below 40 Dawnfire only, 60 or more Lord of Embers only, between either).
  Story rules (`data/story/rules`), companion barks (`data/story/reactions`), `SetPieceSpawnComponent`,
  dialogue start variants/second effects/new conditions and a rewritten quest UI (chapter journal, stage
  log, hints, rewards, updated dots, tracker, toasts, chapter banner, compass portal mode, spoiler-safe
  map pins, dialogue consequence tags) support it. The headless `--story` gate plays all 30 missions twice
  (Run A refuse every ember and take every first fork option, ending Dawnfire; Run B absorb every ember and
  take every second option, ending Lord of Embers) through the real quest, dialogue, flag and boss-death
  paths, plus six legacy-save fixtures (Run C), save/load checkpoints and the ending; boss fights themselves
  cannot be played headless. Not proved: a human play-through, boss/duel balance, eye-level review of the
  new props and NPCs.

- **Perk catalogue (progression upgrade, in progress).** `tools/gen_perks.py` is the one source of the 71 `data/perks/*.tres`
  and their `perk.<id>.name/.desc` rows (the `progression:perks` block of `strings.csv`); `python tools/gen_perks.py --check`
  is the drift gate. `PerksComponent` has free ranks, points spent, a gold respec (`RespecRules`), prerequisite and
  branch-point gates with `WhyNot`, and non-stat effect totals read through `PerkQuery` (caps in `PerkEffectMath`). Every
  `PerkEffectKind` has a hook and a perk: stamina costs at the dodge, block, parry, attack, bow-draw and sprint sites, mana
  cost, school power and spell crit in `SpellcastingComponent`, haggle/buy/sell/service in the shop and service code,
  XP in `AddXp`, material saving in `CraftingComponent`, salvage yield and salvage XP (`CraftXpMult`) there too, loot
  quality in `LootComponent`, arrow damage (`RangedPowerBonus`) in `RangedAttack.Fire`, and standing gains (`RepGainMult`)
  in `ReputationComponent.Add` (gains only). `ContentValidator.ValidatePerks` and `ValidatePerkCatalogue` check the tree
  gates, capstones, branch totals (34-46 points each), reachability within `PerkCatalogue.SkillPointSupply` (54) and that
  every kind is used; `PerkCatalogueTests` re-checks them from the `.tres` text and proves the caps and the shop margins hold
  with the whole catalogue taken. The dodge cost folds in the Dex derivation and the spell and channel mana cost the Int derivation,
  under one floor with the perks. The Perks tab is a tree UI (`PerkTreePanel`; `--panelshots` frames 21-25 photograph it empty, mid-build, corruption-gated and at the respec
  confirm). Milestone levels (10/20/30/40/50) each pay a bonus skill point, saved as `ms`, so 54 are earned. Not done: playtest
  tuning of the numbers.

- **Guilds.** All five guilds reach rank three and a finale (Phase 42 closed 2026-09-28). Each arc is
  two quests off the leader's `member` branch (rank two, then a finale whose three-way verdict is
  given at the hand-in and recorded as a `flag.<guild>.*` flag): Ash Hunters `quest.ash_hunters.
  scar_count`/`three_dragons`, Veiled Archive `greater_house`/`breach_record`, Iron Syndicate
  `quest.iron.caravan_road`/`last_contract`, Emberbound `second_fire`/`reckoning`. Realm NPCs react
  to rank three. Guild content is never read by `quest.main.*`.
- **Rival duels (47.5) and the world after the ending (44.5).** A black-iron brazier in the
  Stormcrown arena (after the Storm Tyrant) and one in the Crimson Mission (after the Prophet) summon
  the Ashen Knight under `BossSummonComponent.FightId` = `boss.ashen_knight_duel1`/`2`: he yields at
  `BossResource.WithdrawHealthFraction` (65% / 45%), the fight ends on `BossWithdrewEvent` (no kill,
  loot or bestiary credit), `flag.rival.duel1_won`/`duel2_won` is set and his parting words play.
  Both are optional, close for good on `flag.ashen_knight_defeated` (`ClosedFlagId`), and his Act IV
  last words gain a line for each duel won. After an ending the weather pool is filtered by story
  flags (`WeatherResource.RequiredFlagId`/`ExcludedByFlagIds`, derived and never saved): Dawnfire
  brings `weather.dawnfire` and strikes rain, storm and fog; the Lord of Embers leaves only the red
  `weather.embers` (`SkyTint`). The Elder, Last Hearth's headwoman and the Saffra Wells wellkeeper
  have after-lines per ending. Baked (`c8b60c0`); still needs an eye-level look at both skies and
  both braziers, and duel balance is untuned.
- **Windows export.** `export_presets.cfg` ("Windows Desktop") is tracked and builds
  `build/windows/Embervale.exe` + `.pck` (gitignored). The locale CSV imports with `importer="keep"`
  so `Loc` reads the raw catalogue in an export; `include_filter` ships `assets/models/manifest.json`
  and `data/world_bake/manifest.json`; the boot-time `ContentValidator` runs only in debug builds.
- **Struck, and staying struck:** Phase 40 (survival/needs) and 40.5 (puzzles/traps). **Out of scope**
  (personal build, never published): storefront, platform compliance, launch, live ops, extra locales.

**Open:** a played session of the UI upgrade with a keyboard and a real gamepad, then its
high-contrast, reduced-motion and HUD-cost checks (its Unverified list above);
the measurement run, import, full bake and gates for the performance pass, then a look
at every tier on a screen (its Unverified list above); first play of the item, crafting and save
upgrade (its Unverified list); the maintainer play-through (G1/G3) of the exported build, New Game to credits and both
endings; eye-level visual review of the new realms, bosses, duel braziers and ending skies; a reviewed
world visual re-baseline (the baseline predates the 2026-09 rebuild); the new realms are not yet in
the per-region traversal, mesh census and screenshot probes (scene audit now covers all six);
balance tuning of the new
bosses and the duels.

**Operating lessons (this 14 GB machine):** never run world bakes in parallel — merge first, then
one master `world_bake.py --bake`; run heavy gates (validate, story, lifecycle, bake, export) one at
a time. Asset lessons (Meshy, `adopt`) are in [`3D_ASSETS.md`](3D_ASSETS.md#adopting-a-model).
Measure before optimizing and once after, centrally: the baseline showed the static scene was
already inside budget, which redirected the whole pass, and six lanes each running a probe would
have measured each other. On this iGPU video memory is system memory, so texture and mesh
residency is a frame-time concern, not only a capacity one.

**Known warnings, diagnosed and not fixed (2026-09-25):** navigation edge-sync warnings and
ObjectDB-leak warnings on a rendered exit. Neither fails a gate. The world visual gate is advisory:
its result depends on the renderer, so judge frames locally rather than on the CI runner.

## Live invariants

Numbers are stable references (other docs cite them); gaps are retired invariants.

1. **Gameplay state persists, and Load REPLACES live collections.** Save ids are stable primary
   keys; clear before restore, including every false/empty branch. A partial restore is a failed load.
2. **One surface owns each fact.** Never add a second ledger for something already owned elsewhere.
3. **A gate belongs at the choke point, not in the caller.**
4. **All player-facing text uses `Loc.T()` and a `strings.csv` key.**
5. **If the player can go there, map it in the same change.** A map location's position is the
   transform of its `MapLocationComponent` parent in a cell scene, never a resource coordinate.
6. **Render world changes at eye level, front and back, with people and furniture around them.**
   ⚠️ **And raycast the camera onto the real ground** — a literal 1.75 m eye height photographs the
   inside of a hill and the baseline passes (`world_shots.gd` and `GuildShots.OnGround` do it).
7. **Before authoring content of an existing kind, read an existing `.tres` header.**
8. **An authored numeric range fails silently at both ends.** Every new range needs a validator arm
   and a negative case in *each* direction.
9. **An event that fires conditionally is not automatically the event that means the thing.** Read
   the line that publishes it and what has to be true for it to fire.
10. **A cache key is a subscription.** Any newly drawn fact must be part of every cache/signature
    that renders it, not merely an event listener.
11. **A SEAM IS GENERATED, NOT ARITHMETIC.** `tools/gen_regions.py` refuses a region whose row bands
    do not tile its extent exactly; every seam route is authored ONCE as a world point.
12. **A layout constraint written as a coordinate outlives its reason.** Author dependencies as
    offsets and ids. A schedule's destinations are local to its actor's cell
    (`ScheduleResource.DestinationOf`) and a property names its cell (`PlacementCellId`); `--validate`
    fails a schedule that leaves its cell.
13. ⚠️ **TERRAIN MAKES GEOGRAPHY; PROPS ONLY DETAIL IT.** A shape that must exist is a
    `WorldLandformResource`. **The generator makes the ground the landforms sit on** — a region
    without a `WorldGenerationProfileResource` fails `--validate`. One generator, one profile per
    realm, never a fork.
14. ⚠️ **SOME OF THE WORLD MUST CONTAIN NOTHING.** Most cells of every realm carry road, weather,
    vegetation and landform and no gameplay beat. Do not fill them in.
15. ⚠️ **DEEP WATER IS NOT A TRAP BECAUSE IT IS DECLARED.** Declare a `WorldWaterResource` on the cell;
    a water mesh authored in a `.tscn` is invisible to the safety system.
16. ⚠️ **A SHALLOW HOLE IS A BUG; A DEEP ONE IS A HAZARD.** `--validate` sweeps the lattice as a
    directed graph and fails ground the player can walk into and not climb out of.
17. ⚠️ **A SPECIES DECLARES THE GROUND IT STANDS ON.** `MaxSlope` defaults to 0.7; `Clumping` is the
    companion rule, because even spacing is the most recognisable pattern there is.
18. ⚠️ **A GUILD IS A FACTION WITH RANKS, AND ITS FLAGS ARE DERIVED, NEVER AUTHORED.** `guild.<slug>.*`
    flags are built by `GuildRules` from the faction id; `GuildRules.Resolve` is the only reader and
    `StoryFlagsComponent` the only writer. Ranks are cumulative, **a gap does not promote**, `CanJoin`
    owns the rejoin policy. Dialogue uses `GuildRankAtLeast` / `GuildNotMember` / `GuildCanJoin`,
    never `HasFlag` with a `guild.*` string.
19. ⚠️ **AN HLOD TIER IS A SILHOUETTE CONTRACT.** The proxy is the same mesh at a fraction of the
    density; cones and boxes keep the mass and lose the outline.
20. ⚠️ **A CELL'S `agent_*` DIMS ARE DERIVED FROM ITS VOXEL GRID, NEVER COPIED.** Height and radius
    CEIL, `agent_max_climb` FLOORS; `--validate` fails an off-grid dim. Raised ground taller than the
    floored climb is player-only ground.
21. ⚠️ **A LEVELLED PAD IS USUALLY A ROAD, AND A BUILDING PUT ON ONE BLOCKS IT.** Author a new `Yard`
    beside the road; the traversal probe, not `--validate`, is what catches this.
22. ⚠️ **A BUILDING VARIANT CHANGES STRUCTURE, NOT JUST DRESSING** — footprint, floors, roof, access,
    wall family, porch/balcony or ruin state. A cosmetic prop swap is not a new building.
23. ⚠️ **AN AUTHORED TARGET HEIGHT IS AN OFFSET, NOT A WORLD Y.** `GroundArea.Elevation`, a
    `Landform.Height` with `Flatten` over 0.5, `WorldWaterResource.SurfaceY` and a region's
    `SpawnPoint.Y` are relative (`ElevationMode`, default `RelativeToBase`). Absolute is the rare case.
24. ⚠️ **GENERATED WATER WETS YOUR BOOTS; AUTHORED WATER DROWNS YOU.** Generated depth stays under
    `DrownDepth`, off roads and yards; anything deeper is a declared `WorldWaterResource`. `--validate`
    fails a pad, route end, spawn or portal under more than `WadeDepth`.
25. ⚠️ **A BUILDING IS RIGID; THE GROUND UNDER IT MUST BE LEVEL.** Conform samples only the node's own
    origin, so author a level `GroundArea` with a high `SurfaceBlend`; overlapping pads must agree. A
    building on a road cannot be levelled (road beats yard) — move it. `--worldgen` ranks structures
    and prints the pad to author; `--validate` fails one over 1 m.
26. ⚠️ **A SERVICE LIVES AS LONG AS THE NODE IT IS PARENTED UNDER.** Register with
    `ServiceScope.RegisterOwned(this, this)`; never write an `Unregister`. A freed service in a live
    scope is an `Invariant` violation.
27. ⚠️ **ANYTHING IN A `static` THAT DESCRIBES A SESSION MUST BE IN `ResetSessionStatics`.**
    `SessionResetTests` fails a static class with a `Clear`/`Reset`/`ClearAll` the list does not name.
28. ⚠️ **A PRODUCTION WORLD CELL IS BAKED, HASHED AND OWNED.** Specs and resources are inputs,
    `data/world_bake/` is output, `manifest.json` bridges them. Gameplay never silently regenerates a
    cell. Re-bake (`world_bake.py --bake`) after any change to a spec, generator input or cell scene.
29. ⚠️ **RESIDENCY IS NOT GAMEPLAY ACTIVATION.** Only Near owns actors, physics and navigation; a
    cell-owned actor goes with its cell unless deliberately promoted to the session.
30. ⚠️ **AN ACTOR ENTERS A WORLD POSITION ONLY AFTER REAL COLLISION IS READY** — through
    `SafePlacementService`, never a timer or a second spawn-correction path.
31. ⚠️ **A MODEL IS "UNREFERENCED" ONLY IF NOTHING COMPUTES ITS NAME.** `assets.py`'s
    `referenced_by_name` allowlist covers computed names; keep it narrow.
32. ⚠️ **ONE ITEM HAS ONE MODEL, ON THE BASE `ItemResource.WorldModelPath`.** Empty means the
    rarity-tinted primitive.
33. ⚠️ **A PROJECT C# `Resource` LOADED BY PATH IS HELD FOR THE PROCESS.** Use
    `ResidentResources.Load<T>`; `ResidentResourceTests` fails a bare `GD.Load`.
34. ⚠️ **THE PALE CONCORD'S NAME STAYS OUT OF PLAYER-VISIBLE TEXT UNTIL ITS REVEAL.** Ids may appear
    anywhere; locale values outside `pale.*` / `location.pale.*` and ungated map locations may not.
    `world_atlas.py --check` enforces it.
35. ⚠️ **A CUT SYSTEM LEAVES NO STUB.** No survival, durability, repair, encumbrance, socket, puzzle
    or trap code or pointers; do not propose them as a fix for anything.
36. ⚠️ **THE CATALOGUE DECIDES WHAT ITEMS EXIST, AND ITS IDS ARE ALREADY IN SAVES.** Generated items,
    sets, unique effects, recipes, affixes and tier pools come from `tools/items/catalogue.py`
    through `gen_items.py`, `gen_recipes.py` and `items/gen_loot.py`. Never hand-edit a marked file,
    never rename or reuse an id, and keep all three `--check` gates clean.
37. ⚠️ **ONE STAT BUDGET, WRITTEN TWICE.** `ItemBudget` in C# and `budget_points` / `weapon_damage` /
    `item_value` / `STAT_WEIGHT` in `gen_items.py` are the same numbers. Change both sides in one
    commit; `ItemCatalogueTests` and `--validate` compare them.
38. ⚠️ **WHAT GEAR GRANTS IS DERIVED FROM WHAT IS WORN, NEVER SAVED.** Set bonuses, gear and affix
    regeneration, active unique effects and every cooldown are rebuilt or cleared on load. Adding a
    save key for one of them creates a second owner of the fact (invariant 2).
39. ⚠️ **A ROLL THE PLAYER PAID FOR IS DERIVED FROM SAVED STATE.** Workmanship and material saving
    from the craft serial, a reforge from the reforge serial and the item, a reward chest from its
    id and the save's salt. A quickload replays the outcome instead of offering another pull. A
    number in a save stays under 2^53, because the JSON parser returns floats.
40. ⚠️ **AN ACTION THAT NEEDS ROOM IS REFUSED BEFORE IT TAKES ANYTHING.** Equip, unequip, craft,
    salvage, reforge and consume all check first and put back exactly what they took when a later
    step fails (`AddOrOverflow`). An item is never destroyed for nothing.
41. ⚠️ **"WHAT THE PLAYER HOLDS" IS PACK AND BAG.** Use `CountOf` / `Contains` / `RemoveItem` /
    `AllStacks`. `Stacks` is the slotted pack only, and a reader that walks it alone no longer sees
    the player's materials.
42. ⚠️ **A REFORGE COSTS MORE THAN IT ADDS.** Any new way to raise an item's value is priced through
    `ReforgeRules` and covered by its `Exploitable` arm in `ItemValidator.Crafting`.
43. ⚠️ **AN ACCEPTED SAVE IS NOT YET A SAVE ON DISK.** In windowed play `SaveGame` returns before the
    queue has written; the truth arrives as `GameSavedEvent` or `SaveFailedEvent`. Anything that
    reads a slot or leaves the session flushes `SaveWriteQueue` first. A live save block refuses
    every save, autosaves included.
44. ⚠️ **A `Load` READS THROUGH `SaveRead` AND SKIPS WHAT IT CANNOT USE.** One throwing `Load` fails
    the whole slot; content the build no longer has is dropped with a warning, never thrown on.
    `SaveManager`'s method names and refusal strings are pinned by `world_quality_check.py`.
45. ⚠️ **A COMPONENT THAT DISABLES ITS TICK OWNS THE RE-ENABLE, ON EVERY PATH, `Load` INCLUDED.**
    `SetProcess(false)` is a promise that nothing can make a tick matter without calling the one
    wake method. A load replaces state without going through the setters, so `Load` wakes
    explicitly. The same holds for a `UiPanel` closed from code and for any HUD value keyed on
    what it shows: the key is dropped on a player, locale, settings or load change (invariant 10).
46. ⚠️ **A SAVED SETTING'S NUMBERS AND DEFAULTS ARE FROZEN.** `Settings.RenderQuality` is
    append-only (Performance is the lowest tier and is `4`); the menu orders tiers through
    `GraphicsMath.UiOrder`. An override's default is its "follow the preset" sentinel, and
    `ResourceSaver` omits a value equal to its default, so changing a default silently changes
    what every existing settings file means.
47. ⚠️ **AT 1 / 1 / UNCUT THE QUALITY SCALE DOES NOTHING, AND IT NEVER MOVES GAMEPLAY.** A
    `WorldQualityScale` consumer neither reads nor writes while the scale is at its default, and a
    new `RenderQualityResource` field defaults to what the game did before it existed, so Medium
    and up stay as authored. Draw distance scales the Far radius and visibility ranges only; Near,
    Mid and Backdrop decide collision, navigation and residency and are not a quality setting.
48. ⚠️ **AN ANIMATION MIXER'S CALLBACK MODE IS SET ONCE.** Trees and fallback players are manual
    for life and `CharacterAnimationComponent` steps them. Changing the mode on an active tree
    restarts its state machine (a corpse stands up), and an action clip is never stepped coarsely
    because it is that action's clock.
49. ⚠️ **A SCREEN THAT WAITS FOR A BUTTON IS NEVER SHOWN TO AN UNATTENDED RUN.** The splash,
    first-run setup, the death screen and the narration hold are gated on
    `ShellFrontRules.Attended` / `ShellSessionRules.Unattended` (headless, `EMBERVALE_USER_DIR`, or
    any argument after `--`). A narration timeline still finishes with no input, and a new such
    screen takes the same gate or it stalls `--story`, `--lifecycle` and every harness.
50. ⚠️ **A HUD ELEMENT HAS TWO GATES AND ONE OWNER PER FLAG.** `HudVisibility` says what the HUD
    mode allows and `HudDynamicRules` what the player asked for. A widget that writes its own
    `Visible` reads `GameHud.Shows(element)` at that write, calls `GameHud.MarkChanged` when its
    content changes, and lives in a `HudLayout` slot or it will not scale. `HudElement` and
    `HudElementMode` numbers are saved and append-only, like every new settings field (46).
51. ⚠️ **A PROMPT IS A SNAPSHOT OF A BINDING THE PLAYER CAN CHANGE.** Draw a key hint with
    `UiGlyph.For(action)`, never a literal key, and redraw it on `InputDeviceChangedEvent` and
    `InputBindingsChangedEvent`. A new action that should be rebindable is added to
    `InputBindingRules.Actions`; one that must stay fixed is left out on purpose.
52. ⚠️ **DIFFICULTY SCALES ONE NUMBER AT ONE PLACE, AND NORMAL IS EXACTLY 1.** Blows on the player,
    in `CombatComponent.ReceiveDamage`. Scaling anywhere else (inside `ApplyDamage`, per attacker)
    desyncs the kill and damage result computed before it, and a Normal that is not bit-for-bit 1
    moves every measured balance number.
53. ⚠️ **A SPELL EFFECT IS A CHILD OF `VfxRoot` AND OF NOTHING ELSE.** Never of `Entity.Body`, a
    projectile or a placed spell node. An effect that follows something copies its position each
    frame behind `IsInstanceValid`. Effects go through the `SpellVfx` facade, age in `_Process`
    (no tweens, no scene-tree timers), are freed when a load begins, and never read back into a
    rule: a call may be dropped with nothing else changing, and headless nothing is built.
54. ⚠️ **A `Node`-DERIVED EFFECT CLASS IS `partial` AND LIVES IN A FILE OF ITS OWN NAME.**
    Otherwise Godot does not attach its script, its `_Process` never runs, and the effect is built
    and never ages. Nothing logs it.
55. ⚠️ **NO PUBLIC STATIC UNDER `src/Magic/Vfx` EXPOSES A PARAMETERLESS `Clear` OR `Reset`**
    beyond the two on the session reset list (`SpellVfx.Reset`, `VfxQuality.Reset`).
    `SessionResetTests` finds such a method by reflection and fails until it is listed and called
    (invariant 27); a helper that only tidies a table is `internal`.
56. ⚠️ **A POOLED EFFECT RESETS ITS TRANSFORM IN `Begin`.** `VfxEffect.Begin` sets it to identity,
    because a wall's emitter is turned with `OrientLike` and the next burst drawn from that pooled
    node sprayed the wall's way. A block that needs a facing sets it after the spawner call, never
    from an earlier life. The same goes for any state a pooled node carries: resources are built in
    the constructor and every per-use value is written on every use.
57. ⚠️ **RENDER LAYER 12 BELONGS TO SPELL EFFECTS, AND DECALS ARE MASKED OFF IT.** Every effect
    mesh is on `VfxMaterials.RenderLayer`; every ground-mark decal's cull mask is
    `VfxMaterials.DecalMask`. A decal tints every surface in its box, additive quads included.
    Put nothing else on the layer, give a new decal the mask, and a camera that sets a cull mask
    must include the layer.
58. ⚠️ **A SHARED CLIP IS FIXED AT THE LIBRARY BUILD, NOT IN THE COMPONENT.** Root travel is
    stripped, the idle is calmed and faced forward, and the strafe is squared and mirrored by
    `tools/build_meshy_anim_library.gd`; the committed `anim_meshy.res` is its output. The gait
    blend's axes are speed over the actor's own run speed, so its points never hold metres per
    second. A body that faces the wrong way or slides is measured with `facing_probe.gd` and
    `--camshots` before the camera is touched.

## Commands

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py [--check]      # data/regions/*.tres is GENERATED from region_spec_*.py
python tools/gen_main_story.py             # main-story quests + ArchivistTruth/AshThrone dialogue
python tools/gen_items.py [--check]        # items, sets, unique effects, shop gear from tools/items/catalogue.py
python tools/gen_recipes.py [--check]      # generated recipes, trainer list, commission margin (--report)
python tools/items/gen_loot.py [--check]   # affixes, tier pools, boss chest tables
python tools/world_bake.py --bake | --check   # --bake prepares only regions whose inputs changed;
                                               # --full forces all, --region <slug> forces one
python tools/world_atlas.py --check        # realm bands, crossings, hidden-realm secrecy, this doc table
godot --headless --path . -- --validate
godot --headless --path . -- --lifecycle   # session teardown gate
godot --headless --path . -- --story       # plays all 30 missions New Game to credits through the real systems: run A (Dawnfire), run B (Lord of Embers), run C (legacy-save fixtures); --story-only=A|B|C filters while debugging
godot --headless --path . -- --worldgen    # generator report: relief, pads, wet anchors
godot --headless --path . -- --state       # content census
godot --path . -- --play                   # newest save, straight into the world
godot --path . -- --capture                # player-facing build profile: no dev tools or sandbox props
python tools/negative_tests.py             # refuses a dirty data/ or scenes/ — commit first
godot --headless --path . --script res://tools/debug_pass_regressions.gd
godot --headless --path . --script res://tools/world_traversal_probe.gd
dotnet build Embervale.csproj -c ExportRelease && python tools/check_shipping_assembly.py
python -c "from pathlib import Path; Path('build/windows').mkdir(parents=True, exist_ok=True)"
godot --headless --recovery-mode --path . --export-release "Windows Desktop" build/windows/Embervale.exe
build/windows/Embervale.exe --headless -- --story   # smoke the export
godot --path . -- --shellshots | --metashots | --hudshots | --combat-shots | --panelshots | --uishots | --tradeshots
                                           # one at a time; each checks the state of every shot. EMBERVALE_RES=1280x800
                                           # EMBERVALE_SHOT_UISCALE=1.5 is the handheld view; EMBERVALE_USER_DIR,
                                           # EMBERVALE_SLOT and EMBERVALE_ARTIFACTS pin the save and the output
                                           # --hudshots includes 01a-01f (spell row and wheel states);
                                           # --panelshots includes 29b and 29c (spellbook pin row)
godot --path . -- --spellshots             # every spell cast for real: <spell>_<tp|fp|tpday>_<windup|release|impact|linger>.png.
                                           # Keep the window focused. EMBERVALE_SPELLSHOTS_FILTER=<ids or schools, comma list>
                                           # EMBERVALE_SPELLSHOTS_VIEW=tp|fp|both  EMBERVALE_SPELLSHOTS_TIER=performance|low|medium|high|ultra
                                           # EMBERVALE_SPELLSHOTS_REDUCED=1 (reduced motion)  EMBERVALE_SPELLSHOTS_HOUR=<hour, default 19.5>
                                           # EMBERVALE_SPELLSHOTS_BACKDROP=0 (no dark wall behind the targets).
                                           # Fails when a spell with a recipe drew nothing under VfxRoot
godot --path . -- --camshots               # both views at idle, walk, jog, sprint, strafes, backpedal, diagonal, charge, channel,
                                           # look down (also at FOV 110 and at a sprint) and up, plus two side views with the capsule.
                                           # Fails on a body drawn back to front, an eye in the chest or a casting hand out of frame
godot --path . -- --vfxperf                # eight casters looping the eight heaviest spells; writes vfxperf_<tier>.json.
                                           # EMBERVALE_VFXPERF_SECONDS=<default 20>  EMBERVALE_VFXPERF_VIEW=wide|tp|fp
                                           # tier and reduced motion from the two EMBERVALE_SPELLSHOTS_ variables above
godot --path . -- --guild-shots | --shrine-shots | --enemy-shots | --look-shots
godot --headless --path . --script res://tools/build_meshy_anim_library.gd   # rebuilds anim_meshy.res; read its detrend, idle, square and mirror lines
godot --headless --path . --script res://tools/anim_library_probe.gd      # loops stay on the spot, the idle faces forward, the mirror is a mirror
godot --headless --path . --script res://tools/locomotion_tree_probe.gd   # the 2D gait blend, the upper-body mask, the action clock
godot --headless --path . --script res://tools/facing_probe.gd            # chest faces the body and hips stay in the capsule at every gait (gate "facing")
godot --headless --path . --script res://tools/camera_probe.gd            # wall spring, the first-person eye and its two cut-outs
python tools/gen_spell_sfx.py [--check | --keep-wav DIR]   # the 25 spell cues and manifest.json; needs numpy and ffmpeg on PATH
godot --headless --path . --script res://tools/magic_learning_probe.gd    # also builds the spellbook and HUD chips
godot --headless --path . --script res://tools/combat_feedback_probe.gd   # hit stop, feedback, lock-on, telegraphs
godot --headless --path . --script res://tools/pack_ui_atlas.gd   # item icon atlas from assets/ui/icons/items/src
godot --path . --script res://tools/world_shots.gd   # add -- --update-world-baseline AFTER inspecting
python tools/world_quality_check.py --mode fast | engine | visual | full
python tools/assets.py status | validate | adopt <src> <dest> | audit
python tools/assets.py audit-weight [--check | --fix | -v]   # texture video memory by class budget
godot --path . --script res://tools/world_perf_probe.gd [-- --json]   # per-cell render cost, all six realms
python tools/compose_building.py <name> <w> <d> <storeys> [--hollow | --open | --ruined]
python tools/gen_map_locations.py [--check]
python tools/gen_guild_dialogue.py <key> <dialogue.id> <faction.id> "<Speaker>"
```

Create the export directory in a fresh checkout: `build/windows` is ignored and is not cloned.
`--recovery-mode` disables editor plugins so a headless export does not wait for an unavailable
local editor MCP endpoint.

`godot` and `python` are not on the shell PATH. Godot is the 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`;
Python is Codex's bundled interpreter
(`C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`).
Export templates are in `%APPDATA%\Godot\export_templates\4.7.1.stable.mono`.

## Verification

### Spell effects, spell wheel and camera (`claude/magic-vfx`)

Run by the orchestrator, one Godot at a time, on 2026-10-06. This table records what was run and
looked at; where a result is not recorded here, **see the PR.**

| Check | Evidence |
| --- | --- |
| Build (`dotnet build Embervale.sln`) | warning-free |
| Unit suite (`dotnet test tests/Embervale.Tests`) | 6448 passing |
| Animation library rebuild (`build_meshy_anim_library.gd`) | run; the rebuilt `anim_meshy.res` is committed |
| `--spellshots`, Ultra, dusk | all 30 spells in both views; the frames were read as contact sheets |
| `--spellshots`, Performance (glow off) | all spells in third person; read as contact sheets |
| `--camshots` | run; read as contact sheets |
| `--hudshots` (with `01a` to `01f`), `--panelshots` (with `29b`, `29c`) | run; read as contact sheets |
| `--enemy-shots` | run, to look at the new idle and gait blend on the other humanoids |
| `--vfxperf` at Performance, Medium and Ultra | Iris Xe, 1280x720; steady p50 13.7 / 18.7 / 18.9 ms against baselines 8.8 / 8.6 / 12.7 |
| `--validate`, `--lifecycle`, `--story`, the magic, animation, facing and camera probes | not recorded here; see the PR |
| `--vfxperf` at Low and High; any other scene, machine or resolution | not run |
| The wheel with presses in place of holds, high contrast, reduced motion | not captured |
| World bake check, export build with the shipping check, negative battery, world quality | not part of this run |
| A human play-through, a real mouse and pad on the wheel, the spell audio, any motion review, Steam Deck hardware | not run; see Unverified above |

### UI, HUD and meta-shell upgrade (`claude/ui-upgrade`)

Run by the orchestrator on the merged branch, one Godot at a time, on 2026-10-06. This table
records what was run, not its numbers: **see the PR for the final numbers.**

| Check | Evidence |
| --- | --- |
| Build (`dotnet build Embervale.sln`) | warning-free |
| Unit suite (`dotnet test tests/Embervale.Tests`) | 5507 passing |
| `--validate` | run; see the PR |
| `--lifecycle` | run; see the PR |
| `--story` | run; see the PR |
| `magic_learning_probe`, `combat_feedback_probe` | run; see the PR |
| Shot harnesses at 1280x720 | `--shellshots`, `--metashots`, `--hudshots`, `--combat-shots`, `--panelshots`, `--uishots`, `--tradeshots`; see the PR |
| Shot harnesses at 1280x800 with UI scale 1.5 (853x533 logical) | the same seven; see the PR |
| 3440x1440, high contrast, reduced motion | not captured |
| Perf scenario and HUD draw-call count, before and after | not run |
| World bake check, export build with the shipping check, negative battery, world quality | not part of this run |
| A human play-through, a physical gamepad, the remapping flow on real devices, the audio cues, Steam Deck hardware | not run; see Unverified above |

### Performance pass (`claude/perf-integration`)

**Before**, captured once on `main` at `cc9735de` on 2026-10-06 (`artifacts/perf-baseline/`,
local): Intel Iris Xe, 14 GB, 1280x720, Forward+, Medium, `world_perf_probe.gd` with a static
camera at eye height in every cell. Render cost only: no AI, combat or HUD load.

| Region | Cells | Mean ms | Worst ms (cell) | Mean draws | Mean prims | Video mem MB | Configure ms |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| ember_crown | 52 | 11.7 | 18.5 (crossway_post) | 687 | 1,096k | 633 | 4117 |
| frostfang_reach | 36 | 7.9 | 9.4 (hold_pass) | 298 | 494k | 535 | 2182 |
| ashen_wilds | 16 | 8.2 | 11.5 (scorched_hollow) | 271 | 416k | 487 | 825 |
| sunspire | 20 | 8.4 | 16.1 (belt_east) | 197 | 401k | 589 | 1447 |
| pale_concord | 12 | 8.2 | 13.0 (west_mere) | 241 | 294k | 516 | 818 |
| celestial | 9 | 6.3 | 11.8 (shattered_rim) | 187 | 161k | 395 | 724 |

**After: pending.** The measurement run that follows this branch fills these in. The probe cannot
see most of what the pass changed (per-actor ticks, HUD work, allocations, streaming hitches in
motion), so an unchanged probe table would not mean the pass did nothing, and an improved one
would not prove play feels better. That needs F4 in a real session.

| Check | Evidence |
| --- | --- |
| Build (`dotnet build Embervale.sln`) | PASS at `c8ab7872` plus this documentation pass, 2026-10-06: 0 warnings, 0 errors (11.7 s) |
| Six-realm probe, after | pending |
| Texture estimate (`audit-weight`) | 266.0 MB to 55.6 MB as reported by the assets lane; an estimate from `.import` text, not a measurement |
| Godot import and full master bake | pending; required before any after-number means anything |
| Full unit suite | pending |
| `--validate`, `--lifecycle`, `--story` | pending |
| World quality (`fast` / `engine` / `performance`) | pending |
| Every tier rendered; a played session with F4 open | not run |
| Phase 57 certification | not run |

### Item, crafting and save upgrade (`claude/ics-integration`)

The engine rows are filled in by the single gates run that follows this branch's last commit. Until
then they are **pending**, and the 2026-10-03 table further down does not cover this work.

| Check | Evidence |
| --- | --- |
| `gen_items.py --check` | PASS at `0e0aa9db`, 2026-10-06: 296 items (205 equippable, 46 consumable, 5 material, 40 scroll), 8 sets, 22 unique effects, 0 files out of date |
| `gen_recipes.py --check` | PASS at `0e0aa9db`: 82 recipes (67 generated, 15 hand-authored), 34 commission pairs, 0 problems |
| `items/gen_loot.py --check` | PASS at `0e0aa9db`: clean, 88 files, 32 new affixes |
| Build (`dotnet build Embervale.sln`) | PASS at `0e0aa9db` plus this documentation pass, 2026-10-06: 0 warnings, 0 errors (16.5 s). The gates run repeats it |
| Full unit suite and SDK tests | pending |
| Import | pending |
| `--validate` | pending |
| `--lifecycle` | pending |
| `--story` | pending |
| Save-audit and save-reload probes | pending |
| World quality (`fast` / `engine`) | pending |
| Negative battery | pending |
| Rendered panels and a human play-through | not run; see Unverified above |

### Before the upgrade

Runtime audit, 2026-10-03, on `codex/debug-optimization-audit`. SDK evidence lives in
`artifacts/headless/<run-id>/summary.json`; those local artifacts include the exact commands,
per-gate results and diagnostics. [`RUNTIME_AUDIT.md`](RUNTIME_AUDIT.md#validation) records the
coverage and limits of the completed checks.

| Check | Completed evidence |
| --- | --- |
| Build and import | PASS — final build `20261003T161856-126dec4f1f` (20.8 s), zero warnings/errors; import `20261003T153012-937ea63ef4` (27.2 s), no diagnostics |
| Normal lifecycle and player checkpoint reload | PASS — three normal save/load round-trips and two capture-profile reload round-trips; zero orphan or invariant violations in each gate, `20261003T153108-fea7607efb` |
| Broad native engine coverage | 23 of 26 gates PASS in `20261003T145053-b2ecf2472d`; map, scenes and animation-library failed in that run and were repaired and rerun below |
| Map, equipment sockets and animation library | PASS — all three gates in `20261003T152041-bea54f8d3b`, no diagnostics |
| All-six-realm scene audit and its positive/negative rules | PASS — individual gates in `20261003T150336-922e8e5a37`; that aggregate run still failed its map gate |
| Asset validation | PASS — `20261003T151851-18dd3e14d2` (39.3 s) |
| World runtime audit | PASS — 41 checks in final run `20261003T162356-3cb6d72f7f`, including clear/blocked player landings, missing prepared data, streaming and transient ownership; no service-scope invariant errors |
| Final unit and SDK tests | PASS — 3,025 C# tests, zero failures/skips, and 34 Python tests; `20261003T161917-417422d182` (26.3 s) |
| Final master bake | PASS — 145 cells and six regions, 151 outputs; source `4e9963094eb6`; `20261003T154601-cd594ae2de` (1,774.5 s), existing navigation warnings only |
| Recursive resource and semantic content validation | PASS — `20261003T161944-da53ba5f70` (132.2 s), zero diagnostics |
| Component lookup measurement | PASS — 10,000 calls in `20261003T144851-ae0e3b55e8`: snapshot loop 2,252,864 managed bytes / 74.66 ms; indexed lookup 0 bytes / 3.59 ms. This measures one code path, not whole-game FPS |
| Final world quality and native audits | PASS — `20261003T162356-3cb6d72f7f` (39.7 s): save/runtime audits, 41 world checks, 24 magic lifetime cases, all-six-realm generation/quality and shipping exclusions |
| Storm Tyrant rendered captures | PASS — `20261003T162436-327fc3a6c7` (52.6 s), ten images generated; six geometry angles visually reviewed. Animation sample state was verified; those four images were generated but not all visually reviewed. Startup frame-budget warning remains |

Existing navigation-edge warnings remain as described above. Human New Game-to-credits play-through,
boss balance at real player levels, and eye-level review of the new realms and bosses remain open.
