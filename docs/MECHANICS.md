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
- **Gait animation** — one 2D blend over strafe and forward speed, each divided by the actor's own
  run speed, so walk (0.45), run (1) and sprint (1.6) each play their own clip on any body; a
  diagonal blends the strafe with the gait for its speed; an airborne body takes a fall pose.
  Every Meshy humanoid uses it. The library's clips have their root travel removed at build, the
  idle is calmed to face forward (the original is kept as `idle_alert`), and `strafe_right` is the
  squared `strafe_left` mirrored. Casts, channels and library swings play on the upper body while
  the legs keep moving. `LocomotionBlend`, `LocomotionTree`, `CharacterAnimationComponent`,
  `tools/build_meshy_anim_library.gd`.
- **NaN guard** — a motion vector is checked before it reaches physics, and a body found at a
  non-finite position or below the world is returned to its last floor; each is logged once per body.
  `MotionSafety`.

## Camera

The rig (`PlayerCameraRig`) is the only writer of the camera transform. Everything else is an
`ICameraLayer` on the player that returns a small nudge (offset, angle, FOV, distance) each frame; the
rig sums and clamps them (`CameraLayer.cs`, `CameraRigMath.CombineLayers`). Motion layers scale by
`CameraComfort`: the settings sliders and Reduced Motion reach all of them.

- **First/third person swap** (`V` or the setting, at any time) — true first person sits at the
  head with the body visible; third person is over the shoulder, behind the character's back (side,
  distance 2–6 m and FOV are live settings). The swap is a critically damped spring, so a second
  press mid-swap turns it round instead of snapping; look direction is kept, the eye seat crossfades,
  and a swap to third person waits in first person until the seat has 1 m of room.
  `PlayerCameraRig`, `CameraRigMath` (`src/Player`).
- **First-person eye** — the eye is anchored to the head's rest position with a neck pivot turned by
  pitch, and takes only 30% of the animated head's vertical travel and 10% of its horizontal (none
  under Reduced Motion), so a clip never throws the view. Looking down carries it forward over the
  chest. Two spheres are cut out of the player's own body, one round the head and one round the
  camera, so neither the face nor a near-plane slice is ever in frame while the head still casts
  its shadow; head-socket gear goes shadows-only. `CameraRigMath.EyeLocal`, `LookDownReach`,
  `HeadHidden`, `EyeSphereRadius`, `PlayerAppearance.SetHeadCutout` / `SetEyeCutout`,
  `player_body.gdshader` (`fp_head`, `fp_eye`).
- **Smooth view between physics ticks** — each drawn frame the camera pivot and the visible body
  are offset by the residual between the last two physics positions, and the offset is removed
  before any physics tick reads them. In third person the visible body trails a camera turn by a
  tenth of a second (at most 30 degrees; off in first person, lock-on, actions, a dodge, mounted,
  casting and Reduced Motion). `PlayerVisualSmoother`, `VisualSmoothing`.
- **Casting arm in first person** — while a spell is wound up, charged or channelled the casting
  arm is swung at the shoulder so the hand is in frame, and effects that start at the hand start at
  the hand the player sees. Presentation only: aim and the spell's path do not move.
  `FirstPersonArmComponent`, `FirstPersonArmModifier`, `FirstPersonArmRules`.
- **Third-person framing tilt** — the seat tilts down a little (at most 8 degrees) so the
  character's feet sit above the hotbar at a level look; it follows the field of view and distance
  settings, never the moment's framing, so the crosshair does not move when a fight begins.
  `CameraRigMath.FramingTilt`, `PlayerCameraRig`.
- **Weapon carry in first person** — what the hands hold is drawn only while it is up for a fight
  (an action or held charge, a raised guard, a lock) and for 2.5 s after; outside that it is a
  shadow on the ground, because the run and strafe clips swing a blade across the view.
  `CameraRigMath.WeaponUp`, `HiddenInFirstPerson`.
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
  a word, parry a word with no number; rapid hits on one target merge. A setting picks all hits,
  your blows only, crits and kills only, or off; held still under Reduced Motion. Numbers step up
  clear of each other (two targets one behind the other, an area spell on a pack) and of the state
  word, so POISE BROKEN no longer prints through the damage that caused it.
  `DamageNumberLayer`, `DamageNumberMath` (`LiftAbove`, `LiftToClear`), `DamageNumberRules`.
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
  (x1.5). A zone capsule can be turned off the vertical (`HitZoneResource.RotationDegrees`), so a dragon's
  tail, neck and wings each get one that lies along them. `Hurtbox`, `HitZoneResource`, `HitZoneRouting`.
- **Look versus reach** — a boss can be drawn larger than it fights: `ModelScale` x f with the capsule x
  sqrt(f) keeps melee reach, telegraph ring and nav unchanged, while `EnemyArchetypeResource.VisualHeight`
  sizes the whole-body hurtbox, plate, status marks, cast origin and lock-on framing (a locked target taller
  than 2.8 m pulls the camera back, up and tilts it). The lock-on point is the middle of the body's
  collision shape. `BodyMetrics`, `BossAdds.RingRadius`, `FramingMath.Lock`.
- **Held weapons on enemies** — `HeldWeaponPath` and `HeldWeaponScale` hang a model on the hand socket
  (the Iron King's mace, the clan shaman's staff); visual only. `EquipmentPresentationComponent`.
- **Damage and resistances** — Physical (armour), Fire, Frost, Lightning, Arcane, Nature, Necrotic
  (own resistances), True; one mitigation curve; resistance never immunity, and a negative resistance
  is a vulnerability (bounded below x2). An unblocked hit does at least 1 damage; crit chance is capped
  at 75% and the multiplier at x4. `CombatMath`, `DamageType`, `HitKind`.
- **Telegraphs** — ground shapes sized to the real wind-up, in four classes read by shape as well as
  colour: standard, parryable (a gold ring closes on the parry moment), unblockable (thick pulsing
  red) and sweep (a fan by the arc). They are drawn soft: a see-through body, a brighter rim, a lit
  part that sweeps to the outer edge as the blow arrives, and edges that fade out. The rim keeps the
  colour that says what to do (the boss phase's, or the unblockable red) and the body takes the
  school of the spell being wound up, so a fire breath's fan is ember and an ash breath's
  violet-grey. Footprint and timing are unchanged. High Contrast draws them flat and solid; Reduced
  Motion holds the shimmer and the unblockable pulse still. An action can author its class and fan
  angle (`ActionDefinitionResource.Telegraph`, `SweepDegrees`); left on Auto it is inferred from the
  action's id, hitbox and commitment. `TelegraphComponent`, `TelegraphRing`, `TelegraphMath`,
  `TelegraphClass`, `assets/shaders/fx/telegraph.gdshader`.

## Magic

- **Spells as data** — 25 player spells and five enemy spells (`data/spells`): school, delivery, cast
  mode (instant, charged by hold, channelled), mana, cooldown, status. `SpellResource`,
  `SpellcastingComponent`. `Q` (pad `RB`) casts, `F` (pad `LB`) is the spell wheel, `T` spellbook.
- **Spell wheel** — hold `F` (or `LB`) and the wheel opens over the HUD: eight favourites on the
  inner ring, the six schools on the outer ring, and the hovered school's known spells fanned out
  past the rim. The mouse or the right stick steers it, letting go over a spell prepares it, the
  centre or Block cancels. The world keeps running and move, jump, dodge and sprint stay live;
  look, lock-on, attack, block and cast are held back. Wedges show a cooldown wipe with seconds,
  the mana price when it cannot be paid and a padlock when corruption is too shallow. It closes
  without selecting on a stagger, death or a menu. `SpellWheel`, `SpellWheelRules`,
  `SpellWheelHold`, `SpellWheelInput`, `PlayGate`, `SpellWheelMetrics`, `SpellGlyphs`.
- **Previous spell** — a tap of `F` (under 0.16 s) swaps back to the spell prepared before this
  one. With no previous spell, or when the wheel cannot open, the press steps to the next spell as
  it always did. With presses in place of holds the key toggles the wheel, a second press or
  Attack selects, and the centre holds the previous spell. `SpellcastingComponent.SelectPrevious`.
- **Favourites** — eight slots, saved. A newly learned spell takes the first free slot and a
  forgotten one leaves it; the spellbook's pin row sets them. `SpellFavouritesRules`,
  `SpellcastingComponent.SetFavourite`, `SpellPinRules`.
- **Spell effects** — every spell has a wind-up at the hand, a release, a travel picture, an
  impact and what it leaves behind, built from pooled blocks (flares, particle bursts, lightning
  ribbons, shells, ground discs and marks, distortion, one screen flash) and coloured from the
  school's hue. All 30 spells have an authored recipe and special-case hooks. Presentation only:
  a dropped or culled effect changes nothing else. `SpellVfx`, `SpellVfxDirector`,
  `SpellVfxCatalog`, `VfxPalette` (`src/Magic/Vfx`).
- **School shapes** — particles are shaped by school (tongues of flame, streaks of snow and
  snowflakes, jagged sparks, tendrils, ash flakes, four-pointed motes, ragged smoke), every wind-up
  wears its school's motif at the hand and every bolt a shaped head. The Pyre Wall is licks of
  flame on a hot ground line, a ward shell fits the body and settles to a shimmer, and a frozen
  shell fits the body it froze. `VfxMotif`, `VfxMotifRules`, `VfxTextureRules`, `SpellVfx.Kit.cs`.
- **Per-spell pictures** — breaths stream from the creature's mouth and light the ground under
  them, the Glacial Bulwark is leaning ice crystals over plate ice, a Rime Shard freezes the floor
  where it shatters, a Blizzard is driven snow over ground mist, lightning leaves arcs on what it
  struck, a swarm circles the swarmed and a sigil turns under the grave-marked for as long as the
  status lasts. `SpellVfx.Special.Elemental.cs`, `SpellVfx.Special.Arcana.cs`,
  `VfxElementalRules`, `VfxArcanaRules`.
- **Own spells in first person** — the casting point is at the lower left of the view, at the left
  hand. A floor ring about the player's own feet is cut back and a shimmer at the screen's edges
  stands in for it; telegraphs and an enemy's standing zone are never cut. `VfxViewRules`,
  `VfxScreenRules.SelfRing`, `VfxSpawner.ScreenEdge`.
- **Spell effect quality** — Performance, Low, Medium, High or Ultra, following the graphics
  preset unless the Graphics tab's Spell effects option overrides it. A tier sets particle
  density, lights, ground marks, distortion, lightning strands and how far away an effect is
  drawn whole, and also what a blast is built from (rays, a billow, debris, smoke, glints). Large
  soft layers thin out as they cover more of the frame. `Settings.SpellEffects`, `VfxBudgetRules`,
  `VfxQuality`, `VfxCoverageRules`; [`RENDERING.md`](RENDERING.md#spell-effects) has the table.
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
  Each status also wears an aura built from the spell-effect blocks; the player's own shows in
  third person and comes off in first. `StatusEffectVfxComponent`, `SpellVfx.StatusAura.cs`.
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
  cell sweeps its cooldown and shows when it is level-locked, has nothing to do or has run out.
  `HotbarComponent`, `HotbarPanel`.
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
- **Casters** — standoff banding, kiting, heal, attack, ward priority; the cast origin pitches onto the target, so a tall caster's bolt does not pass over a 1.8 m player. `EnemyCasterTactics`.
- **Ashen variants** — corrupted versions rolled at spawn. `AshenAffliction`.
- **Dragons** — real four-legged winged bodies of 5 to 22 m with six-zone hit boxes; flight with take-off
  and landing, breath channels leaving the snout (`CastOrigin`), lairs (`LairSpawnComponent`), world-event
  dragon hoards. Bite and wing arcs reach the ground at any capsule height. An airborne breath cannot
  reach the ground at the current hover heights. `FlightComponent`, `BreathComponent`, `DragonMeleeComponent`.
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
  conversation. An NPC's look is the generated body its scene node instances (assigned by role; the old outfit kit is gone). `ScheduleComponent`.
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
- **Quality tiers** — Performance / Low / Medium / High / Ultra. A tier sets render scale, sun shadow
  distance, cascades and filter, shadow atlases, SSAO, SSIL, volumetric fog, SSR, glow, particle
  scale and mesh LOD; the two lowest also shorten draw distance, thin ground cover and stop enemy
  shadows at range. `RenderQualityResource` (`data/rendering`), `WorldQualityScale`;
  [`RENDERING.md`](RENDERING.md) has the table.
- **Distance level of detail for actors** — past 40 m a body's animation steps every third frame
  (never during an action clip); a standing townsperson and an actor with full pools and no status
  stop ticking until something changes. `CharacterAnimationComponent`, `ScheduleComponent`,
  `StatsComponent`, `StatusEffectsComponent`.
- **Corruption appearance** — the player's body shifts with corruption tier (ash, skin wash, ember glow via the body shader).
  `CorruptionAppearanceController`.
- **Magic colour** — one hue per school for every spell effect. `SpellSchools.Color`; `VfxPalette`
  derives each school's bright core, mid and edge colours from it.
- **The player's body colour** — the player's atlas is drawn as emission, the way every other
  Meshy body's stock material draws it, so the player is no longer a dark silhouette beside the
  NPCs. `player_body.gdshader` (`emission_amount`), `PlayerAppearance`.

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
- **Spell sounds** — 25 synthesised cues: a cast, an impact and a blast for each of the six
  schools, plus a wind-up riser, fizzle, ward break, freeze, heal, blink and thunder. The cast cue
  plays when the spell leaves the hand, the riser is pitched to end at release, enemy casts are
  3 dB down, channels and zone pulses are quieter and rate-limited, and identical cues within
  40 ms fold into one. A landed spell hit plays its school's impact in place of the melee hit cue.
  A hard limiter holds the SFX bus just under full scale. `SpellAudio`, `AudioDirector`,
  `AudioBusLayout`, `tools/gen_spell_sfx.py`. *Partial:* nobody has listened to them.
- *Partial:* CC0 assets where registered, procedural placeholders elsewhere (`ProceduralAudio`);
  no voice acting; audio production (Phase 52) not done.

## UI, HUD and meta-shell

The 2026-10 UI upgrade rebuilt this whole section. It was rendered by the shot harnesses and has
not been played; [`NOW.md`](NOW.md) lists what is unverified, and [`UI_STYLE.md`](UI_STYLE.md) §13
maps the code.

- **Boot splash** — the seal and "Press any button", once per launch; never shown to a headless,
  isolated or flagged run. `BootSplash`, `ShellFrontRules.Attended`.
- **First-run setup** — a first launch with no saves offers subtitles, text size, high contrast,
  reduced motion and colour vision, each applied live beside a sample. `FirstRunSetup`,
  `ShellFrontRules.FirstRun`.
- **Title** — a painting for the act the newest save has reached (four variants; slow drift and
  embers, held still by Reduced Motion or the static-background setting), Continue naming its
  save (character, level, region, playtime) or disabled with the reason, New Game, Load Game,
  Settings, Accessibility, Credits, and Quit behind a confirm. `MainMenu`, `TitleBackdrop`.
- **Character creator** — a step rail (race, appearance, background, name), a summary strip of
  the combined stat nudges, a turntable preview with three light rigs, face framing and an idle
  pose, and Randomise look. `CharacterCreator`, `CharacterPreview`, `CreatorRules`.
- **Save slot rows** — thumbnail, kind (manual, quick, auto) as a glyph and a word, character,
  region, level, corruption tier, playtime and local date, with the damaged, newer-format and
  backup badges; delete and overwrite are a hold. `SaveSlotPanel`, `ShellSessionRules`.
- **Loading screen** — the destination realm's painting and name, a progress line fed by the
  loading gate's four stages, and one tip or a lore card for a realm already discovered; no press
  to continue. `LoadingScreen`, `LoadingProgressEvent`, `LoadingCardRules`.
- **Pause sheet** — the tracked quest's current objective and time played over Resume, Save, Save
  to Slot, Load, Settings, main menu and quit. `PauseMenu`.
- **Death screen** — the frame darkens over one line, then Rise or Load last save. Death itself is
  unchanged: the player is back at the region's spawn the same frame, with no penalty; Rise only
  lifts the screen. Not shown to an automated run or under another menu. `DeathScreen`,
  `PlayerHost`.
- **Credits screen** — a scrolling roll from the title: speeds up while held, moves by hand, and
  returns to the title at its end. The ending still plays its own credits cards. `CreditsScreen`.
- **Narration** — cards on black; the two endings play over their own paintings; skip is a hold,
  and pausing asks Resume or Skip. `NarrationSequence`.
- **Hold to confirm** — delete or overwrite a save, reset settings or bindings, respec, and skip a
  narration fill a ring over 0.9 s; an accessibility setting makes each a single press. `HoldRing`,
  `UiFx`.
- **HUD** — vitals with level and a corruption row, prepared spell, quest tracker, time and
  weather, event banner, target plate, prompt, crosshair, minimap, compass, party, hotbar, boss
  bar. `GameHud`, `BossFrame`, `Nameplate`, `HotbarPanel`.
- **HUD presets and per-element modes** — Full, Dynamic or Minimal, or always / dynamic / hidden
  for each of fifteen elements (the spell wheel is the fifteenth, and stays on under Minimal). A
  dynamic element shows in a fight, while a pool is below max,
  for four seconds after it changes, and while `N` is held. `HudOptions`, `HudDynamicRules`,
  `GameHud.Options`.
- **HUD scale, opacity and safe zone** — 0.75 to 1.5, 0.3 to 1, and up to a tenth of the screen
  inset from every edge. `HudLayout`, `HudMetrics`.
- **Vitals** — bars straight on the world with notches (health at 15% and 30%, stamina at the
  winded mark, mana at the prepared spell's cost), a pale chunk for the length a hit just removed,
  and a mark that changes shape when low or winded. `GameHud.Vitals`, `JuicedBar`.
- **Spell row** — the prepared spell's glyph on its school disc, the cast key, name, cost and
  state; the disc goes dark when the cast would not go through. With two or more spells known a
  line above it shows the wheel key ("Hold: wheel", or "Press: wheel" with presses in place of
  holds) and a ghost of the spell a tap swaps back to. `GameHud.Vitals`, `SpellDisc`,
  `SpellPinRules`.
- **Spellbook pins** — the spellbook's top row is the eight wheel favourites, numbered clockwise
  from the top; each known spell's card has Prepare and Pin. Choosing a slot first makes the next
  pin replace it; with all eight full and none chosen the pin is refused. A miniature of the wheel
  beside the slots shows where each number sits on it (slot 1 straight up, then clockwise), each
  dot in its spell's school colour or an empty socket, the chosen slot ringed. `SpellbookPanel`,
  `SpellPinDial`, `SpellPinRules`.
- **Hotbar cells** — the item's picture, key glyph, count, a cooldown wipe with its last nine
  seconds counted, and ready, unusable, level-locked and run-out states. `HotbarPanel`,
  `HotbarRules`.
- **Quest tracker** — at most three objective lines, then "+N more"; it steps aside for the boss
  bar on a narrow screen. `GameHud.Tracker`, `TrackerFoldRules`.
- **Enemy plates** — up to eight health and poise bars over the enemies in the fight, kept six
  seconds after the last blow and faded by 36 m; never doubled with the boss bar or the target
  plate. `EnemyPlateLayer`, `EnemyPlateRules`.
- **Subtitles** — captions for companion barks and boss intro lines (there is no voice acting):
  two-line pages above the hotbar, with size, plate opacity and speaker names as settings; they
  wait out menus. `SubtitleLayer`, `SubtitleRules`.
- **Notifications** — toasts and banners. A toast stays three seconds or a quarter-second a word,
  whichever is longer, times a setting; repeats fold into a count; in a fight only warnings show
  and the rest wait for the last opponent to fall; a hidden feed still shows warnings; a quest
  toast names the journal key. `Notifications`, `Toast`, `ToastRules`.
- **Input glyphs** — every prompt and legend draws the bound key, or the pad button in Xbox,
  PlayStation or by-position shapes, and follows a device change or a remap. `UiGlyph`,
  `UiGlyphRules`.
- **Panels** — character (pack, materials, progression, perks, guilds), spellbook, journal, map,
  bestiary, dialogue, vendor, crafting, storage, appraisal, contract board, save slots, settings,
  pause. `UiPanel`, `UiTheme` (`src/UI`).
- **Hub strip and legend** — Character, Spellbook, Journal, Map and Bestiary share a strip: `Q`/`E`
  or LB/RB step between them without unpausing, `Z`/`C` or LT/RT step a screen's own tabs, and
  every screen lists its verbs in a footer legend. `HubStrip`, `UiLegend`, `UiPanel`.
- **Item icons** — 79 painted archetype pictures on one atlas, chosen from the item's id; an id
  nothing recognises keeps its category glyph. `ItemIcons`, `ItemIconRules`,
  `tools/pack_ui_atlas.gd`.
- **Item slots and detail card** — rarity as frame, ticks and word; a pip on items new since the
  pack was last closed; worn, locked and junk marks; a card with one hero number, signed deltas
  against the worn piece, affixes, set and unique text. `ItemSlot`, `ItemDetailCard`,
  `ItemPresentation`.
- **Compare** — `Shift` or L3 flips any item card to the candidate and the worn piece side by
  side; focusing an equipment slot narrows the pack to what fits it and shows the stats after the
  swap. `ItemDetailCard`, `InventoryPanel`, `StatsPresentation.DerivedDelta`.
- **Perk tree states** — locked, available, owned and maxed nodes each have a frame and a mark,
  the route from owned perks to the focused one is lit, the d-pad follows prerequisite lines, a
  refused press says why, and respec is a hold. `PerkTreePanel`, `PerkTreeRules`, `PerkNodeMark`.
- **Trade screens** — vendor (wares, pack, detail; both purses; an order bar; a price ledger of
  every reason; sell all junk behind a confirm), crafting (recipes craftable first, ingredients
  with have and need, the result with compare, craft max; Craft, Reforge and Salvage as tabs),
  storage (pack, chest, detail; deposit all materials; take all), appraisal and the contract
  board with risk chips. `VendorPanel`, `CraftingPanel`, `StoragePanel`, `AppraisalPanel`,
  `ContractBoardPanel`, `TradeRules`.
- **Map screen** — a smoked-vellum plot with a scale bar, a rail with Place and Legend tabs where
  the legend filters pins by group and category, a pad cursor that snaps to the nearest pin, zoom
  on the sub-tab keys, and a confirm card before every fast travel. `MapScreen`, `MapView`,
  `MapSnapRules`.
- **Journal and bestiary pages** — journal sections as tabs, drawn objective marks, and Show on
  map; bestiary entries sealed until sighted, then a kill tally, then resistances and lore.
  `QuestLogPanel`, `BestiaryPanel`, `BestiaryFactRules`.
- **Dialogue typewriter** — a lower-third window; the line writes itself at 48 characters a
  second and the options arrive when it finishes; a press finishes the line without choosing;
  options carry a mark for plot, consequence, already asked and leave. Off under Reduced Motion.
  `DialoguePanel`, `DialoguePaceRules`.
- **UI audio cues** — click, focus, confirm, back, tab, open, close, denied and hold tick, one
  sound per action, with a title music bed and a low-pass over the world while a menu pauses it;
  all procedural. `UiAudio`, `UiAudioRules`.
- **Settings** — six tabs (Graphics, Audio, Controls, Gameplay, Interface, Accessibility) with a
  description pane and previews, a restore-default button on each changed row, and hold-to-reset
  for a tab or for everything (which keeps bindings and accessibility options). Window mode,
  vsync, FPS cap (uncapped, 30, 40, 60, 120, 144), quality preset, volumes, mouse sensitivity,
  invert Y, camera mode and shoulder, tutorials. `SettingsPanel`, `SettingsTabRules`, `Settings`,
  `SettingsService`.
- **Key and gamepad remapping** — 31 actions, each with a keyboard and a pad binding; a clash
  offers swap, unbind the other, or cancel; Esc, Enter, Tab, the arrows, the function keys, Start
  and Guide cannot be bound; pad movement and the pad hotbar chord are fixed. Saved with the
  settings and applied at boot. `InputBindingRules`, `GameInput.ApplyBindings`.
- **Pad look sensitivity** — separate horizontal and vertical multipliers, 0.25 to 3.
  `PlayerLookInput`, `SettingsMath`.
- **Difficulty** — Story, Normal or Hard scales the damage of a blow that lands on the player
  (melee, ranged, spell hits and detonations) by 0.6, 1 or 1.35. Nothing else moves: not damage
  over time, falls or reflected damage, not what the player deals, not health, loot or
  experience. `DifficultyRules`, `SettingsService.IncomingDamageScale`,
  `CombatComponent.ReceiveDamage`.
- **Accessibility options** — text size, UI scale, colour-vision modes, high contrast, reduced
  motion, a readable font everywhere, presses instead of holds, and the subtitle options.
  `SettingsPanel.Accessibility`, `UiTheme`.
- **Advanced graphics** — render scale, upscaling (bilinear, FSR 1.0, FSR 2.2), anti-aliasing (off,
  FXAA, MSAA 2x/4x, TAA), shadow quality, ambient occlusion, volumetric fog and glow, each starting
  from the preset; a moved control makes the preset read Custom. `SettingsPanel`, `GraphicsMath`.
  Spell effects is the exception: it has its own Follow preset entry, so choosing a preset leaves
  it alone and moving it never makes the preset read Custom.
- **First-run graphics detection** — a fresh install (no settings file, no saves) starts on the
  preset its adapter, memory and thread count earn; never re-run over a saved file.
  `GraphicsAutoDetect`.
- **Menu frame pacing** — with V-Sync off and no cap, the title and pause screens hold 60 FPS.
  `SettingsService.ApplyFrameCap`.
- **Gamepad** — plays the whole game, menus included. *Partial:* no pad has been physically
  driven through the new screens or the remapping flow. `GameInput`, `InputDevice`.
- **Localization** — every player-facing string through `Loc.T` and `data/locale/strings.csv`
  (English only). `Loc`, `LocaleAudit`.

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

What exists, one tool per entry; [`TOOLING.md`](TOOLING.md) has the invocations, flags and output
shapes. "Tooling build" is the Debug assembly: `ExportRelease` compiles none of the harnesses
(`tools/check_shipping_assembly.py`). Session flags (`--play`, `--new-game`, every harness flag) go
after `--`. A machine-readable tool ends with one line of a name and a JSON object:
`EMBERVALE_RESULT` from the game, `PROBE`, `REGEN`, `WORLD_BAKE`, `WORLD_STATIC`, `MESHY`.

### In-game overlays and the dev console

- **Dev console** (`F1`) — 71 commands; `help [prefix]` lists them. A command returns a typed result
  (ok, text, optional JSON data), and `--json` on a line makes the reply the JSON. `DevConsole`,
  `ConsoleCommand`, `ConsoleText`, `DevCommands` (partials `.Movement`, `.Combat`, `.World`, `.Query`).
- **Movement and spawn commands** — `pos`, `tp <x> <z>|cell|loc|region|out` (`tp out` lands outside
  every safe zone), `face`, and `spawn <templateId> [n]` through the encounter director's placement
  rules, with `spawn list`. `DevCommands.Movement.cs`.
- **Combat commands** — `enemies`, `killall`, `hurt`, `god` (a health multiplier; hits still land).
  `DevCommands.Combat.cs`.
- **World commands** — `time`, `timescale`, `weather`, `event`, `inv`, `take`, `save`, `load`, `log`.
  `DevCommands.World.cs`.
- **Query commands** — `get <key>...` (state, world, time, enemy and player keys, and the families
  `flag.` `quest.` `item.` `spell.` `rep.` `stat.`), `dump player|enemies|entity|world|saveables`,
  `hud` (the debug HUD as text). `DevCommands.Query.cs`.
- **Console reference** — `--console-help[=md|json]` prints every command, runner verb and `get` key
  from the binary, with no session. `ConsoleHelp`.
- **Debug HUD** (`F3`) — diagnostics with region, active and resident cells, position and SAFE/WILD;
  `Snapshot()` is the same text for the console. Single-key cheats `H`, `R`, `X`, `P`, `K`. `DebugHud`.
- **Profiler overlay** (`F4`) — FPS, median and worst frame of the last 120, script and physics
  time, draw calls, primitives, nodes, orphans, static memory, video memory (textures, buffers),
  managed heap, allocation rate, collections per generation, then world nodes and scatter, world
  frame p50/p95/p99, active and resident cells, and the sky's tier and weather state. Refreshed
  four times a second; not processed while hidden. `ProfilerOverlay` formats one `ProfilerReading`,
  the reading `--perf-report` also writes; `WorldPerformanceMonitor`.
- **Overlay host** — the console, HUD, profiler and integrity checker are children of the session's
  `DeveloperTools` node; a `--capture` run or an export has none of them. `DeveloperToolsHost`.

### Shell-driven console

- **`--exec` / `--exec-file`** — runs a console script against a live session and quits: statements
  split on `;` and newlines, checked before the first runs, one a frame, each waiting for a usable
  world. Writes `console/result.ndjson` and an `EMBERVALE_RESULT` line (gate `exec`); exit 0 or 1.
  Tooling builds. `ConsoleScript`.
- **Runner verbs** — `wait <seconds>` (game time), `frames <n>`, `wait-until`, `assert` (ops
  `eq ne gt ge lt le contains` over the `get` keys), `expect`, `shot`, `input`, `quit`.
  `ConsoleScript`, `ConsoleHelp.RunnerVerbs`.
- **Command families** — movement, combat, world and query above, plus the older state commands
  (`give`, `xp`, `flag`, `quest`, `story`, `guild`, `perk`, `learn`, `companion`, `shop`, `economy`,
  `settings`, `validate-all`, `invariants`, `repro`). `DevCommands.cs`.
- **Scenario `console` op** — one console line from an SDK scenario plan, through
  `DevConsole.ExecuteJson`. `tools/headless/driver.gd`.
- **Session entry flags** — `--play`, `--slot=<name>`, and in tooling builds `--new-game` (refused
  without an absolute `EMBERVALE_USER_DIR`) and `--quit-after=<seconds>`. `GameShellController`,
  `SessionEntryRules`; the harness table is `SessionHarnesses`.
- **Stale-build guard** — a run on the engine binary whose `Embervale.dll` is older than
  `src/**/*.cs` or the csproj warns `STALE_BINARY`; `--strict-build` makes it exit 1.
  `BuildFreshness`.

### Headless gates (any build unless noted)

- **Gate contract** — quiet unless `--verbose`; each failure once as `[ERROR] <gate>: <message>`,
  the `<gate>: PASS|FAIL` line, then one `EMBERVALE_RESULT {json}` line, also written to
  `--report=<path>`. Exit 0 pass, 1 failed, 2 refused. `HeadlessGate`, `HeadlessReport`.
- **Flag check and `--gates`** — a misspelt or contradictory mode flag exits 2 with the flag it
  probably meant; `--gates` lists every mode and its options. `HeadlessFlags`, `HeadlessArgs`,
  `CommandLineArgs`.
- **`--validate`** — the content gate, 57 arms in one table; `--list`, `--only=<group|arm>`,
  `--skip=`, `--slowest=N`. A filtered run is marked partial. `HeadlessValidation`,
  `ContentValidator.Arms`, `ValidatorArmFilter`.
- **`--state`** — the census; `--ids=<kind> [--match=]` lists ids, `--get=<id>` prints one resource.
  `HeadlessState`.
- **`--economy`**, **`--worldgen`** — the arbitrage table (`--top=N`) and the generator report
  (`--region=<id>`); reports, not gates. `HeadlessEconomy`, `HeadlessWorldGen`.
- **`--lifecycle`**, **`--save-reload`** — New Game, save, destroy, load round trips failing on a
  leak (`--cycles=N`), and the quick-load audit (needs an absolute `EMBERVALE_USER_DIR`).
  `HeadlessLifecycle`.
- **`--story`** — plays the 30 missions in runs A, B and C on isolated saves; `--story-only=`,
  `--story-list`, and `--story-mission=<n|id|a..b>` for one stretch from a fixture (partial).
  `HeadlessStory`, `StoryPlaythrough`, `StoryMissionRange`, `LegacyFixtures`, `StoryDriver`.
- **`--world-bake`**, **`--worldmap`** — the engine halves of the bake and the map renderer; they
  parse their own arguments and print no result line. `HeadlessWorldBake`, `HeadlessWorldMap`.

### The arena (tooling builds)

- **`--arena=<roster>`** — a bot plays the player against enemy templates through the real input
  actions and damage pipeline, and reports wins, time to kill, damage both ways, swings, hits, blocks
  and flags per matchup. Rosters: ids, `id*3`, `encounter.*`, `all`, `bosses`; `--arena --list`.
  `HeadlessArena`, `ArenaRunner`, `ArenaMath`.
- **Arena options** — `--trials`, `--seed`, `--level`, `--weapon`, `--equip`, `--setup`, `--policy`
  (aggressive, guard, passive), `--count`, `--distance`, `--max-fight-s`, `--speed`, `--budget-s`.
  Its numbers compare builds with each other; they are not a difficulty verdict. `ArenaRunner`.

### Render harnesses, one-off shots, filmstrips and image analysis

- **Render harnesses** — shell, meta, HUD, combat, panel, UI-audit and trade shots
  (`--shellshots`, `--metashots`, `--hudshots`, `--combat-shots`, `--panelshots`, `--uishots`,
  `--tradeshots`), each checking the state it photographs; guild, shrine, enemy and look shots.
  `*Shots.cs`, `ShotHarness`.
- **Shared harness options** — `--only=<names, wildcards>`, `--list`, `--no-thumbs`,
  `--shots-verbose`; every run writes `manifest.json`, a thumbnail per shot and one
  `EMBERVALE_RESULT` line (gate `shots`), with notes for flat, black, magenta and duplicate frames.
  `ShotHarness`, `ShotCoreShots.cs` (`ShotFilter`, `ShotManifest`).
- **Spell and camera harnesses** — `--spellshots` casts every spell through the real cast button
  and photographs wind-up, release, impact and linger in both views, and has a humanoid enemy cast
  three of the player's own spells at the first-person player (`efp`); `--camshots` photographs both
  views at every gait, a charge, a channel and looking down and up, and logs where the feet sit
  down the frame; `--enemy-shots` adds idle, walk and run phases for the humanoid enemies.
  `SpellShots`, `CamShots`, `EnemyShots`, `TimedShots`.
- **One-off shot** — `--shot` photographs one world view (place, camera, hour, weather), one UI
  state (`--ui=<suite>/<shot>`) or one spell phase (`--spell= --phase=`); a JSON spec holds many.
  `OneShots`, `OneShotSpecShots.cs`.
- **Filmstrip** — `--film[=FRAMESxSTRIDE]` on the timed harnesses and `--shot` writes the frames
  before a capture as one `<shot>.film.png`, for judging motion. `--direct-input` casts without the
  button, and a focus loss is recorded per shot. `TimedShots`.
- **World shots** — the approach shots per cell against the approved baseline, with `--region`,
  `--cell`, `--pass`, `--view`, `--res`, `--list`. `tools/world_shots.gd`.
- **Image analysis** — `stats`, `diff` (changed boxes and a before, after, heatmap triptych),
  `sheet` (labelled contact sheets) and `thumbs`. `tools/shot_analyze.py`;
  `tools/make_contact_sheet.py` forwards to it.

### Profiling, the perf report and baselines

- **Session perf report** — `--perf-report[=seconds]` samples every frame of a live session and
  ends with one verdict: frame percentiles and hitches, script and physics time, draw calls, memory,
  GC, world budget, integrity, invariant and log counts. `SessionPerfReport`, `FrameStats`,
  `ProfilerReading`.
- **`--vfxperf[=tiers]`** — eight casters on a loop, one `vfxperf_<tier>.json` per effect tier in
  one boot, plus a summary and a result line. `VfxPerfScenario`.
- **Perf comparer and baselines** — one comparer for every perf JSON against a machine-keyed
  baseline, `tests/performance_baselines/<suite>/<key>.<machine>.json`; exit 5 on a regression.
  `tools/perf_compare.py`.
- **Per-cell and streaming probes** — render cost per cell (`--region`, `--cell`, `--top`,
  `--json-file`); streaming activation times and growth per realm. `tools/world_perf_probe.gd`,
  `tools/world_streaming_stress_probe.gd`.
- **Mesh census** — meshes and multimesh instances per cell, diffable against a baseline
  (`--baseline`, `--strict`). `tools/cell_mesh_census.gd`.

### Integrity, flight recorder and repro files

- **Integrity checker** — every 5 s, from the `invariants` command and from `--perf-report`; issues
  carry codes (`player.fell`, `player.gold`, `enemy.position`, `streaming.failed_cells`,
  `pause.tree_running`, `orphans.leaked` and others). `WorldIntegrityChecker`, `IntegrityReport`.
- **Flight recorder** — a 256-entry ring of recent events, written as `flight_<stamp>.jsonl` beside
  the analytics log on a session's first invariant violation. `FlightRecorder`, `AnalyticsSink`.
- **Repro files** — six console scripts in `tools/repro/` (swarm, rich, duskstorm, raid, party,
  shopping), run by `repro <name>` in the console or `--repro=<name>` from a shell. They stage a
  situation; they are not replays. `ReproHarness`, `ReproRun`.

### Analytics

- **Analytics sink** — a dev-only event log, not state: `session_<stamp>.jsonl` under
  `<EMBERVALE_ARTIFACTS>/analytics/` or the user directory, with session start and end, deaths,
  kills, gold by source and quest times; damage is totalled, never written per hit.
  `src/Analytics` (`AnalyticsSink`, `AnalyticsAggregate`).
- **Analytics summary** — `python tools/analytics.py summary` reads those logs into a few lines.
  `tools/analytics.py`.

### The Python SDK (`python tools/embervale.py`)

- **SDK** — one entry point for build, import, validate, test, scenario, screenshot, perf, world
  gates, assets and the commands below; every run has a run directory, an isolated user directory
  and a one-line verdict. A subcommand is a module in `tools/embervale_sdk/commands/`.
- **`verify`** — maps the git diff to the smallest gate set; `--plan` prints it with cost estimates
  and runs nothing. `commands/verify.py`.
- **`gate`** — runs one headless mode and folds its result line into the run. `commands/gate.py`.
- **`run`** — runs a headless mode or any session harness flag with the SDK's guards.
  `commands/run.py`.
- **`console`** — runs a console script in an isolated new game or a copied save slot.
  `commands/console.py`.
- **`shots`** — runs a render suite or one `--shot` under a fixed step, then contact sheets and a
  comparison with the previous run. `commands/shots.py`.
- **`perf-report`**, **`vfxperf`** — the two measurements above with a baseline verdict.
  `commands/perf_report.py`, `commands/vfxperf.py`.
- **`job`** — detached background runs with status, ETA, wait, tail and cancel. `commands/job.py`,
  `jobs.py`.
- **`logs`** — groups a log's errors and warnings with counts and first location, known noise
  removed. `commands/logs.py`, `triage.py`, `tools/headless/known_noise.json`.
- **`doctor`** — versions, stale binary, free memory, stray processes, the heavy lock, the import
  cache, an interrupted negative battery. `cli.py`.
- **`last`**, **`clean`** — reprint the newest run's verdict; prune old run directories.
  `commands/last.py`, `commands/clean.py`.
- **Gate cache** — a gate that passed on exactly these inputs is recorded as `cached`, not run.
  `cache.py`, `artifacts/.gate-cache.json`.
- **Heavy-run lock** — one engine run at a time across every checkout and worktree; a second
  exits 2, or waits with `--wait-lock`. `heavy.py`.
- **SDK stale-build guard** — rebuilds a stale `Embervale.dll` before an engine launch (step
  `auto-build`); `--no-build` opts out. `freshness.py`.

### Generators, content tools and probes

- **Generators** — regions, map locations, the campaign, guild dialogue, perks, appearance, buildings,
  the world bake, and the item catalogue (`gen_items.py`, `gen_recipes.py`, `items/gen_loot.py`).
- **`regen.py`** — checks or regenerates every generator in dependency order (`--check`, `--fix`,
  `--stale`, `--list`, `--only`), with a cache. `tools/regen.py`; world gate `generators`.
- **Content tools** — `refs` (who defines and uses an id, path or model), `census`, `diff` (content
  changes between two revisions) and `balance` (estimate tables with outlier flags), with no engine.
  `tools/content.py`, `tools/tres_reader.py`.
- **World bake and its progress file** — incremental per region; `--plan` says what would bake and
  why, `--status` reads `artifacts/world_bake/status.json`, `--resume` keeps finished regions.
  `tools/world_bake.py`.
- **Static world checks** — seams, layout and composition for every region in one process.
  `tools/check_world_static.py`.
- **Meshy batch** — `--status`, `--dry-run`, a polling limit, and finished items appended to the
  ledger. `tools/meshy_batch.py`.
- **Probes** — GDScript probes under `tools/*.gd`, run with `--script`; the shared base gives
  arguments, checks, metrics and a `PROBE {json}` line (`magic_lifetime_probe.gd`,
  `region_transition_probe.gd` and the mesh census use it). `tools/probe_base.gd`.
- **Negative battery** — breaks the content rules on purpose and expects each refusal; it journals
  the files it mutates so an interrupted run can be restored. `tools/negative_tests.py`.

## Deliberately absent

- Survival needs — hunger, thirst, temperature, durability, repair, encumbrance (Phase 40, struck).
- Puzzles, traps and a dungeon framework (Phase 40.5, struck).
- Swimming, climbing, crouch/sneak movement (stealth exists only as a quest objective rule).
- Sockets and gem enchanting (struck; reforging at a forge is the only way to change a finished
  item), a lore codex, photo mode, cinematics beyond narration cards.
- A second vault or warehouse: storage is the bank, property stashes and the material bag.
- Extra locales; storefront, platform and live-ops features.
- Voice acting, a death penalty, Steam Input glyphs, a live 3D title scene.
