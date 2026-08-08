## Phase 37 — Housing & Player Property `[F]`

- [x] **37A — `PropertyComponent` + `HousingService` (claim/own)** `[F]` ✅
  - **Done when:** a property can be purchased/claimed; ownership is `ISaveable`.
  - **Scope call (maintainer):** 37A alone, matching the 36A–E rhythm — one reviewable change,
    verified before the next builds on it. Claiming is authored **per property** as a gold price, a
    required quest, or both, so the roadmap's purchasable housing is real now and Phase 38 inherits
    a real sink to tune rather than one invented later.
  - **The scaffolding risk, and what avoided it:** "you own a plot with nothing on it" is close to
    the §1 line. So a claim does one useful thing immediately — it registers the holding as a
    fast-travel destination through the existing `FastTravelService.Discover`, which is the
    housing↔Phase 25 tie the roadmap already asks for. 37A is playable on its own rather than a
    record waiting for 37B.
  - **Done:** `PropertyResource` + `PropertyDatabase` (the `BossDatabase` mirror), `HousingService`
    (`Node, ISaveable`, shaped on `FastTravelService` — the one service in the repo that registers
    **and unregisters** with both the locator and the save manager), `PropertyDeedComponent`
    (`InteractableComponent`, the `TravelNodeComponent`/`BossSummonComponent` shape), and the pure
    `PropertyClaim.Resolve` — 8 tests. The prompt and the interaction both read that one function, so
    what the player is told and what happens cannot drift.
  - **Order matters and is pinned:** owned → quest-locked → too-expensive. Reporting the price first
    would send a player off to earn 600 gold for something a quest is holding shut anyway. Each
    refusal names itself, because `BossSummonComponent` already learned that an inert interactable
    giving no reason "reads as a bug rather than a gate".
  - Reused rather than reinvented: the travel node is recorded at the **player's** position, not the
    post's — landing fast travel inside a collider is a trap `TravelNodeComponent` paid for once.
  - **Authored:** the Ashfall Cottage in the town hub — gated on `quest.warband.bounty` (the town
    sells to someone who has done something for the town) *and* priced at 600 gold, so the quest
    earns the right to pay rather than replacing the payment. A deed post sits south-west of the
    square, clear of the buildings and the waystone.
  - Build clean + 798 tests + `--validate` exit 0 with **every** new rule negative-tested (unknown
    region, unknown quest, missing name key, free-on-touch, no travel node) + the edited town hub
    headless-instantiated to confirm the deed parses + 3 clean `--play` runs. An existing save logs
    one `no usable entry for 'housing'` warning — the framework's designed path for a saveable added
    after the save was written (warn, keep current state = nothing owned); it is the only such
    warning and it clears on the next save.
  - **Unlike the boss phases, this one is reachable from `--play`:** the deed stands in the town hub,
    where `--play` resumes. The at-keyboard pass is the prompt's three refusals and the claim itself.
- [x] **37B — Per-property persistent storage** `[F]` ✅
  - **Done when:** property storage extends inventory persistence and round-trips.
  - **The whole phase is one observation:** an `Entity` with an authored `PersistentId` and an
    `InventoryComponent` *already* round-trips, twice over — through `SaveManager` as
    `inventory:<PersistentId>`, and across region-cell churn through `CellPersistenceDirector`. So
    "extends inventory persistence" was met by **authoring a chest**, not by writing persistence.
    37B adds **zero** save code. The alternative on the table was a `PropertyStorageService` keyed by
    property id, saving its own `{property: stacks}` blob — that would have reimplemented
    `ItemInstance.Save`/`FromSave` and the stacking rules alongside the ones that already work, and
    given two things to keep in step forever.
  - **What was genuinely new is the surface.** `ContainerLootComponent`, the repo's only container,
    is one-way: it pops its contents onto the floor as pickups and has never had a deposit path. So
    the two-way window is the actual build, not a reuse — worth saying out loud, because the roadmap
    line makes 37B sound like a persistence task when the persistence was the free half.
  - **Done:** `PropertyStorage.Resolve` (pure, the `PropertyClaim` sibling — 4 tests),
    `PropertyStorageComponent` (`InteractableComponent`, publishes `StorageOpenedEvent` carrying the
    container's own inventory), `StoragePanel` (the `CraftingPanel` shape: event-driven, `E` closes,
    dirty-flag rebuild, two `UiTheme.ScrollList` columns with Store/Take per row), and the
    `CottageChest` against BuildingSW's north wall in the town hub — `prp_cache_chest.glb`, already
    imported and credited for the 30J supply cache, so no new asset.
  - **Ordering is pinned here too:** unknown-property before not-owned. An unresolvable id is an
    *authoring* fault, not a gate a player can pass; reporting it as "not yours" would send someone
    off to buy a property that does not exist and would hide a typo behind a plausible refusal. So
    an unknown id shows **no prompt at all** rather than a lie.
  - **The one real trap, and it is a data-loss one.** `InventoryComponent.Load` restores through
    `AddInstance`, which clamps to `Capacity` — so capacity must be authored on the chest's own node
    and not applied by a sibling after the save manager's mid-load restore, or the overflow vanishes
    silently on reload. That is why `PropertyResource` deliberately gained **no** `StorageCapacity`
    field. Each property has its own chest; the node value is already per-property.
  - **A bug deliberately not copied:** `ContainerLootComponent.Interact` removes by template id,
    which matches across every stack of that template — two distinct affixed instances of one
    template see the first removal satisfy both, and one evaporates. `StoragePanel.Transfer` branches
    on `ItemInstance.IsStackable` and uses the reference-based `RemoveOneInstance` for rolled items,
    and only ever removes what `AddInstance` reported as landed, so a full destination cannot eat the
    remainder. (The loot component's own copy of that bug is left alone — it is a different phase's
    fix and touching it here would widen a storage change into a loot change.)
  - **No new `--validate` rule, deliberately.** 37B adds no data fields: capacity and `PropertyId`
    both live in a `.tscn`, which `ContentValidator` does not scan, so a rule here would guard
    nothing. The five existing 37A property rules were re-run as a regression check instead — each
    negative-tested individually, each still rejects, exit **1** broken and **0** clean.
  - Build clean + **802** tests + `--validate` exit 0 + the edited town hub headless-instantiated
    (chest parses, `PersistentId`/`Capacity`/`PropertyId` all present, 37A's deed post intact) + 3
    clean `--play` runs, no errors and no unexpected warnings. The pre-existing `no usable entry`
    warnings on the old quick save are unchanged and are the framework's designed path.
  - **Reachable from `--play`:** the chest stands beside the deed post in the town hub, where
    `--play` resumes. The at-keyboard pass is the locked refusal before claiming, the claim, a
    store/take round trip with an affixed item and a partial stack, a full chest refusing the
    remainder, and an `F5`/`F9` plus cold reload with items inside.
- [x] **37C — Placeable crafting stations + decoration** `[F]` ✅
  - **Done when:** the player can place stations (`CraftingStationFactory`) and
    decorations in an owned property; placement persists.
  - **Persistence was free for the third phase running, and for a third different reason.** 37B
    reused `InventoryComponent`'s save path; 37C reuses `PersistentSpawnDirector`, which exists
    precisely for runtime-spawned things with no authored id and already records template, position
    and yaw and reconciles them on load. 37C adds **no `ISaveable`**. What it adds is builders
    (`PlaceableTemplates`) and an id scheme (`PlacementIds`).
  - **What was new is the mechanic.** There was no placement of any kind in this repo — no ghost, no
    preview, no build mode, no validity test, no overlap check. Grepping for any of it returned only
    unrelated prose. This is the game's first world-editing verb.
  - **`CraftingStationFactory` had zero callers.** It sat unused since Phase 15 while both
    settlements authored their stations by hand in their cell `.tscn`, and its class doc claimed
    otherwise. The roadmap line named it, so 37C is what finally gave it a job — every station the
    player places is built there. It gained an optional `modelPath` so a placed forge uses the same
    `prp_station_forge.glb` the town hub does, falling back to its old box.
  - **The gap that needed real data:** `PropertyResource` had no spatial fields at all, so "in an
    owned property" was unexpressible. It gained `PlacementCenter` (**world** space — the streamer
    has already moved a cell to its `Center`, so a point copied out of a `.tscn` lands a cell's width
    off) and `PlacementRadius`, where `0` is a holding you may not build in.
  - **Done:** `PlacementCheck.Resolve` (pure, third in the `PropertyClaim`/`PropertyStorage` line),
    `PlacementIds`, `PlaceableTemplates`, `PlacementDirector` (ghost, aim ray, overlap probe, commit,
    removal), `PlacementHud`, `PlaceableItemResource`, and 6 kits + 6 recipes. **14 new tests.**
  - **Ordering pinned again:** owned → ground → inside the holding → blocked. Saying "blocked" to
    someone standing in the town square sends them shuffling two metres left when what they need to
    hear is that they are nowhere near their own house.
  - **Two traps the shape of this phase avoids.** ① `PlacementIds.Next` derives the index by scanning
    the ids that already exist rather than counting: `PersistentSpawnDirector._autoId` is **not
    persisted**, so a counter hands out `#1` again after a load, and `Spawn` answers a known id by
    returning the existing actor — the new prop would simply never appear, and only in a session that
    had loaded a save. ② The ghost is built by the *same* builder and then stripped back to its mesh,
    so it carries no collider (it would block its own probe and its own aim ray) and no components (a
    ghost station that opened the crafting window would be a genuinely confusing bug).
  - **No `ItemType` enum append.** Its ordinals are persisted in every save, so appending is
    irreversible; `PlaceableItemResource` is the marker instead and the panel filters on the type.
  - **Five new `--validate` rules, all negative-tested:** negative placement radius; a placement
    centre outside the region's `Bounds`; a kit naming no template; a kit naming an unregistered one;
    and — the one worth having — **every template is actually built during validation** and checked
    for being an `IEntity` with a collider. "Registered" and "works" are different claims, and
    `PersistentSpawnDirector` discards a bad host with nothing but a log line, so a broken builder
    would otherwise stay invisible until a player spent a kit on nothing.
  - Build clean + **816** tests + `--validate` exit 0 with all five new rules negative-tested + 2
    clean `--play` runs, no errors and no unexpected warnings.
  - **Reachable from `--play`:** the yard is beside the deed post and the chest in the town hub. The
    at-keyboard pass is crafting a kit, the four refusals, placing, using, removing, and a save
    round-trip.
- [x] **37D — Trophy/display slots + one playable property authored** `[F/C]` ✅
  - **Done when:** trophy slots work and one property type is fully playable; the
    rest are content.
  - **A display stand is a one-slot container, and that is the whole design.** The inventory *is*
    the display, so it persists as `inventory:<PersistentId>` with **no new `ISaveable`** — 37B's
    trick, for the fourth sub-phase running and a fourth distinct reason. `Interact` publishes the
    existing `StorageOpenedEvent`, so the existing `StoragePanel` renders it with **no new panel**.
    What 37D actually adds is a rarity floor on that event, honoured on the **Store** direction
    only: Take is never gated, because a stand that could trap an item is worse than one holding
    something dull.
  - **A trophy is Epic-or-better, by rarity rather than a marker type.** Zero authoring: the Iron
    Heart (Legendary) and every future boss reward qualify on the day they land, and a rolled Epic
    the player actually earned can go on the wall. `TrophyDisplay` is pure and fourth in the
    `PropertyClaim`/`PropertyStorage`/`PlacementCheck` line.
  - **Placed stands need no authored `PropertyId`** — they read their holding out of their own
    `place.<propertyId>#<n>` id, which is the use `PlacementIds` was written anticipating.
  - **One new `--validate` rule, both branches negative-tested:** a built template carrying a
    `TrophyStandComponent` must also carry an `InventoryComponent` of capacity **1**. A stand with
    no slot accepts nothing and persists nothing, and `PersistentSpawnDirector` discards a bad host
    with only a log line.
  - **The cottage stopped being `BuildingSW`.** It has its own building, a walled roofless room
    holding the stash and two authored stands, a deed post at its own door (it was in the middle of
    the square, selling a house nobody could see) and a re-centred yard. Roofless is deliberate: an
    enterable interior needs a scene-transition convention this repo does not have, and inventing
    one here would pre-empt Phase 44.
  - ⚠️ **The bug this phase shipped and then fixed: the cottage floated.** Every building node sits
    at half its own height so the box collider spans ground to roof, and the `.glb` origin is at its
    base — so the `Model` child carries a **counter-offset** (`-3.75` on the square's houses). The
    first cut of the Cottage node had none, and the house hung 3.75 m over its own garden. Copy the
    whole idiom, not just the node.
  - Build clean + **834** tests + `--validate` exit 0 with the new rule negative-tested both ways +
    clean `--play`. **Phase 37 complete.**
  - **Reachable from `--play`:** the cottage is south-west of the square. The at-keyboard pass is
    claiming it, putting the Iron Heart on a stand, being refused a health potion, crafting a
    Display Stand kit and placing one in the yard, then **save/reload to confirm both the authored
    and the placed stand kept their trophies** — the one thing no headless run can prove.

---

## Art — the Quaternius standardisation `[P]` (2026-08-05, out of band)

> Maintainer direction mid-37D: **the art set standardises on Quaternius CC0 packs.** Recorded as
> standing policy in `docs/ASSET_POLICY.md` §0; full provenance in `assets/CREDITS.md`.

- [x] **Library + town** — 401 CC0 models vendored at `assets/library/` behind a `.gdignore`;
  the cottage got its own low, wide, single-storey building, and the square four distinct
  buildings (house, inn, smithy, farmhouse) instead of two used twice.
- [x] **Props** — 18 re-sourced, each keeping its **original filename and bounding box**, so no
  scene transform, collider or `PlaceableTemplates` id needed editing. Cleared the project's last
  attribution obligation: `prp_tome_stand` was its only CC-BY model.
- [x] **Creatures** — 29 archetypes that greyboxed as tinted capsules got rigged, animated bodies
  at their own `CapsuleHeight`. Phase 35's dragons finally have a body. Characters needed no work:
  `men/Adventurer` is byte-identical to `chr_player_base`.
- **Two traps worth re-reading before the next asset pass**, both in `ASSET_POLICY.md` §0: judge a
  model **from behind** (an open-backed "cottage" nearly shipped, for the second time), and never
  measure a rig's bounding box without excluding the importer's `glTF_not_exported` `Icosphere` —
  it reads 1 m too tall and produced wrong scales for all 29 creatures **twice**, the second time
  because the same polluted bbox was used to "verify" the fix.
- **Still uncovered by any vendored bundle:** an anvil/forge (`prp_station_forge`) and an
  ice/glacier prop (`prp_glacier`). Both left as-is rather than forced.

---
