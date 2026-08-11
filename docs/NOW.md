# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C. Phase 38 (economy) is ✅ CLOSED — 38A–38V.** 37E/37F/37G landed out of band after it
  (the player's cottage, four runtime errors + the arena rebuild, and the Iron King's body).
- **Phase 39 (Mounts & Traversal) is ✅ CLOSED — 39A, 39B, 39C.** `Y` whistles a horse up; riding is
  1.7x with the gallop on the horse's own pool; melee works from the saddle and a gallop is a charge;
  a mount makes a **local** fast-travel jump free. ⚠️ **The mount is a state of the rider, not a
  second body** — there is no horse entity to hit, aim at, or knock the player off.
- **39C answered invariant 16: a body now steps up 0.5 m**, matched to the navmesh's own
  `agent_max_climb`, and the Embermarket plaza dais the world had deleted to live without it is back
  as the realm's only raised ground. ⚠️ **CLIMB AND SWIM ARE CUT, NOT DEFERRED, AND EACH HAS A
  CONDITION:** swim lands when a cell authors a water **volume with something on the far side**
  (today both water planes are decals with 32 m of empty lake beyond); climb lands when a cell
  authors a surface reachable no other way. Neither left a stub, a flag or a dead export.
  **Do not build either speculatively.**
- **Next: Phase 40 (Survival & Needs) — a decision phase, starting at 40A.** ⚠️ **40A owns the
  repair/durability call** that 38D deferred to it, and `docs/DESIGN.md` §6's sink table has an empty
  "Repair — pending 40A" row waiting on it. ⚠️ **40B's rule is that a cut system leaves no stub** —
  39C is the worked example: two verbs cut, zero code left behind.
- ⚠️ **DYING WHILE MOUNTED IS A KNOWN GAP, NAMED RATHER THAN FIXED (39B).** 39B stopped the body
  playing full-body one-shots while riding — a standing clip on a seated offset makes the rider stand
  up *inside* the horse — but **death is not a one-shot and still plays**, and nothing dismounts on
  death. Expect the corpse to do exactly what the swing did. Whoever touches mounts next owns it.
- **Nothing is parked.** ⚠️ If a future item is parked, park it with a check someone can run, not a
  verdict — 38G sat eleven sub-phases past its own condition because the notice named a conclusion.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred** — cosmetic, healer, passage,
  warehouse (38R) and courier (38R2). Each reason is in the playbook and `DESIGN.md` §6. Two of the
  five already existed under another name. **Do not rebuild them.**
- 📖 **The economy is documented where you would look for it:** `ARCHITECTURE.md` §2.6m is the
  mechanism, `DESIGN.md` §6 + §6.1 the intent and the Phase 56 balance handoff.
- 🎨 **The asset migration onto the four Quaternius MegaKits is complete.** `docs/ASSET_POLICY.md`
  §0.2–§0.3 is the authority.
- ⚠️ **Maintainer action outstanding, found by 39A and not caused by it:** all 42
  `.claude/skills/*/SKILL.md` were regenerated on disk pointing at **`https://ai-game.dev/mcp/...`**
  instead of `http://localhost:23630`. That is the vendor's hosted cloud, which CLAUDE.md §2 says is
  not to be switched to without asking. **39A reverted them and did not commit the change**, but the
  editor is presumably still in Cloud mode — which also explains `godot-cli status .` reporting the
  local MCP server unreachable while a Godot editor was running.

## Last verified (session close, 2026-08-10)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1329 passing** (39A 12, 39B 9, 39C 12) |
| `--validate` | exit 0 |
| **Negative tests** | `python tools/negative_tests.py` — **45/45 rules broken and restored**, each caught by its own refusal. ⚠️ **It now runs longer than two minutes**; killing it mid-run defeats its own `finally` restore and leaves the tree mutated (`git checkout -- data/ scenes/` recovers). ⚠️ **It edits `scenes/` too as of 39C**, and its clean-tree guard covers both. **Run this after moving authored numbers, not only after changing code** |
| `--economy` | **price landscape identical**; the only moved line is the locale string count (1187 → 1188). ⚠️ Diff the report body, not the loader chatter — a travel fee is not a shop multiplier and must never reach `ShopPricing` |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, **15 services**, 8 contracts, 33 dialogues, 14 quests — unchanged |
| **`--play`** | booted, loaded slot1, **32 objects restored, zero project errors**. ⚠️ One `no usable entry for 'mount:player'` warning: expected and self-healing — every new `ISaveable` says it once against a save older than itself. ⚠️ `WASAPI: GetBufferSize` is the Windows audio driver, and exit-time `PagedAllocator`/RID-leak lines are the forced kill, not the project |
| Rendered | the plaza dais day and dusk from 3 positions (approach, on top, the kerb from a metre); the mount **8 ways** via `tools/mount_shots.gd` — front, back, side, the walk-up with stalls and townspeople around it, overhead, **and both camera seats** (first-person eye, third-person rest offset) **and the swing**. ⚠️ Four seat iterations were needed and the first horse was **twice the height of a two-storey house** |
| **Step-up** | `godot --headless --path . --script res://tools/stepup_probe.gd` — a real body, real geometry, **both directions**: climbs the 0.3 m dais (rose 0.301), refuses the 11 m bell tower (rose 0.000). Exits 0/1, so it is a gate |
| Not verified | ⚠️ **no key has ever been pressed.** `Y`, the toggle, the toasts, the dev command, the gallop drain in motion, the charge bonus, the `HitReaction` fix the travel discount on the map screen, and how the step **feels** under a hand are all proved by test and by reading. ⚠️ **No NPC was watched climbing the dais**, and the step now runs for every walking actor while only the player's case was exercised. The renders reproduce the component's transforms and clips in a harness; they are not `MountComponent` assembling them |

## Live invariants — the things that will bite you

1. **A region loads whole.** Every cell of the active region is resident; `RegionStreamer` has no
   distance test and no unload during play. A new cell is permanently in the tree.
   ⚠️ Both *regions* cannot be resident together — their cells share coordinate space (Phase 44).
2. ⚠️ **`sell <= LOCAL value <= buy` holds at every shop by construction, and 38G made that a
   narrower claim than it sounds.** A round trip at **one** shop always costs. A carry from a surplus
   to a demand is *meant* to pay, and `--economy` names the routes that do — **do not "fix" a
   positive margin in that table.** Treat `sell > buy` **at one shop** as a defect.
3. ⚠️ **A DEMAND TABLE IS A FLOOR UNDER OTHER PEOPLE'S RULES.** A contract reward, a commission fee
   and a fence's margin are all measured against *what the best buyer pays*, and that moves when a
   cell authors a tag. ⚠️ **`ShopResource.CellId` is empty by default and empty means par.**
4. ⚠️ **WHEN A NEW PRICE APPEARS, ASK WHAT IT IS A SPREAD OVER** — and every new multiplier joins
   `NoCombinationOfMultipliersLetsSellingBeatBuying` or it does not ship. A clamp stops a money
   printer, not a free round trip; `ValidateShopTrade`'s margin rule is what stops that one, and
   **every price the player can move directly is evaluated at Allied.**
5. ⚠️ **THE EXPLANATION IS THE CHARGE** (38U). `PriceBreakdown.Total` is what the vendor window, the
   commission desk and the map screen display *and* charge. Its lines accumulate factors in
   **`ShopPricing`'s own multiplication order** (`ARCHITECTURE.md` §2.6m) — reordering those
   multiplies is a silent off-by-one gold and one test catches it.
6. ⚠️ **A RULE PROVEN ONCE IS NOT A RULE PROVEN TODAY** (38V). **`tools/negative_tests.py` is the
   answer and it is re-runnable.** ⚠️ It edits `data/` in place and refuses to start on a dirty tree;
   that refusal is the guard working. ⚠️ **It cannot reach a rule that lives in a code constant** —
   39A's model-path rule had to be broken by hand, and doing so found that the rule catches a wrong
   path but **not a deleted file** (the `.import` sidecar keeps `ResourceLoader.Exists` true).
7. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT** (37F). A
   `CharacterBody3D` keeps its velocity between frames, so a single non-finite write poisons that
   body for the rest of the run and surfaces as a crash in whatever system moves it next.
   ⚠️ **Guard where a value enters the stateful thing, not where it explodes.** `MotionSafety` +
   `LocomotionComponent.Move` are the pattern, and they **log once per body**.
   ⚠️ **A CACHED POSE IS THE SAME BUG WEARING VISUALS** (39B): `HitReactionComponent` cached the body
   mesh's rest position at spawn, 39A moved that mesh to the saddle, and the first hit taken while
   mounted dropped the rider through the horse permanently. **When a sub-phase adds a state, ask what
   every existing component does IN that state — the answer is a list of things that cached
   something.** A green build/test/validate/economy/play/render battery walked past it six ways,
   because none of those things hits the player while mounted.
8. ⚠️ **A MODEL THAT READS AT 20 m CAN DOMINATE AT 4 m** (37E) — and **39A adds the next turn of it:
   RENDER FROM THE SEAT, NOT AT THE OBJECT.** Every exterior shot of the mount was correct while the
   first-person eye sat *inside the horse's neck*; a defect that lives at a camera position is only
   visible from that camera position. ⚠️ **A roof kills the sun**: an interior needs real lights.
9. ⚠️ **A GODOT `Label` DEFAULTS `mouse_filter` TO IGNORE, SO A TOOLTIP ON ONE IS SILENTLY DEAD**
   (38U). `UiTheme`'s text builders set **`Pass`**. **A property whose default differs from its base
   class's is the defect class that passes every review.**
10. ⚠️ **DERIVE, THEN BOUND — TWO MECHANISMS, NEITHER SUBSTITUTING FOR THE OTHER.** A quickload
   **replays** what the day determines; a ledger stores only **what the player did**.
   ⚠️ `string.GetHashCode()` is randomised per process; `StableRoll` is a hand-written FNV-1a.
   ⚠️ **GDScript can only call methods whose signatures marshal** — probe `has_method` first.
   ⚠️ **Nothing about a contract may reach the quest log.**
11. ⚠️ **A SERVICE CAN BE FIRED FROM A CONVERSATION, EXCEPT A BANK** — a bank opens the **host
   entity's** inventory and a conversation has no host entity. ⚠️ **An entity still gets one
   interactable.** ⚠️ **A world interaction prompt is not a `Control` and has nothing to hover.**
12. **A broker fronts nothing, so no purse and no saturation apply to her.** ⚠️ **A stack at a broker
    is a multiply and a stack at a counter is 38H's decaying sum.**
13. **`contraband` is the one trade tag that fails CLOSED**, and a fenced sale moves two factions
    **once per sale, never per unit**. The Crossway toll is charged in `GameBootstrap.PayToll`.
14. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Seven firings.
    38R's is the plainest: an NPC on open ground stood exactly where the player stands to read the
    previous sub-phase's board, and the fault was in neither object and in no line of either `.tscn`.
    ⚠️ **A body's facing is not knowable from a file.** ⚠️ **When no pack model fits, primitives are
    a legitimate answer.**
15. ⚠️ **A DECISION CAN LIVE IN A `.tres` HEADER, AND NOTHING GREPS THOSE.** 38R's warehouse was
    killed mid-session by a comment in `EmberCrownBank.tres`; 39A's whole brief was in
    `EmberCrownStable.tres`'s. **Before authoring content of a kind that already exists, read the
    existing one's header.**
16. ⚠️ **`CharacterBody3D` STILL has no step-up; `LocomotionComponent` does** (39C). Every walking
    actor climbs up to **0.5 m**, matched to the navmesh's `agent_max_climb` and pinned to it by a
    `--validate` rule. ⚠️ **RAISED GROUND IS NOW POSSIBLE, NOT FREE:** the plaza dais is the realm's
    only piece of it and it is the only ground skin with a collider — everything else is a 5–6 cm
    decal, which is *beneath* the step's own minimum and is caught on rather than climbed.
    ⚠️ **A step must be legible from a metre away or it is a trip hazard** — the dais needed a
    hue change, not a value change, to read at 0.3 m. ⚠️ Verticality still comes mostly from props.
    ⚠️ **The step is simulated by the engine, never computed**: a capsule's rounded bottom catches a
    step's *corner*, so arithmetic under-reports every real step (0.3 m measured as 0.156 m) and
    unit tests will happily agree with it. `tools/stepup_probe.gd` is the check, both directions.
17. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — all 12 skinned
    bodies are; an un-retargeted rig given the shared library freezes mid-guard with nothing logged.
    ⚠️ **An unresolved clip slot is silent**, so a slot whose *choice* matters (39A's `ride`, which
    picks between two seated poses) is pinned by test, not asserted non-empty.
18. **Check what is already vendored before pulling from the web.** As of 38O the library holds **no
    unadopted CC0 medieval bodies at all** — but it does hold twelve animals, and 39A's horse came
    from there.
19. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL.** Apply node scale *and*
    `nodes/root_scale`. ⚠️ **The `animals` pack's armatures carry a 100x scale, so its accessors read
    ~4.8 m for a horse** — measure the *imported* scene. ⚠️ **And `global_transform` returns IDENTITY
    with an error for a node added during `_initialize`**, which reports the raw bind-pose box as
    confidently as the right answer: `await process_frame` twice first.
20. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1**, and scale
    lives in each prop's `.import`, never in a cell transform. **Grass goes in patches.**
21. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
    blacksmith and the inn stay monolithic; the kit ships one wall height, 3.12 m.
22. ⚠️ **The Ember Crown map was re-laid (38F).** The settled cells form one contiguous city and every
    wilds cell is outside it; the arena moved to 150 m, past the gate. ⚠️ **A schedule carries a copy
    of its cell's `Center` as `Origin`** — moving a cell is never a one-line edit.
23. ⚠️ **A MOUNTED BODY MAY NOT PLAY A FULL-BODY CLIP** (39B). The library has no mounted animation,
    and a standing clip puts the hips ~0.5 m above the seated pose the saddle offset was measured
    against — so an attack or a flinch does not bend the rider, it **stands him up inside the horse**.
    `CharacterAnimationComponent.PlayOneShot` refuses one-shots while `Riding`; **death is not a
    one-shot and is the known remaining hole.** The real fix is an `AnimationTree` with a
    bone-filtered upper-body layer, which is a sub-phase.
24. ⚠️ **TWO FUNCTIONS THAT ORDER THE SAME BRANCHES MUST AGREE, OR THE SCREEN EXPLAINS A NUMBER IT
    DID NOT CHARGE** (39B). `TravelFee.For` picks the fee and `PriceBreakdown.Travel` picks the
    reason; owned-land and mounted are both free, so the two orderings are a real decision and a test
    pins it. ⚠️ **A price lookup fails CLOSED** — an unresolvable service charges rather than waives.
25. ⚠️ **A RESTORED FILE WITH AN OLD TIMESTAMP DOES NOT REBUILD** (39A). Undoing a deliberate break
    with `mv file.bak file` and rebuilding prints **"Build succeeded"** and then runs the *broken*
    binary. `touch` before `dotnet build` — it is CLAUDE.md §2's stale-binary trap through a door
    that section does not name.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before running — nothing else recompiles C#
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
python tools/negative_tests.py                  # proves the gate still bites (45 cases, >2 min)
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
godot --path . --script res://tools/mount_shots.gd        # measure + render the mount (needs a GPU)
godot --headless --path . --script res://tools/stepup_probe.gd  # step-up gate, exit 0/1
godot-cli status .                              # the Godot MCP probe — it is DOWN every session start
```
