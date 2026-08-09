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
- 🎨 **Running alongside: the asset migration onto the four Quaternius MegaKits.** **A (animation
  gate) done and proved. B (nature) done** — 12 ground-cover props adopted and **all 9 Ember Crown
  cells dressed**, each to a style that suits the place.
  **C (architecture) done** — 11 of 16 building placements are composed modular scenes; the
  blacksmith, the inn and the low cottage stay monolithic on purpose. **D (interiors) and E (sweep) done — the asset migration is complete.**
  `docs/ASSET_POLICY.md` §0.2–§0.3 is the authority.

## Last verified (asset Phase A)

| | |
| --- | --- |
| Build | clean |
| Tests | **1179** passing (1158 + 21 new) |
| `--validate` | exit 0 |
| Ember Crown cells | 9, all resident |
| Shops / items | 22 / 63 |
| Retarget render gate | 11 of 12 bodies, eye level front/back — feet at y≈0.02, heads 1.27–1.55 m |
| Ground cover | 9 cells, ~900 nodes, no colliders anywhere, navmesh untouched |
| Ember Crown map | re-laid: 9 cells, 0 overlaps, all connected by full edges |
| Composed buildings | 11 placements, 5 cells, one collider each |

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
8. **A retargeted rig is marked by its skeleton being named `GeneralSkeleton`** — **11 of the 12
   skinned bodies are.** ⚠️ **`chr_player_base` is the exception and stays that way**: its `RootNode`
   carries a 4.82 m translation, so the rest fixer either sinks it 4.8 m or shears its spine, and
   there is no third setting. It needs a re-export with a zeroed root translation, which is not a
   Blender round-trip job (17 bone-parented children). `CharacterAnimationComponent` gates the shared
   library on the skeleton name — an un-retargeted rig must not receive it, or it resolves a
   block/cast clip whose every track points at bones it does not have and freezes mid-guard with
   nothing logged. **The player therefore has no block/cast/channel clip**, exactly as before.
   ⚠️ The library is an **upper-body pose from the hips up**: the Quaternius feet are root-parented
   IK goals (`PT.*` are pole targets, not toes), so its leg motion cannot transfer. `ASSET_POLICY.md`
   §0.2 carries all four traps.
9. **Check what is already vendored before pulling from the web.** 38N2's pull returned a file
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

10. ⚠️ **The nature megakit's ground cover is 4–10× life size while its trees are 1:1.** Scale lives
    in each prop's `.import` (`nodes/root_scale`), never in a cell transform. And **grass goes in
    patches** — an even scatter reads as litter, which is what `wilds_north`'s first pass looked
    like. `ASSET_POLICY.md` §0.3.
11. **A cell's dressing is a judgement about the place, not a setting.** `tools/dress_cell.py` has
    five styles — `meadow`, `verge`, `shore`, `industrial`, `edges` — and every cell records which
    one it used and the command to regenerate it. ⚠️ **The arena is `edges` on purpose**: a combat
    floor has to stay legible, and scenery a player reads as cover is worse than a bare floor.
    ⚠️ A banded style must be sampled **around the ring**, not by rejection — three separate
    truncation bugs put town_hub's whole verge on one edge before a render caught it.

12. ⚠️ **A generic wall kit composes generic buildings well and special-purpose ones badly.** The
    blacksmith and the inn stay monolithic because their bespoke geometry — forge canopy, dormers —
    is what makes them readable; a composed shell is a handsome barn that says nothing. And the kit
    ships **one wall height, 3.12 m**, so `bld_cottage`'s 4.20 m silhouette cannot be built from it.
    `tools/compose_building.py` writes a shell from `<name> <wide> <deep> <storeys>`.

13. ⚠️ **Solid-looking scenery with no collider is a real class of defect here, and 18 props had it**
    — dead pines, rock clusters, six glaciers. When adding scenery: a collider child **inherits its
    node's scale** (author in local units), a tree takes a **trunk** collider rather than a bounding
    box, and a collider **outside the `NavigationRegion3D` blocks the player while the navmesh stays
    ignorant of it**. `ASSET_POLICY.md` §0.6.

14. ⚠️ **The Ember Crown map was re-laid (38F) and two documented decisions were reversed.** The
    settled cells now form one contiguous city — town_hub + embermarket + hollowreach, with Tarn's
    Landing adjoining — and every wilds cell is outside it. The **arena moved from 65 m off the town
    square to 150 m**, past the gate and the wilds, as the last cell in the realm.
    ⚠️ **The Crossway toll's old justification is gone**: wilds_north no longer sits between the town
    and the gate, so the toll now stands on what is *beyond* the gate rather than the road to it.
    `data/regions/EmberCrown.tres` carries the full arithmetic and the reasoning.
    ⚠️ **A schedule carries a copy of its cell's `Center` as `Origin`** — six had to move with their
    cells. Moving a cell is never a one-line edit.
