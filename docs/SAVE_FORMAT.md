# Save format — the persistence contract

> **Why this file exists.** Persistence is the highest-risk system in the game: it is the only one
> whose defects destroy work the player already did, and the only one where a mistake made today
> lands on a player months from now. The 2026-08-15 audit found **two P0s living here**, and the
> format, the `SaveId` contract, the migration policy and — most importantly — **what is deliberately
> not saved** existed only as comments spread across `SaveManager.cs` and its callers.
>
> `ARCHITECTURE.md` §1 is how the machinery works. This is what the bytes mean and which rules you
> cannot break without costing someone their progress.

---

## 1. Layout on disk

```
user://saves/<slot>/
    save.json        the full envelope — authoritative
    header.json      a read mirror of the envelope's header, for the slot browser
    screenshot.png   320×180 thumbnail, best-effort, never load-bearing
```

Legacy flat saves (`user://saves/<slot>.json`) are still **readable**, and are deleted the first time
that slot is written in the directory layout.

Slots are just directory names. `quick` is the default; `auto1`/`auto2`/`auto3` are the autosave ring
(`AutosaveService.RingSlots`).

## 2. The envelope

```json
{
  "version":   2,
  "timestamp": 1755270000.0,
  "header":    { ... },
  "objects":   { "<SaveId>": { ...component state... } }
}
```

`objects` is a flat map of `SaveId → state`. **The set of live objects drives restoration**, not the
file: on load, each registered `ISaveable` pulls *its own* entry by id. That is what lets the game
scale to hundreds of actors without bespoke save code, and it is why the two directions fail
differently — see §6.

## 3. The header

Written by `SaveManager.BuildHeader` plus `GameBootstrap.BuildSaveHeader` (the gameplay half, wired
through `SaveManager.HeaderProvider` so the manager stays free of gameplay types).

| Field | Used for |
| --- | --- |
| `timestamp`, `playtime_seconds` | slot browser ordering and display |
| `region`, `region_id` | display name, **and the region a load restores into** |
| `player_x/y/z`, `player_yaw` | **the transform a load restores** |
| `race_id`, `char_name` | the character `StartLoadedGame` spawns |
| `level`, `corruption_tier` | slot browser |

⚠️ **The header is not decoration — it is load-bearing.** Since the 2026-08-15 audit it drives where
and who the player is after a load. Treat a wrong header as a wrong save.

⚠️ **`header.json` is a mirror and must never be stale.** It is written after `save.json` commits,
with no transaction across the two. If that write fails the mirror is **deleted**, because
`ReadHeader` prefers it and would otherwise answer every question about this save with the previous
save's answers — including the region and position. A missing mirror is free (`ReadHeader` falls back
to the envelope's copy); a stale one misplaces the player.

## 4. The `SaveId` contract

A `SaveId` is a **stable string key**. Renaming one silently orphans everything it saved.

Two shapes:

- **World services** — a bare noun, one per world: `map`, `weather`, `worldclock`, `shopstock`,
  `spawns`, `cell_persistence`, `companions`, `bestiary`, `contracts`, `fasttravel`, `housing`,
  `haggles`, `wagers`, `consignment`, `contraband_impound`, `shocks`, `tutorial`.
- **Per-actor components** — `"<prefix>:<PersistentId>"`, built by `SaveKeyPolicy.Key`:
  `inventory:player`, `stats:npc.holt`, and so on.

⚠️ **A component persists only if its owner has a `PersistentId`** (`SaveKeyPolicy.ShouldPersist`).
Transient actors — spawned mobs, the training dummy — are session-only **by design**: a runtime-keyed
entry can never be reclaimed after a world rebuild, because the reloaded actor gets a fresh runtime
id, so it would both fail to restore *and* linger as orphaned state. The `savecheck` dev command
flags any volatile key (`SaveKeyPolicy.IsVolatile`); there should be none.

**References are ids, never paths or indices.** Spawned actors round-trip as
`{pid, tid, x, y, z, yaw}` and are rebuilt through `PersistentActorRegistry.Create`. Nothing in a save
points at a scene path or an array position, which is why authoring can move freely.

## 5. What is NOT saved

Everything here resets on load, deliberately. **Check this list before assuming a bug.**

| Not saved | Consequence |
| --- | --- |
| `EncounterDirector`, `WorldEventDirector` | roaming spawns and world events re-roll. `SupplyShockService` exists as its own saveable node precisely because the director is not one. |
| `RegionStreamer`, `SliceDirector`, `BossEncounterDirector` | rebuilt from the restored region |
| `MusicDirector`, `AmbienceDirector`, `AudioDirector` | audio re-derives from world state |
| `PlacementDirector` | intentional — a placed prop persists through `PersistentSpawnDirector`, which already records template, position and yaw |
| `GameManager.State` | the loader decides the state |
| Player transform / active region | **not** an `ISaveable` — they live in the header (§3) |

## 6. Failure policy

The rules, in the order a load applies them:

1. **No `version` field → refuse.** Every envelope this game has written carries one. Its absence
   means a truncated write, a hand-edit, or some other JSON document entirely.
2. **`version` > current → refuse.** A newer save cannot be read by an older build.
3. **`version` < current WITH a migration step → migrate forward.** ⚠️ There is one now:
   **v1 → v2 (the 2026-08-29 geography overhaul)**. Every world coordinate a v1 document holds was
   written against a lattice that no longer exists — the Ember Crown's cells all moved except the
   town hub, Frostfang Reach was lifted out of the Ember Crown's coordinate space entirely, and the
   ground stopped being flat, so even an unmoved X/Z can have eight metres of hillside over it. The
   step **discards the three records a player can be teleported to**: the header transform (the load
   falls through to the region's authored `SpawnPoint`), the fast-travel network (a jump to a v1
   landing point is a jump into a hill; the posts are unmoved and re-attune by walking to them), and
   `MapService`'s saved pins (they re-register the moment their cell loads). Everything else — quests,
   flags, inventory, perks, reputation, the economy, blessings, companions — carries no coordinates
   and is kept: a player's progress is not a casualty of a terrain change.

4. **`version` < current with no migration step → refuse.** ⚠️ This used to warn and load at best
   effort. v1 is the first format that ever existed, so there is no legitimate older save — the
   branch only ever caught corrupt or foreign files, and waved them through into live components.
   When a v2 arrives, register a step that upgrades `root` in place; **an unmigratable save must fail
   loudly, never load in pieces.**
4. **No `objects` section → refuse.**
5. **Any `ISaveable.Load` throws → the whole load fails.** Each exception is caught so one bad entry
   cannot abort the other thirty-odd, but the result is reported as a failure and
   `GameLoadedEvent` is **not** published. ⚠️ **A partial restore is a failed load**: the caller
   abandons the session to the title, because continuing hands the player a world assembled from half
   the save and half of whatever was already live, and the next autosave writes that over the good
   file.
6. **An entry with no live claimant** logs an orphan warning (drift, or a renamed `SaveId`). Entries a
   streamed-out cell is holding are claimed via `ClaimDeferred` and are *not* reported — warning on
   the healthy path is how a diagnostic teaches you to ignore it.
7. **A live saveable with no entry** keeps its current state and warns. ⚠️ This is a merge over live
   state, not a reset — the hazard `CLAUDE.md` §7 describes.

Writes are atomic: staged to `<target>.tmp`, then renamed over the target, so a crash mid-write can
never truncate a good file.

## 7. Rules for changing any of this

1. **Never rename a `SaveId`** without a migration step. It is the primary key.
2. **Never add a `version` bump without a migration step** — §6.3 now refuses what it cannot migrate,
   which is correct and will also refuse *your* old saves.
3. **Clear before restoring.** A `Load` that merges into a live collection carries the previous
   world's entries. `Clear()` the collection first and **write the `else` branch for every boolean**,
   or a flag set in the old session survives into the restored one.
4. **A new gameplay system ships with its persistence story** — that is what implementing `ISaveable`
   means here. If the answer is "it doesn't persist", say so in the class comment and add it to §5.
5. **A new header field is a save-compatibility change.** Old saves will not have it; give it a
   default that means "absent", the way `HasLocation` does for pre-29.5 saves.
6. **Test it against a save written by the previous build**, not only by the one you just changed.
   Round-tripping your own writes proves serialization, not compatibility.

## 8. Verifying a persistence change

```bash
dotnet test tests/Embervale.Tests     # SaveKeyPolicy and the pure helpers only
godot --path . -- --play              # boots the newest save; reports objects restored, 0 errors
```

⚠️ **The headless suite cannot reach most of this.** `SaveManager` is a `Node` and the test project
excludes `GodotObject` construction by design, so the load paths are verified in-engine. `--play`
prints `Loaded slot '<slot>'; restored N object(s)` — **compare N against the figure `NOW.md`
recorded**, because a silent drop is exactly what a broken `SaveId` looks like.

Still needing a human, and named rather than assumed:

1. Save → move → **F9** → the player must return to the save point, hard-loading if the region differs.
2. The same through the pause menu's Load.
3. A hand-corrupted `objects` block must refuse and drop to the title, not enter `Playing`.
