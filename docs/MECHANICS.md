# Embervale — mechanics catalogue

Every mechanic and core system in the game, one or two lines each, with the class or data folder that
owns it. It says **what exists**; [`ARCHITECTURE.md`](ARCHITECTURE.md) says how it works and
[`RECIPES.md`](RECIPES.md) how to add to it. Counts are `.tres` files at 2026-09-28 (`--state` has the
live census). *Partial* marks something that exists but is incomplete.

## Movement and traversal

- **Walk, sprint, jump** — input-agnostic kinematic motor; speed from the `MoveSpeed` stat. Walk toggle
  (`Caps Lock`) or a half-pushed stick; the player's sprint drains stamina and is refused while winded;
  coyote time and a jump buffer; slower uphill, snaps down steps; long drops stumble, and past 7 m cost
  the player health (deep water cushions). `LocomotionComponent`, `LocomotionRules`, `SprintStamina`,
  `JumpAssist`, `FallRules` (`src/Movement`).
- **Step-up** — climbs kerbs and steps up to 0.5 m that Godot's `CharacterBody3D` cannot, rolls
  included; kept only if the body rose, advanced and landed on walkable floor, so it never climbs
  cliffs. `StepUp`. There is no climbing or ledge grab beyond this.
- **Dodge roll** — held input rolls on an ease-out burst; neutral input backsteps (cheaper, shorter
  i-frames). Presses mid-swing or mid-roll are buffered, an attack cancels the roll once i-frames
  close, back-to-back dodges cost more, refused while winded or mounted. `DodgeComponent`, `Dodge`.
- **Stamina pacing** — regen pauses while you keep spending, then ramps in, runs slower near empty and
  behind a raised guard, and scales with Endurance. Hitting zero leaves you winded (no dodge or sprint,
  the stamina bar dims) until 35% refills. `StaminaPacing`, `StatsComponent` (`src/Stats`).
- **Mounts** — whistle (`Y`) once the stablemaster is paid and the horse gallops in from out of sight,
  then waits where you dismount; walk/trot/canter/gallop with a speed ramp and a heading that turns
  slower the faster it goes; it balks at deep water and steep drops, jumps from a trot, and only a
  gallop at real pace hits harder. `MountComponent`, `MountGaits`, `MountTerrain`, `MountWhistle`,
  `MountRules`, `MountedCombat`.
- **Wading, not swimming** — water slows you through ankle, knee and waist bands; at 0.9 m a warning
  fires and past the 1.1 m limit the water pushes you back to the shallows. Deep water, an exitless
  pit or a fall out of the world fades you back to recent safe ground, facing away. `WorldWater`,
  `WadingRules`, `WorldWading`, `WorldRecovery`, `SafeGroundTrail`, `WorldWaterResource`.
- **Safe placement** — every spawn, landing and respawn waits for real collision and a clear capsule,
  ring-searching up to 3 m around a refused point and reporting why it gave up; the loading gate
  retries placement and names the stage it is stuck on. `SafePlacementService`, `PlacementSearch`,
  `LoadingCoordinator`, `LoadingGateRules`.
- **Foot IK and footsteps** — two-bone leg IK plants each foot on slopes and stairs, drops the pelvis
  to the lower foot and rolls planted feet to the ground; off mid-air, mounted or rolling. Footsteps
  land on the animation's foot contact, sound by water, tag, terrain biome or collider, and scale with
  gait; jumps and landings too, NPCs within 20 m. `FootIkComponent`, `FootPlacement`,
  `FootstepComponent`, `FootstepGait`, `FootstepAudio`, `Surfaces`.
- **NaN guard** — a motion vector is checked before it reaches physics, and a body found at a
  non-finite position or below the world is returned to its last floor; each is logged once per body.
  `MotionSafety`.

## Camera

The rig (`PlayerCameraRig`) is the only writer of the camera transform. Everything else is an
`ICameraLayer` on the player that returns a small nudge (offset, angle, FOV, distance) each frame; the
rig sums and clamps them (`CameraLayer.cs`, `CameraRigMath.CombineLayers`). Motion layers scale by
`CameraComfort`: the settings sliders and Reduced Motion reach all of them.

- **First/third person swap** (`V` or the setting, at any time) — true first person rides the head
  bone with the body visible; third person is over the shoulder (side, distance 2–6 m and FOV are live
  settings). The swap is a critically damped spring, so a second press mid-swap turns it round instead
  of snapping; look direction is kept, the eye seat crossfades, and a swap to third person waits in
  first person until the seat has 1 m of room. `PlayerCameraRig`, `CameraRigMath` (`src/Player`).
- **Camera profiles** — exploration, sprint, combat, target-lock, aim and mounted multiply the
  player's settings, never replace them. Sprint and gallop lean with speed; a context blends in and
  releases at different rates (combat framing is sticky, aim snaps in and lets go slowly); each has
  its own pitch limit; look input slows when the view narrows. Only widening FOV is scaled by FOV Kick.
  `CameraProfile`, `CameraContext`.
- **Wall spring** — sweeps only world geometry and camera blockers, so actors never yank it. Five
  probes catch corners, a low ceiling lowers the camera, a wall beside the head narrows the shoulder,
  and with Auto Shoulder Swap it swings to the free shoulder before pulling in (hysteresis and a
  0.8 s hold, so it never flickers). Pull-in is instant; push-out waits 0.25 s then eases. First person
  has a near-plane guard. `CameraRigMath.ProbeMotion`, `SpringStep`, `ShoulderSwapped`.
- **Lock-on and aim framing** — a close locked target is framed past the shoulder (lateral slide,
  rise, slight pull-back, yaw cancelling the slide so the target stays on the crosshair), easing in
  over 0.5 s and out over 0.9 s so a broken lock fades. Aiming tightens FOV slightly and leads toward
  the shoulder side. Cycling goes left to right on screen, a lost target keeps the lock for 0.8 s, and
  a new lock swings the body round over 0.3 s. The aim ray starts at the pivot's depth, so a prop
  between camera and player is never the aim point. `CameraFramingLayer`, `FramingMath`, `LockOn`,
  `LockOnComponent`, `AimController`.
- **Camera shake** — trauma per source (hit, heavy, block, crit, stagger, spell, action, landing),
  each capped, the total held to 0.8. Smooth noise rather than jitter, and hits kick from the side
  the blow came from (slide and roll, never pitch or yaw, so the crosshair stays put). Scaled by the
  Camera Shake setting. `CameraShake`, `ShakeMath` (`src/Combat`).
- **Movement feel** — landing dip scaled by drop height, first-person head bob and sway by gait
  (stronger sprinting; off airborne, mounted or rolling), a settle when a sprint ends, dodge lean into
  the roll and pull-back on a backstep, per-gait mounted sway. Half strength in third person, bob off
  there. `CameraMotionLayer`, `CameraMotionMath`.
- **Special views** — dialogue push-in toward the speaker, a boss-entrance lean for the intro lock, a
  slow pull-back after the player dies, a faint lean toward a focused interactable, a wider seat when
  mounted. Restrained ceilings (a few degrees), and the player's own look input always wins.
  `CameraDirectorLayer`, `SpecialViewMath`. *Partial:* a dialogue pauses the world, which stops the
  rig ticking, so the dialogue push-in does not currently play; the others do.
- **Obstruction fade** — props and actors between the camera and the player thin out (up to 80%
  transparency) in third person or when pulled back; walls and terrain stay solid, and every mesh is
  restored exactly. Baked architecture and scatter are merged meshes, so they are not faded.
  `CameraOcclusion`, `CameraOcclusionMath`.
- **Camera comfort** (Gameplay and Accessibility settings) — Camera Shake, Head Bob and FOV Kick
  sliders, Auto Shoulder Swap, Lock-On Framing and Fade Obstructions toggles. Reduced Motion zeroes
  bob and FOV kick and holds shake to 25%. `CameraComfort`, `Settings`.

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
