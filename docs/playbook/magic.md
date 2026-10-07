# Magic upgrade — integrated implementation (2026-10-02)

The core, status, learning and content work is integrated. This page records the implemented contract,
spell registry and regression coverage. Current verification and remaining human review live in
[`../NOW.md`](../NOW.md); system ownership is in [`../ARCHITECTURE.md`](../ARCHITECTURE.md#213-magic-srcmagic).

## Cast rules

Committed and readable, the way Combat now is. A cast has a visible wind-up (`WindupSeconds`), a release
frame the animation and the spell agree on, and a recovery. A stagger during the wind-up cancels the cast
(poise is symmetric; `Interruptible = false` and Barkskin opt out of stagger interruption). Silence,
Stun and death cancel even those protected casts. Release and channel ticks recheck interruption;
they do not wait for an idle-frame poll. Death cancels even when the player respawns immediately.
Spells are blockable and never parryable (`Blockable = false` for the few unblockable ones).
Ground and barrier spells are read from a telegraph
ring exactly as long as their delay. An enemy's wind-up warning takes the school of the spell it is
winding up as its body colour (`TelegraphComponent` on `CastWindupStartedEvent`); its shape, size
and timing do not change. Presentation never changes a rule: it reads events, and everything
that punctuates a blow for feel scales by `CombatComfort` (Reduced Motion caps hit-stop and flash at 25%).
The shared combat pipeline owns damage, guard and poise. Magic uses `HitKind.Spell`, `DamagePacket`,
`CombatComfort`, `TelegraphComponent`, `DamageNumberLayer` and typed combat/magic events.

Mana, health cost and cooldown are paid when a committed cast starts. An interrupted wind-up refunds
half its mana; voluntary cancellation keeps the committed cost. Charged input determines power,
piercing and authored status-duration bonuses without changing a shared resource. Channels start
after wind-up, hold the action and animation at release, and resume the authored recovery on key-up.
Blink commits its direction and affordable distance at start, then refunds any portion blocked before
release. Sunfall's direct central hit explicitly crushes guard; its surrounding blast does not.

## The 25 player spells

Ids are stable. School is the `DamageType`. A replacement preserves the old id through `SpellAliases`.

| # | Id | Name | School | Delivery / mode | The one thing it does |
| --- | --- | --- | --- | --- | --- |
| 1 | `spell.emberlash` | Emberlash | Fire | Projectile, instant, replaces `spell.firebolt` | Fast, cheap bolt that leaves a Kindled stack; at 3 stacks the Kindle detonates |
| 2 | `spell.flame_lance` | Flame Lance | Fire | Projectile, charged | Pierces; a fuller charge pierces more foes and burns longer |
| 3 | `spell.pyre_wall` | Pyre Wall | Fire | Barrier | Line of fire: burns what crosses it, eats arrows, does not stop bodies |
| 4 | `spell.sunfall` | Sunfall | Fire | Ground, charged, replaces `spell.fireball` | Meteor at the aim point after a telegraph; crushes guards on a direct hit |
| 5 | `spell.rime_shard` | Rime Shard | Frost | Projectile, instant | Chills; a shard into a chilled foe freezes it (Frost identity) |
| 6 | `spell.frost_nova` | Frost Nova | Frost | Area | Ring around you: chills everything; anything already chilled freezes |
| 7 | `spell.blizzard` | Blizzard | Frost | Area with zone | Lingering storm that slows and stacks chill; you can be caught in your own |
| 8 | `spell.glacial_bulwark` | Glacial Bulwark | Frost | Barrier | Solid ice wall that stops bodies and projectiles, breakable, Shatter-able |
| 9 | `spell.ball_lightning` | Ball Lightning | Lightning | Projectile, homing | Slow homing orb that chains when it lands; it prefers a Stormbranded foe |
| 10 | `spell.storm_conduit` | Storm Conduit | Lightning | Projectile, channelled | Held beam; slowed and interruptible while it runs, and it chains to a Stormbranded foe |
| 11 | `spell.thunder_step` | Thunder Step | Lightning | Dash | Dash along the aim striking what you pass; the last foe struck is stunned (`status.stunned`) |
| 12 | `spell.stormbrand` | Stormbrand | Lightning | Projectile, instant | Brands a foe; every lightning hit within range arcs to the brand |
| 13 | `spell.null_lance` | Null Lance | Arcane | Projectile, instant, replaces `spell.arcane_lance` | Dispels a benefit and silences: it interrupts a caster mid wind-up |
| 14 | `spell.arcane_shield` | Arcane Shield | Arcane | Self | Ward that absorbs a fixed amount and breaks with a flash; an unspent ward returns a share of its mana |
| 15 | `spell.blink` | Blink | Arcane | Self | Teleport along the aim, stopped short by a wall; a shorter jump costs less mana |
| 16 | `spell.gravity_well` | Gravity Well | Arcane | Ground | Pulls foes to the centre after a telegraph; stacks with Blizzard and Pyre Wall |
| 17 | `spell.mending_bloom` | Mending Bloom | Nature | Self, instant, replaces `spell.lesser_heal` | Heal now plus a Regrowth tail |
| 18 | `spell.lifebloom_totem` | Lifebloom Totem | Nature | Self with totem | Totem heals over time and can be destroyed |
| 19 | `spell.thornsnare` | Thornsnare | Nature | Ground | Roots foes in the area (Root), with control immunity afterwards |
| 20 | `spell.stinging_swarm` | Stinging Swarm | Nature | Projectile, instant | Swarmed DoT that jumps to a nearby foe when its bearer dies |
| 21 | `spell.barkskin` | Barkskin | Nature | Self | Armour up for a few seconds, and casting is hyperarmoured while it lasts (a stagger cannot cancel a cast) |
| 22 | `spell.ember_siphon` | Ember Siphon | Necrotic | Projectile (corruption tier 2) | Lifesteal bolt; heals more the lower the target's health |
| 23 | `spell.soul_tithe` | Soul Tithe | Necrotic | Projectile, `HealthCost` | Pay health for a big hit; refunds mana only for its own killing hit |
| 24 | `spell.knit_bone` | Knit Bone | Necrotic | Self | Cleanses a Decay affliction on you and heals more for each stack it removed |
| 25 | `spell.grave_mark` | Grave Mark | Necrotic | Projectile, instant | Marks a foe: damage taken rises, and Necrotic lifesteal off a marked foe doubles |

Retired ids (their `.tres` are deleted, `SpellAliases` resolves them): `spell.firebolt`, `spell.fireball`,
`spell.arcane_lance`, `spell.lesser_heal`. Enemy-only spells remain
`PlayerLearnable = false`: `spell.ash_breath`, `spell.dragon_breath`, `spell.drake_breath`,
`spell.elder_word`, `spell.wither`.

Spell numbers are authored in `data/spells`; the route validator and pure rules tests enforce numeric
bounds. Probe coverage establishes behavior, while combat feel and balance still need play-through.

## Status and school rules

`status.kindled`, `status.rooted`, `status.silenced`, `status.stormbrand`, `status.barkskin`,
`status.grave_mark`, `status.swarmed`, `status.soul_echo`, `status.stunned`, `status.burning`,
`status.chill`, `status.frozen`, `status.decay`, `status.regrowth`, `status.arcane_ward`.

`StatusEffectsComponent` owns stack refresh, detonation, death spread, wards, controls and immunity.
Refresh retains tick cadence and the stronger remaining lifetime. Each damage/tick/spread invocation
owns its iteration snapshot so ward breaks and death callbacks can safely reenter the component.
Effects, their modifiers and immunity clear on death and before saveables restore. Status Stun has
its own lifetime beside combat stagger; consuming Frozen releases that stun without altering a poise
stagger. Root and Stun cancel dodge movement and invulnerability.

`SchoolIdentity` and `SpellCombo` read afflictions before the spell adds its own status. Kindle
detonates, Chill escalates to Frozen, Lightning seeks Stormbrand, Arcane projectiles dispel benefits,
and Necrotic lifesteal uses actual health damage and the target's mark/health outcome. Damage and death
are snapshotted before event handlers can respawn a player; a lethal hit does not afflict that revived
actor. Six combos are declared in `SpellCombo.All`. Knit Bone consumes Decay stacks before healing.

## Delivery lifetime and restore

`SpellProjectile`, `SpellGround`, `SpellZone`, `SpellBarrier` and `SpellTotem` use `SpellLifetime`.
Deliveries live under the caster's session/world host, become inert before live load, and cancel on
caster removal. Validity checks also cover a caster queued for deletion before its exit signal runs.
A lingering spell from the abandoned timeline cannot land after a quickload or survive quitting to
title. Barrier interception uses the physical volume, including projectile radius and wall thickness.

`SpellcastingComponent.Load` cancels transient casts and replaces known spells, ranks and cooldowns.
Aliases migrate retired ids and deduplicate their replacements. `GameLoadingEvent` strips transient
effects and deliveries before any registered saveable restores, including on a load that later fails.

## Data and event contract

- `SpellDelivery`: `Ground = 4`, `Barrier = 5`, `Dash = 6` (append-only, pinned in `EnumStabilityTests`).
- `StatusControl` flags: `None`, `Root`, `Silence`, `Stun`, `Mark`.
- `SpellResource`: committed-cast, ground, barrier, dash, cost and consume fields. Optional mechanisms
  default inactive; casts default blockable and interruptible, with `ImpactWeight = 0.5`.
- `StatusEffectResource`: `Controls`, `Dispellable`, `ControlImmunitySeconds`, `DetonateAtStacks`,
  `DetonateDamagePerStack`, `DetonateRadius`, `SpreadOnDeathRadius`, `DamageTakenModifier`. Optional
  control/detonation/spread/damage modifiers default inactive; effects default dispellable.
- `StatusEffectsComponent`: `Controls`, `IsSilenced`, `IsRooted`, `IsStunned`, `StacksOf(id)`.
- `SpellAliases` and `SpellDatabase.Get`: retired ids resolve to their replacement.
- Events in `MagicEvents.cs`: `CastWindupStartedEvent`, `SpellInterruptedEvent`, `SpellBlockedEvent`,
  `SpellHitEvent`, `BarrierEndedEvent`, `StatusDispelledEvent`, `StatusDetonatedEvent`,
  `StatusResistedEvent`, `SpellComboEvent`, `SpellLearnedEvent`, `SchoolRankedUpEvent`. The contract
  declares them; the owning behavior publishes them once.
- Player-facing copy uses `Loc.T` and `data/locale/strings.csv`; school, cast/status, learning/book
  and spell-description keys are shared by gameplay and UI.

## Learning and authored routes

Tomes, dialogue and trainers share `SpellLearning.TryLearn`; a learned or refused result publishes
once. Spellbook purchases spend progression points through the component. Corrupted learning requires
its tier and explicit embrace. Mastery saves school points and ranks; held channels can bank at most
one point per spell per second. Power, cooldown and resistance bonuses derive from those ranks.
`Weave` derives from the active region and bounds ordinary/corrupted power and cost multipliers.

The content probe checks the real databases, enemy/boss/race loadouts, aliases, a legitimate route for
every player spell and prepared world tome interactions. Tome scene edits are inputs to the world
bake, so delivery requires one master bake after integration.

## Selecting a spell: the wheel, the tap and favourites (2026-10-06)

`Q` (pad `RB`) casts the prepared spell. `F` (pad `LB`) is one action, `cycle_spell`, with two
meanings: a tap under 0.16 s swaps back to the previous spell, and a hold opens the spell wheel,
steered by the mouse or the right stick and selected by letting go. The inner ring is the eight
favourites, the outer ring the six schools, and the hovered school's known spells fan out past the
rim. Block or the centre cancels. With presses in place of holds the key toggles the wheel and a
second press or Attack selects.

None of it changes a cast rule. `Select` and `SelectPrevious` refuse while a cast is pending,
charging or channelling, as `Cycle` does, so a cast in flight keeps the spell it began with. A
selection publishes `SpellSelectedEvent`; an enemy choosing its spell does not. Favourites and the
previous spell are saved under the `spells` record ([`../SAVE_FORMAT.md`](../SAVE_FORMAT.md)):
learning pins a spell into the first free slot, forgetting clears its slot and the previous
spell, and the spellbook's pin row sets them. The wheel is not a menu: the world runs, and only
look, lock-on, attack, block and cast are lent to it. Ownership is in
[`../ARCHITECTURE.md`](../ARCHITECTURE.md#213-magic-srcmagic).

## Presentation: effects and sound (2026-10-06)

Presentation reads events and never changes a rule; this section is where it lives, not a new
contract. Every spell effect goes through the `SpellVfx` facade (`src/Magic/Vfx`), which is a
no-op on a headless display, so every probe below runs exactly as before. All 30 spells have a
recipe in `SpellVfxCatalog.Elemental.cs` or `SpellVfxCatalog.Arcana.cs` and special-case hooks in
the matching `SpellVfx.Special.*.cs`; `SpellVfxCatalogTests` fails a spell in `data/spells` with no
recipe, so a new spell needs one in the same change. What a hit, a burst or a zone pulse sounds
like is decided by the pure `SpellAudio` and played by `AudioDirector` from `SpellCastEvent`,
`CastWindupStartedEvent`, `SpellImpactEvent` and `SpellBurstEvent`.

A recipe gets its school's look for free: shaped particles, a wind-up motif at the hand and a
shaped bolt head come from the kit with no recipe edit, and a special reaches for the kit's
helpers before building anything of its own (`BodyFit`, `ResidualCrackle`, `TetherScatter`,
`MouthAnchor` / `MouthGlow` / `BreathPuffs`, `Snowfall`, `AshFall`, the optional `TotemPulse`
hook). The header of `src/Magic/Vfx/SpellVfx.Kit.cs` is the API. What the per-spell specials are
sized to, and what the lean tiers leave out, is in the pure `VfxElementalRules` and
`VfxArcanaRules`.

Five things a spell author should know:

- **The picture is not the path.** In first person the player's bolt is drawn starting at a point
  fixed in the view, at the lower left where the left hand is, and settles onto the true path over
  0.25 s. The collision area, `Aim()` and the muzzle offset are untouched.
- **A ring about the player's own feet is cut in first person.** A flat ring or disc centred on
  the camera is drawn at a fraction while it is small (`VfxScreenRules.SelfRing`) and a shimmer at
  the screen's edges stands in for it (`SelfCastInView`, called by the generic release for every
  Self delivery). A telegraph and an enemy's standing zone are never cut. Lay a self-cast out so
  first person sees it: reach past the bottom of the frame, do not rely on the floor ring.
- **A self-buff cast on an ally draws on the caster.** `SpellVfx.Release` is not told a target and
  both call sites pass only the caster. Changing that is a facade signature change in `src/Magic`,
  not something a recipe or a hook can do.
- **A telegraph still runs exactly as long as its delay.** For the player's own ground spells and
  walls the telegraph ring stays armed (the probe reads it) and is hidden while the effect layer
  draws its own; enemy casts keep the ring.
- **`StatusVfx*` node names are a contract.** `magic_status_probe` asserts them, and the richer
  status auras are drawn beside them, never in their place.

The tier table, the shaders and the comfort caps are in
[`../RENDERING.md`](../RENDERING.md#spell-effects). `--spellshots` renders every spell, with three
of the player's own cast back at the first-person player by an enemy (`efp`), and
`--vfxperf` times eight casters ([`../NOW.md`](../NOW.md) → Commands). ⚠️ The effects were judged
from still frames and the sounds from numbers: no motion review, and nobody has listened.

## Regression coverage

Build and import before engine probes; running Godot does not recompile C#. Use the Developer SDK:
`python tools/embervale.py tool <probe-name>`. All five probes are also registered in the engine/full
world suite.

| Probe | Coverage |
| --- | --- |
| `magic_core_probe` | Committed casts, charge, interruption at release/tick, held channel recovery, ground/barrier/dash, costs, cleanse and real roster behavior |
| `magic_status_probe` | Real status components, detonation/spread, mark/lifesteal, wards/dispel, control immunity, rooted movement, combos, VFX and death/load cleanup |
| `magic_learning_probe` | Mastery, Weave clamps, shared learning, corrupted tome confirmation, spellbook/HUD construction and restore |
| `magic_content_probe` | 25 player spells, five enemy spells, aliases/loadouts, legitimate learning routes and prepared tome interactions |
| `magic_lifetime_probe` | Pre-load cancellation and session/caster lifetime for active projectiles, placed spells, barriers, zones and totems |

Pure numeric rules are exercised by `tests/Embervale.Tests`; the SDK has its own Python unit suite.
Read the entire native probe output for script/engine errors: an aborted GDScript function must not
be accepted merely because another path prints PASS. Development probe drivers are excluded from
shipping assemblies.

Automated results and artifact references belong in [`../NOW.md`](../NOW.md). They establish the
checked behavior and content; the maintainer's full play-through, eye-level visual review and boss/
duel balance remain separate open work.
