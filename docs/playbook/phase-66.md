## Phase 66 — Expansion / DLC Framework `[F/C]`

- [ ] **66A — Entitlement / DLC content loading** `[F]`
- [ ] **66B — New-realm-sized expansion seam** `[F/C]`
- [ ] **66C — Expansion shipping tooling (no base-game fork)** `[F]`

> **🚩 Gate G6 — Live.** A shipped game with a sustainable content cadence.
> (Roadmap §8.)

---

## Appendix — keeping this playbook honest

- **Re-derive sizing as you go.** If a sub-phase repeatedly overflows a session,
  the *next* time you hit its sibling, split it pre-emptively and update this file.
- **This file is the live tracker.** Tick boxes here per session; mirror only the
  *phase-level* status into `PRODUCTION_ROADMAP.md` §11 so the two don't drift.
- **The gates are real.** Don't open a stage's first sub-phase until the prior
  gate's criteria are verified in a build. The automated battery (build, tests,
  `--validate`, a live `--play`) is runnable here; the gate's *play-it-through*
  criteria are the maintainer's, and only they can close a gate.
- **Every sub-phase still owes the full DoD** (`PRODUCTION_ROADMAP.md` §0.3):
  builds, playable, `ISaveable` round-trips, `validate-all` green, docs updated,
  draft PR. The **Done when** line is *extra*, not instead.

---

## Appendix — deliberate-shortcut ledger (`ponytail:` markers)

Harvested by the Phase 35 audit. These are **known ceilings deliberately accepted**, not
bugs and not oversights — each one names the cheap thing that was built and the upgrade
path if it ever stops being enough. They are recorded here because a marker buried in a
source file is invisible to planning, which is how "later" quietly becomes "never".

Nothing here is scheduled. Revisit an entry only when its stated trigger actually fires.

| Where | Ceiling accepted | Upgrade trigger |
| ----- | ---------------- | --------------- |
| `Combat/CombatMath.cs` | No vulnerability side — a negative resist clamps to ×1 | An encounter needs damage *amplified*, not just resisted (DESIGN §1.5 permits it, alongside a resisted-school answer) |
| `Magic/SpellCombo.cs` | Combo table lives in code, not a `.tres` | A content author (not an engineer) needs to add combos |
| `Magic/SpellZone.cs` | Zones spawn at the caster with a fixed radius | Aim-placed or growing zones are wanted |
| `Magic/SpellTotem.cs` | Heals its owner only — no AI, collision or nav | A real summon system is needed (not before Phase 36's boss adds) |
| `Magic/SpellTomeComponent.cs` | One tome teaches one spell | A multi-spell archive — but that is just several tomes |
| `Magic/SchoolIdentity.cs` | Lightning single-jump; Arcane one buff per hit | A school needs more reach than one hop |
| `Magic/SchoolMasteryComponent.cs` | 1 mastery point per cast *event* — a channel ranks per tick | Channelled spells out-rank instants in practice |
| `World/SafeZones.cs` | One safe zone per region | A region needs a second safe area |
| `World/Weave.cs` | One ambient potency value per region | Ley-site restoration lands as content |
| `World/CellNavBaker.cs` | On-thread navmesh bake at cell load | A cell's geometry grows enough to stall a worker visibly |
| `Quests/ObjectiveLocator.cs` | Linear scan of the enemy group per call | Group size grows past what the caller's throttle hides |
| `UI/CompassStrip.cs` | Objective target re-resolved on a timer, cached | Targets move fast enough that the cache reads stale |
| `Player/FirstPersonArmsComponent.cs` | Same unmirrored mesh on both hands | Real first-person arm assets replace the greybox (Phase 53) |
| `Player/PlayerController.cs` | Camera spring masks `World`, which actor bodies share — a companion stepping behind the player pulls the camera in | It reads as twitchy in play; the fix is a dedicated camera-blocker layer |
| `Races/RaceComponent.cs` | Dev-tool race swap skips reputation | A player-facing respec/race-change is ever offered |
| `Enemies/AshenAcolyteFactory.cs` | Reuses the goblin loot table | A Fallen/cultist table is authored |
| `Localization/LocaleAudit.cs` | Duplicate-key scan treats one physical line as one row, while `LocCatalog` honours RFC-4180 | The first **multi-line** value in `strings.csv` (a newline inside quotes). *Restated by the Phase 24 audit: this entry used to read "no quoted-comma support / a string needs a comma inside quotes", which had already fired 105 times with no effect — the split only extracts the key, and keys never contain commas.* |
| `Magic/SpellcastingComponent.cs` | Blink is a straight horizontal ray | Vertical or curved blink is wanted |
| `Debugging/ContentValidator.cs` (×2) | Travel nodes validated at runtime, not authored; one regex for scene-authored flags | A second scene-authored writer of either kind appears |
| `Enemies/ArenaHookComponent.cs` | Reacts to *any* boss's phase change and death, not the one in this arena | A second arena, or any boss alive in the same region as one (added by the Phase 36 audit) |
| `Quests/QuestLogComponent.cs`, `Enemies/BossEncounterDirector.cs`, `World/WorldEventDirector.cs` | **A reward handed to a full pack is lost silently.** All three call `AddItem` for quest/boss/event payouts and discard the return, which is how many actually fit | Needs a design answer, not a guard: the reward is owed the moment the quest completes, so it can be neither refused (the quest is done) nor rolled back. An overflow stash, a mail drop, or "refuse the hand-in while full" are the real options. Trigger: the first report of a vanished reward. Flagged by the Phase 6–8 audit, which fixed the *transactional* cases (craft, unequip) where refusing **is** available |
| `Quests/QuestLogComponent.cs`, `Progression/ProgressionComponent.cs`, `Factions/ReputationComponent.cs` | **Last hit wins.** All three award on `ReferenceEquals(e.Killer, Entity)`, so a kill your companion lands grants the player no XP, no reputation shift and no quest credit | A maintainer decision, not a bug — the rule is applied consistently in all three, so it reads as deliberate. It predates Phase 32; the trigger is a play-through where Kael finishing a bounty target feels wrong. If it changes, all three move together (flagged by the Phase 9–11 audit) |

**House rule going forward:** when you write a `ponytail:` marker, name the ceiling *and*
the trigger — a shortcut with no stated upgrade condition is indistinguishable from a bug
six months later.
