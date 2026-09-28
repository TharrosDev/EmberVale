# Embervale — mechanics catalogue

Every mechanic and core system in the game, one or two lines each, with the class or data folder that
owns it. It says **what exists**; [`ARCHITECTURE.md`](ARCHITECTURE.md) says how it works and
[`RECIPES.md`](RECIPES.md) how to add to it. Counts are `.tres` files at 2026-09-28 (`--state` has the
live census). *Partial* marks something that exists but is incomplete.

## Movement and traversal

- **Walk, sprint, jump** — input-agnostic kinematic motor; speed from the `MoveSpeed` stat.
  `LocomotionComponent` (`src/Movement`).
- **Step-up** — climbs kerbs and steps Godot's `CharacterBody3D` cannot; accept/revert rule.
  `StepUp`. There is no climbing or ledge grab beyond this.
- **Dodge roll** — i-frame window, refused mid-swing, stamina-gated. `DodgeComponent`, `Dodge`.
- **Stamina pacing** — regen pauses while you keep spending, so mashing drains to empty.
  `StaminaPacing` (`src/Stats`).
- **Mounts** — whistle (`Y`) a horse once the stablemaster is paid; gallop is a sustain pool with its
  own pacing; mounted blows scale with gait, not with merely sitting on a horse. `MountComponent`,
  `MountRules`, `MountedCombat`.
- **Wading, not swimming** — declared water is wadeable under 1.1 m and refused deeper; deep water or
  an exitless pit returns you to safe ground. `WorldWater`, `WorldRecovery`, `WorldWaterResource`.
- **Safe placement** — every spawn, landing and respawn waits for real collision and a clear capsule.
  `SafePlacementService`, `LoadingCoordinator`.
- **Foot IK and footsteps** — feet planted on terrain; footstep sound by floor surface and gait.
  `FootIkComponent`, `FootstepComponent`.
- **NaN guard** — a motion vector is checked before it reaches physics. `MotionSafety`.

## Camera

- **First/third person swap** (`V` or the setting, at any time) — true first person rides the head
  bone with the body visible; third person is over the shoulder (side, distance 2–6 m and FOV are live
  settings). `PlayerCameraRig`, `CameraRigMath` (`src/Player`).
- **Camera profiles** — exploration, sprint, combat, target-lock and aim multiply the player's
  settings. `CameraProfile`.
- **Wall spring** — sweeps only world geometry and camera blockers, so actors never yank it.
- **Camera shake** — on heavy hits. `CameraShake`, `ShakeMath` (`src/Combat`).

## Combat

- **One action timeline** — every actor's attacks, casts and shots are `ActionDefinitionResource`s
  with windows as fractions of the clip; one executor. `CharacterActionComponent`, `ActionTimeline`.
- **Melee combos and input buffer** — weapon attack chains; an early press is buffered until the swing
  can cancel; combo links alternate swing direction and the finisher hits harder. `AttackBuffer`.
- **Directional melee (big bodies)** — dragons pick bite, wing or tail by where you stand.
  `DragonMeleeComponent`, `DragonMelee`. Humanoid melee has no player-chosen direction.
- **Motion warp** — closes the last of a committed attack's gap, swept so it never passes a wall.
  `MotionWarp`.
- **Blocking, parry, riposte** — `RMB` blocks with chip damage; a block raised inside the parry
  window negates the hit and staggers the attacker (the riposte opening). `CombatComponent`, `Parry`.
- **Poise and stagger** — symmetric for every actor: flinch, stagger, heavy, knockdown by reaction
  class; a stagger cancels a wind-up, never a live blow; wind-ups can take extra poise damage; bosses
  are never knocked down. `PoiseReaction`, `CombatComponent`.
- **Hit-stop and hit feedback** — weight-scaled freeze frame, mesh lurch toward the blow, weapon
  trails, screen flash per state (crit, block, stagger, parry), damage-direction indicator.
  `HitStopDirector`, `HitReactionComponent`, `WeaponTrailComponent`, `CombatFeedbackOverlay`,
  `DamageDirectionOverlay`.
- **Lock-on** — middle mouse; cycles targets in range; faces the target. `LockOnComponent`, `LockOn`.
- **Ranged** — bows fire sub-stepped arrows along the camera's aim point. `WeaponResource.IsRanged`,
  `Arrow`, `AimController`. 19 weapons (`data/weapons`).
- **Hit zones** — an actor can carry several hurtboxes with their own damage and poise multipliers
  (dragon heads, boss bodies). `Hurtbox`, `HitZoneResource`.
- **Damage and resistances** — Physical (armour), Fire, Frost, Lightning, Arcane, Nature, Necrotic
  (own resistances), True; one mitigation curve, resistance never immunity; crits. `CombatMath`,
  `DamageType`.
- **Telegraphs** — ground rings sized to the real wind-up, tinted by boss phase. `TelegraphComponent`.

## Magic

- **Spells as data** — 19 spells (`data/spells`): school, delivery (projectile, area, self, cone), cast
  mode (instant, charged by hold, channelled), mana, cooldown, status. `SpellResource`,
  `SpellcastingComponent`. `Q` casts, `F` cycles, `T` spellbook.
- **Special deliveries** — homing (Ball Lightning), lingering zones (Blizzard), a healing totem
  (Lifebloom), a gaze-directed teleport (Blink). `SpellHoming`, `SpellZone`, `SpellTotem`.
- **School identities** — Fire stacking ignite, Frost chill to freeze, Lightning chain, Necrotic
  lifesteal, Nature regrowth, Arcane ward and dispel. `SchoolIdentity`.
- **Combos** — Shatter and Thermal Shock read afflictions already on the target. `SpellCombo`.
- **Mastery** — schools rank up with use; spells gain rank damage. `SchoolMasteryComponent`,
  `SpellMastery`.
- **Status effects** — Burning, Chill, Frozen, Decay, Regrowth, Arcane Ward (`data/status_effects`),
  with VFX; transient. `StatusEffectsComponent`.
- **The fading Weave** — each region's `WeavePotency` weakens ordinary magic and strengthens corrupted
  magic as it falls. `Weave`, `WeaveMath`.
- **Corrupted casts** — spells with `MinCorruptionTier` can only be learned at that tier or above.
- **Learning** — recovered from tomes, dialogue (`LearnSpell`) and trainers, never bought off a shelf.
  `SpellTomeComponent`.

## Corruption and the ember choice

- **Corruption meter** 0–100 in five tiers (Untainted, Touched, Marked, Ashbound, Embers).
  `CorruptionComponent`, `CorruptionTier`.
- **Embers** — each fallen Flamebearer but Morthul offers its ember: absorb for +25 corruption and a
  flag, or refuse. `data/dialogue/*Absorb.tres`.
- **What it changes** — body appearance (ash skin, ember glow; a placeholder material pass),
  dialogue gates (`CorruptionAtLeast`/`Below`), spell access, shrine refusal, a global *dread* penalty
  on faction standing, and which ending the throne offers. `CorruptionAppearanceController`,
  `ReputationComponent.Dread`, `EndingPath`.

## Progression and character

- **XP and levels** — kill bounties, level cap 50, one skill point per level, per-level stat growth
  recomputed from level. `ProgressionComponent`, `data/progression`.
- **Perks** — 6 perks (`data/perks`) bought with skill points. `PerksComponent`.
- **Stats** — resources, primaries, derived stats and six resistances with flat/percent modifiers.
  `StatsComponent`, `Stat` (`src/Stats`).
- **Races and character creation** — six playable races (Human, Valari, Sylthari, Grondar, Draekyn,
  Umbral) with stat deltas, innate perks and starting standing; name and race picked at New Game.
  `RaceResource`, `CharacterCreator` (`data/races`). *Partial:* appearance options are data only.

## Items, equipment and loot

- **Items** — 77 (`data/items`): consumables, weapons, armour, materials, quest items, placeables.
  `ItemResource`, `ItemInstance`.
- **Inventory and equipment** — slot stacking with capacity; main hand, off hand, head, chest, hands,
  legs, feet, ring, amulet, ammo; bonuses as stat modifiers; weapons and shields visible on sockets.
  `InventoryComponent`, `EquipmentComponent`, `EquipmentPresentationComponent`.
- **Hotbar** — five consumable slots on `1`–`5`. `HotbarComponent`.
- **Loot and rarity** — Common to Legendary; rarity decides affix count; 19 loot tables; drops scatter
  as floor pickups; chests roll guaranteed legendary gear on first open. `LootGenerator`,
  `LootRarity`, `ContainerLootComponent`.
- **Affixes** — 11 prefixes/suffixes frozen onto each instance. `AffixDatabase` (`data/affixes`).
- **Relics** — six Flamebearer hearts (`item.relic.*`) plus four guild-finale tokens.
- **Pickups** — `E`, or hold `E` to sweep nearby loot. `ItemPickupComponent`, `InteractionSensor`.
- No durability, repair or encumbrance (struck).

## Crafting

- **Recipes at stations** — 15 recipes (`data/recipes`) at hand, forge, workbench or alchemy
  stations; affixed output; learned from trainers. `CraftingComponent`, `CraftingStationComponent`.
- **Salvage** — deconstruct for a fraction of materials plus XP. `Deconstruction`.
- **Commission** — pay a smith to craft for you. `CommissionRules`.

## Economy

- **Shops** — 24 (`data/shops`) with opening hours, travel days, stock, restock, purses and
  specialties; one price authority. `ShopPricing`, `ShopStock`, `ShopHours`, `VendorPanel`.
- **Standing prices and haggling** — faction tier moves buy prices; one haggle per merchant per day.
  `HaggleRules`, `PriceBreakdown`.
- **Regional supply and demand** — surplus and demand tags change local value, so hauling between
  settlements pays; timed supply shocks. `RegionDemand`, `SupplyShockService`.
- **Services** — 16 (`data/services`): trainer, bank, inn (rest), stable, passage, search, redeem,
  collect, appraisal, commission, contracts, mercenary, wager. `ServiceComponent`, `ServiceKind`.
- **Contracts, consignment, wagers** — supply contract boards, a broker selling on consignment, a
  gambling house. `ContractLedger`, `ConsignmentLedger`, `WagerLedger`.
- **Fences and contraband** — wardens impound contraband and fine you. `ContrabandLaw`.
- **Tolls** — gold, a permit flag or a consumed pass at tolled crossings; a bribe service.
  `TollFee`, `RegionResource.Toll*`.
- **Investment** — permanent stakes in a merchant, a late-game gold sink. `ShopInvestmentTier`.
- **Fast-travel fee** — 15 gold within a realm (free when mounted), 40 across realms, free to an
  owned holding, never tolled. `TravelFee`, `TravelCosts`.

## Housing

- **Claimable property** — one holding, the Ashfall cottage in the Ember Crown (`data/properties`);
  claiming adds a fast-travel node. `HousingService`, `PropertyDeedComponent`.
- **Stash, yard placement, trophy stands** — persistent storage, placeable props, one-item display
  stands.
  `PropertyStorageComponent`, `PlacementDirector`, `TrophyStandComponent`.

## Companions

- **Five companions** (`data/companions`: Kael, Tessa, Wren, a Dawnwarden witness, an Iron client) —
  recruit and dismiss through dialogue; orders Follow / Hold / Engage (`C`); leash beats the fight;
  downed, never lost; loyalty per companion. `CompanionRoster`, `CompanionAIComponent`.

## Factions and guilds

- **Reputation** — 13 factions (`data/factions`) with tiers, hostility thresholds, kill penalties and
  an ally/enemy web. `ReputationComponent`, `FactionResource`.
- **Five guilds** — Dawnwardens, Ash Hunters, Veiled Archive, Iron Syndicate, Emberbound: join, rise
  through ranks, reach rank three and a finale with a three-way verdict; realm NPCs react to rank
  three. `GuildRules` (flags derived, never authored).
- **Frostfang clans** — a clan faction with the Clan Hold and a rank chain with a betrayal branch.

## Shrines and blessings

- **Divine shrines** — six gods' shrines (`data/shrines`), each granting a permanent passive once;
  refused above the god's corruption tolerance. `ShrineComponent`, `BlessingComponent`, `BlessingRules`.

## Quests

- **Quests as data** — 41 (`data/quests`), main and side, rewards in XP, gold, items and standing.
  `QuestResource`, `QuestLogComponent`.
- **Objective types** — Kill, Collect, Reach, Talk, Escort, Defend, Interact, Stealth (fails the moment
  an enemy engages). `ObjectiveResource`.
- **Branching** — objectives gated on required/forbidden flags; completion flags drive the world.
- **Timed quests** — `TimeLimitSeconds`; failed quests are retakeable.
- **Auto-start chains** — `AutoStartFlagId` starts a quest the moment a flag is set (how the acts chain).
- **Navigation** — objectives resolve to map locations for the compass and waypoint.
  `ObjectiveNavigation`. Journal `J`; the HUD tracks one quest.

## Dialogue

- **Conversation graphs** — 75 dialogues (`data/dialogue`); choices with one condition and one effect.
  `DialogueResource`, `DialogueSession`.
- **Conditions** — quest state, story flags, corruption, companion recruited/loyalty, shop open/closed,
  guild rank/membership/can-join.
- **Effects** — start quest, set/clear flag, add corruption, recruit/dismiss companion, loyalty, learn
  spell, open shop, open service, join guild, guild rank.
- **Story flags** — named booleans on the player, validated reader against writer.
  `StoryFlagsComponent`.
- **Talking creatures** — an enemy archetype can carry a `DialogueId` (a dragon that talks).

## Enemies and AI

- **Roster as data** — 39 archetypes (`data/enemies`), 19 AI profiles (`data/ai_profiles`); one brain
  with states Idle, Patrol, Investigate, Combat, Retreat, Returning. `EnemyAIComponent`,
  `AIProfileResource`, `EnemyArchetypeResource`.
- **Perception** — sight cone, line of sight, proximity, alerts to allies, provocation memory.
  `EnemySenses`, `EnemyPerception`.
- **Tactics** — pack flanking, guard cycles, territory leash, AI LOD clock. `PackFlank`, `GuardCycle`,
  `TerritoryLeash`, `AiLodClock`.
- **Casters** — standoff banding, kiting, heal, attack, ward priority. `EnemyCasterTactics`.
- **Ashen variants** — corrupted versions rolled at spawn. `AshenAffliction`.
- **Dragons** — flight with take-off and landing, breath channels aimed from the air, hit zones, lairs
  (`LairSpawnComponent`), world-event dragon hoards. `FlightComponent`, `BreathComponent`.
- **Encounters** — 46 groups (`data/encounters`) spawned around the player by region, day phase and
  weather, never in safe zones. `EncounterDirector`.
- **Scaling** — champions and adds get a tagged stat bonus. `EnemyScaling`.
- **Bestiary** — 41 entries (`data/bestiary`), Unseen / Sighted / Known by kills, Ashen kills counted
  (`B`). `BestiaryService`.

## Bosses

- **Boss fights as data** — 12 fights (`data/bosses`): ordered phases entered by health threshold,
  stat escalation, spell grants, AI swaps, telegraph colour, add waves, enrage timer, intro lock and
  defeat slow-mo, reward, defeat flag and conversation. `BossResource`, `BossController`.
- **Braziers** — a boss is summoned from a `BossSummonComponent`, gated on quests and flags.
- **Arena hooks** — arena nodes revealed by phase. `ArenaHookComponent`.
- **Yielding duels** — a fight can end with the boss withdrawing at a health fraction: defeat flag and
  conversation, no kill, loot or bestiary credit. `WithdrawHealthFraction`, `BossWithdrewEvent`,
  `BossSummonComponent.FightId` / `ClosedFlagId`.
- **The seven Flamebearers** — the Iron King (Ember Crown arena), the Storm Tyrant (Frostfang,
  Stormcrown), the Beast Lord (Ashen Wilds), the Crimson Prophet (Sunspire, Crimson Mission), the
  Hollow Queen (Pale Concord, Hollow Court), the Ashen Knight (Celestial, Knight's Gate; also two
  optional Act II duels) and Morthul (the Ash Throne).
- **Dragon world bosses** — dragons placed by lair spawners that remember whether they have fought.
  `LairSpawnComponent`.

## World

- **Region streaming and bake** — six generated regions baked offline into packed cells; streamed by
  predicted distance in Near / Mid / Far / Backdrop tiers. `RegionStreamer`, `tools/world_bake.py`,
  `data/world_bake`.
- **One generated ground per realm** — staged heightfield, drainage, authored landforms, roads and
  yards. `WorldHeightfield`, `WorldGenerationProfileResource` (`data/world_gen`).
- **The six realms:**
  - **Ember Crown** (52 cells) — the human heartland: the capital under the Iron Citadel, market
    towns, the Tarn, the mine, the Iron King's arena.
  - **Frostfang Reach** (36) — alpine clan country: snowfields and glacier, the Clan Hold, three dragon
    territories, the Storm Tyrant at Stormcrown.
  - **Ashen Wilds** (16) — the Cataclysm scar: ash waste, Last Hearth, the Ash Hunters' station, the
    Beast Lord.
  - **Sunspire Dominion** (20) — desert: Saffra Wells, the great library, the Crimson Mission.
  - **Pale Concord** (12, hidden until revealed) — a realm held at dusk: Vesperhold, the Hollow Court.
  - **Celestial Realm** (9) — the ruin of the dead gods: the Knight's Gate, the Ash Throne.
- **Doors and gates** — one door per neighbour; story gates by `UnlockFlagId`; tolled crossings.
  `RegionTransitionComponent`.
- **Fast travel** — attune waystones, pay the fee, land at baked points. `FastTravelService`,
  `TravelNodeComponent`.
- **Map and discovery** — places discovered by walking up, by sight, by quest or flag; 98 map
  locations (`data/map_locations`); world map (`M`) with search and tiers, minimap, compass, waypoint
  beacon. `MapService`, `MapDiscoveryRules`, `MapScreen`, `WaypointBeacon`.
- **Day/night** — 24 h day in 180 s of real time, saved with the day count. `WorldClock`.
- **Weather** — 7 states (`data/weather`), weighted rolls, story-gated after the ending.
  `WeatherDirector`.
- **World events** — one at a time: raids, caches, champion hunts (`data/world_events`).
  `WorldEventDirector`.
- **NPC routines** — 41 schedules (`data/schedules`); NPCs walk routines, flee alerts, face you in
  conversation; outfits by profession and faction. `ScheduleComponent`, `NpcVisualKit`.
- **Actors that leave** — a placed actor disappears once a story flag is set. `FlagVisibilityComponent`.
- **Safe zones** — region and cell bubbles where encounters never spawn. `SafeZones`.

## Vibrancy and presentation

- **Sky and light** — sun, moon, ambient, fog and exposure from an eight-key day cycle. `SkyController`,
  `data/rendering/DayCycle.tres`.
- **Region atmosphere** — per-realm sun tint, energy and haze; a fixed-sky hour (the Pale Concord is
  held at dusk, 17.4) while the clock keeps running. `WorldEnvironmentProfileResource.FixedSkyHour`.
- **Weather presentation** — light, sky, fog and precipitation per state; camera-local rain and snow;
  world wetness and snow accumulation on terrain and scatter; wind on vegetation and water; roof and
  shelter suppression. Shader globals `world_wetness`/`world_snow`/`world_rain`/`world_wind`.
- **Sky tint** — a weather state can tint the whole sky; the Dawnfire ending's warm clear sky and the
  Lord of Embers' red ember sky. `WeatherResource.SkyTint`.
- **Space profiles** — interior, dungeon and underwater looks, plus authored environment volumes.
  `data/rendering/Interior.tres` etc., `EnvironmentVolume`.
- **Biomes and terrain layers** — 12 biomes over 21 painted noise layers (no ground textures), blended
  by slope, altitude, moisture and shore. `WorldBiomeProfileResource` (`data/biomes`),
  `WorldTerrainLayerResource` (`data/terrain_layers`).
- **Scatter ecology** — trees, rocks and plants declare slope, height, clumping and climate bands.
  `WorldBiomeScatter`, `BiomeScatterLayerResource`.
- **HLOD and backdrop** — far cells as reduced-density silhouettes; terrain continued to the horizon.
  `WorldRegionBackdrop`.
- **Practical lights and particles** — distance-faded lights with a shadow budget; ambient particles.
  `EnvironmentEmitters`.
- **Quality tiers** — Low / Medium / High / Ultra (shadows, SSAO, SSIL, volumetric fog, SSR, particle
  scale, render scale). `RenderQualityResource` (`data/rendering`).
- **Corruption appearance** — the player's body shifts with corruption tier (placeholder materials).
  `CorruptionAppearanceController`.
- **Magic colour** — one hue per school for every spell effect. `SpellSchools.Color`.

## Story

- **Prologue** — narration cards after character creation, then the tutorial. `OpeningSequence`,
  `TutorialDirector` (observational hints, never blocking).
- **Four acts** — Act I Awakening (the Iron King), Act II Gathering (Storm Tyrant, Beast Lord, Crimson
  Prophet in any order), Act III Truth (the Archivist's reading), Act IV Celestial War (the Ashen
  Knight, Morthul). `quest.main.*`, `tools/gen_main_story.py`.
- **Act I end card** — stepping through the Frostfang door after the Iron King. `SliceDirector`,
  `ClosingSequence`.
- **Hidden-realm reveal** — the Pale Concord opens once the three Act II Flamebearers fall.
  `HiddenRealmReveal`.
- **Visions** — three cards on how each Flamebearer fell, after its ember conversation.
  `VisionSequence`.
- **Rival duels** — the Ashen Knight tests you at Stormcrown and the Crimson Mission; his last words
  remember each duel won. `dialogue.rival_duel1`/`2`.
- **Endings** — the Ash Throne offers Dawnfire below 40 corruption, Lord of Embers at 60+, either
  between; ending cards, an epilogue by embers absorbed, credits. `EndingSequence`.
- **After the ending** — free roam; the sky the ending chose; after-lines from the Elder, Last Hearth's
  headwoman and the Saffra Wells wellkeeper. `flag.game_complete`.

## Audio

- **Buses and routing** — Master, Music, SFX, Ambience, UI, Voice; cues routed by id prefix.
  `AudioBusLayout`, `AudioCueRouting`.
- **Music** — Boss over Combat over Safe over Explore, crossfaded. `MusicDirector`, `MusicStateMachine`.
- **Ambience** — weather over town over day/night; ambient emitters. `AmbienceDirector`.
- **SFX** — pooled one-shots and positional sounds, footsteps by surface. `AudioDirector`.
- *Partial:* CC0 assets where registered, procedural placeholders elsewhere (`ProceduralAudio`);
  no voice acting; audio production (Phase 52) not done.

## UI, HUD and meta-shell

- **Title and New Game** — main menu, character creator, loading screen. `MainMenu`, `LoadingScreen`.
- **HUD** — vitals, spell, quest tracker, time and weather, event banner, nameplates, prompt,
  crosshair, minimap, compass, party, boss bar. `GameHud`, `BossFrame`.
- **Panels** — inventory, spellbook, journal, map, bestiary, dialogue, vendor, crafting, storage,
  appraisal, contract board, save slots, settings, pause. `UiPanel`, `UiTheme` (`src/UI`).
- **Settings** — window mode, vsync, FPS cap, quality tier, volumes, mouse sensitivity, invert Y,
  camera mode and shoulder, tutorials, reduced motion, subtitles, colour-vision modes, high contrast.
  `Settings`, `SettingsService`. *Partial:* a difficulty setting is stored but no gameplay system reads
  it; there is no key remapping.
- **Gamepad** — plays the whole game; prompt glyphs follow the active device. `GameInput`,
  `InputDevice`.
- **Localization** — every player-facing string through `Loc.T` and `data/locale/strings.csv`
  (English only). `Loc`, `LocaleAudit`.
- **Notifications** — toasts and banners. `Notifications`, `Toast`.

## Save and load

- **Slots and quick save** — versioned JSON envelopes, atomic writes, migrations (v3), `F5`/`F9`.
  `SaveManager`, `SaveSlotPanel`.
- **Autosave** — on a cadence and at region boundaries. `AutosaveService`.
- **World persistence** — cell snapshots, removed actors culled, spawned actors recreated.
  `CellPersistenceDirector`, `PersistentSpawnDirector`. Contract: [`SAVE_FORMAT.md`](SAVE_FORMAT.md).

## Developer tooling (dev builds only unless noted)

- **Dev console** (`F1`), debug HUD (`F3`), profiler (`F4`), seeded repro replays, integrity checker.
  `DevConsole`, `DevCommands`, `ProfilerOverlay`, `ReproHarness`, `WorldIntegrityChecker`.
- **Headless gates** (any build) — `--validate`, `--lifecycle`, `--story`, `--state`, `--economy`,
  `--worldgen`, `--world-bake`, `--worldmap`. `src/Bootstrap/Headless*.cs`.
- **Render harnesses** — HUD, panel, guild, shrine, enemy and shell shots; world shots. `*Shots.cs`,
  `tools/world_shots.gd`.
- **SDK** — `python tools/embervale.py` (doctor, build, validate, test, scenario, screenshot, perf,
  world gates, assets). [`TOOLING.md`](TOOLING.md).
- **Generators** — regions, map locations, main story, guild dialogue, buildings, the world bake.
- **Analytics sink** — a dev-only event log, not state. `src/Analytics`.

## Deliberately absent

- Survival needs — hunger, thirst, temperature, durability, repair, encumbrance (Phase 40, struck).
- Puzzles, traps and a dungeon framework (Phase 40.5, struck).
- Swimming, climbing, crouch/sneak movement (stealth exists only as a quest objective rule).
- Enchanting and sockets, a lore codex, photo mode, cinematics beyond narration cards.
- Key remapping; extra locales; storefront, platform and live-ops features.
