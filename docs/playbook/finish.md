# Finish plan (2026-09-27)

Maintainer direction: **finish the game.** The roadmap is an idea guide, not a checklist. This page
is the contract for the finishing run; it supersedes the playbook's per-phase ordering where they
disagree. The build is personal and never published, so storefront/platform/live phases (61-66) and
extra locales (60) are out of scope.

## Definition of done

1. **New Game -> credits, both endings**, reachable from legitimate play with no dev commands.
2. All five realms plus the Celestial Realm exist, are traversable, and host their Flamebearer.
3. Every story beat is data (quests, dialogue, flags, narration cards) on existing systems.
4. `build`, `test`, `validate`, `report lifecycle`, `world_bake --check`, `world_atlas --check` pass.
5. A Windows `ExportRelease` build is produced.

New realms are **compact** (roughly 12-24 cells): enough country to travel and a clear route to the
boss, never Ember Crown's 52 cells. Empty country still matters; POI density does not.

## Story spine (quest ids `quest.main.*`)

| Act | Beat | Where | Gate out |
| --- | --- | --- | --- |
| I Awakening | the existing vertical slice: Ashfall -> Kael -> the warband -> the Iron King | Ember Crown | `flag.iron_king_defeated` (opens Frostfang, Ashen Wilds, Sunspire) |
| II Gathering | four realm Flamebearers, any order; each defeat offers its ember (absorb = +25 corruption, `flag.<boss>_absorbed`) and drops its relic | Frostfang, Ashen Wilds, Sunspire | three known-realm embers -> `flag.pale_concord_revealed` |
| II (hidden) | the Hollow Queen | Pale Concord | `flag.hollow_queen_defeated` |
| III Truth | the Veiled Archive reading at the Sunspire library: the Cataclysm, Morthul, the Ash Throne | Sunspire library | `flag.celestial_gate_open` |
| IV Celestial War | the Ashen Knight, then Morthul, then the throne choice | Celestial Realm | ending flag -> ending cards -> credits |

Endings: the throne dialogue reads `CorruptionTiers.EligibilityOf`. **Dawnfire** (reject power,
`flag.ending_dawnfire`) and **Lord of Embers** (claim the throne, `flag.ending_embers`). Undecided
players may choose either. After credits the save continues in free roam.

## ID registry (do not rename)

| Flamebearer | enemy template | boss id | defeat flag | relic | absorb dialogue | owner |
| --- | --- | --- | --- | --- | --- | --- |
| Iron King | `enemy.iron_king` | `boss.iron_king` | `flag.iron_king_defeated` | `item.relic.iron_heart` | `dialogue.iron_king_absorb` | done |
| Storm Tyrant | `enemy.storm_tyrant` | `boss.storm_tyrant` | `flag.storm_tyrant_defeated` | `item.relic.storm_heart` | `dialogue.storm_tyrant_absorb` | main |
| Beast Lord | `enemy.beast_lord` | `boss.beast_lord` | `flag.beast_lord_defeated` | `item.relic.wild_heart` | `dialogue.beast_lord_absorb` | Ashen Wilds |
| Crimson Prophet | `enemy.crimson_prophet` | `boss.crimson_prophet` | `flag.crimson_prophet_defeated` | `item.relic.crimson_heart` | `dialogue.crimson_prophet_absorb` | Sunspire |
| Hollow Queen | `enemy.hollow_queen` | `boss.hollow_queen` | `flag.hollow_queen_defeated` | `item.relic.hollow_heart` | `dialogue.hollow_queen_absorb` | Pale Concord |
| Ashen Knight | `enemy.ashen_knight` | `boss.ashen_knight` | `flag.ashen_knight_defeated` | `item.relic.ashen_heart` | `dialogue.ashen_knight_absorb` | Celestial |
| Morthul | `enemy.morthul` | `boss.morthul` | `flag.morthul_defeated` | — | `dialogue.ash_throne` (the ending choice, main owns) | Celestial |

Absorb flags follow `flag.<name>_absorbed` (`flag.iron_king_absorbed` exists).

| Realm | region id | band (x; z) | entry | portal gate flag |
| --- | --- | --- | --- | --- |
| Ashen Wilds | `region.ashen_wilds` | 900..2100; -1100..300 | Ember Crown Ashen Breach (500, -282) | `flag.iron_king_defeated` |
| Sunspire Dominion | `region.sunspire` | -800..800; 900..2300 | Ember Crown Southmarch Gate (72, 402) | `flag.iron_king_defeated` |
| Pale Concord | `region.pale_concord` | -2600..-1500; -400..900 | Sunspire library (story portal) | `flag.pale_concord_revealed` |
| Celestial Realm | `region.celestial` | 2400..3200; -2400..-1600 | Sunspire library (story portal) | `flag.celestial_gate_open` |

## Boss placement

Every arena uses `BossSummonComponent` (a brazier): set `BossTemplateId`, `DefeatedFlagId`,
`RequiredQuestId` (empty = ungated), `PromptKey` and `LockedPromptKey`. The Iron King's
`scenes/regions/ember_crown/arena.tscn` plus `ArenaHookComponent` is the reference arena.
`data/bosses/IronKing.tres` is the reference fight: three phases, add waves, a phase-three move set.

## Integration rules for parallel realm work

- Each realm is built on its own branch in its own worktree. Main merges them one at a time and
  **re-bakes after each merge** (`world_bake.py --bake`), so bake binaries never need hand merging.
- `data/locale/strings.csv`: append your keys in one block under a `# <realm>` comment line if the
  file allows comments, otherwise at the end. Never reorder existing rows.
- The Pale Concord's name must stay out of every surface the atlas gate scans until its reveal.
