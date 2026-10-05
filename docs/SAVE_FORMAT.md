# Save format — the persistence contract

> **Why this file exists.** Persistence is the highest-risk system in the game: it is the only one
> whose defects destroy work the player already did, and the only one where a mistake made today
> lands on a player months from now. The 2026-08-15 audit found **two P0s living here**, and the
> format, the `SaveId` contract, the migration policy and — most importantly — **what is deliberately
> not saved** existed only as comments spread across `SaveManager.cs` and its callers.
>
> `ARCHITECTURE.md` §2.22 is how the machinery works. This is what the bytes mean and which rules you
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
that slot is written in the directory layout. The slot browser and Continue list them alongside
directory saves, preferring the directory when both layouts exist.

Slots are just directory names. `quick` is the default; `auto1`/`auto2`/`auto3` are the autosave ring
(`AutosaveService.RingSlots`).

## 2. The envelope

```json
{
  "version":   3,
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

Written by `SaveManager.BuildHeader` plus `SaveHeaderComposer.Build` (the gameplay half, wired
through `SaveManager.HeaderProvider` so the manager stays free of gameplay types).

| Field | Used for |
| --- | --- |
| `timestamp`, `playtime` | slot browser ordering and display |
| `region`, `region_id` | display name, **and the region a load restores into** |
| `player_x/y/z`, `player_yaw` | **the transform a load restores** |
| `race_id`, `char_name` | the character `StartLoadedGame` spawns |
| `appearance`, `background` | optional creator choices; appearance ids use `CharacterProfile`'s semicolon encoding; `background` is a `background.*` id (anything else, such as a pre-background free-text line, reads as no background). A background's kit, perk rank, flag and standing live in the Inventory/Perks/StoryFlags/Reputation saves; its stat deltas are re-derived from this header on load |
| `level`, `corruption_tier` | slot browser |

⚠️ **The header is not decoration — it is load-bearing.** Since the 2026-08-15 audit it drives where
and who the player is after a load. Treat a wrong header as a wrong save.

All four character fields are copied from the gameplay provider into both header copies and restored
before session construction. Older headers default missing appearance/background to empty strings;
these optional fields do not change version 3. An absent
player location stays absent when serialized (`HasLocation == false` omits the transform keys);
writing zero-valued coordinates would turn an old/no-player header into a teleport to the origin.

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
| `EncounterDirector` | roaming spawns re-roll. `SupplyShockService` exists as its own saveable node precisely because the director is not one. (⚠️ `WorldEventDirector` IS saved, as `world_events`: its cooldowns and the live event's origin — a world coordinate the v2 → v3 step drops.) |
| `RegionStreamer`, `SliceDirector`, `BossEncounterDirector` | rebuilt from the restored region |
| `MusicDirector`, `AmbienceDirector`, `AudioDirector` | audio re-derives from world state |
| `PlacementDirector` | intentional — a placed prop persists through `PersistentSpawnDirector`, which already records template, position and yaw |
| `GameManager.State` | the loader decides the state |
| Player transform / active region | **not** an `ISaveable` — they live in the header (§3) |

## 6. Failure policy

The rules, in the order a load applies them:

1. **No valid integer `version` field → refuse.** Every envelope this game has written carries one. Its absence
   means a truncated write, a hand-edit, or some other JSON document entirely.
   Fractional and out-of-range numeric versions are also refused instead of being truncated.
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

   **v2 → v3 (the 2026-09 world rebuild).** Every settlement moved and the realms grew about eight
   times in area. The step drops the header transform (the player lands at the region's `SpawnPoint`),
   `MapService`'s saved footprints and waypoint, and `WorldEventDirector`'s live event; sets every
   companion in `companions.party` to Follow so the post-load catch-up brings them to the player;
   shifts every `place.<property>#n` spawn by exactly the distance its property's build yard moved
   (from the v2 yard at world (95, 0, 90)); and re-seats `cache.world.start` beside the new spawn.
   Fast-travel entries and map pin positions are KEPT: `WorldPlaceIndex` (the bake's record of every
   waystone landing and map pin) outranks a saved coordinate wherever the bake has one, so a moved
   waystone moves every save's landing without a migration. ⚠️ **The v1 → v2 step read a `state` key
   the envelope never had** and so never discarded what it documented; it reads `objects` now.

4. **`version` < current with no migration step → refuse.** ⚠️ This used to warn and load at best
   effort. v1 is the first format that ever existed, so there is no legitimate older save — the
   branch only ever caught corrupt or foreign files, and waved them through into live components.
   When a v2 arrives, register a step that upgrades `root` in place; **an unmigratable save must fail
   loudly, never load in pieces.**
5. **No dictionary `objects` section, or any non-dictionary object state → refuse.** A corrupt entry
   is detected before any live saveable is changed; it is not mistaken for an absent system.
6. **Any `ISaveable.Load` throws → the whole load fails.** Each exception is caught so one bad entry
   cannot abort the other thirty-odd, but the result is reported as a failure and
   `GameLoadedEvent` is **not** published. ⚠️ **A partial restore is a failed load**: the caller
   abandons the session to the title, because continuing hands the player a world assembled from half
   the save and half of whatever was already live, and the next autosave writes that over the good
   file.
   This also includes components that register while persistent actors are being recreated. An
   exception from the final location restore returns failure and does not publish `GameLoadedEvent`.
7. **An entry with no live claimant** logs an orphan warning (drift, or a renamed `SaveId`). Entries a
   streamed-out cell is holding are claimed via `ClaimDeferred` and are *not* reported — warning on
   the healthy path is how a diagnostic teaches you to ignore it.
8. **A live saveable with no entry** receives `Load(empty dictionary)`. The implementation must
   replace the abandoned timeline with its empty/default state, including clearing spawned actors.
   A reset exception is a failed load. Registrations removed or queued for deletion by an earlier
   restore are skipped when the manager reaches their old snapshot entries.

Writes are atomic: staged to `<target>.tmp`, then renamed over the target, so a crash mid-write can
never truncate a good file. The staged file is flushed and checked for write errors before commit.
**A snapshot with any failed capture or empty/duplicate `SaveId` is refused before commit.** The
previous authoritative file and header remain intact, and `GameSavedEvent` is not published.
An exception while composing the header or serializing also returns failure.

**Save/load operations cannot nest.** Callbacks run during restoration and can publish gameplay
events; a save triggered by one must not overwrite the source with an intermediate world. A nested
save or load returns failure until the outer operation has completed.

**Player loads rebuild the session.** F9 and the pause menu use the same deferred
`SessionLifecycleCoordinator.RequestReload` route as the slot browser's fresh-session load. This
recreates authored actors removed after the checkpoint, restores the saved character and region,
and uses that region's spawn when a migrated/older header carries no transform. An absent checkpoint
leaves the current session running. Only one request may be pending; ending its session cancels the
callback. The low-level `SaveManager.LoadGame` remains the overlay used within that rebuilt session
and by native probes. F5/F9 are available in capture and exported builds; save/reload keys are
ignored while the world is loading.

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
python tools/embervale.py world --mode engine --gate save-audit # native integrity probe with isolated saves
python tools/embervale.py world --mode engine --gate save-reload # F9/pause session rebuild and cancellation
```

⚠️ **The headless suite cannot reach most of this.** `SaveManager` is a `Node` and the test project
excludes `GodotObject` construction by design, so the load paths are verified in-engine. `--play`
prints `Loaded slot '<slot>'; restored N object(s)` — **compare N against the figure `NOW.md`
recorded**, because a silent drop is exactly what a broken `SaveId` looks like.

Still needing a human, and named rather than assumed:

1. Save → move → **F9** → the player must return to the save point, hard-loading if the region differs.
2. The same through the pause menu's Load.
3. A hand-corrupted `objects` block must refuse and drop to the title, not enter `Playing`.
