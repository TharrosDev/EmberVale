# Embervale — Production Roadmap (Alpha → Beta → Launch)

> **~22k tokens — this is the phase-level plan, not the work list.** For what to actually do
> next, go to [`docs/playbook/`](playbook/README.md): it breaks every phase here into
> session-sized sub-phases and carries the retrospectives. Come here for a phase's *scope*, its
> *gate*, and where it sits between Alpha and Launch. §11 mirrors phase-level status.

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
> a single Claude Code session. [`docs/playbook/`](playbook/README.md) breaks
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
   the recipes in docs/RECIPES.md allow; new code is only for genuinely new
   mechanics.
4. Cross-references resolve under the `ContentValidator` (`validate` console
   command) — no dangling item/quest/dialogue/template ids.
5. `README.md` + this file are updated (mark phase, queue next); a **draft PR**
   into `main` is opened (CLAUDE.md §9).
6. New player-facing strings go through the **localization layer** — `Loc.T("key")`
   against `data/locale/strings.csv` (live as of Phase 24G) — **no hard-coded UI text**.

### 0.4 Content the systems already make free

Because the architecture is resource-driven, large swaths of the game are
**authoring, not engineering**. Per docs/RECIPES.md, each of these is "a `.tres`, no
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
authoring recipes that turn them into content with no new code, see docs/RECIPES.md.

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
| 39.5 | World Map & Location Intelligence | F/C | The map as the world's geographical index: a location layer, discovery, search, waypoints |
| ~~40~~ | ~~Survival & Needs~~ | ❌ | **NOT WANTED — struck 2026-08-12.** No durability, hunger, thirst, temperature or encumbrance. Ever |
| ~~40.5~~ | ~~Dungeon & Puzzle Framework~~ | ❌ | **NOT WANTED — struck 2026-08-12.** No puzzle, trap or vault tooling. Phases 50 and 51E owe their own answers |
| 41 | Quest Authoring at Scale & Branching | F/C | Beyond Kill/Collect: escort, defend, choice/branch, timed, faction-gated objective types |
| 41.5 ✅ | Divine Shrines & Blessings | F/C | The Seven Gods, mechanized — shrine blessings tied to each god's domain, refused above each god's corruption tolerance |
| 42 | Guild & Faction Questlines | C | The five LORE guilds as joinable factions with multi-quest arcs and ranks |
| 42.5 | The Crimson Cult | F/C | The Crimson Prophet's "empire of worshippers" as a real hostile/infiltrable faction |
| 43 | Cinematics & Scripted Sequences | F | In-engine cutscene tooling, camera tracks, scripted set-pieces, dialogue staging |
| 43.5 | Flamebearer Vision Sequences | F/C | A flashback-cutscene type showing how each fallen Flamebearer fell, triggered on their defeat |
| 44 | Alpha Content Pass — all five realms blocked out | C | Greybox + first-pass content for every realm (incl. the hidden Pale Concord), every fallen Flamebearer |
| 44.5 | World State: Realm Decay & Restoration | F | A per-realm decay/restoration tier so the world visibly reflects story progress and the ending choice |
| 45 | TRUE Feature-Complete Audit & Freeze | F/P | Matrix-audit every mechanic, close feature holes (including physical ranged combat), then freeze |

### Stage D — Beta / Content Complete → **G3**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 46 | The Main Story — Act I: Awakening | C | Full narrative content for Act I |
| 47 | The Main Story — Act II: Gathering the Flame | C | All five realms' main arcs + the six fallen Flamebearers |
| 47.5 | The Ashen Knight: Rival Duels | C | Recurring non-final duels across Acts II–III so the "greatest rival" has a real arc, not one Act IV reveal |
| 48 | The Main Story — Act III: Truth of the Gods | C | The mid-game turn, lore reveals, the Ash Throne |
| 49 | The Main Story — Act IV: The Celestial War + Endings | C | The Ashen Knight, Morthul, both endings (Dawnfire / Lord of Embers) |
| 50 | Side Content, Activities & Wilderness Pacing | C | Measured realm-specific content distribution without filling intentional empty country |
| 50.5 | Lore Codex & Compendium | C/P | Populate and finish the pre-G2 Codex foundation; skip entirely if G2 cuts it |
| 51 | Itemization, Loot & Reward Economy Pass | C | The full item/affix/set/relic catalogue; the divine relics; reward curves |
| 51.5 | Enchanting & Relic Socketing | F/C | Optional itemization deepener — sockets + enchant consumables on rare+ gear |
| 52 | Full Audio & Music Production | P | Coverage-matrix score/SFX/ambience production; VO scope decided before recording |
| 53 | Art Complete & World Beautification | P | Final art pass across all regions; lighting; set dressing; the dying-world identity |
| 53.5 | Photo Mode | P | Optional polish-tier feature — free camera, hide-HUD, dying-world filters |
| 54 | Accessibility & Input | F/P | Complete remapping/subtitles/assists/difficulty and audit the accessibility/controller work already shipped |
| 55 | G3 Content-Complete Acceptance Campaign | C/P | Campaign both endings, all branches/realms/systems, reachability and placeholder closure |

### Stage E — Release Candidate → **G4**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 56 | Balance & Difficulty Tuning | C/P | Combat math, economy, XP curve, encounter pacing, the corruption pacing |
| 57 | Performance & Memory Cert | P | Derive budgets on approved target hardware; certify frame pacing, world loads, memory and shaders |
| 58 | Save/Load Hardening & Migration | F | Long-playthrough saves, migration, corruption recovery, slot integrity |
| 59 | Bug Triage, QA & Soak | P | Full test matrix, soak/longevity, telemetry, crash-free target |
| 60 | Localization Completion & Culturalization | C/P | Full string coverage, fonts/glyphs, LQA in shipped languages |
| 61 | Platform Compliance & Storefront | P | Approve targets first; reproducible builds, required services, compliance and storefront |
| 62 | Release Candidate & Gold Master | P | Lock, RC builds, final cert pass, day-one patch plan |

### Stage F — Launch → **G5**

| #  | Phase | Tag | One-liner |
| -- | ----- | --- | --------- |
| 63 | Launch | P | Ship approved targets with verified artifacts, monitoring, support and rollback ready |

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
  per domain (cross-linking docs/RECIPES.md recipes). Optionally a tiny Godot
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
(systems 1–21) — lives in `docs/playbook/`.

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
  elementals. Each = a factory archetype (docs/RECIPES.md "new actor") + `.tres`
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
- **Services** — ~~repair~~ (❌ durability struck with Phase 40, 2026-08-12), trainers
  (buy perks/skill points), bank/storage, stablemaster (mounts), innkeeper (rest/
  time-skip).
- Economy balance is later (Phase 56); this builds the machinery.

### Phase 39 — Mounts & Traversal `[F]`

- **`MountComponent`** ✅ **(39A)** — summon/dismount and mounted locomotion/sprint/stamina.
  The mount is a **state of the rider, not a second body**: the player's own `CharacterBody3D`
  keeps moving, wearing a horse, so there is no navigation, no second persistence record and no
  dismount-placement problem — and no step-up either, which is invariant 16 unchanged and 39C's to
  decide. Ownership is 38D's `flag.stable.mount_owned` and a `--validate` rule holds the two halves
  together. Gallop is a pool of the horse's own, and its exhaustion **latch clears only when the
  player stops asking** (clearing on the recovery mark alone sawtooths). Mounted-while-combat rules
  Mounted-combat rules and fast-travel integration are ✅ **(39B)**: melee works from horseback and a
  **gallop is a charge** (a walking mount is exactly neutral), a dodge roll is the one verb riding
  takes away, and a mount makes a **local** jump free while a realm crossing still costs — a horse
  shortens a walk, it does not carry the player through 38M's toll for nothing. ⚠️ **The mount is no
  longer a pure sink**; `docs/DESIGN.md` §6's table says so. ⚠️ 39B also fixed two defects 39A
  shipped, one of which (a cached mesh rest pose dropping the rider through the horse on the first
  hit) passed every check 39A ran.
- **Traversal verbs the world needs** ✅ **(39C)** — **step-up only, at 0.5 m**, matched to every
  cell's `agent_max_climb` and pinned to it by a `--validate` rule. That mismatch was live for the
  whole project: the navmesh routed NPCs over ground a body could not follow them onto, and cells
  were authored around it (`embermarket.tscn` deleted a 0.3 m dais over exactly this). The dais is
  back as the realm's only raised ground and the verb's in-world caller.
  ⚠️ **Climbing and swimming are CUT, each against a runnable condition** rather than a verdict:
  swim when a cell authors a water *volume with something on the far side* (both water planes are
  decals today), climb when a cell authors a surface reachable no other way. **Neither left a stub** —
  40B's rule, worked example. Phase 44 should author verticality against 0.5 m.

### Phase 39.5 — World Map & Location Intelligence `[F/C]`

⚠️ **Inserted after Phase 39 shipped, with the maintainer's approval** — the map was on no phase at
all, having last been touched as 25E and 37.5E. It is numbered 39.5 because that is when it landed;
Phase 40 was not displaced.

The realm outgrew its map. 23 shops, 15 services and 15 cells, and the map could plot exactly three
kinds of thing — region spawn points, cell centres and fast-travel nodes — because those were the
only things in the game that had a position. **A shop has no coordinates**; `ShopResource.CellId` is
an economy field, buildings and districts are not entities, and NPC positions live in scene
transforms and schedule routes.

- **39.5A ✅** — the location layer and the map that reads it. `MapLocationResource` says what a place
  is and links to the authoritative record by id; a `MapLocationComponent` in the cell scene says
  where, and is the only record of it. 63 locations across all 15 cells, pan/zoom/tier-culling,
  search over shop and keeper names, category filters, a selection panel with distance and bearing,
  a waypoint, a live region breadcrumb, and land drawn from each cell's measured ground footprint.
  Five `--validate` rules including both directions of the scene seam, five negative tests, 47 unit
  tests, `tools/gen_map_locations.py` and `tools/map_probe.gd`.
- **39.5B ✅ — the player HUD** (maintainer direction, 2026-08-11: briefed as a standalone HUD
  overhaul and folded in here, because the minimap, the tracked quest and the compass are all
  `MapService`). ⚠️ **The audit is the finding: the HUD did not need overhauling.** Roughly sixty of
  the brief's eighty sections were already satisfied by Phase 18, 30.5B/C/D/I and 37.5B. Four were
  not — **no minimap existed anywhere in the repo**; there was **no tracked-quest concept** (two
  files scanned for the first active quest independently); `GameHud` had **no visibility logic at
  all**, so the whole HUD sat on top of every menu offering a prompt a paused tree ignores; and
  objective advances were silent. Shipped `MinimapHud` (a `MapView` with the mouse off, north-up),
  `MinimapFilter`, `MapPins`, `HudVisibility`, `DamageDirectionOverlay`, `QuestLogComponent.Tracked`,
  a distance-and-bearing readout, one `--validate` arm, three negative tests and 43 unit tests. ⚠️ **A
  death HUD and a combat HUD state were CUT and named** — respawn is synchronous so there is no
  window for one, and there is no `InCombat` flag for the other to read.

  ⚠️ **The maintainer sent the audit back — "they were done poorly" — and was right.** The ~60
  already-satisfied sections were accurately marked (real implementations, real bindings, real tests)
  and still contained four shipped defects, because **"does this work" and "is this good" are
  different questions and only one of them is answerable from code.** Building `--hudshots` (the UI
  capture harness 39.5A named as its most expensive gap) surfaced all four on its first run: a bar
  **trough the same colour as its background**, so every gauge in the game had an invisible empty
  track; `DayPhases.Label` returning **hard-coded English** the HUD clock had shown the player since
  Phase 18; `GameHud` **not being pause-immune**, so the new mode table never ran and the HUD froze on
  top of menus; and no low/critical health state at all. The quality pass also rebuilt the resource
  hierarchy, the clock, the compass strip, the tracker rows, the spell row and the hotbar's empty
  states. The section-by-section audit of all 80 is in
  [`docs/playbook/phase-39_5.md`](playbook/phase-39_5.md).
- **39.5C ✅ — panel capture, the map's labels, quest destinations.** Measured all eight deferred
  conditions before building any: three ripe, five not, and the five stay deferred with their
  measurements recorded. Built `--panelshots` **first** (39.5B built its harness last and had to
  revisit finished work), which found two defects immediately. ⚠️ **The clustering condition named
  the wrong defect** — markers never overlap (closest pair 2.13 m, 19 px at Detail zoom) but their
  50–70 px **labels** did, so `LabelPlacer` replaced the clustering that was queued. ⚠️ The map rail
  overflowed and Godot resolved it by crushing the `ExpandFill` child, slicing the FILTERS buttons in
  half. Quest destinations landed with the map-coverage arm CLAUDE.md §1 promised — ⚠️ **and exactly
  one objective in the game earns one**, because hostiles are region-scoped encounters and materials
  come off loot tables; this world needs search *areas*, not points. The compass was **rebuilt from
  nothing** at the maintainer's direction: nine draw passes in a 320×26 box, now four, with the
  discovered-place ticks cut because the minimap answers that question better.
- **Phase 39.5 is CLOSED.** ⚠️ **There is no 39.5D** — the remaining table is condition-gated rather
  than scheduled, and creating a sub-phase to hold unripe items is what 38G did wrong.

### ~~Phase 40 — Survival & Needs~~ ❌ **NOT WANTED — STRUCK 2026-08-12**

**Maintainer direction: this game has no survival needs.** Durability/repair, food/hunger and
temperature are **cut** — not deferred, not condition-gated, and there is no condition that revives
them. Rest already exists as a *sink* (`ServiceKind.Inn`, `service.ashfall.bed`) and is untouched.

The cut deleted two long-standing stubs, which is the only code this phase ever produced:
`CraftingStationType.Cooking` (zero recipes, zero stations, last enum member) and
`InventoryComponent.MaxWeight` / `IsOverEncumbered` — carry weight shipped in Phase 5 as *"not yet
enforced (drives encumbrance later)"* and sat with **zero readers for thirty-five phases**. `later`
never comes now, so the pair went. **`TotalWeight` stays** and the character sheet still prints it: a
weight readout is an item fact, not a budget.

⚠️ **Do not reach for durability as a Phase 56 economy fix.** §6's rule is *sinks the player wants to
spend on*; the table already has one recurring drip and sixteen purchases. Full reasoning and the
per-need table: [`playbook/phase-40.md`](./playbook/phase-40.md).

### ~~Phase 40.5 — Dungeon & Puzzle Framework~~ ❌ **NOT WANTED — STRUCK 2026-08-12**

**Struck whole**, same direction. No `PuzzleComponent`, no trap primitives, no relic-trial vault
convention. Nothing existed to remove. ⚠️ **The trap arm (40.5C) was offered as a partial keep and
declined** — recorded so the cheapest-looking piece is not re-proposed.

**Two phases were written against this one and now owe their own answer:**

- **Phase 50** — dungeons become **rooms with encounters and loot**, on the existing
  `EncounterResource` / `LootTable` / cell-scene tooling. ⚠️ **Do not reinvent hazards inside Phase 50**
  because a room feels empty; that is this phase returning through the side door.
- **Phase 51E** — the guardian half of a relic trial is already expressible (`LairSpawnComponent`);
  the *trial* half has no answer. The likeliest shape is a relic won from a fight or a quest, needing
  no new system. Detail: [`playbook/phase-40_5.md`](./playbook/phase-40_5.md).

### Phase 41 — Quest Authoring at Scale & Branching `[F/C]`

The systems quest framework does Kill/Collect (Phase 9). The main story needs
more **objective types and branching**.

- New `ObjectiveResource` types: ✅ **Reach/Explore and Talk (41A)**, ✅ **Escort and
  Defend/Survive (41B)**, ✅ **Interact/Use and Stealth (41C)**, then Choice/Branch —
  each event-driven like the existing two; **branching** via story flags + dialogue
  effects (Phase 10 already has the flag spine).
  - ⚠️ **Timed was NOT appended as an objective type (41C), deliberately.** A deadline is
    a property of the errand, and `QuestProgress` stores one `int` per objective with
    nowhere to put a per-objective clock — so it is `QuestResource.TimeLimitSeconds`,
    mirroring `WorldEventResource`. Two ways to express one deadline is invariant 5
    waiting to happen. **Do not "restore" it as a type.**
  - ⚠️ **41A's lesson for every type after it: the type is a few lines, and the whole
    job is knowing which existing event actually means the thing.** `Advance` is one
    choke point and the events mostly exist already, so the cheap implementation and
    the correct one differ only by a question nobody is forced to ask — "discovered"
    turned out not to mean "arrived". ✅ **41B asked its version — what counts as a fail,
    and which event says so — and the answer was that for an escort there IS no such
    event: nothing in this build can damage an NPC.** The escortee is therefore a
    companion (`CompanionFactory` builds a damageable ally from a `.tres`), and
    `CompanionDownedEvent` is the fail; Defend's fail is the player's own death.
    ✅ **41C's version found a third answer: an event that means the thing only when a
    data knob says so.** `EnemyAlertedEvent` is published only when the AI profile's
    `AlertRadius > 0`, which an ambusher authors as 0 — so stealth rides the
    unconditional `EnemyStateChangedEvent` instead. ✅ **41D asked its version — who else
    writes this story flag? — and the answer was that `ValidateStoryFlags` already knew,
    so the whole job was making objective gates READERS in it.** The fourth reader family
    joined a rule written in 34.5C, and the sub-phase's real difficulty turned out to be
    somewhere else entirely: an inert objective is a THIRD state (not met, not pending)
    that six drawing surfaces were all written without.
  - ✅ **THAT DEBT IS PAID (41D): `QuestResource.SequentialObjectives`.** Objectives were
    UNORDERED by rule for thirty phases and three `.tres` headers said *"wait for 41D"*.
    They now order on a **quest-level bool** (per-objective prerequisite indices were
    considered and declined — fragile the first time anyone reorders a `.tres`, and a
    diamond graph nothing asked for). Default is false, so every quest authored before
    this keeps today's behaviour with no edit. `quest.hollowreach.word` still sequences
    by geography and its header now says so as a *choice*, not as a wait.
- Quest **state graphs** (a quest with multiple endings/paths), ✅ **failure states
  (41B — `QuestStatus.Failed`, and a failed quest is RETAKEABLE because `CanStart`
  admits it, so every `QuestAvailable` dialogue gate reopens unchanged)**, and
  ✅ **quest-driven world changes (41E).** A quest's optional `CompletionFlagId` uses the
  persistent `StoryFlagsComponent` rather than a parallel world-state save. `quest.warband.heart`
  opens Frostfang Reach; Coyle's tally delivery removes him from the Locker by flag, deliberately a
  departure rather than a fake NPC death. `ValidateStoryFlags` covers the writer/reader seam.
- ✅ **A quest-debugging console (41F)** — `quest start/advance/complete/reset` drives the normal
  quest-log, reward, event and world-change path, rather than editing save state. `validate-all` also
  catches a branch that requires its own quest's completion flag: both ids resolve, but the branch can
  never enter because completion is what writes the flag.

### Phase 41.5 — Divine Shrines & Blessings `[F/C]`

LORE devotes a full section to the Seven Gods, but no phase ever gives them an in-game
presence beyond Morthul-as-villain. This mechanizes the other six.

- ✅ **41.5A — `ShrineResource` + `BlessingComponent`** — `shrine.solaryn` now grants a persistent
  first-visit Armor passive through the real sandbox interactable. The player owns one saved set of
  shrine ids and re-derives modifiers on load; `--validate`, a zero-bonus negative case, pure
  replacement tests, and four live front/back shrine frames cover the core.
- ✅ **41.5B — six shrine placements** — the remaining five blessings and Solaryn's final world body
  are live in six fiction-led, generated-map placements. The validator closes the resource set and
  requires exactly one caller per shrine; twelve eye-level front/back captures review the final cells.
- **Current playable-world placement** — the only complete realm is Ember Crown, so 41.5B places
  the fixed six callers across its fiction-led cells. When Phase 44 blocks out later realms, relocate
  (never duplicate) these map-linked callers as world layout demands.
- ✅ **41.5C — corruption-gated refusal** — every shrine authors its own `RefusalCorruption`
  threshold and refusal line, and `BlessingComponent.Offer` is the one place that decides
  already-claimed → refused → blessed. A refusal claims nothing, applies nothing and persists
  nothing, so it reverses by lowering corruption; a blessing already granted is never revoked.
  **Refusal only — no curse:** a lasting curse would need a second persisted set on the player
  and a second authority for the same fact, which is exactly what 41.5A/B's carried traps forbid.

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

The playbook splits each guild into recruitment/identity and senior-rank/finale sessions, plus
shared hub and integration passes. Membership/rank/refusal reuses story flags; public attitude
remains faction reputation. There is no parallel guild progression or quest runtime.

### Phase 42.5 — The Crimson Cult `[F/C]`

The Crimson Prophet "built an empire of worshippers" (LORE) — currently that's only a
boss fight at the end of Sunspire Dominion, with no in-world presence backing it.

- **A hostile `FactionResource` for the Crimson Cult** with outpost/hub presence
  seeded in Sunspire Dominion (ties to Phase 44D).
- **Cult archetypes** (zealots, inquisitors) authored on the Phase 34 matrix — data,
  no new AI code.
- **An infiltration questline** (a Phase 41D choice/branch arc) letting the player
  pose as a convert ahead of the Crimson Prophet confrontation (47D), rewarding
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

### Phase 44 — Alpha Content Pass: all five realms blocked out `[C]`

Greybox + first-pass content for the **whole game's shape**: Ember Crown (Iron King),
Frostfang Reach (Storm Tyrant), Ashen Wilds (Beast Lord), Sunspire Dominion (Crimson
Prophet) and the **Pale Concord** (Hollow Queen) — each with its hub(s), key POIs,
encounter sets, the resident fallen Flamebearer's Alpha boss encounter, and the
main-quest spine connecting them. Rough but **complete in extent** — every realm,
every boss, every guild reachable.

**The Pale Concord is the odd one out and is built last.** LORE keeps it off every map: the
Hollow Queen hid a kingdom from death and from history, so the realm is *found*, not travelled
to — there is no fast-travel node or neighbour link advertising it until Act II's discovery beat
(47E) opens the way. Its fiction is a stalled realm: a sky fixed at dusk, nothing ripening,
nothing rotting, nobody able to die. That hands 44E/44.5 a concrete brief rather than a blank
region — **candidate rules, none of them built yet:** no natural health regeneration inside its
bounds, a `WorldClock` that does not advance, corpses that never despawn, and NPCs whose only
request is to be released. Each is a real system change (`StatsComponent`, `WorldClock`, the
despawn path), so they are scoped decisions for 44E, not assumptions.

Phase 44 follows the generated modern world pipeline: atlas/budget first, then macro specification,
routes/intentional wilderness, settlements/POIs, population/ecology/boss territory, and full
world-quality/performance closure. Ember Crown and Frostfang are reconciled without rewriting their
closed geography. New realms begin from `tools/region_spec_template.py`; generated region data is
never hand-edited.

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

### Phase 45 — TRUE Feature-Complete Audit & Freeze `[F/P]`

- Build a feature-completeness matrix against DESIGN, LORE, live code/data and the roadmap, covering
  every player verb, system, UI/audio/cinematic and persistence owner with actual evidence.
- **Close the confirmed physical-ranged-combat hole before G2.** The current tree has no bow item,
  firing path or ranged weapon component; the old Phase 51 note claiming a placeholder bow exists is
  stale. Phase 45 builds/proves the bow mechanic; Phase 51 authors catalogue breadth only.
- Run cross-system, sequence-break, save/load and five-realm performance matrices; burn all
  Blocker/Critical defects and any High that invalidates a gate row.
- **Feature freeze is signed only after the matrix has zero feature holes.** Later exceptions require
  explicit impact/rollback/test approval and invalidate affected evidence until rerun.

> **🚩 Gate G2 — Alpha / Feature Complete.** Every mechanic in the shipped game
> exists and works together: corruption, races, companions, dragons, bosses,
> housing, economy, mounts, cutscenes, all quest types, all five realms reachable.
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

The bulk of the game (LORE): travel the four known realms and **find the fifth**, acquire
divine relics, build alliances, defeat the fallen Flamebearers — the Iron King (Ember Crown),
the Storm Tyrant (Frostfang Reach), the Beast Lord (Ashen Wilds), the Crimson Prophet
(Sunspire Dominion) and the Hollow Queen (the Pale Concord), plus seeds of the Ashen Knight
rivalry. Each realm = its questline + boss + relic + corruption beat + guild ties; the Pale
Concord adds a discovery beat, since nothing in the world admits it exists.

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

### Phase 50 — Side Content, Activities & Wilderness Pacing `[C]`

Measured, realm-specific side quests, lairs, events, exploration rewards, environmental stories,
guild bounties, companion loyalty quests, collectibles and ambient behavior. A distribution matrix
derives category ranges and discovery spacing from each realm's size/travel rhythm. Phase 44's
intentional empty cells remain protected. Lairs use existing rooms/combat/loot/doors; no puzzle,
trap or vault framework returns.

### Phase 50.5 — Lore Codex & Compendium `[C/P]`

Phase 45A decides whether the compendium is launch scope; if retained, Phase 45E builds/proves its
schema, unlock/persistence authority, panel foundation, validation and debug seam **before G2**.
Phase 50.5 then authors the catalogue, collectible/story placements, localized presentation and
coverage acceptance, distinct from the combat Bestiary. If the feature is cut at G2, the phase is
skipped and lore books remain ordinary readable/quest content—no stub.

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

**Live catalogue audit (planning overhaul):** 63 item templates exist; nine equipment slots work,
but authored gear covers only MainHand, Head, Chest and Ring. OffHand, Hands, Legs, Feet and Amulet
are empty. The old note claiming a placeholder bow exists is stale. Phase 45 owns the physical
ranged mechanic and proof bow; Phase 51 owns catalogue breadth and placement.

### Phase 51.5 — Enchanting & Relic Socketing `[F/C]`

Not LORE-mandated — an optional itemization deepener beyond the existing affix system.
Cut cleanly if it doesn't clear playtest; flagged here so it isn't lost.

- **`SocketComponent`** on rare+ equippables + an `EnchantResource` consumable that
  slots in for a stat bonus, extending `EquipmentComponent` rather than replacing
  affixes.
- Authored as a content tail on top of Phase 51's catalogue.

### Phase 52 — Full Audio & Music Production `[P]`

The complete adaptive score (per realm/boss/theme), full SFX coverage, ambience
for every region/weather/time, and approved dialogue/VO integration through `AudioDirector`.
A coverage census distinguishes real assets from the procedural fallback. VO scope, recording
language(s), casting and text-only coverage are an explicit decision gate before recording.

### Phase 53 — Art Complete & World Beautification `[P]`

Final environment art across all five realms, character/creature/boss final
models, the dying-world art direction fully realized (light fading, ash, ember
glow), VFX polish, set dressing, and the visual cohesion pass. No greybox remains.

### Phase 53.5 — Photo Mode `[P]`

Not LORE-mandated — a polish-tier nicety, not a gap. Pairs naturally with the Phase 53
art pass.

- A pause-state free camera (reuses `GameState.Paused`), hide-HUD toggle, and a few
  dying-world-themed filters matched to Phase 53's art direction.

### Phase 54 — Accessibility & Input `[F/P]`

Complete input remapping, subtitle/caption coverage, aim/lock-on assists and scalable difficulty,
then run an end-to-end accessibility campaign. This extends rather than recreates shipped text/UI
scale, high contrast, color-vision adaptation, reduced motion, controller bindings and device-aware
prompts. Phase 45 must freeze the launch-required remapping/difficulty/assist foundations before G2;
Phase 54 completes their UX/coverage/acceptance. Hardware verification is conditional on approved targets.

### Phase 55 — G3 Content-Complete Acceptance Campaign `[C/P]`

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

Approve target hardware/resolution/quality first, then derive—not invent—frame-time/frame-pacing,
draw/primitive, RAM/VRAM, region build/load and shader first-use budgets from repeatable measurements.
Use Embervale's existing world/performance harnesses across representative and worst-case scenes.

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

Choose launch locales from audience/budget/font/LQA/support evidence, then complete extraction,
translation import, font/glyph/shaping coverage, pseudo-localized overflow testing and native
in-context LQA for those approved languages. No language or script is assumed before the decision
gate. Phase 24's `Loc` discipline keeps the pipeline bounded.

### Phase 61 — Platform Compliance & Storefront `[P]`

First approve the target OS/store/platform/service matrix. Then build reproducible signed artifacts,
implement only required store/cloud/achievement/controller/compliance work, produce rights-cleared
store assets/legal/credits, and rehearse packaging/submission. Console and other platform work stays
conditional until chosen.

### Phase 62 — Release Candidate & Gold Master `[P]`

Code/content lock; RC build series; final cert pass; the day-one patch plan; gold
master sign-off against the G4 bar (**zero known crash/blocker bugs**).

> **🚩 Gate G4 — Release Candidate.** A gold-master-quality build, certified on
> target platforms, zero blockers, day-one patch staged. Ready to ship.

---

## 7. Stage F — Launch (→ G5)

### Phase 63 — Launch `[P]`

Ship the exact signed artifacts to the explicitly approved targets. Verify independent acquisition/
install/launch, stage the day-one-patch decision against live thresholds, keep save-compatible
rollback ready, and operate monitoring/support/escalation.

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

These are separately approved initiatives, not launch promises. New Game+ starts with an explicit
carry/reset/rederive table; every content drop proves launch-save compatibility and base behavior
when absent.

### Phase 66 — Expansion / DLC Framework `[F/C]`

Namespaced pack manifests, entitlement/offline behavior, isolated loading/validation, pack-owned save
migrations, missing-pack recovery and the modern new-realm production seam. A small test pack proves
base-game isolation; hypothetical expansion fiction is deliberately not designed here.

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
| B — Vertical Slice | G1 | 29–33 | ⏳ All phases built (29–32 ✅, 33A–33E ✅); **G1 needs a maintainer play-through + one export** |
| C — Alpha / Feature Complete | G2 | 34–45 | ⏳ In progress (**34–39.5, 41 and 41.5 complete; 40/40.5 struck**; next: 42A) |
| D — Beta / Content Complete | G3 | 46–55 | ⬜ Planned |
| E — Release Candidate | G4 | 56–62 | ⬜ Planned |
| F — Launch | G5 | 63 | ⬜ Planned |
| G — Live / Post-launch | G6 | 64–66 | ⬜ Planned |

**Where we are.** `docs/NOW.md` is authoritative: Stage C is active and **42A is next**. Phases
40/40.5 are struck, not deferred. The out-of-band world-geography and world-quality passes are
closed and are not Phase 44; future realm work uses their generated authoring/quality pipeline.

| Phase | | What it delivered |
| --- | --- | --- |
| 22–28 + 25.5 | ✅ | Production bible, corruption, meta-shell + localization, region streaming/map/fast travel, the Stage A hardening pass, races & creation, the Ember Crown, the Iron King boss slice |
| 29 · 29.5 | ✅ | Combat feel (hit-stop, parry/riposte, dodge i-frames, lock-on); spellcraft — cast archetypes, school identities, mastery, combos, the fading Weave, enemy casters, the magic UI |
| 30 · 30.5 · 31 · 32 | ✅ | Models & visual identity (`ART_STYLE.md`); the UI/HUD overhaul and the `UiPanel` framework; audio foundations; the companion system, with Kael authored in full |
| 33 | ⏳ | Vertical slice assembly — 33A–33C ✅, 33D/33E **built but never played** |
| 34 | ✅ | The enemy & creature roster (34A–34G): AI profiles, 26 creatures as data, per-school resistances, every magic school's on-hit identity, Ashen corruption variants, the bestiary |
| 34.5 | ✅ | The Frostfang clans (34.5A–34.5C): `faction.frostfang_clans` and the clan hold — Frostfang's first settlement; raider/beast-tamer/shaman archetypes that stay neutral until provoked; and a three-link rank chain with a betrayal branch. Encounters gained a region filter and quests gained a faction reward along the way |
| 35 | ✅ | Dragons (35A–35G) — hit zones, flight, breath weapons, lairs, a creature that talks — sub-phase detail in [Phase 35 — what shipped, sub-phase by sub-phase](#phase-35--what-shipped-sub-phase-by-sub-phase) below |
| 36 | ✅ | The boss kit (36A–36E): a boss is authored data now (`data/bosses/*.tres` — phases, enrage, granted spells, telegraph colours), the Iron King lost his bespoke factory so there is **one** path through the pipeline, wind-ups are telegraphed by a model-independent ground ring and interruptible by a stagger **for every actor including the player**, phases summon capped add waves, an arena binds its own spawn points and phase reactions declaratively in its `.tscn`, and each boss's intro lock, defeat slow-mo, guaranteed reward and corruption-choice dialogue come from its own resource |
| 37 | ✅ | Housing (37A–37D): a holding can be bought and/or earned, ownership persists and registers a fast-travel node; it has a stash (the game's **first two-way container**); you can craft kits and set stations and decoration down in its yard (the game's **first world-editing verb**, with a ghost that names *which* refusal applies); and display stands show off Epic-or-better trophies. Persistence came free all four times — ownership is a service, the stash and the stands **are** inventories keyed by `PersistentId`, and placed props ride `PersistentSpawnDirector`. The Ashfall Cottage is authored end to end as the one playable property **37E (out of band, 2026-08-10)** rebuilt the holding entirely: `compose_building.py --hollow` makes a house with per-wall colliders and an open doorway, so the Ashfall Cottage is now an **enterable, furnished home in its own cell** (`ember_crown.ashfall_homestead`) with a workshop, a garden and an ownership-gated free bed |
| 37.5 | ✅ | **The UI overhaul** (37.5A–G) — an original AAA-fantasy interface language across every screen — sub-phase detail in [Phase 37.5 — what shipped, sub-phase by sub-phase](#phase-375--what-shipped-sub-phase-by-sub-phase) below |
| 39.5 | ✅ | **World map & location intelligence — CLOSED at 39.5C; there is no 39.5D.** The map reads authoritative world data instead of the three things that happened to have coordinates. ⚠️ **A location's position is its node's transform in a cell scene, never an authored coordinate**, and `--validate` checks that seam in both directions. Author with `tools/gen_map_locations.py`. **39.5B was the player HUD**: a minimap (none existed), one tracked-quest authority replacing two independent first-active scans, and the visibility logic `GameHud` had never had. ⚠️ **Its audit found ~60 of the brief's 80 sections already satisfied** — the finding was that the HUD was mature, not broken. ✅ **39.5B also closed the harness gap** with `--hudshots`, which renders the HUD to PNG and found four already-shipped defects on its first run. ⚠️ **It covers the HUD, not panels** — the map screen still cannot be captured, and that is where 39.5A's three defects were. **39.5C added panel capture (`--panelshots`), the map's label placer and quest destinations**, and measured all eight deferred conditions rather than guessing — each carries a runnable condition *and the number it measured at*; see [`docs/playbook/phase-39_5.md`](playbook/phase-39_5.md) |
| ~~40~~ | ❌ | **Survival & Needs — NOT WANTED, struck 2026-08-12** (maintainer direction). No durability/repair, hunger, thirst, temperature or encumbrance, and no condition revives them. Rest already exists as a *sink* (the inn, the home bed) and is untouched; food items stay instant-heal consumables with a `food` trade tag. The cut deleted the two stubs the decision had been holding — `CraftingStationType.Cooking` and `InventoryComponent.MaxWeight`/`IsOverEncumbered`, the latter unread since Phase 5 — and settled seven "pending 40A" pointers. ⚠️ **Do not reach for wear as a Phase 56 economy fix.** See [`docs/playbook/phase-40.md`](playbook/phase-40.md) |
| ~~40.5~~ | ❌ | **Dungeon & Puzzle Framework — NOT WANTED, struck 2026-08-12**, whole phase. Nothing existed, so nothing was removed. ⚠️ **The trap arm was offered as a partial keep and declined.** Two phases owe their own answer: **Phase 50** authors dungeons as rooms with encounters and loot on existing tooling, and **Phase 51E** has a guardian but no trial. See [`docs/playbook/phase-40_5.md`](playbook/phase-40_5.md) |
| 41 | ✅ | **Quest authoring at scale — CLOSED at 41F.** Six types join `Kill`/`Collect` on the one `QuestLogComponent.Advance` choke point: `Reach`/`Talk` (41A), `Escort`/`Defend` (41B), `Interact`/`Stealth` (41C). 41B gave the log its first losing end state, `QuestStatus.Failed`, **retakeable by design**; 41C gave the quest a **deadline** — a quest-level `TimeLimitSeconds`, deliberately not an objective type; 41D gave objectives a **branch gate** (`RequiredFlagId`/`ForbiddenFlagId`) and quests **ordering** (`SequentialObjectives`), **with no new save state** — the branch is re-derived from a story flag that already persisted. **41E extends that same persisted flag spine with `QuestResource.CompletionFlagId`: a quest can open Frostfang or remove a placed actor without a second state store. 41F makes that work exercisable with `quest start/advance/complete/reset` and catches self-locked completion-flag branches in `validate-all`.** ⚠️ **The escortee is a COMPANION because nothing in this build can damage an NPC.** ⚠️ **Stealth rides `EnemyStateChangedEvent`, not `EnemyAlertedEvent`, which is published only when the AI profile's `AlertRadius > 0`** — an ambusher authors 0, so the obvious rule would never fire against the enemies built to catch you unawares. ⚠️ **A seeded state is invisible to every rule written for an earned one** (41C), and ⚠️ **an INERT objective is invisible to every rule written for the other two** (41D — six surfaces, three of which would have aimed the player down the branch they declined). ⚠️ **A rendered frame keeps catching what the build, the tests and the validator pass over.** See [`docs/playbook/phase-41.md`](playbook/phase-41.md) |
| 38 | ✅ | **Economy, vendors & services — closed at 38V.** All twenty-two sub-phases (38A–38V) including 38G; nothing parked, five briefed services deliberately struck. Mechanism in [`ARCHITECTURE.md`](ARCHITECTURE.md) §2.6m, intent + the Phase 56 balance handoff in [`DESIGN.md`](DESIGN.md) §6/§6.1, and the rules are re-provable with `python tools/negative_tests.py` (42 cases). Sub-phase detail in [Phase 38 — what shipped, sub-phase by sub-phase](#phase-38--what-shipped-sub-phase-by-sub-phase) below (that list stops at 38O; **`docs/playbook/phase-38.md` is the current one**) |
| Art | ✅ | **The Quaternius standardisation** (out of band, maintainer direction): the art set is now one CC0 artist. 401 models vendored at `assets/library/` behind a `.gdignore`, 18 props re-sourced keeping their filenames and boxes, and **29 archetypes that greyboxed as tinted capsules got rigged animated bodies** — Phase 35's dragons finally have one. Every model in the game is CC0 and the project owes **no attribution**; the `prp_tome_stand` release blocker is gone. Policy in `ASSET_POLICY.md` §0 |


---

### Phase 38 — what shipped, sub-phase by sub-phase

*Split out of the phase table in the agent-ergonomics pass: this was one 15,241-character table cell, and every `grep` for a phase word paid for all of it.*

- **Economy, vendors & services.**

- 38A ✅ trade exists: a `ShopResource` carries a merchant's wares and its buy/sell spread, a `VendorComponent` opens the window, and one Godot-free `ShopPricing` owns every price — rounding up on buy so a 1-gold trinket can never round to free, down on sell so a payout can never go negative, and clamped both ways so `SellPrice <= BuyPrice` holds for *any* authored spread. That last one is not theory: an inverted spread is an infinite gold loop, and `--validate` now rejects one (proven by authoring it and watching the build fail). Prices read `ItemInstance.Value`, so rarity and affixes already price rolled loot with no second table. Quest items and gold are unsellable — a sold quest object silently strands a Collect objective. ⚠️ **The three Ember Crown vendors are still stub conversations.** `EntityNode.GetComponent<T>` returns the *first* child match, so a `VendorComponent` behind their `DialogueComponent` would never fire; whether trade replaces the conversation or hangs off an `OpenShop` dialogue effect is **38E's** call. Until then `shop <id>` in the F1 console opens any shop, publishing the same event a placed vendor would.

- 38B ✅ stock has depth. Three kinds, and the numbers say which with no mode enum: `Quantity = 0` is an unlimited row, above it is finite and depletes, and a `LeveledTable` is a `LootTable` rolled at each restock at a quality that climbs with the player's level — the **first player-level-driven scaling in the game**, despite `LootRarity`'s comment having claimed for phases that quality came partly from "enemy level". It moves rarity and affixes, never *what* a merchant deals in. `WorldClock` gained a `Day`, because it had no notion of a date at all: `TimeOfDay` wraps through `PosMod` and nothing counted the wraps, so "three days later" was inexpressible. `ShopStockService` holds and persists what each shop has left; **restock is evaluated when a shop is opened**, not on a tick — nothing can observe the difference, and `WorldEventDirector` is the counter-example that ticks cooldowns every frame and then loses them on reload because it is not `ISaveable`. Three bugs the tests and the validator caught rather than play-testing: day arithmetic overflowing on a never-stocked shop's `int.MinValue` stamp (the test failed on its first run), a clock *behind* the stamp freezing a shop forever after a quickload, and a finite row authored with no restock clock.

- 38C ✅ standing finally changes a number. A merchant prices by faction: a surcharge across the hostile half of the seven-step ramp down to 15% off at Allied, and a faction the player is *hostile* to will not deal at all — reusing each faction's own authored `HostileThreshold` rather than inventing a second notion of hostile in the economy. That is the **first reputation tier read in the game**: `ReputationComponent` shipped in Phase 16 with exactly two behavioural readers, both asking the same boolean. Only the buy side moves, deliberately — with both of `ShopPricing`'s clamps in play a generous sell fraction converges on `sell == buy`, which is frictionless churn rather than an exploit. Gold has real sinks now: a merchant's **purse** runs dry and refills on 38B's restock clock, so a field of corpses cannot be fenced in one visit, and **fast travel costs gold** — free to a holding you own, which is what makes the sink read as a choice rather than a toll booth. `DESIGN.md` §6 carries the authoritative sink table, as its own house rule requires. Two process notes worth keeping: the travel fee's first draft resolved the active region two different ways, so the price shown would have differed from the price charged (caught by a `--play` run, and the premise behind it — that `RegionStreamer` was unregistered — turned out to be wrong outright); and the purse arithmetic had to move out of the Godot `Node` into `ShopStock` before any test could reach it.

- 38D ✅ four paid services, one component. `ServiceResource` + `ServiceKind` + one `ServiceComponent` branching the way `WorldEventDirector` does, the pure half in `ServiceRules`, and the price on `ShopPricing.ServicePrice` so a service and a shop of the same faction move on one discount ramp. The trainer is **the first caller `CraftingComponent.Learn` has ever had** — it had none from Phase 15, which is why `GameIds.Recipes.Starting` was the entire reachability guarantee and how `recipe.leather_vest` rotted unreachable for twenty phases; `recipe.drakescale_mail` moved out of that array to be taught, since it only ever sat there because nothing could teach a recipe. `--validate` now checks reachability as a **union** and rejects the overlap too. The bank is 37B's storage with the property gate removed — an `InventoryComponent` on a prop with a `PersistentId` *is* the vault, so no panel and no save code. The inn moves the clock and refills every resource; the stablemaster sells a mount that Phase 39A will read out of a story flag. ⚠️ **Repair was deferred to 40A, deliberately**: no durability concept exists anywhere, and 40B's rule is that cut systems leave no stub, so there is no `Repair` kind at all. ✅ **Resolved 2026-08-12 — the answer was no**: Phase 40 was struck as *not wanted* and durability is CUT, so the absence 38D shipped is now permanent and cost nothing to make so. 38D also **resolved the §6 contradiction 38C left standing** — a trainer sells access, never a rank, so gold reaches skill points only through levelling. Two traps the phase records: `SetTimeOfDay` needs `RestHour + 24` or a rest rewinds the hour and freezes 38B's restock clock, and which kinds require an `UnlockFlagId` is a validator rule because every wrong pairing is well-formed data and a broken economy. **

- 38E** ✅ closed the phase by making the economy reachable at all: `DialogueEffect.OpenShop` (ordinal 9) lets a trade choice publish the existing `ShopOpenedEvent`, so Aldreth, Bryn and Mirela became real merchants **without losing the quest conversations** two of them carry. That was the one-interactable decision reserved since 38A, and the effect won over replacing the component for a forward reason as much as a content one — a merchant who is a person can carry hours, a haggle, a contract and a rumour, and a `VendorComponent` on a crate cannot. It also validated a shop id for the first time ever: `.tscn` is not scanned, so a mistyped `ShopId` has always been silent, while an `EffectArg` sits in a `.tres` the validator reads. Two authored shops (`shop.ember_crown.smith`, `shop.ember_crown.apothecary`) and Aldreth's real conversation replaced the hard-coded stub. ⚠️ An `OpenShop` choice must leave `Goto` empty or the conversation waits behind the shop window and returns when it closes — enforced by a new rule. No new C# file: one enum member, one switch case, and data. **38F–

- 38J ✅ turned shops into merchants.** Trade tags gave each one a trade and a specialty (a premium paid and a keener price asked, both from a closed vocabulary the validator holds authored data to); a merchant now **saturates** on what you dump on her, pricing a stack unit by unit so tidy selling is not punished; shelves gate behind **standing, a story flag or an investment stake**, and a stake is the phase's second permanent gold sink; and shops keep **hours** while travelling merchants keep a **calendar** — presence being a pure function of the day, so it needs no save state at all. **

- 38K ✅ built the Embermarket** — a market district one street south of the square, and then a second pass rebuilt it, because the first was structurally bland: six identical tents on six identical crates in two mirrored rows, two materials, no emission and nobody in 2,704 m². It is now a crossroads of ten stalls with a bell tower, a plaza, braziers, ember VFX and a notice board. ⚠️ Two engine facts came out of it and are worth more than the district: `CharacterBody3D` has **no step-up**, so a 0.3 m kerb is an invisible wall the navmesh happily paths NPCs over — verticality comes from props, never from terrain; and a `.tscn` reads fine while looking wrong, so a throwaway harness that instantiates a cell and renders it (`tools/market_shots.gd`) found more in one run than three readings of the file. **

- 38L ✅ filled it with twelve specialist merchants** — but the catalogue had to come first: the game had **26 items**, and `weapon`, `gem`, `jewelry`, `potion`, `herb` and `relic` had **one member each**, so twelve specialists would have been twelve merchants selling the same iron ingot and 38F's whole premise would have been decorative. 23 items and five new trade tags landed first, then ten merchants reached by conversation and two travelling ones. ⚠️ **A traveller cannot use the dialogue route**: `ShopOpen`/`ShopClosed` test hours and never presence, and only `VendorComponent` hides an away merchant — so the two caravanners carry components and pay the unvalidated-`ShopId` price for it. Twelve shops, ten conversations and 23 items landed with **zero new validator rules**, which is the 38A–38J battery earning its keep. **

- 38M ✅ put a price on the road.** The Crossway toll charges every **portal** crossing between the Ember Crown and the Frostfang Reach, and it is charged in `GameBootstrap.OnRegionTransitionRequested` — the one function the portal's event and the `region` dev command both arrive at, because 38C already learned that gating one caller leaves the other a free ride. ⚠️ **Fast travel is deliberately untolled**: a jump already pays `TravelFee.CrossRegionFee`, and two charges for one journey is the toll-booth feel 38C designed against — so at 25 gold the road *undercuts* the map and the wardens compete with fast travel rather than taxing it. A permit exempts forever and a bribe covers one crossing, and the two are **the same verb**: one new `ServiceKind.Passage` and one branch on 38D's `ServiceComponent` inherited the price, the standing discount, the hostile refusal and the whole prompt battery, so the permit and the bribe differ in three numbers. ⚠️ The bribe cannot record itself in `UnlockFlagId` — that field doubles as the already-bought receipt, so a bribe stored there would refuse to sell a second time, which is a permit; it grants a `GrantedFlagId` the crossing **consumes** instead, because a permanent pass sold under the permit's price would delete the sink after one purchase. Its standing cost needed no new currency: −8 with the villagers is charged again at every counter in town through 38C's ramp. Three validator rules, negative-tested both ways, and the reachability one is the only thing standing between a typo and an uncrossable road — story flags have no database, and `.tscn` is still not scanned. One new C# file (`TollFee`). **

- 38M2 ✅ built the gate the toll is about**, and moved the crossing to it: `RegionResource.PortalPoint` (empty = the old spawn-relative placement, so Frostfang needed no edit) puts the Ember Crown's door at the far side of the Crossway Post, so the wardens are people you walk past rather than people standing next to your bed. ⚠️ **The `rts` model pack is roughly 1/6 scale** — a "fortress gate" is 1.94 × 0.67 m out of the box, a gateway two feet high — caught by measuring three candidates against a 1.8 m reference before authoring anything around them, and adapted through `nodes/root_scale` in the `.import` rather than a Blender round-trip. The gate is a **gap between two closed palisades** (inner edges at x = ±3.94, an 8 m road through a 7.88 m gap), so nothing has to animate for the player to pass. **Two engine changes landed alongside by maintainer direction:** a region now loads **whole** — every cell resident on entry, no distance test and no unload during play, with `StreamDecision`, its hysteresis tests, `UnloadMargin` and `RegionCellResource.LoadRadius` all deleted with the rule (⚠️ both *regions* cannot be resident together: Frostfang's roosts share coordinate space with the Ember Crown's arena and northern wilds, which is a Phase 44 world-layout question) — and the spawn caps went to **15**, ambient encounters and the sandbox camp alike, the latter needing its spawn radius widened with it because the director seeds its whole population at once (**both back to 5 in 38N1** — fifteen read as too much pressure; the widened radius stayed). **

- 38N1 ✅ built the realm's first production settlement.** The Emberdeep Mine is a **source and a sink** rather than two more stalls: Bregan Holt sells ore at the lowest markup in the realm (1.15) and barely buys, while Marta Quill pays the most for food anywhere (0.62, `food` specialty) and ⚠️ **deliberately refuses ore** — letting the sink buy everything is precisely what would flatten two settlements back into one. Two items opened the `ore` tag past its single member first, the same catalogue-before-content order 38L had to learn. The sub-phase also delivered the arbitrage report the roadmap asked for, behind an `economy` console command **and** a new `--economy` headless flag (the F1 console cannot be driven remotely, so a console-only report would have shipped unexercised). ⚠️ **And the report proved that arbitrage is currently impossible**: `ShopPricing` clamps every markup to `>= 1` and every sell fraction to `<= 1`, so `sell <= value <= buy` holds at every shop and a carry between two merchants is always a loss — 48 goods, every margin negative. That is the strongest argument yet for **38G**, whose regional demand moves an item's *value* per settlement and is the only thing that can turn those positive. Two art findings: the `rts` pack's 1/6 scale held a second time (raw heights 1.17 m, 0.39 m, 0.42 m, 0.25 m), and ⚠️ **`npc_merchant_f` turned out to be modern dress — white t-shirt and trainers — and it shipped in 38L**, caught by rendering it at eye level before using it again. **

- 38N2 ✅ finished the pair.** Tarn's Landing is the mine's mirror — a fishing hamlet on the western water whose curer sells fish cheaper than anyone and whose chandler pays over the odds for rope, cloth and iron while ⚠️ **refusing fish outright**, and says so in his own words rather than through a greyed-out row. Seven items landed first (the `fish` tag had two members), so the realm now has **three markets** and `--economy` reads as a network: fish cheapest at the Landing and best-paid at the mine's company store, copper ore cheapest at the mine and best-paid at the Landing. ⚠️ **The water is a decal, not a volume** — a translucent plane with no collider over a near-black lakebed, because swimming does not exist and a real volume would be an invisible wall or a hole; the first pass omitted the lakebed and the tarn read as flat grey ground, since **a transparent surface needs something dark to be transparent against**. ⚠️ **And the open-web character pull returned a file that was already on disk**: the best body of the search was byte-identical to `assets/library/women/adventurer.glb`, vendored and unadapted since the 38L migration, so the "library is out of medieval bodies" constraint was a bookkeeping error rather than an art problem. Four of the six candidates were rejected on sight (modern dress, a punk with a chainsaw, an ornament that is not a person, a four-bone rig), which is the render gate earning its keep for the third sub-phase running — and `npc_merchant_f`, the t-shirt-and-trainers body that shipped in 38L, is now retired from the game. Next: **38O** — done, below

- 38O ✅ gave the realm its FOURTH market and its first prohibition.** Hollowreach is a smugglers' wharf down the water from Tarn's Landing, and the two fences on it are the only merchants in Embervale who will touch `contraband` — which is ⚠️ **the one trade tag that fails CLOSED**. Every other tag is a filter a shop may opt out of; this one is a door a shop must opt *in* to, and it overrides an item's other tags, so a stolen signet is refused by the jeweller who deals in jewellery. The whole inversion is **one branch in `TradeTags.Accepts`**, which the vendor window, the sale and `EconomyReport` already shared. Selling through a fence costs **two factions at once** — standing gained with the outlaws, lost with the villagers, once per sale rather than per unit — and the outlaw half needed no new machinery at all: `EnemyAIComponent` already reads `IsHostile(faction.outlaws)`, so six fenced sales stop the bandits attacking on sight. ⚠️ **A fence cannot author the faction she answers to**: `faction.outlaws` starts at −30, below its own hostile threshold, so an outlaw-factioned vendor would be hidden and refuse to trade from the first minute of a new game. Carrying the goods is the risk — the Crossway wardens search you and impound what they find, recoverable for a **per-unit fine**, and `--validate` refuses a realm that can seize with nowhere to redeem, because a permanent seizure is theft rather than a cost the player can price. The cell needed **no new art**: every model was adopted for an earlier district and re-read, and the library is now genuinely out of unadopted CC0 medieval bodies. ⚠️ **A collider copied out of a sibling cell was wrong** — `wilds_north` gives `prp_ruin_pillar` a lying-down box and it stands up here, which one render caught and three readings of the file did not. Next: **38P** (consignment house + appraiser)

### Phase 37.5 — what shipped, sub-phase by sub-phase

*Split out of the phase table in the agent-ergonomics pass: this was one 7,424-character table cell, and every `grep` for a phase word paid for all of it.*

- **The UI overhaul** (37.5A–G) — an original AAA-fantasy interface language across every screen.

- 37.5A ✅ the foundation: three vendored SIL OFL typefaces (Cinzel for carved titles, EB Garamond for prose, Inter for the 12 px floor and tabular stat columns) where the game had shipped Godot's default everywhere; a screen-space grain shader and an engraved brass double-rule replacing the flat 1 px box; three surface depths (`WellBg`/`PanelBg`/`CardBg`) so a control reads as cut *into* a panel or sat *on* it; and the semantic ramps the UI had been going without — magic school, quest state, disposition, and a **retuned rarity ramp** (the old one was stock saturated MMO green/blue/purple, which broke UI_STYLE §2's saturation rule) now pinned by tests to climb in luminance and stay separable, so rarity survives greyscale and a colourblind player. Also the three magic motifs (rune circle, sigil drift, ink shimmer) as shaders wired to one reduced-motion uniform. Nothing user-visible changed shape — this phase only laid the tokens the rest consume.

- 37.5B ✅ de-drift and the HUD: the plan's "~104 stray styleboxes" turned out to be mostly the **alpha-fade idiom** and 3D world colours; genuine UI drift was ~12 sites, and auditing them properly is what found the two real defects — **seven hand-rolled scrims at four values, six of them blue-black** against the style guide's first rule, and three off-scale font sizes. It also fixed a bug 37.5A shipped: a **second school-colour ramp** in `UiTheme` competing with the `SpellSchools.Color` that had tinted every projectile since Phase 12, which would have made a firebolt one orange in flight and another in the spellbook. `BossFrame` and `Nameplate` split out of `GameHud` (975 → 812) owning their own events and update loops; the boss frame spends the HUD's whole ornament budget (corner brass, phase pips); and the nameplate finally shows **disposition**, which the HUD had never displayed even though neutral-until-provoked factions arrived in Phase 34.5.

- 37.5C ✅ the item screens: the character sheet's Gear tab became three columns (worn slots | backpack grid | detail pane) instead of one scrolling text list, and storage and crafting took the same slot/card vocabulary. ⚠️ **The plan assumed item icons existed and they do not** — `ItemResource.Icon` has been on the resource since Phase 5 with 0 of 26 items setting it and nothing reading it, so a literal icon grid would have been 26 empty boxes; slots carry a category glyph instead (silhouette = category, colour = rarity, frame width = tier) and prefer a real icon the moment one is authored. The comparison logic that tells the player whether a pickup is an upgrade is pure and takes plain values rather than `ItemInstance`, specifically so the test project (which forbids Godot objects) can pin it — a sign error there is invisible and plausible. Running it caught what the build and tests could not: `FocusNeighbor*` needs a node already in the tree, so the first grid-navigation pass threw on every cell every frame while working perfectly under a mouse.

- 37.5C2 ✅ the stat block: added after the maintainer spotted that Progression and Perks were in no sub-phase at all. It turned out **the game had never displayed a single player stat** — `InventoryPanel` held no `StatsComponent`, so Armor, the power stats, crit and all six 34E resistances were shown nowhere, which had also left 37.5C's upgrade comparison half-blind. Defence rows now carry the *derived* mitigation percentage straight from `CombatMath.ArmorMultiplier`, because a raw Armor number on a hyperbolic curve tells the player nothing; a test asserting the reduction never reads as immunity was narrowed rather than clamped, since the underflow lives in the shared combat formula and clamping would have made the screen disagree with combat. A coverage test now fails the build if a new `StatType` is displayed nowhere.

- 37.5D ✅ the spellbook: magic left the character sheet's fourth tab for its own screen (`T` for tome), the one surface in the game that runs **cold** — ink-violet vellum behind tarnished silver, spending the whole ornament budget (rune ring, sigil field, title shimmer) that nothing else may touch. It surfaced two things the game had never shown: the **prepared-spell cycle order** that `Q`/`F` walk (a caster with six spells had been cycling blind), and the **reactive combos** that have been live since Phase 29.5D and were discoverable only by noticing a bigger number — both read from the same tables combat resolves, not copies. `ContentValidator` now gates the UI's fonts and shaders, and the obvious form of that check is worthless: `GD.Load<Shader>` returns a non-null Shader for source that does not parse at all, so the guard keys off the uniform list instead, proven by deliberately breaking a shader and watching `--validate` fail.

- 37.5E ✅ the world screens: the journal finally splits Main from Errands off a **real** `QuestResource.IsMainQuest` — the field 37.5B declined to fake with a backwards heuristic — while still refusing a Failed section, because `QuestStatus` has no Failed state and the heading would be a permanently empty promise. The map plots **fast-travel waypoints for the first time** (they have carried a position since Phase 25G and were only ever listed), draws region names on the plot, and makes the player an arrow rather than a dot, since orientation is what makes a map usable while walking; filters deliberately do not re-fit the bounds, because a map that zooms when you hide a pin is unreadable. Quest markers stay out — quests name a template id, not a place. Dialogue became the illuminated page and the bestiary a codex that opens as it fills.

- 37.5F ✅ the shell: the main menu became the game's highest-ornament screen (corner brass and the ink shimmer it shares only with the spellbook), save slots became cards carrying region, level and corruption as structured fields instead of one crammed five-fact string, and notification colour moved from the words to the chip's spine — a `Dim` autosave notice had been dim *text* on a dark chip. ⚠️ **The planned `UiPanel` migration was dropped:** the premise was that these screens hand-roll the modal contract, and all five already call `UiState.Open` and `UiFocus.GrabFirst`, so the only gain left was an open fade against a lifecycle rewrite of the sole path into the game. It also caught the third and fourth instances of the `Panel()`-as-generic-box trap 37.5B predicted — save rows and toasts. This is the one sub-phase whose headline screen is genuinely verified, since `run_project` lands on the main menu.

- 37.5G ✅ accessibility and responsiveness: text scale (independent of UI scale, and floored at the 12 px legibility minimum so the control cannot defeat itself), high contrast, and colour-vision adaptation that **daltonizes rather than simulates** — simulation renders what a colourblind viewer sees, which is a diagnostic view and precisely the wrong thing to ship. It is applied at the token layer because daltonization is not idempotent, and never to world art. The responsiveness audit found that 37.5C and 37.5D had both sized columns against an assumed ~1900 px: a Steam Deck at UI scale 1.5 reports a **853x533 logical** viewport, and they overflowed it by 321 px and 167 px. Layout derives from the viewport now, verified at 854x534 / 1280x800 / 1920x1080 / 3440x1440. **Phase 37.5 is complete (37.5A-G).**

### Phase 35 — what shipped, sub-phase by sub-phase

*Split out of the phase table in the agent-ergonomics pass: this was one 2,264-character table cell, and every `grep` for a phase word paid for all of it.*

- Dragons —

- 35A ✅ the body: hit zones as authored data (`HitZoneResource`, head ×2.0 / tail ×0.6) replacing the one-capsule hurtbox every actor had, entity-level hit dedupe so a swing clipping four zones bills once, an AI turn rate, and jaws/wing/tail arcs.

- 35B ✅ flight: `LocomotionComponent.Flying` owns the vertical axis alone (so the AI steers a flier with the walker's code), tuned on the AI profile, running a time-boxed takeoff→hover→land cycle.

- 35C ✅ breath: a new `SpellDelivery.Cone` resolved by `SpellResolver.Sweep` (sharing `Detonate`'s body), authored as a channeled Fire spell with school identity and a burn — not a bespoke attack, exactly as this roadmap asked.

- 35D ✅ the Wild dragon as a territorial world boss: a roost cell in Frostfang, the AI's first territory leash (it had none — combat chased forever), and a lair spawner that persists so a killed boss stays killed.

- 35E ✅ the Ash dragon — a whole second dragon authored as pure `.tres` against that pipeline (its own creature per LORE, not an `AshenAffliction` reskin), with Necrotic breath and flatter hit zones so it has no safe side; it also exposed and fixed a 35D spawn-placement bug.

- 35F ✅ the Ancient — the first actor in the game that is a boss and a conversation at once: an archetype can now carry a `DialogueId`, a faction the player starts Neutral toward makes it talk-first-fight-if-provoked with no AI code, and a new `LearnSpell` dialogue effect closes the 29.5E Weave-recovery loop the roadmap asked for (favour teaches the Elder Word; killing it and reading its hoard teaches the same word, gated on a new `LairSpawnComponent.DefeatFlagId`). It also paid the debt 35D and 35E both flagged: the roost is now a base scene all three lairs inherit.

- 35G ✅ the Reach became dragon country: `enemy.frost_drake` is a lesser dragon that wanders as an ambient encounter (the named three stay lair-only, or the Ancient's quest would be farmable), a champion Hunt and a spilled-hoard Cache give the Phase 17 event table its first late-game tier, and drakescale mail is the first gear in the game to carry one of 34E's resistances. It also closed a live 34.5B gap — world events had no `RegionIds`, so goblin raids had been rolling in Frostfang Reach. **Phase 35 is complete (35A–35G).**

> ### 🚩 What actually stands between here and Gate G1
>
> Three things, all requiring a maintainer at the keyboard — no remote session can do them:
>
> 1. **Play the slice arc end to end** (`VERTICAL_SLICE_PLAN.md` §5.2). 33D stitched it — a
>    quest-gated boss, Kael named by the elder, a corruption warning before the arena, the
>    Frostfang door held shut until the Iron King falls, a closing card that branches on whether
>    you took his ember — but it has never been played through.
> 2. **Export once** (§8.2). 33E made an exported build *be* the slice: `BuildProfile` strips the
>    sandbox props, dev overlays and cheat keys from any non-development run.
> 3. **Give Kael a model of his own** (§8.5).
>
> Phase 34's own at-keyboard leftovers are listed at the end of its block in
> `docs/playbook/` — the bestiary's save round-trip is the notable one.

**Next up:** 35 (dragons). Remaining audio *production* — real CC0
music/ambience, surface tagging — carries into Phase 52; final art across all five realms is Phase 53.

> This roadmap turns the 21-phase *systems sandbox* into **Embervale, shipped** —
> a hybrid first/third-person open-world fantasy RPG where you battle fallen heroes across four
> dying realms and choose whether to save creation or become its next Ash King.
