# Magic upgrade playbook (2026-09)

The brief every agent on the Magic upgrade reads first. The shared contract is already committed on
`magic/integration`; this file says what to build against it, who owns what, and how to finish. The
maintainer merges the branches and updates `MECHANICS.md`, `ARCHITECTURE.md` and `CLAUDE.md`. **You do
not edit those three files.**

## Feel

Committed and readable, the way Combat now is. A cast has a visible wind-up (`WindupSeconds`), a release
frame the animation and the spell agree on, and a recovery. A stagger during the wind-up cancels the cast
(poise is symmetric; `Interruptible = false` opts out). Spells are blockable and never parryable
(`Blockable = false` for the few unblockable ones). Ground and barrier spells are read from a telegraph
ring exactly as long as their delay. Presentation never changes a rule: it reads events, and everything
that punctuates a blow for feel scales by `CombatComfort` (Reduced Motion caps hit-stop and flash at 25%).
Reuse what Combat added: `HitKind.Spell`, `DamagePacket`, `CombatComfort`, `HitOutcome`, `TelegraphComponent`,
`DamageNumberLayer`, and the events in `src/Combat/CombatEvents.cs`.

## The 25 player spells

Ids are fixed. School is the `DamageType`. `new` = needs a mechanism from the contract, `kept` = exists
today and is deepened, `replaces` = the old id resolves to it through `SpellAliases`.

| # | Id | Name | School | Delivery / mode | The one thing it does |
| --- | --- | --- | --- | --- | --- |
| 1 | `spell.emberlash` | Emberlash | Fire | Projectile, instant, replaces `spell.firebolt` | Fast, cheap bolt that leaves a Kindled stack; at 3 stacks the Kindle detonates |
| 2 | `spell.flame_lance` | Flame Lance | Fire | Projectile, charged, kept | Pierces; a fuller charge pierces more foes and burns longer |
| 3 | `spell.pyre_wall` | Pyre Wall | Fire | Barrier | Line of fire: burns what crosses it, eats arrows, does not stop bodies |
| 4 | `spell.sunfall` | Sunfall | Fire | Ground, charged, replaces `spell.fireball` | Meteor at the aim point after a telegraph; crushes guards on a direct hit |
| 5 | `spell.rime_shard` | Rime Shard | Frost | Projectile, instant | Chills; a shard into a chilled foe freezes it (Frost identity) |
| 6 | `spell.frost_nova` | Frost Nova | Frost | Area, kept | Ring around you: chills everything; anything already chilled freezes |
| 7 | `spell.blizzard` | Blizzard | Frost | Area with zone, kept | Lingering storm that slows and stacks chill; you can be caught in your own |
| 8 | `spell.glacial_bulwark` | Glacial Bulwark | Frost | Barrier | Solid ice wall that stops bodies and projectiles, breakable, Shatter-able |
| 9 | `spell.ball_lightning` | Ball Lightning | Lightning | Projectile, homing, kept | Slow homing orb that chains when it lands; it prefers a Stormbranded foe |
| 10 | `spell.storm_conduit` | Storm Conduit | Lightning | Projectile, channelled, kept | Held beam; slowed and interruptible while it runs, and it chains to a Stormbranded foe |
| 11 | `spell.thunder_step` | Thunder Step | Lightning | Dash | Dash along the aim striking what you pass; the last foe struck is stunned (`status.stunned`) |
| 12 | `spell.stormbrand` | Stormbrand | Lightning | Projectile, instant | Brands a foe; every lightning hit within range arcs to the brand |
| 13 | `spell.null_lance` | Null Lance | Arcane | Projectile, instant, replaces `spell.arcane_lance` | Dispels a benefit and silences: it interrupts a caster mid wind-up |
| 14 | `spell.arcane_shield` | Arcane Shield | Arcane | Self, kept | Ward that absorbs a fixed amount and breaks with a flash; an unspent ward returns a share of its mana |
| 15 | `spell.blink` | Blink | Arcane | Self, kept | Teleport along the aim, stopped short by a wall; a shorter jump costs less mana |
| 16 | `spell.gravity_well` | Gravity Well | Arcane | Ground | Pulls foes to the centre after a telegraph; stacks with Blizzard and Pyre Wall |
| 17 | `spell.mending_bloom` | Mending Bloom | Nature | Self, instant, replaces `spell.lesser_heal` | Heal now plus a Regrowth tail |
| 18 | `spell.lifebloom_totem` | Lifebloom Totem | Nature | Self with totem, kept | Totem heals over time and can be destroyed |
| 19 | `spell.thornsnare` | Thornsnare | Nature | Ground | Roots foes in the area (Root), with control immunity afterwards |
| 20 | `spell.stinging_swarm` | Stinging Swarm | Nature | Projectile, instant | Swarmed DoT that jumps to a nearby foe when its bearer dies |
| 21 | `spell.barkskin` | Barkskin | Nature | Self | Armour up for a few seconds, and casting is hyperarmoured while it lasts (a stagger cannot cancel a cast) |
| 22 | `spell.ember_siphon` | Ember Siphon | Necrotic | Projectile, kept (corruption tier 2) | Lifesteal bolt; heals more the lower the target's health |
| 23 | `spell.soul_tithe` | Soul Tithe | Necrotic | Projectile, `HealthCost` | Pay health for a big hit; refunds mana on a kill (Soul Echo) |
| 24 | `spell.knit_bone` | Knit Bone | Necrotic | Self, kept, now `PlayerLearnable` | Cleanses a Decay affliction on you and heals more for each stack it removed |
| 25 | `spell.grave_mark` | Grave Mark | Necrotic | Projectile, instant | Marks a foe: damage taken rises, and Necrotic lifesteal off a marked foe doubles |

Retired ids (their `.tres` are deleted, `SpellAliases` resolves them): `spell.firebolt`, `spell.fireball`,
`spell.arcane_lance`, `spell.lesser_heal`. Enemy-only spells stay as they are and stay
`PlayerLearnable = false`: `spell.ash_breath`, `spell.dragon_breath`, `spell.drake_breath`,
`spell.elder_word`, `spell.wither`.

The "one thing it does" column is the intent. Where a mechanism turns out awkward, keep the identity and
change the mechanism, then say so in your final report so the content descriptions match what shipped.
General rules the core agent owns: a cast interrupted in its wind-up refunds half its mana.

Numbers are yours to tune inside these bands: mana 8 to 34, cooldown 0.6 to 16 s, `WindupSeconds` 0.15 to
0.9 (more wind-up for more payoff), `MaxRank` 3. Cheap spells must not out-scale expensive ones.

## Reserved status ids (placeholders are committed; the status agent owns their content)

`status.kindled`, `status.rooted`, `status.silenced`, `status.stormbrand`, `status.barkskin`,
`status.grave_mark`, `status.swarmed`, `status.soul_echo`, `status.stunned`, plus the existing `status.burning`,
`status.chill`, `status.frozen`, `status.decay`, `status.regrowth`, `status.arcane_ward`. The content
agent references only these ids. The status agent may add more for its own use, but tells the content agent by id in its
final report and never renames these.

## Shared contract (already committed)

- `SpellDelivery`: `Ground = 4`, `Barrier = 5`, `Dash = 6` (append-only, pinned in `EnumStabilityTests`).
- `StatusControl` flags: `None`, `Root`, `Silence`, `Stun`, `Mark`.
- `SpellResource`: committed-cast, ground, barrier, dash, cost and consume fields. All default off.
- `StatusEffectResource`: `Controls`, `Dispellable`, `ControlImmunitySeconds`, `DetonateAtStacks`,
  `DetonateDamagePerStack`, `DetonateRadius`, `SpreadOnDeathRadius`, `DamageTakenModifier`. All default off.
- `StatusEffectsComponent`: `Controls`, `IsSilenced`, `IsRooted`, `IsStunned`, `StacksOf(id)`.
- `SpellAliases` and `SpellDatabase.Get`: retired ids resolve to their replacement.
- Events in `MagicEvents.cs`: `CastWindupStartedEvent`, `SpellInterruptedEvent`, `SpellBlockedEvent`,
  `SpellHitEvent`, `BarrierEndedEvent`, `StatusDispelledEvent`, `StatusDetonatedEvent`,
  `StatusResistedEvent`, `SpellComboEvent`, `SpellLearnedEvent`, `SchoolRankedUpEvent`. The contract
  declares them; the owner of the behaviour raises them (core: windup, interrupted, blocked, hit,
  barrier ended; status: dispelled, detonated, resisted, combo; learning: learned, ranked up).
- Adding to the contract: add a field or event to your own file, or to your own marker block. Do not
  change a contract signature; if one is wrong, say so in your final report and work around it.

## Ownership (one agent per file, no exceptions)

| Agent | Branch and worktree | Owns |
| --- | --- | --- |
| **core** | `magic/core` | `SpellcastingComponent`, `SpellResolver`, `SpellResource` (beyond the contract), `SpellActions`, `SpellProjectile`, `SpellCharge`, `SpellDelivery` (users), `SpellZone`, `SpellTotem`, `SpellCone`, `SpellSweep`, `SpellHoming`, `SpellFlash`, new `Spell*` placement files (ground, barrier, dash), `Aim` and cast-time feedback (`CombatFeedbackDirector` hooks for spells only, `TelegraphComponent` use), enemy `BreathComponent` if a rule change needs it |
| **status** | `magic/status` | `StatusEffectsComponent`, `StatusEffect`, `StatusEffectResource` (beyond the contract), `StatusMath`, `StatusEffectVfxComponent`, `StatusEffectDatabase`, `SchoolIdentity`, `SpellCombo`, `data/status_effects/*` |
| **learning** | `magic/learning` | `SchoolMasteryComponent`, `SchoolMasteryMath`, `SpellMastery`, `SpellTomeComponent`, `Weave` and `WeaveMath`, `SpellbookPanel`, the HUD spell and status widgets, `DevCommands` Magic commands, `DialogueSession` `LearnSpell` handling, save/load of spell knowledge (`SpellcastingComponent.Save/Load` edits go through the core agent: ask for them in your report) |
| **content** | `magic/content` | `data/spells/*`, `SpellDatabase`, `SpellAliases`, `GameIds.cs`, enemy and boss loadouts (`data/enemies`, `data/bosses`, `data/races` spell ids), `EnemyCasterTactics`, tome and trainer placement, `CombatTelegraphClassTests` spell ids |

Nobody edits a file another agent owns. If you need a change there, put it in your final report as a
one-line request for the maintainer. The contract commit already touched the hubs; that is over.

## Shared files: delimited blocks only

`strings.csv`, `PlayerFactory.cs`, `EnumStabilityTests.cs`, `MagicEvents.cs` and
`tools/world_quality_check.py` each carry four marker pairs named `magic-core`, `magic-status`,
`magic-learning` and `magic-content`. Write inside your own pair and nowhere else in those files. Locale
key prefixes: core `magic.cast.*`, status `magic.status.*`, learning `magic.learn.*` and `magic.book.*`,
content `spell.<id>.name` and `spell.<id>.desc`. Every player-facing string goes through `Loc.T`.

## Gates for every agent

1. First `git merge --ff-only magic/integration` in your worktree, so you start from the contract.
2. Run `godot --headless --import --path .` once (the Godot binary is the `_console.exe` in CLAUDE.md
   section 2) before `--validate`, then `git checkout -- .claude/skills` to revert the rewritten skill files.
3. `dotnet build Embervale.sln` with 0 warnings and 0 errors, `dotnet test tests/Embervale.Tests` green, and
   `godot --headless --path . -- --validate` green. `dotnet build` first, always: nothing that runs the
   game recompiles C#.
4. Pure rules get unit tests in `tests/Embervale.Tests` (a Godot `Resource` cannot be constructed there, so
   keep the rule in a static class over primitives, like `ChargeRules` and `StatusMath`).
5. Write **one** headless probe in the mould of `tools/combat_offence_probe.gd`, named
   `tools/magic_<group>_probe.gd`, and register it between your own markers in
   `tools/world_quality_check.py`. Read the whole probe output for script errors: a GDScript error
   mid-function aborts that function and the probe can still print PASS. A GDScript call to a C# method
   with optional parameters needs the full signature, and a struct or interface cannot cross the boundary,
   so build actors by hand and read results through Godot types.
6. Do not edit `MECHANICS.md`, `ARCHITECTURE.md` or `CLAUDE.md`.
7. Commit plainly on your branch. No AI trailer, no "Generated with" footer. Do not merge and do not push
   to main.
8. If you hit a usage limit, stop cleanly with everything committed; the maintainer resumes you.

## Final report (what the maintainer needs)

Files changed, the rules you added in one line each, the probe name and what it proves, anything you
could not verify without playing or without new art, and any request for a file you do not own.
