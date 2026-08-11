# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C. Phase 38 (economy) ✅ CLOSED. Phase 39 (Mounts & Traversal) ✅ CLOSED — 39A, 39B, 39C.**
  ⚠️ **The mount is a state of the rider, not a second body.** ⚠️ **CLIMB AND SWIM ARE CUT, NOT
  DEFERRED**, each with a condition (39C): swim lands when a cell authors a water volume with
  something on the far side; climb lands when a cell authors a surface reachable no other way.
  **Do not build either speculatively.**
- **Phase 39.5 (World Map & Location Intelligence) — 39.5A ✅ CLOSED.** ⚠️ **It was inserted into the
  roadmap deliberately, with the maintainer's approval, because the map was on no phase at all.**
  The map now reads the world: 63 authored locations across all 15 cells, pan/zoom/search/filter/
  select, distance and bearing, a waypoint, a live region breadcrumb, and land drawn from each
  cell's measured ground footprint.
- **NEXT: Phase 39.5B — the rest of the map** (maintainer direction, 2026-08-10). The deferred table
  in `docs/playbook/phase-39_5.md` is the scope, and ⚠️ **every item there carries a runnable
  condition rather than a verdict** — check each one before building it, because 38G sat eleven
  sub-phases past its own condition when the notice named a conclusion. ⚠️ **The first item is not on
  that table and outranks it: THIS REPO CANNOT SEE ITS OWN UI.** Three screen-space defects shipped
  through a fully green battery in 39.5A and every one was found by the maintainer opening the map.
  ⚠️ **The quest-marker item is blocked on quest data, not on map work** — a quest names a template
  id, not a place — so it is a `QuestResource` change first.
- **Then: Phase 40 (Survival & Needs) — a decision phase, starting at 40A.** ⚠️ **40A owns the
  repair/durability call** that 38D deferred to it, and `docs/DESIGN.md` §6's sink table has an empty
  "Repair — pending 40A" row waiting on it. ⚠️ **40B's rule is that a cut system leaves no stub** —
  39C and 39.5A are both worked examples.
- ⚠️ **A LOCATION'S POSITION IS ITS NODE'S TRANSFORM IN A CELL SCENE, NEVER AN AUTHORED COORDINATE**
  (39.5A). `MapLocationResource` says what a place is; a `MapLocationComponent` parented to the stall
  or keeper says where. `--validate` scans `.tscn` in **both** directions. Author with
  `tools/gen_map_locations.py` — it generates the `.tres`, the locale keys and the scene markers from
  one table, and `--check` is a gate.
- ⚠️ **IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP, IN THE SAME SUB-PHASE THAT ADDS IT**
  (maintainer direction, 39.5A). **This is a gate:** `ValidateEverythingIsOnTheMap` fails `--validate`
  for any shop or service no map location names, and coverage ships at 23/23 and 15/15 so it can only
  be broken by adding something new. CLAUDE.md §1 and the shop/service/cell recipes all say so.
  ⚠️ **Quest destinations are not coverable yet** — a quest names a template id, not a place — and
  that is 39.5B, not an exemption.
- ⚠️ **THREE ORPHANS FOUND BY A FEATURE-CONTINUITY AUDIT (2026-08-11), EACH NEEDING A DECISION
  RATHER THAN A FIX.** All three are content or code with **no caller**, which is the
  `CraftingComponent.Learn` failure the working agreement names — nothing fails over any of them:
  1. **`quest.cull_goblins` cannot be started by anything.** 33D deliberately stopped the sandbox
     auto-seeding it (good reason: a full journal before the player has done anything undercuts the
     opening) but left the `.tres` **and** the dead `GameIds.CullGoblins` constant behind. Either
     wire it to a giver or delete both. ⚠️ **There is no `ValidateQuestReachability` rule** — the
     pattern exists for recipes and contraband, and a quest arm would have caught this.
  2. **`QuestGiverComponent` has zero references** — not code, not scenes, not data. Quests are
     granted by `DialogueEffect.StartQuest` instead. ⚠️ It also carries two **hard-coded
     player-facing strings** (`"Talk"`, `"Accept: …"`), so if it is ever placed it ships
     untranslated.
  3. **`CraftingStationType.Cooking` has no recipe and no station.** ⚠️ **Do not delete it blind —
     Phase 40A owns the food/cooking decision**, so this is 40A's to adopt or cut.
- ⚠️ **DYING WHILE MOUNTED IS A KNOWN GAP, NAMED RATHER THAN FIXED (39B).** Death is not a one-shot,
  so it still plays a full-body clip on a seated offset, and nothing dismounts on death. Whoever
  touches mounts next owns it.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred.** Two of the five already
  existed under another name. **Do not rebuild them.**
- 📖 The economy is documented where you would look: `ARCHITECTURE.md` §2.6m is the mechanism,
  `DESIGN.md` §6 + §6.1 the intent. 🎨 The four-MegaKit asset migration is complete;
  `docs/ASSET_POLICY.md` §0.2–§0.3 is the authority.
- ✅ **The `.claude/skills/*/SKILL.md` cloud-URL drift 39A flagged is FIXED and committed** (39.5A).
  Opening Godot from the project manager sets no `GODOT_MCP_HOST` and silently defaults to **Cloud**
  mode, which is what regenerated them; `godot-cli open . --mode Custom --url http://localhost:23630`
  is the fix and CLAUDE.md §2 now records it.

## Last verified (session close, 2026-08-10)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1376 passing** (39.5A adds 47) |
| `--validate` | exit 0 |
| **Negative tests** | `python tools/negative_tests.py` — **53/53 broken and restored** (8 are the map's). ⚠️ Runs **over two minutes**; killing it mid-run defeats its own `finally` and leaves the tree mutated (`git checkout -- data/ scenes/` recovers). ⚠️ It edits `scenes/` too, and refuses to start on a dirty tree |
| `--economy` | **price landscape identical**, diffed against `HEAD~1` data. ⚠️ **`git stash` stashes NOTHING on a committed tree**, so a stash-based before/after comparison silently compares a build with itself — check the diff is non-trivial before believing it. Locale count 1188 → 1317 |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 33 dialogues, 14 quests, **64 map locations** |
| **Continuity audit** | 2026-08-11, static: 59 components all attached · 300+ authored ids across 21 content types with **zero orphans** · 18/18 story flags set *and* read · 29/29 input actions bound *and* read · 12/12 UI panels instantiated · every gameplay enum member handled **except `CraftingStationType.Cooking`**. Three orphans found, listed above |
| **`map_probe`** | `godot --headless --path . --script res://tools/map_probe.gd` — **63 markers across 15 cells**, each a distinct in-cell world position. Exits 0/1, so it is a gate |
| **`--play`** | booted, loaded slot1, 33 objects restored. ⚠️ The 3 `CraftingStationComponent has no owning Entity ancestor` warnings are **pre-existing** — proved by re-running against `HEAD~1` scenes |
| Rendered | ⚠️ **NOTHING NEW. No screenshot of the map screen exists**, and the world is visually unchanged (map markers are invisible `Node3D`s). Every visual fact about the map in this session came from the maintainer opening it |
| Not verified | ⚠️ **The map's own interactions are proved by test and by the maintainer's eye, not by captured output** — pan, zoom, drag, right-click waypoint, search, filters and the fast-travel jump have no automated coverage above the pure-logic layer |

## Live invariants — the things that will bite you

1. **A region loads whole.** Every cell of the active region is resident; no distance test, no unload
   during play. ⚠️ Both *regions* cannot be resident together (Phase 44).
   ⚠️ **So `RevealWithCell` on a map location means "known on entering the REGION"** (39.5A).
2. ⚠️ **`sell <= LOCAL value <= buy` holds at every shop by construction.** A round trip at **one**
   shop always costs; a carry from surplus to demand is *meant* to pay. Treat `sell > buy` **at one
   shop** as a defect.
3. ⚠️ **A DEMAND TABLE IS A FLOOR UNDER OTHER PEOPLE'S RULES.** ⚠️ `ShopResource.CellId` is empty by
   default and empty means par.
4. ⚠️ **WHEN A NEW PRICE APPEARS, ASK WHAT IT IS A SPREAD OVER** — every new multiplier joins
   `NoCombinationOfMultipliersLetsSellingBeatBuying` or it does not ship.
5. ⚠️ **THE EXPLANATION IS THE CHARGE** (38U). `PriceBreakdown.Total` is what the vendor window, the
   commission desk and the map screen display *and* charge. ⚠️ **39.5A moved the map's fee display
   twice and it still calls the one `TravelCosts.QuoteFor` the jump charges.**
6. ⚠️ **A RULE PROVEN ONCE IS NOT A RULE PROVEN TODAY** (38V). `tools/negative_tests.py` is the answer
   and it is re-runnable. ⚠️ **It cannot reach a rule that lives in a code constant.**
7. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT** (37F), and **A CACHED POSE
   IS THE SAME BUG WEARING VISUALS** (39B). ⚠️ **AND A VALUE TYPE CARRYING LAYOUT STATE IS THE SAME
   BUG WITH NO CACHE IN IT** (39.5A): `MapProjection` is built before layout, so its viewport is
   `(1,1)` until something calls `Resized` — nothing did, and the whole map projected about a
   half-pixel at the top-left corner. **When a sub-phase adds a state, ask what every existing thing
   does IN that state.**
8. ⚠️ **A MODEL THAT READS AT 20 m CAN DOMINATE AT 4 m** (37E); **RENDER FROM THE SEAT, NOT AT THE
   OBJECT** (39A). ⚠️ **AND THE UI HAS NO SEAT TO RENDER FROM** (39.5A): `--play` cannot press a key
   and the Godot MCP drives the *editor*, not the game, so **a screen-space defect is invisible to
   every check this repo has.** Three shipped through a fully green battery.
9. ⚠️ **A GODOT `Label` DEFAULTS `mouse_filter` TO IGNORE** (38U). **A property whose default differs
   from its base class's is the defect class that passes every review.** ⚠️ 39.5A's near-miss of the
   same family: a public `Hidden` on a `Control` silently shadows `CanvasItem.Hidden`.
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
    actor climbs up to **0.5 m**, pinned to the navmesh's `agent_max_climb` by a `--validate` rule.
    ⚠️ **The step is simulated, never computed** — `tools/stepup_probe.gd` is the check.
17. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`.** ⚠️ **An unresolved
    clip slot is silent**, so a slot whose *choice* matters is pinned by test.
18. **Check what is already vendored before pulling from the web.**
19. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL.** ⚠️ **`global_transform` returns
    IDENTITY with an error for a node added during `_initialize`** — `await process_frame` twice.
20. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1.**
21. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.**
22. ⚠️ **The Ember Crown map was re-laid (38F).** ⚠️ **A schedule carries a copy of its cell's
    `Center` as `Origin`** — moving a cell is never a one-line edit. ⚠️ **A cell has no authored
    size at all** (39.5A) — the floor dimensions exist only as prose in the region `.tres` header,
    which is why the map measures ground geometry at runtime instead.
23. ⚠️ **A MOUNTED BODY MAY NOT PLAY A FULL-BODY CLIP** (39B). **Death is the known remaining hole.**
24. ⚠️ **TWO FUNCTIONS THAT ORDER THE SAME BRANCHES MUST AGREE.** ⚠️ **A price lookup fails CLOSED.**
25. ⚠️ **A RESTORED FILE WITH AN OLD TIMESTAMP DOES NOT REBUILD** (39A). `touch` before `dotnet build`.
26. ⚠️ **A COMPUTED LOCALE KEY IS INVISIBLE TO EVERY DATA-DRIVEN CHECK** (39.5A). A category name is
    built from an enum member, so adding a member adds a key reference no `.tres` mentions and no
    database walk can find — `map.category.crafting` shipped missing and would have shown the player
    a raw key in three places. `ValidateMapTaxonomyIsNamed` and `ValidateBreakdownKeys` are the
    pattern: **enumerate the declared set, not the set today's data happens to reach.**
27. ⚠️ **MOVING A FEATURE IS INDISTINGUISHABLE FROM DELETING IT** (39.5A). Fast travel was relocated
    onto a marker behind a discovery gate the player had not walked yet, and read as removed. **Ask
    what state the player is in when the new path does not exist yet.**

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before running — nothing else recompiles C#
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
python tools/negative_tests.py                  # proves the gate still bites (50 cases, >2 min)
python tools/gen_map_locations.py [--check]     # author map locations; --check is a gate
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
godot --headless --path . --script res://tools/map_probe.gd     # map placement gate, exit 0/1
godot --headless --path . --script res://tools/stepup_probe.gd  # step-up gate, exit 0/1
godot-cli status .                              # the Godot MCP probe — it is DOWN every session start
```
