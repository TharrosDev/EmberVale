# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C, Phase 38 (economy). 38A–38S done, and 38G with them. Next: 38T — Caravan events +
  supply shocks.**
- Open the plan: `docs/playbook/phase-38.md`, the `38T` entry, plus **38G's** — 38T moves the table
  38G authored, and 38G's two carried lines are written for it.
- ✅ **38G is no longer parked.** It was deferred for eleven sub-phases on one condition — *"it needs a
  second market to contrast against"* — which 38K and 38N had satisfied five sub-phases earlier while
  the notice sat unread. **Nothing else is parked.** ⚠️ If a future item is parked, park it with a
  check someone can run, not a verdict.
- ⚠️ **Five of 38R's seven briefed services were struck, not deferred** — cosmetic, healer, passage,
  warehouse (38R) and courier (38R2). Each reason is in the playbook and `DESIGN.md` §6. Two of the
  five already existed under another name. **Do not rebuild them.**
- 🎨 **The asset migration onto the four Quaternius MegaKits is complete** (A–E). `docs/ASSET_POLICY.md`
  §0.2–§0.3 is the authority.

## Last verified (session close, 2026-08-09)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1257 passing**; 38G added 9 (`RegionDemandTests`) and gave the 38F sweep a demand axis |
| `--validate` | exit 0; **4 new 38G rules + both commission cases negative-tested both ways** |
| `--economy` | **CHANGED ON PURPOSE — 3 routes now turn a profit** (eel and riverfish east, copper ore west) |
| `--state` | 2 regions, 14 cells, 63 items, **23 shops**, 14 services, 8 contracts, 33 dialogues, 14 quests |
| **`--play`** | booted, loaded slot1, all 9 cells resident, 27 objects restored, **0 project errors** |
| Saved state | **38G adds none** — demand is authored data. Four "no usable entry" warnings, not five |
| Rendered | **nothing — 38G placed nothing in the world.** Its driver is a caption in `VendorPanel` |
| Bodies retargeted | 12 of 12 skinned (`fp_arm` has no skin and needs none) |
| Props with no collider | 0 (audit clean) |

## Live invariants — the things that will bite you this arc

1. **A region loads whole.** Every cell of the active region is resident; `RegionStreamer` has no
   distance test and no unload during play. A new cell is permanently in the tree.
   ⚠️ Both *regions* cannot be resident together — their cells share coordinate space (Phase 44).
2. ⚠️ **`sell <= LOCAL value <= buy` holds at every shop by construction — and as of 38G that is a
   narrower claim than it was.** A carry between two settlements **can now pay**, because
   `RegionDemand.ValueAt` moves an item's value by place and both sides of one counter spread over the
   same local number. So: a round trip at **one** shop still costs, always; a carry from a surplus to a
   demand is meant to pay, and `--economy` names the three routes that do. **Do not "fix" a positive
   margin in that table** — that is the feature. Do treat `sell > buy` **at one shop** as a defect.
3. ⚠️ **A DEMAND TABLE IS A FLOOR UNDER OTHER PEOPLE'S RULES** (38G). A contract reward, a commission
   fee and a fence's margin are all measured against *what the best buyer pays*, and that number moves
   when a cell authors a tag. Authoring ore demand at the coast broke `contract.crossway.iron_ore` on
   the first `--validate` run — correctly. ⚠️ **`ShopResource.CellId` is empty by default and empty
   means par**, so a shop that forgets it prices as though it were in town and only the `--economy`
   table shows it; the validator can only catch a cell *no* shop points at.
4. ⚠️ **WHEN A NEW PRICE APPEARS, ASK WHAT IT IS A SPREAD OVER.** A commission (38Q) relates **two**
   items — materials in, a finished piece out — so no clamp closes it and only the authored labour fee
   does; `--validate` proves that every boot at the cheapest standing. A **haggle** (38S) is a spread
   over *one* item's value like standing and the specialty premium, so the 38A clamps cover it with no
   new argument. **The question is cheap and the answer is sometimes "nothing to do".**
5. ⚠️ **BUT A CLAMP STOPS A MONEY PRINTER, NOT A FREE ROUND TRIP** (38S). `ValidateShopTrade`'s margin
   rule is the one that keeps buying and selling back *costing* something, and every new multiplier
   must be folded into **both** of its sides. The haggle tightened the permitted fraction/markup ratio
   from ~0.52 to ~0.42 on a haggling shop. ⚠️ **And every new multiplier joins
   `NoCombinationOfMultipliersLetsSellingBeatBuying` or it does not ship** (38F's contract).
6. ⚠️ **A HAGGLE IS THE FIRST THING TO MOVE THE SELL SIDE, AND IT MAY ONLY BECAUSE IT IS DAY-BOUNDED**
   (38S). Standing deliberately does not (`MarkupFor` says why: a generous fraction converges on
   `sell == buy`). ⚠️ **A broker cannot be haggled** — `VendorPanel` prices her rows through
   `ConsignmentRules`, which the deal never reaches, so `--validate` refuses the pairing.
7. ⚠️ **DERIVE, THEN BOUND — TWO MECHANISMS, NEITHER SUBSTITUTING FOR THE OTHER** (38Q2, 38R2, 38S).
   The contract board, a throw of the bones and a merchant's mood are all pure functions of the day, so
   a quickload **replays** them. What stops them being farmed is a ledger: `ContractLedger`,
   `WagerLedger`, `HaggleLedger` — each storing only **what the player did**, never what was offered or
   what the answer was. ⚠️ Storing the outcome is the obvious shape and the one that rots.
   ⚠️ `string.GetHashCode()` is randomised per process; `StableRoll` is a hand-written FNV-1a.
   ⚠️ **Nothing about a contract may reach the quest log.**
8. ⚠️ **A SERVICE CAN BE FIRED FROM A CONVERSATION, EXCEPT A BANK** (38R). `DialogueEffect.OpenService`
   runs the whole 38D battery through `ServiceComponent.TryUse` — but a `Bank` opens the **host
   entity's** inventory and a conversation has no host entity. ⚠️ **An entity still gets one
   interactable**: the innkeeper's `ServiceComponent` was *deleted* to put his conversation back, and
   the realm's only bed is reachable through `dialogue.innkeeper` and nowhere else.
9. ⚠️ **TWO SERVICES ARE CHARGED AFTER THEIR VERB AND EVERY OTHER ONE BEFORE** (38Q, 38R): a commission
   fails on a full pack, a hire fails on a full party. Only the commission needs a rollback. ⚠️ Both
   must be **PRICED**, inverting 38O's free-service rule, because both hand over *goods*.
10. ⚠️ **A WAGER'S RULE IS CHECKED AT *ALLIED* STANDING** (38R2) — and so is every 38S rule.
    `PriceOf` discounts a stake and nothing discounts a payout, so a table that is a sink at Neutral can
    print at the top of the ramp. **Every price the player can move directly is evaluated at Allied.**
11. **A broker fronts nothing, so no purse and no saturation apply to her** (38P). ⚠️
    **`EconomyReport.BestBuyers` skips a consignment house and that is not optional** (38P2).
12. **`contraband` is the one trade tag that fails CLOSED** (38O), and **a fenced sale moves two
    factions once per sale, never per unit** — deliberately the opposite granularity to 38H's per-unit
    payout decay. **The Crossway toll is charged in `GameBootstrap.PayToll`**, portal crossings only.
13. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Seven firings.
    38R's is the plainest: an NPC on open ground stood exactly where the player stands to read the
    previous sub-phase's board, and the fault was in neither object and in no line of either `.tscn`.
    ⚠️ **A body's facing is not knowable from a file.** Earlier: `prp_banner_guild` chosen by precedent
    (38Q2), `prp_weapon_stand` reading as a sawhorse (38Q), a lying-down collider on a standing pillar
    (38O), 4-of-6 rejections in 38N2, `npc_townsman` hi-vis, `npc_merchant_f` t-shirt-and-trainers.
    ⚠️ **When no pack model fits, primitives are a legitimate answer.**
14. ⚠️ **A DECISION CAN LIVE IN A `.tres` HEADER, AND NOTHING GREPS THOSE** (38R). 38R's warehouse was
    killed mid-session by a comment in `EmberCrownBank.tres`; 38S's haggle skipped the two wharf fences
    because `HollowreachHull.tres` explains why their `FactionId` is empty. **Before authoring content
    of a kind that already exists, read the existing one's header.**
15. **`CharacterBody3D` has no step-up.** Verticality comes from props, never from terrain; a 30 cm
    kerb is an invisible wall the navmesh happily paths NPCs over.
16. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — all 12 skinned
    bodies are. An un-retargeted rig given it freezes mid-guard with nothing logged. ⚠️ A model whose
    root carries a translation cannot be retargeted until normalised, and the library is an
    **upper-body pose from the hips up**. `ASSET_POLICY.md` §0.2 carries all four traps.
17. **Check what is already vendored before pulling from the web.** As of 38O the library holds **no
    unadopted CC0 medieval bodies at all**.
18. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL** (38P2). Apply node scale *and*
    `nodes/root_scale` from the `.import`. ⚠️ A **solid-looking prop with no collider** is a real class
    of defect here (18 had it). `ASSET_POLICY.md` §0.6.
19. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1**, and scale
    lives in each prop's `.import`, never in a cell transform. **Grass goes in patches.**
20. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
    blacksmith and the inn stay monolithic; the kit ships one wall height, 3.12 m.
21. ⚠️ **The Ember Crown map was re-laid (38F).** The settled cells form one contiguous city and every
    wilds cell is outside it; the arena moved to 150 m, past the gate. ⚠️ **A schedule carries a copy
    of its cell's `Center` as `Origin`** — moving a cell is never a one-line edit.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before run_project — it does not recompile
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
```
