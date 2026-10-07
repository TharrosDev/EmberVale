# Embervale — project history

A compact record of what every phase produced, plus the lessons still worth knowing. The per-phase
playbook (`docs/playbook/phase-*.md`, ~8k lines of plans and retrospectives), `SESSION_PLAYBOOK.md`,
`STAGE_A_STATUS.md` and `VERTICAL_SLICE_PLAN.md` were collapsed into this page on 2026-09-27.
**Git history keeps every one of them verbatim**: `git log --all -- docs/playbook/` finds the last
commit that had them, and `git show <commit>:docs/playbook/phase-38.md` reads one.

Current state is [`NOW.md`](NOW.md); what is left is [`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md).

## Phases

Status: **done**, **partial**, **not built** (never started; not needed for the finish plan),
**cut** (maintainer struck it), **out of scope** (personal build, never published).

### Systems (1–21)

| # | Phase | Outcome |
| --- | --- | --- |
| 1 | Core architecture | done: EventBus, ServiceLocator, GameManager, SaveManager, entity/component model |
| 2 | Player controller | done: hybrid first/third-person controller (later split into six components) |
| 3 | Combat framework | done: damage pipeline, hit/hurtboxes, poise, blocking |
| 4 | Enemy AI | done: perception-driven FSM (later profile-driven, one shared brain) |
| 5 | Inventory | done: slot inventory, pickups |
| 6 | Equipment | done: slots and stat bonuses |
| 7 | Loot generation | done: item instances, affixes, loot tables |
| 8 | Progression | done: XP curve, levels, perks |
| 9 | Quest framework | done: Kill/Collect quests and the quest log |
| 10 | Dialogue | done: node graphs, conditions, effects, story flags |
| 11 | NPC schedules | done: world clock and routines |
| 12 | Magic | done: spells and status effects |
| 13 | World systems | done: day/night, weather, encounters |
| 14 | HUD polish | done: `UiTheme`, vitals, crosshair |
| 15 | Crafting | done: recipes, stations, salvage |
| 16 | Factions | done: reputation and hostility |
| 17 | Procedural events | done: raids, caches, hunts |
| 18 | Game UI overhaul | done: `GameHud`, pause menu, toasts, tooltips |
| 19 | Optimization | done: pooling, AI LOD |
| 20 | Deep debugging | done: dev console, invariants, content validator, profiler, repro harness |
| 21 | Content expansion | superseded by the production phases |

### Stage A — First Playable (G0 reached)

| # | Phase | Outcome |
| --- | --- | --- |
| 22 | Production bible and pipeline | done: `IDS.md`, `DESIGN.md`, templates, `GameIds`, validator growth |
| 23 | Corruption | done: meter, tiers, appearance, dialogue and spell gates |
| 24 | Meta-shell and localization | done: title, settings, save slots, `Loc.T` + `strings.csv` |
| 25 | Region streaming and map | done: streamer, portals, cell persistence, map, compass, fast travel |
| 25.5 | Stage A hardening | done (A–P): `SaveKeyPolicy`, anti-hitch, geometry and locale gates |
| 26 | Races and character creation | done: six races as data, a creator |
| 27 | Ember Crown | done: first region, navmesh baked at stream-in, dying-world palette |
| 28 | Iron King | done: brazier-summoned boss, healthbar, absorb/decline corruption beat |

### Stage B — Vertical Slice (G1 not signed off)

| # | Phase | Outcome |
| --- | --- | --- |
| 29 | Combat feel | done: hit-stop, parry/riposte, dodge i-frames, lock-on |
| 29.5 | Spellcraft and the fading Weave | done: cast modes, school identities, mastery, combos, Weave, enemy casters |
| 30 | Models and visual identity | done: `ART_STYLE.md`; the cast later moved to Meshy, the world to Quaternius, and on 2026-10-06 the outdoor world to Meshy too (housing kit kept) |
| 30.5 | UI/HUD overhaul | done: `UiPanel` framework and tokens |
| 31 | Audio foundations | done: buses, music and ambience directors, footsteps |
| 32 | Companions | done: roster, orders, loyalty; Kael authored in full |
| 33 | Slice assembly and onboarding | done: prologue, tutorial, the 8-beat slice to the Iron King. The maintainer played the slice arc on 2026-07-30 with no blockers; the G1 gate itself (a play-through of the current build plus an export) was never signed off |

### Stage C — Feature Complete

| # | Phase | Outcome |
| --- | --- | --- |
| 34 | Enemy roster | done: AI profiles, creatures as data, resistances, Ashen variants, bestiary |
| 34.5 | Frostfang clans | done: clan faction, the Clan Hold, a rank chain with a betrayal branch |
| 35 | Dragons | done: hit zones, flight, breath, lairs, a dragon that talks |
| 36 | Boss framework | done: `BossResource` phases, telegraphs, add waves, arena hooks, defeat data |
| 37 | Housing | done: claim, stash, yard placement, trophy stands; the enterable Ashfall Cottage |
| 37.5 | UI overhaul | done (A–G): one fantasy interface language across every screen |
| 38 | Economy, vendors, services | done (A–V): shops, services, standing prices, tolls, fences, contracts, haggling, supply shocks, 42 negative tests |
| 39 | Mounts and traversal | done |
| 39.5 | Map and location intelligence | done (A–C): `MapLocationResource`, sight discovery, minimap, `--hudshots` / `--panelshots` |
| 40 | Survival and needs | **cut** 2026-08-12: no durability, hunger, thirst, temperature, encumbrance, repair |
| 40.5 | Dungeon and puzzle framework | **cut** 2026-08-12: no puzzle, trap or vault tooling |
| 41 | Quest authoring at scale | done (A–F): eight objective types, deadlines, branches, completion flags, quest dev commands |
| 41.5 | Divine shrines | done (A–C): seven gods' blessings, corruption-gated refusal |
| 42 | Guild questlines | done: 42A–E, G, I, K (guild model, five hubs, rank-one arcs, Dawnwardens command arc, Veiled Archive admission, Emberbound initiation), then 42F/H/J/L finales for the Ash Hunters, Veiled Archive, Iron Syndicate and Emberbound (rank three, three-way verdict) and 42M integration (realm NPCs react to rank three) |
| 42.5 | The Crimson Cult | not built as a faction system; the Crimson Mission and the Crimson Prophet (finish run) carry the story role |
| 43 | Cinematics | not built; `NarrationSequence` cards (opening, closing, ending) serve instead |
| 43.5 | Flamebearer visions | done (finish run): `VisionSequence`, three cards after each absorb conversation |
| 44 | All five realms blocked out | done: the 2026-09 world rebuild (Ember Crown, Frostfang) and the finish run (Ashen Wilds, Sunspire, Pale Concord, Celestial) |
| 44.5 | Realm decay and restoration | done, scoped: story-gated ending skies (`WeatherResource.RequiredFlagId`/`ExcludedByFlagIds`, `SkyTint`) and NPC after-lines |
| 45 | Feature-complete audit | not run as a phase |

### Stage D — Content Complete

| # | Phase | Outcome |
| --- | --- | --- |
| 46 | Act I | done: the vertical slice is Act I |
| 47 | Act II | done (finish run): `quest.main.gathering`, the hidden-realm reveal, `quest.main.hidden` |
| 47.5 | Ashen Knight rival duels | done (finish run): two optional yielding duels (`WithdrawHealthFraction`, `BossWithdrewEvent`, `FightId`); his last words remember them |
| 48 | Act III | done (finish run): `quest.main.truth`, the Archivist's reading |
| 49 | Act IV and endings | done (finish run): Ashen Knight, Morthul, the throne choice, both endings, credits |
| 50 | Side content and pacing | partial: guild arcs and realm settlements; no dedicated pass |
| 50.5 | Lore codex | not built |
| 51 | Itemization pass | built 2026-10-06, not yet played: a generated six-tier catalogue (373 items, 8 sets, 22 unique effects, 43 affixes, 52 loot tables), level-aware loot, boss reward chests; see the section below |
| 51.5 | Enchanting and sockets | scoped 2026-10-06: reforging at a forge (reroll, upgrade to +5, promote a rarity) is the whole of it; sockets and gem enchanting are struck |
| 52 | Audio production | not built |
| 53 | Art complete | partial: all seven Meshy bosses adopted; no final pass |
| 53.5 | Photo mode | not built |
| 54 | Accessibility and input | partial: full gamepad play, colour-vision modes, high contrast, reduced motion; since 2026-10-06 (built and rendered, not played; below) key and gamepad remapping, a caption layer with subtitle options, first-run accessibility setup, a readable-font switch, presses in place of holds, and a difficulty setting that scales the damage of blows on the player; no audit |
| 55 | G3 acceptance campaign | replaced by the headless `--story` gate plus the pending maintainer play-through |

### Stages E–G — Release, Launch, Live

| # | Phase | Outcome |
| --- | --- | --- |
| 56 | Balance and difficulty | open: the new bosses need tuning |
| 57 | Performance cert | optimization pass built 2026-10-06, unmeasured (below); the formal certification is still not run and the 16.67 ms budget stays a soft target |
| 58 | Save hardening | built 2026-10-06 (gate results in `NOW.md`): format 4 (checksum, one backup generation, stable set-piece keys), save blocks, queued writes, a slot browser that reads slot health |
| 59 | QA and soak | not run |
| 60 | Localization completion | out of scope (English only) |
| 61 | Platform compliance and storefront | out of scope |
| 62 | Release candidate | out of scope; a Windows export (2026-09-28) closed the finish run instead |
| 63 | Launch | out of scope |
| 64 | Launch response | out of scope |
| 65 | Post-launch content | out of scope |
| 66 | Expansion/DLC framework | out of scope |

### Out-of-band passes (maintainer-directed, 2026-08-29 to 2026-09-25)

World geography (one heightfield per region), world quality (terrain materials, water safety,
region template), runtime loading gate, architecture kit, environment/props, 3D pipeline
consolidation (`assets.py`), world-generation replacement (staged generator + drainage), the
infrastructure overhaul (`GameBootstrap` dismantled, scoped services, `--lifecycle`), the
combat/animation/camera overhaul (one action timeline, true first person, ranged combat), the
world-production overhaul (offline bake, residency tiers, safe placement), the 3D asset gap pass,
the 2026-09 world rebuild (52- and 36-cell realms in atlas bands, save v3), and the lifecycle
finalizer fix (`ResidentResources`).

### UI, HUD and meta-shell upgrade (2026-10-06)

One foundation, eight lanes in two waves of four, a review-fix pass per lane, then three polish
lanes, merged on `claude/ui-upgrade`. The direction was "banked embers": keep the identity (ash,
bone, ember, violet) and move from boxed panels on a picture to cut plates with one lit edge,
real iconography, one motion and sound language, and the structure a player expects (hub strip,
footer legend, tabbed settings, a configurable HUD).

- **Foundation.** `UiTheme` split into partials; `UiFx`, `HoldRing`, `UiAudio`, `UiGlyph`,
  `UiLegend`, `HubStrip`, `UiSkin`, `UiTheme.Sheet`, the plate tokens, a bent text-scale curve,
  HUD options and metrics, `ItemIcons` with an atlas packer, the new settings fields, and empty
  shells for the death screen, subtitles, credits and enemy plates so no lane touched
  composition code.
- **Wave 1.** Settings and input (six tabs, remapping, HUD options), HUD core (bare keylined
  vitals, hotbar, tracker, prompts), items (slot marks, detail card, compare, perk tree states),
  knowledge panels (journal, map, bestiary, dialogue typewriter).
- **Wave 2.** Shell front (splash, title, first run, loading, credits), shell session (slots,
  pause, creator, death, narration), HUD combat (boss bar, enemy plates, damage-number modes,
  toasts, subtitles), trade (vendor, crafting, storage, appraisal, contracts).
- **Between the waves.** Two edits the settings lane could not make in frozen files landed
  centrally: pad look sensitivity reached `PlayerLookInput`, and the difficulty multiplier
  reached `CombatComponent.ReceiveDamage`, which is what turned a stored setting into a mechanic.
- **Polish.** The orchestrator ran the harnesses, read the frames and handed defects back by
  lane: HUD (ink weight and halo, plate opacity, square hotbar cells, a centred bottom bar, the
  lock dot), shell (painting behind sub-screens, creator idle and framing, ending-card contrast,
  the handheld title), panels (map tone and labels, compare columns, price ledger, hub plate on
  the two outliers).

What the screenshots found that reading had not: a tracker plate tinted blue by the sky behind a
0.66 alpha; a title built before the handheld viewport existed, pushing Quit under the legend; a
race tooltip running off both screen edges; a spellbook page 28 px wider than its frame; a hotbar
48 px off the crosshair that jumped when the minimap hid. Three "defects" were the harness's own
setup: a pin-snap shot whose pin was never discovered, a junk confirm staged at a shop that buys
no junk, and a hotbar-states shot on a level 50 save that nothing could lock. The lanes wrote
under a no-engine rule; the gates and seven shot harnesses were then run centrally, and the
record is in [`NOW.md`](NOW.md). It has not been played.

### Performance pass (2026-10-06)

Six lanes against one measured baseline, merged on `claude/perf-integration`, then review fixes.
The baseline was taken once, centrally, and it changed the plan: the static world already drew
inside 16.67 ms almost everywhere at Medium on the Iris Xe laptop, so the heaviness felt in play
was not the scene. The lanes went after the other suspects.

- **Load.** Debug builds compile optimized (the editor binary always loads the Debug assembly,
  and it was unoptimized, never-tiered code); the boot validator runs on headless boots only;
  `Loc.T` caches hits; the perf probe covers six realms and reports texture and buffer memory.
- **Rendering.** A Performance tier below Low, Low rebalanced, preset overrides, first-run adapter
  detection, a menu frame cap, and `SkyController` and `EnvironmentEmitters` writing on change.
- **Streaming.** Reconcile on change, an activation deadline checked inside the cell, no
  instantiate and activate in one play frame, one scatter mesh copy per realm, and the tier's
  draw distance and ground-cover density.
- **Assets.** A texture budget by asset class and `assets.py audit-weight`; 52 `.import` files
  capped and compressed, estimated 266 MB to 56 MB.
- **CPU.** Idle components stop ticking, `EventBus` publishes without a copy or an allocation,
  animation, AI senses, foot IK and input stop making engine calls and `StringName`s per frame,
  spell flashes are pooled.
- **HUD.** Labels written on change, idle panels and bars not processed, compass and minimap
  repainted on movement, the monitor a fixed ring, the F4 overlay at 4 Hz with memory and GC.

Fixes after the merge: switching an animation tree's callback mode for level of detail restarted
its state machine, so a corpse crossing 40 m stood back up (the mixer is now manual for life and
stepped by the component); the enemy shadow cut looked for a `MeshInstance3D` where a glTF root
is a plain `Node3D` and did nothing for any modelled enemy; the budget monitor had been stopped
with the overlay closed, which silenced its log warning; the realm-settle wait could turn a slow
load into an abort; auto-detect ran over installs that had saves but no settings file; and Low
had lost its pixel-cost cut. Written under a no-engine rule like the upgrade before it: it
compiles, and the measurement, the import, the bake and every gate were left to one central run
recorded in [`NOW.md`](NOW.md). No number is claimed.

### Item, crafting and save upgrade (2026-10-05/06)

Nine lanes on one foundation, merged on `claude/ics-integration`. The foundation fixed the shared
seams first (item schema fields, `ItemInstance` workmanship and marks, the material bag, save slot
rosters and the save gate, `ItemValidator`, the planned id catalogue) so the lanes could not
collide; each lane then built one system against them.

- **Items.** Every consumable effect kind with shared cooldown groups, required levels,
  two-handed weapons, lossless swaps, real ammunition, item sets and ten unique-effect kinds.
- **Catalogue.** `tools/items/catalogue.py` plus `gen_items.py`: 296 generated items in six realm
  tiers on one stat budget that C# (`ItemBudget`) and Python both compute, stocked into shops.
- **Loot.** Tier pools, item-level rolls near the looter's level, pity, 32 new affixes including
  regeneration and signature affixes, and a persistent reward chest per Flamebearer.
- **Crafting.** 67 generated recipes, a crafting skill, workmanship, bulk orders, recipe scrolls,
  one shared salvage plan, and reforging priced so that it can never be sold at a profit.
- **Inventory UI.** Material bag tab, search, marks, partial stacks, junk and buyback, hotbar
  cooldowns and a pad chord, a merged loot feed.
- **Save.** Format 4 with a checksum and one backup generation, pure envelope, migration and slot
  rules under xUnit, save blocks, an in-game slot browser, writes and thumbnails off the main
  thread, and an autosave that waits for a quiet moment.

Fixes after the merge included: salvage and reforge of worn gear with a
full pack, consumables ignoring their required level, a chest losing spilled loot across a save, a
loot salt too large to survive JSON, commissions rolling above Common, consignment losing its
quantity, and a one-press save guessing a slot. No sockets, durability, repair or encumbrance
were added. The whole upgrade was written under a no-engine rule: it compiles and its filtered
unit tests pass, and `--validate`, `--lifecycle`, `--story`, the save-audit probe and every render
were left to the single gates run recorded in [`NOW.md`](NOW.md).

### Meshy world overhaul (2026-10-06/07)

Owner direction: the outdoor world is generated art, not the four Quaternius packs, with no cosmetic
bolt-ons, real dragons and a world that reads as vast. Built on `claude/meshy-world-overhaul` as a tooling
foundation, one generation run (795 credits), then six lanes each with a review-fix pass: bolt-on
removal, NPC bodies, scale, scatter, creatures and landmarks. The lanes never launched Godot, so the
integrator's gates and the single master bake were left to one central run recorded in
[`NOW.md`](NOW.md); no frame rate, fight or walk is claimed.

- **Sourcing.** Three lanes: a generated cast, a generated outdoor world, and the Quaternius housing and
  architecture kit kept on purpose ([`ASSET_POLICY.md`](ASSET_POLICY.md) §0.1). Variety rule: two or three
  models per type, the rest from scatter transform and tint.
- **Built.** Nine scatter species and 12 new giants, 17 landmark kinds composed in all six realms, 26 new
  map locations, six NPC bodies, four dragons on a fitted 20-bone rig, three beasts rebound, a regenerated
  sword and dagger, ten bodies drawn larger than they fight, new fog and backdrop heights, and the
  pipeline ([`3D_ASSETS.md`](3D_ASSETS.md) → OUTDOOR WORLD, [`RECIPES.md`](RECIPES.md) → Assets).
- **Removed.** `EnemyVisualKit`, `NpcVisualKit`, both kit models, the player's pauldrons and pouch.
  Steam Deck stopped being a target.
- **Traps worth keeping.** Low polycount targets (under about 1,500) come back faceted with holes;
  text-to-3D ignores "T-pose" unless spelled out strictly; `adopt` reads a fresh Meshy rig as QUADRUPED on
  its first run; a scratch folder inside the project needs a `.gdignore` or Godot imports it; Godot must
  not run on a stale C# assembly before the import; two world models must not share an atlas; stone
  atlases come back near-white and are tinted in the wrapper; a `BoneAttachment3D` overwrites its own
  transform, so a grip was never applied; a model swap inherits no collision, and the first-draft fog
  number in the plan was the wrong direction (the old 0.003 already fogged a colossus at 600 m by 83%).

### Magic upgrade integration (2026-10-01/02)

The separate core, status, learning and content implementations were integrated into one working
upgrade: 25 player spells and five enemy spells; committed wind-up/release/recovery; held channels;
ground telegraphs, breakable barriers and dash; status controls, wards, DoTs and six combos; school
mastery, the Weave, aliases and shared learning routes. Prepared tome placements are part of the
master bake. The original assignment brief is now the implementation reference in
[`playbook/magic.md`](playbook/magic.md).

Integration fixed shared-resource charge mutation, status reentrancy and death/load cleanup, status
stun ownership, dodge control cancellation, named-stack healing, Blink cost reconciliation and direct
Sunfall guard breaking. Death and controls cancel at the cast release/tick choke points. Damage
resolution captures health damage and lethal outcome before a synchronous player respawn can change
the live stats. `SpellLifetime` places deliveries under their session/world owner and cancels them
before live load or caster removal.

Confirmed local evidence: all five magic native probes, the deterministic fast world gate and full
SDK validation passed. The master bake produced 145 cells and six region resources with source
signature `4151e553f2d5`, with existing navigation-edge warnings. The final unit run passed 2,996 C#
tests with zero failures/skips and 28 Python SDK tests. Lifecycle passed three New Game/save/destroy/
Load round-trips with zero orphan/baseline/invariant violations; shared action/melee/defence probes
and direct story validation also passed. The rendered magic scenario passed, with the wind-up HUD
and six-school spellbook captures reviewed. A fresh Windows export completed using recovery mode,
and its packaged `--story` passed in isolation with no gameplay errors. Packaging logged one Godot
editor/core hot-reload timer diagnostic; the existing navigation-edge and rendered-exit ObjectDB
warnings remain. Exact run and log references are in [`NOW.md`](NOW.md). The maintainer's full
play-through, eye-level world/boss review and boss/duel balance work remain open.

## Lessons still worth knowing

Each of these shipped a defect or a false pass. Rules already written in `CLAUDE.md`,
`RECIPES.md`, `NOW.md` or `WORLD_AUTHORING.md` are not repeated here.

- **Read a hit's outcome before its events can change the actor.** The player respawns inside the
  death event; reading health afterward lost lethal-hit lifesteal and reapplied afflictions to the
  revived player. Capture the damage/lethal result before dispatch and use it through the spell hooks.
- **A probe that cannot fail is not a gate.** Four probes once printed PASS while testing nothing:
  a `Vector3?` that does not marshal aborted a script mid-function, an arrow parented to a null
  `CurrentScene` was never in the tree, un-parented bodies posed no bones, and a wall test passed
  because the warp it measured never fired. Add a control case, or negative-test the gate, first.
- **A crashed test run can look green.** Constructing a Godot `Resource` in the pure suite takes the
  whole run down and reports the few tests that ran as passing. Compare the test count, not the colour.
- **Read the gate that is already failing.** `debug_pass_regressions.gd` failed on `main` for days
  because New Game could not reach Playing (a spawn 3.01 m above the ground, 1 cm outside the loading
  probe), and no headless route took the New Game path. The `--lifecycle` and `--story` gates exist
  because of it.
- **Headless bakes can write zeroed GPU-derived data.** Baked vegetation and the backdrop once had
  zeroed MultiMesh transforms from the headless dummy renderer. Render the bake before trusting it.
- **Ask what a tool can actually see.** The Godot MCP drives the editor, where the runtime HUD does not
  exist; UI defects shipped through a green battery until `--hudshots` / `--panelshots` rendered the
  real screens. Build the capture harness before the UI work, not after.
- **A harness shot is evidence only if it drives the thing you changed.** And a failing shot may
  be its own staging: check that the fixture can reach the state before changing the screen.
- **Parallel agents share whatever they were not told is theirs.** Two UI lanes wrote a helper
  script of the same name into one scratch folder and each ran the other's once. Give every lane
  its own folder as well as its own worktree.
- **A frozen file needs a way to ask.** Lanes that could not edit shared files listed exact
  edits as requests; the two that mattered (difficulty, pad sensitivity) landed between waves.
  The ones nobody collected are still gaps: dedicated menu actions for compare, track and mark.
- **The gate that finds a placement defect is rarely the one that names the thing.** `--validate` and
  the layout check passed buildings standing on roads; the traversal probe and a render found them.
- **When a new state is defined by a negation, grep for the negation.** Quest branches (41D) broke six
  old `!IsObjectiveComplete`-style tests that had been correct for thirty phases.
- **A world change derives from one persisted fact**, and hiding a body also means its collider and its
  prompt, refreshed after a wholesale load (loads do not replay individual flag events).
- **A debug command must go through the real choke point**, or it proves nothing about events,
  rewards or world changes. Resetting a view (a quest log entry) is not resetting its world fact.
- **A choice a completed quest leaves open must only add**, never clear: fire `SetFlag` on the exact
  derived string rather than an effect that can demote when revisited.
- **When nesting dialogue conditions, the condition the node text presupposes goes outermost.**
- **Verify content against states on purpose.** A companion's killing blow never credited a Kill
  objective from Phase 32C to 42E because no quest had been tested that way.
- **Grep existing flavour text before inventing a place or item** for an arc; earlier writers often
  named the seam already (`strings.csv`).
- **Striking a phase is work outside the phase**: grep for its name and follow every pointer into the
  phases written against it, and record which partial keeps were offered and declined.
- **A deferred condition is a hypothesis.** Measure it, then look at the area anyway: clustering
  measured false while the defect sat a few metres away.
- **When a new surface answers an old surface's question, take the question away from the old one.**
  The compass kept discovered places after the minimap took that job, and both got worse.
- **"Arms crossed" was an import setting, not an animation bug**: A-pose bodies on T-pose clips with
  `fix_silhouette` off. A rigged body shipping zero animations gets no `AnimationPlayer` at all.
- **Finish run (2026-09-27/28).** Never run world bakes in parallel: merge the branches, then one
  master bake. Run heavy gates one at a time on a 14 GB machine. An export silently drops files it
  does not recognise as resources — the locale CSV (fixed with `importer="keep"`) and the two
  `manifest.json` files (`include_filter`) — so smoke-test the export itself (`--story` inside it).
  A validator arm that reads `.tscn` text reports false errors in an export, where scenes are binary.
