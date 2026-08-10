# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C. Phase 38 (economy) is ✅ CLOSED — 38A–38V, all twenty-two sub-phases.**
- **37E landed out of band** (maintainer direction, 2026-08-10): the player's house was a sealed mesh
  beside a roofless grey pen. It is now **a real, enterable cottage in its own cell** —
  `ember_crown.ashfall_homestead` at `(52, 0, 46)`, east of the Embermarket — furnished, with a
  workshop, a garden and a bed you can sleep in. `docs/playbook/phase-37.md` carries the retrospective.
- **37F landed out of band** (maintainer direction, 2026-08-10): four reported runtime errors traced
  and fixed — two of them **one bug** — plus the boss arena rebuilt from a grey box into a ruined
  stone amphitheatre. ⚠️ **It also found that the Iron King's body is a man in an orange bomber
  jacket**. **37G then fixed it**: he is a crowned, armoured king at 2.605 m, with the clip bindings
  pinned by test because a mis-bound animation slot is silent.
- **Next: Phase 39 (Mounts & Traversal), starting at 39A** — `MountComponent`, summon/mount/dismount
  and mounted locomotion. Open `docs/playbook/phase-39.md`. ⚠️ **39A is not starting from nothing:**
  38D's `ServiceKind.Stable` already sells a mount and records it in a story flag, so 39A owns the
  mount itself and not the purchase.
- **Nothing is parked.** ⚠️ If a future item is parked, park it with a check someone can run, not a
  verdict — 38G sat eleven sub-phases past its own condition because the notice named a conclusion.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred** — cosmetic, healer, passage,
  warehouse (38R) and courier (38R2). Each reason is in the playbook and `DESIGN.md` §6. Two of the
  five already existed under another name. **Do not rebuild them.**
- 📖 **The economy is now documented where you would look for it:** `ARCHITECTURE.md` §2.6m is the
  mechanism, `DESIGN.md` §6 + §6.1 the intent and the Phase 56 balance handoff.
- 🎨 **The asset migration onto the four Quaternius MegaKits is complete** (A–E). `docs/ASSET_POLICY.md`
  §0.2–§0.3 is the authority.

## Last verified (session close, 2026-08-10)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1297 passing** (37F added 6, 37G added 8) |
| `--validate` | exit 0 |
| **Negative tests** | `python tools/negative_tests.py` — **42/42 rules broken and restored**, each caught by its own refusal. **Run this after moving authored numbers, not only after changing code** |
| `--economy` | **byte-identical** to 38U's |
| `--state` | 2 regions, **15 cells**, 63 items, 23 shops, **15 services**, 8 contracts, 33 dialogues, 14 quests |
| **`--play`** | booted, loaded slot1, all **10 cells resident**, **32 objects restored**, **0 project errors and no save warnings at all** — and the log shows a live shortage of ore at the mine, which is 38T running in-world. ⚠️ One `WASAPI: GetBufferSize` line is the **Windows audio driver**, not the project |
| Saved state | 38V adds none |
| Rendered | **the arena day and dusk from 7 positions**, the Iron King in the arena from 5 and every candidate body front and back, and (37E) the homestead from 12 — the approach, the doorway, inside in both directions, the workshop, the yard. Three defects found and fixed that were invisible in the `.tscn` |
| Bodies retargeted | 12 of 12 skinned (`fp_arm` has no skin and needs none) |
| Props with no collider | 0 (audit clean) |

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
   **`ShopPricing`'s own multiplication order** (`ARCHITECTURE.md` §2.6m spells it out) — reordering
   those multiplies is a silent off-by-one gold and one test catches it.
6. ⚠️ **A RULE PROVEN ONCE IS NOT A RULE PROVEN TODAY** (38V). `ValidateShopTrade`'s band tightened
   twice after its original negative test — 38S folded in the haggle, 38T asked it at the shocked
   extremes — so the run that proved it was proving a rule the repo no longer had.
   **`tools/negative_tests.py` is the answer and it is re-runnable.** ⚠️ It edits `data/` in place
   and refuses to start on a dirty tree; that refusal is the guard working.
7. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT** (37F). A
   `CharacterBody3D` keeps its velocity between frames, so a single non-finite write poisons that
   body for the rest of the run and surfaces as a crash in whatever system moves it next — two
   reports, two subsystems, one value. ⚠️ **Guard where a value enters the stateful thing, not where
   it explodes**; those are never the same place. `MotionSafety` + the guards in
   `LocomotionComponent.Move` are the pattern, and **they log once per body** so the still-unproven
   source stays findable.
8. ⚠️ **A MODEL THAT READS AT 20 m CAN DOMINATE AT 4 m** (37E). A 3 m waystone that looks right on a
   road stood as a monolith blocking the player's own front door; the lamp post moved twice for the
   same reason. ⚠️ **Render the APPROACH, not just the object** — every 37E defect was a correctly
   authored model in a position that ruined the shot, and the isolated render was clean three times
   while the walk-up was wrong. ⚠️ **A roof kills the sun**: a chandelier is a mesh and emits nothing,
   so an interior needs real lights or it renders as a cave.
9. ⚠️ **A GODOT `Label` DEFAULTS `mouse_filter` TO IGNORE, SO A TOOLTIP ON ONE IS SILENTLY DEAD**
   (38U). `UiTheme`'s text builders set **`Pass`** — hoverable without becoming clickable. **A
   property whose default differs from its base class's is the defect class that passes every review.**
10. ⚠️ **DERIVE, THEN BOUND — TWO MECHANISMS, NEITHER SUBSTITUTING FOR THE OTHER.** Everything a
   quickload could reroll is a pure function of the day, so a reload **replays** it; a ledger stores
   only **what the player did**. ⚠️ `string.GetHashCode()` is randomised per process; `StableRoll` is
   a hand-written FNV-1a. ⚠️ **GDScript can only call methods whose signatures marshal** — probe
   `has_method` before writing a `.gd` harness. ⚠️ **Nothing about a contract may reach the quest log.**
11. ⚠️ **A SERVICE CAN BE FIRED FROM A CONVERSATION, EXCEPT A BANK** — a bank opens the **host
   entity's** inventory and a conversation has no host entity. ⚠️ **An entity still gets one
   interactable**: the realm's only bed is reachable through `dialogue.innkeeper` and nowhere else.
   ⚠️ **A world interaction prompt is not a `Control` and has nothing to hover.**
12. **A broker fronts nothing, so no purse and no saturation apply to her.** ⚠️ **A stack at a broker
    is a multiply and a stack at a counter is 38H's decaying sum** — the one place in the game where
    unit price × quantity is the wrong number.
13. **`contraband` is the one trade tag that fails CLOSED**, and a fenced sale moves two factions
    **once per sale, never per unit**. The Crossway toll is charged in `GameBootstrap.PayToll`.
14. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Seven firings.
    38R's is the plainest: an NPC on open ground stood exactly where the player stands to read the
    previous sub-phase's board, and the fault was in neither object and in no line of either `.tscn`.
    ⚠️ **A body's facing is not knowable from a file.** Earlier: `prp_banner_guild` by precedent
    (38Q2), `prp_weapon_stand` reading as a sawhorse (38Q), a lying-down collider on a standing
    pillar (38O), 4-of-6 rejections in 38N2, `npc_townsman` hi-vis, `npc_merchant_f` in trainers.
    ⚠️ **When no pack model fits, primitives are a legitimate answer.**
15. ⚠️ **A DECISION CAN LIVE IN A `.tres` HEADER, AND NOTHING GREPS THOSE.** 38R's warehouse was
    killed mid-session by a comment in `EmberCrownBank.tres`. **Before authoring content of a kind
    that already exists, read the existing one's header.**
16. **`CharacterBody3D` has no step-up.** Verticality comes from props, never from terrain; a 30 cm
    kerb is an invisible wall the navmesh happily paths NPCs over. ⚠️ **Phase 39C inherits this
    one** — it is the phase that decides whether that stays true.
17. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — all 12 skinned
    bodies are. An un-retargeted rig given it freezes mid-guard with nothing logged. ⚠️ A model whose
    root carries a translation cannot be retargeted until normalised, and the library is an
    **upper-body pose from the hips up**. `ASSET_POLICY.md` §0.2 carries all four traps.
18. **Check what is already vendored before pulling from the web.** As of 38O the library holds **no
    unadopted CC0 medieval bodies at all**.
19. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL.** Apply node scale *and*
    `nodes/root_scale` from the `.import`. ⚠️ A **solid-looking prop with no collider** is a real
    class of defect here. ⚠️ **The `rts` pack is roughly 1/6 scale and nothing in the files says so.**
20. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1**, and scale
    lives in each prop's `.import`, never in a cell transform. **Grass goes in patches.**
21. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
    blacksmith and the inn stay monolithic; the kit ships one wall height, 3.12 m.
22. ⚠️ **The Ember Crown map was re-laid (38F).** The settled cells form one contiguous city and every
    wilds cell is outside it; the arena moved to 150 m, past the gate. ⚠️ **A schedule carries a copy
    of its cell's `Center` as `Origin`** — moving a cell is never a one-line edit.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before running — nothing else recompiles C#
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
python tools/negative_tests.py                  # proves the gate still bites (42 cases)
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
godot-cli status .                              # the Godot MCP probe — it is DOWN every session start
```
