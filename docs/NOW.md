# NOW — where the project is

**This is the single source of project state. Rewrite it; do not append to it.**

## Where we are

- **Stage C.** Economy (38), mounts/traversal (39), map intelligence (39.5), and quest authoring
  (41) are closed. **Phases 40 and 40.5 are struck, not deferred:** this game has no survival
  needs, durability, hunger, encumbrance, puzzle, trap, or vault system. A cut system leaves no stub.
- **41.5A–B — Divine Shrines & Blessings core + placements ✅ CLOSED.** The six dead gods have
  stat-distinct, map-linked shrine callers in the Ember Crown. The player's `BlessingComponent` owns
  the sole `blessings:player` claim set, clears old modifiers before load, then re-derives every
  blessing from the canonical `ShrineResource` data; no shrine body saves state.
- **NEXT: 41.5C — corruption-gated blessing refusal/curse.** A high-corruption visit must select a
  refusal/curse outcome before it claims the shrine, so the player set remains the only authority.
- The former Phase 40 dependencies are settled: `CraftingStationType.Cooking` is gone (ordinal 4
  retired; append future stations at 5), and inventory has no max-weight/encumbrance state. Weight is
  still an item fact. The deferred 39.5 table remains measured, not scheduled.

## Last verified (2026-08-28 — 41.5B)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` — **1496 passing** |
| `--validate` | exit 0; **6 shrines**, 1454 locale strings, 70 map locations, 18 quests, 34 dialogues |
| Map generator | `python tools/gen_map_locations.py --check` — 70 locations, 0 files out of date |
| Shrine render | `--shrine-shots`: 12 live 1280×720 eye-level front/back frames; final Solaryn caller claimed its blessing and proved replacement-load semantics |
| Negative rules | `python tools/negative_tests.py --only shrine` — 4 cases broken, caught, restored; recovered `--validate` exit 0 |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 34 dialogues, 18 quests, 70 map locations, 3 companions |
| `--play` | newest `auto1` booted, restored 34 objects, and loaded all 10 Ember Crown cells successfully |
| Not run | `--economy` (no price touched); full world baseline (the shrine-specific live captures cover this placement) |

## Live invariants

1. **Gameplay state persists, and Load REPLACES live collections.** Save ids are stable primary
   keys; clear before restore, including every false/empty branch. A partial restore is a failed load.
2. **One surface owns each fact.** A shrine body is a caller; the player's claimed-id set is the only
   blessing authority. Never add per-shrine flags or a second blessing ledger.
3. **All player-facing text uses `Loc.T()` and a `strings.csv` key.** Literal English can render
   correctly while silently breaking localization.
4. **If the player can go there, map it in the same sub-phase.** A map location's position is the
   transform of its `MapLocationComponent` parent in a cell scene, never a resource coordinate.
5. **Render world changes at eye level, front and back, with people and furniture around them.**
   Reading a transform is not a placement review.
6. **Before authoring content of an existing kind, read an existing `.tres` header.** Decisions hide
   there. Before adopting a model, inspect the four Quaternius packs and `assets/library/manifest.json`.
   Use primitives only when no pack model fits; never introduce a fifth style.
7. **An event that fires conditionally is not automatically the event that means the thing.** Read its
   publisher before using it as a mechanic trigger; seeded state also defeats "not yet done" filters.
8. **A cache key is a subscription.** Any newly drawn fact must be part of every cache/signature that
   renders it, not merely an event listener.

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate
python tools/negative_tests.py
godot --headless --path . -- --state
godot --path . -- --play
godot --path . -- --shrine-shots
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down; do not confuse an open editor with a live MCP.
