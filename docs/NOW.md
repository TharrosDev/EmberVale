# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C, Phase 38 (economy). 38A–38Q2 done. Next: 38R — Services II.**
- Open the plan: `docs/playbook/phase-38.md`, the `38R` entry. Read the two entries above it too —
  the "two things worth carrying" lines are the cheapest bug prevention in the repo.
- ⏸ **38G is parked, not next.** It prices goods by settlement demand and sits above 38R in the file.
  Do not trust the first unchecked box. Nothing since 38N has unparked it: a fence changes *who will
  buy*, a broker *how much one counter pays*, an appraiser only *what the player is told*, and
  neither a commission nor a contract is a route at all — none of them moves an item's **value** by
  settlement, so every margin in `--economy` is still negative.
- 🎨 **The asset migration onto the four Quaternius MegaKits is complete** (A–E). `docs/ASSET_POLICY.md`
  §0.2–§0.3 is the authority.

## Last verified (session close, 2026-08-09)

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1210 passing**; 38Q2 added 7 (`ContractRulesTests`) |
| `--validate` | exit 0; **all seven new 38Q2 rules negative-tested both ways** |
| `--economy` | **byte-identical before and after 38Q2** — neither a commission nor a contract is a route; every margin still negative (38G's job) |
| `--state` | 2 regions, 14 cells, 63 items, 23 shops, **12 services, 8 contracts**, 31 dialogues, 14 quests |
| **`--play`** | booted, loaded slot1, streamed all 9 cells, 27 objects restored, **0 project errors** |
| `ContractLedger` | save/load round trip **actually run** headlessly, including the §7 replace-never-merge rule |
| Cells rendered | Bryn's order bench (38Q) and the Crossway caravan board (38Q2), noon and dusk, front and back |
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
   quickload cannot reroll them — 38S's rule, honoured a sub-phase early and for free. `ContractLedger`
   saves **only what the player filled**, never what was offered. ⚠️ And the reward rule is the
   **mirror** of the commission one: a contract is refused for paying *less* than the best buyer (a
   longer walk for less money), with **no ceiling**, because what bounds it is being fillable once per
   rotation — the ledger, never the price. ⚠️ **Nothing about a contract may reach the quest log**:
   `QuestLogPanel` carries no Contracts heading on purpose.
5. **A broker fronts nothing, so no purse and no saturation apply to her** (38P). `VendorPanel.Consign`
   calls neither `TakePurse` nor `Absorb` nor `FenceStanding`, and all three absences are the feature.
   It still cannot invert the spread: the payout routes through `ShopPricing.SellPrice`. ⚠️
   **`EconomyReport.BestBuyers` skips a consignment house and that is not optional** (38P2) — her
   `SellFraction` is inert data the vendor window never reads.
6. ⚠️ **`ServiceKind.Commission` is charged AFTER its verb and must be PRICED — both invert the rules
   around it** (38Q). Every other service is charged first, because none of their verbs can fail; this
   one fails on a full pack and rolls back, so charging first is the only way to lose money for
   nothing. And 38O's *free* rule (a fee fails closed on the player who needs the counter) has been
   right three times running and is wrong here, because this service hands over **goods** — a free
   master is the materials shop with the spread deleted. `--validate` enforces both directions.
7. **`contraband` is the one trade tag that fails CLOSED** (38O). Every other tag is a filter a shop
   may opt out of; that one is a door a shop must opt *in* to. The whole exception is one branch in
   `TradeTags.Accepts` — do not add a second check anywhere else.
8. **A fenced sale moves two factions, once per sale, never per unit** (`ShopResource`'s four
   contraband fields, applied in `VendorPanel.Sell`). Deliberately the opposite granularity to 38H's
   per-unit payout decay.
9. **The Crossway toll is charged in `GameBootstrap.PayToll`**, on portal crossings only. Fast travel
   pays `TravelFee` and nothing else; one journey does not pay twice.
10. ⚠️ **RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT.** Six firings now.
   38Q hit three variants in one session: a **prop** rejected on sight (`prp_weapon_stand` reads as a
   sawhorse from every angle but straight-on), a placement **inside a building**, and one that put
   **Bryn standing inside his own 2.85 m counter** — a fault in neither object alone, which is why
   only the render found it. ⚠️ 38Q2 added the sixth and it is a new kind: `prp_banner_guild` was
   chosen **because two other boards already use it**, and it was wrong twice over — a cloth pennant
   has no surface to post on, *and* this cell already spends that model on the one banner that says
   whose road it is. **Precedent is not judgement, and a duplicate model is only visible with both
   objects in one frame.** Earlier: `npc_townsman` hi-vis, `npc_merchant_f` t-shirt-and-trainers,
   4-of-6 rejections in 38N2, a lying-down collider on a standing pillar in 38O.
   ⚠️ **When no pack model fits, primitives are a legitimate answer** — the Crossway board is three
   `BoxMesh`es in the cell's own vocabulary, and a wrong-shaped model adopted to honour the
   four-packs rule would have been worse than a right-shaped box.
11. **`CharacterBody3D` has no step-up.** Verticality comes from props, never from terrain; a 30 cm
   kerb is an invisible wall the navmesh happily paths NPCs over.
12. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — all 12 skinned
   bodies are. `CharacterAnimationComponent` gates the shared library on that name; an un-retargeted
   rig given it freezes mid-guard with nothing logged. ⚠️ A model whose root node carries a
   translation cannot be retargeted until normalised, and the library is an **upper-body pose from the
   hips up**. `ASSET_POLICY.md` §0.2 carries all four traps.
13. **Check what is already vendored before pulling from the web.** As of 38O the library holds **no
   unadopted CC0 medieval bodies at all**.
14. ⚠️ **MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL** (38P2). Apply node scale *and*
   `nodes/root_scale` from the `.import`, or the number is fiction — `prp_tome_stand`'s first collider
   was over twice too big in every axis. ⚠️ And a **solid-looking prop with no collider** is a real
   class of defect here (18 had it): a collider child inherits its node's scale, a tree takes a trunk
   rather than a bounding box, and a collider outside the `NavigationRegion3D` blocks the player while
   the navmesh stays ignorant of it. `ASSET_POLICY.md` §0.6.
15. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1**, and scale
   lives in each prop's `.import`, never in a cell transform. **Grass goes in patches**, not an even
   scatter. A cell's dressing is a judgement about the place: `tools/dress_cell.py` has five styles and
   every cell records which one it used. ⚠️ The arena is `edges` on purpose — a combat floor has to
   stay legible.
16. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
   blacksmith and the inn stay monolithic; the kit ships one wall height, 3.12 m.
   `tools/compose_building.py` writes a shell from `<name> <wide> <deep> <storeys>`.
17. ⚠️ **The Ember Crown map was re-laid (38F).** The settled cells form one contiguous city and every
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
