# NOW — where the project is

**This file is the single source of truth for project state.** `CLAUDE.md`, `README.md`,
`PRODUCTION_ROADMAP.md` and the playbook index link here instead of repeating it — before this file
existed the same three lines were maintained in four places and rewritten every sub-phase.

**Rewrite it, do not append to it.** It should never grow past a screen.

---

## Where we are

- **Stage C, Phase 38 (economy). 38A–38O done. Next: 38P — consignment house + appraiser.**
- Open the plan: `docs/playbook/phase-38.md`, the `38P` entry. Read the two entries above it too —
  the "two things worth carrying" lines are the cheapest bug prevention in the repo.
- ⏸ **38G is parked, not next.** It prices goods by settlement demand and sits above 38P in the file.
  Do not trust the first unchecked box. 38O did not unpark it: a fence changes *who will buy*, not
  the spread, so every margin in `--economy` is still negative.

## Last verified (38O)

| | |
| --- | --- |
| Build | clean |
| Tests | 1158 passing |
| `--validate` | exit 0 |
| Ember Crown cells | 9, all resident |
| Shops / items | 22 / 63 |

## Live invariants — the things that will bite you this arc

1. **A region loads whole.** Every cell of the active region is resident; `RegionStreamer` has no
   distance test and no unload during play. A new cell is permanently in the tree.
   ⚠️ Both *regions* cannot be resident together — their cells share coordinate space (Phase 44).
2. **`sell <= value <= buy` holds at every shop by construction**, so carrying goods between two
   merchants can *never* turn a profit. `--economy` prints the proof. Only 38G's regional demand can
   change it — do not try to author around it with a generous spread.
3. **`contraband` is the one trade tag that fails CLOSED** (38O). Every other tag is a filter a shop
   may opt out of; that one is a door a shop must opt *in* to, and it overrides an item's other tags.
   The whole exception is one branch in `TradeTags.Accepts`, which the vendor window, the sale and
   `EconomyReport` all already route through — do not add a second check anywhere else.
4. **A fenced sale moves two factions, once per sale, never per unit.** `ShopResource`'s four
   contraband fields; applied in `VendorPanel.Sell` beside `Absorb`, *after* the goods change hands.
   Deliberately the opposite granularity to 38H's per-unit payout decay.
5. **The Crossway toll is charged in `GameBootstrap.PayToll`**, on portal crossings only. Fast travel
   pays `TravelFee` and nothing else; one journey does not pay twice.
6. **Render every character body at eye level, front and back, before adopting it.** This trap has
   fired three times (`npc_townsman` hi-vis, `npc_merchant_f` t-shirt-and-trainers, and 4-of-6
   rejections in 38N2). Nothing about it is visible from a filename. ⚠️ 38O found the same trap one
   layer down: a **collider** copied from another cell was wrong too (`prp_ruin_pillar` stands up;
   `wilds_north` gives it a lying-down box). Render the cell, do not only read it.
7. **`CharacterBody3D` has no step-up.** Verticality comes from props, never from terrain; a 30 cm
   kerb is an invisible wall the navmesh happily paths NPCs over.
8. **Check what is already vendored before pulling from the web.** 38N2's pull returned a file
   byte-identical to one sitting unadapted in `assets/library/`. As of 38O the library holds **no
   unadopted CC0 medieval bodies at all** — every one is already in `assets/models/characters/`, and
   the remaining unadopted men are modern dress.

## Commands worth knowing

```
dotnet build Embervale.sln                      # ALWAYS before run_project — it does not recompile
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate         # content gate, exit 0/1
godot --headless --path . -- --economy          # the realm's price landscape
godot --headless --path . -- --state            # the content census
godot --path . -- --play                        # boot into the newest save
```
