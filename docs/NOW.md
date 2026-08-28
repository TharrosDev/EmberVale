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
- **NEXT: 42A — membership/rank flag framework + a small rank UI**, reusing the existing story
  flags and `FactionResource`. It is the gate the five guild questlines (42B–F) all sit behind.
- The former Phase 40 dependencies are settled: `CraftingStationType.Cooking` is gone (ordinal 4
  retired; append future stations at 5), and inventory has no max-weight/encumbrance state. Weight is
  still an item fact. The deferred 39.5 table remains measured, not scheduled.

## Last verified (2026-08-28 — 41.5C)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` — **1503 passing** |
| `--validate` | exit 0; 6 shrines, **1460 locale strings**, 70 map locations, 18 quests, 34 dialogues |
| Negative rules | `python tools/negative_tests.py --only shrine` — **6 cases** broken, caught, restored; recovered `--validate` exit 0 |
| Corruption gate (live) | `--shrine-shots`: `shrine.solaryn` refused at corruption 45 (no claim, Armor unchanged at 57.5) and blessed at 35 (Armor 67.5) |
| Shrine render | `--shrine-shots`: 12 live 1280×720 eye-level front/back frames; the Solaryn pair shows the refusal and blessing toasts stacked in-world |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 34 dialogues, 18 quests, 70 map locations, 3 companions |
| `--play` | newest `auto1` booted, restored 34 objects, loaded all 10 Ember Crown cells, 0 errors |
| Not run | `--economy` (no price touched); no new world placement was made, so no new front/back capture is owed beyond the 12 above |

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

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
godot --headless --path . -- --state
godot --path . -- --play
godot --path . -- --shrine-shots
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down — `godot-cli status .` reports no editor and a
refused connection. Do not confuse an open editor with a live MCP, and note the whole verification
spine above runs without it.
