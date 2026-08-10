# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C, Phase 38 (economy). 38A–38R2 done. Next: 38S — Haggling + merchant memory.**
- Open the plan: `docs/playbook/phase-38.md`, the `38S` entry. Read the two entries above it too —
  the "two things worth carrying" lines are the cheapest bug prevention in the repo.
- ⏸ **38G is parked, not next.** It prices goods by settlement demand and sits above 38S in the file.
  Do not trust the first unchecked box. Nothing since 38N has unparked it: a fence changes *who will
  buy*, a broker *how much one counter pays*, an appraiser only *what the player is told*, and neither
  a commission, a contract, a hired sword nor a throw of the bones is a route at all — none of them
  moves an item's **value** by settlement, so every margin in `--economy` is still negative.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred** — cosmetic, healer, passage,
  warehouse (38R) and courier (38R2). Each reason is in the playbook and `DESIGN.md` §6. Two of the
  five already existed under another name. **Do not rebuild them.**
- 🎨 **The asset migration onto the four Quaternius MegaKits is complete** (A–E). `docs/ASSET_POLICY.md`
  §0.2–§0.3 is the authority.

## Last verified (session close, 2026-08-09)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1237 passing**; 38R2 added 11 (`WagerRulesTests`) — the first genuinely testable logic since 38Q2 |
| `--validate` | exit 0; **all six new 38R2 rules negative-tested both ways** |
| `--economy` | **byte-identical before and after 38R2** — a wager is not a route; every margin still negative (38G's job) |
| `--state` | 2 regions, 14 cells, 63 items, 23 shops, **14 services**, 8 contracts, 33 dialogues, 14 quests, 2 companions |
| **`--play`** | booted, loaded slot1, streamed all 9 cells, 27 objects restored, **0 project errors** |
| `WagerLedger` | save/load round trip **actually run** headlessly, including the §7 replace-never-merge rule |
| Cells rendered | the Hollowreach bones table (38R2), noon and dusk, four positions, before and after its two barrels |
| Bodies retargeted | 12 of 12 skinned (`fp_arm` has no skin and needs none) |
| Props with no collider | 0 (audit clean) |

## Live invariants — the things that will bite you this arc

1. **A region loads whole.** Every cell of the active region is resident; `RegionStreamer` has no
   distance test and no unload during play. A new cell is permanently in the tree.
   ⚠️ Both *regions* cannot be resident together — their cells share coordinate space (Phase 44).
2. **`sell <= value <= buy` holds at every shop by construction**, so carrying goods between two
   merchants can *never* turn a profit. `--economy` prints the proof. Only 38G's regional demand can
   change it — do not try to author around it with a generous spread.
3. ⚠️ **A COMMISSION IS THE FIRST PRICE THOSE CLAMPS DO NOT PROTECT** (38Q). Every other price is a
   spread over *one* item's value; a commission relates **two** — ingredients in, a finished piece out
   — and crafting is meant to add value, so buy-materials → commission → sell is an unbounded loop
   that no clamp closes. Only the authored labour fee does, and `--validate` proves it every boot via
   `CommissionRules.Exploitable` at the cheapest standing on the ramp. **A new recipe, a keener buyer
   or a new specialty can each reopen it.** General form: when a new price appears, ask what it is a
   spread *over* before assuming the 38A clamps cover it.
4. ⚠️ **THE CONTRACT BOARD IS DERIVED FROM THE DAY AND NEVER STORED** (38Q2). `ContractRules.Cycle` +
   a fixed scramble is the whole rotation, so the same day always shows the same postings and a
   quickload cannot reroll them. `ContractLedger` saves **only what the player filled**, never what was
   offered. ⚠️ The reward rule is the **mirror** of the commission one: a contract is refused for
   paying *less* than the best buyer, with **no ceiling**, because what bounds it is being fillable
   once per rotation. ⚠️ **Nothing about a contract may reach the quest log.**
5. ⚠️ **A SERVICE CAN NOW BE FIRED FROM A CONVERSATION, EXCEPT A BANK** (38R).
   `DialogueEffect.OpenService` runs the whole 38D battery through `ServiceComponent.TryUse` — but a
   `Bank` opens the **host entity's** inventory and a conversation has no host entity, so `--validate`
   refuses that authoring outright. ⚠️ **An entity still gets one interactable**: the innkeeper's
   `ServiceComponent` was *deleted* to put his conversation back, and the realm's only bed is now
   reachable through `dialogue.innkeeper` and nowhere else.
6. ⚠️ **TWO SERVICES ARE CHARGED AFTER THEIR VERB AND EVERY OTHER ONE BEFORE** (38Q, 38R): a
   commission fails on a full pack, a hire fails on a full party. Only the commission needs a
   rollback — a hire resolves inside the same synchronous call as its affordability check, so the
   purse cannot move. ⚠️ Both must be **PRICED**, which inverts 38O's free-service rule (a fee fails
   closed on the player who needs the counter) because both hand over *goods*. A free mercenary is
   `DialogueEffect.RecruitCompanion`, which already exists and is how Kael joins.
7. ⚠️ **A WAGER IS THE FIRST PRICE THE PLAYER CAN WIN, AND ITS RULE IS CHECKED AT *ALLIED*
   STANDING** (38R2). `PriceOf` discounts the stake and nothing discounts the payout, so a table that
   is a sink at Neutral can be a printer at the top of the ramp — `--validate` refuses any house whose
   payout × chance reaches its stake, asked at the cheapest stake. ⚠️ **The outcome is derived
   (`WagerRules.Won`), so a quickload replays a loss; what BOUNDS the game is the throws-a-day count in
   `WagerLedger`.** Those are two separate mechanisms and neither substitutes for the other. ⚠️ And
   the seed folds the house id in with a hand-written FNV-1a: **`string.GetHashCode()` is randomised
   per process** and would make the same day pay differently after a restart.
8. **A broker fronts nothing, so no purse and no saturation apply to her** (38P). `VendorPanel.Consign`
   calls neither `TakePurse` nor `Absorb` nor `FenceStanding`, and all three absences are the feature.
   ⚠️ **`EconomyReport.BestBuyers` skips a consignment house and that is not optional** (38P2).
9. **`contraband` is the one trade tag that fails CLOSED** (38O). Every other tag is a filter a shop
   may opt out of; that one is a door a shop must opt *in* to. One branch in `TradeTags.Accepts`.
10. **A fenced sale moves two factions, once per sale, never per unit** (`ShopResource`'s four
   contraband fields, applied in `VendorPanel.Sell`). Deliberately the opposite granularity to 38H's
   per-unit payout decay.
11. **The Crossway toll is charged in `GameBootstrap.PayToll`**, on portal crossings only. Fast travel
   pays `TravelFee` and nothing else; one journey does not pay twice.
12. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Seven firings now.
   38R's is the plainest: an NPC placed on open ground stood exactly where the player stands to **read
   last sub-phase's board**, and the fault was in neither object and in no line of either `.tscn`.
   ⚠️ **A body's facing is not knowable from a file** — this one faces +Z, so the identity transform
   pointed her backwards, and only two opposed eye-level shots settled it. Earlier: `prp_banner_guild`
   chosen by precedent (38Q2), `prp_weapon_stand` reading as a sawhorse and Bryn standing inside his
   own counter (38Q), a lying-down collider on a standing pillar (38O), 4-of-6 rejections in 38N2,
   `npc_townsman` hi-vis, `npc_merchant_f` t-shirt-and-trainers.
   ⚠️ **When no pack model fits, primitives are a legitimate answer** — the Crossway board is three
   `BoxMesh`es in the cell's own vocabulary.
13. ⚠️ **A DECISION CAN LIVE IN A `.tres` HEADER, AND NOTHING GREPS THOSE** (38R). 38R's warehouse
   survived the brief, the plan and a review, and was killed mid-session by a comment in
   `EmberCrownBank.tres` ruling out a second vault. **Before authoring content of a kind that already
   exists, read the existing one's header.**
14. **`CharacterBody3D` has no step-up.** Verticality comes from props, never from terrain; a 30 cm
   kerb is an invisible wall the navmesh happily paths NPCs over.
15. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — all 12 skinned
   bodies are. `CharacterAnimationComponent` gates the shared library on that name; an un-retargeted
   rig given it freezes mid-guard with nothing logged. ⚠️ A model whose root node carries a
   translation cannot be retargeted until normalised, and the library is an **upper-body pose from the
   hips up**. `ASSET_POLICY.md` §0.2 carries all four traps.
16. **Check what is already vendored before pulling from the web.** As of 38O the library holds **no
   unadopted CC0 medieval bodies at all**.
17. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL** (38P2). Apply node scale *and*
   `nodes/root_scale` from the `.import`. ⚠️ And a **solid-looking prop with no collider** is a real
   class of defect here (18 had it). `ASSET_POLICY.md` §0.6.
18. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1**, and scale
   lives in each prop's `.import`, never in a cell transform. **Grass goes in patches.**
   `tools/dress_cell.py` has five styles; the arena is `edges` on purpose.
19. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
   blacksmith and the inn stay monolithic; the kit ships one wall height, 3.12 m.
   `tools/compose_building.py` writes a shell from `<name> <wide> <deep> <storeys>`.
20. ⚠️ **The Ember Crown map was re-laid (38F).** The settled cells form one contiguous city and every
   wilds cell is outside it; the arena moved to 150 m, past the gate, as the last cell in the realm.
   ⚠️ **A schedule carries a copy of its cell's `Center` as `Origin`** — moving a cell is never a
   one-line edit. `data/regions/EmberCrown.tres` carries the arithmetic.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before run_project — it does not recompile
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
```
