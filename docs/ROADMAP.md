# Development Roadmap

## Scope: this is the *systems* roadmap, not the game

These 20 phases build **base-game infrastructure** — the reusable systems and
engine-on-top-of-Godot that the game runs on (architecture, player, combat, AI,
items/loot, progression, quests, dialogue, magic, world systems, UI, crafting,
factions, events, optimization). They are *capabilities*, not content.

**Completing all 20 phases does not mean the game is finished.** When this roadmap
is done we will have a powerful, well-factored, data-driven sandbox that *can
express* the game — but it will still be a near-empty playground. The actual game
is authored on top of these systems and is explicitly **out of scope here**:

- World: regions, level geometry, points of interest, the map.
- Narrative: story, characters, and the actual quest/dialogue *content*.
- Specific enemies, bosses, items, spells, recipes (beyond sandbox test data).
- Balance, economy, difficulty curves.
- Art, models, animation, audio, music, VFX, UI polish.
- Meta/shell: main menu, settings, new-game and save-slot flow, onboarding.

The architecture is deliberately resource-driven so most of that content lands as
authored `.tres` data against these systems, not new code (item/affix/loot/
attribute resources today; quest/dialogue graphs, spells, recipes later).

A **separate content/production roadmap** (world, story, art, audio, balance, ship
polish) will be introduced once this systems roadmap is complete — that is the
larger body of work that turns the toolkit into a shippable game.

> Caveat: a few phases blur the line — Magic (12) and Crafting (14) need some
> content to be meaningful, and Content Expansion (18) is the seam where systems
> and content begin to overlap (hence ⬜ Ongoing rather than a finite deliverable).

## Phases

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ✅ Done      | First-person CharacterEntity, camera look, code-defined input, locomotion, melee |
| 3  | Combat Framework     | ✅ Done      | Hitbox/hurtbox, damage pipeline (armor/crit), weapons, combos, stamina, stagger |
| 4  | Enemy AI             | ✅ Done      | Perception FSM (idle/patrol/investigate/combat/retreat), coordination, spawner |
| 5  | Inventory System     | ✅ Done      | Item resources + database, stacking inventory, pickups, UI, save |
| 6  | Equipment System     | ✅ Done      | Slots, equippable items, stat bonuses, weapon swap, character UI |
| 7  | Loot Generation      | ✅ Done      | Item instances, procedural affixes, drop tables, loot component |
| 8  | Progression System   | ✅ Done      | XP, levels, per-level stat growth, skill points, perks      |
| 9  | Quest Framework      | ✅ Done      | Event-driven objectives, quest log, rewards, givers, save   |
| 10 | Dialogue System      | ✅ Done      | Node-graph conversations, choices, conditions/effects, story flags |
| 11 | NPC Schedules        | ✅ Done      | World clock, data-driven daily routines, event-driven reactions |
| 12 | Magic System         | ✅ Done      | Schools, projectiles, AoE, status effects                   |
| 13 | World Systems        | ✅ Done      | Day/night, weather, encounters                              |
| 14 | HUD & Panels Polish  | ✅ Done      | Shared UI theme, vitals bars, crosshair, framed panels      |
| 15 | Crafting             | ⏳ Next      | Recipes, stations, materials                                |
| 16 | Faction Systems      | ⬜ Planned   | Reputation, consequences                                    |
| 17 | Procedural Events    | ⬜ Planned   | World events, dynamic spawns                                |
| 18 | Game UI Overhaul     | ⬜ Planned   | Real game UI: HUD, menus, tooltips, scenes over the debug overlay |
| 19 | Optimization         | ⬜ Ongoing   | Pooling, LOD, streaming                                     |
| 20 | Content Expansion    | ⬜ Ongoing   | Regions, enemies, quests via data                           |

## Phase 1 — delivered

Core architecture foundation that everything else builds on:

- **EventBus** — typed pub/sub decoupling all systems.
- **ServiceLocator** — registry for world-scoped systems.
- **GameManager / GameState** — top-level flow machine with pause handling.
- **Entity + EntityComponent** — composition-based actor framework.
- **Stats** — `Stat`/`StatModifier`/`AttributeSet`/`StatsComponent` with the
  full modifier pipeline and resource (HP/STA/MP) handling.
- **Save** — `ISaveable` + `SaveManager` writing versioned JSON to `user://`.
- **Sandbox** — `GameBootstrap` + `DebugHud`: a runnable scene that damages,
  heals, kills, respawns, saves, and loads a component-based entity.

## Phase 2 — delivered (Player Controller)

- Generalized the actor framework: `IEntity` interface + shared `EntityNode`
  helpers, so kinematic `CharacterEntity` (CharacterBody3D) and static `Entity`
  (Node3D) are both first-class component hosts. Events now carry `IEntity`.
- `GameInput`: input actions defined in code (WASD, jump, sprint, interact,
  attack, cast, pause) — type-checked, no fragile `project.godot` input block.
- `LocomotionComponent`: reusable ground motor (gravity, accel, jump,
  `MoveAndSlide`) driven by the `MoveSpeed` stat; AI will reuse it.
- `PlayerController`: first-person mouse-look (body yaw + camera pitch), drives
  locomotion, and a melee raycast that feeds the Phase 1 damage pipeline.
- `PlayerFactory`: assembles the player (collision, stats, camera, components).
- Sandbox: player walks the world and can melee the dummy to death; floor and
  dummy now have physics colliders.

## Phase 3 — delivered (Combat Framework)

- `DamageType` + `DamagePacket`/`DamageResult` value types and `CombatMath`
  (attacker-side crit roll & power scaling; defender-side armor mitigation).
- `Hitbox`/`Hurtbox` `Area3D` components on dedicated collision layers
  (`CombatLayers`); hitboxes poll overlaps during their active window and hit
  each target once.
- `CombatComponent`: poise/stagger, blocking, and the defender damage pipeline
  feeding `StatsComponent`; raises `DamageDealtEvent`/`EntityStaggeredEvent`.
- `WeaponResource` (resource-driven) + `MeleeWeaponComponent`: wind-up/active/
  recovery state machine with combos, finishers, stamina cost and attack-speed
  scaling.
- Player wields an Iron Sword (LMB attack, RMB block); the dummy has a hurtbox
  and combat component. `StatsComponent` gained passive stamina/mana regen.

## Phase 4 — delivered (Enemy AI)

- `EnemyEntity` (CharacterEntity) + `PlayerCharacter` marker type so enemies can
  resolve the player distinctly via the `ServiceLocator`.
- `EnemyAIComponent`: an Idle → Patrol → Investigate → Combat → Retreat → Dead
  state machine that reuses `LocomotionComponent` to move and
  `MeleeWeaponComponent` to attack — the same systems the player uses.
- Perception: vision range + FOV cone gated by a line-of-sight raycast, plus a
  short-range proximity sense; tracks a last-known position for investigation.
- Group coordination: spotting the player broadcasts `EnemyAlertedEvent`, pulling
  nearby idle/patrolling allies to investigate.
- Friendly fire prevented via a `Team` on `CombatComponent` honored by hitboxes.
- `EnemyFactory` + `EnemySpawnDirector` maintain a goblin camp population; dead
  enemies despawn and are replaced.
- Player can now be killed by enemies and respawns at the start.

## Phase 5 — delivered (Inventory System)

- `ItemResource` (`.tres`-driven: id, name, type, rarity, stack size, weight,
  value) + `ItemDatabase` that indexes `data/items/` by id for save/loot lookup.
- `ItemStack` runtime quantities; `InventoryComponent` (slot-based, stacking,
  add/remove/count, weight tracking) implementing `ISaveable`.
- `InteractableComponent` interaction base + raycast `interact` in the player
  controller; `ItemPickupComponent`/`ItemPickupFactory` for world pickups.
- `InventoryPanel` UI (toggle with `I`); goblins drop hide/gold on death.

## Phase 6 — delivered (Equipment System)

- `EquipmentSlot` enum + `EquippableItemResource` (slot, flat stat bonuses, optional
  `WeaponResource`) layered over `ItemResource`.
- `EquipmentComponent`: equip/unequip moves items to/from the `InventoryComponent`,
  applies stat bonuses as `StatModifier`s sourced to the item, and swaps the active
  weapon on the `MeleeWeaponComponent` (restoring the baseline on unequip).
  Implements `ISaveable`.
- The character screen (`InventoryPanel`) now shows equipment slots and the backpack
  with Equip/Unequip buttons; opening it frees the mouse (`UiState.MenuOpen`).
- Gear pickups in the sandbox: steel sword, leather cap/vest, hunter's ring.

## Phase 7 — delivered (Loot Generation)

- **`ItemInstance`** — an item-instance layer over `ItemResource` carrying a rolled
  `ItemRarity`, a generated display name (prefix + base + suffix), and a frozen list
  of `ItemAffix`es. Mundane items are plain instances (`ItemInstance.Plain`); only
  affix-less instances stack, so rolled gear is unique. `ItemStack` now holds an
  instance; inventory, equipment, pickups, UI and save/load all flow instances.
- **Affixes** — `AffixDefinition` (`[GlobalClass]` `.tres` under `data/affixes/`)
  declares a stat, value range, minimum rarity, gear-family applicability and weight.
  `AffixDatabase` indexes them and queries the eligible pool per equippable+rarity;
  a rolled `ItemAffix` maps onto a `StatModifier` sourced to its instance.
- **Drop tables** — `LootTable` + `LootEntry` (`[GlobalClass]` `.tres` under
  `data/loot/`) describe independent per-entry drop chances, quantities, an
  optional gold roll and a quality bias. `LootGenerator` rolls a table into
  `LootDrop`s: it picks rarity (`LootRarity`, quality-weighted), draws distinct
  affixes by weight, and rolls each value scaled by rarity/quality.
- **`LootComponent`** — on death, an actor rolls its `LootTable` and spawns a world
  pickup per drop, scattered around the corpse. Goblins now loot from
  `data/loot/GoblinLoot.tres` (hide/ore/potion/affixed sword + gold), replacing the
  hard-coded goblin-hide drop.
- Equipped instances apply template flats **and** rolled affixes to stats; the
  inventory/equipment screens show rarity colours and per-affix bonus lines; all of
  it round-trips through save/load (instances persist id + rarity + name + affixes).
- The sandbox seeds one procedurally-rolled Rare blade so the pipeline is visible
  the moment you press Play.

## Phase 8 — delivered (Progression System)

- **Kill attribution** — `EntityDiedEvent` now carries the optional `Killer`;
  `StatsComponent.ApplyDamage(amount, source)` threads the attacker (from
  `DamagePacket.Source`) into the death event so kills can be credited.
- **`ProgressionResource`** (`[GlobalClass]`, `data/progression/*.tres`) — the XP
  curve (`BaseXpToLevel × level^exponent`), level cap, per-level flat stat gains and
  skill points per level. **`ExperienceComponent`** — a passive XP bounty on
  enemies (goblins grant 25).
- **`ProgressionComponent`** (`ISaveable`) — listens for `EntityDiedEvent`s it
  caused, awards the dead entity's XP, resolves multi-level-ups against the curve,
  re-derives cumulative per-level stat growth as `StatModifier`s sourced to itself,
  refills resources on level-up, and banks skill points. Persists level / XP /
  unspent points (growth is recomputed from level, never stored).
- **Perks** — `PerkResource` (`[GlobalClass]`, `data/perks/*.tres`) + `PerkDatabase`
  + **`PerksComponent`** (`ISaveable`): rankable passives that spend skill points and
  apply a stat bonus as a `StatModifier` sourced to the perk; ranks persist and
  re-apply on load. Five starter perks (Toughness, Might, Precision, Endurance
  Training, Warding).
- **UI** — the HUD shows `Level / XP / SP`; the character screen (`I`) shows
  progression and a PERKS section with Learn buttons. Raises `XpGainedEvent` /
  `LeveledUpEvent` / `PerkChangedEvent`. Debug key **`X`** grants 50 XP.

## Phase 9 — delivered (Quest Framework)

- **`IEntity.TemplateId`** — lifted onto the interface so quests can match a slain
  actor to its archetype id (`Entity`/`CharacterEntity` already exposed it).
- **Quest content** — `QuestResource` (`[GlobalClass]`, `data/quests/*.tres`) with
  `ObjectiveResource` (Kill / Collect, target id, required count) and
  `QuestItemReward` sub-resources, XP/gold rewards and an optional
  `PrerequisiteQuestId` for chaining. `QuestDatabase` indexes them (the standard
  static-database pattern). Objective/reward arrays are authored untyped and read back
  by element cast (as `LootTable.Entries`).
- **`QuestLogComponent`** (`ISaveable`, on the player) — holds `QuestProgress` per
  quest, advances objectives from `EntityDiedEvent` (kills it caused) and
  `ItemPickedUpEvent`, and on completion grants rewards through the sibling
  `ProgressionComponent` (XP) and `InventoryComponent` (gold + items). Raises
  `QuestStartedEvent` / `QuestObjectiveAdvancedEvent` / `QuestCompletedEvent`; persists
  the full log.
- **`QuestGiverComponent`** (`InteractableComponent`) — an NPC offers a quest via the
  existing `E` raycast interact, honouring prerequisites and in-progress/completed state.
- **UI** — `QuestLogPanel`, a non-modal read-only overlay toggled with `J`, lists
  active quests with per-objective progress and a completed section; the HUD shows a
  compact active-quest tracker.
- **Sandbox** — "Cull the Goblins" (kill 3 goblins) auto-starts on Play; a Village
  Elder near spawn offers "Gather Iron" (collect 3 iron ore). Both round-trip
  through save/load.

## Phase 10 — delivered (Dialogue System)

- **Dialogue content** — a node-graph conversation as `.tres` under `data/dialogue/`:
  `DialogueResource` (`[GlobalClass]`: id, speaker, start node, nodes) holds
  `DialogueNode` sub-resources (id, speaker override, line text, choices), each with
  `DialogueChoice` sub-resources (reply text, `Goto` target node, a gating
  `DialogueCondition` and a fired `DialogueEffect`). Node/choice arrays are authored
  untyped and read back by element cast (the same pattern as quests/loot).
  `DialogueDatabase` indexes them by id (the standard static-database pattern). New
  conversation = a `.tres`, no code change.
- **Conditions & effects (declarative, no scripting)** — a choice is hidden unless its
  `DialogueCondition` passes (`QuestAvailable` / `QuestActive` / `QuestCompleted` /
  `QuestNotStarted` / `HasFlag` / `MissingFlag`), and picking it fires a
  `DialogueEffect` (`StartQuest`, `SetFlag`, `ClearFlag`). `DialogueSession` — a plain
  runtime walk of a conversation — evaluates conditions and applies effects against the
  player's `QuestLogComponent` and `StoryFlagsComponent`, keeping the UI a thin view.
- **`StoryFlagsComponent`** (`ISaveable`, on the player) — a persistent set of named
  boolean flags giving conversations memory ("you've met the elder"). Deliberately
  general so later systems (NPC schedules, world events) can read/write the same flags;
  raises `StoryFlagChangedEvent` and round-trips through save/load.
- **`DialogueComponent`** (`InteractableComponent`) — an NPC that opens a conversation
  on the player's `E` interact by publishing a `DialogueStartedEvent`. This replaces the
  bare quest-giver: offering a quest is now just a choice effect inside a conversation.
- **UI** — `DialoguePanel`, a modal window driven entirely by `DialogueStartedEvent`:
  it builds a session, renders the speaker line and condition-filtered choice buttons,
  applies effects on click and advances, and (like the character screen) frees the mouse
  and sets `UiState.MenuOpen` while open. Rebuilds from a dirty flag so a choice never
  frees its own button mid-signal.
- **Sandbox** — the Village Elder near spawn now *talks*: his conversation offers
  "Gather Iron" (a `StartQuest` choice gated on `QuestAvailable`), branches on the
  quest's state while it's active/completed, and on a thank-you sets the
  `flag.elder_thanked` story flag — which unlocks a friendlier greeting on later visits.
  Flags persist across save/load.

## Phase 11 — delivered (NPC Schedules)

- **World clock** — `WorldClock` (`Node`, `ISaveable`, `ServiceLocator`-registered) advances
  a 24-hour day at a configurable real-time rate and announces each new hour via
  `TimeOfDayChangedEvent(Hour, DayPhase)`. It is the minimal time source schedules need;
  **Phase 13 (World Systems)** will build the fuller day/night + weather model on top. The
  time of day persists through save/load, so routines resume where they left off. A
  `DayPhase` (Night/Dawn/Day/Dusk) is derived from the hour; the HUD shows the clock.
- **Schedule content** — `ScheduleResource` (`[GlobalClass]`, `data/schedules/*.tres`) holds
  `ScheduleEntry` sub-resources (`StartHour`, `Activity` label, `Destination`). Entries are
  authored untyped and read by element cast (the established pattern); `EntryForHour(hour)`
  resolves the active block (wrapping pre-dawn hours to the last block). `ScheduleDatabase`
  indexes them. New routine = a `.tres`, no code change.
- **`ScheduleComponent`** (`EntityComponent`, on a static NPC `Entity`) — reads the clock,
  walks the host toward the current block's destination with a simple kinematic step
  (villagers need no physics), facing where it goes, and reports its `Activity` via
  `NpcActivityChangedEvent`. **Reactions** are event-driven: a nearby `EnemyAlertedEvent`
  triggers a timed flee away from the threat (overriding the routine), and a
  `DialogueStartedEvent` where it is the speaker freezes it to face the player until the
  `DialogueEndedEvent`.
- **Sandbox** — the Village Elder now keeps a daily routine: the well at dawn, the forge by
  day, the square at midday, home by dusk, asleep at night — visibly walking between them as
  the (fast) clock turns. Lure goblins near and he flees; talk to him and he stops to face
  you. The clock's time of day round-trips through save/load.

## Phase 12 — delivered (Magic System)

- **Spell content** — `SpellResource` (`[GlobalClass]`, `data/spells/*.tres`) is the data a
  cast needs: a `School` (its `DamageType`, so spells flow straight through the existing
  mitigation pipeline and tint via `SpellSchools`), a `Delivery` shape
  (`Projectile`/`Area`/`Self`), mana cost, cooldown, base damage, healing, an optional applied
  status effect id, and projectile/range/impact-radius fields. `SpellDatabase` indexes them
  (the standard static-database pattern). New spell = a `.tres`, no code change.
- **Status effects** — `StatusEffectResource` (`[GlobalClass]`, `data/status_effects/*.tres`)
  is a timed condition that can deal damage-over-time (`DamagePerTick`/`TickInterval`) and/or
  apply one stat modifier (`ModStat`/`ModType`/`ModValue`) — covering burns, chills/slows and
  buffs alike. `StatusEffectDatabase` indexes them. **`StatusEffectsComponent`**
  (`EntityComponent`, on every combatant) ticks active effects: DoT applies through the
  `StatsComponent` credited to the caster (so DoT kills still attribute for progression/quests),
  and stat modifiers are pushed/pulled as `StatModifier`s sourced to the effect instance.
  Re-applying refreshes duration; effects are transient (not persisted, like poise/stagger).
- **`SpellcastingComponent`** (`EntityComponent`, `ISaveable`) — the magic analogue of
  `MeleeWeaponComponent`: known spells, the prepared index, per-spell cooldowns, mana spend and
  the cast itself. Delivery is resource-driven — a `SpellProjectile` fired along the caster's
  aim, an instant `SpellResolver.Detonate` burst around the caster, or a self heal/buff. It is
  input-agnostic and reusable by any actor. Known spells + the prepared index persist through
  save/load; cooldowns are transient.
- **Delivery** — `SpellProjectile` (`Area3D` on the Hitbox layer, the moving analogue of a melee
  `Hitbox`) flies forward and resolves on the first enemy hurtbox, world contact or end of range;
  `SpellResolver` applies single-target or radial (sphere-query) damage + status using the same
  friendly-fire rules as hitboxes, and spawns a short `SpellFlash` to make bursts legible.
  Spells scale off `SpellPower` via the new `CombatMath.RollSpell` (the mirror of `RollAttack`).
- **UI & input** — `Q` casts the prepared spell, `F` cycles it; the HUD shows mana, the prepared
  spell with its cooldown/mana state, and active status effects on the player and target. Events
  live in `src/Magic/MagicEvents.cs` (`SpellCastEvent`/`SpellSelectedEvent`/`StatusEffect*Event`).
- **Sandbox** — the player starts knowing five spells: **Firebolt** (single-target bolt + Burning
  DoT), **Fireball** (projectile that detonates as a burning AoE), **Frost Nova** (an instant
  burst around the caster that chills/slows), **Lesser Heal** (self heal) and **Arcane Shield**
  (a self buff warding +Armor). Goblins and the training dummy carry a `StatusEffectsComponent`
  so the burns and slows visibly land. The spellbook round-trips through save/load.

## Phase 13 — delivered (World Systems)

Builds the fuller day/night + weather + dynamic-spawn model on top of the Phase 11
`WorldClock`, all reacting through the `EventBus` and persisting the world's state.

- **Day/night atmosphere** — `SkyController` (`Node3D`) animates the scene's directional
  "sun" light and `Environment` from the clock's *continuous* `TimeOfDay`: it sweeps the
  sun east→west, dips it toward the horizon and warms its colour at dawn/dusk, scales its
  energy by a day factor, and darkens the sky/ambient at night (via
  `BackgroundEnergyMultiplier`). The sun + environment are built by the bootstrap and
  injected, so the controller only animates what already exists.
- **Weather** — `WeatherResource` (`[GlobalClass]`, `data/weather/*.tres`) authors each
  state's duration, selection weight, light/sky dimming, fog and precipitation;
  `WeatherDatabase` indexes them. **`WeatherDirector`** (`Node`, `ISaveable`,
  `ServiceLocator`-registered) holds the active state and a countdown measured in in-game
  hours off the clock, rolls a new weighted state (never the same twice) when it expires,
  and publishes `WeatherChangedEvent`. The `SkyController` blends the atmosphere toward the
  active state (light/sky/fog) and drives a rain `GpuParticles3D` that follows the player;
  the weather (and time-to-change) round-trips through save/load. Five states ship: Clear,
  Cloudy, Rain, Storm, Fog.
- **Encounters** — `EncounterResource` (`[GlobalClass]`, `data/encounters/*.tres`) authors a
  weighted, day-phase-gated enemy group; `EncounterDatabase` indexes them.
  **`EncounterDirector`** (`Node3D`) spawns them around the player on a cadence the world
  bends — more often at night and during storms — capped by a concurrent budget and tracked
  via `TreeExited`, reusing `EnemyFactory` and the existing death/loot/XP flow. It publishes
  `EncounterTriggeredEvent`. Emergent and transient (not persisted), like the static camp.
  This is deliberately lightweight; the richer named-**world-event** framework (objectives,
  rewards) is Phase 16.
- **UI** — the HUD now shows the current weather alongside the clock; the sandbox visibly
  cycles dawn→day→dusk→night with shifting light while weather rolls over and patrols/warbands
  wander in. Time and weather both survive save/load.

## Phase 14 — delivered (HUD & Panels Polish)

A focused pass over the existing debug-grade overlay UI — not the full game UI (that is
Phase 18), but a real, consistent improvement to what's on screen today.

- **Shared theme** — `UiTheme` (`src/UI/UiTheme.cs`) centralises the overlay's look: a
  palette (panel bg/border, gold accent, body/dim text, good/bad, per-resource fills) plus
  builders for a framed rounded `PanelContainer`, padded containers, accent headers, body
  lines, styled action buttons and coloured resource `ProgressBar`s. Every panel now routes
  its controls through it, so the HUD, character screen, journal and dialogue window share
  one style and a later full UI pass is a single-file change.
- **HUD rebuild** — `DebugHud` is reorganised into framed panels: a vitals panel with live
  **coloured HP/Stamina/Mana bars** (replacing the old ASCII bars) plus level/spell/effects/
  quest text, a target panel (with its own HP bar) that shows/hides with the current target,
  and a bottom controls hint. A screen-centre **crosshair** (`Crosshair`) marks the aim point;
  HUD panels ignore the mouse so they never eat menu clicks.
- **Panel polish** — the character screen, quest journal and dialogue window adopt the framed
  theme, accent headers and styled buttons; the character screen gained a **scroll area** so a
  full backpack + perk list never runs off-screen. All existing behaviour (toggles, dirty-flag
  rebuilds, equip/learn/choice wiring, modality) is unchanged.
