# Embervale — Content Recipes

> **What this is.** Step-by-step recipes for adding content without breaking anything: the
> fields to author, the order to do them in, and the trap each one has already sprung on
> somebody. Lifted out of `CLAUDE.md` §8 verbatim, because it was **66% of a file that loads
> into every session** and no session needs more than one of these.
>
> **How to use it.** Find your recipe in the index, read that one, ignore the rest. The
> ⚠️ markers are not decoration — every one of them is a defect that shipped before it was
> written down here.
>
> **Companions.** `CLAUDE.md` §7 has the repo-wide gotchas (read those every time);
> [`ARCHITECTURE.md`](ARCHITECTURE.md) has the systems reference; [`IDS.md`](IDS.md) has the
> id scheme every recipe here authors against.

---

## Index

- [A new component](#a-new-component)
- [A new actor / enemy type](#a-new-actor--enemy-type)
- [A new boss fight (Phases 36A–36D)](#a-new-boss-fight-phases-36a36d)
- [A new claimable property (Phase 37A)](#a-new-claimable-property-phase-37a)
- [Giving a property a stash (Phase 37B)](#giving-a-property-a-stash-phase-37b)
- [A new placeable prop or a buildable yard (Phase 37C)](#a-new-placeable-prop-or-a-buildable-yard-phase-37c)
- [Giving a property a trophy stand (Phase 37D)](#giving-a-property-a-trophy-stand-phase-37d)
- [A new shop / merchant (Phase 38A–38J)](#a-new-shop--merchant-phase-38a38j)
- [A new service — trainer / bank / inn / stable (Phase 38D)](#a-new-service--trainer--bank--inn--stable-phase-38d)
- [Generators — do not hand-write boilerplate](#generators--do-not-hand-write-boilerplate-agent-ergonomics-pass)
- [A new region cell (Phases 25, 38K/N1/N2/O, 37E)](#a-new-region-cell-phases-25-38kn1n2o-37e)
- [A new map location (Phase 39.5A)](#a-new-map-location-phase-395a)
- [A production settlement (Phase 38N1)](#a-production-settlement-phase-38n1)
- [A tolled crossing — toll, permit, bribe (Phase 38M)](#a-tolled-crossing--toll-permit-bribe-phase-38m)
- [A fence and contraband (Phase 38O)](#a-fence-and-contraband-phase-38o)
- [A new gold sink (Phase 38C)](#a-new-gold-sink-phase-38c)
- [A big/boss creature with body zones (Phase 35A)](#a-bigboss-creature-with-body-zones-phase-35a)
- [Making a creature fly (Phase 35B)](#making-a-creature-fly-phase-35b)
- [A breath weapon (Phase 35C)](#a-breath-weapon-phase-35c)
- [Placing a world boss in a lair (Phase 35D)](#placing-a-world-boss-in-a-lair-phase-35d)
- [A creature that talks (Phase 35F)](#a-creature-that-talks-phase-35f)
- [A new weapon](#a-new-weapon)
- [A new item](#a-new-item)
- [A new piece of equipment](#a-new-piece-of-equipment)
- [A new loot affix](#a-new-loot-affix)
- [A new loot table / dropper](#a-new-loot-table--dropper)
- [A new perk](#a-new-perk)
- [A new XP-bearing enemy (or tuning the curve)](#a-new-xp-bearing-enemy-or-tuning-the-curve)
- [A new quest](#a-new-quest)
- [A new conversation](#a-new-conversation)
- [A new NPC routine](#a-new-npc-routine)
- [A new weather state](#a-new-weather-state)
- [A new encounter](#a-new-encounter)
- [A new world event](#a-new-world-event)
- [A new crafting recipe](#a-new-crafting-recipe)
- [A new spell](#a-new-spell)
- [A new status effect](#a-new-status-effect)
- [A new faction](#a-new-faction)
- [A new stat](#a-new-stat)
- [A new event](#a-new-event)
- [A new persistent system](#a-new-persistent-system)
- [A new input action](#a-new-input-action)
- [A new dev-console command](#a-new-dev-console-command)
- [A new UI panel / HUD widget](#a-new-ui-panel--hud-widget)

---


## A new component

1. Create `src/<Area>/XxxComponent.cs` extending `EntityComponent`
   (`[GlobalClass]` if editor-creatable).
2. Resolve siblings/stats in `OnInitialize` via `Entity!.GetComponent<T>()`.
   Subscribe to events here; unsubscribe in `OnTeardown`.
3. Add it as a child of the actor in the relevant factory (or scene).

## A new actor / enemy type

1. (Optional) marker subclass of `CharacterEntity` for type-level identity.
2. Add an `AttributeSet` `.tres` for its stats and (if it fights) a
   `WeaponResource` `.tres`.
3. Write a factory (mirror `EnemyFactory`) wiring: collision, mesh, `StatsComponent`,
   `CombatComponent` (set `Team`), `LocomotionComponent`, `Hurtbox`,
   `Hitbox` + `MeleeWeaponComponent`, and a behaviour component.

   **Usually you should not write a factory at all** — author a
   `data/enemies/Xxx.tres` (`script_class="EnemyArchetypeResource"`) instead and
   `EnemyArchetypeFactory` builds it, `EnemyArchetypeDatabase` registers it, and
   `spawn <id>` works with no code. A bespoke factory earns its place only by doing
   something structurally different (goblin, Ashen Acolyte). The Iron King had one until Phase
   36B and lost it: once his phases moved into `data/bosses/`, his factory was a worse copy of the
   shared one — it silently skipped the hit reaction, the weapon trail and the quest enemy group.

## A new boss fight (Phases 36A–36D)

1. Author `data/bosses/Xxx.tres` (`script_class="BossResource"`): unique `Id` (`boss.*`) and
   `Phases` — an array of `BossPhaseResource` sub-resources, **ordered high health to low**, the
   first at `HealthFraction = 1.0`. Each phase carries its `AttackSpeedBonus`/`MoveSpeedBonus`
   (fractions, applied as `PercentMult` under a `boss.phase{n}` source), optional `GrantSpellIds`,
   an optional `AiProfileId` swap, and its `TelegraphColor`/`TelegraphEnergy` wind-up flare.
   Optionally add the enrage fuse: `EnrageSeconds` (`0` = none), `EnrageSpellIds`, the two enrage
   bonuses, and `EnrageForcesFinalPhase`. `WindupPoiseMultiplier` (36C) decides how punishable the
   phase's wind-ups are — above `1` makes the telegraph a window worth attacking into, below `1`
   hardens it. It must stay positive; `0` is a phase that can never be interrupted, which in play
   looks exactly like the interrupt being broken, so `--validate` rejects it.
   `AddWaves` (36D) is an array of `BossAddWaveResource` sub-resources — `TemplateId` (any registered
   enemy), `Count`, `RepeatSeconds` (`0` = once on entering the phase), `MaxAlive` (`0` = uncapped)
   and `HealthMultiplier`. ⚠️ **A repeating wave must set `MaxAlive`**; the validator rejects one
   without it, because an uncapped repeat ends the fight by burying the player rather than beating
   them. Adds die with the boss through the ordinary damage path, so their loot and XP still land.
   The `Encounter`/`Reward` groups (36E) carry the intro lock, the defeat slow-mo, the guaranteed
   `RewardItemId`, the `DefeatFlagId` and the `DefeatDialogueId` that offers the corruption choice.
   ⚠️ **A reward or a defeat conversation requires a `DefeatFlagId`** — without one nothing records
   that it already happened, so it pays out on every death. That is not hypothetical: it is the
   shape of the bug 36E fixed, and `--validate` now rejects it. Leave `DefeatFlagId` empty on a lair
   boss; `LairSpawnComponent` already records those, and a second writer of the same fact drifts.
2. Point an archetype at it: set `IsBoss = true` **and** `BossId = "boss.xxx"` on
   `data/enemies/Xxx.tres`. `EnemyArchetypeFactory` attaches the `BossController`; there is no code
   to write. An `IsBoss` archetype with no `BossId` still gets a controller and falls back to the
   default three-stage escalation, so a boss is never left with no structure at all.
3. **An arena binds itself to the fight in its own `.tscn`, not in code** (36D). Tag `Marker3D`s
   `groups=["boss_add_spawn"]` and waves arrive there — found by group, so renaming or re-parenting a
   marker cannot silently unbind it, and scoped to markers under the boss's own parent, so two loaded
   arenas cannot lend each other spawns. With no markers the adds fall back to a ring around the
   boss, which is what a lair gets. Add an `ArenaHookComponent` (`ActivateAtPhase` + `Reveals`
   node paths) to have the arena itself reveal things as the fight escalates; it resets on the boss's
   death, because `BossSummonComponent` deliberately re-arms until the defeat is persisted.
   See `scenes/regions/ember_crown/arena.tscn` for both.
4. ⚠️ **The enrage clock starts on the first damage traded with the boss**, not on
   `BossEncounterStartedEvent` — only `BossSummonComponent` publishes that (the Iron King's path),
   so keying off it would leave every lair boss with a fuse that never lit.
5. ⚠️ **Mark any spell a phase grants `PlayerLearnable = false`.** The grant goes through the same
   path a dialogue reward uses, which ignores that flag — but the player's spellbook lists every
   spell in the database, so a monster ability would otherwise show up as purchasable.
6. `--validate` checks the domain **in both directions**: phases must descend from `1.0`, granted
   spells and profile ids must resolve, an archetype's `BossId` must exist, and a `BossId` may only
   sit on an `IsBoss` archetype (otherwise it is a silent no-op).

## A new claimable property (Phase 37A)

1. Author `data/properties/Xxx.tres` (`script_class="PropertyResource"`): unique `Id`
   (`property.*`), a `NameKey` in `strings.csv`, its `RegionId`, and a `TravelNodeId` — claiming
   registers the holding as a fast-travel destination, which is what makes owning it worth anything.
2. Give it a way to be had: a `PriceGold`, a `RequiredQuestId`, or both. ⚠️ **Neither is rejected by
   `--validate`** — a property that is neither sold nor earned is claimed by the first player who
   walks into its post. A missing `TravelNodeId` is rejected too: gold spent on somewhere you cannot
   return to.
3. Place the deed: an `Entity` in a region cell with a collider (the interact raycast needs one) and
   a `PropertyDeedComponent { PropertyId = "property.xxx" }`. See `CottageDeed` in
   `scenes/regions/ember_crown/town_hub.tscn`.
4. ⚠️ **Every refusal must say which refusal it is.** The prompt reports owned / quest-locked /
   too-expensive separately, in that order — the quest gate before the price, so a player is never
   sent to earn gold for something a quest is holding shut. `PropertyClaim.Resolve` owns that order
   and both the prompt and the interaction read it, so they cannot drift apart.

## Giving a property a stash (Phase 37B)

1. Add an `Entity` to the region cell with a collider, an `InventoryComponent`, and a
   `PropertyStorageComponent { PropertyId = "property.xxx" }`. See `CottageChest` in
   `scenes/regions/ember_crown/town_hub.tscn`. **That inventory *is* the storage** — there is no
   storage service and no new save code, because an entity with a stable `PersistentId` already
   round-trips its inventory through `SaveManager` (`inventory:<PersistentId>`) and survives cell
   churn through `CellPersistenceDirector`.
2. ⚠️ **Give it a `PersistentId`, and never change it.** It is the save key. Without one the
   inventory does not register as a saveable at all (`SaveKeyPolicy.ShouldPersist`) and the stash
   silently empties on every reload — the failure looks like an item-loss bug, not a missing field.
   Two chests sharing an id is worse: they overwrite each other, last write wins.
3. ⚠️ **Author `Capacity` on the `InventoryComponent` node, not on the `PropertyResource`.**
   `InventoryComponent.Load` restores through `AddInstance`, which clamps to `Capacity`, so a
   capacity applied by another component *after* the save manager's mid-load restore drops the
   overflow without a word. Each property has its own chest, so the node value is already
   per-property.
4. Interacting publishes a `StorageOpenedEvent` carrying the container's inventory; the single
   `StoragePanel` (built in `GameBootstrap` beside the `CraftingPanel`) shows both sides. No panel
   wiring per container.
5. ⚠️ **Moving a stack removes by reference for rolled items, by template id only for stackables.**
   `RemoveItem(id, qty)` matches across *every* stack of that template, so two distinct affixed
   instances of one template would see the first removal satisfy both and one would evaporate — that
   bug is live in `ContainerLootComponent.Interact` today. `StoragePanel.Transfer` branches on
   `ItemInstance.IsStackable` and uses `RemoveOneInstance` otherwise. It also only removes what
   `AddInstance` reported as *landed*, so a full destination cannot eat the remainder.
6. There is no `--validate` rule here: capacity and the `PropertyId` both live in a `.tscn`, which
   `ContentValidator` does not scan. A mis-typed `PropertyId` resolves to nothing and the chest
   shows **no prompt at all** — if a chest is silently unusable in game, check that field first.

## A new placeable prop or a buildable yard (Phase 37C)

1. **A yard:** set `PlacementCenter` and `PlacementRadius` on the `PropertyResource`. ⚠️ The centre is
   **world** space. A cell scene is authored at its own origin and moved to the cell's `Center` by
   the streamer, so a point read straight out of a `.tscn` lands a cell's width from the house — add
   the cell's `Center` first. `--validate` catches the gross version by testing the centre against
   the region's `Bounds`. `PlacementRadius = 0` is a holding that may not be built in, and it refuses
   everywhere rather than succeeding everywhere.
2. **A prop:** add an id + a `Build` case to `src/Housing/PlaceableTemplates.cs`. That one file is
   both the id set the validator reads and the builders `PersistentActorRegistry` gets, so the two
   cannot disagree. Stations go through `CraftingStationFactory.Create` (pass a `modelPath`);
   decorations are an `Entity` + model + collider and deliberately have **no** interaction, because
   the only verb a decoration has is Remove and placement mode owns that.
3. **A kit:** author `data/items/Xxx.tres` (`script_class="PlaceableItemResource"`) with `TemplateId`
   pointing at that id, plus a recipe — and ⚠️ seed the recipe in `GameIds.Recipes.Starting` or
   `--validate` fails the build (see the crafting recipe entry above).
4. ⚠️ **Never hand out placement ids from a counter.** `PersistentSpawnDirector._autoId` is not
   persisted, so a counter reissues `#1` after a load and `Spawn` answers a known id by returning the
   *existing* actor — the new prop silently never appears, and only in a session that loaded a save.
   `PlacementIds.Next` scans the live ids instead. The id also encodes the property
   (`place.<propertyId>#<n>`), which is how a holding's contents are found with no second save record.
5. `--validate` **builds every template** and requires an `IEntity` with a collider. "Registered" and
   "works" are different claims, and `PersistentSpawnDirector` discards a bad host with only a log
   line — the player would just lose the kit.
6. A prop is removed in placement mode, not by interacting with it: a station's own `Interact`
   already opens its crafting window. Removal refuses on a full pack rather than destroying the prop.

## Giving a property a trophy stand (Phase 37D)

1. **Authored:** add an `Entity` to the cell with a collider, an `InventoryComponent` of
   **`Capacity = 1`**, and a `TrophyStandComponent { PropertyId = "property.xxx" }`. See
   `CottageStandW`/`CottageStandE` in `town_hub.tscn`. **Placeable:** nothing to do — the
   `prop.display.stand` template and its Display Stand kit already exist.
2. ⚠️ **Give it a `PersistentId` and never change it**, exactly as for 37B's chest. That one-slot
   inventory *is* the display, and it persists as `inventory:<PersistentId>`; without an id it does
   not register as a saveable at all and the trophy vanishes on reload.
3. ⚠️ **Capacity must be 1.** `InventoryComponent.Load` clamps to `Capacity`, and a stand acting as
   a chest is not a trophy case. `--validate` enforces the 1 on the placeable template.
4. **Leave `PropertyId` empty on anything placed** — a placed stand reads its holding out of its own
   `place.<propertyId>#<n>` id (`PlacementIds.PropertyOf`), which is why one template serves every
   property.
5. **What it accepts is `TrophyDisplay.MinimumRarity` (Epic).** Change it there, not at a call site:
   the stand and `StoragePanel`'s Store button both read it, so they cannot disagree. Take is
   deliberately never gated — a stand that could trap an item is worse than one holding junk.
6. The window is the existing `StoragePanel`; a stand publishes the same `StorageOpenedEvent` with a
   `MinRarity`. There is no trophy UI to wire.

## A new shop / merchant (Phase 38A–38J)

> ⚠️ **MANDATORY LAST STEP: put it on the map.** A shop with no `MapLocationResource` **fails
> `--validate`** (`ValidateEverythingIsOnTheMap`). Add a row to `tools/gen_map_locations.py` and run
> it — see [a new map location](#a-new-map-location-phase-395a). Do it in this sub-phase, not the
> next one.

1. Author `data/shops/Xxx.tres` (`script_class="ShopResource"`): unique `Id` (`shop.*`), a `NameKey`
   in `strings.csv`, a `Stock` array of `ShopStockEntry` sub-resources (`ItemId` + `Quantity`, the same
   `.tres` sub-resource pattern `LootEntry` uses), `RestockDays`, an optional `LeveledTable`, an
   optional `FactionId` and `PurseGold`, and the spread — `BuyMarkup` (>= 1) and `SellFraction`.
   Auto-indexed by `ShopDatabase`.
   ⚠️ **`SellFraction` must stay below `BuyMarkup`.** Equal or inverted is an infinite gold loop: buy a
   stack, sell it straight back, repeat. `--validate` rejects it and `ShopPricing` clamps so a
   hand-edited `.tres` cannot do it either. Gold and `ItemType.Quest` items are rejected from stock — a
   quest object bought off a shelf, or coins bought with coins.
2. **Give the merchant a trade (38F).** `AcceptedTags` is what she will buy at all; `Specialties` is what
   she is expert in — she pays `ShopPricing.SpecialtySellBonus` over the odds for it and asks
   `SpecialtyBuyDiscount` less. Both are words from `src/Economy/TradeTags.cs`, matched against
   `ItemResource.TradeTags`. **Tags are not ids** — bare lowercase words, no domain prefix, no `IDS.md`
   row; the closed vocabulary in that file is the validator's whole authority, and adding one is a line
   there plus a `trade.tag.<tag>` locale key.
   ⚠️ **Both empties mean yes.** An empty `AcceptedTags` is a general store, and an untagged item is
   accepted everywhere — the same inverted fail-safe a missing `ReputationComponent` gets, so a
   half-authored world trades normally instead of refusing everything.
   ⚠️ **A settlement needs one merchant with an empty `AcceptedTags`**, or loot becomes unsellable by
   authoring accident. In the Ember Crown that is Aldreth.
   ⚠️ **`Specialties` must be a subset of a non-empty `AcceptedTags`** — a specialist who refuses her own
   trade is well-formed data that reads in game as the premium being broken. `--validate` rejects it, and
   also rejects a spread too thin to survive the premium (sell ≈ buy is frictionless churn).
   **Do not add an `ItemType` member for this.** Its ordinals are persisted in every save, and an item
   wears several tags anyway (a leather cap is `armor` *and* `leather`).
3. **Stock comes in three kinds and the numbers say which** (38B), with no mode enum:
   `Quantity = 0` is an unlimited row (a materials stall that never runs out); `Quantity > 0` is finite
   and refills on the shop's clock; a `LeveledTable` is a `LootTable` rolled at each restock, at a
   quality scaled by the player's level through `ShopStock.QualityForLevel`. That is the game's **first
   player-level-driven scaling** — it moves rarity and affixes, never *which* items a merchant deals in.
   ⚠️ **A finite row needs `RestockDays > 0`**, and so does a `LeveledTable`; `--validate` rejects
   either without one, because a shop with finite stock and no clock is emptied by the first player
   through the door and a pool rolled once is frozen for the run.
4. **Restock is evaluated when a shop is opened, not on a tick.** No `_Process`, no event
   subscription, no `DayChangedEvent` — a shop restocks because enough days had passed by the time the
   player walked up, and nothing can observe the difference. `WorldEventDirector` is the counter-example:
   it ticks real-seconds cooldowns every frame and is not `ISaveable`, so they vanish on reload.
   `WorldClock.Day` is the date; `time 26` rolls it forward one day (an in-game day is
   `DayLengthSeconds` — 180 s — of real waiting), and `shop restock <id>` skips the wait entirely.
5. ⚠️ **Runtime stock lives in `ShopStockService`, never on the resource.** A `ShopResource` is shared
   by every vendor naming it and is not `ISaveable`, so a remaining count written into it would leak
   between merchants *and* vanish on reload. **The rolled leveled wares persist too** — that is the
   whole reason they are in the save: if a reload rerolled the pool, the player would reload until a
   Legendary appeared.
6. **Standing moves prices (38C).** Author `FactionId` on the shop and
   `ShopPricing.PriceMultiplierFor` does the rest: a 15%–35% surcharge across the hostile half of the
   ramp down to 15% off at Allied, applied through `ShopPricing.MarkupFor` so the multiplication has one
   home. A faction the player is **hostile** to will not trade at all — that gate reuses
   `ReputationComponent.IsHostile`, so each faction's authored `HostileThreshold` decides it, and the
   prompt names the refusal. This is the first thing in the game to read a reputation *tier* and change
   a number; before it, standing was written, displayed, and consulted only as a boolean.
   ⚠️ **Author the faction on the `ShopResource`, not the vendor entity**, even though every town NPC
   already has a `FactionComponent`: `ShopOpenedEvent` carries no vendor, the `shop` dev command has no
   vendor at all, and the validator cannot scan a `.tscn`.
   ⚠️ **A shop must invert the AI's fail-safe.** `EnemyAIComponent` treats a missing
   `ReputationComponent` as *hostile*, which is right for a creature deciding whether to swing; for a
   merchant it would make every shop in a half-built world refuse. An unresolvable standing trades
   normally at the authored price.
   ⚠️ **`BuyMarkup` needs headroom.** `BuyPrice` clamps its markup to `>= 1`, which is what makes a
   discount incapable of inverting the spread — and also what silently swallows a markup too thin to
   discount, so a shop near `1.0` charges its best two tiers the same. `--validate` reports that,
   because the arithmetic cannot.
   **Only the buy side moves.** A merchant who likes you paying *more* for your loot is symmetric and
   tempting, but with both clamps in play a generous sell fraction converges on `sell == buy` —
   frictionless churn — and standing already modifies prices without it.
7. **A merchant fills up (38H).** `ShopStockService` counts units absorbed per template since the shop's
   last restock, and `ShopStock.SaturatedPayout` prices a stack **unit by unit** as that count climbs —
   never one price times a quantity, or dumping the whole stack at once becomes strictly optimal and the
   mechanic punishes only the tidy seller. There is nothing to author: it rides `RestockDays`, which now
   carries the shelves, the purse and the appetite.
   ⚠️ **A shop with `RestockDays = 0` does not saturate at all** — nothing would clear it, so the decay
   would be permanent. That is answered inside `SaturationMultiplier`, which is why there is no validator
   rule for it.
   ⚠️ **Each unit floors at 1 gold.** A one-coin item against any multiplier below 1 rounds to nothing,
   and a zero payout is refused as worthless — saturation would become a silent *refusal* for exactly the
   cheap high-volume goods it exists for.
8. **A merchant's purse is a sink from the other end (38C).** `PurseGold` (`0` = unlimited) is spent
   buying from the player and refills at restock, so a field of corpses cannot be fenced in one visit.
   ⚠️ **A positive purse needs `RestockDays > 0`** — same rule and same reason as a finite stock row.
   A payout the merchant cannot cover refuses the whole sale; paying part of it is item loss with a
   receipt. The purse arithmetic lives in `ShopStock` (`CanCover`/`AfterSpend`/`AfterRefund`) rather
   than in the service, because the service is a Godot `Node` the test project cannot construct.
9. Place it. **Two routes, and 38E decided which is which:**
   - **A merchant who talks** — author a `DialogueChoice` with `Effect = OpenShop` (9) and
     `EffectArg = "shop.xxx"` on the conversation she already has. This is the default. It exists because
     ⚠️ **an entity gets one interactable** — `EntityNode.GetComponent<T>` returns the *first* child match,
     so a `VendorComponent` behind a `DialogueComponent` never fires — and because two of the three town
     merchants carry live quest content that a menu must not displace. It also puts the shop id somewhere
     `ContentValidator` **can read**, which a `.tscn` export never was.
     ⚠️ **Leave `Goto` empty on that choice.** A conversation left open behind the vendor window returns
     when the shop closes; `--validate` rejects an `OpenShop` choice that points anywhere.
     The handover is safe in that order: `DialogueSession.Choose` applies the effect *before* it resolves
     `Goto`, so `VendorPanel` registers with `UiState` before `DialoguePanel` deregisters, and the owner
     count never hits zero — no pause flicker, no mouse-mode flicker.
   - **An unattended counter or stall** — an `Entity` with a collider and a
     `VendorComponent { ShopId = "shop.xxx" }`. Still the right answer where there is nobody to talk to;
     its first world placement is the market district. ⚠️ Its `ShopId` lives in a `.tscn` and is therefore
     **unvalidated** — a typo gives no prompt at all rather than an error (see 8 below).

   ⚠️ **A TRAVELLING MERCHANT MAY NOT USE THE DIALOGUE ROUTE (38L).** This is not a preference; the
   dialogue path has no notion of presence. `DialogueSession` evaluates `ShopOpen`/`ShopClosed` through
   `ShopHours.IsOpenAt` alone — *hours*, never `IsInTown` — and the only thing that hides an away
   merchant and zeroes their collider is `VendorComponent.ApplyPresence`. Wire a traveller through a
   conversation and they stand at their cart every day of the cycle with a working trade line, which is
   38J's whole mechanic inverted. **Anyone with a `VisitEveryDays > 0` gets a `VendorComponent`**, and
   pays the unvalidated-`ShopId` price for it — Hesk and the Embermarket's two caravanners all do.
   Making a traveller talk would mean teaching the two conditions about presence *and* adding a
   hiding component for dialogue merchants; nobody has needed it enough yet.

   Reach any shop without walking to it via `shop <id>` in the F1 console; drive the discount with
   `rep <factionId> <delta>`.
10. ⚠️ **`ContentValidator` does not scan `.tscn`**, so a mistyped `ShopId` gives **no prompt at all**
   rather than an error — the same trap `PropertyStorageComponent.PropertyId` carries. A merchant
   silently unusable in game: check that field first.
11. **Prices have one authority: `ItemInstance.Value`**, which already folds in rarity and affix
   count, so the spread applies to rolled loot for free. Put any new pricing maths in
   `src/Economy/ShopPricing.cs` and any restock/level/purse maths in `src/Economy/ShopStock.cs` (both
   Godot-free, so the test project can pin them) — never at a call site, and never a second price table.
   ⚠️ Day arithmetic there widens to `long`: a never-stocked shop is stamped `int.MinValue`, and
   `0 - int.MinValue` overflows back to a negative, which answered "not due" for the one case that
   most obviously is. The test caught it on the first run.
12. **Buying charges before it delivers, and refunds if delivery fails.** The other order cannot work:
    `InventoryComponent.AddInstance` merges a stackable into an existing stack, so the instance handed
    in is often never stored and `RemoveOneInstance` would find nothing to roll back. Refunding gold
    always works — spending it either freed a slot or left a stack with room.
13. **Selling removes by reference for rolled items, by template id only for stackables** — the same
    split `StoragePanel.Transfer` makes, for the same reason. A zero payout is refused rather than
    accepted: handing an item over for nothing is item loss wearing a transaction's clothes. The shelf
    decrement (`ShopStockService.TakeOne`) is deliberately the **last** step of a purchase: nothing may
    consume stock on a path that ends without the player holding the goods.
14. **Gate a shelf, and sell a stake in the merchant (38I).** A `ShopStockEntry` carries three optional
    gates, all of whose defaults mean *ungated*: `RequiredTier` (`ReputationTier.Hated` is the bottom of
    the ramp, so it needs no sentinel), `RequiredFlagId`, and `RequiredInvestment`. `ShopResource`
    carries `InvestmentTiers` — an array of `ShopInvestmentTier` (`Cost` + `PurseBonus`), cheapest
    first — and buying a rung is permanent, raises the merchant's purse at **every future restock**, and
    unlocks the rows gated behind it. `ShopStock.LockOf` is the one gate authority (pure, swept by
    tests); the panel evaluates it because the gate needs the player and `ShopStockService` is
    deliberately player-agnostic.
    ⚠️ **A stake moves no price, on purpose.** Standing owns the price ramp (38C) and 38F's sweep
    contract says every new multiplier joins `NoCombinationOfMultipliersLetsSellingBeatBuying` — 38I
    honours it by adding none. If a later sub-phase wants an investor discount, it joins that test.
    ⚠️ **A locked row is shown, greyed, with its gate named** — the sold-out rule, for a stronger
    reason: a hidden row teaches nothing, and a locked one is how the player learns a stake buys
    something. The refusal order is **flag → standing → gold**, `PropertyClaim.Resolve`'s rule, so
    nobody is sent to earn coin for something a story beat is holding shut.
    ⚠️ **An unlimited purse (`PurseGold = 0`) stays unlimited** no matter the stake — adding to it would
    make a bottomless merchant *finite*, a downgrade the player paid for. Settled in
    `ShopStock.PurseAfterInvestment` so no caller can forget it, **and** rejected by `--validate`,
    because safe arithmetic does not make the data meaningful.
    ⚠️ **`RefundPurse` clamps to the invested ceiling, not the authored one**, or a sale that debited and
    failed would quietly erase what the stake bought.
    `--validate` rejects nine shapes, every one of them well-formed data that buys nothing: a free rung,
    a ladder that stops climbing, a purse bonus on an unlimited purse or on a shop with no restock clock,
    a stake granting no purse and unlocking no row, a row needing more rungs than exist, a shop that
    gates *every* row (its window opens empty for a new player), a `RequiredFlagId` nothing writes
    (folded into `ValidateStoryFlags`' reader pass), and a standing gate above Neutral on a shop with no
    `FactionId` — the window falls back to Neutral there, so that shelf never opens.
    Reach it without the grind: `shop invest <id>` in the F1 console buys a rung for free, and the
    `shop` listing prints `stake held/total`.

15. **Give a shop a clock, and a merchant a road (38J).** `ShopResource` carries `OpenHour`/`CloseHour`
    (**equal means always open** — the `0`/`0` default, so the fields arrive inert) and
    `VisitEveryDays`/`VisitDayOffset` (`0` = resident). All the arithmetic lives in the new Godot-free
    `src/Economy/ShopHours.cs` — `IsOpenAt` (half-open window, wraps past midnight), `OpenSpanHours`,
    `NextOpenHour`, `IsInTown`, `NextVisitDay`.
    **Presence needs no save state at all.** It is a pure function of `WorldClock.Day`, so unlike every
    other piece of shop state in this arc there is nothing to persist and nothing to drift out of step
    with a reloaded clock. Do the same for anything else derivable from the clock.
    - **A merchant who talks** gates her trade choice on `DialogueCondition.ShopOpen` (12) with
      `ConditionArg` = her shop id, and authors a second choice on `ShopClosed` (13) pointing at a
      closed-hours node. ⚠️ **`--validate` requires that pairing** on any shop with hours: without it the
      player picks "let's trade" at midnight and *nothing happens*, which is a dead choice rather than a
      refusal. `ApplyEffect` refuses too, as a backstop — but the backstop is silent by design, so the
      condition is what actually speaks. ⚠️ **Gate every trade choice**, not just the first: Aldreth
      offers trade on two nodes, and a gate on one of two doors is not a gate.
    - **An unattended stall or a traveller** uses `VendorComponent`, which now hides its own entity on
      the days the merchant is away. ⚠️ **Hiding a `Node3D` does not disable its collision** — the
      hidden trader would still stop the interact ray and the player's body, an invisible wall that
      reads as a physics bug. `ApplyPresence` zeroes and restores the collider's `CollisionLayer`
      alongside `Visible`, and both live in one function so neither can happen without the other.
    - It rides `TimeOfDayChangedEvent`, the hourly tick `ScheduleComponent` already uses — no
      `_Process`, no new event, and the day rolls over inside it.
    ⚠️ **Hours must be authored to match the merchant's `ScheduleComponent` routine, by hand.** A
    `ScheduleId` lives in a `.tscn`, which `ContentValidator` does not scan, so nothing can check that
    the shop shuts around the hour she walks away from her stall.
    ⚠️ **A consumable may never be sold only by travelling shops**, and `--validate` enforces it. Hours
    are a *wait* — the inn advances the clock — but a merchant who may not be in town is a coin flip
    against the calendar, and a player out of potions cannot sleep their way to one. **Services keep no
    hours at all**: an inn that closed at night would be the only way to pass the night, closed at
    night.
    Eight validator rules in all: an hour outside `0..23`, a day shorter than
    `ShopHours.MinimumOpenSpan`, a negative visit cycle, a cycle of `1` (that is a resident), a cycle
    above `ShopHours.MaxVisitGap`, an offset outside `0..n-1` (a merchant who never appears), a
    shop-hours condition naming an unknown shop, and the ungated `OpenShop` choice above.
    `shop <id>` in the console **deliberately overrides both** and says so in its output.

## A new service — trainer / bank / inn / stable (Phase 38D)

> ⚠️ **MANDATORY LAST STEP: put it on the map.** A service with no `MapLocationResource` **fails
> `--validate`** (`ValidateEverythingIsOnTheMap`). Add a row to `tools/gen_map_locations.py` and run
> it — see [a new map location](#a-new-map-location-phase-395a).

1. Author `data/services/Xxx.tres` (`script_class="ServiceResource"`): unique `Id` (`service.*`), a
   `NameKey` in `strings.csv`, a `Kind`, a `PriceGold`, an optional `FactionId`, and the fields that
   `Kind` reads. Auto-indexed by `ServiceDatabase`. Place it as an `Entity` with a collider and a
   `ServiceComponent { ServiceId = "service.xxx" }`.
2. **`UnlockFlagId` is the pay-once contract, and which services need it is a validator rule.**
   Empty = charged every use (an inn bed, a lesson whose recipes are their own receipt). Set = charged
   once, and the story flag *is* the record.
   ⚠️ **A Bank or Stable without one charges its fee on every single interaction** — the exact shape of
   the bug 36E fixed for boss rewards.
   ⚠️ **A Trainer granting `XpReward` without one is an infinite gold-to-levels pump.** `DESIGN.md` §6
   forbids buying the defining power, so an XP lesson must be bounded by a flag. A trainer that only
   teaches recipes needs no flag: not knowing the recipe *is* the check.
   ⚠️ **An Inn with one** would make the first night the only one that ever charged.
3. ⚠️ **An inn must rest through `ServiceRules.RestTarget`, never with its authored `RestHour`.**
   `WorldClock.SetTimeOfDay` advances `Day` only for an hour of 24 or more and otherwise just rewinds
   the hour — so resting from 20:00 to 08:00 has to be asked for as `32`. Passing `8` looks like it
   works and silently freezes 38B's shop restock clock and every future daily service, with nothing in
   the failure pointing at the inn. Resting *at* the target hour buys a whole day, because an inn is
   never a no-op.
4. **A trainer sells access, never a rank.** Recipes through `CraftingComponent.Learn` and XP through
   `ProgressionComponent.AddXp` — so skill points arrive by *levelling*. Nothing in the game grants a
   point directly and 38D deliberately did not add a way; see the recipe-reachability rule above for
   the `Starting` ∪ trainers union.
5. **A bank is 37B's storage with the property gate removed.** Put an `InventoryComponent` on the
   service's own entity and it *is* the vault — `StoragePanel` already answers `StorageOpenedEvent`, so
   there is no UI and no save code to write.
   ⚠️ **Give that entity a `PersistentId` and never change it** (`inventory:<PersistentId>` is the save
   key). Without one the inventory does not register as a saveable at all and the vault empties on every
   reload, which reads as item loss rather than a missing field. Author `Capacity` on the node, not the
   resource: `InventoryComponent.Load` clamps to it.
6. **Standing prices a service and can refuse it**, through the same `ShopPricing.PriceMultiplierFor`
   ramp a shop uses — `ServicePrice` is the flat-price entry point, rounding up and flooring at 1 so a
   discount cannot make a priced service free. ⚠️ Copy the **inverted** hostility default: an
   unresolvable `ReputationComponent` serves normally, the opposite of `EnemyAIComponent`'s fail-safe.
7. ⚠️ **An entity gets one interactable**, so a `ServiceComponent` behind a `DialogueComponent` never
   fires. The innkeeper's placeholder conversation was replaced outright; the trainer and stablemaster
   are their own NPCs; the vault is a prop, because the thing that must persist is an inventory and no
   town NPC carries a `PersistentId`. And ⚠️ **`ContentValidator` cannot scan `.tscn`**, so a mistyped
   `ServiceId` gives no prompt at all rather than an error.
8. **There is no `ServiceKind.Repair`**, and that is deliberate: no durability or condition concept
   exists anywhere in the game. 38D's brief says repair lands only "if durability is adopted in 40",
   and 40B's rule is that cut systems leave no stub — so a kind resolving to nothing would be worse
   than its absence. The deferral is recorded in `docs/DESIGN.md` §6 against Phase 40A.

9. **A commission counter (38Q) is this recipe plus two fields, and it breaks two of the habits above.**
   `Kind = Commission`, `CommissionStation` (never `Hand`), `MaterialsShopId` pointing at the master's
   own shop, and `PriceGold` as the labour. He opens the ordinary `CraftingPanel` filtered to that
   station and supplies whatever the pack is short of, at that shop's prices and the player's standing.
   ⚠️ **It must be PRICED, and `--validate` enforces that** — the opposite of the free-service rule
   that fired for 38O's search, 38P's collect counter and 38P2's appraiser. Those are free because an
   unaffordable service fails closed on the player who needs it; a commission *hands over goods*, so a
   free one is the materials shop with the spread deleted.
   ⚠️ **It is charged AFTER its verb**, the only kind that is (`CraftingComponent.Commission`). A full
   pack refuses the piece and rolls the whole craft back, so charging first would be the one way in the
   battery to lose the money for nothing.
   ⚠️ **A commission is the first price the `ShopPricing` clamps do not protect.** It spans two
   different items — ingredients in, output out — and crafting is meant to add value, so buy-make-sell
   is an unbounded loop that only the labour fee closes. `--validate` runs
   `CommissionRules.Exploitable` over every recipe at the station, at Allied standing, and names the fee
   you need. **Do not author the fee on the floor it prints** — a later recipe or a keener buyer eats
   the margin.
   ⚠️ **Sanity-check what the counter is worth next to a free station** before authoring one at all.
   `town_hub` has three public stations; a master charging for labour alone would be strictly worse
   than walking twenty metres, which is the "correct and imperceptible" failure that got 38G parked.

## A supply contract / a contract board (Phase 38Q2)

1. Author `data/contracts/Xxx.tres` (`script_class="ContractResource"`): `Id` (`contract.*`), a
   `NameKey` in `strings.csv`, `ItemId`, `Quantity`, `RewardGold`, and optionally `FactionId` +
   `ReputationDelta`. Auto-indexed by `ContractDatabase`. **No code, and no quest.**
2. ⚠️ **A contract is not a quest and must never become one.** `QuestLogPanel` deliberately carries no
   Contracts heading — "the journal shows the states the data actually has" — so there is no
   `QuestResource`, no objective and no `DialogueEffect.StartQuest` anywhere in the feature. The
   board's own window is the whole UI.
3. ⚠️ **The reward must BEAT the best buyer, and `--validate` enforces the floor it prints.** A
   posting paying less than a merchant already pays is a longer walk for less money — 38G's
   imperceptibility failure. There is deliberately **no ceiling**: buying goods cheap and delivering
   them dear is a real trade, and what bounds it is that a posting can be filled **once per rotation**
   (`ContractLedger`). ⚠️ This is the exact mirror of the commission rule above — that one is refused
   for being too *cheap* because it can be looped; this one for being too *poor* because it cannot.
4. ⚠️ **Never name an `ItemType.Quest` item.** Handing one over would strand a Collect objective with
   no way to recover it — `ShopPricing.Sellable`'s own reasoning, and a rule because of it.
5. **The board itself is a `ServiceKind.Contracts` service** (`BoardSlots`, `RotationDays`), free and
   flagless, on a prop entity with a collider and one component — an entity gets one interactable.
   ⚠️ `--validate` insists the authored pool holds **more** contracts than the board has slots, or a
   rotation would show one posting twice.
6. ⚠️ **The rotation is derived from the day and never stored** (`ContractRules.Cycle`), so the same
   day always shows the same board and a quickload cannot reroll it. Only what has been *filled* is
   saved. If a later board ever needs a rolled rotation, that rotation has to become saved state and
   this property is gone — do not reach for an RNG without meaning it.
7. **Adding or removing a contract reshuffles which posting sits on which slot** for every cycle past
   and future, because the pool is indexed by position. Harmless — nothing saved refers to it — but
   worth knowing before blaming the rotation for looking wrong after an edit.

## Generators — do not hand-write boilerplate (agent-ergonomics pass)

Two committed scripts cover the repo's highest-volume authoring. Both **print to stdout** so the
output is read before it lands: the `.tscn`/`.tres` stays the authored artefact.

- **`python tools/gen_cell_props.py props.txt`** — a table of
  `name  ext_id  x  z  shape_id  y_centre  [yaw]  [y_offset]` becomes the four-node prop stanzas.
  Verified byte-identical against `emberdeep_mine.tscn`. `--no-collider` for scenery that must not
  carve the navmesh. It encodes the conventions that were being retyped per prop: static props
  parent to `Nav`, the collider is a child `StaticBody3D`, and its size comes from the model's
  **measured** bounding box.
- **`python tools/gen_merchant_dialogue.py <key> <dialogue.id> <shop.id> "<Speaker>"`** — the
  resident-merchant graph, verified identical against `data/dialogue/Wenna.tres`. ⚠️ It emits the
  **scaffold only**; the locale rows stay hand-written, and it lists which ones you owe on stderr.

A dressed cell was ~8k output tokens of near-identical stanzas and a merchant conversation ~1.3k.
Both scripts were written ad hoc and thrown away twice before being committed.

## A new map location (Phase 39.5A)

**Do not hand-author these.** The `.tres`, the locale keys and the scene marker must agree, and
`tools/gen_map_locations.py` generates all three from one table so they cannot drift.

1. **Add a row to the table in `tools/gen_map_locations.py`** with the `add(...)` helper:
   the cell file, the id tail, the category, **the anchor node path in the cell scene**, the display
   name, and whichever of `shop=` / `service=` / `dialogue=` / `travel=` apply.
2. **Pick the anchor carefully — it is the whole feature.** ⚠️ **Parent the marker to the stall,
   counter or keeper the location IS, never to the cell root with an offset.** The marker's transform
   is the location's only position, so a marker parented to the thing moves with it and a marker
   parented to the cell root is a second copy of a coordinate that will rot (invariant 22).
   `.` means the cell root and is correct **only** for the settlement itself.
3. **Do not author a coordinate anywhere.** There is no field for one.
4. **Link, do not restate.** The map asks `ShopDatabase` what a place sells, `ServiceDatabase` what
   it charges and `DialogueDatabase` who keeps it. Never copy a name or a price into the location.
5. **Reuse an existing name key where the place already has one.** The five settlements with
   waystones use their `travel.*.name` key, so renaming the waystone renames the pin.
6. `RevealWithCell = true` only for something visible from outside. ⚠️ **A region loads whole, so it
   really means "known on entering the region"** — anything the player should *find* leaves it false
   and is discovered by walking within 20 m.
7. **Run it:** `python tools/gen_map_locations.py`, then `--check` to confirm it is idempotent.
8. **Gate it:** `godot --headless --path . -- --validate` (both directions of the scene seam) and
   `godot --headless --path . --script res://tools/map_probe.gd` (a distinct, in-cell world position).

⚠️ **A new `MapCategory` is a change to `src/World/MapCategory.cs` AND to the `CATEGORY` list in the
generator**, which stores the enum's *index*. They are a contract; reordering one without the other
silently recategorises every location, and only `--validate` catches it.

⚠️ **Add a category only when content exists for it.** The filter panel and legend already hide
groups with no pins, so an empty category is invisible rather than harmless — it is a promise the
world does not keep, which is the empty-heading problem 37.5E refused for the journal.

## A new region cell (Phases 25, 38K/N1/N2/O, 37E)

> ⚠️ **A new cell needs at least a settlement/landmark map location**, or it is a place the player can
> stand in that the map cannot name. See [a new map location](#a-new-map-location-phase-395a).

⚠️ **This recipe did not exist until 37E**, and the settlement recipe below pointed at it ("the cell
recipe above"). Everything here had been rediscovered four times from other cells' comments.

1. **Pick the centre by arithmetic, not by eye.** Cells share one coordinate space and abut exactly.
   A 52 m floor centred at x = 52 runs x 26..78, so it abuts a 52 m floor centred at x = 0 (which ends
   at 26) precisely. ⚠️ **A gap is a hole the player falls through; an overlap is two coplanar floors
   z-fighting along the seam. Neither is visible from the `.tres` or the `.tscn`** — write the sum
   into the cell's comment, which is what every cell since 38K does.
2. **Author the cell** in `data/regions/<Region>.tres`: a `sub_resource` with `Id`, `ScenePath`,
   `Center`, and add it to the region's `Cells` array. ⚠️ **Forgetting the array is silent** — the
   resource exists, `--state` does not count it, and nothing loads.
3. **`SafeRadius`** is the no-spawn bubble. Set it to cover anywhere the player is meant to be able to
   stand still. The region's own `SafeZoneRadius` cannot reach a cell a street away.
4. ⚠️ **`Surplus` / `Demand` / `ShockTags` only if a shop stands in the cell.** `ValidateCellTrade`
   refuses trade tags with no counter to read them — a half-authored settlement is invisible (38G),
   and a cell that sells nothing (a homestead, a wilds, an arena) authors none.
   ⚠️ A shockable cell also needs a `cell.<id>` locale row, or the caravan board posts the raw key.
5. **The scene** (`scenes/regions/<region>/<cell>.tscn`): a `NavigationRegion3D` named `Nav`, a floor
   mesh + `StaticBody3D`, then the content. ⚠️ **Everything with a collider parents under `Nav`** —
   geometry outside it is not carved into the bake (27A) and the failure is invisible until you watch
   an NPC walk through a wall. Interactable `Entity`s parent to the cell root.
6. **A schedule in the cell carries a COPY of the cell's `Center` as `Origin`** so destinations stay
   cell-local. Moving a cell is never a one-line edit.
7. **Props: measure, never guess.** `python tools/gen_cell_props.py` expands a table into stanzas;
   collider sizes come from the model's measured bounding box (`ASSET_POLICY.md` §0.6 — accessor
   bounds ignore node scale and will lie to you).
8. ⚠️ **RENDER IT — the approach, not just the objects.** Copy `tools/market_shots.gd`, point it at
   the cell, and shoot at **eye level** from where the player actually arrives. Every 37E defect was a
   correctly-authored model in a position that ruined the shot: a 3 m waystone that reads fine on a
   road stood as a monolith blocking a front door at 4 m. **A `.tscn` reads fine while looking wrong.**

## A production settlement (Phase 38N1)

1. **Author what it refuses before what it sells.** A settlement is a different *place* only if its
   merchants are a **source** and a **sink** rather than two more stalls: one who sells the local
   product at the realm's lowest `BuyMarkup` and barely buys, and one who pays the realm's best
   `SellFraction` for what the place cannot make. `AcceptedTags` is the design; `Stock` is decoration
   on top of it.
   ⚠️ **The temptation is to let the sink buy everything so the walk is never wasted, and that is
   exactly what flattens two settlements back into one.** The Emberdeep quartermaster deliberately
   does not accept `ore`.
2. **Check the tag has members first.** `TradeTags`' own rule is that a tag with nothing wearing it is
   "a promise rather than a feature" — the mine needed a second and third `ore` item before an ore
   settlement meant anything, exactly as 38L needed a catalogue before twelve specialists did.
3. **Everything else is the existing recipes**: the cell recipe above, "a new shop / merchant" for the
   two shops and their conversations, and a `ScheduleResource` with `Origin` set to the cell's
   `Center` so destinations stay cell-local.
4. ⚠️ **Carrying goods between two settlements cannot turn a profit yet, and no amount of authoring
   changes it.** `ShopPricing` clamps every markup to `>= 1` and every sell fraction to `<= 1`, so
   `sell <= value <= buy` holds at each shop and a two-shop carry is always a loss. Run
   `godot --headless --path . -- --economy` to see it. Regional demand (38G) moves an item's *value*
   per settlement and is the only thing that turns those margins positive — do not try to author
   around it with a generous spread, which just narrows the loss.

## A tolled crossing — toll, permit, bribe (Phase 38M)

1. **Author the toll on the destination region**, not on the link: `TollGold`, `TollPermitFlagId` and
   `TollPassFlagId` on `data/regions/Xxx.tres`. `TollGold = 0` (the default) is an untolled road, so
   every existing region is unaffected. A gate on a two-way road is **two identical blocks, one per
   region** — the same "declare it on the destination" shape `UnlockFlagId` has, so the bootstrap
   needs no per-link table.
2. **Sell the papers as `ServiceKind.Passage` services.** A permit authors `UnlockFlagId` and nothing
   else — the receipt *is* the exemption, so there is no second record to drift. A bribe authors
   `GrantedFlagId` (consumed at the gate) and a negative `ReputationDelta`, and leaves `UnlockFlagId`
   **empty**.
   ⚠️ **Recording a bribe in `UnlockFlagId` makes it a permit**: that field doubles as
   `ServiceComponent`'s already-bought check, so the gate-hand would refuse to sell a second one.
   ⚠️ **A permanent pass sold cheaper than the permit deletes the sink** — one bribe and the road is
   free forever. The pass is consumed by `GameBootstrap.PayToll` for exactly that reason.
3. **The standing cost is the second half of the price and needs no new currency.** 38C prices every
   merchant off the same faction standing, so a bribe is charged once at the gate and again at every
   counter in town, forever. Author `FactionId` or the cost lands nowhere — a validator rule.
4. ⚠️ **A permit and a bribe are two entities.** `GetComponent<T>` returns the first child match, so
   two `ServiceComponent`s on one body leave the second unreachable and silent (the 38E finding).
5. **Charge at the crossing, never at the interactable.** `GameBootstrap.PayToll` sits in
   `OnRegionTransitionRequested`, which the portal *and* the `region` dev command both arrive at —
   gating only the component leaves the console a free ride, which is 38C's travel-fee lesson.
   Fast travel is deliberately **not** tolled: it already pays `TravelFee`, and one journey does not
   pay two charges.
6. **Quote the price from the function that charges it.** `RegionTransitionComponent.Prompt` and
   `PayToll` both call `TollFee.Resolve`, so the number at the gate is the number taken. The prompt is
   also the refusal channel — `Notifications` has no generic message event, and a refusal that says
   itself where the player is already looking needs none.
7. **Two flags, no database — so `--validate` is the only thing standing between a typo and an
   uncrossable road.** The rule checks that each flag a tolled region names is granted by some
   authored `Passage` service, as a union. Placing the warden proves nothing: `.tscn` is not scanned.

## A fence and contraband (Phase 38O)

**Read this before authoring anything that no honest merchant should touch.** Contraband is not a
trade tag like the other twenty-two — it is the only one that fails **closed**, and every step below
exists because the ordinary tag rules are wrong for a prohibition.

1. **Tag the goods, and tag them with something else too.** `TradeTags.Contraband` on the item, plus
   whatever it actually is (`gem`, `pelt`, `arcane`…). The second tag is not decoration: it is what
   makes the refusal legible, because the jeweller who deals in gemstones turns down a stolen signet
   in front of the player.
   ⚠️ **Contraband dominates.** `TradeTags.Accepts` answers it first and ignores everything else, so
   a contraband item is refused by every shop that does not name `contraband` in its own accepted
   list — including a general store with an **empty** list, which under 38F's "both empties mean yes"
   would otherwise fence smuggled goods across the most respectable counter in town.
2. **Give it a source.** Contraband nothing drops and nobody stocks is five files that exist and
   cannot be reached. Loot rows on the factions who would carry it (`BanditLoot`, `SyndicateLoot`) are
   the cheap answer; a fence's own shelf is the other, and an item on neither is the
   `CraftingComponent.Learn` shape CLAUDE.md §1 forbids.
3. **Author the fence's refusals first**, exactly as a production settlement does (38N1). A fence who
   takes everything is a general store with a reputation cost bolted on, and the walk to her buys the
   player nothing.
4. ⚠️ **Leave the fence's `FactionId` EMPTY.** The natural owner is `faction.outlaws`, which starts at
   `-30` — tier `Hostile`, at or below its own `HostileThreshold` — so an outlaw-factioned vendor is
   hidden by `VendorComponent.ApplyPresence` and refuses to trade from the first minute of a new game.
   The standing a fence *moves* and the standing she *prices by* are two different questions.
5. **Author both sides of the cost.** `ContrabandFactionId`/`ContrabandDelta` for the faction the sale
   pleases, `ContrabandPenaltyFactionId`/`ContrabandPenaltyDelta` for the one it offends. Positive and
   negative respectively — `--validate` rejects a backwards sign, a missing faction, a zero delta
   beside a named faction, a one-sided fence, and a cost on a shop that will not take the goods.
   ⚠️ **Per sale, not per unit.** This is deliberately the opposite of 38H's per-unit payout decay:
   charged per unit, one click on a stack of twenty moves the player three reputation tiers.
6. **Two fences, not one.** One fence is a door; two are a choice, and they differ in the three
   numbers that matter — what they accept, what they pay, and what the sale costs in standing. Give at
   least one of them `OpenHour == CloseHour` (always open), or contraband becomes unsellable for part
   of every day and the player reads a shop's opening times as the mechanic being broken.
7. **Confiscation is a `ServiceKind.Search`; recovery is a `ServiceKind.Redeem`.** Both are ordinary
   38D services and inherit the price, the standing discount, the hostile refusal and the whole prompt
   battery. The search is priced `0` — a search the player can be too poor to undergo waves the
   contraband through — and the redemption's `PriceGold` is the **per-unit fine**, not the bill;
   `ContrabandLaw.Fine` multiplies it by what is held.
   ⚠️ **Two bodies, not one with two services** — `GetComponent<T>` returns the first child match
   (38E), the same rule the permit and the bribe already follow.
   ⚠️ **A realm that can seize must be able to give back.** `--validate` fails a `Search` with no
   `Redeem` anywhere: a permanent seizure is theft rather than a fine, and it is the only thing that
   would make carrying contraband a risk the player cannot price.
8. **Nothing new joins the sweep test, and say so where you would have added it.** Contraband adds a
   *refusal*, not a price multiplier, so `NoCombinationOfMultipliersLetsSellingBeatBuying` is honoured
   by there being nothing to add — 38F's contract still binds the next author who does add one.

## A new gold sink (Phase 38C)

1. `docs/DESIGN.md` §6 holds the authoritative sink table — add the row there, or the sink exists in
   code and nowhere a designer looks.
2. Put the price in a Godot-free class under `src/Economy/` (`TravelFee` is the model) and charge it at
   the **single point every path converges on**. The travel fee is charged in
   `GameBootstrap.OnFastTravelRequested`, not at the map screen, because the `travel goto` dev command
   publishes the same event — gating the UI alone leaves the console a free ride.
3. The UI shows the price and greys the control with a reason, reading the **same function** the charge
   uses (`TravelCosts.FeeFor` for both). A price shown that differs from the price taken is the bug this
   split exists to prevent, and it is easy to reintroduce by resolving a dependency two different ways
   — the first draft of 38C did exactly that, reading the active region from a service the map could see
   and the bootstrap could not.
4. **Give the sink an exemption the player can earn.** Travel to a holding you own is free, matched
   through `PropertyResource.TravelNodeId`. Without something like that a sink reads as a toll booth
   rather than as a choice.

## A big/boss creature with body zones (Phase 35A)

1. Author the archetype `.tres` as above, plus:
   - `HitZones` — an array of `HitZoneResource` sub-resources (`Id`,
     `DamageMultiplier`, `Offset`, `Radius`, `Height`; height ≤ 2×radius makes it a
     sphere). Non-empty **replaces** the whole-body capsule hurtbox, and doubles as
     the greybox silhouette, so the visual can never drift from what is damageable.
     The multiplier scales poise damage too — a headshot staggers harder.
   - `IsBoss = true` → the actor is a `BossEntity`, which is what the Phase 28C
     healthbar and the 28D corruption-on-kill loop resolve by type.
   - `DirectionalMelee = true` → a `DragonMeleeComponent` swaps the one
     `MeleeWeaponComponent`'s hitbox between jaws/wing/tail by the target's bearing.
2. **Give its AI profile a `TurnSpeedDegrees`.** The AI faces its target before every
   swing, and the default (`0`) snaps instantly — a body that always looks at you can
   only ever use its frontal attack, so the flank and rear arcs are dead code without
   a turn rate. It is also the knob that makes a heavy creature *feel* heavy.
3. `ContentValidator` checks the zones (ids unique and non-empty, radius and
   multiplier positive, directional melee backed by zones).

## Making a creature fly (Phase 35B)

1. Set `TakeoffRange > 0` on its **AI profile** (`data/ai_profiles/Xxx.tres`) plus
   `HoverAltitude`, `ClimbSpeed`, `AirborneDuration` and `GroundedDuration`. Flight
   is a property of the profile, not the archetype — `EnemyArchetypeFactory` attaches
   a `FlightComponent` when the profile can fly, and `0` (the default on all four)
   means no flight and no cost.
2. **Keep the airborne window short.** A flier with no ranged attack that hovers
   indefinitely is a fight where neither side can act. The cycle is deliberately
   `Grounded → TakingOff → Airborne → Landing → Grounded`, never open-ended.
3. Nothing else needs changing: the AI steers a flier horizontally exactly as it
   steers a walker, and `LocomotionComponent.Flying` owns the vertical axis alone.
   `ContentValidator` rejects half-authored flight tuning either way round.

## A breath weapon (Phase 35C)

1. Author `data/spells/Xxx.tres` with `Delivery = 3` (Cone) and `CastMode = 2`
   (Channeled): `ConeAngleDegrees` is the **full** opening angle, `ImpactRadius` is
   the cone's *length*, and `PlayerLearnable = false` for a monster's breath. It is
   an ordinary spell — school resistances, `SchoolIdentity`, status effects and
   `SpellResolver` all apply with no special-casing.
2. On the archetype set `BreathSpellId` **and** add the same id to `KnownSpellIds`
   (the breath is cast through the normal spellcasting path, not around it — the
   validator rejects one without the other). `BreathDuration` is how long the
   channel is held.
3. **A caster is a profile that stands off, not an actor that holds spells.** Giving
   a melee creature spells does not turn it into a kiter — `EnemyAIComponent` branches
   on `AIProfileResource.IsStandoff` (`StandoffRange > AttackRange`) alone. Set a
   standoff range only if you actually want it to back away.

## Placing a world boss in a lair (Phase 35D)

1. Set `TerritoryRadius` on its AI profile. Without it the AI **chases forever** —
   `_home` is otherwise read only by patrol and retreat — and a flying boss will
   follow the player into the next realm. `0` is no leash, which is every other
   profile.
2. Add a marker `Entity` to the region cell's `.tscn` with a **stable
   `PersistentId`** and a `LairSpawnComponent` (`TemplateId`, `SpawnOffset`). It
   builds the creature through `EnemyTemplateRegistry` — no new factory.
3. **Persist the spawner, never the boss.** `CellPersistenceDirector` reconciles on
   `RegionCellLoadedEvent`, published *after* the streamer adds the cell root, so a
   boss spawned in that frame races the walk and a deferred one misses it entirely —
   either way the boss resurrects every time the cell reloads. The authored spawner is
   always found, so it holds the "defeated" bit instead.
4. **The cell carries its own floor** (see 34.5A). Size it for the fight: the roost's
   floor is 90 m because the territory radius is 45. Butt it against a neighbouring
   cell's floor rather than overlapping — co-planar floors z-fight — and keep it clear
   of other cells' props and the region's safe zone.
5. **Give each lair its own `PersistentId`.** `LairSpawnComponent.SaveId` derives from
   it, so two lairs sharing one means killing either marks both defeated.
6. **Inherit `scenes/regions/roost.tscn`** (Phase 35F paid the debt the two hand-authored
   roosts flagged). The base owns the nav region + baker, the floor mesh/collider and the
   `Nest`/`Lair` markers; a roost overrides the `RoostCell` script's `FloorSize`/
   `FloorColor`/`EmberColor`/`EmberEnergy`, the `Nest`'s `PersistentId`, the `Lair`'s
   `TemplateId`, and adds its props **as children of `Nav`** (geometry outside the
   navigation region is not carved into the bake). Floor mesh, shape and material are
   base-scene sub-resources and therefore shared by every roost — `RoostCell` `Duplicate()`s
   each before touching it, and anything else you vary must do the same.
7. **Set `DefeatFlagId` if anything needs to know the boss is dead** (35F). It is the only
   thing in the game that turns a kill into a story flag, so it is what a dialogue
   condition or a gated interactable (e.g. `SpellTomeComponent.RequiredFlagId`) can ask.

## A creature that talks (Phase 35F)

1. Set `DialogueId` on the archetype. `EnemyArchetypeFactory` attaches a
   `DialogueComponent`, and the player's interact raycast is unmasked — it resolves the
   owner from whatever collider it hits, so the body the creature already has is the
   target. No extra collision, no bespoke factory.
2. **Put it in a faction the player is not hostile to**, or it attacks before the prompt is
   ever readable. `faction.dragons` is the pattern: `DefaultReputation` in the Neutral band,
   `HostileThreshold` at Unfriendly. `EnemyAIComponent.PlayerIsTarget` does the rest, and
   the first player hit sets `_provoked` regardless — neutral-until-provoked is pure data.
3. To have it **teach a recovered spell**, use the `LearnSpell` dialogue effect (`8`) with a
   `spell.*` id. It goes through the same corruption-gated `SpellcastingComponent.Learn` a
   tome does and **ignores `PlayerLearnable`**, which is how a spell that can never be
   bought can still be given. Mark such a spell `PlayerLearnable = false`; the character
   screen lists it anyway once it is known.

⚠️ **Spawning an actor into a region cell: create at zero, add, *then* set
`GlobalPosition`.** The factories and `EnemyTemplateRegistry.Create` take a **local**
position, and a cell's root has already been moved to the cell's centre by the
streamer — so handing `Create` a world position applies the cell offset twice.
`BossSummonComponent` has always done it in the right order; the 35D lair spawner did
not, and its dragon landed on the wrong part of the map (visibly in the void once a
cell sat far from the origin). `EnemySpawnDirector` had the same latent bug.

## A new weapon

1. Author `data/weapons/Xxx.tres` (`script_class="WeaponResource"`).
2. Point a `MeleeWeaponComponent.Weapon` at it (factory or future equipment).

## A new item

1. Author `data/items/Xxx.tres` (`script_class="ItemResource"`) with a unique
   `Id` (e.g. `item.material.silver`).
2. It is auto-indexed by `ItemDatabase` on startup. Reference it anywhere via
   `ItemDatabase.Get("item....")` — pickups (`ItemPickupFactory.Create`), loot
   drops, shops, recipes.
3. New interactable kinds: subclass `InteractableComponent` (override `Prompt`
   and `Interact`) and add a collider so the player's raycast can hit it.

## A new piece of equipment

1. Author `data/items/Xxx.tres` (`script_class="EquippableItemResource"`,
   `MaxStack = 1`): set `Slot`, the `Bonus*` fields, and (for weapons) a `Weapon`
   `ext_resource` pointing at a `WeaponResource`. `BonusFrostResist` (Phase 35G) is the
   only one of the 34E resistances gear can carry so far — the other five are one
   `[Export]` and one line in `StatBonuses()` each, added when an item wants them.
2. It's indexed by `ItemDatabase` like any item; equip it via the character screen.
   Bonuses apply automatically through `EquipmentComponent` → `StatsComponent`.

## A new loot affix

1. Author `data/affixes/Xxx.tres` (`script_class="AffixDefinition"`): unique `Id`,
   a `Label` fragment, `Kind` (0 Prefix / 1 Suffix), target `Stat`, `MinValue`/
   `MaxValue`, `MinRarity`, `Weight`, and the `For{Weapons,Armor,Accessories}` flags.
2. Auto-indexed by `AffixDatabase`; it enters the eligible pool for any equippable
   whose gear family + rolled rarity match. No code change.

## A new loot table / dropper

1. Author `data/loot/Xxx.tres` (`script_class="LootTable"`) with `LootEntry`
   sub-resources (item id, `DropChance`, `Min/MaxQuantity`, `RollAffixes`), plus
   optional gold (`GoldChance`/`GoldMin`/`GoldMax`) and `QualityBonus`.
2. Add a `LootComponent` to the actor (set `Table` or `TablePath`); it rolls and
   spawns pickups on death. See `EnemyFactory` for the wiring.

## A new perk

1. Author `data/perks/Xxx.tres` (`script_class="PerkResource"`): unique `Id`,
   `DisplayName`, `Description`, `MaxRank`, `Cost`, target `Stat`, `ModifierType`
   and `ValuePerRank`.
2. Auto-indexed by `PerkDatabase`; it appears in the character screen's PERKS list
   and is learnable once the player has skill points. No code change.

## A new XP-bearing enemy (or tuning the curve)

1. Add an `ExperienceComponent { XpValue = N }` to the actor's factory (see
   `EnemyFactory`) to grant XP on death.
2. Tune levelling by editing `data/progression/PlayerProgression.tres` (or author a
   new `ProgressionResource` and point a `ProgressionComponent.CurvePath`/`Curve` at
   it).

## A new quest

1. Author `data/quests/Xxx.tres` (`script_class="QuestResource"`) with a unique `Id`,
   `Title`/`Summary`, `Objectives` (an array of `ObjectiveResource` sub-resources:
   `Type` 0=Kill / 1=Collect, `TargetId` = entity `TemplateId` or item id,
   `RequiredCount`), and rewards (`XpReward`, `GoldReward`, `RewardItems` of
   `QuestItemReward`, and `FactionRewardId`/`FactionRewardAmount` — Phase 34.5C, the same
   pair `WorldEventResource` has; the amount may be negative). Optional
   `PrerequisiteQuestId` chains it after another. **Objectives are Kill/Collect only** —
   "go and talk to X" is not expressible, so a turn-in is a conversation the player has to
   remember to have.
   ⚠️ **A Kill objective must name something that respawns.** `--validate` requires the target to be
   spawnable by an encounter or world event, because a lair boss is killed once and stays dead — a
   quest taken afterwards can never complete and never leaves the journal (Phase 35F shipped exactly
   that). Targeting a one-shot boss needs `AllowsOneShotTarget = true` **and** an offering dialogue
   that gates on the target still being alive; see `quest.ancient.kin` + `dialogue.ancient_dragon`,
   which pair it with `LairSpawnComponent.DefeatFlagId`.
   **Story flags** (`Effect` SetFlag / `Condition` HasFlag) are the only way to mark
   *state* a quest can't: membership, a rank, a favour owed. They have no database, so
   `--validate` can only catch a flag that **nothing ever sets** — a `SetFlag` typo still
   fails silently. A choice carries **one** `Effect`, so a choice that starts a quest cannot
   also set a flag: hang the flag on the next node's farewell choice (see `Elder.tres`).
2. **Set `LocationId` on an objective only if the thing it names actually lives somewhere**
   (39.5C). It takes a `location.*` id and is what puts the objective on the compass, prints a
   destination under the HUD tracker, and rings the pin on the map — see
   [a new map location](#a-new-map-location-phase-395a) for authoring the place itself.
   ⚠️ **Almost nothing in Embervale qualifies, and leaving it empty is the normal answer.** Every
   hostile is a **region-scoped** `EncounterResource` spawned around the player by the
   `EncounterDirector`, and every quest material comes off a **loot table**, not a placed node — so
   "kill six goblins" and "gather iron ore" have no destination, and inventing one sends the player
   somewhere no better than anywhere else. The one authored example is `quest.ancient.kin`, whose
   target is a **placed lair** (`ash_roost.tscn`, which carries the matching `MapPin`).
   ⚠️ `--validate` fails on a `LocationId` no `MapLocationResource` declares — this is the quest arm
   of *"if the player can go there, it goes on the map"* (CLAUDE.md §1).
3. Auto-indexed by `QuestDatabase`. Start it via a `QuestGiverComponent` (set its
   `QuestId`) on a world `Entity`, in a `DialogueChoice` (`Effect` StartQuest), or
   directly with `player.GetComponent<QuestLogComponent>().StartQuest(...)`. Objectives
   advance and rewards apply automatically. No code change for new Kill/Collect quests.

## A new conversation

1. Author `data/dialogue/Xxx.tres` (`script_class="DialogueResource"`): unique `Id`,
   `SpeakerName`, `StartNodeId`, and `Nodes` — an array of `DialogueNode` sub-resources
   (`Id`, optional `Speaker`, `Text`, `Choices`). Each `DialogueChoice` sub-resource has
   `Text`, a `Goto` node id (empty = end), an optional `Condition`+`ConditionArg` (gates
   visibility — incl. `QuestAvailable`, `HasFlag`, and `CorruptionAtLeast`/`CorruptionBelow`)
   and an optional `Effect`+`EffectArg` (`1`=StartQuest, `2`=SetFlag, `3`=ClearFlag,
   `4`=AddCorruption, `5`=RecruitCompanion, `6`=DismissCompanion, `7`=AddCompanionLoyalty
   (`<companionId>:<delta>`)). Companion gates come as conditions too (`CompanionRecruited`,
   `CompanionNotRecruited`, `CompanionLoyaltyAtLeast` = `<companionId>:<value>`).
   Enums export as ints (see `DialogueEnums.cs`).
2. Auto-indexed by `DialogueDatabase`. Attach a `DialogueComponent` (set its
   `DialogueId`) to a world `Entity` with a collider; the player's `E` interact opens it
   in `DialoguePanel`. No code change for new conversations.

## A new NPC routine

1. Author `data/schedules/Xxx.tres` (`script_class="ScheduleResource"`): unique `Id` and
   `Entries` — an array of `ScheduleEntry` sub-resources (`StartHour` 0–23, `Activity`
   label, `Destination` world `Vector3`). Hours before the first block wrap to the last.
2. Auto-indexed by `ScheduleDatabase`. Add a `ScheduleComponent` (set its `ScheduleId`) to
   a static NPC `Entity`; it walks the routine off the `WorldClock` and reacts to alerts /
   dialogue. No code change for new routines.

## A new weather state

1. Author `data/weather/Xxx.tres` (`script_class="WeatherResource"`): unique `Id`, `Type`,
   `SelectionWeight`, `MinHours`/`MaxHours`, and the atmosphere fields (`LightEnergyScale`,
   `SkyEnergyScale`, `FogDensity`/`FogColor`, `Precipitation`).
2. Auto-indexed by `WeatherDatabase`; the `WeatherDirector` can roll it and the
   `SkyController` renders it (light/fog/rain). No code change.

**A new region** (Phase 25)
1. Author `data/regions/Xxx.tres` (`script_class="RegionResource"`): unique `Id` (`region.*`),
   `DisplayName`, `Realm` (the `Realm` enum int), `SpawnPoint` (`Vector3` — where the player
   appears on entry, Phase 25C), `Bounds` (`AABB`), `DefaultWeatherId` + `DayPhaseBias`,
   `Neighbours` (`Array[String]` of region ids), and `Cells` — an array of `RegionCellResource`
   sub-resources (each: `Id` `<region>.<cell>`, `ScenePath`, `Center` `Vector3`, `LoadRadius`).
   Place each cell scene at `scenes/regions/<region>/<cell>.tscn`, built at local origin (the
   streamer positions the instance at `Center`); see `docs/ARCHITECTURE.md` §2.6h-2.
   **Navmesh (Phase 27A):** wrap the cell's walkable geometry in a `NavigationRegion3D` "Nav" with a
   `NavigationMesh` whose `geometry_parsed_geometry_type = 1` (**static colliders** — never visual
   meshes; runtime mesh parsing forces a GPU→CPU readback hitch), and add a `CellNavBaker`
   (`src/World/CellNavBaker.cs`) as its child so the navmesh **bakes at stream-in**. Give the cell a
   floor `StaticBody3D`+`CollisionShape3D` (the bake's walkable surface) and a collider on every
   obstacle (they carve the mesh). Keep `agent_*` dims on the 0.25 voxel grid (`agent_height = 1.75`,
   `agent_max_climb = 0.5`) to avoid precision warnings. Enemy `NavigationAgent3D`s path on it
   automatically; with no Nav region they fall back to straight-line steering, so a navmesh is
   optional per cell but expected for any space enemies fight in.
2. Auto-indexed by `RegionDatabase`; the save header resolves the active region's name, and the
   `RegionStreamer` instances **every** one of the `Cells` on entering the region and keeps them all
   resident (a per-frame budget, no distance test — 38M2 deleted the `LoadRadius` rule, the
   hysteresis and the field, so a cell is authored with a `Center` and nothing else). The
   `ContentValidator` checks neighbours, default weather, and that each cell `ScenePath` resolves.
   No code change for a new region.
   **Adding a cell to an existing region (Phase 38K)** is the same `.tres` sub-resource plus a
   `.tscn`, and four things are worth doing deliberately:
   - ⚠️ **Work out where the floors meet on paper.** The Embermarket's 52 m square abuts the hub's
     60 m one exactly (hub floor ends at `z = 20`; a 52-wide floor centred at `z = 46` starts there).
     A gap is a hole the player falls through and an overlap is two coplanar floors z-fighting along
     a seam — neither is visible from the `.tres`, so the arithmetic goes in a comment beside it.
   - **`SafeRadius` (38K) makes a cell its own no-spawn area.** `0` — the default — means it is not
     one. A settlement can be more than one cell, and stretching the region's single
     `SafeZoneRadius` to reach a district a street away also smothers the encounters around the
     wilds. Author overlapping bubbles so there is no unprotected strip of road between them.
     `SafeZones` holds a list now; `GameBootstrap.ApplySafeZones` rebuilds it, and ⚠️ `SafeZones.Set`
     **replaces** and must be called before the per-cell `Add`s, or a region transition leaves the
     previous realm's districts protecting empty ground here.
   - **A travel node's `TravelName` is a locale key now.** `TravelNodeComponent` resolves it through
     `Loc.T`, and because `Loc.T` returns a plain string unchanged, the Phase 25 waystone's authored
     English still renders and needed no migration.
   - `--validate` rejects a cell whose scene **exists but does not parse** (a hand-authored `.tscn`
     with a syntax error — note it does *not* catch a missing `ext_resource`, which Godot tolerates
     and loads anyway), a negative `SafeRadius`, and two cells sharing an `Id` (the streamer keys its
     loaded set by id, so one can never be instanced).
   - ⚠️ There is deliberately **no** "every cell declares a `NavigationRegion3D`" rule. 38K wrote one
     and deleted it the same hour: a text scan cannot see through scene inheritance, so the three
     Frostfang roosts — which inherit their `Nav` from `RoostCell` — all reported as unnavigable, and
     the glacier legitimately has none because it is scenery. A check that is wrong three times out
     of four teaches authors to ignore the validator.
3. **Hard transitions (Phase 25C):** declaring a region in another's `Neighbours` makes the
   bootstrap spawn a travel portal between them automatically (a `RegionTransitionComponent` a few
   metres in front of the region's `SpawnPoint`, or at **`RegionResource.PortalPoint`** when the
   region authors one — 38M2, so a region with a gate can put its door at the gate).
   ⚠️ One `PortalPoint` per region, so a region with two neighbours would stack both portals on it.
   Fine for two regions; a third makes this per-neighbour. Stepping through (or `region goto <id>` in F1) publishes a
   `RegionTransitionRequestedEvent`; the bootstrap shows the `LoadingScreen`, re-targets the
   streamer (`UnloadAll` + `Configure`), teleports the player to the destination's `SpawnPoint`, and
   autosaves the boundary. Reciprocal links give a two-way door. No code change for a new transition.

## A new encounter

1. Author `data/encounters/Xxx.tres` (`script_class="EncounterResource"`): unique `Id`,
   `EnemyTemplateId`, `MinCount`/`MaxCount`, `SelectionWeight`, the `At{Dawn,Day,Dusk,
   Night}` allow flags, `CorruptionChance` (0..1, Phase 34F — see below), and `RegionIds`
   (Phase 34.5B — `Array[String]` of `region.*` ids; **empty means anywhere**). Author
   `RegionIds` whenever the creature belongs to one realm, or it rolls in every region: that
   is how frost stalkers ended up prowling the Ember Crown for two phases. A misspelled id
   narrows the encounter to *nowhere* and `--validate` is the only thing that catches it.
2. Auto-indexed by `EncounterDatabase`; the `EncounterDirector` spawns it around the player
   when its day phase is active, resolving `EnemyTemplateId` through `EnemyTemplateRegistry`
   — so any registered archetype works, not just the goblin (Phase 34B). No code change.

**A corrupted (Ashen) variant of an existing creature** (Phase 34F)
1. **Don't author a new archetype for it.** Set `CorruptionChance` on an encounter and each enemy
   it spawns rolls to rise Ashen: `AshenAffliction.Afflict` adds named `"ashen"` stat modifiers,
   scales XP, prefixes the nameplate via `enemy.ashen_prefix`, and chars the body with the same
   ash/ember colours `CorruptionAppearanceController` uses on the player. An "Ashen Wolf" authored
   as its own `.tres` is a copy of `Wolf.tres` that drifts the moment either is tuned.
2. Corruption is a property of the **place**, not the player — LORE attributes it to Morthul and
   the realm. Author the chance per encounter; Phase 44.5's realm decay tier can drive it later.
3. Reach for a real archetype only when the creature is more than a tinted, tougher base — a
   different AI profile, spell loadout or faction (see `enemy.ash_maw`, `enemy.cinder_thrall`).
4. If you extend the affliction: never change `TemplateId` (quest kill objectives match on it), and
   always `Duplicate()` a material before tinting it or the change writes through to every other
   instance sharing that imported resource.

## A new world event

1. Author `data/world_events/Xxx.tres` (`script_class="WorldEventResource"`): unique `Id`,
   `Kind` (`0`=Raid / `1`=Cache / `2`=Hunt), `SelectionWeight`, `CooldownSeconds`,
   `TimeLimitSeconds`, `RegionIds` (Phase 35G — `Array[String]` of `region.*` ids;
   **empty means anywhere**, exactly as for encounters, so author it whenever the event
   belongs to one realm or it rolls in every region — that is how goblin raids reached
   Frostfang Reach), the `At{Dawn,Day,Dusk,Night}` flags, spawn knobs (enemy `MinCount`/
   `MaxCount` + `HealthMultiplier` — a Hunt champion is just a count of 1 and a multiplier,
   not a second archetype, or `CacheItemId`/`CacheQuantity`), and rewards
   (`XpReward`, `GoldReward`, `RewardItemId`/`RewardItemQuantity`, `FactionRewardId`/
   `FactionRewardAmount`).
2. Auto-indexed by `WorldEventDatabase`; the `WorldEventDirector` rolls and runs it (announce →
   track → reward). New Raid/Cache/Hunt events need no code; a genuinely new behaviour is a new
   `WorldEventKind` + a branch in the director's start/track switch.

## A new crafting recipe

1. Author `data/recipes/Xxx.tres` (`script_class="CraftingRecipeResource"`): unique `Id`,
   `Station` (`0`=Hand / `1`=Forge / `2`=Workbench / `3`=Alchemy / `4`=Cooking), an
   `Ingredients` array of `RecipeIngredient` sub-resources (`ItemId` + `Quantity`, same
   sub-resource `.tres` pattern as `LootEntry`), `OutputItemId`/`OutputQuantity`, and
   `OutputRarity` (`0`=Common plain; higher rolls affixes for an equippable output).
2. Auto-indexed by `RecipeDatabase`. The player learns it by id (seed via
   `CraftingComponent.StartingRecipeIds` in `PlayerFactory`, or call `Learn`); it then appears
   at a matching `CraftingStationComponent`. New stations: `CraftingStationFactory.Create(...)`
   in the bootstrap. No code change for new recipes.
3. ⚠️ **A recipe must be reachable by one of exactly two paths, and `--validate` checks the union.**
   Either seed it in `GameIds.Recipes.Starting` (what `PlayerFactory` grants every new character), or
   have a `ServiceKind.Trainer` teach it via `ServiceResource.TaughtRecipeIds` (Phase 38D, the first
   caller `CraftingComponent.Learn` ever had — before that the array was the whole of reachability and
   `recipe.leather_vest` rotted unreachable from Phase 15 to Phase 35).
   ⚠️ **Never both.** `PlayerFactory` seeds `Starting` unconditionally, so a recipe in both lists is a
   trainer charging for knowledge the player walked in with; the validator rejects that too.
   A late-game recipe now has a real choice of gate: **taught** (`recipe.drakescale_mail` is bought
   from the Ember Crown smithing lesson) or a **scarce ingredient** (the same mail still needs eight
   dragon scales that only Frostfang's dragonkin drop). Before 38D only the second existed, which is
   *why* it was gated that way.

## A new spell

1. Author `data/spells/Xxx.tres` (`script_class="SpellResource"`): unique `Id`, `School`
   (a `DamageType`), `Delivery` (`0`=Projectile / `1`=Area / `2`=Self), `ManaCost`,
   `Cooldown`, `BaseDamage`, `Healing` (Self), an optional `StatusEffectId`, and the
   delivery knobs (`Range`/`ProjectileSpeed` for projectiles, `ImpactRadius` for an AoE
   burst — a Projectile with `ImpactRadius > 0` detonates as an area on impact).
2. Auto-indexed by `SpellDatabase`. Add the id to a `SpellcastingComponent.KnownSpellIds`
   (the player's is set in `PlayerFactory`); cast with `Q`, cycle with `F`. No code change.
3. ⚠️ **The spellbook lists every spell in the database**, so an enemy's spell appears in the
   player's character screen as purchasable unless you set `PlayerLearnable = false`
   (Phase 34D). Set it on any spell authored for a monster loadout.

## A new status effect

1. Author `data/status_effects/Xxx.tres` (`script_class="StatusEffectResource"`): unique
   `Id`, `School`, `Duration`, optional DoT (`DamagePerTick`/`TickInterval`) and one stat
   modifier (`ModStat`/`ModType`/`ModValue`, e.g. `MoveSpeed` PercentMult `-0.5` = a slow),
   and `IsBeneficial` for buffs.
2. Auto-indexed by `StatusEffectDatabase`. Reference it from a spell's `StatusEffectId`; it
   applies to whoever the spell hits (or the caster, for a Self cast) via the target's
   `StatusEffectsComponent`. No code change.

## A new faction

1. Author `data/factions/Xxx.tres` (`script_class="FactionResource"`): unique `Id`,
   `DefaultReputation`, `HostileThreshold` (a `ReputationTier` int, `2`=Unfriendly),
   `KillReputationPenalty`, and `Enemies`/`Allies` (`Array[String]([...])` of faction ids).
2. Auto-indexed by `FactionDatabase`; the player's `ReputationComponent` seeds a standing for
   it automatically. Tag actors with a `FactionComponent { FactionId = "..." }` (see
   `EnemyFactory` / the elder in the bootstrap) — enemy AI then keys aggression off the
   player's standing with that faction. No code change.

## A new stat

1. Add to the `StatType` enum (**append only** — ordinals persist in `.tres`/saves); if it's a
   depleting resource, update `StatTypes.IsResource`.
2. Add an exported field + mapping in `AttributeSet` (`ToBaseValues`).
3. Add a `Loc` key in `StatNames.Key` + `strings.csv`. **Not optional** —
   `StatNamesTests.EveryStatType_MapsToADistinctNonFallbackKey` fails on any stat without one.
4. Extend `EnumStabilityTests.StatType_Ordinals` to pin the new ordinal.
5. Use via `StatsComponent.GetValue(StatType.Xxx)`. A stat missing from an `AttributeSet` reads
   `0`, so a new stat is inert for existing content until something authors it.

> Worked example — the Phase 34E resistance family (`FireResist` … `NecroticResist`).
> `CombatMath.Mitigate` routes each `DamageType` through `CombatMath.ResistanceStat` and reuses
> `ArmorMultiplier`, so there is **one** defence curve, and resistance never becomes immunity
> (DESIGN's "no school a trap" rule). Authoring an enemy that shrugs off a school is now pure
> data: set the matching `*Resist` on its `AttributeSet`.

## A new event

1. Add a `readonly record struct XxxEvent(...) : IGameEvent` in the relevant
   `*Events.cs`.
2. `Publish` it where it happens; `Subscribe`/`Unsubscribe` where reacted to.

## A new persistent system

1. Implement `ISaveable` (stable `SaveId`, `Save`/`Load` with a Godot
   `Dictionary`).
2. `SaveManager.Instance.Register(this)` in `OnInitialize`, `Unregister` in
   `OnTeardown`.

## A new input action

1. Add a constant + `Bind(...)` in `GameInput`.
2. Read it via `Godot.Input.IsActionPressed/JustPressed/GetVector`.

**A new sound cue / audio asset** (Phase 31)
1. Pick a cue id by convention: `sfx.*` / `step.*` (positional, SFX bus), `music.*`,
   `amb.*`, `ui.*`, `voice.*` (2D). The prefix alone determines the bus + positional flag via
   the pure `AudioCueRouting` — no per-cue wiring.
2. Register its sound in `AudioLibrary.Build()`: a real asset (`GD.Load<AudioStream>(...)` of a
   CC0/open `.ogg`/`.wav` under `assets/audio/`) if one exists, else a `ProceduralAudio`
   placeholder. An unregistered id plays silence and warns once — never throws.
3. Request it: publish `SoundCueRequestedEvent(id, pos)` / `MusicCueRequestedEvent(id)`, or call
   `ServiceLocator.Get<AudioDirector>().PlayCue(id[, pos])`. No code change to add a cue whose
   prefix already routes.

**A new companion** (Phase 32)
1. Author `data/companions/Xxx.tres` (`script_class="CompanionResource"`): unique `Id`
   (`companion.*`), `NameKey`/`TitleKey` (`Loc` keys — add them to `data/locale/strings.csv`, the
   validator fails without them), the build paths (`AttributesPath`/`WeaponPath`/`ModelPath`),
   `FactionId`, optional `KnownSpellIds` (non-empty ⇒ it gets a `SpellcastingComponent`, i.e. a
   caster companion), the follower envelope (`FollowDistance`/`EngageRadius`/`AttackRange`/
   `LeashRadius`), and the loyalty knobs (`StartingLoyalty`, `LoyaltyQuestReward`,
   `RecruitQuestId`/`LoyaltyQuestId`/`DialogueId`).
2. It is auto-indexed by `CompanionDatabase` and auto-registered in `CompanionRegistry` — **no code
   change**. Recruit it *by id*: a `DialogueChoice` (`Effect` `5`=RecruitCompanion), a quest hook,
   `ServiceLocator.Get<CompanionRoster>().Recruit("companion.x")`, or `companion recruit <id>` in the
   F1 console. The roster spawns the actor into a formation slot, tracks loyalty, persists the party,
   and reconciles it back on load.

**A new enemy archetype — humanoid, beast or undead** (Phase 34B/34C/34D)
1. Author `data/enemies/Xxx.tres` (`script_class="EnemyArchetypeResource"`): unique `Id`
   (`enemy.*`), a `NameKey` authored in `strings.csv`, the build paths (`AttributesPath`,
   `WeaponPath`, `LootTablePath`, optional `ModelPath` — empty falls back to a capsule in
   `PlaceholderTint`), an `AiProfileId` (see above), `FactionId`, and `XpValue`.
   `CapsuleRadius`/`CapsuleHeight` size the body *and* the melee reach — the hitbox scales off
   height against a 1.8 m humanoid reference, so a short quadruped bites at its own scale
   (Phase 34C) with no extra knob to set.
   **To make it a caster** (Phase 34D) three things must line up, and the failure is silent:
   a non-empty `KnownSpellIds` (adds the `SpellcastingComponent`), a standoff `AiProfileId`
   like `ai.caster` (so it kites instead of closing), **and a real `Mana` pool in its
   `AttributeSet`** — spells with no mana means it just stands there, with no warning. Tune
   `ManaRegen` on the archetype for cast pacing. Mark enemy-only spells
   `PlayerLearnable = false` or they show up in the player's spellbook.
2. Auto-indexed by `EnemyArchetypeDatabase`, which registers a builder with
   `EnemyTemplateRegistry`, so `EnemyArchetypeFactory` builds it and encounters/world events/quest
   kill-targets can reference the id immediately. Add a `data/encounters/*.tres` pointing at it to
   make it actually appear in the wilds. No code change — reach for a bespoke factory only when the
   actor is *structurally* different (the boss's phase controller, the acolyte's cast origin), not
   when it just has different numbers.

**A new bestiary entry** (Phase 34G)
1. Author `data/bestiary/Xxx.tres` (`script_class="BestiaryEntryResource"`): `Id` = the **enemy
   template id** it documents, `LoreKey` (authored in `strings.csv` as `enemy.<name>.lore`),
   `Category` (`0` Humanoid / `1` Beast / `2` Undead / `3` Construct / `4` Elemental / `5` Ashen /
   `6` Boss), and `KillsToKnow` — kills before the full page opens (`1` for a boss you fight once,
   so it skips the Sighted stage). Leave `NameKey` empty unless the creature has no
   `EnemyArchetypeResource` to take one from.
2. Auto-indexed by `BestiaryDatabase`; the `B` screen picks it up with no code change.
3. ⚠️ **`--validate` checks this domain in both directions.** An entry must name a registered
   template, *and* every registered template must have an entry — so adding an enemy without a
   bestiary page fails the build. That is intentional: it is the guard against content that exists
   but nothing can reach.

**A new enemy AI personality** (Phase 34A)
1. Author `data/ai_profiles/Xxx.tres` (`script_class="AIProfileResource"`): unique `Id`
   (`ai.*`) plus the knobs you want off the defaults — perception (`VisionRange`,
   `FovDegrees`, `AlertRadius`), melee (`AttackRange`, `FlankSpreadDegrees`), standoff
   (`StandoffRange`, `KiteDistance`), guard (`BlockDuration`, `BlockRecovery`), nerve
   (`RetreatHealthFraction`, `FleeOnSight`), and `AmbushRange`.
2. Auto-indexed by `AIProfileDatabase`. Point a factory's
   `EnemyAIComponent { ProfileId = "ai.xxx" }` at it. No code change — the behaviours are
   branches in the one brain, gated on these numbers, so they combine freely (a shielded
   flanking ambusher is just three knobs). A zeroed knob turns its behaviour off; an
   unknown id warns and falls back to `ai.brute`.

## A new dev-console command

1. In `DevCommands.RegisterAll`, `console.Register(new ConsoleCommand(name, usage, summary,
   (console, args) => ...))`. Resolve the player / a world director via the `ServiceLocator`
   (register the director there if it isn't yet), parse `args`, and return a result line.
2. It appears in `help` automatically; reach it in-game with `F1`. For determinism, add a
   scenario to `ReproHarness` (seed + the command sequence) and run it with `repro <name>`.

**Pooling a high-churn node** (perf)
1. Hold a `NodePool<T>` (`src/Core/Pooling`) on the owner; build it in `OnInitialize`
   (`new NodePool<T>(factory, prewarm)`) and `Clear()` it in `OnTeardown`.
2. Make the node reusable: build its children once in `_Ready`, expose a `Launch/Configure`
   to re-arm per use, and on "death" invoke a release callback (the pool's `Return`) instead
   of `QueueFree`. To spawn: `pool.Get()` → `AddChild` → position → `Launch(...)`. See
   `SpellProjectile` + `SpellcastingComponent`. (Throttle/sleep expensive per-frame work by
   distance to the player the way `EnemyAIComponent` does — perception cache + far-sleep.)

## A new UI panel / HUD widget

1. Build it through `UiTheme` (`src/UI/UiTheme.cs`): `UiTheme.Panel()` for the frame,
   `UiTheme.Padding()` inside it, then `UiTheme.Header`/`Body`/`Action`/`Bar` for content —
   don't hand-roll styleboxes/fonts. A modal panel sets `UiState.MenuOpen` + frees the mouse;
   a non-modal overlay (like the journal) does not.
2. Rebuild from a dirty flag in `_Process` (never during a button signal). Add new palette
   colours/builders to `UiTheme` rather than per-panel so the look stays consistent (and the
   Phase 18 overhaul stays a one-file change).

---
