# NOW — where the project is

**This is the single source of project state. Rewrite it; do not append to it.**

## Where we are

- **Stage C.** Economy (38), mounts/traversal (39), map intelligence (39.5), and quest authoring
  (41) are closed. **Phases 40 and 40.5 are struck, not deferred:** this game has no survival
  needs, durability, hunger, encumbrance, puzzle, trap, or vault system. A cut system leaves no stub.
- **41.5A–C — Divine Shrines & Blessings ✅ CLOSED.** The six dead gods have stat-distinct,
  map-linked shrine callers in the Ember Crown, and each now refuses a supplicant whose corruption
  has reached its authored tolerance. `BlessingComponent.Offer` is the sole decision point —
  already-claimed → refused → blessed — and the player's claim set stays the only blessing
  authority; refusal keeps no state anywhere and no shrine body saves anything.
- **The level-layout rebuild (2026-08-28) ✅ CLOSED — out of band, maintainer-directed, and not a
  roadmap phase.** All **fifteen** explorable cells had their physical layout redesigned, not
  redecorated: footprints, entrances, circulation, building placement, landmarks, sightlines and
  encounter space. Every cell now has a spatial identity of its own; the shared central-road /
  mirrored-row / centred-plaza formula is gone from the realm. Three seam defects fell out of it and
  were fixed with it (see invariant 11). ⚠️ **This is NOT Phase 44** — that phase blocks out all five
  realms and is still ahead; nothing here has been done against its scope.
- **NEXT: 42A — membership/rank flag framework + a small rank UI**, reusing the existing story
  flags and `FactionResource`. It is the gate the five guild questlines (42B–F) all sit behind.
- The former Phase 40 dependencies are settled: `CraftingStationType.Cooking` is gone (ordinal 4
  retired; append future stations at 5), and inventory has no max-weight/encumbrance state. Weight is
  still an item fact. The deferred 39.5 table remains measured, not scheduled.

## Last verified (2026-08-28 — the level-layout rebuild)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` — **1503 passing** |
| `--validate` | exit 0; 6 shrines, **1460 locale strings**, 70 map locations, 18 quests, 34 dialogues |
| Negative rules | `python tools/negative_tests.py` — **94 cases** broken, caught, restored; tree restored clean and recovered `--validate` exit 0 |
| Corruption gate (live) | `--shrine-shots`: `shrine.solaryn` refused at corruption 45 (no claim, Armor unchanged at 57.5) and blessed at 35 (Armor 67.5) |
| Shrine render | `--shrine-shots`: 12 live 1280×720 eye-level front/back frames; the Solaryn pair shows the refusal and blessing toasts stacked in-world |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 34 dialogues, 18 quests, 70 map locations, 3 companions |
| `--play` | newest `auto1` booted, restored 34 objects, loaded all 10 Ember Crown cells, 0 errors over 60 s with the integrity checker running |
| World render | `world_shots.gd` — all 15 cells streamed, settled and rendered, 150 day/dusk frames, **visual baseline regenerated** (every frame of it changed, which is the point) |
| Step-up gate | `stepup_probe.gd` — migrated to the Salt Steps and green: rose 0.301 on the terrace, 0.001 into the bell tower |
| Map placement | `map_probe.gd` — 70 markers across 15 cells, no duplicate or centre-parked pin |
| Layout overlap | `python tools/check_cell_layout.py <cell>` — 0 overlapping structures in all 15 |
| Not run | `--economy` (no price touched); the Frostfang portal crossing still needs keyboard input no CLI here can inject, so it is exercised only through `world_shots.gd` streaming both regions |

## Live invariants

1. **Gameplay state persists, and Load REPLACES live collections.** Save ids are stable primary
   keys; clear before restore, including every false/empty branch. A partial restore is a failed load.
2. **One surface owns each fact.** A shrine body is a caller; the player's claimed-id set is the only
   blessing authority. Never add per-shrine flags or a second blessing ledger.
3. **A gate belongs at the choke point, not in the caller.** The corruption refusal lives in the
   player's `BlessingComponent`, so no seventh shrine placement can forget it. Put the next
   condition where the mutation already funnels.
4. **All player-facing text uses `Loc.T()` and a `strings.csv` key.** Literal English can render
   correctly while silently breaking localization.
5. **If the player can go there, map it in the same sub-phase.** A map location's position is the
   transform of its `MapLocationComponent` parent in a cell scene, never a resource coordinate.
6. **Render world changes at eye level, front and back, with people and furniture around them.**
   Reading a transform is not a placement review.
7. **Before authoring content of an existing kind, read an existing `.tres` header.** Decisions hide
   there. Before adopting a model, inspect the four Quaternius packs and `assets/library/manifest.json`.
   Use primitives only when no pack model fits; never introduce a fifth style.
8. **An authored numeric range fails silently at both ends.** Below the floor the reward is
   unreachable; above the ceiling the branch is dead content. Both load and render fine, so every new
   range needs a validator arm and a negative case in *each* direction.
9. **An event that fires conditionally is not automatically the event that means the thing.** Read its
   publisher before using it as a mechanic trigger; seeded state also defeats "not yet done" filters.
10. **A cache key is a subscription.** Any newly drawn fact must be part of every cache/signature that
    renders it, not merely an event listener.
11. **A seam is arithmetic, and a road that points at a wall says nothing.** A cell's neighbours meet
    it at exact coordinates derived from the two Centers and floor sizes, and a region path may be
    drawn to a seam the cell scene does not open onto — the two are authored in different files and
    nothing cross-checks them. Three shipped that way and were found only by walking the numbers:
    **the arena** was sealed on its southern seam and open on its western side, which faces region
    void; **Hollowreach's** road ran along z ≈ 0 while the Embermarket's stair arrives at z = 12;
    **Tarn's Landing** had 20 m of lake over the whole western seam that `wilds_west` connects to.
    When you move a road, walk it into the neighbouring cell's file.
12. **A layout constraint written as a coordinate outlives its reason.** "The stall transforms do not
    move" was a real contract in 38K and a cage by 2026-08-28; what was actually load-bearing was the
    *relative* shape of it (a Counter at local (2.2, 0, 0), a merchant 1.1 m along the stall's own
    +X), which survives any move. Author dependencies as offsets and ids, never as absolute points —
    and when a comment forbids a change, find what it is protecting before believing it.

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
godot --headless --path . -- --state
godot --path . -- --play
godot --path . -- --shrine-shots
godot --path . --script res://tools/world_shots.gd      # renders all 15 cells; add -- --update-world-baseline after a layout change
godot --headless --path . --script res://tools/map_probe.gd
godot --headless --path . --script res://tools/stepup_probe.gd
python tools/relayout_cell.py <cell.tscn> <spec>        # move/rotate/delete/rename authored nodes
python tools/check_cell_layout.py <cell.tscn>           # gross structural overlaps
python tools/redress_cell.py <cell.tscn> <style> <first_ext_id>
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down — `godot-cli status .` reports no editor and a
refused connection. Do not confuse an open editor with a live MCP, and note the whole verification
spine above runs without it.
