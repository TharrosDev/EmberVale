# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C. Phase 38 (economy) ✅ CLOSED. Phase 39 (Mounts & Traversal) ✅ CLOSED.**
  ⚠️ **The mount is a state of the rider, not a second body.** ⚠️ **CLIMB AND SWIM ARE CUT, NOT
  DEFERRED**, each with a condition (39C). **Do not build either speculatively.**
- **Phase 39.5 (World Map & Location Intelligence) ✅ CLOSED — 39.5A, 39.5B, 39.5C. There is no
  39.5D**; the remaining table is condition-gated, not scheduled, and inventing a sub-phase to hold
  unripe items is what 38G did wrong. ⚠️ **`docs/playbook/phase-39_5.md` records what each deferred
  item MEASURED AT**, so the next session checks whether the world changed rather than re-deriving it.
- ❌ **PHASE 40 (Survival & Needs) AND PHASE 40.5 (Dungeon & Puzzle Framework) ARE STRUCK — NOT
  WANTED** (maintainer direction, 2026-08-12). **This game has no survival needs**: no durability or
  repair, no hunger, no thirst, no temperature, no encumbrance. **And no puzzle, trap or vault
  tooling.** Neither is deferred and neither is condition-gated — **there is no condition that
  revives them**, so do not propose any of them as a fix for anything. ⚠️ **The trap arm of 40.5 was
  offered as a partial keep and declined**; that is recorded so the cheapest-looking piece is not
  re-proposed next session.
- ⚠️ **STRIKING A PHASE IS WORK, AND MOST OF IT IS OUTSIDE THE PHASE.** The build had zero survival
  code, so the cut looked like two checkboxes — but Phase 40 had **two live stubs and seven files
  pointing at a decision it would make**, and a struck phase with dangling pointers is worse than an
  open one, because they now name something that will never resolve. Both stubs are gone:
  1. **`CraftingStationType.Cooking`** — orphan #3 of the 2026-08-11 audit, which `NOW.md` had
     reserved for 40A with *"do not delete it blind."* Zero recipes, zero stations, zero scenes, and
     the **last** member of an append-only enum, so nothing shifted. ⚠️ **Ordinal 4 stays retired;
     the next station appends at 5**, and being last-and-unauthored is the only reason it was safe.
  2. **`InventoryComponent.MaxWeight` / `IsOverEncumbered`** — shipped Phase 5 as *"not yet enforced
     (drives encumbrance later)"* and sat with **zero readers for thirty-five phases**. `later` never
     comes now. ⚠️ **`TotalWeight` STAYS** and the character sheet still prints it: a weight readout
     is an item fact, not a budget.
- ⚠️ **"40B's rule" — a cut system leaves no stub — SURVIVES ITS PHASE BEING STRUCK.** It is cited by
  name in ~10 files (`RECIPES.md`, `MapService.cs`, `MapLocationResource.cs`, `HudVisibility.cs`,
  `PRODUCTION_ROADMAP.md`, the 38/39/39.5 entries) and **those citations were deliberately not
  rewritten** — `docs/playbook/phase-40.md` is kept as a struck entry that preserves the provenance,
  and **invariant 28 below is the rule's home.**
- **Two phases were written against 40.5 and now owe their own answer**, named rather than discovered
  later: **Phase 50** authors dungeons as rooms with encounters and loot on existing tooling (⚠️ **do
  not reinvent hazards there**), and **Phase 51E** has a guardian (`LairSpawnComponent`) but no trial.
- **Phase 41 (Quest Authoring at Scale & Branching) — 41A ✅ CLOSED.** `ObjectiveType.Reach` (2) and
  `Talk` (3) join `Kill` and `Collect` through the same `QuestLogComponent.Advance` choke point. Talk
  rides `DialogueEndedEvent` — **no new event type**; Reach polls at 4 Hz. `quest.hollowreach.word`
  is the caller: Holt sends the player to Hollowreach to ask Sedge Marrow about the barrels, and it
  is **the first quest in the game that is neither a cull nor a fetch.**
- ⚠️ **REACH IS PROXIMITY, NOT DISCOVERY, AND THE FREE-LOOKING REUSE IS THE TRAP** (41A). `MapService`
  already tracks discovery, so driving Reach off it looks like the whole feature for one subscription
  — but a location authored `RevealWithCell` is discovered **on entering the REGION** (invariant 1),
  so that Reach objective completes the moment the player crosses into the Ember Crown from anywhere
  in it. ⚠️ **`ArrivalRadius` is its own constant, deliberately not `MapService.DiscoveryRadius`**:
  spotting a place and arriving at it are different questions.
- ⚠️ **BOTH 41-BOUND ORPHANS ARE RESOLVED, BY DELETION** (41A). `quest.cull_goblins` and
  `GameIds.CullGoblins` are gone — unstartable since Phase 33D, and `quest.warband.bounty` already
  covers *slay goblins for a reward*. `QuestGiverComponent` is gone — zero references, superseded by
  `DialogueEffect.StartQuest`, and carrying two hard-coded player-facing strings.
  **All three of the 2026-08-11 audit's orphans are now closed.**
- ⚠️ **A DEFECT WAS ALREADY SHIPPED IN TWO QUESTS AND NOTHING COULD SEE IT** (41A). Twelve of fourteen
  quests author locale keys; `GatherIron` and `CullTheGoblins` authored **literal English**. Invisible
  because **`Loc.T` returns the key unchanged on a miss**, so `Loc.T("Gather Iron")` renders "Gather
  Iron" and looks perfect in the journal, on the tracker and in every screenshot — then breaks on the
  first non-English locale. **`quest.gather_iron` is live** and had been wrong since Phase 12.
  `ValidateQuestStringsAreKeys` checks **presence in the catalogue**, not "looks dotted", so it
  catches a mistyped key too. ⚠️ **`ObjectiveResource.ShortLabel()` had the same hole** — its fallback
  built `$"Slay {TargetId}"`, putting a raw id on screen for the first objective authored without a
  `Description`. **A fallback nothing reaches is a defect nothing reports.**
- **NEXT: 41B — Escort + Defend/Survive objective types.** ⚠️ **41A's lesson applies directly:** a new
  objective type is a few lines, and the whole job is knowing *which event means what*. For 41B that
  question is **what exactly counts as a fail, and which event says so** — answer it before writing
  the branch.
- ⚠️ **A LOCATION'S POSITION IS ITS NODE'S TRANSFORM IN A CELL SCENE, NEVER AN AUTHORED COORDINATE**
  (39.5A). `MapLocationResource` says what a place is; a `MapLocationComponent` parented to the stall
  or keeper says where. `--validate` scans `.tscn` in **both** directions. Author with
  `tools/gen_map_locations.py`; `--check` is a gate.
- ⚠️ **IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP, IN THE SAME SUB-PHASE THAT ADDS IT.**
  **This is a gate:** `ValidateEverythingIsOnTheMap` fails `--validate` for any shop or service no
  map location names; coverage ships at 23/23 and 15/15. ⚠️ **Quest destinations landed in 39.5C and
  exactly ONE objective in the game earned one** — every hostile is a region-scoped
  `EncounterResource` and every quest material comes off a loot table, so **this world needs search
  AREAS, not points**, and that stays a measured condition rather than a guess. ✅ **41A changed the
  arithmetic in one direction only: a `Reach` objective's `TargetId` IS a location id, so every Reach
  objective is its own destination by construction** — and `--validate` refuses a `LocationId`
  authored beside one, because that would be two answers to a question that has one (invariant 5).
  **Reach is the one objective shape a point genuinely fits**; it does not help the other nineteen.
- ⚠️ **DYING WHILE MOUNTED IS A KNOWN GAP, NAMED RATHER THAN FIXED (39B).** Death plays a full-body
  clip on a seated offset, and nothing dismounts on death.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred.** Two already existed under
  another name. **Do not rebuild them.**
- ✅ **A FULL-REPO AUDIT RAN 2026-08-15 AND ITS WAVE 1 IS LANDED** — no feature work.
  Two save-path P0s (invariant 36), the inert boss AI knob (invariant 37), and three per-frame
  allocation sites, and **CI: `dotnet build --warnaserror` + 1426 tests + `--validate` now run on
  every push** (`.github/workflows/ci.yml`). ⚠️ **CI had been declined before and that was reversed
  deliberately** — do not "restore" the no-CI note as drift. ⚠️ **Editing that workflow needs a token
  with `workflow` scope**, which the session OAuth token lacks; it went in through the GitHub API.
- ✅ **WAVE 2 (foundation) IS ALSO LANDED** — five commits, still no feature work. One
  `ResourceDirectory.Load<T>` replaced **25 copied `*Database.Initialize` bodies** (−930 lines, and
  `ItemDatabase` was the one that had already drifted out of a directory guard);
  `ValidateSceneAuthoredIds` closes the `.tscn` hole `ServiceComponent`'s header had named for
  phases (8 id families, 96 values, **negative battery now 66/66**); a `CellPersistenceDirector`
  closure leak; the `header.json` mirror is deleted rather than left stale; and `TryMigrate` now
  **refuses** an unmigratable save instead of best-efforting it. 📖 **`docs/SAVE_FORMAT.md` is new
  and is the contract** — read it before touching anything that saves, and note its "what is
  deliberately NOT saved" list before filing a bug against a decision.
- ✅ **WAVE 3 (structure) IS PARTLY LANDED — `GameBootstrap` 1501 → 1266 lines and 8 fields gone.**
  Three commits, taken at the 41A/41B boundary: `WorldEnvironmentBuilder` (sun/sky/tonemap/ground),
  six write-only panel fields deleted, and `RegionSetup` (safe zones, portals, toll).
  ⚠️ **TWO EXTRACTIONS WERE REFUSED WITH REASONS, AND THE REASONS ARE THE USEFUL PART:**
  1. **`DebugHotkeys`** — the roadmap called it easy because it is already behind
     `BuildProfile.ShowDeveloperTools`. It is not: the block reaches `_dummy`, `_respawnCountdown`,
     `_player`, `_console`, `_hud`, `_profiler` and `AbortToTitle`, so extracting it **relocates**
     coupling. `SandboxProps` already ruled on that exact state and wrote down why.
  2. **The region-transition state machine** (~290 lines: streamer swap, fast travel,
     `PerformRegionLoad`, `_currentRegionId`). ⚠️ **NOTHING HERE EXERCISES A REGION TRANSITION** —
     `--play` never walks a portal, `--panelshots` never travels — so it would ship verified by
     reading. **It waits for a session with a playthrough in it.**
  ⚠️ **A refactor of code no gate covers is a refactor that needs a human, and saying so is cheaper
  than a regression found three phases later.**
- ✅ **WAVE 4 IS CLOSED — THREE COMMITS AND FOUR REFUSALS, AND THE REFUSALS ARE THE LONGER HALF.**
  It was scheduled against its own "opportunistic, never as scheduled work" classification, on the
  instruction to push back where that was right. Done: **L-14** — `EnemyArchetypeFactory` decided
  flight from `AIProfileDatabase` while the brain it built one line earlier resolved through
  `ResolveProfile`; both now go through one static `EnemyAIComponent.Resolve`. ⚠️ **LATENT, NOT
  LIVE** — the factory never sets an inline `Profile` and all four boss `.tres` author every phase
  `AiProfileId = ""`, so no data reaches the divergence; **this removed a second answer, it did not
  fix a symptom.** **L-15a** — `MapService`'s twin 14-line lookups became one `FindCell` with two
  projections. **L-15c** — `EnemyScaling.ApplyHealthMultiplier` now holds the champion-scaling rule
  `WorldEventDirector` and `BossController` each carried a copy of.
  ⚠️ **FOUR THINGS WERE REFUSED, WITH REASONS:**
  1. **L-12 (~40 balance constants → data) — declined entire, not deferred.** `ShakeMath` and
     `HitStop` say in their own headers that they are Godot-free *so the feel curve is
     unit-testable*, and the test project's csproj forbids `GodotObject` construction. **The
     constants are not a workaround for that limit; they are the shape it correctly produced.**
     Moving them to `.tres` would delete the tests that pin the curves to serve an audience of one
     who edits C# here every session.
  2. **L-15b (`HorizontalDistance` ×3) — skipped.** Four pure lines carrying **no rule**: no
     invariant, no balance decision, nothing that can drift. A shared home means a new file plus a
     dependency edge from `Enemies`, `Companions` and `Housing` to delete eight lines — addition
     dressed as deletion. ⚠️ **And the same files flatten Y inline in twelve other places**, so
     consolidating three of fifteen sites is a new inconsistency, not consolidation. **This is the
     line between L-15b and L-15c: duplication that encodes a decision gets one home, duplication
     that encodes a formula does not.**
  3. **`AshenAffliction` was not folded into `EnemyScaling`** though it names itself a third mirror —
     it scales health, power and XP under one removable tag, a different rule that shares a line.
  4. ~~**L-17**~~ — the "53 raw content ids in `VendorPanel`" finding was **retracted 2026-08-15 as
     wrong**: all 47 literals are `Loc.T` **locale keys**, which correctly live in the UI. Moving
     them to `GameIds` would be actively harmful. Recorded so it is not rediscovered and "fixed".
  ⚠️ **A KNOWN BOUND, NAMED RATHER THAN BUILT FOR** (invariant 28): `FlightComponent` is attached at
  build time, so a boss phase that switched to a flying profile at runtime still could not gain
  flight. **Flight is structural, not a knob a phase can turn.**
  📖 The report is the plan file from the audit session, and its §N "DO NOT TOUCH" list is still the
  important half — `CombatMath`, `ShopPricing`/`PriceBreakdown`, `EventBus`, the `.tres` pipeline and
  the UI/gameplay boundary are healthy, and **refactoring them is how the audit does harm.**
- 📖 Economy: `ARCHITECTURE.md` §2.6m is the mechanism, `DESIGN.md` §6 + §6.1 the intent.
  🎨 `docs/ASSET_POLICY.md` §0.2–§0.3 is the asset authority.

## Last verified (session close, 2026-08-15 — audit Wave 4)

| | |
| --- | --- |
| Build | `dotnet build --warnaserror` clean, **0 warnings** — and CI enforces that on every push |
| Tests | **1426 passing**, unchanged. ⚠️ **No test was added and none could be**: every Wave 4 change is either a Godot-`Resource` path (`AIProfileResource`, `AIProfileDatabase.Get`) or a live-`StatsComponent` path, all excluded by the test project's own no-`GodotObject` rule. **That constraint is exactly why L-12 was refused** |
| `--validate` | exit 0; **1351** locale strings, unchanged (no string was touched) |
| **Negative tests** | `python tools/negative_tests.py` — **66/66 broken and restored** (63 → 66 was Wave 2's `ValidateSceneAuthoredIds`; the 2026-08-12 table said 63 and was stale). ⚠️ **No case was added, because Wave 4 added no validator rule** — invariant 6 has nothing to bite here, so the battery was run to prove the existing rules still pass. Tree restored clean, `--validate` exit 0 afterwards. ⚠️ Runs **over two minutes**, mutates `data/` **and** `scenes/`, refuses a dirty tree; killing it mid-run defeats its own `finally` (`git checkout -- data/ scenes/` recovers) |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 33 dialogues, **14 quests**, 64 map locations — **identical to 41A's census**, which is the point of running it |
| **`--play`** | booted, loaded slot1, **33 objects restored**, **10 cells**, **0 errors**. ⚠️ The 3 `CraftingStationComponent` warnings are **pre-existing**. ⚠️ **The six-line "C# backtrace" blocks around them are `PushWarning`'s own trace, not exceptions** |
| **Verbatim proof** | Wave 3's habit, kept: every moved body diffed against its original normalised for renames — `MapService` **14/14 and 14/14** into one `FindCell`, champion scaling **10/10 and 10/10** into `EnemyScaling`. ⚠️ **A green build does not prove a loop survived** |
| Not run, deliberately | ⚠️ **`--panelshots` / `--hudshots` — nothing in these three files reaches a `Control`**, so invariant 8 does not apply. Said rather than skipped silently. ⚠️ **`--economy`** — `EnemyScaling` moves a *combat* multiplier, not a price; no price, spread or multiplier was touched. ⚠️ **`map_probe`** — no map location or cell scene changed; `MapService`'s lookup change is a proved-verbatim body move |
| ⚠️ **Not covered by anything** | **The flight attachment L-14 touches is never exercised by a gate.** No dragon spawns in slot1 and the `F1` console needs keyboard input, so nothing observed a `FlightComponent` being attached. What *is* certain is narrower and worth stating plainly: with no archetype authoring an inline `Profile`, the new expression **reduces to the identical `AIProfileDatabase.Get` call** for every one of the 31 archetypes in `data/`. **Region transitions remain uncovered too** — `MapService` is on that path |
| MCP | ⚠️ Not needed and not probed — nothing was placed in the world and nothing was rendered |
| MCP | ⚠️ **DOWN all session, both halves** (`godot-cli status .` → editor not running, 23630 connection refused). Not needed — nothing was placed in the world. ⚠️ **`.claude/skills/*/SKILL.md` had drifted to `ai-game.dev` URLs and was restored, not committed** — that is invariant 8's Cloud-mode tell, and it means the editor was opened from the project manager at some point |

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
   the minimap and the compass share **one** `MapPins` builder and **one** resolved objective target.
   Two surfaces computing the same answer agree until the day one of them gains a filter.
6. ⚠️ **A RULE PROVEN ONCE IS NOT A RULE PROVEN TODAY** (38V). `tools/negative_tests.py` is the
   answer. ⚠️ **It cannot reach a rule that lives in a code constant.**
7. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT** (37F); **A CACHED POSE IS
   THE SAME BUG WEARING VISUALS** (39B); **A VALUE TYPE CARRYING LAYOUT STATE IS THE SAME BUG WITH NO
   CACHE IN IT** (39.5A — `MapProjection`'s viewport is `(1,1)` until `Resized`). **When a sub-phase
   adds a state, ask what every existing thing does IN that state.** ⚠️ **39.5B is the corollary: the
   HUD had never been asked what it does in the "a menu is open" state.**
8. ✅ **THE UI HAS A SEAT TO RENDER FROM — USE IT.** `--hudshots` (12 states) and `--panelshots` (9)
   found **six** shipped defects across 39.5B/C. ⚠️ **A UI change that has not been captured is not
   verified**, and "reviewed against the API" is the phrase that preceded every one of them. ⚠️ **AND
   A RUNNING EDITOR IS NOT A WORKING MCP** (39.5B): the tell is `.claude/skills/*/SKILL.md` showing
   `ai-game.dev` URLs — that means Cloud mode and every call 503s. `godot-cli close .` then
   `godot-cli open . --mode Custom --url http://localhost:23630 --editor-path <console-LESS .exe>`.
9. ⚠️ **A GODOT PROPERTY WHOSE DEFAULT DIFFERS FROM ITS BASE CLASS'S IS THE DEFECT CLASS THAT PASSES
   EVERY REVIEW** — a `Label` defaults `mouse_filter` to Ignore (38U); a public `Hidden` on a
   `Control` shadows `CanvasItem.Hidden` (39.5A); **a `Button` in a NON-MODAL `UiPanel` is unreachable
   by mouse** (39.5B), because `UiPanel` only frees the cursor for modal panels.
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
    one-line edit. ⚠️ **A cell has no authored size at all** (39.5A).
23. ⚠️ **A MOUNTED BODY MAY NOT PLAY A FULL-BODY CLIP** (39B). **Death is the known remaining hole.**
24. ⚠️ **TWO FUNCTIONS THAT ORDER THE SAME BRANCHES MUST AGREE.** ⚠️ **A price lookup fails CLOSED.**
25. ⚠️ **A RESTORED FILE WITH AN OLD TIMESTAMP DOES NOT REBUILD** (39A). `touch` before `dotnet build`.
26. ⚠️ **A COMPUTED LOCALE KEY IS INVISIBLE TO EVERY DATA-DRIVEN CHECK** (39.5A/B). `map.category.*`,
    `hud.compass.*` and `hud.unit.*` are picked from a value, not authored at a call site.
    `ValidateMapTaxonomyIsNamed` and `ValidateHudComputedKeys` are the pattern: **enumerate the
    declared set, not the set today's data happens to reach.**
27. ⚠️ **MOVING A FEATURE IS INDISTINGUISHABLE FROM DELETING IT** (39.5A). **Ask what state the player
    is in when the new path does not exist yet.** ⚠️ **This is also why 39.5B did not build the
    brief's idle auto-hide.**
28. ⚠️ **A CUT SYSTEM LEAVES NO STUB, AND "THE GAME HAS NO SUCH STATE" IS A LEGITIMATE ANSWER.**
    **This is the rule the repo cites as "40B's rule", and this is its home now that Phase 40 is
    struck** — the ~10 citations elsewhere still resolve, to a preserved entry. Worked examples:
    39C's climb/swim, 39.5A's districts, 39.5B's cut `HudMode.Dead` (respawn is synchronous, so there
    is no window for a death HUD) and its cut combat HUD (**there is no `InCombat` flag anywhere in
    `src/`** and the HUD may not invent one). ⚠️ **Phase 40 is the largest worked example**: striking
    a phase means deleting its stubs and settling every pointer at it, and **the grep is the
    deliverable**. **Check whether the state exists before building the presentation of it.**
29. ⚠️ **"ALREADY SHIPPED AND CHECKED" IS NOT "GOOD", AND AN AUDIT THAT ONLY READS CODE CANNOT TELL
    THE DIFFERENCE** (39.5B, maintainer direction). ~60 of 80 HUD sections were marked satisfied,
    correctly — **four defects were sitting in them anyway**, every one a *presentation* fact, and
    presentation facts are invisible to builds, tests, validators and code review alike. **When the
    question is quality rather than correctness, render it and look.**
30. ⚠️ **A DEFERRED CONDITION IS A HYPOTHESIS, AND IT CAN BE WRONG IN THE RIGHT NEIGHBOURHOOD**
    (39.5C). Clustering was gated on "two `Detail` markers overlap"; the closest pair is 2.13 m and
    they never do — while their **labels** collided badly enough to render seven names as one pile.
    **Measure the condition, then look at the area anyway.**
31. ⚠️ **WHEN A NEW SURFACE ANSWERS AN OLD SURFACE'S QUESTION, TAKE THE QUESTION AWAY FROM THE OLD
    ONE** (39.5C). The compass carried discovered-place ticks for two sub-phases after the minimap
    made that job redundant, and **both surfaces got worse**. Adding a widget is the moment to ask
    what it makes unnecessary elsewhere.
32. ⚠️ **A `VBoxContainer` RESOLVES OVERFLOW BY CRUSHING WHOEVER HAS `ExpandFill`** (39.5C). **Bound
    the section that grows, and give anything with `ExpandFill` a `CustomMinimumSize` floor too.**
33. ⚠️ **A COLOUR TOKEN CAN BE WRONG AND NOTHING WILL EVER SAY SO.** `Trough` sat within a rounding
    error of `CardBg` for multiple phases, making every bar in the game unreadable, and it passed the
    contrast tests because those check *text* pairs. ⚠️ **When two tokens are used together, the pair
    is the thing to check** — the depth scale (`WellBg`/`PanelBg`/`CardBg`) already encodes which
    pairs are meant to be distinguishable.
34. ⚠️ **A MISSING LOCALE KEY WHOSE NAME READS AS ENGLISH IS SILENT IN EVERY INSTRUMENT** (41A).
    `Loc.T` returns the key unchanged on a miss, so a `.tres` authoring `Title = "Gather Iron"`
    renders "Gather Iron" — correct on screen, in the journal, on the tracker and in a captured
    frame — and breaks on the first non-English locale. Two live quests carried this for twenty-nine
    phases. **The only rule that catches it asks *is this string a key*, not *does this string
    render*, and it must check presence in the catalogue** (which also catches a mistyped key, the
    failure that survives a rename). ⚠️ **Its sibling: a fallback nothing reaches is a defect nothing
    reports** — `ShortLabel()` was one line from putting `enemy.goblin` on screen.
35. ⚠️ **WHEN A NEW STATE ARRIVES, ASK WHICH EXISTING EVENT ACTUALLY MEANS IT** (41A). A new objective
    type is a few lines, because `Advance` is one choke point and the events already exist; the whole
    job is the semantics. "Discovered" is not "arrived" — `RevealWithCell` means *entered the region*
    — so the free-looking reuse would have shipped a quest that completes itself on region entry.
    **The cheap implementation and the correct one differ by a question nobody is forced to ask.**
36. ⚠️ **A ROUTE THAT SKIPS THE RESTORE IS STILL A LOAD, AND NOTHING SAID SO** (audit 2026-08-15).
    Position and region came back in `StartLoadedGame` only — **F9 and the pause menu called
    `SaveManager.LoadGame` directly and got the world rewound around a player who never moved.**
    The restore now lives *inside* the load (`SaveManager.LocationApplier`), so a fourth route
    inherits it. **⚠️ AND A PARTIAL RESTORE IS A FAILED LOAD:** `LoadGame` caught each saveable's
    exception and still returned `true`, so all-34-threw was indistinguishable from clean and the
    next autosave wrote it over the good file. It returns `false` now and **every caller drops to
    the title** — a half-restored world is not a world to resume into.
37. ⚠️ **A KNOB YOU VALIDATE IS A CLAIM THAT THE KNOB WORKS** (audit 2026-08-15).
    `BossPhaseResource.AiProfileId` was authored, validated by `ContentValidator`, and **inert** —
    `EnemyAIComponent` resolved its profile once in `OnInitialize` and cached it, so the phase-change
    write landed on a field nothing read again. Masked only because all nine authored phases set it
    to `""`. **Verify the read path exists before writing the validator rule**, or the first author to
    use the field gets a green gate and no behaviour.


## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before running — nothing else recompiles C#
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
python tools/negative_tests.py                  # proves the gate still bites (58 cases, >2 min)
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
