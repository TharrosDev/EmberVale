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

## 37E — The Ashfall Homestead: a real, enterable home in its own cell `[C]` ✅

*Out of band, maintainer direction (2026-08-10): "the current player house looks terrible and janky —
rebuild it completely, in its own cell, make it a real house a player would actually like to spend
time in, and want to spend gold to purchase."*

- **What was there.** `bld_cottage.glb` — a **sealed 4.84 m mesh you could not enter** — beside a
  **roofless pen of grey `BoxMesh` walls** holding the stash, a `BoxMesh` deed post and two trophy
  stands that were broken ruin columns. Six hundred gold bought a box you stood next to.
- ⚠️ **37D's STATED BLOCKER WAS NO LONGER TRUE, AND CHECKING THAT WAS THE WHOLE UNLOCK.** Its comment
  read: *"Roofless by design: an enterable interior needs a scene transition convention this repo
  does not have yet."* It does not need one. The megakit has walls with door and window openings,
  floors and roofs; the only thing missing was a composition with **per-wall colliders instead of one
  solid box**. There is no transition — you walk in through the door. **A parked reason ages into a
  fact if nobody re-reads it** — the same failure 38G's parking notice had, one phase earlier.
- **Landed:** `compose_building.py --hollow` (per-wall colliders, an open doorway, a floor), the
  house `bld_ashfall_house.tscn` (3x4 modules, 6x8 m interior), a new cell
  `ember_crown.ashfall_homestead` at `(52, 0, 46)`, 18 models adopted from the two kits, a free
  ownership-gated bed, the missing **"A new region cell"** recipe, and the demolition of the old one.
- ⚠️ **THE NAVMESH ARGUMENT INVERTS FOR A BUILDING YOU ENTER.** The composer's one-box collider is
  documented as deliberate — *"fifty little static bodies would carve fifty little holes in the
  navmesh"* — and that is right for a background town house and wrong for a home. Hollow mode carves
  exactly those holes on purpose: the walls **should** obstruct and the interior **should** be
  walkable. The rule was not wrong, its scope was.
- ⚠️ **A HOLLOW HOUSE'S DOOR MUST HANG OPEN, AND THE HINGE GOES ON THE EDGE OF THE OPENING.** The leaf
  has no collider and hollow mode leaves none across the door module, so a shut door is one the player
  walks *through* — clipping, not an entrance. First fix swung it from the module centre, where the
  leaf stood **edge-on in the middle of its own doorway** and rendered as a post across the entrance.
  The leaf's origin is its hinge (local x -0.05..1.07), so the origin belongs at the opening's edge.
- ⚠️ **A MODEL THAT READS AT 20 m CAN DOMINATE AT 4 m, AND THIS COST THREE RENDERS.** The deed post
  was `prp_waystone` — 3 m of grey stone that stood in the approach shot as a **monolith blocking the
  front door**. It is a lectern now. The lamp post was moved twice for the same reason before landing
  at the gate, which is the only place its silhouette reads as a lamp. ⚠️ **Neither is visible from a
  `.tscn`, and both looked correct in the file.** Invariant 12, fired twice in one session.
- ⚠️ **A ROOF KILLS THE SUN AND NOTHING WARNS YOU.** The chandelier and the wall lantern are meshes;
  neither emits light. Two `OmniLight3D`s are what make the interior a room rather than a cave, and
  the only way to know they were needed — or enough — was to render from inside.
- **The bed is a free `ServiceKind.Inn` gated on ownership**, and the gate is the one code change.
  `ServiceComponent` had **no ownership check** while `PropertyStorageComponent` and
  `TrophyStandComponent` had both asked `HousingService.Owns` since 37B — so without it any passer-by
  sleeps in the player's bed. ⚠️ **The gate lives on the component, not in `TryUse`**, which is static
  because `DialogueEffect.OpenService` reaches it with no component at all: a service fired from a
  conversation has no holding to belong to, so it is ungated **by nature rather than by omission**.
- ⚠️ **THE `PersistentId`s ARE UNCHANGED AND THAT IS NOT COSMETIC.** An inventory saves as
  `inventory:<PersistentId>`, so `ember_crown.cottage_chest` and both trophy ids moved cells byte for
  byte. Renaming one would have silently emptied a player's stash and read as item loss, not as a
  content change. **What changed is the models on them**, which the id does not care about.
- **The economy note is in `DESIGN.md` §6**: the inn is 10 gold a night forever, the house was 600
  gold once, so it breaks even at sixty nights and **the purchase is the sink**. The inn keeps its job
  for every player who has not bought and in every settlement that is not this one.
- ⚠️ **THERE WAS NO "A NEW REGION CELL" RECIPE AND THE SETTLEMENT RECIPE POINTED AT ONE** ("the cell
  recipe above"). CLAUDE.md §8's rule applied exactly: it is written now — the abutment arithmetic,
  the `Nav` parenting rule, the trade-tag gate and `SafeRadius`.
- Build clean, **0 warnings** + **1283** tests unchanged + `--validate` exit 0 + `tools/negative_tests.py`
  **42/42** + `--economy` **byte-identical** + `--state` **15 cells, 15 services** + a `--play` boot
  with **10 cells resident, 32 objects restored, 0 project errors** and the homestead in the load log.
- ⚠️ **What was NOT verified.** The bed's prompt, the not-yours refusal and the deed purchase need a
  human at the interact key — **reviewed against the Godot 4.7 C# API, not observed**. The
  third-person camera indoors was judged from a 1.7 m eye-level render rather than from playing, so
  "the room is big enough for the camera" is an inference from a 6x8 m span, not a measurement.
- Two things worth carrying:
  1. ⚠️ **A DELIBERATE LIMITATION IS STILL A LIMITATION, AND IT DECAYS.** 37D's "needs a scene
     transition convention" was true of the approach 37D took and became a repo-wide fact nobody
     re-tested for two phases. **When a comment explains why something cannot be done, check whether
     it is explaining the world or explaining last year's decision.**
  2. **Render the approach, not just the object.** Every defect this sub-phase found was a *framing*
     defect — a correct model in a position that ruined the shot. The isolated render of the house
     was clean three times while the walk-up was wrong.

---

## 37F — Reported runtime errors, and the arena rebuilt `[F/C]` ✅

*Out of band, maintainer direction (2026-08-10): a list of Godot errors from a play session, plus
"rebuild the Boss arena as it too looks super boring bad".*

- ⚠️ **TWO OF THE FOUR REPORTS WERE ONE BUG, AND THE STACK TRACES HID IT.** A companion tripped
  `MoveAndSlide`'s *"Vector3 cannot be normalized"*; a **dead** enemy tripped `Mathf.MoveToward`,
  which throws inside `Math.Sign` on NaN. Different systems, different frames, different messages —
  and the same non-finite value, because **a `CharacterBody3D` keeps its velocity between frames**,
  so one bad write poisons that body for the rest of the run and the crash surfaces wherever the
  body next happens to move. **Neither trace points anywhere near the source.**
- ⚠️ **THE ENEMY'S PATH IN WAS `Zero * NaN`, WHICH READING THE CALL SITE ARGUES IS IMPOSSIBLE.**
  `Stand` passes `Vector3.Zero`, so "the direction was bad" is ruled out by inspection — but the
  motor computes `horizontal * speed`, and a poisoned `MoveSpeed` stat makes a NaN target out of a
  zero input. **The arithmetic knew something the call site denied.** `MotionSafetyTests` pins that
  exact line so the next reader does not re-derive it.
- **Landed:** `MotionSafety` (pure, 6 tests) and three guards in `LocomotionComponent.Move` — the one
  function both AI movers route through, and the player does not use it at all, so the fix has
  exactly two callers and no blast radius.
- ⚠️ **IT LOGS ONCE PER BODY, AND THAT IS THE HALF THAT MATTERS.** A silent clamp fixes the crash and
  destroys the evidence. The source is **still unproven** — `Stat.Recalculate` has no division, so a
  NaN modifier value is the likeliest door and I could not reach it from a reading. The log line
  names the owner, which is what keeps this findable instead of a bug that merely stopped shouting.
- **The Iron King's missing texture was a stale import cache**, not a missing asset: the `.glb`
  carries its image in a bufferView, but `ce150cc` replaced the model and deleted the extracted
  `boss_iron_king_Zombie_Atlas.png` while `.godot/imported/…scn` — **stamped ten minutes before that
  commit** — still pointed at it. Set to embed-uncompressed and re-imported; the cache is now `.scn`
  + `.md5` with no `.ctex` and no loose PNG, so there is nothing left to go missing.
  ⚠️ **The class was swept, not the instance**: all 32 creature models now load with geometry.
- ⚠️ **THE `'consignment'` WARNING WAS NOT A DEFECT, AND CHECKING BEAT ASSUMING IN BOTH DIRECTIONS.**
  The plan said verify before fixing. Reading `slot1/save.json` directly: it contains `consignment`,
  `shopstock`, `contraband_impound`, `shocks`, `haggles`, `wagers`, `contracts` and `housing` — every
  economy system. The warning came from an **older save**. ⚠️ My own first pass then flagged
  `shop_stock` and `impound` as missing, which was **my guess at the key names**, not the data:
  they are `shopstock` and `contraband_impound`. **A missing key is a claim about two things, and I
  had only checked one.** A `--play` boot now logs no "no usable entry" line at all.
- **The arena was a 36 m flat-grey `BoxMesh` floor, three `BoxMesh` walls (there was no west one) and
  one CYLINDER brazier** — and its single `prp_arena_wall` per side was **scaled 4.3x** to span 36 m,
  stretching the stonework with it. It is now a ring of five unstretched wall instances a side, a
  broken outer tier of ruin walls, ten pillars, rim braziers with real lights, banners on the inner
  face, and dead pines beyond. The west stays open — it is the entrance — with two gate pillars.
- ⚠️ **THE FIGHTING CIRCLE STAYED BARE, AND THAT WAS THE CONSTRAINT THE FILE ITSELF SHOUTED.** The
  old scene's ground cover carries a capitalised note: *"a combat floor has to stay legible: scenery
  a player reads as cover, or an enemy appears to path around, is worse than a bare floor."* Every
  piece added here is on the rim or beyond it. **The temptation was to fill the middle, because the
  middle is what the screenshot shows.**
- ⚠️ **Four node groups had to survive byte-identical** — `Brazier/Summon`, `AddSpawns` (found by
  *group*, so a rename is safe but a **move** drops an add on the boss), `EmberVents/VentA–D`, and
  the two `ArenaHookComponent`s whose `Reveals` are **NodePaths to those vents**. Renaming a vent
  breaks the boss's phase telegraph silently.
- **Two render-only defects, both mine, both invisible in the `.tscn`:** the floor's `uv1_scale` of 9
  over a 36 m plane made **4 m flagstones**, and the banners hung *inside* the ring wall at ground
  level so only their top edge showed. 37E's carry — *render the approach, not the object* — applied
  again and caught both.
- Build clean, **0 warnings** + **1289** tests (6 new) + `--validate` exit 0 + `--economy`
  **byte-identical** + `--state` unchanged at 15 cells / 15 services + a `--play` boot with **10
  cells resident, 32 objects restored, 0 project errors and no save warnings at all**.
- ⚠️ **What was NOT verified, and one item is uncomfortable.** The NaN guard is proved by unit tests
  and by reading, **not** by watching the reported errors fail to recur — they need a long combat
  session with a companion, which a `--play` boot does not produce. **The guard is correct; the claim
  "the bug is gone" is not yet earned.** The boss was rendered in isolation, not summoned in the
  arena. The arena's collision was not walked.
- ⚠️ **AND A FINDING BIGGER THAN ANYTHING REPORTED: THE IRON KING IS A MAN IN AN ORANGE BOMBER JACKET.**
  With the texture fixed he renders correctly — as a modern-dress civilian in a bomber jacket, teal
  shorts and trainers, holding a thin sword. The game's flagship boss. This is the **fourth** time
  this exact defect has shipped (`npc_townsman` hi-vis, `npc_merchant_f` t-shirt-and-trainers, four
  of six 38N2 candidates) and the first time it has been on a boss.
  **Deliberately not fixed here, with the check written down rather than a verdict:**
  `assets/library/men/king.glb` is the obvious body and is the *same 62-bone Quaternius rig as
  `chr_player_base`* — but its clip names differ (`Sword_Slash`/`HitRecieve` against the current
  `Slash`/`Stab`/`HitReact`), and `AnimationClips.Resolve` failing is **silent**: the actor T-poses or
  winds up and never strikes. **The check to run: resolve every slot against `king.glb`'s clip list
  and confirm each one binds, then retarget via the `.import` bonemap and render.** Doing that badly
  inside a bug-fix sub-phase is how a boss fight breaks quietly.
- Two things worth carrying:
  1. ⚠️ **A STATEFUL COMPONENT TURNS ONE BAD FRAME INTO A PERMANENT FAULT.** Velocity, cooldowns,
     accumulators — anything read back out of the engine next frame. **Guard where the value enters
     the stateful thing, not where it explodes**, because those are never the same place.
  2. **Verify a warning before silencing it, and verify your verification.** The `'consignment'`
     report was not a bug; my first check of it produced two *new* false alarms from guessed key
     names. Reading the save was three commands and settled all of it.

---

## 37G — The Iron King gets a body `[C]` ✅

*37F found it and deliberately left it: with his texture restored, the game's flagship boss rendered
as a man in an orange bomber jacket, teal shorts and trainers. This is the pass that fixes it.*

- **Landed:** `assets/library/men/king.glb` copied over `boss_iron_king.glb` — a bearded king in a
  mail coif and gold crown, steel plate at shoulder, arm and knee, dark blue tabard — at
  `nodes/root_scale = 1.368` so he stands **2.605 m** against the archetype's `CapsuleHeight = 2.6`.
  Plus `IronKingClipsTests` (8 cases). **No code change and no data change**: `ModelPath` already
  pointed here, so the swap is a file and an import setting.
- ⚠️ **THE WHOLE RISK WAS SILENT AND THE TEST IS THE ONLY THING THAT RETIRED IT.** The old body
  shipped `Slash`, `Stab`, `HitReact`; this one ships `Sword_Slash` and `HitRecieve` — **no overlap
  on two of the three**. `AnimationClips.Resolve` returning empty is a *legal* answer (a creature
  with no block clip simply never blocks), so a boss that has quietly lost its attack animation winds
  up and never strikes and **nothing logs a word**. A render of a standing model cannot see it.
- ⚠️ **AND THE PACK SHIPS GUN CLIPS** — `Idle_Gun`, `Idle_Gun_Pointing`, `Gun_Shoot`, `Run_Shoot`.
  Only `AnimationClips`' exact-match pass keeps the plain `Idle`; a first-match-wins scan would be
  correct here *purely because the list happens to be alphabetical*. The failure that guards against
  is the one its own comment names: **"a fantasy boss idling in a rifle stance, which nothing would
  flag as an error"**.
- 🎯 **THE RESOLVER HAD ALREADY BEEN HARDENED FOR THIS EXACT SWAP, BY SOMEONE WHO NEVER MADE IT.**
  `AnimationClips.Match` carries the comment *"the Iron King's replacement happens to list them
  alphabetically…"*, and `hit`'s alias list contains the **misspelled** `recieve` — which is exactly
  how this pack spells `HitRecieve`. The groundwork was laid and then not used. **Read the code that
  will consume your change before assuming it has not thought about you.**
- **File copy, not a round-trip** (`ASSET_POLICY.md`): the rig already fits, and a Blender round-trip
  destroys bone-parented children — which this model has, since the sword hangs off a hand bone.
- **Verified in the right order.** Rendered **front and back before adopting** (the standing rule:
  four of six 38N2 candidates were unusable and none of it was visible from a filename) — the back is
  fully modelled armour, no open geometry. Then measured: 2.605 m tall, **feet at −0.002 m**, which
  is the sunk-body defect `ce150cc` had to fix twice. Then rendered **in the arena at eye level**,
  not in isolation — 37F's carry.
- ⚠️ **Three candidates were rendered and rejected**, which is the gate working rather than
  ceremony: `monsters/demon` (1.68 m, too small and reads as a minion), `monsters/goleling_evolved`
  (2.57 m and the right size, but a rock golem is not a fallen Flamebearer) and `monsters/orc_enemy`
  (1.46 m). The lore is specific — *"The First Flamebearer… power consumed him, now rules through
  fear"* — and a crowned man in armour is the only one of the four that reads as a king at all.
- Build clean, **0 warnings** + **1297** tests (8 new) + `--validate` exit 0 + `--economy`
  **byte-identical** + `--state` unchanged + a `--play` boot with 10 cells, 32 objects, **0 project
  errors**.
- ⚠️ **What was NOT verified.** He was rendered standing, not fought: **no clip was actually played**,
  so the bindings are proved by `AnimationClips.Resolve` under test rather than by watching him
  swing. His rig is a plain 62-bone `Skeleton3D`, **not** `GeneralSkeleton` — so he is un-retargeted
  and cannot reach the shared library's `block`/`cast`/`channel`. That matches the body he replaced
  exactly (43-bone `Skeleton3D`, no block or cast either), so it is not a regression — but it does
  mean **he still has no guard animation**, and if the fight ever wants one that is a retarget pass.
- One thing worth carrying: ⚠️ **"The model is missing its texture" and "the model is wrong" are two
  bugs, and fixing the first reveals the second.** 37F fixed a stale import cache and declared the
  boss repaired; the boss was repaired, and was also a civilian in a bomber jacket. **A render is the
  only step that distinguishes "loads" from "correct", and it belongs after every asset fix, not
  only after an asset *addition*.**

---
