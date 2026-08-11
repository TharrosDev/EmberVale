# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C. Phase 38 (economy) ✅ CLOSED. Phase 39 (Mounts & Traversal) ✅ CLOSED — 39A, 39B, 39C.**
  ⚠️ **The mount is a state of the rider, not a second body.** ⚠️ **CLIMB AND SWIM ARE CUT, NOT
  DEFERRED**, each with a condition (39C). **Do not build either speculatively.**
- **Phase 39.5 (World Map & Location Intelligence) — 39.5A ✅ CLOSED, 39.5B ✅ CLOSED.**
  39.5A made the map read the world: 63 authored locations across all 15 cells, pan/zoom/search/
  filter/select, distance and bearing, a waypoint, and land drawn from measured ground footprint.
  **39.5B was the player HUD** (maintainer direction, 2026-08-11 — briefed as a standalone overhaul
  and folded in here, because the minimap, the tracked quest and the compass are all `MapService`).
- ⚠️ **39.5B's structural win is `--hudshots`, and everything else followed from it.** The audit
  first concluded the HUD was already fine — roughly sixty of the brief's eighty sections were
  shipped and checked by Phase 18, 30.5B/C/D/I and 37.5B. **The maintainer rejected that: shipped is
  not the same as good.** Building the capture harness settled it, because every finding below came
  from *looking at a frame*, and not one was reachable by any other check this repo has:
  1. ⚠️ **`UiTheme.Trough` was the same colour as `CardBg`** (`0.13,0.125,0.115` vs
     `0.135,0.126,0.112`), so **every bar in the game had an invisible empty track** — health at
     122/664 read as a short red nub, not a nearly-empty gauge. Now delegates to `WellBg`, which is
     what `UI_STYLE.md` §2 always said troughs were.
  2. ⚠️ **`DayPhases.Label` returned hard-coded English and the HUD clock printed it at the player**
     from Phase 18 — a §46 violation in the most-visible widget in the game.
  3. **`GameHud` was not pause-immune**, so the new mode table never ran: a menu paused the tree and
     froze the whole HUD *on top of the menu*, which is the exact defect the table was added to fix.
  4. The three resources were **pixel-identical rows** (no hierarchy), health had **no low/critical
     treatment at all**, the hotbar printed **"(EMPTY)" four times**, and the prepared spell had no
     keycap and no cost.
- **Four genuine gaps also shipped:** there was **no minimap anywhere in the repo**; **no
  tracked-quest concept** (`GameHud` and `CompassStrip` each scanned for the first active quest and
  agreed only by accident of dictionary order); no HUD visibility logic; and silent objective
  advances. The section-by-section audit of all 80 is in `docs/playbook/phase-39_5.md`.
- ✅ **THE HARNESS GAP IS CLOSED, BOTH HALVES.** `godot --path . -- --hudshots` renders **12** HUD
  states; `-- --panelshots` renders **9** panel states, and **the map screen has now been
  photographed for the first time in the project's history**. Between them they found six shipped
  defects across 39.5B and 39.5C. ⚠️ **Build the instrument before the work** — 39.5B built its
  harness afterwards and had to revisit everything it had already called done.
- **Phase 39.5C ✅ CLOSED — panel capture, the map's labels, quest destinations.** It measured all
  eight deferred conditions before building any of them: three ripe, five not. ⚠️ **The five stay
  deferred and `docs/playbook/phase-39_5.md` records what each MEASURED AT**, so the next session
  checks whether the world changed rather than re-deriving the number.
  1. ⚠️ **The clustering condition named the wrong defect.** It was deferred until "two `Detail`
     markers overlap"; the closest pair in the game is **2.13 m** (19 px at Detail zoom, against a
     4 px pin) and they never do. **Their 50–70 px labels always did** — the town hub drew seven
     names as one pile of struck-through text. `LabelPlacer` fixes that; clustering would have
     merged the markers, which were the part that worked.
  2. ⚠️ **A `VBoxContainer` resolves overflow by crushing whichever child has `ExpandFill`.** The map
     rail's fast-travel list grows a row per attunement with no ceiling, so the FILTERS scroll
     collapsed to ~14 px and rendered its buttons **sliced in half**.
  3. ⚠️ **Quest destinations landed, and exactly ONE objective in the game earns one.** Every hostile
     is a **region-scoped** `EncounterResource` spawned around the player; every quest material comes
     off a **loot table**, not a placed node. Only the Ash dragon is a placed lair. **This world
     needs search AREAS, not points** — now a measured condition rather than a guess.
- ⚠️ **THE COMPASS WAS REBUILT FROM NOTHING** (maintainer: "bad and janky", then "multiple elements
  overlapping — break it down and restart"). It was **nine draw passes in a 320×26 box** and read as
  a barcode. The rewrite is mostly subtraction: no panel, no fade, no graduations, and — the real cut
  — **no discovered-place ticks, because the minimap now answers that question better**. Four
  elements remain, plus an edge arrow for a destination behind you, which used to draw nothing at all.
- **Phase 39.5 is CLOSED. There is no 39.5D** — the remaining table is condition-gated, not
  scheduled, and inventing a sub-phase to hold unripe items is what 38G did wrong.
- **NEXT: Phase 40 (Survival & Needs) — a decision phase, starting at 40A.** ⚠️ **40A owns the
  repair/durability call** that 38D deferred to it, and `docs/DESIGN.md` §6's sink table has an empty
  "Repair — pending 40A" row waiting on it. ⚠️ **40B's rule is that a cut system leaves no stub** —
  39C, 39.5A and 39.5B's cut `HudMode.Dead` are all worked examples.
- ⚠️ **A LOCATION'S POSITION IS ITS NODE'S TRANSFORM IN A CELL SCENE, NEVER AN AUTHORED COORDINATE**
  (39.5A). `MapLocationResource` says what a place is; a `MapLocationComponent` parented to the stall
  or keeper says where. `--validate` scans `.tscn` in **both** directions. Author with
  `tools/gen_map_locations.py`; `--check` is a gate.
- ⚠️ **IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP, IN THE SAME SUB-PHASE THAT ADDS IT.**
  **This is a gate:** `ValidateEverythingIsOnTheMap` fails `--validate` for any shop or service no
  map location names; coverage ships at 23/23 and 15/15. ⚠️ **Quest destinations are not coverable
  yet** — a quest names a template id, not a place — and that is 39.5C, not an exemption.
- ⚠️ **THREE ORPHANS FOUND BY A FEATURE-CONTINUITY AUDIT (2026-08-11), EACH NEEDING A DECISION
  RATHER THAN A FIX.** All three are content or code with **no caller**:
  1. **`quest.cull_goblins` cannot be started by anything.** Wire it to a giver or delete it and the
     dead `GameIds.CullGoblins` constant. ⚠️ **There is no `ValidateQuestReachability` rule.**
  2. **`QuestGiverComponent` has zero references** — quests are granted by `DialogueEffect.StartQuest`
     instead. ⚠️ It carries two hard-coded player-facing strings, so if placed it ships untranslated.
  3. **`CraftingStationType.Cooking` has no recipe and no station.** ⚠️ **Phase 40A owns the
     food/cooking decision** — do not delete it blind.
- ⚠️ **DYING WHILE MOUNTED IS A KNOWN GAP, NAMED RATHER THAN FIXED (39B).** Death plays a full-body
  clip on a seated offset, and nothing dismounts on death.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred.** Two already existed under
  another name. **Do not rebuild them.**
- 📖 Economy: `ARCHITECTURE.md` §2.6m is the mechanism, `DESIGN.md` §6 + §6.1 the intent.
  🎨 `docs/ASSET_POLICY.md` §0.2–§0.3 is the asset authority.

## Last verified (session close, 2026-08-11)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1428 passing** (39.5C adds 9) |
| `--validate` | exit 0; **1331** locale strings |
| **Negative tests** | `python tools/negative_tests.py` — **58/58 broken and restored** (1 is 39.5C's). ⚠️ Runs **over two minutes**; killing it mid-run defeats its own `finally` and leaves the tree mutated (`git checkout -- data/ scenes/` recovers). ⚠️ It edits `scenes/` too, and refuses to start on a dirty tree |
| `--economy` | unchanged — **60 goods, 3 routes**, and `git diff origin/main --name-only -- data/` returns `AncientKin.tres` alone, a quest that carries no price. ⚠️ **Prove it by diff rather than by re-reading the report**: cheaper *and* stronger than eyeballing the same table twice — and it sidesteps the `git stash` trap (**a stash on a committed tree stashes NOTHING**, so a stash-based before/after silently compares a build with itself). |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 33 dialogues, 14 quests, **64 map locations** — unchanged |
| **`map_probe`** | `godot --headless --path . --script res://tools/map_probe.gd` — **64 markers across 15 cells**. Exits 0/1, so it is a gate |
| **`--play`** | booted, loaded slot1, **33 objects restored**. ⚠️ The 3 `CraftingStationComponent` warnings are **pre-existing**. ⚠️ **The six-line "C# backtrace" blocks around them are `PushWarning`'s own trace, not exceptions** — `grep -i error` matches `godot_variant_call_error` inside every frame and reads as six errors in a clean log |
| **Rendered** | ✅ **21 frames at 1280×720, all opened and read** — `--hudshots` (12) + `--panelshots` (9). **The map screen has now been photographed for the first time.** Six shipped defects came out of these captures across 39.5B and 39.5C |
| Not verified | ⚠️ **Only 1280×720** — ultrawide, 16:10 and low-resolution windowed rest on `HudLayout`'s flow/anchor design, not a frame (§41–42). ⚠️ **The damage-direction arc is still unphotographed**: it needs an attacker, and both harnesses drive state rather than combat. ⚠️ **The map's objective ring is wired and correct but cannot render yet** — the one authored quest destination is cross-region, so its pin is undiscovered and therefore absent |

## Live invariants — the things that will bite you

1. **A region loads whole.** Every cell of the active region is resident; no distance test, no unload
   during play. ⚠️ Both *regions* cannot be resident together (Phase 44).
   ⚠️ **So `RevealWithCell` on a map location means "known on entering the REGION"** (39.5A).
2. ⚠️ **`sell <= LOCAL value <= buy` holds at every shop by construction.** Treat `sell > buy` **at
   one shop** as a defect.
3. ⚠️ **A DEMAND TABLE IS A FLOOR UNDER OTHER PEOPLE'S RULES.** ⚠️ `ShopResource.CellId` is empty by
   default and empty means par.
4. ⚠️ **WHEN A NEW PRICE APPEARS, ASK WHAT IT IS A SPREAD OVER** — every new multiplier joins
   `NoCombinationOfMultipliersLetsSellingBeatBuying` or it does not ship.
5. ⚠️ **THE EXPLANATION IS THE CHARGE** (38U). ⚠️ **AND ONE SURFACE OWNS EACH FACT** (39.5B): the map,
   the minimap and the compass share **one** `MapPins` builder and **one** resolved objective target,
   and the tracker's "320 m · NW" is measured to the same point the compass draws its marker at. Two
   surfaces computing the same answer agree until the day one of them gains a filter.
6. ⚠️ **A RULE PROVEN ONCE IS NOT A RULE PROVEN TODAY** (38V). `tools/negative_tests.py` is the
   answer. ⚠️ **It cannot reach a rule that lives in a code constant.**
7. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT** (37F); **A CACHED POSE IS
   THE SAME BUG WEARING VISUALS** (39B); **A VALUE TYPE CARRYING LAYOUT STATE IS THE SAME BUG WITH NO
   CACHE IN IT** (39.5A — `MapProjection`'s viewport is `(1,1)` until `Resized`). **When a sub-phase
   adds a state, ask what every existing thing does IN that state.** ⚠️ **39.5B is the corollary: the
   HUD had never been asked what it does in the "a menu is open" state, and the answer was "everything
   it does in gameplay, on top of the menu, including offering a key that a paused tree ignores."**
8. ✅ **THE UI NOW HAS A SEAT TO RENDER FROM — USE IT** (39.5B). `--hudshots` is the answer to 39.5A's
   most expensive gap, and **it found four shipped defects on its first run**, including a bar trough
   the same colour as its background and a hard-coded English string on screen since Phase 18. ⚠️ **A
   UI change that has not been captured is not verified**, and "reviewed against the API" is the
   phrase that preceded every one of those defects. Extend the shot list rather than trusting a diff.
   ⚠️ **AND A RUNNING EDITOR IS NOT A WORKING MCP** (39.5B): the editor process was up all session, so
   a task-list check says yes; the server behind it was not. The tell is
   `.claude/skills/*/SKILL.md` showing `ai-game.dev` URLs — that means the editor was opened from the
   project manager and is in **Cloud** mode, so the local server has nothing behind it and every call
   503s or times out. `godot-cli close .` then `godot-cli open . --mode Custom --url
   http://localhost:23630 --editor-path <the console-LESS .exe>` is the fix.
9. ⚠️ **A GODOT PROPERTY WHOSE DEFAULT DIFFERS FROM ITS BASE CLASS'S IS THE DEFECT CLASS THAT PASSES
   EVERY REVIEW** — a `Label` defaults `mouse_filter` to Ignore (38U); a public `Hidden` on a
   `Control` shadows `CanvasItem.Hidden` (39.5A). ⚠️ **39.5B's member of the family: a `Button` in a
   NON-MODAL `UiPanel` is unreachable by mouse**, because `UiPanel` only frees the cursor for modal
   panels. It still works by focus navigation, so it looks fine in code and half-works in play.
10. ⚠️ **DERIVE, THEN BOUND.** ⚠️ `string.GetHashCode()` is randomised per process; `StableRoll` is a
    hand-written FNV-1a. ⚠️ **GDScript can only call methods whose signatures marshal.**
11. ⚠️ **A SERVICE CAN BE FIRED FROM A CONVERSATION, EXCEPT A BANK.** ⚠️ **An entity still gets one
    interactable.**
12. **A broker fronts nothing**, so no purse and no saturation apply to her.
13. **`contraband` is the one trade tag that fails CLOSED.** The Crossway toll is charged in
    `GameBootstrap.PayToll`.
14. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Seven firings.
    ⚠️ **A body's facing is not knowable from a file.** ⚠️ **When no pack model fits, primitives are
    a legitimate answer.**
15. ⚠️ **A DECISION CAN LIVE IN A `.tres` HEADER, AND NOTHING GREPS THOSE.** **Before authoring
    content of a kind that already exists, read the existing one's header.**
16. ⚠️ **`CharacterBody3D` STILL has no step-up; `LocomotionComponent` does** (39C). Every walking
    actor climbs to **0.5 m**, pinned by a `--validate` rule. ⚠️ **The step is simulated, never
    computed** — `tools/stepup_probe.gd` is the check.
17. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`.** ⚠️ **An unresolved
    clip slot is silent**, so a slot whose *choice* matters is pinned by test.
18. **Check what is already vendored before pulling from the web.**
19. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL.** ⚠️ **`global_transform` returns
    IDENTITY with an error for a node added during `_initialize`** — `await process_frame` twice.
20. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1.**
21. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.**
22. ⚠️ **A schedule carries a copy of its cell's `Center` as `Origin`** — moving a cell is never a
    one-line edit. ⚠️ **A cell has no authored size at all** (39.5A), which is why the map measures
    ground geometry at runtime.
23. ⚠️ **A MOUNTED BODY MAY NOT PLAY A FULL-BODY CLIP** (39B). **Death is the known remaining hole.**
24. ⚠️ **TWO FUNCTIONS THAT ORDER THE SAME BRANCHES MUST AGREE.** ⚠️ **A price lookup fails CLOSED.**
25. ⚠️ **A RESTORED FILE WITH AN OLD TIMESTAMP DOES NOT REBUILD** (39A). `touch` before `dotnet build`.
26. ⚠️ **A COMPUTED LOCALE KEY IS INVISIBLE TO EVERY DATA-DRIVEN CHECK** (39.5A). `map.category.crafting`
    shipped missing and would have shown the player a raw key in three places. ⚠️ **39.5B added a
    second set** — `hud.compass.*` picked from a bearing, `hud.unit.*` from a magnitude.
    `ValidateMapTaxonomyIsNamed` and `ValidateHudComputedKeys` are the pattern: **enumerate the
    declared set, not the set today's data happens to reach.**
27. ⚠️ **MOVING A FEATURE IS INDISTINGUISHABLE FROM DELETING IT** (39.5A). **Ask what state the player
    is in when the new path does not exist yet.** ⚠️ **This is also why 39.5B did not build the
    brief's idle auto-hide**: a HUD element that fades out on a timer is a feature the player watches
    disappear, and there was no measured need to justify it.
28. ⚠️ **A CUT SYSTEM LEAVES NO STUB, AND "THE GAME HAS NO SUCH STATE" IS A LEGITIMATE ANSWER**
    (39.5B). The brief asked for a death HUD; `GameBootstrap.OnEntityDied` calls `RespawnPlayer`
    **synchronously**, so there is no window in which one could be seen. It asked for a combat HUD
    state; **there is no `InCombat` flag anywhere in `src/`** and the HUD may not invent one. Both
    were cut and named. **Check whether the state exists before building the presentation of it.**
29. ⚠️ **"ALREADY SHIPPED AND CHECKED" IS NOT "GOOD", AND AN AUDIT THAT ONLY READS CODE CANNOT TELL
    THE DIFFERENCE** (39.5B, maintainer direction). The first pass through the HUD brief marked ~60 of
    80 sections satisfied, correctly: every one had a real implementation, real data bindings and real
    tests. **Four defects were sitting in them anyway** — an invisible bar trough, a hard-coded English
    string on screen since Phase 18, a HUD that froze on top of menus, and three resources with no
    hierarchy between them. Every one is a *presentation* fact, and presentation facts are invisible to
    builds, tests, validators and code review alike. **When the question is quality rather than
    correctness, render it and look; "it has an implementation" answers a different question.**
30. ⚠️ **A DEFERRED CONDITION IS A HYPOTHESIS, AND IT CAN BE WRONG IN THE RIGHT NEIGHBOURHOOD**
    (39.5C). Marker clustering was gated on "two `Detail` markers overlap"; measured, the closest pair
    in the game is 2.13 m and they never do — while their **labels** collided badly enough to render
    seven names as one pile. **Measure the condition, then look at the area anyway**: the measurement
    tells you whether to build *that thing*, not whether the neighbourhood is clean.
31. ⚠️ **WHEN A NEW SURFACE ANSWERS AN OLD SURFACE'S QUESTION, TAKE THE QUESTION AWAY FROM THE OLD
    ONE** (39.5C). The compass carried a tick per discovered place for two sub-phases after the
    minimap made that job redundant, and the cost was not duplication — **both surfaces got worse**.
    Adding a widget is the moment to ask what it makes unnecessary elsewhere.
32. ⚠️ **A `VBoxContainer` RESOLVES OVERFLOW BY CRUSHING WHOEVER HAS `ExpandFill`** (39.5C). The map
    rail's fast-travel list grows a row per attunement with no ceiling, so the FILTERS scroll beside
    it collapsed to ~14 px and drew its buttons sliced in half. **Bound the section that grows, and
    give anything with `ExpandFill` a `CustomMinimumSize` floor too** — `ExpandFill` means "take the
    slack", and there may be none.
33. ⚠️ **A COLOUR TOKEN CAN BE WRONG AND NOTHING WILL EVER SAY SO.** `Trough` sat within a rounding
    error of `CardBg` for multiple phases, making every bar in the game unreadable, and it passed the
    contrast tests because those check *text* pairs. ⚠️ **When two tokens are used together, the pair
    is the thing to check, not each token against `Text`** — and the depth scale (`WellBg`/`PanelBg`/
    `CardBg`) already encodes which pairs are meant to be distinguishable.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before running — nothing else recompiles C#
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
python tools/negative_tests.py                  # proves the gate still bites (56 cases, >2 min)
python tools/gen_map_locations.py [--check]     # author map locations; --check is a gate
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
godot --path . -- --hudshots                    # render 12 HUD states to PNG. ⚠️ NOT --headless
godot --path . -- --panelshots                  # render 9 map/journal states. ⚠️ NOT --headless
godot --headless --path . --script res://tools/map_probe.gd     # map placement gate, exit 0/1
godot --headless --path . --script res://tools/stepup_probe.gd  # step-up gate, exit 0/1
godot-cli status .                              # the Godot MCP probe — it is DOWN every session start
```
