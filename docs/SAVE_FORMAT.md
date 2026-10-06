# Save format — the persistence contract

> **Why this file exists.** Persistence is the highest-risk system in the game: it is the only one
> whose defects destroy work the player already did, and the only one where a mistake made today
> lands on a player months from now. The 2026-08-15 audit found **two P0s living here**, and the
> format, the `SaveId` contract, the migration policy and — most importantly — **what is deliberately
> not saved** existed only as comments spread across `SaveManager.cs` and its callers.
>
> `ARCHITECTURE.md` §2.22 is how the machinery works. This is what the bytes mean and which rules you
> cannot break without costing someone their progress.

**Where the rules live in code.** The decisions are pure C# with no engine in them, so xUnit runs
every one; `SaveManager` does the file work and the dispatch to live saveables.

| File (`src/Save/`) | Owns |
| --- | --- |
| `SaveEnvelope.cs` | validating a document without applying it: parse, shape, version, checksum (§6) |
| `SaveChecksum.cs` | the canonical content checksum (§2.1) |
| `SaveMigrations.cs` | the v1 → v4 chain (§6 rule 3) |
| `SaveBackup.cs` | when the backup generation is kept, and when a load reads it (§1.1) |
| `SetPieceSaveIds.cs` | the stable set-piece key (§4) |
| `SaveRead.cs` | tolerant dictionary reads for `ISaveable.Load` (§6 rule 9) |
| `SaveSlots.cs`, `SaveKind.cs`, `SaveHealth.cs` | the slot rosters and what a slot is |
| `SaveManager*.cs` | save, load, inspect, delete; the save-block gate |

---

## 1. Layout on disk

```
user://saves/<slot>/
    save.json        the full envelope — authoritative
    save.json.bak    the previous generation: the save that save.json replaced (§1.1)
    header.json      a read mirror of the envelope's header, for the slot browser
    screenshot.png   320×180 thumbnail, best-effort, never load-bearing
    *.tmp            a file being staged; never read, removed at startup if a crash left one
```

Legacy flat saves (`user://saves/<slot>.json`) are still **readable**, and are deleted the first time
that slot is written in the directory layout. The slot browser and Continue list them alongside
directory saves, preferring the directory when both layouts exist.

Slots are just directory names, and `SaveSlots` (mirrored on `SaveManager`) names the ones the game
uses: `quick` (F5/F9), the manual roster `slot1`..`slot6`, and the autosave ring `auto1`..`auto3`
(`AutosaveService.RingSlots` is the same array). `SaveSlots.KindOf` turns a slot id into a
`SaveKind` (`Manual`, `Quick`, `Auto`); any other name is `Manual`.

### 1.1 The backup generation

Each slot keeps **exactly one** previous save. When a save replaces `save.json`, the file being
replaced is renamed to `save.json.bak` between staging the new file and committing it.

- **Only a sound save is kept.** The outgoing `save.json` is validated first (`SaveBackup.ShouldRotate`)
  and a damaged one is overwritten in place. Rotating it would put a corrupt file on top of the good
  generation, which is the one case the backup exists for.
- **A load falls back to it** when `save.json` is unreadable or gone and the backup is sound
  (`SaveBackup.Choose`). The player is told: `SaveManager.LastLoadUsedBackup` is set, a toast is
  raised (`save.recovered.title` / `save.recovered.detail`), and before the load
  `InspectSlot` reports `RecoveredFromBackup` with the **backup's** header.
- ⚠️ **A save from a newer build is never bypassed.** A `Newer` primary with a loadable backup stays
  `Newer` and is refused: loading the older generation would silently discard what the newer build
  saved, and the next save would bury it.
- **The window with no primary is survivable.** Between the two renames only `save.json.bak` exists.
  `SaveExists` counts it and a load reads it, so a crash there costs nothing but the save in flight.
- ⚠️ **The backup has no `header.json.bak`.** Its header is the one inside its own envelope. A second
  mirror would be a second file that can go stale against the save beside it, for a read that only
  happens on the recovery path; §3 is what a stale mirror costs.
- `DeleteSlot` removes the backup with everything else.

## 2. The envelope

```json
{
  "version":   4,
  "timestamp": 1755270000.0,
  "checksum":  "sha256:9f2c…",
  "header":    { ... },
  "objects":   { "<SaveId>": { ...component state... } }
}
```

`objects` is a flat map of `SaveId → state`. **The set of live objects drives restoration**, not the
file: on load, each registered `ISaveable` pulls *its own* entry by id. That is what lets the game
scale to hundreds of actors without bespoke save code, and it is why the two directions fail
differently — see §6.

### 2.1 The checksum

`checksum` is `"sha256:"` plus the lowercase hex SHA-256 of a **canonical form of `objects`**
(`SaveChecksum`). It is verified on every load and by `InspectSlot`; a mismatch is a corrupt save.

- **Canonical** means independent of how the JSON was written. Object keys are visited in ordinal
  order, whitespace is not part of it, strings are hashed decoded, and **numbers are hashed by value**
  (`50`, `50.0` and `5e1` are one number; a fraction is taken to 15 significant digits). Re-indenting
  a save, or reading it and writing it back unchanged, keeps the checksum. Changing any key, string
  or number, dropping an entry, or reordering an array breaks it.
- ⚠️ **Numbers are by value on purpose.** The engine's JSON parser does not promise to return the
  type it was given, so a tool that round-trips a save through it (`HeadlessLifecycle`'s v2 fixture
  does) would otherwise invalidate content it never touched.
- **Only `objects` is covered.** The header is not: the header carries the checksum itself (§3), and
  a tool may restamp a header without touching the content.
- **An absent checksum is accepted.** Every save before format 4 has none. A checksum that is present
  but is not a `sha256:` string that matches is a mismatch, including one of a kind this build does
  not know.
- It is an integrity check against damage, not a signature. Anyone who edits a save by hand can
  delete the field.
- **Writing it.** The envelope is serialized once with a placeholder, the checksum is computed from
  that text, and the placeholder is replaced. ⚠️ That read-back is also the last guard on the write:
  if the serialized document does not parse, the save is refused and the previous file is kept.

## 3. The header

Written by `SaveManager.BuildHeader` plus `SaveHeaderComposer.Build` (the gameplay half, wired
through `SaveManager.HeaderProvider` so the manager stays free of gameplay types).

| Field | Used for |
| --- | --- |
| `timestamp`, `playtime` | slot browser ordering and display |
| `region`, `region_id` | display name, **and the region a load restores into** |
| `player_x/y/z`, `player_yaw` | **the transform a load restores** |
| `race_id`, `char_name` | the character `StartLoadedGame` spawns |
| `appearance`, `background` | optional creator choices; see §3.1 |
| `level`, `corruption_tier` | slot browser |
| `kind` | how the save was made: the `SaveKind` ordinal (0 Manual, 1 Quick, 2 Auto). Always written |
| `format` | the save format version the file was written in. Written when known (always, since format 4) |
| `build` | the game build that wrote it (`application/config/version`). Written when non-empty |
| `checksum` | the envelope's checksum (§2.1), so a mirror can be matched to its save. Written when present |
| `display_name` | the player's own label for the save. Written when non-empty |

Absent-defaults for the five newer keys: no `kind` answers by slot name (`SaveSlots.KindOf`), no
`format` is 0 (unknown; `InspectSlot` fills it from the envelope), no `build`, `checksum` or
`display_name` is empty. None of them changes the format version.

`SaveSlotInfo` also carries findings that are **never written to disk**: `Health`, `PrimaryHealth`,
`RecoveredFromBackup` and `HasBackup`. They are what `InspectSlot` learned about the files.

⚠️ **The header is not decoration — it is load-bearing.** Since the 2026-08-15 audit it drives where
and who the player is after a load. Treat a wrong header as a wrong save.

All four character fields are copied from the gameplay provider into both header copies and restored
before session construction. An absent player location stays absent when serialized
(`HasLocation == false` omits the transform keys); writing zero-valued coordinates would turn an
old/no-player header into a teleport to the origin. On read the transform is all-or-nothing on
`player_x`.

⚠️ **`header.json` is a mirror and must never be stale.** It is written after `save.json` commits,
with no transaction across the two. If that write fails the mirror is **deleted**, because
`ReadHeader` prefers it and would otherwise answer every question about this save with the previous
save's answers — including the region and position. A missing mirror is free (`ReadHeader` falls back
to the envelope's copy, then to the backup generation's); a stale one misplaces the player.

⚠️ **`ReadHeader` is the cheap read and trusts the files it finds; `InspectSlot` is the true one.**
`InspectSlot` validates the envelope and takes the header **from the envelope a load would read**,
never from the mirror, so it is right even when `save.json` is damaged and the backup will be loaded
instead. Anything about to load a slot should build from `InspectSlot`'s header.

### 3.1 Appearance and background

Appearance ids (`appearance.*`, one per slot) use `CharacterProfile`'s semicolon encoding, and an
absent, stale or not-offered id resolves to the slot default (`AppearanceRules.Resolve`) so nothing
is saved beyond the ids. `background` is a `background.*` id; anything else, such as a
pre-background free-text line, reads as no background. A background's kit, perk rank, flag and
standing live in the Inventory/Perks/StoryFlags/Reputation saves; its stat deltas are re-derived from
this header on load. Older headers default both to empty strings.

## 4. The `SaveId` contract

A `SaveId` is a **stable string key**. Renaming one silently orphans everything it saved.

Three shapes:

- **World services** — a bare noun, one per world: `map`, `weather`, `worldclock`, `shopstock`,
  `spawns`, `cell_persistence`, `companions`, `bestiary`, `contracts`, `fasttravel`, `housing`,
  `haggles`, `wagers`, `consignment`, `contraband_impound`, `shocks`, `tutorial`.
- **Per-actor components** — `"<prefix>:<PersistentId>"`, built by `SaveKeyPolicy.Key`:
  `inventory:player`, `stats:npc.holt`, and so on.
- **Set pieces** — `"setpiece:<region>/<cell>#<node path>"`, built by `SetPieceSaveIds.Build`:
  `setpiece:ember_crown/citadel#RampSentries`. The slug is the same whether the cell was loaded from
  its authored scene or from the bake. ⚠️ Until format 4 this key embedded the cell's `res://` scene
  path, the one key that broke "references are ids, never paths"; the v3 → v4 step (§6 rule 3) rewrites it.

⚠️ **A component persists only if its owner has a `PersistentId`** (`SaveKeyPolicy.ShouldPersist`).
Transient actors — spawned mobs, the training dummy — are session-only **by design**: a runtime-keyed
entry can never be reclaimed after a world rebuild, because the reloaded actor gets a fresh runtime
id, so it would both fail to restore *and* linger as orphaned state. The `savecheck` dev command
flags any volatile key (`SaveKeyPolicy.IsVolatile`); there should be none.

**References are ids, never paths or indices.** Spawned actors round-trip as
`{pid, tid, x, y, z, yaw}` and are rebuilt through `PersistentActorRegistry.Create`. Nothing in a save
points at a scene path or an array position, which is why authoring can move freely.

### 4.1 Additive keys with absent-defaults

A component may grow keys without a version bump if `Load` says what absent means. The ones that
exist:

| Entry | Key | Holds | When absent |
| --- | --- | --- | --- |
| `perks:<pid>` | `ranks` | perk id → rank | no perks |
| `perks:<pid>` | `free` | perk id → ranks that cost no points (kept by a respec) | the race's innate perks count as one free rank each |
| `perks:<pid>` | `spent` | skill points invested | the sum of `(rank - free) * Cost` over held perks |
| `perks:<pid>` | `respecs` | respec count | 0 |
| `crafting:<pid>` | `known` | recipe ids | none known |
| `crafting:<pid>` | `crafts` | completed crafts (the serial the material-saving roll derives from) | 0, and `Load` replaces a live serial even then |
| `progression:<pid>` | `ms` | highest level whose milestone skill point was paid | see below |
| `inventory:<pid>` | `stacks` | the pack: `[{qty, instance}]` | empty pack |
| `inventory:<pid>` | `materials` | the material bag, same entry shape. Written only when non-empty | empty bag |
| item instance | `quality` | `CraftQuality` ordinal. Written only off Standard | Standard |
| item instance | `upgrade` | upgrade level, clamped to `0..ItemUpgrades.MaxLevel`. Written only above 0 | 0 |
| item instance | `ilvl` | the level the item was rolled at. Written only above 0 | 0: the template's own item level |
| item instance | `locked` | the player's lock mark. Written only when true | false |
| item instance | `junk` | the player's junk mark. Written only when true | false |

`perks` `Load` strips what it applied, replaces everything from the save and never re-checks
prerequisites. A quickload replays a craft's outcome from `crafts` instead of rerolling it.

**`ms`** — levels 10, 20, 30, 40 and 50 each pay one skill point. A save from before milestones has
no `ms`: the points for every milestone at or below the saved level are paid once on load and `ms`
is set to that level, so the key is written from then on.

**`materials`** — `InventoryComponent.Load` clears both stores and sends every entry of both lists
back through `AddInstance`. So an old save's materials move into the bag when the component uses one
(`UseMaterialBag`), and a saved bag lands in the pack, over capacity if need be, when it does not.
Nothing is discarded either way. During `Load` a marked and an unmarked stack of one item are not
merged.

An item instance is `{id, rarity, name, affixes}` plus the five keys above. An instance whose `id`
is no longer in the item database is dropped from its stack (§6 rule 9).

## 5. What is NOT saved

Everything here resets on load, deliberately. **Check this list before assuming a bug.**

| Not saved | Consequence |
| --- | --- |
| `EncounterDirector` | roaming spawns re-roll. `SupplyShockService` exists as its own saveable node precisely because the director is not one. (⚠️ `WorldEventDirector` IS saved, as `world_events`: its cooldowns and the live event's origin — a world coordinate the v2 → v3 step drops.) |
| `RegionStreamer`, `SliceDirector`, `BossEncounterDirector` | rebuilt from the restored region |
| `MusicDirector`, `AmbienceDirector`, `AudioDirector` | audio re-derives from world state |
| `PlacementDirector` | intentional — a placed prop persists through `PersistentSpawnDirector`, which already records template, position and yaw |
| `GameManager.State` | the loader decides the state |
| `StatDerivationComponent` | primaries' derived-stat modifiers are rebuilt from the primaries, which come back from level, race, gear and perks |
| Player transform / active region | **not** an `ISaveable` — they live in the header (§3) |
| Save blocks (`PushSaveBlock`) | a block belongs to something live in the world; session teardown clears them |
| A set piece's spawned enemies | transient; only `fired` and `cleared` are saved, and the cleared story flag is the durable truth |
| `SaveSlotInfo.Health`, `PrimaryHealth`, `RecoveredFromBackup`, `HasBackup` | findings about the files, recomputed by `InspectSlot` |

## 6. Failure policy

Rules 1 to 6 are decided on the file's **text**, by `SaveEnvelope.Read`, before anything live is
touched. `SaveManager.InspectSlot` runs the same validation for the slot browser, so a slot it calls
`Ok` is one a load will accept. The rules, in the order a load applies them:

0. **Pick the generation.** If `save.json` fails any of rules 1 to 6 **except rule 2**, or is
   missing, and `save.json.bak` passes them all, the backup is loaded instead and the player is told
   (§1.1). Otherwise the primary's own failure is the result.
1. **No valid integer `version` field → refuse.** Every envelope this game has written carries one. Its absence
   means a truncated write, a hand-edit, or some other JSON document entirely.
   Fractional and out-of-range numeric versions are also refused instead of being truncated.
2. **`version` > current → refuse** (`SaveHealth.Newer`). A newer save cannot be read by an older
   build, and its backup is not used in its place.
3. **`version` < current WITH a migration step → migrate forward**, one step at a time
   (`SaveMigrations`). The steps:

   **v1 → v2 (the 2026-08-29 geography overhaul).** Every world coordinate a v1 document holds was
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
   waystone moves every save's landing without a migration. The step needs the Ember Crown's spawn
   point and each property's yard centre; they are **injected** (`SaveMigrationLookups`), which is
   what keeps the chain testable. ⚠️ **The v1 → v2 step read a `state` key the envelope never had**
   and so never discarded what it documented; it reads `objects` now.

   **v3 → v4 (stable set-piece keys, and the checksum).** A set piece saved under
   `setpiece:res://…/<cell>.tscn#<node>` (or the baked cell's `.scn` path), the one `SaveId` that
   named a file. Every such key is rewritten to `setpiece:<region>/<cell>#<node>` (§4), at the top
   level of `objects` and inside `cell_persistence.state`, which is keyed by the same ids. The state
   under each key is untouched, and so is the header: a v3 transform was written against the current
   world and is restored. If a document holds both forms of one piece, the stable entry wins. The
   checksum needs no step: an absent one is accepted and the next save writes one.

4. **`version` < the first format (1) → refuse.** ⚠️ This used to warn and load at best
   effort. v1 is the first format that ever existed, so there is no legitimate older save — the
   branch only ever caught corrupt or foreign files, and waved them through into live components.
   **An unmigratable save must fail loudly, never load in pieces.**
5. **No dictionary `objects` section, or any non-dictionary object state → refuse.** A corrupt entry
   is detected before any live saveable is changed; it is not mistaken for an absent system.
6. **A checksum that does not match the content → refuse** (§2.1). The file parses and has the right
   shape, and something in it is not what was written.
7. **Any `ISaveable.Load` throws → the whole load fails.** Each exception is caught so one bad entry
   cannot abort the other thirty-odd, but the result is reported as a failure and
   `GameLoadedEvent` is **not** published. ⚠️ **A partial restore is a failed load**: the caller
   abandons the session to the title, because continuing hands the player a world assembled from half
   the save and half of whatever was already live, and the next autosave writes that over the good
   file.
   This also includes components that register while persistent actors are being recreated. An
   exception from the final location restore returns failure and does not publish `GameLoadedEvent`.
8. **An entry with no live claimant** logs an orphan warning (drift, or a renamed `SaveId`). Entries a
   streamed-out cell is holding are claimed via `ClaimDeferred` and are *not* reported — warning on
   the healthy path is how a diagnostic teaches you to ignore it.
9. **Missing content never bricks a save.** Rule 7 is for a `Load` that cannot do its job, not for a
   save that names something the build no longer has. Content that is gone is **skipped with a
   warning** and the rest of the save loads:
   - a persistent actor whose template is no longer registered is not recreated
     (`PersistentSpawnDirector.Load`); a template that *is* registered and builds nothing is a defect
     in the build and still fails the load;
   - an item whose template is gone is dropped from its stack (`ItemInstance.FromSave` returns null);
   - a `Load` reads its dictionary through `SaveRead` (`Text`, `Int`, `Float`, `Number`, `Flag`,
     `Section`, `AsSection`, `List`), which answers an absent key or a value of the wrong type with
     the caller's fallback. ⚠️ `data["key"]` throws on an absent key and `AsGodotDictionary()` on a
     non-dictionary, and either one turns a single odd entry into a lost slot.
10. **A live saveable with no entry** receives `Load(empty dictionary)`. The implementation must
    replace the abandoned timeline with its empty/default state, including clearing spawned actors.
    A reset exception is a failed load. Registrations removed or queued for deletion by an earlier
    restore are skipped when the manager reaches their old snapshot entries.

### 6.1 Writing

Writes are atomic: staged to `<target>.tmp`, then renamed over the target, so a crash mid-write can
never truncate a good file. The staged file is flushed and checked for write errors before commit.
`save.json`, `header.json` and `screenshot.png` are all written this way. A staged file that could
not be committed is removed, and any `*.tmp` a crash left in the save folder is removed at startup;
a staged file is never read, so nothing is lost with it.

**A snapshot with any failed capture or empty/duplicate `SaveId` is refused before commit**, and so
is one whose serialized form does not read back (§2.1). The previous authoritative file and header
remain intact, and `GameSavedEvent` is not published. An exception while composing the header or
serializing also returns failure.

**Save/load operations cannot nest.** Callbacks run during restoration and can publish gameplay
events; a save triggered by one must not overwrite the source with an intermediate world. A nested
save or load returns failure until the outer operation has completed.

### 6.2 Save blocks and save events

`SaveManager.PushSaveBlock(reasonKey)` blocks saving until the returned token is disposed (a boss
fight, a conversation). Blocks nest, release in any order, and a double dispose is harmless.
`SaveManager.ClearSaveBlocks()` drops them all at session teardown.

- ⚠️ **A live block refuses every save, autosaves included.** `SaveGame` returns false. A block says
  that a save taken now would be a bad place to come back to, which is as true of a save the game
  takes as of one the player asks for.
- `CanSaveNow(out reasonKey)` is the question a menu asks first: the newest live block's reason,
  else `save.blocked.busy` during a save or load, else `save.blocked.no_world` when there is no
  world. `SaveGame` enforces the first two; it does not enforce the third, because the native probes
  save with no world.

Events (`Embervale.Core.Events`), on every path through `SaveGame`:

| Path | Events, in order |
| --- | --- |
| saved | `SaveStartedEvent(slot, kind)` → `GameSavedEvent(slot, isAutosave)` |
| started and failed (capture, serialize, write) | `SaveStartedEvent` → `SaveFailedEvent(slot, "save.failed.write")` |
| refused by a block | `SaveFailedEvent(slot, <the block's reason key>)`, no start |
| refused because a save or load is running | `SaveFailedEvent(slot, "save.blocked.busy")`, no start |

Every start is followed by exactly one `GameSavedEvent` or `SaveFailedEvent`. `kind` is `Auto` for
an autosave, else the slot's kind.

### 6.3 Deleting

`DeleteSlot(slot, out failures)` removes the save, its backup, the header, the thumbnail, any staged
file and the legacy flat file. It returns true only when the slot existed and is now gone, and
`failures` names each file that would not go with the engine's reason. `DeleteSlot(slot)` is the same
call without the list. An absent slot returns false with no failures.

### 6.4 Loading rebuilds the session

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
2. **Never add a `version` bump without a migration step** — §6 rule 4 refuses what it cannot migrate,
   which is correct and will also refuse *your* old saves. A step is a pure function in
   `SaveMigrations` over the parsed document, with whatever world data it needs injected through
   `SaveMigrationLookups`, and it ships with a golden fixture of the format it upgrades
   (`tests/Embervale.Tests/Fixtures/saves`).
3. **A step changes what it documents and nothing else.** Anything it does not name comes out as it
   went in; `SaveMigrationsTests` checks that against a genuine v1 save.
4. **Clear before restoring.** A `Load` that merges into a live collection carries the previous
   world's entries. `Clear()` the collection first and **write the `else` branch for every boolean**,
   or a flag set in the old session survives into the restored one.
5. **A new gameplay system ships with its persistence story** — that is what implementing `ISaveable`
   means here. If the answer is "it doesn't persist", say so in the class comment and add it to §5.
6. **A new key is additive with an absent-default**, and goes in the §4.1 table. Read it with
   `SaveRead`.
7. **A new header field is a save-compatibility change.** Old saves will not have it; give it a
   default that means "absent", the way `HasLocation` does for pre-29.5 saves.
8. **Never put a path in a save.** A `res://` path in a key or a value ties the save to where a file
   happens to live. The set-piece key did, and it took a format version to undo.
9. **A refusal string is pinned.** `tools/world_quality_check.py` matches the save-audit gate's
   expected error lines by regex, including the logging method's name (`SaveGameCore`, `AtomicWrite`,
   `LoadGameCore`, `LoadGame`, `Register`). Change a message or rename one of those methods and the
   regex in the same commit.
10. **Test it against a save written by the previous build**, not only by the one you just changed.
    Round-tripping your own writes proves serialization, not compatibility.

## 8. Verifying a persistence change

```bash
dotnet test tests/Embervale.Tests --filter "FullyQualifiedName~Save"   # envelope, checksum, migrations, backup, keys
godot --path . -- --play              # boots the newest save; reports objects restored, 0 errors
python tools/embervale.py world --mode engine --gate save-audit # native integrity probe with isolated saves
python tools/embervale.py world --mode engine --gate save-reload # F9/pause session rebuild and cancellation
```

**What xUnit covers** (pure, no engine): every refusal in §6 rules 1 to 6, the checksum's canonical
form, each migration step against golden v1, v2 and v3 envelopes, the backup selection table and the
set-piece key. **What the save-audit probe covers** (in engine, isolated user dir): capture failures,
save events and blocks, the checksum and backup on real files, fallback, migrations through a live
load, restore failures, a missing actor template, legacy discovery and deletion.

⚠️ **The headless suite cannot reach the rest.** `SaveManager` is a `Node` and the test project
excludes `GodotObject` construction by design, so file handling and dispatch are verified in-engine.
`--play` prints `Loaded slot '<slot>'; restored N object(s)` — **compare N against the figure
`NOW.md` recorded**, because a silent drop is exactly what a broken `SaveId` looks like.

Still needing a human, and named rather than assumed:

1. Save → move → **F9** → the player must return to the save point, hard-loading if the region differs.
2. The same through the pause menu's Load.
3. A hand-corrupted `objects` block with no backup must refuse and drop to the title, not enter `Playing`.
4. Corrupt `save.json` with a good `save.json.bak` → the load succeeds from the backup and the toast shows.
