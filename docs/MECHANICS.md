# Embervale — mechanics catalogue

Every mechanic and core system in the game, one or two lines each, with the class or data folder that
owns it. It says **what exists**; [`ARCHITECTURE.md`](ARCHITECTURE.md) says how it works and
[`RECIPES.md`](RECIPES.md) how to add to it. Counts are `.tres` files at 2026-09-28 (`--state` has the
live census); the item, crafting and save sections were recounted from the files on 2026-10-06.
*Partial* marks something that exists but is incomplete.

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
  `CameraDirectorLayer`, `SpecialViewMath`. A dialogue pauses the world, so the rig ticks itself for
  its duration (`CameraRigMath.TicksWhilePaused`), with the player's inputs held at rest.
- **Obstruction fade** — props and actors between the camera and the player thin out (up to 80%
  transparency) in third person or when pulled back; walls and terrain stay solid, and every mesh is
  restored exactly. Baked architecture and scatter are merged meshes, so they are not faded.
  `CameraOcclusion`, `CameraOcclusionMath`.
- **Camera comfort** (Gameplay and Accessibility settings) — Camera Shake, Head Bob and FOV Kick
  sliders, Auto Shoulder Swap, Lock-On Framing and Fade Obstructions toggles. Reduced Motion zeroes
  bob and FOV kick and holds shake to 25%. `CameraComfort`, `Settings`.

## Combat

Weighty and readable: a swing commits, its tell is legible, and parry, guard and poise decide fights.
Attackers stamp a `HitKind` (and a `Charge`) on the `DamagePacket`; the defence pipeline turns that into
damage, poise and guard pressure in one place (`DefenceRules`, `CombatMath`). Presentation never
changes a rule: it reads events, and everything that punctuates a blow for feel scales by
`CombatComfort` (Hit Stop, Screen Flash, Damage Numbers, Lock-On Assist, Aim Assist; Reduced Motion
caps the first two at 25%).

- **One action timeline** — every actor's attacks, casts and shots are `ActionDefinitionResource`s
  with windows as fractions of the clip; one executor with a per-swing context (kind, charge, damage
  and poise scale, hyperarmour, step). Light chains keep the start of their recovery committed
  (`RecoveryCommit`), heavy and plunge tails are punishable, and a committed action's facing turns no
  faster than its `TurnDegreesPerSecond` (always for enemies; for the player only while locked on).
  `CharacterActionComponent`, `ActionTimeline`, `ChargeRules`, `PlungeRules`.
- **Melee combos and input buffer** — weapon attack chains alternate swing direction and the finisher
  hits harder. A press is buffered only within 0.28 s of the cancel point, so mashing through a long
  wind-up queues nothing; a stagger clears it; a buffered press keeps its direction. Enemies weight
  against repeating their last blow. `AttackBuffer`, `AttackInput`, `ActionSelection`.
- **Heavy and charged attacks** — hold attack 0.22 s or more to charge, release to swing. A short
  hold is a heavy; from 0.3 it is charged (damage and poise grow with the hold, released faster the
  longer it was held), and from 0.8 it is hyperarmoured. Charging slows movement to 40%, drains
  stamina and is dropped by a stagger, a raised guard, a dodge or a weapon swap. A tap swings a light.
  `HitKind.Heavy`, `HitKind.Charged`, `ChargeReleasedEvent`.
- **Plunge attack** — attack in the air dives at 18 m/s and lands as a sphere blow scaling from x1 to
  x2 with the drop (0.7–8 m); it always crushes a guard. Refused on the ground, mounted or without
  stamina. `PlungeRules`, `HitKind.Plunge`.
- **Directional melee** — dragons pick bite, wing or tail by where you stand, with 12 degrees of
  hysteresis so the arc never flickers. Humanoids take a direction from movement input at the commit:
  forward lunges 0.9 m for more damage, back steps away for less, sideways steps for more poise damage.
  An attack out of a roll is the weapon's roll-cut. `DragonMeleeComponent`, `DragonMelee`,
  `AttackDirections`.
- **Motion warp** — closes the last of a committed attack's gap, swept so it never passes a wall, and
  ignores a target outside twice its turn limit. It now faces the target (the old yaw faced away).
  `MotionWarp`.
- **Blocking, parry, riposte** — `RMB` blocks. The guard covers a front zone at full strength and a
  flank zone at 60% with no parry; the rear is open. A block costs stamina scaled by the blow's weight
  and guard pressure, and a tired guard takes more poise damage. A block raised inside the window
  parries, graded: Perfect (first half of the window) is free, negates the blow and staggers the
  attacker longest; Good costs stamina; Late is a hard deflect with no riposte. Arrows and spells can
  be blocked, never parried. One parry per guard raise. `CombatComponent`, `Parry`, `DefenceRules`.
- **Guard break** — a guard that cannot pay its stamina, or that a fully charged or plunging blow
  crushes, breaks: the full hit lands and the body takes a long stagger scaled by class. A body with no
  stamina pool cannot be stamina-broken. `GuardBrokenEvent`.
- **Punish window and criticals** — a body is open after a poise break, a parry or a guard break, for
  the stagger plus 0.25 s, and for the committed tail of its own heavy or plunge swing. The first melee
  blow on an open body is a riposte critical (x1.25 swing recovery, x1.35 poise break, x1.75 guard
  break, x2 parry, x2.5 perfect parry); a blow from behind is a backstab (x1.5).
  Bosses take 60% of the bonus and the player's side is never opened, so poise stays symmetric.
  `OpenCause`, `HitKind.Riposte`, `HitKind.Backstab`, `CriticalHitEvent`, `PunishWindowOpenedEvent`.
- **Poise and stagger** — symmetric for every actor: flinch, stagger, heavy, knockdown by reaction
  class; a stagger cancels a wind-up, never a live blow; wind-ups can take extra poise damage; bosses
  are never knocked down. A flinch has its own timer and no longer cancels an action, and poise is not
  chipped while staggered, so a stagger cannot be chain-locked. `PoiseReaction`, `CombatComponent`.
- **Hit-stop and hit feedback** — every blow is named once (`HitOutcome`: hit, blocked, parried, guard
  broken, critical, poise broken, resisted) and hit-stop, mesh lurch, sparks, sound, screen flash and
  floating numbers all follow that name. Hit-stop weighs outcome, damage and kind, is rate-limited and
  restores the clock on pause; the trail streaks through the swing from the release frame. The
  damage-direction indicator is coloured by outcome and warns of a hostile wind-up behind or beside
  you; nameplates show a poise bar and a state tag. `CombatFeedbackDirector`, `HitStopDirector`,
  `HitReactionComponent`, `WeaponTrailComponent`, `CombatFeedbackOverlay`, `DamageDirectionOverlay`.
- **Floating damage numbers** — crits large and gold, blocks small in parentheses, resisted dim with
  a word, parry a word with no number; rapid hits on one target merge. Off with the Damage Numbers
  setting; held still under Reduced Motion. `DamageNumberLayer`, `DamageNumberMath`.
- **Lock-on** — middle mouse; cycles targets in range, faces the target, and says when it breaks
  (target died, too far, lost sight). With Lock-On Assist it prefers a target mid-swing or nearly dead,
  passes the lock to the next enemy within 10 m on a kill, and steps on a mouse flick or a full stick
  push. Cover is judged against static geometry and terrain only, so a person never blocks a lock. `LockOnComponent`, `LockOn`, `LockOnCueLayer`, `LockChangedEvent`, `LockBrokenEvent`.
- **Ranged** — the bow's startup is the draw: hold to draw (7 stamina a second), release on the action's
  release frame. A tapped or buffered press is a weak snap shot (x0.45 damage, scatter); a full draw hits
  hardest and flies fastest. Arrows are solved under gravity through the crosshair, stop and stick in
  world geometry, and query the physics space each sub-step so a thin body is never tunnelled. An
  aim assist bends the arrow (never the camera), preferring the lock-on target and never through cover.
  Archers with no draw loose a full draw as before. `RangedAttack`, `BowDrawComponent`, `Arrow`,
  `AimController`, `RangedMath`, `AimAssistMath`. 19 weapons (`data/weapons`).
- **Hit zones** — an actor can carry several hurtboxes with their own damage and poise multipliers
  (dragon heads, boss bodies); a zone of x1.5 or more is a weak point, an arrow lands on the
  highest-multiplier zone it reaches, and a tall single-zone body takes a headshot on its top 18%
  (x1.5). `Hurtbox`, `HitZoneResource`, `HitZoneRouting`.
- **Damage and resistances** — Physical (armour), Fire, Frost, Lightning, Arcane, Nature, Necrotic
  (own resistances), True; one mitigation curve; resistance never immunity, and a negative resistance
  is a vulnerability (bounded below x2). An unblocked hit does at least 1 damage; crit chance is capped
  at 75% and the multiplier at x4. `CombatMath`, `DamageType`, `HitKind`.
- **Telegraphs** — ground rings sized to the real wind-up, tinted by boss phase, in four classes read
  by shape as well as colour: standard, parryable (a gold ring closes on the parry moment), unblockable
  (thick pulsing red) and sweep (a fan by the arc). An action can author its class and fan angle
  (`ActionDefinitionResource.Telegraph`, `SweepDegrees`); left on Auto it is inferred from the action's
  id, hitbox and commitment. `TelegraphComponent`, `TelegraphClass`.

## Magic

- **Spells as data** — 25 player spells and five enemy spells (`data/spells`): school, delivery, cast
  mode (instant, charged by hold, channelled), mana, cooldown, status. `SpellResource`,
  `SpellcastingComponent`. `Q` casts, `F` cycles, `T` spellbook.
- **Committed casts** — visible wind-up, release and recovery; stagger interrupts for a half-mana
  refund. Held channels retain commitment and slow movement; charge scales damage, piercing and burn
  duration. `SpellActions`, `SpellcastingComponent`, `CharacterActionComponent`.
- **Special deliveries** — delayed ground telegraphs, destructible barriers, enemy-hitting dashes,
  piercing and homing bolts, lingering zones, a healing totem and a committed-direction Blink.
  `SpellGround`, `SpellBarrier`, `SpellProjectile`, `SpellZone`, `SpellTotem`.
- **School identities** — Fire stacking ignite, Frost chill to freeze, Lightning chain, Necrotic
  lifesteal, Nature regrowth, Arcane ward and dispel. `SchoolIdentity`.
- **Combos** — Shatter, Thermal Shock, Steam Burst, Meltdown, Superconduct and Smoke Out read pre-hit afflictions.
  `SpellCombo`.
- **Mastery** — schools rank up with use; spells gain rank damage. `SchoolMasteryComponent`,
  `SpellMastery`.
- **Status effects** — stacking burns and decay, frost control with immunity, roots, silence,
  marks, swarms, regeneration and wards (`data/status_effects`), with VFX. Root/Stun cancels dodge
  i-frames; effects, modifiers and immunity clear on death and before load. `StatusEffectsComponent`.
- **The fading Weave** — each region's `WeavePotency` weakens ordinary magic and strengthens corrupted
  magic as it falls. `Weave`, `WeaveMath`.
- **Corrupted casts** — spells with `MinCorruptionTier` can only be learned at that tier or above.
- **Learning** — tomes, dialogue (`LearnSpell`) and trainers share one learning route; spellbook
  purchase/upgrade spends spell points. Corrupt tomes require an explicit embrace. Retired spell ids
  migrate on load. `SpellLearning`, `SpellTomeComponent`, `SpellAliases`, `SpellbookPanel`.

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
- **Perks** — 71 perks (`data/perks`, generated by `tools/gen_perks.py`) in seven branches (Warrior, Archer, Mage, Rogue,
  Crafter, Social, Ashbound) bought with skill points. Each main branch is a tree of five tiers worth about 40 points
  against the 54 a character can earn (49 level points plus a bonus point at each milestone level, 10/20/30/40/50), so a build chooses: tier two needs 2
  points spent in the branch, three needs 5, four needs 9, and the one capstone needs 14 plus a prerequisite. Perks carry
  stat bonuses and non-stat effects (`PerkEffectKind`, read through `PerkQuery`, capped by `PerkEffectMath`); every kind
  has a perk and the whole catalogue stays under each cap. They cut the Stamina price of blocking, attacks, parrying, bow
  draw, dodging and sprinting (never below half), spell mana cost (never below 60%), raise a school's spell power, spell
  crit, arrow damage, loot quality, salvage yield, salvage XP, XP gained and standing gained, and change haggle chance,
  shop buy and sell prices, service prices and the chance a craft returns a material. The Ashbound branch is gated by
  corruption tier and nothing else is. `Respec` sells every bought rank back for gold (`RespecRules`). `PerksComponent`,
  `PerkResource`, `PerkCatalogue`. The Perks tab of the character screen (`I`) is a tree: a branch strip, one branch at a
  time as tier rows by column with prerequisite connectors, nodes showing rank pips and a state (learnable, locked, needs
  points, corruption-gated, maxed), a detail pane with the reason a perk cannot be learned, the skill-point chip and a Respec
  button with a gold-cost confirmation (`PerkTreePanel`, `PerkTreeCanvas`, `PerkTreeRules`). Pressing a node buys its next
  rank. The dev commands `perk`, `respec`, `sp` drive the same components.
- **Stats** — resources, primaries, derived stats and six resistances with flat/percent modifiers.
  `StatsComponent`, `Stat` (`src/Stats`). **Primaries are real for the player:** each point above its
  base gives Strength +0.8 Physical Power; Dexterity +0.2% crit and +0.3% attack speed; Intelligence
  +0.7 Spell Power and +4 Mana; Vitality +5 Health and +0.3 Armor; Endurance +3 Stamina (plus the
  existing regen scaling). Levelling grows each primary 0.25 per level, so level-50 Health, Stamina,
  Physical Power and Armor match the old totals, and casters now gain Spell Power and Mana. Race
  deltas, gear and perks on a primary move the derived stats too. The stat screen shows "Per point"
  under each primary, and the `derived` dev command dumps it. `StatDerivation`, `StatDerivationComponent`.
  Dexterity cuts the player's dodge stamina price (-0.5%/pt, floor 0.75) and Intelligence the price of the player's
  spells and channels (-0.4%/pt, floor 0.8); each is combined with the matching perk factor under one floor (0.4 dodge,
  0.5 mana) so they cannot stack past it, and enemies, who have no `StatDerivationComponent`, are unaffected.
- **Races and character creation** — six playable races (Human, Valari, Sylthari, Grondar, Draekyn,
  Umbral) with stat deltas, innate perks and starting standing; name and race picked at New Game.
  `RaceResource`, `CharacterCreator` (`data/races`).
- **Appearance** — the creator picks a skin tone, hair colour, eye colour, ember glow colour and build from
  swatches filtered by race, beside a live preview of the same body model. Material-only: one body shader
  recolours skin, hair and eyes through a region mask, and the build scales the body width (0.92 / 1.0 / 1.1).
  Purely cosmetic. Corruption tiers still ash the body and light the skin and eyes in the chosen ember colour.
  `AppearanceOptionResource`, `PlayerAppearance`, `AppearanceRules`, `player_body.gdshader` (`data/appearance`).
- **Backgrounds** — eight soft nudges picked at New Game (Wayfarer is the no-op default): a free tier-1
  perk rank, a small kit capped at 80 gold in value, a story flag read by a hub dialogue, a standing
  tweak and at most +1 on a stat. Kit and perk are New Game only; stat deltas are re-derived from the
  profile on load. The lean badge is cosmetic. `BackgroundResource`, `BackgroundApplier`,
  `BackgroundRules` (`data/backgrounds`).

## Items, equipment and loot

- **Items** — 373 (`data/items`): 220 equippables, 51 consumables, 7 placeables and 95 plain items
  (materials, 40 recipe scrolls, quest items, relics, coin). 296 are generated from
  `tools/items/catalogue.py` by `tools/gen_items.py`: six tiers of gear, one per realm, across eight
  weapon classes plus shields, three armour weights, rings, amulets and arrows. `ItemResource`
  (`ItemLevel`, `Tier`, `RequiredLevel`, `SetId`, `UniqueEffectId`), `EquippableItemResource`,
  `ConsumableItemResource`, `ItemDatabase`.
- **Item instances** — a rolled copy: rarity, frozen affixes, generated name, the item level it was
  rolled at, workmanship, upgrade level, and the player's lock and junk marks. Only affix-less
  copies of one workmanship and level stack. `ItemInstance`, `ItemStack`, `CraftQuality`,
  `ItemUpgrades`.
- **Stat budget** — an equippable spends points by item level, slot, hands and rarity; weapon
  damage and gold value come from the same level. `--validate` allows a quarter either way.
  `ItemBudget` (C#) mirrors `budget_points` / `weapon_damage` / `item_value` in `gen_items.py`.
- **Inventory and equipment** — slot stacking with a capacity; main hand, off hand, head, chest,
  hands, legs, feet, ring, amulet, ammo; bonuses as stat modifiers sourced to the instance (six
  school resistances, mana, mana and stamina regeneration included); weapons and shields shown on
  sockets, with a stand-in model per weapon class. `InventoryComponent`, `EquipmentComponent`,
  `EquipmentPresentationComponent`.
- **Material bag** — the player's plain materials live in an uncapped bag beside the pack (the
  Materials tab) and take no slot; counting, removing, crafting, selling, storage, appraisal and
  impound read pack and bag together. Other inventories (chests, merchants) do not use one.
  `InventoryComponent.UseMaterialBag` / `Materials` / `AllStacks`.
- **Marks, split, drop** — lock an item (never sold, salvaged, junked or dropped), mark it junk
  (sold or salvaged in one confirmed press), split a stack, move part of one, drop it as a pickup.
  Search, slot filter and sort orders in the pack; search in the stash; sell-some, sell-all-junk and
  a session-only buyback shelf at a counter. `InventoryPanel`, `StoragePanel`, `ItemTransfer`,
  `QuantityPicker`, `ItemPresentation`.
- **Level, hands and room** — gear and the stronger consumables carry a required level; a
  two-handed weapon empties the off hand and refuses an off-hand item; a swap with no room for what
  comes off is refused with a toast, never lossy. `EquipmentComponent.CanEquip`, `InventoryRules`,
  `EquipRefusal`.
- **Item sets** — eight (`data/item_sets`); wearing enough distinct pieces grants a stat bonus or a
  unique effect at each threshold. Re-derived from what is worn after every change and every load,
  never saved. `ItemSetResource`, `ItemSetBonusResource`, `SetRules`, `EquipmentComponent.SetPieces`.
- **Unique effects** — 22 (`data/unique_effects`) over ten kinds: on-hit status, heal on kill,
  low-health power, block reflect, spell echo, dodge refund, gold find, thorns, crit execute, mana
  shield. Carried by the 17 named legendaries and by set thresholds; cooldowns are session-only.
  `UniqueEffectResource`, `UniqueEffectKind`, `UniqueEffectsComponent`, `UniqueEffectRules`.
- **Consumables** — heal, restore stamina or mana (instant or over time), a timed stat buff, a cure;
  three shared cooldown groups (potion, elixir, food); a use that would be wasted (on cooldown,
  already full, nothing to cure, level too low) is refused before anything is spent. Cooldowns and
  running restores are cleared on load. `ConsumableEffectKind`, `ConsumableEffectsComponent`,
  `ConsumableRules`, `InventoryComponent.Consume`.
- **Ammunition** — the ammo slot holds the whole stack; the player's bow spends one arrow a shot,
  refills from the pack when the slot runs dry, and will not draw on an empty quiver; arrow tier
  adds 4 damage a tier above the first. `EquipmentComponent.ConsumeAmmo`, `AmmoRules`.
- **Hotbar** — five consumable slots on `1`–`5`, or hold `LT` and press D-pad / Select on a pad; each
  cell sweeps its cooldown. `HotbarComponent`, `HotbarPanel`.
- **Loot tables** — 52 (`data/loot`): 21 family and shop tables, 24 tier pools (gear, materials,
  supplies, scrolls for each of six tiers, `data/loot/tiers`) and 7 Flamebearer chest tables
  (`data/loot/bosses`). Rows nest other tables (`{tier}` in a path resolves to the realm's tier),
  pick one of a weighted group, gate on level and can be once per save. `LootTable`, `LootEntry`,
  `LootGenerator`, `LootContext`; pools generated by `tools/items/gen_loot.py`.
- **Rarity and item level** — Common to Legendary; rarity decides the affix count (0 to 4). A drop
  is rolled for the realm it happens in, at an item level within two of the looter's, clamped to
  the tier's band; a dry streak of non-Rare rolls raises quality after 6 and guarantees a Rare at
  24. `LootRarity`, `LootTiers`, `PityRules`, `LootLedger` (saved as `loot_ledger`).
- **Affixes** — 43 prefixes and suffixes (`data/affixes`, 32 generated) frozen onto each instance;
  values grow with item level; a group allows one of its members per item; Epic and Legendary rolls
  take a signature affix first; three kinds add health, stamina or mana regeneration.
  `AffixDefinition`, `AffixDatabase`, `ItemAffix`, `AffixRegenBinding`.
- **Boss reward chests** — each Flamebearer leaves a persistent chest where it fell (a yielding
  duel leaves none); the chest rolls the boss's table when opened, seeded per save so every reload
  gives the same roll, and holds its signature legendary once per save. Loot still on the ground
  is kept by the chest across a save. `LootComponent`, `ContainerLootComponent`, `LootSeeds`.
- **Drop presentation** — Uncommon and better pickups stand a rarity beam; Rare and better chime
  on drop and on pickup; the loot feed merges a burst of pickups into one toast. `LootPresentation`,
  `ItemPickupFactory`, `LootFeedMerger`.
- **Pickups** — `E`, or hold `E` to sweep nearby loot; gold and plain materials are collected by
  walking over them (never what the player dropped, never contraband). A full pack says so once
  through `InventoryFullEvent`. `ItemPickupComponent`, `InteractionSensor`.
- **Relics** — six Flamebearer hearts (`item.relic.*`) plus four guild-finale tokens.
- No durability, repair, encumbrance or sockets (struck).

## Crafting

- **Recipes at stations** — 82 recipes (`data/recipes`, 67 generated by `tools/gen_recipes.py`): 8
  by hand, 26 at a forge, 33 at a workbench, 15 at an alchemy bench. Crafted singly or in bulk (up
  to 99) from pack and material bag; a craft with no room for its output is refused and the
  ingredients returned. `CraftingRecipeResource`, `CraftingComponent`, `CraftingStationComponent`.
- **Learning** — six of the generated recipes are known from the start, 21 are taught by the
  trainer and 40 by the trainer or by studying their recipe scroll (from the inventory or the
  crafting window). `GameIds.Recipes.Starting`, `ServiceKind.Trainer`,
  `CraftingComponent.StudyScroll`.
- **Crafting skill** — crafting earns skill XP (10 a recipe tier, a quarter once two ranks past
  the recipe) and ranks 0–10; a recipe of tier N needs rank N-1 at the player's own bench.
  `CraftingSkill`, saved as `skill_xp`.
- **Workmanship** — each crafted piece of unstackable gear rolls Standard, Fine, Superior or
  Masterwork from the rank above the recipe's requirement; it scales the template's stats (up to
  +18%) and value (up to +35%), never the affixes. The roll is derived from the saved craft count,
  so a quickload replays it. `CraftQuality`, `CraftQualities`, `CraftingSkill.Roll`.
- **Affixed output** — a recipe with an output rarity above Common rolls affixes at the template's
  item level, with a little luck from mastery. `LootGenerator.RollAffixed`.
- **Reforging** — at a forge only, for gold and the item tier's ingot: reroll one affix (the price
  climbs with each reroll of that item), upgrade a piece up to +5 (6% stats and 5% value a level),
  or promote Uncommon to Rare and Rare to Epic with one new affix. Every fee is priced above what
  it adds to the sale value, which `--validate` checks; outcomes are seeded from the item and a
  saved serial. Worn gear is reforged in place. `ReforgeRules`, `ReforgeQuote`,
  `CraftingComponent.RerollAffix` / `Upgrade` / `Promote`.
- **Salvage** — deconstruct gear for a fraction of its recipe's materials plus XP; salvage perks
  raise the fraction from 50% to at most 75%. A recipe is reversed only at its own station (scrap
  elsewhere, and the window names the better station); worn gear salvages with a full pack;
  junk-marked gear salvages in one confirmed press; locked and stackable gear never.
  `Deconstruction`, `SalvagePlan`, `CraftingComponent.PlanSalvage`.
- **Material saving** — a crafting perk can hand one unit of the largest ingredient back after a
  craft (a derived roll over the saved craft count, never a free craft). `MaterialSaving`.
- **Commission** — pay a smith to make a known recipe, buying the ingredients the player lacks;
  the piece comes out Common and Standard, needs no crafting rank and earns no skill XP.
  `CraftingComponent.Commission`, `CommissionRules`.
- **Crafting window** — craft, salvage and reforge tabs; search, filters, a quantity picker and one
  pinned recipe. `CraftingPanel`.

## Economy

- **Shops** — 24 (`data/shops`) with opening hours, travel days, stock, restock, purses and
  specialties; one price authority. `ShopPricing`, `ShopStock`, `ShopHours`, `VendorPanel`.
- **Standing prices and haggling** — faction tier moves buy prices; one haggle per merchant per day. Perks add to the
  haggle chance (never opening a merchant who does not haggle) and take up to 5% off buy prices and add up to 5% to sell
  prices, shown as a line in the price tooltip; `--validate` proves the shop margin at those caps. `HaggleRules`,
  `PriceBreakdown`.
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
- **Corruption appearance** — the player's body shifts with corruption tier (ash, skin wash, ember glow via the body shader).
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

- **Envelope** — versioned JSON (format 4) with a SHA-256 checksum over the saved objects, atomic
  writes and one backup generation per slot that a load falls back to; migrations from v1; a save
  from a newer build is refused, never bypassed. `SaveManager`, `SaveEnvelope`, `SaveChecksum`,
  `SaveMigrations`, `SaveBackup`.
- **Slots** — a quick slot, three manual slots the player can write, and a three-slot autosave
  ring the player can load but never write. `SaveSlots`, `SaveKind`, `SaveSlotPolicy`.
- **Quick save and load** — `F5` always writes the quick slot; `F9` loads the newer of the quick
  slot (when it holds this character) and the session's own slot, asking for a second press when
  more than a minute of play is unsaved. `SaveSlotPolicy.QuickLoadTarget`, `DeveloperToolsHost`.
- **Pause menu** — Save writes the session's manual slot, or opens the browser to pick one when the
  session has none (it never guesses); Save to Slot and Load open the slot browser, which badges
  damaged, newer-format and backup-recovered slots. `PauseMenu`, `SaveSlotPanel`,
  `SessionLifecycleCoordinator.TrySave`.
- **Save blocks** — a boss fight (from the first blow, released after 20 quiet seconds) and a
  conversation refuse manual, quick and automatic saves, with the reason as a toast.
  `SaveManager.PushSaveBlock` / `CanSaveNow`.
- **Autosave** — every five minutes of play, after a quest or a level-up, on arriving in a region,
  and before leaving to the menu or desktop; at most one a minute; deferred while saving is
  blocked, a menu is open, or the player is fighting or airborne (up to 90 seconds).
  `AutosaveService`, `AutosaveCadence`.
- **Off-thread writes** — in windowed play the files are written by a queue, every read and every
  exit flushes it first, and a write that fails afterwards still raises the failure toast. Gates
  and tooling write inline. `SaveWriteQueue`, `SaveWriteWorker`, `SaveFiles`, `SaveIndicator`.
- **Thumbnails** — a slot's picture is the last frame of play, never the menu; when no clean frame
  exists the old picture is removed. `SaveThumbnailService`.
- **Tolerant loads** — a `Load` replaces live state; a malformed item, affix or stack entry is
  skipped and content the build no longer has is dropped with a warning. `SaveRead`.
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
- **Generators** — regions, map locations, the campaign, guild dialogue, perks, appearance, buildings,
  the world bake, and the item catalogue (`gen_items.py`, `gen_recipes.py`, `items/gen_loot.py`).
- **Analytics sink** — a dev-only event log, not state. `src/Analytics`.

## Deliberately absent

- Survival needs — hunger, thirst, temperature, durability, repair, encumbrance (Phase 40, struck).
- Puzzles, traps and a dungeon framework (Phase 40.5, struck).
- Swimming, climbing, crouch/sneak movement (stealth exists only as a quest objective rule).
- Sockets and gem enchanting (struck; reforging at a forge is the only way to change a finished
  item), a lore codex, photo mode, cinematics beyond narration cards.
- A second vault or warehouse: storage is the bank, property stashes and the material bag.
- Key remapping; extra locales; storefront, platform and live-ops features.
