# Embervale — Production Roadmap (Alpha → Beta → Launch)

> **What this is.** A prior **systems roadmap** built the *engine-on-top-of-Godot*
> — 21 phases of reusable, data-driven systems (Phases 1–21; see §0.5 for the
> list). That work is **done**, and it explicitly deferred "the actual game"
> (world, story, art, audio, balance, shell, ship polish) to a *separate
> content/production roadmap*.
> **This is that roadmap.** It takes Embervale from a near-empty sandbox that
> *can express* the game to a **launch-ready, shippable product**, then beyond.
>
> It is deliberately exhaustive. There is no cap on phases; each is sized to leave
> the repo **buildable and playable at every commit** (CLAUDE.md §1) and to
> round-trip through save/load before it is called done.
>
> **Working session by session?** The phases below are *milestones*, too large for
> a single Claude Code session. [`SESSION_PLAYBOOK.md`](SESSION_PLAYBOOK.md) breaks
> every phase (22–66) into lettered **sub-phases** (22A, 22B, …), each sized to fit
> one session with its own task list and "Done when" bar. Use the playbook as the
> day-to-day tracker; use this document for the milestone/gate view.

---

## 0. How to read this document

### 0.1 Two roadmaps, one game

| Roadmap | Scope | Status |
| ------- | ----- | ------ |
| Systems roadmap (Phases 1–21, §0.5) | **Systems**: capabilities the game runs on | ✅ Done (21 ⏳ ongoing seam) |
| **This document (Phases 22+)** | **Production**: the game itself, made shippable | ⏳ Active |

Phase numbering **continues** from the systems roadmap (next new phase is **22**)
so there is one unbroken history. Phase 21 (Content Expansion) is the *seam*: it
stays open as the umbrella under which early content lands, while the numbered
production phases below give that content structure, gates, and an end state.

### 0.2 The five gates (milestone definitions)

Production is organized into **stages**, each ending in a hard **gate** with exit
criteria. A stage is not "done" because its phases are checked off — it is done
when the gate criteria are independently verifiable in a build. (The human builds
and plays; this container cannot — CLAUDE.md §2. "Verified in a build" means the
maintainer confirmed it, not that we claim it.)

| Gate | Industry term | The one-line bar | "Feature-?" | "Content-?" |
| ---- | ------------- | ---------------- | ----------- | ----------- |
| **G0 First Playable** | Pre-production proof | One real region, one real boss, the corruption hook works end-to-end | Partial | Sliver |
| **G1 Vertical Slice** | The game in miniature | 30–60 min that looks/plays like the shipped game, ship-quality, one realm slice | Near-complete for the slice | Slice |
| **G2 Alpha** | Feature complete | **Every system and mechanic in the game exists and works**; content may be rough/incomplete | **Complete** | Incomplete |
| **G3 Beta** | Content complete | **All content is in**; main story playable start→finish→both endings; bugs/balance/polish remain | Complete | **Complete** |
| **G4 Release Candidate** | Ship-ready | Zero known crash/blocker bugs, certified on target platforms, gold-master quality | Locked | Locked |
| **G5 Launch** | Live | Shipped to players on target platforms | Locked | Locked |
| **G6 Live** | Post-launch | Patches, content drops, the long tail | Evolving | Evolving |

> **Why "Alpha = feature complete" matters.** The single biggest scheduling trap
> in RPG production is discovering a missing *system* during content authoring.
> The LORE demands several systems the systems roadmap never built — most
> critically the **Corruption System** (LORE calls it "the defining mechanic"),
> **Companions**, **Housing**, **playable Races**, **Dragons**, **Mounts/
> Travel**, and the **meta/shell**. Those are *features*, not content, so they are
> front-loaded into the Vertical Slice and Alpha stages and must all exist before
> G2. After G2 we only *make content and fix*, never *invent mechanics*.

### 0.3 Definition of Done (every production phase)

A production phase is done when **all** hold:

1. It builds; the repo is playable; no regressions in existing systems.
2. Any new stateful system implements `ISaveable` and round-trips save/load
   (CLAUDE.md §1 — persistence is not optional).
3. New content is **authored as `.tres` data** against existing systems wherever
   the recipes in CLAUDE.md §8 allow; new code is only for genuinely new
   mechanics.
4. Cross-references resolve under the `ContentValidator` (`validate` console
   command) — no dangling item/quest/dialogue/template ids.
5. `README.md` + this file are updated (mark phase, queue next); a **draft PR**
   into `main` is opened (CLAUDE.md §9).
6. New player-facing strings go through the **localization layer** — `Loc.T("key")`
   against `data/locale/strings.csv` (live as of Phase 24G) — **no hard-coded UI text**.

### 0.4 Content the systems already make free

Because the architecture is resource-driven, large swaths of the game are
**authoring, not engineering**. Per CLAUDE.md §8, each of these is "a `.tres`, no
code change": items, equipment, affixes, loot tables, perks, quests (Kill/
Collect), dialogue graphs, NPC schedules, weather, encounters, world events,
recipes, spells, status effects, factions. The production roadmap leans on this
hard: most *content* phases are data + a content pipeline, and only call out new
code where the LORE needs a mechanic the sandbox lacks.

### 0.5 The systems already built (Phases 1–21, ✅ done)

This roadmap stands on a completed systems foundation. Those 21 phases are
**done** (they round-trip through save/load and are live in the sandbox); the
production phases below assume them:

1 Core Architecture · 2 Player Controller · 3 Combat Framework · 4 Enemy AI ·
5 Inventory · 6 Equipment · 7 Loot Generation · 8 Progression · 9 Quest Framework ·
10 Dialogue · 11 NPC Schedules · 12 Magic · 13 World Systems (day/night, weather,
encounters) · 14 HUD & Panels Polish · 15 Crafting · 16 Faction Systems ·
17 Procedural Events · 18 Game UI Overhaul · 19 Optimization · 20 Deep Debugging ·
21 Content Expansion (⏳ the ongoing seam this roadmap structures).

For *how* those systems work, see [`ARCHITECTURE.md`](ARCHITECTURE.md); for the
authoring recipes that turn them into content with no new code, see CLAUDE.md §8.

---

## 1. The phase map (at a glance)

> Legend: **[F]** introduces new engine/feature code · **[C]** primarily content
> authoring · **[P]** production craft (art/audio/UX/perf/ship). Most phases are a
> blend; the tag marks the center of gravity.

### Stage A — Pre-production & First Playable → **G0**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 22 | Production Bible & Content Pipeline | F/P | Tooling, IDs, validation, content-authoring ergonomics, the "game design doc" of record |
| 23 | The Corruption System | F | The LORE's defining mechanic: corruption meter, thresholds, appearance/dialogue/ability shifts |
| 24 | Meta-Shell & Localization Spine | F | Title screen, settings, save-slot/new-game flow, options, the i18n string layer |
| 25 | Region Streaming & World Map | F | Stream large authored regions, fast-travel graph, the in-game map/compass |
| 25.5 | Stage A Hardening & Stabilization | F/P | Debug, optimize and harden everything built in 22–25 before new features stack on it |
| 26 | Playable Races & Character Creation | F | The six LORE races as data-driven trait sets + a creator |
| 27 | First Playable Region — Ember Crown (vertical core) | C/P | One real region authored end-to-end to prove the pipeline |
| 28 | First Boss — a Fallen Flamebearer (vertical core) | F/C | One full boss encounter (the Iron King slice) proving boss tooling |

### Stage B — Vertical Slice → **G1**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 29 | Combat Feel & Game Juice | F/P | Hit-stop, camera shake, animation canceling, i-frames, lock-on, feedback layers |
| 29.5 | Spellcraft & the Fading Weave | F | Magic made deep + original: cast archetypes, school identities, mastery, combos, the fading Weave, enemy casters |
| 30 | Animation, Models & Visual Identity | P | Rigged characters, weapon/spell VFX, the art direction made real |
| 30.5 | UI & HUD Overhaul | P/F | Unify + rebuild every UI surface to ship quality: design tokens, HUD, panels, motion, gamepad nav |
| 31 | Audio Foundations | F/P | Audio bus/mixer, music director, SFX, ambience, the `AudioDirector` |
| 32 | Companion System | F | Recruitable allies: follow/command AI, loyalty, abilities, party persistence |
| 33 | Vertical Slice Assembly & Onboarding | C/P | Stitch 22–32 into a ship-quality 30–60 min slice + the opening tutorial |

### Stage C — Alpha / Feature Complete → **G2**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 34 | Enemy & Creature Roster (bestiary framework) | F/C | The full archetype matrix: humanoids, beasts, undead, constructs, behaviors |
| 34.5 | Frostfang Clans & Beast-Race Factions | F/C | The LORE warrior clans/beast races as their own culture — a faction + questline, not just bestiary entries |
| 35 | Dragons | F/C | Aerial/ground dragon AI, breath attacks, Ancient/Wild/Ash variants — a tentpole feature |
| 36 | Boss Framework & Encounter Design | F | Phases, arenas, telegraphs, gimmicks — the reusable boss kit for all 6+1 |
| 37 | Housing & Player Property | F | Purchasable homes, storage, station placement, trophies, customization (LORE Housing) |
| 38 | Economy, Vendors & Services | F/C | Merchants, buy/sell, repair, training, banks, dynamic pricing, gold sinks |
| 39 | Mounts & Traversal | F | Mounts, stamina/sprint traversal, climbing/swimming as the world demands |
| 40 | Survival & Needs (scoped) | F | Only the systems LORE/design require (e.g. carry weight already exists) — keep or cut deliberately |
| 40.5 | Dungeon & Puzzle Framework | F | Reusable lever/trap/puzzle tooling for ruins/temples/vaults, plus the relic-trial vault convention |
| 41 | Quest Authoring at Scale & Branching | F/C | Beyond Kill/Collect: escort, defend, choice/branch, timed, faction-gated objective types |
| 41.5 | Divine Shrines & Blessings | F/C | The Seven Gods, mechanized — shrine blessings tied to each god's domain |
| 42 | Guild & Faction Questlines | C | The five LORE guilds as joinable factions with multi-quest arcs and ranks |
| 42.5 | The Crimson Cult | F/C | The Crimson Prophet's "empire of worshippers" as a real hostile/infiltrable faction |
| 43 | Cinematics & Scripted Sequences | F | In-engine cutscene tooling, camera tracks, scripted set-pieces, dialogue staging |
| 43.5 | Flamebearer Vision Sequences | F/C | A flashback-cutscene type showing how each fallen Flamebearer fell, triggered on their defeat |
| 44 | Alpha Content Pass — all four realms blocked out | C | Greybox + first-pass content for every realm, every fallen Flamebearer |
| 44.5 | World State: Realm Decay & Restoration | F | A per-realm decay/restoration tier so the world visibly reflects story progress and the ending choice |
| 45 | Alpha Hardening & Feature Freeze | F/P | Stabilize, profile, integration test the whole feature set; declare feature complete |

### Stage D — Beta / Content Complete → **G3**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 46 | The Main Story — Act I: Awakening | C | Full narrative content for Act I |
| 47 | The Main Story — Act II: Gathering the Flame | C | All four realms' main arcs + the six fallen Flamebearers |
| 47.5 | The Ashen Knight: Rival Duels | C | Recurring non-final duels across Acts II–III so the "greatest rival" has a real arc, not one Act IV reveal |
| 48 | The Main Story — Act III: Truth of the Gods | C | The mid-game turn, lore reveals, the Ash Throne |
| 49 | The Main Story — Act IV: The Celestial War + Endings | C | The Ashen Knight, Morthul, both endings (Dawnfire / Lord of Embers) |
| 50 | Side Content, Activities & World Density | C | Side quests, POIs, dungeons, world events, collectibles, ambient life |
| 50.5 | Lore Codex & Compendium | F/C | An in-game compendium of gods/Flamebearers/realms/guilds, populated by the Phase 50 lore-book collectibles |
| 51 | Itemization, Loot & Reward Economy Pass | C | The full item/affix/set/relic catalogue; the divine relics; reward curves |
| 51.5 | Enchanting & Relic Socketing | F/C | Optional itemization deepener — sockets + enchant consumables on rare+ gear |
| 52 | Full Audio & Music Production | P | The complete score, VO direction, full SFX/ambience coverage |
| 53 | Art Complete & World Beautification | P | Final art pass across all regions; lighting; set dressing; the dying-world identity |
| 53.5 | Photo Mode | P | Optional polish-tier feature — free camera, hide-HUD, dying-world filters |
| 54 | Accessibility & Input | F/P | Remapping, subtitles, colorblind, difficulty options, controller, Steam Deck |
| 55 | Content-Complete Integration & First Full Playthrough | C/P | The whole game start→finish, both endings, no placeholders |

### Stage E — Release Candidate → **G4**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 56 | Balance & Difficulty Tuning | C/P | Combat math, economy, XP curve, encounter pacing, the corruption pacing |
| 57 | Performance & Memory Cert | P | Frame budget on min-spec + Steam Deck; streaming hitches; load times; memory |
| 58 | Save/Load Hardening & Migration | F | Long-playthrough saves, migration, corruption recovery, slot integrity |
| 59 | Bug Triage, QA & Soak | P | Full test matrix, soak/longevity, telemetry, crash-free target |
| 60 | Localization Completion & Culturalization | C/P | Full string coverage, fonts/glyphs, LQA in shipped languages |
| 61 | Platform Compliance & Storefront | P | Steam/console cert (TRC/XR/lotcheck), achievements, store page, builds |
| 62 | Release Candidate & Gold Master | P | Lock, RC builds, final cert pass, day-one patch plan |

### Stage F — Launch → **G5**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 63 | Launch | P | Ship. Day-one patch ready. Live monitoring on. |

### Stage G — Live / Post-launch → **G6**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 64 | Launch Response & Stabilization | P | Hotfixes, crash/telemetry triage, community response |
| 65 | Post-Launch Content (the long tail) | C/F | New regions, New Game+, higher difficulties, content drops |
| 66 | Expansion / DLC Framework | F/C | The seam for paid expansions; entitlement/DLC loading |

---

## 2. Stage A — Pre-production & First Playable (→ G0)

**Goal of the stage:** prove the team can turn the sandbox into *the game*. Build
the missing load-bearing **features** the LORE demands, then author *one* real
region and *one* real boss to validate the entire content pipeline before scaling.

### Phase 22 — Production Bible & Content Pipeline `[F/P]`

The bridge from "systems" to "game." Make authoring content fast, safe, and
consistent before there is a lot of it.

- **Content design bible** — a `docs/DESIGN.md` that pins the *design* decisions
  the LORE leaves open: combat pillars (Skyrim breadth × Elden Ring weight, "no
  button mashing"), the moment-to-moment loop, progression intent (no class lock,
  player-authored builds), difficulty philosophy, the corruption fantasy, and the
  economy intent. This is the document content authors and balancers answer to.
- **ID & naming registry** — extend the existing central id constants (PR #31)
  into a documented namespace scheme for *every* content domain
  (`item.*`, `quest.*`, `npc.*`, `region.*`, `boss.*`, `faction.*`, `relic.*`,
  `dialogue.*`, `flag.*`). Authoring against typos is the #1 content-scale bug.
- **Content validation, leveled up** — grow `ContentValidator` from "references
  resolve" to "content is *well-formed*": quests reachable, dialogue graphs have
  no dead ends/orphan nodes, loot tables non-empty, every region has a spawn, no
  duplicate ids. Wire it into a `validate-all` console command and a headless
  check the maintainer can run.
- **Authoring ergonomics** — a `data/_templates/` set of canonical `.tres`
  starting points for each content type, plus a short "how to author X" appendix
  per domain (cross-linking CLAUDE.md §8 recipes). Optionally a tiny Godot
  `EditorPlugin` later, but **data + validation first**.
- **Telemetry/analytics spine (dev-only)** — lightweight event logging
  (`AnalyticsEvent`) routed through the EventBus so balance/QA later have data
  (deaths by location, quest funnels). Off in retail builds by default.

### Phase 23 — The Corruption System `[F]`

The LORE's **defining mechanic** — and it does not exist yet. This is the most
important *new system* in the entire production roadmap and gates the slice.

- **`CorruptionComponent`** (`ISaveable`, on the player) — a 0–100 corruption
  meter raised by absorbing fallen-Flamebearer power, dark choices, and certain
  abilities; nudged by story beats. Tiered thresholds (e.g. Untainted → Touched →
  Marked → Ashbound → Embers) each fire a `CorruptionTierChangedEvent`.
- **Consequences (the point)** — wire corruption into systems that already exist:
  - *Appearance* — a `CorruptionAppearanceController` swaps player materials/VFX
    (eye glow, ash veins) per tier; hooks the future model/animation work.
  - *Dialogue* — new `DialogueCondition`/`Effect` enums (`CorruptionAtLeast`,
    `CorruptionBelow`, `AddCorruption`) so conversations gate/branch on it
    (extends the existing declarative dialogue — DialogueEnums.cs).
  - *NPC reactions* — `ReputationComponent`/faction AI read corruption so the
    world fears a corrupted player (a global "dread" standing modifier).
  - *Abilities* — corrupted variants of spells/perks unlocked above a tier
    (authored as normal `SpellResource`/`PerkResource`, gated by corruption).
- **Both-endings hook** — corruption is the dial behind Dawnfire vs Lord of
  Embers; the system exposes the final-choice eligibility the endings read.
- **UI** — a corruption gauge in the character screen + subtle HUD vignette at
  high tiers (through `UiTheme`). Round-trips through save/load.

### Phase 24 — Meta-Shell & Localization Spine `[F]`

The "meta/shell" the systems roadmap explicitly excluded (ROADMAP scope note;
Phase 18 note). You cannot ship without it.

- **Title/main menu** — New Game, Continue, Load, Settings, Quit; runs as its own
  `GameState.MainMenu` scene before the world boots (GameManager already models
  the state).
- **Save-slot flow** — multiple named save slots with metadata (region, level,
  playtime, corruption tier, timestamp, screenshot), manual + autosave + quick
  save; built on `SaveManager` (extend from single-file to slot directories).
- **Settings** — graphics, audio buses, controls, gameplay, accessibility; a
  `Settings` resource persisted to `user://`, applied through a `SettingsService`.
- **Localization layer** — a `Loc` facade + Godot translation `.po`/CSV pipeline;
  **all** new UI/dialogue strings go through string keys from here on. This must
  land *before* mass content authoring or retrofitting strings becomes a tax.
- **New-game onboarding seam** — the hook character creation (26) and the opening
  (Act I, Phase 46) plug into.

### Phase 25 — Region Streaming & World Map `[F]`

The systems roadmap optimized a *single flat sandbox* and called true region
streaming out of scope (Phase 19 note). The four-realm world needs it.

- **`RegionResource` + `RegionStreamer`** — author regions/sub-cells as scenes;
  load/unload around the player by distance with a budget, hysteresis, and a
  loading screen for hard transitions (realm-to-realm). Persistent actors restore
  via the existing `PersistentSpawnDirector` (PR #29).
- **World map & compass** — a data-driven map (region metadata + discovered POIs)
  and an on-HUD compass/quest marker, through `UiTheme`/`GameHud`.
- **Fast-travel graph** — discoverable travel nodes, gated by discovery and
  (later) safety; respects the day/night clock and weather on arrival.
- **World partition discipline** — naming/placement conventions so authored
  regions (Phase 27, 44) drop into streaming cells without bespoke wiring.

### Phase 25.5 — Stage A Hardening & Stabilization `[F/P]`

A consolidation pass, **not new features.** Phases 22–25 added a lot of load-bearing
*systems* fast — the corruption system, the meta-shell/save-slots/settings,
localization, region streaming, cell persistence, the map/compass, fast travel.
Before Phase 26+ stacks races, a real region and a boss on top, this phase
**debugs, optimizes and hardens what already exists** so the foundation is solid.
It is the Stage-A analogue of Phase 45 (Alpha Hardening), scoped to the work built
so far. It covers **two bands**: the Stage A production work (Phases 22–25) *and* a
fresh regression/hardening pass over the foundational **systems 1–21** — building on,
not repeating, their earlier Phase 19 (optimization) and Phase 20 (deep-debugging)
passes, now that Stage A leans on them and the codebase has grown. (Phase 21 Content
Expansion stays the ongoing content seam, not hardened here.)

Concrete signal to chase: the sandbox boot/load logs already surface recurring
**save warnings** — transient actors emitting *"no PersistentId"*, and *"orphaned
state on load"* entries for stale component keys — exactly the kind of latent
rough edge this phase resolves.

- **Save/load integrity** — root-cause the recurring `PersistentId`/orphaned-state
  warnings; guarantee every new `ISaveable` (corruption, settings, map, fasttravel,
  cell-persistence, save slots) round-trips cleanly with zero spurious warnings.
- **Streaming & perf** — stress the `RegionStreamer`/`CellPersistenceDirector` under
  fast traversal and repeated load/unload + save/load; replace the fixed transition
  settle with a streamer-idle gate; profile load hitches.
- **System hardening** — edge-case passes on the corruption system, the meta-shell/
  settings/save-slot lifecycle, and the new UI surfaces (modal map, compass,
  vignette) including mouse-mode/`UiState` correctness across overlapping menus.
- **Gates & coverage** — grow `ContentValidator`/analytics to cover the Stage A
  content + id domains; finish with a full integration regression sweep and a
  recorded known-issues/perf-baseline ledger.
- **Foundation (systems 1–21)** — a clustered hardening pass over the core/entity/
  stats spine, player/combat, enemy AI, items/loot, progression/quests/dialogue,
  magic/status, world/schedules/events, crafting/factions, and the legacy UI panels.

The session-by-session breakdown — **25.5A–G** (Stage A work) and **25.5H–P**
(systems 1–21) — lives in `SESSION_PLAYBOOK.md`.

### Phase 26 — Playable Races & Character Creation `[F]`

LORE ships six playable races (Human, Valari, Grondar, Sylthari, Draekyn,
Umbral), each with distinct traits.

- **`RaceResource`** (`.tres`) — per-race base `AttributeSet` deltas, innate
  perks/abilities (e.g. Valari magic affinity, Grondar strength/endurance,
  Sylthari wildlife communion, Draekyn dragon ability, Umbral stealth), starting
  reputation tweaks, and appearance options. Auto-indexed `RaceDatabase`.
- **`CharacterCreator`** — the new-game screen: race, appearance, name, optional
  background; writes the chosen race into the player's components at spawn
  (`PlayerFactory` takes a creation profile). Persists in the save header.
- **Trait wiring** — race traits flow through existing systems (StatModifiers,
  seeded perks, faction standing), not a new inheritance chain (CLAUDE.md §1).
- **Magic affinity (woven, Phase 29.5)** — the Valari "natural affinity for magic"
  (and any racial school lean) wires into the 29.5 **mastery/Weave** system: a starting
  mastery nudge + a Weave-attunement trait, not a class lock (DESIGN §1.5). Data through
  `RaceResource`, no new system.

### Phase 27 — First Playable Region: Ember Crown (vertical core) `[C/P]`

Author **one real region** end-to-end — the human heartland hub — to prove the
content pipeline produces ship-quality space, not greybox.

- A walkable slice of the Ember Crown: a town hub (vendors, a guild presence, an
  inn, crafting stations, a housing plot), surrounding wilds with encounters and
  POIs, day/night + weather already alive (Phase 13). Populated with scheduled
  NPCs (Phase 11).
- First-pass-but-real environment art, navmesh, audio ambience, and lighting —
  the bar the rest of the world will match.
- Used as the **persistent test bed** for every later feature.

### Phase 28 — First Boss: a Fallen Flamebearer (vertical core) `[F/C]`

One full boss — a slice of the **Iron King** — to build and prove boss tooling
ahead of the full framework (Phase 36).

- A multi-phase fight (telegraphed attacks, an arena, a mechanic), a boss
  healthbar, a defeat→reward→corruption-gain beat (absorbing his fragment raises
  corruption — wiring 23 to the story), and a memorable music cue (placeholder).
- Establishes the "defeat a fallen Flamebearer" loop that Act II repeats six
  times.

> **🚩 Gate G0 — First Playable.** A new game → character creation → load into the
> Ember Crown → play the core loop (explore, fight, loot, quest, craft, talk) →
> reach and defeat the Iron King slice → gain corruption → save/load intact. The
> corruption mechanic visibly changes *something*. This proves the game is real.

---

## 3. Stage B — Vertical Slice (→ G1)

**Goal:** make 30–60 minutes that look and feel like the **shipped** game — the
trailer-worthy proof of the experience. Everything in the slice is ship-quality;
it is the bar all later content matches and the basis for any pitch/marketing.

### Phase 29 — Combat Feel & Game Juice `[F/P]`

The LORE's combat bar is explicit: *Skyrim breadth × Elden Ring weight, heavy
impact, precise timing, no button mashing.* The framework (Phase 3) has the math;
this gives it **feel**.

- Hit-stop/freeze frames, camera shake, directional hit reactions, weapon trails,
  impact VFX/SFX, screen feedback on crit/stagger/block/parry.
- **Parry/riposte & dodge i-frames**, animation canceling windows, input
  buffering, attack-commitment tuning — the timing depth.
- **Lock-on / soft target** built out from the existing `FocusedEntity` (Phase
  18) into a real target-lock with switching.
- Stamina/poise pacing tuned to discourage mashing (extends CombatComponent).

### Phase 29.5 — Spellcraft & the Fading Weave `[F]`

The systems roadmap built a *functional* magic system (Phase 12: projectile/area/self
spells across the `DamageType` schools, a mana economy, status effects) — but the
sandbox ships only a handful of generic elemental spells, no enemy casters, and no
mechanical identity. `DESIGN.md §1.5` **pins magic as a required build spine** ("every
magic school must be a viable spine to build around, none a trap"), yet nothing in the
roadmap deepened or expanded it. This phase does — and sits in the slice on purpose
(mirroring the 30.5 UI overhaul): magic must read as a *real, original* answer to an
encounter before the slice can claim to "look and play shipped," and **every new mechanic
must exist before the G2 feature freeze.** The slice ships ~one school deep; the breadth
(full catalogue, all-faction casters) threads through the woven sub-phases below.

**The original hook — the Weave.** Magic is the failing **Weave** of a dying world
(LORE: *"magic is fading,"* Nyth the magic-goddess dead, the Valari innately attuned).
You don't *buy* spells — you **recover lost spellcraft**, and corruption offers an easier,
darker path to power (extending the Phase 23H gate). This makes magic distinctively
Embervale's, not generic elemental fare, and binds it to the defining mechanic.

- **Cast archetypes** — a new `CastMode` (Instant · **Charged** hold-to-empower · **Channeled**
  sustained beam/drain at a mana-per-second cost) layered on top of the existing
  Projectile/Area/Self *shape* (`SpellResource.Delivery`). `SpellcastingComponent` grows
  charge/channel state; the player controller and enemy AI both drive it.
- **School identities** — each `DamageType` school plays *differently*, not just a tint +
  resistance: **Fire** ignite/DoT stacks · **Frost** chill→freeze control · **Lightning**
  burst + chain-to-nearby · **Arcane** utility (ward/blink/dispel/force) · **Nature**
  sustain (heal-over-time, thorns, a totem/summon) · **Necrotic** the corrupted line
  (lifesteal, decay), gated by corruption per 23H. Mostly authored data + one signature
  mechanic per school.
- **Spell scaling & school mastery** — spells scale off `SpellPower`/Intelligence
  (extends `CombatMath.RollSpell`); casting a school ranks a persistent **mastery track**
  that empowers and unlocks that school's spells (reuses the perk/progression patterns;
  `ISaveable`). Mastery is the "hard to master" magic ceiling, not just bigger numbers.
- **Reactive combos** — cross-school interactions read the target's status effects (Chill +
  Lightning = shatter; Burning + a Nature bloom = …) via a small `SpellCombo` resolver —
  the magic analogue of the combat read.
- **The fading Weave** — a light, dev-tunable **magic-potency** dial per region (ties to the
  dying-world identity and Phase 25 streaming): ambient magic is weak, altars/ley sites
  restore it, and lost/ancient spells must be *found*, not vendored. Corruption interplay:
  corrupted casting grows *easier* as the world dies — temptation made mechanical.
- **Enemy & NPC casters** — `EnemyAIComponent` gains a **casting behavior** (cast at range,
  kite to maintain distance, heal/buff allies) reusing `SpellcastingComponent` on enemies,
  plus a first caster archetype (a Valari-trained mage / cultist). The marquee "enemy magic"
  the sandbox entirely lacks today.
- **Magic UI + content tail `[C]`** — a spellbook/school view with charge/channel/mastery
  feedback (functional here, beautified in 30.5) and **one signature spell authored per
  school** for the slice (the full catalogue is Phase 51).

> **Why before G2.** Cast archetypes, school identities, mastery, combos, the Weave dial,
> and caster AI are all *mechanics*. After the G2 feature freeze we only author spells as
> `.tres` against these systems — so the systems must land now, in the slice, where they
> are proven as a viable build.

### Phase 30 — Animation, Models & Visual Identity `[P]`

The art direction made real for the slice cast (player, core enemies, key NPCs,
the boss).

- Player, core-enemy, key-NPC, and world/prop models authored in Blender via the
  Blender MCP for the slice cast and the Ember Crown dressing (Phase 27), ahead of
  rig/animation integration.
- Rigged/animated third-person player character (locomotion, attacks, block, hit,
  death) + weapons + spell casting, framed by the over-the-shoulder camera; enemy
  animation sets driving the existing AI/combat states.
- Spell/status VFX matched to `SpellSchools` tints; the dying-world material
  language (ash, faded color, embers) established as a style guide.
- Asset import/LOD conventions feeding the optimization work (Phase 19/57).

### Phase 30.5 — UI & HUD Overhaul `[P/F]`

The systems roadmap built a *functional* UI (Phase 14 polish, Phase 18 the "real game
UI") on a one-file `UiTheme`. Across Stage A–B the game grows many *individual* surfaces —
the corruption gauge/vignette (23), the meta-shell + settings (24), the world map + compass
(25), character creation (26), the boss healthbar (28), combat feedback + lock-on (29). This
phase, landing right after the **art direction** is set (30), takes all of them from
"functional and inconsistent" to **one cohesive, beautiful, ship-quality UI** — the UI half
of the G1 "looks shipped" bar. It is craft + a little new framework code, not new mechanics.

- **Design system, not a theme file** — grow `UiTheme` into real **design tokens** (palette,
  type scale, spacing, radius, elevation, motion durations/easing) with a `docs/UI_STYLE.md`
  the whole game answers to; the dying-world identity (ash, faded color, ember accents) made
  the UI language, matched to Phase 30.
- **HUD rebuilt** — a responsive, **scalable**, safe-area-aware HUD architecture; core widgets
  (vitals, prepared spell + cooldown, status effects, crosshair), wayfinding (compass, quest
  tracker, interaction prompt, nameplate, world-event banners, toasts), and the combat/boss
  HUD (boss healthbar, lock-on reticle, crit/stagger/block/parry feedback, the corruption
  vignette hook) — all unified on the tokens, with juice.
- **Menus rebuilt on one framework** — a screen/route manager + a reusable panel shell
  (modal/non-modal), tabs, list/grid, and a tooltip system; inventory, character/equipment,
  perks, crafting, dialogue, journal/quests and map panels rebuilt on it.
- **Feel & input** — motion/microinteractions (transitions, hover/press, value-change
  animations) with a reduced-motion guard; a **gamepad/keyboard focus-navigation** system with
  input-device-aware glyphs; a UI-scale + legibility pass verified at min-spec / Steam Deck.
- **Localized from the start** — every string goes through the Phase 24 `Loc` layer (no
  hard-coded UI text). Accessibility is *advanced* here and *completed* in Phase 54.

### Phase 31 — Audio Foundations `[F/P]`

- **`AudioDirector`** (`ServiceLocator`-registered) + Godot audio buses (master/
  music/SFX/ambience/UI/voice), volumes wired to Settings (Phase 24).
- **Adaptive music** — combat/exploration/boss/safe-zone states driven by
  EventBus (combat start/end, boss start, region/day-phase change); crossfades.
- SFX hooks across existing events (hit, cast, pickup, level-up, UI), 3D
  ambience per region/weather/time, footsteps by surface.

### Phase 32 — Companion System `[F]`

LORE: recruitable companions with personal storylines, loyalty missions, unique
abilities, and alternate-ending outcomes (Kael, Nyra, Orik, Seraphine, Vex).

- **`CompanionComponent` + follower AI** — recruit/dismiss, follow/hold/command,
  combat assist reusing `EnemyAIComponent`/`LocomotionComponent`/`CombatComponent`
  on the player's team; party roster persists (`ISaveable`).
- **Loyalty** — a per-companion standing (reuse `ReputationComponent` patterns)
  raised by choices/loyalty quests; gates banter, abilities, and ending flags.
- **Content hooks** — companions are data: a `CompanionResource` + a recruit quest
  + a dialogue graph + a loyalty quest each. One companion (Kael) authored fully
  in the slice; the rest in Beta. *No romance* (LORE) — friendship/brotherhood.

### Phase 33 — Vertical Slice Assembly & Onboarding `[C/P]`

- Stitch 22–32 into a continuous, polished 30–60 min: new game → creation →
  opening → Ember Crown → a quest chain → a guild taste → the Iron King slice →
  a corruption beat → a cliffhanger.
- **Onboarding/tutorial** — diegetic teaching of move/look/combat/block/dodge/
  magic/interact/inventory/quests, skippable, through the existing prompt/toast
  systems.
- First **external-facing build** candidate (capture for trailer/playtest).

> **🚩 Gate G1 — Vertical Slice.** A stranger can play 30–60 min that looks and
> feels shipped: real art, real audio, combat that has weight, a companion at your
> side, a boss, the corruption hook paying off. This is the project's "yes, this
> is the game" moment.

---

## 4. Stage C — Alpha / Feature Complete (→ G2)

**Goal:** **every system and mechanic the finished game will ever have now exists
and works.** Content can be rough, incomplete, greyboxed — but after G2 we never
again *invent a mechanic*, only author content and fix. This is the stage that
de-risks the schedule.

### Phase 34 — Enemy & Creature Roster (bestiary framework) `[F/C]`

- The archetype matrix the four realms need: humanoids (bandits, cultists,
  soldiers, the Iron Syndicate), beasts (wolves, the Sylthari-adjacent wildlife),
  undead (the Hollow Queen's legions), constructs, corrupted/Ashen creatures,
  elementals. Each = a factory archetype (CLAUDE.md §8 "new actor") + `.tres`
  attributes/loot/XP + an AI behavior profile.
- **AI behavior variety** — ranged, casters, shielded, pack/flanking, fleeing,
  ambush — as tunable `EnemyAIComponent` profiles/behavior data, not one-offs.
- **Caster roster (woven, Phase 29.5)** — flesh out the 29.5 caster AI into a *roster*
  of school-themed casters per faction: cultist pyromancers (Fire), Hollow-Queen
  necromancers (Necrotic), Sylthari nature-shamans (Nature), Iron Syndicate stormcallers
  (Lightning), Valari battle-mages (Arcane). Each = a `.tres` spell loadout + a caster
  behavior profile, no new code (the casting mechanic ships in 29.5).
- A `BestiaryDatabase` + an in-game bestiary (Ash Hunters fantasy) tracking kills/
  lore — content, via existing UI patterns.

### Phase 34.5 — Frostfang Clans & Beast-Race Factions `[F/C]`

LORE names Frostfang Reach's "warrior clans and beast races" as a distinct culture, not
generic wildlife. Without this they'd dissolve into Phase 34's bestiary as just more
enemies.

- A `FactionResource` for the Frostfang clans (distinct from the five guilds) with hub/
  outpost presence, reputation/dread (23G) applying to it like any faction.
- Clan archetypes (raiders, beast-tamers, shamans) authored on the Phase 34 archetype
  matrix — data, no new AI code.
- A short questline + rank chain (mirrors Phase 42's pattern, scoped to one realm) so
  the clans are a real culture to ally with or fight, not set dressing.

### Phase 35 — Dragons `[F/C]`

A LORE tentpole (Ancient/Wild/Ash dragons) and a marquee feature.

- **Aerial+ground dragon AI** — flight pathing, landing/takeoff, breath cones/
  AoE (reuse `SpellResolver`/status), tail/wing melee, multi-hit-zone bodies; a
  scalable boss-class actor.
- **Variants** — Wild (territorial world bosses), Ash (corrupted elite enemies),
  Ancient (intelligent, *speak* via dialogue — quest/lore givers). Optional later:
  Draekyn dragon-blood interactions; mountable dragons are a stretch/post-launch.
- **Ancient dragons as Weave-keepers (woven, Phase 29.5)** — the intelligent Ancients
  hold *lost spellcraft*: defeating or earning one's favor **teaches a recovered spell**
  (the 29.5 Weave-recovery loop), and dragon breath is authored as a 29.5 **channeled**
  spell with school identity (reusing `SpellResolver`/status), not a bespoke attack.
- Dragon encounters seed Frostfang Reach and high-end world events.

### Phase 36 — Boss Framework & Encounter Design `[F]`

Generalize the Iron King slice (Phase 28) into the reusable kit for all six
fallen Flamebearers + the Ashen Knight + Morthul + dragons + world bosses.

- **`BossResource` + `BossController`** — phase definitions (HP thresholds,
  ability sets, enrage), arena hooks, telegraph/wind-up tooling, adds/summon
  waves, interrupt/stagger windows, a boss healthbar + intro/defeat sequencing,
  and a guaranteed reward (often a **divine relic** + corruption gain).
- Authoring a boss becomes mostly data + a dialogue/cinematic + an arena.

### Phase 37 — Housing & Player Property `[F]`

LORE: purchasable homes, cabins, towers, estates, fortresses with storage,
crafting stations, trophies, and customization.

- **`PropertyComponent`/`HousingService`** — purchase/claim, per-property
  persistent storage (extends inventory persistence), placeable crafting stations
  (reuse `CraftingStationFactory`), trophy/display slots, and decoration. One
  property type playable here; the rest authored as content.
- Ties to economy (Phase 38) and fast travel (Phase 25).

### Phase 38 — Economy, Vendors & Services `[F/C]`

- **`VendorComponent`/`ShopResource`** — buy/sell with the existing item system,
  per-vendor stock (static + restocking + leveled), buy/sell spreads,
  reputation-discounts (faction standing), gold sinks.
- **Services** — repair (a durability system if adopted in Phase 40), trainers
  (buy perks/skill points), bank/storage, stablemaster (mounts), innkeeper (rest/
  time-skip).
- Economy balance is later (Phase 56); this builds the machinery.

### Phase 39 — Mounts & Traversal `[F]`

- **`MountComponent`** — summon/dismount, mounted locomotion/sprint/stamina,
  mounted-while-combat rules; integrates with fast travel and stamina.
- **Traversal verbs the world needs** — climbing, swimming, ledges/jumping
  refinements — added only where region design (44) calls for them.

### Phase 40 — Survival & Needs (scoped, deliberate) `[F]`

A **decision phase**, not an assumption. Carry weight already exists (Phase 5).
Decide — and record in `docs/DESIGN.md` — whether to add durability, food/rest,
or temperature (Frostfang fiction supports it), or to **explicitly cut** them.
Build only what survives the design call; an empty/cut phase is a valid outcome.

### Phase 40.5 — Dungeon & Puzzle Framework `[F]`

LORE's ruins, buried temples, and dragon nests imply environmental puzzles and traps —
no phase 1–40 builds that tooling; `EncounterResource`/`WorldEventResource` are combat
-only. This lands before dungeon content (Phase 50E) authors against it, and gives
divine-relic acquisition (Phase 51E) a real seam.

- **Puzzle primitives** — a `PuzzleResource`/`PuzzleComponent` family (lever/pressure
  -plate, sequence, light-and-shadow) wired through `InteractableComponent` + the
  EventBus, gating a door/reward on solve.
- **Trap primitives** — hazard triggers (spikes, dart volleys, collapsing floors)
  dealing damage through the existing `DamagePacket` pipeline, authored as data.
- **Relic-trial vault convention** — one puzzle + one guardian encounter per vault, the
  template every realm's divine relic (51E) reuses for its acquisition.
- A CLAUDE.md §8 "a new puzzle/trap" recipe, and `ContentValidator` checks for
  solvability/dangling triggers.

### Phase 41 — Quest Authoring at Scale & Branching `[F/C]`

The systems quest framework does Kill/Collect (Phase 9). The main story needs
more **objective types and branching**.

- New `ObjectiveResource` types: Reach/Explore, Escort, Defend/Survive, Interact/
  Use, Talk, Timed, Stealth, Choice/Branch — each event-driven like the existing
  two; **branching** via story flags + dialogue effects (Phase 10 already has the
  flag spine).
- Quest **state graphs** (a quest with multiple endings/paths), failure states,
  and quest-driven world changes (an NPC dies, a region opens).
- A quest-debugging console (`quest start/advance/complete/reset`) for content QA.

### Phase 41.5 — Divine Shrines & Blessings `[F/C]`

LORE devotes a full section to the Seven Gods, but no phase ever gives them an in-game
presence beyond Morthul-as-villain. This mechanizes the other six.

- **`ShrineResource` + `BlessingComponent`** — a shrine interactable per god (Solaryn,
  Veyra, Tharos, Nyth, Drakar, Elyndra) granting a persistent passive on first visit,
  reusing the perk/stat pipeline (no new system, just a god-flavored content wrapper).
- **One shrine per realm, placed to fiction** — e.g. Drakar (war/strength) in
  Frostfang, Nyth (knowledge/magic) in Sunspire's libraries; placement is content.
- **Corruption interplay** — a high-corruption visit can trigger a refusal/curse
  variant instead of the blessing (a `CorruptionComponent` condition check), tying the
  gods back into the defining mechanic.

### Phase 42 — Guild & Faction Questlines `[C]`

The five LORE guilds (Dawnwardens, Ash Hunters, Veiled Archive, Iron Syndicate,
Emberbound) as joinable factions with rank progression and multi-quest arcs.

- Each guild = a `FactionResource` (Phase 16) + a membership/rank flag chain + a
  questline (Phase 41) + a hub presence + rewards. Mostly **content**; any rank/
  membership UI is small.
- **Veiled Archive = the spell-recovery questline (woven, Phase 29.5)** — the scholar
  guild's arc *is* the Weave-recovery loop: hunting lost tomes, ley sites, and Ancient
  knowledge to restore spellcraft, rewarding recovered spells + mastery. Pure content on
  the 29.5 systems (quest + dialogue + tome rewards).

### Phase 42.5 — The Crimson Cult `[F/C]`

The Crimson Prophet "built an empire of worshippers" (LORE) — currently that's only a
boss fight at the end of Sunspire Dominion, with no in-world presence backing it.

- **A hostile `FactionResource` for the Crimson Cult** with outpost/hub presence
  seeded in Sunspire Dominion (ties to Phase 44D).
- **Cult archetypes** (zealots, inquisitors) authored on the Phase 34 matrix — data,
  no new AI code.
- **An infiltration questline** (a Phase 41D choice/branch arc) letting the player
  pose as a convert ahead of the Crimson Prophet confrontation (47E), rewarding
  cult-specific lore/items.

### Phase 43 — Cinematics & Scripted Sequences `[F]`

- **In-engine cutscene tooling** — a `CutsceneResource`/`SequenceDirector`
  (timeline of camera moves, actor blocking, dialogue, VFX/SFX, fades), skippable,
  pausing gameplay cleanly (works with `GameState`). Reuses the dialogue + audio +
  animation systems.
- Scripted set-pieces (a city under attack, a boss intro, a betrayal) become
  authorable for the story acts.

### Phase 43.5 — Flamebearer Vision Sequences `[F/C]`

DESIGN §5 demands the player *feel* themselves becoming a fallen Flamebearer; today
defeating one is just a stat/loot beat. A short flashback per Flamebearer makes the
corruption theme experiential, not narrated.

- **A `VisionSequence` cutscene variant** on the Phase 43 `CutsceneResource`/
  `SequenceDirector` (a desaturated/ash-tinted playback mode — no new timeline system),
  triggered from the boss-defeat hook already wired in 28D/36E.
- **One vision per fallen Flamebearer** (Iron King, Hollow Queen, Storm Tyrant, Beast
  Lord, Crimson Prophet, Ashen Knight) showing how they fell.
- Ties into the corruption appearance shift (23F/30I) — the player's reflection in the
  vision can hint at their own rising tier.

### Phase 44 — Alpha Content Pass: all four realms blocked out `[C]`

Greybox + first-pass content for the **whole game's shape**: Ember Crown,
Frostfang Reach, Ashen Wilds, Sunspire Dominion — each with its hub(s), key POIs,
encounter sets, the resident fallen Flamebearer's lair/boss stub, and the
main-quest spine connecting them. Rough but **complete in extent** — every realm,
every boss, every guild reachable.

### Phase 44.5 — World State: Realm Decay & Restoration `[F]`

Dawnfire's "the lands heal, dragons return" (LORE) implies the *world* should reflect
story progress, not just the player's corruption tier (Phase 23 is player-only). This
gives the macro "return changed" arrow (DESIGN §2.1) a world-scale half.

- **`RealmStateComponent`/`WorldStateService`** — a per-region decay tier (mirrors
  `CorruptionTier`'s shape, realm-scoped) driven by story flags (a Flamebearer
  defeated, a relic claimed); `ISaveable`.
- **Visual hooks** — lighting/fog/weather-bias read the tier now; the Phase 53 art
  pass builds the final look on top.
- **Ending payoff seam** — Phase 49's endings write a final realm-wide state (healed
  for Dawnfire, ashen for Lord of Embers) that Phase 53/65 content reads.

### Phase 45 — Alpha Hardening & Feature Freeze `[F/P]`

- Full integration test of the entire feature set; fix system interaction bugs;
  profile the streaming world under load; lock the feature list.
- **Feature freeze declared.** From here, no new mechanics without an explicit,
  recorded exception — only content and fixes.

> **🚩 Gate G2 — Alpha / Feature Complete.** Every mechanic in the shipped game
> exists and works together: corruption, races, companions, dragons, bosses,
> housing, economy, mounts, cutscenes, all quest types, all four realms reachable.
> A determined player can traverse the entire game's *shape* even if content is
> rough. **The schedule is now de-risked.**

---

## 5. Stage D — Beta / Content Complete (→ G3)

**Goal:** **all content is in.** The main story is playable start to finish to
*both* endings; side content is authored; art and audio are complete. What remains
is bugs, balance, and polish — not creation.

### Phase 46 — Main Story, Act I: Awakening `[C]`

Full content for Act I (LORE): the player discovers they are the Seventh
Flamebearer, ancient forces begin hunting them, the journey begins. Opening,
inciting incident, first companion, the corruption seed, the hook into Act II.

### Phase 47 — Main Story, Act II: Gathering the Flame `[C]`

The bulk of the game (LORE): travel all four realms, acquire divine relics, build
alliances, defeat the fallen Flamebearers — the Iron King, the Hollow Queen, the
Storm Tyrant, the Beast Lord, the Crimson Prophet (and seeds of the Ashen Knight
rivalry). Each realm = its questline + boss + relic + corruption beat + guild ties.

### Phase 47.5 — The Ashen Knight: Rival Duels `[C]`

LORE calls the Ashen Knight "the player's greatest rival" — that fiction calls for a
rival *arc*, not one reveal in Act IV. Phase 47F only seeds the rivalry; this phase
pays it off with content.

- **Two scripted duels** (mid Act II, then Act III) using the Phase 36 Boss Framework +
  Phase 43 cinematics — non-lethal/escape-clause encounters that escalate the Ashen
  Knight's hostility and banter.
- **A flag thread** — each duel sets story flags the Act IV final confrontation (49B)
  reads, so the rivalry has visible build-up rather than appearing cold.

### Phase 48 — Main Story, Act III: Truth of the Gods `[C]`

The mid-game turn (LORE): the history of the Divine Cataclysm, the true nature of
Morthul/the Ash King, and the revelation that *someone must always sit upon the
Ash Throne* — the thematic pivot that sets up the endings.

- **The Weave's truth (woven, Phase 29.5)** — Act III is where the *fading Weave* pays
  off narratively: the death of Nyth (the magic-goddess) as the cause of magic's decline,
  and the choice between restoring the Weave (Dawnfire) or feeding on its corrupted dregs
  (Lord of Embers). Story beats gate the highest **recovered/ancient spells** behind this
  turn. Content on the 29.5 Weave system, feeding the Phase 49 endings.

### Phase 49 — Main Story, Act IV: The Celestial War + Endings `[C]`

The climax (LORE): assault the ruined Celestial Realm, defeat the **Ashen Knight**
(the player's rival), confront **Morthul**, and the **final choice** — both
endings authored and reachable:

- **Dawnfire** — reject power, restore balance, the Age of Dawn begins.
- **Lord of Embers** — embrace corruption, claim the Ash Throne, the Age of Embers
  begins.

The corruption system (Phase 23) and companion loyalty (Phase 32) feed ending
eligibility and variations. Epilogues per ending + per major choice.

### Phase 50 — Side Content, Activities & World Density `[C]`

Side quests across all realms, dungeons/lairs, the full world-event/encounter
tables, collectibles (lore books for the Veiled Archive fantasy), bounties (Iron
Syndicate / Ash Hunters), companion loyalty quests, ambient NPC life, and the
density that makes an open world feel alive.

### Phase 50.5 — Lore Codex & Compendium `[F/C]`

Phase 50G authors lore-book collectibles, but nothing builds the UI to read or track
them against the gods/Flamebearers/realms/guilds they reference — distinct from the
combat bestiary (34G).

- **`CodexEntryResource` + `CodexDatabase`** — entries unlock on a flag/collectible
  pickup, `ISaveable`.
- **A Codex panel** built on the Phase 30.5F panel framework; locked entries show as
  teasers, unlocked ones are fully readable.
- **Seed entries for every god, fallen Flamebearer, realm, and guild** so the
  compendium is complete in *shape* even before every collectible is placed.

### Phase 51 — Itemization, Loot & Reward Economy Pass `[C]`

The full item catalogue: weapons/armor/accessories per tier and realm, the affix/
set families, consumables/materials/recipes, and the **divine relics** (unique
flamebearer-power items tied to corruption and abilities). Reward placement across
quests/bosses/dungeons; the loot tables of the whole game authored and curated.

- **The full spell catalogue (woven, Phase 29.5)** — author the *complete* spellbook
  against the 29.5 systems: every school fleshed to a viable build across tiers, signature
  charged/channeled spells, the corrupted-magic line, and **spell tomes as loot** + a few
  **relic spells** (divine-relic-tier). This is the magic content *bulk*, and it lives here
  because it is data on frozen systems (G2-safe) — no new mechanics, only authoring.

### Phase 51.5 — Enchanting & Relic Socketing `[F/C]`

Not LORE-mandated — an optional itemization deepener beyond the existing affix system.
Cut cleanly if it doesn't clear playtest; flagged here so it isn't lost.

- **`SocketComponent`** on rare+ equippables + an `EnchantResource` consumable that
  slots in for a stat bonus, extending `EquipmentComponent` rather than replacing
  affixes.
- Authored as a content tail on top of Phase 51's catalogue.

### Phase 52 — Full Audio & Music Production `[P]`

The complete adaptive score (per realm/boss/theme), full SFX coverage, ambience
for every region/weather/time, and **voice-over** (at minimum key story/companion
beats) recorded and integrated through the `AudioDirector`.

### Phase 53 — Art Complete & World Beautification `[P]`

Final environment art across all four realms, character/creature/boss final
models, the dying-world art direction fully realized (light fading, ash, ember
glow), VFX polish, set dressing, and the visual cohesion pass. No greybox remains.

### Phase 53.5 — Photo Mode `[P]`

Not LORE-mandated — a polish-tier nicety, not a gap. Pairs naturally with the Phase 53
art pass.

- A pause-state free camera (reuses `GameState.Paused`), hide-HUD toggle, and a few
  dying-world-themed filters matched to Phase 53's art direction.

### Phase 54 — Accessibility & Input `[F/P]`

Full input remapping (KB/M + controller), subtitles + speaker names + sizing,
colorblind options, scalable difficulty (per LORE "easy to learn, hard to
master" — difficulty options, not class locks), aim/lock-on assists, and
**Steam Deck** input/UI verification. A ship requirement, not a nicety.

### Phase 55 — Content-Complete Integration & First Full Playthrough `[C/P]`

A complete, no-placeholder playthrough start→finish→**both endings**; fix
narrative/flag/sequence breaks; confirm every quest, region, boss, companion, and
guild arc is reachable and completable.

> **🚩 Gate G3 — Beta / Content Complete.** The whole game is playable end to end,
> both endings reachable, all art/audio in, no placeholders. From here it is
> *only* balance, bugs, polish, and ship.

---

## 6. Stage E — Release Candidate (→ G4)

**Goal:** turn content-complete into ship-ready. No new content; stabilize,
balance, certify.

### Phase 56 — Balance & Difficulty Tuning `[C/P]`

Combat math (damage/armor/crit, weapon classes, spell schools), the XP curve and
level cap, the economy (prices, gold flow, sinks), encounter pacing and boss
difficulty, **corruption pacing** (so both endings are earnable and the
temptation reads), and the difficulty options. Data-driven via the existing
resources; informed by playtest + telemetry (Phase 22).

### Phase 57 — Performance & Memory Cert `[P]`

Hit the frame budget on **min-spec PC and Steam Deck** (LORE target): streaming
hitch elimination, draw-call/LOD/shadow budgets (extends Phase 19), memory
ceilings, load-time targets, and shader pre-compilation. Profile-guided, not
guessed.

### Phase 58 — Save/Load Hardening & Migration `[F]`

Stress the save system against **100+ hour playthroughs**: schema migration
across patches (the `TryMigrate` seam exists), corruption recovery, slot
integrity, autosave cadence, and cloud-save compatibility. The thing that, if it
breaks at launch, breaks trust.

### Phase 59 — Bug Triage, QA & Soak `[P]`

The full QA matrix: functional passes per region/quest/system, soak/longevity
tests, edge-case and regression suites (grow `Embervale.Tests` + GUT in-engine
tests), a crash-free-session target, and a triaged bug database burned down to
zero blockers.

### Phase 60 — Localization Completion & Culturalization `[C/P]`

Full string extraction and translation in the shipped languages, font/glyph
coverage (CJK as scoped), text-fit/overflow LQA, and culturalization review. Made
cheap because everything went through the Phase 24 `Loc` layer from the start.

### Phase 61 — Platform Compliance & Storefront `[P]`

Steam (and any console) cert: TRC/XR/lotcheck requirements, achievements/trophies,
cloud saves, controller glyphs, the store page (capsule, screenshots, trailer cut
from the slice), age ratings, EULA/credits, and reproducible release builds.

### Phase 62 — Release Candidate & Gold Master `[P]`

Code/content lock; RC build series; final cert pass; the day-one patch plan; gold
master sign-off against the G4 bar (**zero known crash/blocker bugs**).

> **🚩 Gate G4 — Release Candidate.** A gold-master-quality build, certified on
> target platforms, zero blockers, day-one patch staged. Ready to ship.

---

## 7. Stage F — Launch (→ G5)

### Phase 63 — Launch `[P]`

Ship to Steam (+ any console) on the target platforms (Windows, Linux, Steam
Deck). Day-one patch live, store page up, monitoring and crash telemetry on,
community/support channels staffed.

> **🚩 Gate G5 — Launch.** Embervale is live.

---

## 8. Stage G — Live / Post-launch (→ G6)

### Phase 64 — Launch Response & Stabilization `[P]`

Triage real-player crash/telemetry, ship hotfixes and a first balance patch,
respond to community, and stabilize the live build.

### Phase 65 — Post-Launch Content (the long tail) `[C/F]`

**New Game+** (carry-over + escalated difficulty, leveraging corruption/relics),
higher difficulty tiers, additional regions/dungeons/bosses, more companions and
loyalty content, seasonal world events — all riding the data pipeline.

### Phase 66 — Expansion / DLC Framework `[F/C]`

Entitlement/DLC content loading, a new-realm-sized expansion seam, and the tooling
to ship paid expansions without forking the base game. The bridge to Embervale's
future.

> **🚩 Gate G6 — Live.** A shipped game with a sustainable content cadence.

---

## 9. Cross-cutting tracks (run through every stage)

Some work isn't a phase — it's a discipline maintained continuously:

- **Buildable & playable, always** (CLAUDE.md §1). Every commit. Non-negotiable.
- **Persistence first** — any new stateful system is `ISaveable` the day it lands,
  not retrofitted.
- **Data over code** — author content as `.tres` against existing systems; reserve
  new code for genuinely new mechanics (and freeze those at G2).
- **Validation discipline** — `ContentValidator`/`validate` stays green; broken
  references never merge.
- **Localization discipline** — after Phase 24, no hard-coded player-facing
  strings.
- **Performance budget** — keep the Phase 19 LOD/pooling discipline as the world
  and content grow; don't let it rot before the Phase 57 cert.
- **Testing** — grow `Embervale.Tests` (pure logic) + in-engine GUT (systems);
  a new system ships with coverage of its load-bearing math/flow.
- **Accessibility & input** — design for remap/subtitle/scalable difficulty from
  the start; Phase 54 *completes* rather than *invents* it.
- **Telemetry-informed balance** — once Phase 22's spine exists, let data steer
  difficulty/economy decisions.

---

## 10. Dependency spine (why the order)

The ordering is driven by hard dependencies, not preference:

1. **Corruption (23)** is the defining mechanic and threads through dialogue,
   factions, abilities, appearance, and *both endings* — it must exist before the
   slice and before any story content references it.
2. **Shell + localization (24)** must precede mass content authoring or every
   string becomes a retrofit tax, and you cannot playtest a slice without a way to
   start/save a game.
3. **Streaming + map (25)** must precede authoring four large realms (27, 44),
   or regions get built against assumptions streaming later breaks.
4. **Races (26)** affect the player from character creation, so they precede the
   opening/onboarding (33, 46).
5. **The vertical slice (27–33)** proves the pipeline and quality bar before
   scaling content — the classic "one of everything, perfect" before "all of
   everything." The **UI/HUD overhaul (30.5)** sits late in the slice on purpose:
   it lands *after* the art direction (30) and after the individual UI surfaces
   (23–29) exist, so it unifies and beautifies them in one pass rather than
   polishing a moving target — and *before* the slice is assembled (33).
   **Magic depth (29.5)** sits in the slice for the same reason combat feel (29) does:
   magic is a *pinned build spine* (DESIGN §1.5), so its mechanics — cast archetypes,
   school identities, mastery, combos, the Weave, caster AI — must exist and prove out
   in the slice. Its *breadth* (the full catalogue, all-faction casters, dragon/guild
   spell-recovery) is then pure content woven through 26, 34, 35, 42, 47–48, 51 against
   those frozen systems — the magic case of the data-over-code rule.
6. **Feature-complete (34–45)** front-loads *all* remaining mechanics so the
   content stages (46–55) never block on a missing system. **This is the schedule's
   keystone:** G2 is the promise that nothing left is unknown engineering.
7. **Content (46–55)** then runs as parallelizable authoring against frozen
   systems — the most schedulable, most outsourced-friendly work.
8. **RC (56–62)** can only meaningfully tune/cert a content-complete game.

---

## 11. Status

| Stage | Gate | Phases | Status |
| ----- | ---- | ------ | ------ |
| A — Pre-production & First Playable | G0 | 22–28 | ✅ Complete (22–28 + 25.5 hardening; G0 First Playable reached) |
| B — Vertical Slice | G1 | 29–33 | ⏳ In progress (29 ✅, 29.5 ✅; next: 30 Animation, Models & Visual Identity) |
| C — Alpha / Feature Complete | G2 | 34–45 | ⬜ Planned |
| D — Beta / Content Complete | G3 | 46–55 | ⬜ Planned |
| E — Release Candidate | G4 | 56–62 | ⬜ Planned |
| F — Launch | G5 | 63 | ⬜ Planned |
| G — Live / Post-launch | G6 | 64–66 | ⬜ Planned |

**Where we are:** **Stage A is complete** — Phases 22 (production bible/pipeline), 23 (Corruption),
24 (meta-shell + localization), 25 (region streaming, world map/compass, fast travel), the 25.5
hardening pass, 26 (races & character creation), 27 (the Ember Crown vertical core) and 28 (the Iron
King boss slice) are all done and **Gate G0 (First Playable) is reached**. Stage B is underway:
**Phase 29 (Combat Feel & Game Juice) is complete** (hit-stop, shake, directional reactions,
parry/riposte, dodge i-frames, lock-on, stamina pacing), and **Phase 29.5 (Spellcraft & the Fading
Weave) is complete (29.5A–G)** — cast archetypes (charged/channeled), school identities +
status-effect mechanics, spell scaling + per-school mastery, reactive combos, the fading-Weave
region-potency dial + spell recovery, enemy/NPC caster AI, and the 29.5G magic UI (a school-grouped
spellbook with mastery/charge/channel feedback, a SpellPoints economy, and one signature spell per
school: Flame Lance, Ball Lightning, Blizzard, Blink, Lifebloom Totem, Ember Siphon).

**Immediate next step: Phase 30 — Animation, Models & Visual Identity** (the art direction made
real for the slice cast; sub-phases 30A–30I in `SESSION_PLAYBOOK.md`), then 30.5 (UI overhaul),
31 (audio), 32 (companions), 33 (slice assembly) toward **Gate G1 — Vertical Slice**.

> This roadmap turns the 21-phase *systems sandbox* into **Embervale, shipped** —
> a third-person open-world fantasy RPG where you battle fallen heroes across four
> dying realms and choose whether to save creation or become its next Ash King.
