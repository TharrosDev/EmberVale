# Embervale — Content Recipes

Step-by-step recipes for adding content: what to author, in what order, and the trap each step has
already sprung. **Find your recipe in the index and read only that one.** Every ⚠️ is a defect that
shipped before it was written down. Repo-wide gotchas are `CLAUDE.md` §7; the systems reference is
[`ARCHITECTURE.md`](ARCHITECTURE.md); ids follow [`IDS.md`](IDS.md).

**Rules that apply to every recipe.** `data/<folder>/` is auto-indexed by its database — adding a
`.tres` registers it. Enums export as ints and are append-only. `.tres` files take no comments (a
`;` line fails the parser). Every player-facing string is a `strings.csv` key. `ContentValidator`
does **not** scan `.tscn`, so an id typed into a scene (a `ShopId`, `ServiceId`, `PropertyId`,
`ScheduleId`) is unvalidated and fails as *no prompt at all*. `EntityNode.GetComponent<T>` returns
the **first** child match, so **an entity gets one interactable**: two interactable components on
one body leave the second silent.

## Index

- Actors: [component](#a-new-component) · [enemy archetype](#a-new-enemy-archetype) ·
  [AI profile](#a-new-enemy-ai-profile) · [bestiary entry](#a-new-bestiary-entry) ·
  [Ashen variant](#an-ashen-corrupted-variant) · [body zones](#a-bigboss-creature-with-body-zones) ·
  [flight](#making-a-creature-fly) · [breath](#a-breath-weapon) · [lair boss](#placing-a-world-boss-in-a-lair) ·
  [talking creature](#a-creature-that-talks) · [boss fight](#a-new-boss-fight) ·
  [**Flamebearer boss, end to end**](#a-new-flamebearer-boss-end-to-end) ·
  [yielding duel fight](#a-yielding-duel-fight) · [weapon](#a-new-weapon) ·
  [companion](#a-new-companion)
- Items: [item](#a-new-item) · [equipment](#a-new-piece-of-equipment) · [affix](#a-new-loot-affix) ·
  [loot table](#a-new-loot-table--dropper) · [perk](#a-new-perk) · [XP / curve](#a-new-xp-bearing-enemy-or-tuning-the-curve) ·
  [crafting recipe](#a-new-crafting-recipe) · [spell](#a-new-spell) · [status effect](#a-new-status-effect)
- Story: [quest](#a-new-quest) · [chained story quest](#a-chained-story-quest-autostartflagid) ·
  [conversation](#a-new-conversation) · [faction](#a-new-faction) · [NPC routine](#a-new-npc-routine)
- World: [region](#a-new-region) · [region cell](#a-new-region-cell) · [sealed or hidden realm](#a-sealed-or-hidden-realm) ·
  [map location](#a-new-map-location) · [guild hub](#a-guild-hub-and-its-officers) ·
  [production settlement](#a-production-settlement) · [encounter](#a-new-encounter) ·
  [world event](#a-new-world-event) · [weather](#a-new-weather-state) ·
  [weather gated on a story flag](#weather-gated-on-a-story-flag)
- Economy: [shop](#a-new-shop--merchant) · [service](#a-new-service--trainer--bank--inn--stable) ·
  [contract board](#a-supply-contract--a-contract-board) · [toll](#a-tolled-crossing--toll-permit-bribe) ·
  [fence](#a-fence-and-contraband) · [gold sink](#a-new-gold-sink)
- Housing: [property](#a-new-claimable-property) · [stash](#giving-a-property-a-stash) ·
  [placeable / yard](#a-new-placeable-prop-or-a-buildable-yard) · [trophy stand](#giving-a-property-a-trophy-stand)
- Code: [stat](#a-new-stat) · [event](#a-new-event) · [persistent system](#a-new-persistent-system) ·
  [input action](#a-new-input-action) · [sound cue](#a-new-sound-cue) · [dev command](#a-new-dev-console-command) ·
  [pooling](#pooling-a-high-churn-node) · [UI panel](#a-new-ui-panel--hud-widget) · [generators](#generators)

---

## Actors

### A new component

1. `src/<Area>/XxxComponent.cs` extending `EntityComponent` (`[GlobalClass]` if editor-creatable).
2. Resolve siblings in `OnInitialize` via `Entity!.GetComponent<T>()`; subscribe there, unsubscribe
   in `OnTeardown`. Never override `_Ready`.
3. Add it as a child of the actor in its factory or scene.

### A new enemy archetype

1. Author `data/enemies/Xxx.tres` (`EnemyArchetypeResource`): `Id` (`enemy.*`), `NameKey`, build
   paths (`AttributesPath`, `WeaponPath`, `LootTablePath`, optional `ModelPath` — empty is a capsule
   in `PlaceholderTint`), `ModelScale` (default 1), `AiProfileId`, `FactionId`, `XpValue`.
   `CapsuleRadius`/`CapsuleHeight` size the body **and** the melee reach (scaled against a 1.8 m
   humanoid). `EnemyArchetypeFactory` builds it and `spawn <id>` works with no code.
2. **Do not write a factory** unless the actor is *structurally* different (the goblin's
   `EnemyFactory`, the Ashen Acolyte). A bespoke factory is a worse copy of the shared one — the Iron
   King's silently skipped the hit reaction, weapon trail and quest enemy group until it was deleted.
3. **A caster needs three things or it silently stands still:** non-empty `KnownSpellIds`, a standoff
   profile (e.g. `ai.caster`), and a real `Mana` pool in its `AttributeSet`. Tune `ManaRegen`. Mark
   enemy-only spells `PlayerLearnable = false`. Authored `KnownSpellIds` bypass `Learn`'s corruption gate.
4. Add a bestiary entry (validator requires it) and an encounter to make it appear in the wilds.

### A new enemy AI profile

Author `data/ai_profiles/Xxx.tres` (`AIProfileResource`, `ai.*`) with the knobs you need: perception
(`VisionRange`, `FovDegrees`, `AlertRadius`), melee (`AttackRange`, `FlankSpreadDegrees`), standoff
(`StandoffRange`, `KiteDistance`), guard (`BlockDuration`/`BlockRecovery`), nerve
(`RetreatHealthFraction`, `FleeOnSight`), `AmbushRange`, `TurnSpeedDegrees`, `TerritoryRadius`,
flight fields. Behaviours are branches in one brain gated on these numbers, so they combine; a zero
turns a behaviour off; an unknown id falls back to `ai.brute`. ⚠️ `EnemyAlertedEvent` is published
only when `AlertRadius > 0`.

### A new bestiary entry

`data/bestiary/Xxx.tres` (`BestiaryEntryResource`): `Id` = the **enemy template id**, `LoreKey`
(`enemy.<name>.lore`), `Category` (0 Humanoid · 1 Beast · 2 Undead · 3 Construct · 4 Elemental ·
5 Ashen · 6 Boss), `KillsToKnow` (`1` for a boss). Leave `NameKey` empty unless there is no archetype.
⚠️ `--validate` checks both directions: every entry names a template **and** every template has an entry.

### An Ashen (corrupted) variant

Do not author a new archetype. Set `CorruptionChance` on the encounter; `AshenAffliction.Afflict`
adds `"ashen"` modifiers, scales XP, prefixes the name and chars the body. Corruption belongs to the
**place**. Reach for a real archetype only for a different profile, loadout or faction. If you extend
the affliction: never change `TemplateId` (kill objectives match it), and always `Duplicate()` a
material before tinting or the tint writes through to every instance.

### A big/boss creature with body zones

1. `HitZones` on the archetype: `HitZoneResource`s (`Id`, `DamageMultiplier`, `Offset`, `Radius`,
   `Height`; height ≤ 2×radius is a sphere). Non-empty **replaces** the capsule hurtbox and is the
   greybox silhouette; the multiplier scales poise damage too.
2. `IsBoss = true` makes a `BossEntity` (healthbar, corruption-on-kill). `DirectionalMelee = true`
   swaps the hitbox between jaws/wing/tail by the target's bearing.
3. ⚠️ **Give its AI profile a `TurnSpeedDegrees`.** The default 0 snaps, so flank and rear attacks
   are dead code.

### Making a creature fly

Set `TakeoffRange > 0` on its **AI profile**, plus `HoverAltitude`, `ClimbSpeed`, `AirborneDuration`,
`GroundedDuration` (all 0 = no flight). The factory adds `FlightComponent`. **Keep the airborne window
short** — the cycle is Grounded → TakingOff → Airborne → Landing, never open-ended. The validator
rejects half-authored flight either way round.

### A breath weapon

1. A spell with `Delivery = 3` (Cone), `CastMode = 2` (Channeled); `ConeAngleDegrees` is the **full**
   angle, `ImpactRadius` the length; `PlayerLearnable = false`.
2. On the archetype set `BreathSpellId` **and** the same id in `KnownSpellIds` (validator enforces);
   `BreathDuration` is the hold.
3. A caster is a *profile* that stands off (`StandoffRange > AttackRange`), not an actor with spells.

### Placing a world boss in a lair

1. Set `TerritoryRadius` on its profile, or it chases forever (a flier follows you across realms).
2. A marker `Entity` in the cell with a **stable, unique `PersistentId`** and a `LairSpawnComponent`
   (`TemplateId`, `SpawnOffset`). **Persist the spawner, never the boss** — a boss spawned on cell load
   races `CellPersistenceDirector` and resurrects. Two lairs sharing an id both die at once.
3. Shape the fight space with a `WorldLandformResource` in the region spec, not a floor.
4. Inherit `scenes/regions/roost.tscn` (owns `Nav`, baker, `Nest`/`Lair` markers); props go under `Nav`.
5. Set `DefeatFlagId` if anything must know the boss is dead.

⚠️ **Spawning into a cell: create at zero, add, *then* set `GlobalPosition`.** Factories take a
**local** position and the cell root is already moved to its centre, so a world position applies the
offset twice.

### A creature that talks

Set `DialogueId` on the archetype (the factory attaches a `DialogueComponent`; the interact ray hits
its body). **Put it in a faction the player is not hostile to** (`faction.dragons` pattern), or it
attacks first; the first hit provokes it regardless. To teach a spell use `LearnSpell` (8) — it
ignores `PlayerLearnable`, so mark such spells `false`.

### A new boss fight

1. `data/bosses/Xxx.tres` (`BossResource`, `boss.*`): `Phases` — `BossPhaseResource`s **ordered high
   health to low**, the first at `HealthFraction = 1.0`, each with `AttackSpeedBonus`/`MoveSpeedBonus`
   (fractions under a `boss.phase{n}` source), optional `GrantSpellIds`, `AiProfileId` swap,
   `TelegraphColor`/`TelegraphEnergy`, `WindupPoiseMultiplier` (>1 makes a wind-up worth hitting; must
   stay positive — 0 looks like a broken interrupt and is rejected), `Attacks`, and `AddWaves`
   (`BossAddWaveResource`: `TemplateId`, `Count`, `RepeatSeconds` 0 = once, `MaxAlive`,
   `HealthMultiplier`). ⚠️ **A repeating wave must set `MaxAlive`.** Optional enrage: `EnrageSeconds`
   (0 = none), `EnrageSpellIds`, bonuses, `EnrageForcesFinalPhase`. Encounter/Reward: intro lock,
   defeat slow-mo, `RewardItemId`, `DefeatFlagId`, `DefeatDialogueId`.
   ⚠️ **A reward or defeat dialogue requires a `DefeatFlagId`**, or it pays out on every death. Leave
   it empty on a lair boss (`LairSpawnComponent` owns that fact).
2. On the archetype: `IsBoss = true` **and** `BossId`. An `IsBoss` with no `BossId` gets the default
   three-stage table; a `BossId` on a non-boss is rejected.
3. **The arena binds itself in its `.tscn`:** `Marker3D`s in group `boss_add_spawn` under the boss's
   parent (no markers = a ring around the boss); `ArenaHookComponent` (`ActivateAtPhase`, `Reveals`)
   reveals nodes by phase, scoped to bosses under its own arena, and resets on death. See
   `scenes/regions/ember_crown/arena.tscn`.
4. ⚠️ The enrage clock starts on the first damage traded, not on `BossEncounterStartedEvent`.
5. ⚠️ Mark every phase-granted spell `PlayerLearnable = false`.

### A new Flamebearer boss, end to end

The seven Flamebearers follow one shape; `enemy.storm_tyrant` / `boss.storm_tyrant` is the model.
Ids follow `docs/playbook/finish.md`'s registry.

1. **Body.** `data/attributes/XxxAttributes.tres`, a weapon `.tres`, and `data/enemies/Xxx.tres` with
   `IsBoss = true`, `BossId`, `AiProfileId = "ai.boss"` (or its own), `FactionId`, `XpValue`,
   `MaxPoise`, `Reaction`. Model: a generated Meshy body adopted with `python tools/assets.py adopt`
   (`docs/3D_ASSETS.md`), or, until one exists, an existing body with `ModelScale` — and set
   `CapsuleHeight` to match the scaled model.
2. **Fight.** `data/bosses/Xxx.tres` per the recipe above: three or more phases, add waves naming
   registered enemies, `RewardItemId = "item.relic.<x>_heart"`, `DefeatFlagId = "flag.<x>_defeated"`,
   `DefeatDialogueId = "dialogue.<x>_absorb"`.
3. **Relic.** `data/items/` entry for the relic.
4. **Ember offer.** `data/dialogue/XxxAbsorb.tres` — copy `StormTyrantAbsorb.tres`: the absorb choice
   carries `AddCorruption` (4) `"25"` and leads to a node whose farewell sets `flag.<x>_absorbed`
   (one effect per choice); a decline branch; the offer gated on `MissingFlag` of the absorb flag.
5. **Bestiary** (`Category = 6`, `KillsToKnow = 1`) and every locale key (name, lore, prompts, lines).
6. **Arena.** In the cell scene, a brazier `Entity` with a collider and `BossSummonComponent`:
   `BossTemplateId`, `DefeatedFlagId`, `RequiredQuestId` and/or `RequiredFlagId` (empty = ungated;
   both checked), `PromptKey` / `LockedPromptKey` (locale keys — the locked prompt must say *why*),
   `SpawnOffset`. Add `boss_add_spawn` markers and, if wanted, an `ArenaHookComponent`. Put the arena
   on the map (`gen_map_locations.py`) in the same change; if it needs a new cell, see the cell recipe.
7. **Story wiring (code lists).** Main-story objectives live in `tools/gen_main_story.py` (re-run it).
   If the boss joins the hidden-realm reveal, add its defeat flag to `HiddenRealmReveal.RequiredFlags`;
   if its ember counts for the epilogue, add its absorb flag to `EndingSequence.AbsorbFlags`; add its
   template to `HeadlessStory`'s Flamebearer list. For a vision, add its absorb dialogue id to
   `VisionSequence.Visions` and author `vision.<name>.1`..`3` in `strings.csv` (`--story` fails a
   missing card).
8. **Verify:** `--validate`, `--story`, then summon it in a real run and render the arena at eye level.

### A yielding duel fight

A boss who yields instead of dying (the Ashen Knight's rival duels; `boss.ashen_knight_duel1` is the
model). No new enemy template: the duel reuses the boss's archetype.

1. `data/bosses/XxxDuel.tres`: a normal `BossResource` (shorter phase list is fine) with
   `WithdrawHealthFraction` in `(0, 1)` (0.65 = leaves at 65% health), its own `DefeatFlagId`
   (e.g. `flag.rival.duelN_won`) and `DefeatDialogueId` for the parting words. ⚠️ **A withdrawing
   fight must set `DefeatFlagId`** (`--validate` fails it; nothing else would ever cool the brazier).
   Phase one's `AttackSpeedBonus`/`MoveSpeedBonus` apply from the start, so negative values open it
   slowed. Leave `RewardItemId` empty unless the duel pays.
2. The parting conversation in `data/dialogue/` plus its locale keys.
3. In the arena cell scene, a brazier `BossSummonComponent`: `BossTemplateId` = the boss's own
   archetype, **`FightId`** = the duel's `boss.*` id, `DefeatedFlagId` = the duel's flag,
   `RequiredFlagId` for when it opens, and **`ClosedFlagId`** for when it can never be fought again
   (the duels close on `flag.ashen_knight_defeated`). Map it in the same change.
4. ⚠️ A withdrawal publishes `BossWithdrewEvent`, not `EntityDiedEvent`: no kill credit, loot, XP or
   bestiary. Anything new that ends a fight on death must also listen for it.
5. To make a later scene remember it, read the duel flag with `HasFlag` (the Knight's last words do).
   Add the duel to `HeadlessStory.CheckRivalDuels` if it is story-relevant, then re-bake (the cell
   scene changed) and `--story`.

### A new weapon

`data/weapons/Xxx.tres` (`WeaponResource`): damage type, base/poise damage, stamina cost, and an
authored `Attacks` chain of `ActionDefinitionResource`s (windows as **fractions** of the action's
duration; `Duration = 0` lets the clip decide) — or the legacy wind-up/active/recovery floats, which
are synthesised into a chain. `IsRanged` plus the projectile fields make a bow. The actor's
`CharacterActionComponent.Weapon` or an `EquippableItemResource.Weapon` points at it.

### A new companion

`data/companions/Xxx.tres` (`CompanionResource`, `companion.*`): `NameKey`/`TitleKey` (validator
fails without them), build paths, `FactionId`, optional `KnownSpellIds` (caster), follower envelope
(`FollowDistance`/`EngageRadius`/`AttackRange`/`LeashRadius`), `StartingLoyalty`,
`LoyaltyQuestReward`, `LoyaltyQuestId`, `DialogueId`. Auto-registered; recruit by id (dialogue effect
5, `CompanionRoster.Recruit`, `companion recruit <id>`).

---

## Items and progression

### A new item

`data/items/Xxx.tres` (`ItemResource`, unique `Id`); reference it anywhere by id. `WorldModelPath`
lives on the base `ItemResource` (hand, ground and plinth all read it; empty = tinted primitive).
New interactable kinds subclass `InteractableComponent` (`Prompt`, `Interact`) and need a collider.

### A new piece of equipment

`EquippableItemResource`, `MaxStack = 1`: `Slot`, `Bonus*` fields, optional `Weapon`. Of the six
school resistances only `BonusFrostResist` exists on gear; each other one is one `[Export]` plus one
line in `StatBonuses()`. Bonuses apply through `EquipmentComponent`.

### A new loot affix

`data/affixes/Xxx.tres` (`AffixDefinition`): `Id`, `Label`, `Kind` (0 Prefix / 1 Suffix), `Stat`,
`MinValue`/`MaxValue`, `MinRarity`, `Weight`, `For{Weapons,Armor,Accessories}`.

### A new loot table / dropper

`data/loot/Xxx.tres` (`LootTable`) with `LootEntry`s (item, `DropChance`, `Min/MaxQuantity`,
`RollAffixes`), optional gold and `QualityBonus`. An archetype names it in `LootTablePath`.

### A new perk

`data/perks/Xxx.tres` (`PerkResource`): `Id` (`perk.<name>`), `MaxRank`, `Cost`, then the stat bonus (`Stat`,
`ModifierType`, `ValuePerRank`; leave `ValuePerRank` at 0 for a perk that is only non-stat effects).
Name and description are **locale keys** `<id>.name` / `<id>.desc` in `data/locale/strings.csv`
(`DisplayName`/`Description` are only the fallback); `--validate` fails a perk without both rows.

Tree fields, all defaulted: `Branch`, `Tier` (1-5), `Column` (0-4), `PrerequisiteIds` (each needs one rank),
`BranchPointsRequired` (points already spent in the branch), `IsCapstone`. Extra effects go in `Effects`, each a
`PerkEffectResource`: `Kind` None is one more stat modifier (`Stat`, `ModifierType`), any other `Kind` is a
non-stat effect with an optional `Arg` (e.g. a spell school) that call sites read with `PerkQuery.Of(entity, kind, arg)`.
⚠️ Values are summed over ranks and then **capped by `PerkEffectMath`**, so a perk cannot buy past the cap; read the
unit of each kind on `PerkEffectMath.RangeOf`. ⚠️ A prerequisite and a branch-points gate are enforced by `Learn`, **not
by `Load`**: a save is restored as it was, and `GrantFree` (race innate perks) skips both gates. Test it with the dev
commands `sp <n>`, `perk <id> [rank]`, `learn <id>` and `respec`. A `*StaminaMult` effect is read at its call site with
`PerkQuery.Factor(entity, kind, arg)` (1 for an entity with no perks); the dodge also passes `roll` or `backstep` as `Arg`.

### A new XP-bearing enemy (or tuning the curve)

`XpValue` on the archetype. Tune levelling in `data/progression/PlayerProgression.tres`.

### A new crafting recipe

1. `data/recipes/Xxx.tres` (`CraftingRecipeResource`): `Station` (0 Hand · 1 Forge · 2 Workbench ·
   3 Alchemy · 4 Cooking), `Ingredients` (`RecipeIngredient`s), output id/quantity, `OutputRarity`.
2. ⚠️ **Reachable by exactly one path**, validator-checked as a union: seeded in
   `GameIds.Recipes.Starting`, **or** taught by a `ServiceKind.Trainer` (`TaughtRecipeIds`). Never
   both — a trainer charging for what the player walked in with is rejected.

### A new spell

`data/spells/Xxx.tres` (`SpellResource`): `School` (a `DamageType`), `Delivery` (0 Projectile · 1 Area ·
2 Self · 3 Cone), `CastMode`, `ManaCost`, `Cooldown`, `BaseDamage`, `Healing`, optional
`StatusEffectId`, delivery knobs (a Projectile with `ImpactRadius > 0` detonates). ⚠️ **The spellbook
lists every spell**, so set `PlayerLearnable = false` on anything authored for a monster.

### A new status effect

`data/status_effects/Xxx.tres`: `School`, `Duration`, optional DoT, one stat modifier
(`ModStat`/`ModType`/`ModValue`), `IsBeneficial`, `MaxStacks`. Reference it from a spell.

---

## Story

### A new quest

1. `data/quests/Xxx.tres` (`QuestResource`): `Id`, `Title`/`Summary`, `Objectives`
   (`ObjectiveResource`: `Type`, `TargetId`, `RequiredCount`), rewards (`XpReward`, `GoldReward`,
   `RewardItems`, `FactionRewardId`/`FactionRewardAmount` — may be negative), optional
   `PrerequisiteQuestId`, `CompletionFlagId`, `TimeLimitSeconds`, `IsMainQuest`.
   **Objective types** (append-only): 0 Kill · 1 Collect · 2 Reach (`location.*`) · 3 Talk
   (`dialogue.*`) · 4 Escort (`companion.*`; **requires** `LocationId`) · 5 Defend (`location.*`,
   `RequiredCount` in seconds) · 6 Interact (an `InteractId` on a scene node) · 7 Stealth (no
   `TargetId`; seeded met, lost when any enemy enters Combat).
   ⚠️ **A deadline is not an objective type** — it is `TimeLimitSeconds` on the quest.
   ⚠️ **A Kill objective must name something that respawns** (an encounter or world event can spawn
   it), or a quest taken after the kill never completes. A one-shot boss needs
   `AllowsOneShotTarget = true` **and** an offering dialogue gated on the target being alive
   (`quest.ancient.kin` + `dialogue.ancient_dragon`). A companion's killing blow credits the player.
2. **`LocationId` only if the target actually lives somewhere.** Encounters spawn around the player and
   materials drop from loot tables, so most objectives have no destination; inventing one misleads.
   ⚠️ `--validate` fails a `LocationId` no `MapLocationResource` declares.
3. **Branching and ordering:** `ObjectiveResource.RequiredFlagId` / `ForbiddenFlagId` make an
   objective **inert** (cannot advance, does not block, not drawn); `SequentialObjectives` completes in
   order and steps over shut gates. ⚠️ **Choose the branch before the quest starts** — a choice has one
   effect, so the fork sets the flag and the next node's choice starts the quest (`Sedge.tres`).
   `QuestProgress` refuses to complete on zero live objectives. ⚠️ **Gate the fork choices on each
   other** (`MissingFlag`) or a player can hold both flags. ⚠️ **The ending is the flag, and it needs a
   consumer** (a dialogue condition, `ShopStockEntry.RequiredFlagId`, `RegionResource.UnlockFlagId`);
   there is deliberately no per-outcome reward table (invariant 2). Worked example:
   `quest.hollowreach.barrels` + `shop.hollowreach.hull`. The validator refuses a gate flag nothing
   writes, a gate that requires and forbids one flag, a gated Stealth objective, sequential with fewer
   than two objectives, and every objective sharing one `RequiredFlagId`.
4. **Story flags** have no database: `--validate` catches a flag nothing sets, not a `SetFlag` typo.
5. Start it with `DialogueEffect.StartQuest`, `QuestLogComponent.StartQuest`, or `AutoStartFlagId`.
   `QuestGiverComponent` no longer exists. Drive it with `quest start/advance/complete/reset`.
6. **Campaign fields (append-only):** type 8 **Milestone** (`TargetId` = a `flag.*` with a writer,
   `RequiredCount` 1; completes when the flag is set, on unlock and on load). Objective: `CompletionFlagId`,
   `ActivatedFlagId`, `IsOptional` (never blocks, never fails the quest; a quest needs one required objective),
   `HintKey`/`JournalEntryKey`. Quest: `StartFlagId`, `FailFlagId`, `IsLedger` (never auto-tracked), `ChapterKey`
   (an id, not a locale key), `OrderInAct`, `RegionId`, `GiverNameKey`, `RecommendedLevel`, `DetailKey`. ⚠️ **Completion
   needs at least one required live objective and no unmet required live one** (`ObjectiveProgress.AllLiveMet`), and a quest whose own
   `CompletionFlagId` is held never auto-starts. Placed actors: `FlagVisibilityComponent.VisibleWhenFlagId`.

### A chained story quest (`AutoStartFlagId`)

A quest with `AutoStartFlagId` starts itself (and is tracked) the moment that flag is set, and on load
for a save that already holds it — no quest giver. The previous act's `CompletionFlagId` or a boss's
`DefeatFlagId` is the trigger. ⚠️ `--validate` fails an auto-start flag nothing ever sets. The main
story is generated: edit the tables in `tools/gen_main_story.py` (acts, objectives, the Archivist's
Act III reading, the throne choice) and run it; locale text stays in `strings.csv`.

### A new conversation

1. `data/dialogue/Xxx.tres` (`DialogueResource`): `Id`, `SpeakerName`, `StartNodeId`, `Nodes`
   (`DialogueNode`: `Id`, `Speaker`, `Text`, `Choices`). A `DialogueChoice` has `Text`, `Goto` (empty =
   end), **one** `Condition`+`ConditionArg` and **one** `Effect`+`EffectArg`.
   Conditions: 0 Always · 1 QuestAvailable · 2 QuestActive · 3 QuestCompleted · 4 QuestNotStarted ·
   5 HasFlag · 6 MissingFlag · 7 CorruptionAtLeast · 8 CorruptionBelow · 9 CompanionRecruited ·
   10 CompanionNotRecruited · 11 CompanionLoyaltyAtLeast (`<id>:<value>`) · 12 ShopOpen · 13 ShopClosed ·
   14 GuildRankAtLeast · 15 GuildNotMember · 16 GuildCanJoin.
   Effects: 1 StartQuest · 2 SetFlag · 3 ClearFlag · 4 AddCorruption · 5 RecruitCompanion ·
   6 DismissCompanion · 7 AddCompanionLoyalty (`<id>:<delta>`) · 8 LearnSpell · 9 OpenShop ·
   10 OpenService · 11 JoinGuild · 12 GuildRank (`DialogueEnums.cs` is the authority).
2. ⚠️ **One condition and one effect per choice**: an "A and B" gate chains through an intermediate
   node (`Kael.tres`); a choice that starts a quest cannot also set a flag. When nesting, put the
   condition the node text presupposes outermost.
3. ⚠️ `OpenShop`/`OpenService` must leave `Goto` empty, or the conversation reopens behind the window.
4. Attach a `DialogueComponent { DialogueId }` to an `Entity` with a collider.

### A new faction

`data/factions/Xxx.tres`: `Id`, `DefaultReputation`, `HostileThreshold` (a `ReputationTier` int),
`KillReputationPenalty`, `Enemies`/`Allies`. Tag actors with `FactionComponent`. A guild is a faction
with ranks (`GuildRules`; NOW invariant 18).

### A new NPC routine

`data/schedules/Xxx.tres`: `Id` and `Entries` (`StartHour`, `Activity`, `Destination`). Destinations
are **cell-local** — `ScheduleComponent` adds the world position of the cell its NPC stands in
(`ScheduleResource.DestinationOf`), so moving a cell moves the routine. Hours before the first block wrap to the last. Add a
`ScheduleComponent { ScheduleId }` to a static NPC. `--validate` fails a destination outside the cell.

---

## World

Read [`WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a region. `data/regions/*.tres` is
**generated**: edit `tools/region_spec_<region>.py`, run `python tools/gen_regions.py`, then
`python tools/world_bake.py --bake`.

### A new region

1. Start from `tools/region_spec_template.py`, **never a copy of the Ember Crown's spec** (it drags
   one realm's cell sizes, seed and road widths along as though they were physics). Claim an atlas band
   in `tools/world_atlas.py` and add its crossings; `world_atlas.py --check` gates both.
2. The spec declares `Id`, display name, `Realm`, `SpawnPoint` (Y is clearance), bounds, weather bias,
   `Neighbours`, a `WorldEnvironmentProfileResource`, a `WorldPerformanceBudgetResource`, a
   `WorldGenerationProfileResource` (required), biome and the cell lattice. Run `tools/new_cell_scenes.py`
   for scenes, then `gen_regions.py`, `gen_map_locations.py`, `world_bake.py --bake`.
3. **Portals come from `Neighbours`**, spawned by `RegionSetup` — no per-scene authoring. Each neighbour
   may have its own door and landing: `NeighbourPortalPoints[i]` / `NeighbourArrivalPoints[i]` are
   parallel to `Neighbours` (zero falls back to `PortalPoint` / `SpawnPoint`), so walking back through a
   crossing lands at that crossing. `UnlockFlagId` on the **destination** hides its door until set.
   Stepping through (or `region goto <id>`) re-targets the streamer, pays any toll, lands via safe
   placement and autosaves.
4. Add the region to the per-region GDScript probes (traversal, census, scene audit, shots).

### A new region cell

> ⚠️ A new cell needs at least a settlement/landmark map location.

1. **Declare it in the spec's row bands**; the generator refuses a lattice that does not tile, and
   every seam route is one world point. `--check` fails a stale `.tres`.
2. The cell carries `Id` (`<region>.<cell>`), `ScenePath`, `Center`, a `WorldCellPresentationResource`
   (`TopologyResolution` counts against the terrain-vertex budget), optional scatter (layers with
   exclusion circles and an HLOD tier whose begin range overlaps the detailed end, with fade margins).
   ⚠️ A cell missing from the region's `Cells` array is silent.
3. `SafeRadius` makes a no-spawn bubble; overlap bubbles so no road strip is unprotected.
   `SafeZones.Set` replaces and runs before the per-cell adds.
4. ⚠️ `Surplus` / `Demand` / `ShockTags` only if a shop stands in the cell (plus a `cell.<id>` locale row
   if shockable).
5. **Scene:** a `NavigationRegion3D` named `Nav` with a `CellNavBaker`, then content. **No floor** — the
   terrain collider is parented into `Nav` by `WorldCellPresentation`. ⚠️ **Everything with a collider
   goes under `Nav`** or it does not carve the bake; interactable entities and actors go on the cell
   root. ⚠️ **A node's Y is clearance above the ground**; true world heights join `terrain_absolute`. A
   building wants a level pad (invariant 25).
6. Navmesh `agent_*` dims derive from the voxel grid (invariant 20): pick `cell_size`/`cell_height`
   (0.3 in settlements, 0.5/0.4 in large wilds), then `agent_height = ceil(1.75/ch)*ch`,
   `agent_radius = ceil(0.5/cs)*cs`, `agent_max_climb = floor(0.5/ch)*ch`; slope 40–42. Today: 0.3/0.3 →
   1.8/0.6/0.3, 0.4/0.3 → 1.8/0.8/0.3, 0.5/0.4 → 2.0/0.5/0.4. Parse **static colliders** only.
7. Props: `tools/gen_cell_props.py`, collider sizes from the model's **measured** bounds.
8. `--validate` rejects a scene that exists but does not parse, a negative `SafeRadius`, duplicate cell
   ids, and any route over a 0.80 grade. There is deliberately no "every cell has a `Nav`" rule (text
   scans cannot see scene inheritance).
9. ⚠️ **Render it from where the player arrives, at eye level.** A `.tscn` reads fine while looking wrong.

### A sealed or hidden realm

- `ScopedContentOnly = true` on the region keeps realm-agnostic encounters and world events out; only
  those naming it in `RegionIds` roll there.
- `WorldEnvironmentProfileResource.FixedSkyHour` (≥ 0) pins the sky and sun at that hour; the clock,
  schedules and weather keep running (the Pale Concord's dusk).
- Entry only by story: a portal from another region gated on this region's `UnlockFlagId`, set by code
  or content (`HiddenRealmReveal` sets `flag.pale_concord_revealed` from three defeat flags, on every
  flag change, load and region change).
- ⚠️ Keep the realm's name out of player-visible text until the reveal: locale values only under
  `pale.*` / `location.pale.*`, and every map location in its cells gated on the reveal flag.
  `world_atlas.py --check` enforces it.

### A new map location

**Do not hand-author these.** Add an `add(...)` row to `tools/gen_map_locations.py` (cell file, id
tail, category, **anchor node path**, name, and `shop=`/`service=`/`dialogue=`/`travel=`).

1. ⚠️ **Parent the marker to the stall, counter or keeper the location IS**, never to the cell root
   with an offset (invariant 12). `.` is correct only for the settlement itself. No coordinate anywhere.
2. Link, do not restate — the map asks the shop/service/dialogue databases. Reuse existing name keys.
3. `RevealWithCell = true` only for a capital or a place the roads announce. Otherwise discovery is by
   `MapDiscoveryRules` (20 m walk-up; Primary/Secondary sighted within 190/80 m with line of sight);
   `RevealFlagId` reveals by flag; `RequiredFlagId` conceals; a quest objective reveals on start.
4. Run the generator and `--check`; then `--validate` and `tools/map_probe.gd`.
5. Reach/Defend put the `location.*` id in `TargetId`; live targets use `LocationId` as the unloaded
   fallback; travel places link the `travel.*` id.
6. ⚠️ A new `MapCategory` changes `src/World/MapCategory.cs` **and** the generator's `CATEGORY` list
   (it stores the index). Add a category only when content exists for it.

### A guild hub and its officers

A hub is a `MapLocationResource`, an officer a placed `Entity`; the guild's `FactionResource` gains ids.

1. ⚠️ **Do not put the building on a pad — a levelled `GroundArea` is usually a road.** Read the cell's
   paths and clear `Width/2 + Shoulder` from every centreline. Never use a deliberately empty cell.
2. Author a new `Yard` in the region spec; `Elevation` is absolute world Y, so copy the adjacent pad's.
3. `python tools/compose_building.py` (`--hollow`, `--open`); ⚠️ a different shell per guild. ⚠️ Do not
   file-copy a raw `medieval_village` glTF (it imports at ~1/200).
4. Instance under `Nav`; the door faces the approach (a wall module's outer face is local `-Z`) —
   render to know.
5. Officers are top-level entities (never under `Nav`): `Entity` + `DisplayName` + `TemplateId` (`npc.*`,
   the roster key) + `Animation` + collider on `Shape_npc` + `Faction` + `Dialogue` + `Model` last.
   Keep them off route corridors.
6. `python tools/gen_guild_dialogue.py <key> <dialogue.id> <faction.id> "<Speaker>"`, then the nine
   `dlg.<key>.*` rows. ⚠️ Never `HasFlag` with a `guild.*` argument.
7. The leader's routine is cell-local, with no destination inside the (solid) building.
8. Map it now (nearest existing category; `reveal=False` for a hub to be found).
9. Declare `HubLocationId`, `LeaderNpcId`, `QuartermasterNpcId`, `ContactNpcId` on the faction; leave
   `RankPeerNpcId` empty until an arc needs it.
10. Verify in order: `--validate`, `check_cell_layout.py`, **`world_traversal_probe.gd`** (finds a
    building on a road), then `--guild-shots` and look at the frames.

### A production settlement

1. **Author what its merchants refuse before what they sell**: a source (local product at the realm's
   lowest `BuyMarkup`, barely buys) and a sink (best `SellFraction` for what the place cannot make). ⚠️
   A sink that buys everything flattens two settlements into one.
2. Check the trade tag has members first.
3. Everything else is the cell, shop and routine recipes.
4. ⚠️ Carrying goods cannot profit through spreads alone (`sell <= value <= buy` at every shop); only
   regional demand (`RegionDemand`) makes a carry pay. `--economy` shows it.

### A new encounter

`data/encounters/Xxx.tres`: `EnemyTemplateId`, `MinCount`/`MaxCount`, `SelectionWeight`,
`At{Dawn,Day,Dusk,Night}`, `CorruptionChance`, `RegionIds`. ⚠️ **Empty `RegionIds` means anywhere** —
author it whenever the creature belongs to one realm (frost stalkers once prowled the Ember Crown). A
misspelt id narrows it to nowhere; only `--validate` catches it.

### A new world event

`data/world_events/Xxx.tres`: `NameKey`, `Kind` (0 Raid · 1 Cache · 2 Hunt), weight, cooldown, time
limit, `RegionIds` (**empty = anywhere**), day-phase flags, spawn knobs (a Hunt champion is count 1 plus
`HealthMultiplier`) or `CacheItemId`/`CacheQuantity`, rewards. A new behaviour is a new
`WorldEventKind` plus a branch in the director.

### A new weather state

`data/weather/Xxx.tres`: `Type`, `SelectionWeight`, `MinHours`/`MaxHours`, `LightEnergyScale`,
`SkyEnergyScale`, `FogDensity`/`FogColor`, `Precipitation`, `WindStrength`, `SkyTint` (white = no
change; multiplies sky, horizon, ambient and fog colour).

### Weather gated on a story flag

`Dawnfire.tres` / `Embers.tres` are the models.

1. On the new state set `RequiredFlagId` (it can be rolled only while that flag is held). The moment
   the flag is set, `WeatherDirector` forces the first state requiring it, so the sky arrives with the
   story beat.
2. On every state the beat should end, add the flag to `ExcludedByFlagIds`. ⚠️ If the flag excludes
   every other state, the one left is permanent — that is how the Lord of Embers sky works; do it on
   purpose.
3. Nothing is saved: eligibility is re-derived from story flags on flag changes, after a load and on
   a 2 s poll. The flag needs a writer (`--validate` fails a read nothing sets).
4. Verify with `--story` if the flag is an ending, otherwise force the flag in a run and look at it.

---

## Economy

Mechanism: `ARCHITECTURE.md` §2.21; intent and the sink table: `DESIGN.md` §6. Every price is maths in
Godot-free `src/Economy/ShopPricing.cs` / `ShopStock.cs` / `ShopHours.cs`, never at a call site and never
a second price table; any new multiplier joins `NoCombinationOfMultipliersLetsSellingBeatBuying`. Run
`python tools/negative_tests.py` after touching an economy rule or authored price.

### A new shop / merchant

> ⚠️ **Last step: put it on the map** — a shop with no map location fails `--validate`.

1. `data/shops/Xxx.tres` (`ShopResource`, `shop.*`): `NameKey`, `Stock` (`ShopStockEntry`: `ItemId`,
   `Quantity`), `RestockDays`, optional `LeveledTable`, `FactionId`, `PurseGold`, `CellId`, and the
   spread `BuyMarkup` (≥ 1) / `SellFraction`. ⚠️ **`SellFraction` must stay below `BuyMarkup`** (equal
   or inverted is an infinite loop; rejected and clamped). Gold and quest items are rejected from stock.
   ⚠️ `CellId` empty means par pricing.
2. **Trade tags** (`src/Economy/TradeTags.cs`, bare lowercase words): `AcceptedTags` (what she buys),
   `Specialties` (premium and discount). ⚠️ Both empties mean yes. ⚠️ **Every settlement needs one
   merchant with empty `AcceptedTags`.** ⚠️ `Specialties` ⊆ a non-empty `AcceptedTags`; a spread too thin
   for the premium is rejected. Do not add an `ItemType` for trade.
3. **Stock kinds by number:** `Quantity = 0` unlimited; `> 0` finite, refills on the clock; a
   `LeveledTable` rolls at restock by player level. ⚠️ Finite rows and leveled tables need
   `RestockDays > 0`.
4. **Restock is evaluated when opened**, not ticked. Runtime stock, purses, absorption and rolled wares
   live in `ShopStockService` (saved), **never on the resource**. `time 26` rolls a day; `shop restock <id>`.
5. **Standing prices the buy side only** (15%–35% surcharge when disliked, 15% off at Allied); hostile
   refuses. ⚠️ Author `FactionId` on the shop, not the vendor. ⚠️ An unresolvable standing trades
   normally (the inverse of the AI's fail-safe). ⚠️ `BuyMarkup` needs headroom or the best tiers price
   alike (reported).
6. **Saturation** prices unit by unit as a template is absorbed since restock; none when
   `RestockDays = 0`; each unit floors at 1 gold.
7. **Purse** (`PurseGold`, 0 = unlimited) refills at restock; ⚠️ needs `RestockDays > 0`; an uncovered
   payout refuses the whole sale.
8. **Placing her.** A merchant who talks: a choice with `OpenShop` (9), `EffectArg = "shop.xxx"`, `Goto`
   empty. An unattended stall: an `Entity` with a collider and `VendorComponent { ShopId }`
   (unvalidated). ⚠️ **A travelling merchant (`VisitEveryDays > 0`) must use `VendorComponent`** — the
   dialogue route has no notion of presence.
9. **Buy charges before delivering and refunds on failure; sell removes by reference for rolled items,
   by template for stackables; a zero payout is refused; the shelf decrement is the last step.**
10. **Gated shelves and stakes:** `ShopStockEntry.RequiredTier` / `RequiredFlagId` / `RequiredInvestment`;
    `InvestmentTiers` (`Cost`, `PurseBonus`, cheapest first) raise the purse permanently and unlock rows.
    ⚠️ A stake moves no price. ⚠️ Locked rows show greyed with the gate named; order flag → standing →
    gold. ⚠️ An unlimited purse stays unlimited; `RefundPurse` clamps to the invested ceiling. Nine
    validator refusals cover rungs that buy nothing. `shop invest <id>` for testing.
11. **Hours and travel:** `OpenHour`/`CloseHour` (equal = always open), `VisitEveryDays`/`VisitDayOffset`.
    Presence is a pure function of the day — nothing saved. A talking merchant gates **every** trade
    choice on `ShopOpen` (12) and pairs a `ShopClosed` (13) choice (validator requires). A stall hides
    itself and ⚠️ zeroes its collider with its visibility. ⚠️ Match hours to the NPC's schedule by hand.
    ⚠️ **No consumable may be sold only by travellers.** Services keep no hours. `shop <id>` overrides both.

### A new service — trainer / bank / inn / stable

> ⚠️ **Last step: put it on the map.**

1. `data/services/Xxx.tres` (`ServiceResource`, `service.*`): `NameKey`, `Kind`, `PriceGold`, optional
   `FactionId`, kind fields; place an `Entity` with a collider and `ServiceComponent { ServiceId }`.
2. **`UnlockFlagId` is pay-once.** ⚠️ A Bank or Stable without one charges every use. ⚠️ An XP Trainer
   without one is a gold-to-levels pump. ⚠️ An Inn with one charges once ever.
3. ⚠️ **An inn rests through `ServiceRules.RestTarget`** — `SetTimeOfDay(8)` from 20:00 rewinds without
   advancing the day and freezes every daily clock; ask for 32.
4. A trainer sells access (recipes via `Learn`, XP via `AddXp`), never skill points.
5. A bank is an `InventoryComponent` on the service entity. ⚠️ Give it a permanent `PersistentId`; author
   `Capacity` on the node.
6. Standing prices services via `ShopPricing.ServicePrice` (rounds up, floors at 1); copy the inverted
   hostility default.
7. **There is no Repair kind and never will be** (Phase 40 struck).
8. **Commission counter:** `Kind = Commission`, `CommissionStation` (never Hand), `MaterialsShopId`,
   `PriceGold` as labour. ⚠️ Must be priced; ⚠️ charged **after** the craft (rolls back on a full pack);
   ⚠️ `--validate` runs `CommissionRules.Exploitable` and prints the floor fee — author above it. Check
   it is worth more than a free station.
9. A service can be opened from dialogue (`OpenService`, 10), except a Bank.

### A supply contract / a contract board

1. `data/contracts/Xxx.tres` (`ContractResource`): `NameKey`, `ItemId`, `Quantity`, `RewardGold`,
   optional `FactionId` + `ReputationDelta`. ⚠️ **Never a quest.**
2. ⚠️ The reward must beat the best buyer (validator prints the floor); no ceiling — once per rotation.
3. ⚠️ Never an `ItemType.Quest` item.
4. The board is a free, flagless `ServiceKind.Contracts` service (`BoardSlots`, `RotationDays`) on its
   own entity. ⚠️ The pool must exceed the slots.
5. ⚠️ The rotation is derived from the day and never stored; only fills are saved. Adding a contract
   reshuffles slots for every cycle (harmless).

### A tolled crossing — toll, permit, bribe

1. **Declare the toll on the destination region:** `TollGold` (0 = free), `TollPermitFlagId`,
   `TollPassFlagId`, and `TollFromRegionIds` (empty = charge arrivals from every neighbour; the Crossway
   charges only arrivals from Frostfang, since its wardens do not stand at the Ashen Breach or the
   Southmarch Gate). A two-way gate is two blocks, one per region.
2. **Papers are `ServiceKind.Passage` services.** A permit sets `UnlockFlagId` only. A bribe sets
   `GrantedFlagId` (consumed at the gate) and a negative `ReputationDelta`, and leaves `UnlockFlagId`
   empty. ⚠️ A bribe in `UnlockFlagId` becomes a permit; ⚠️ a permanent pass cheaper than the permit
   deletes the sink.
3. Author `FactionId` so the bribe's standing cost lands (validator rule). ⚠️ Permit and bribe are two
   entities.
4. **Charge at the crossing:** `RegionSetup.PayToll` in the transition handler, where the portal and
   `region goto` converge; never on fast travel. The prompt quotes `TollFee.Resolve`, the same function.
5. `--validate` checks each flag is granted by some `Passage` service.

### A fence and contraband

1. Tag goods `contraband` **plus** what they are. ⚠️ Contraband dominates: every shop that does not list
   `contraband` refuses it, even an empty-list general store.
2. Give it a source (loot rows on the factions who would carry it, or a fence's shelf).
3. Author the fence's refusals first.
4. ⚠️ **Leave the fence's `FactionId` empty** — `faction.outlaws` starts hostile and would hide her.
5. Both sides of the cost: `ContrabandFactionId`/`ContrabandDelta` (+) and
   `ContrabandPenaltyFactionId`/`ContrabandPenaltyDelta` (−); validator checks signs and pairing.
   ⚠️ Per sale, not per unit.
6. Two fences, at least one always open.
7. Confiscation is `ServiceKind.Search` (price 0); recovery is `ServiceKind.Redeem` (`PriceGold` is the
   per-unit fine). Two bodies. ⚠️ A realm that can seize must be able to give back (validator).

### A new gold sink

1. Add the row to `DESIGN.md` §6.
2. Price it in a Godot-free `src/Economy/` class and charge at the **single convergence point** (the
   travel fee in `WorldSessionDirector.OnFastTravelRequested`, where the map button and `travel goto`
   meet).
3. The UI shows the price from the **same function** the charge uses (`TravelCosts.FeeFor`).
4. Give it an earnable exemption (travel to a holding you own is free).

---

## Housing

### A new claimable property

1. `data/properties/Xxx.tres` (`PropertyResource`, `property.*`): `NameKey`, `RegionId`, `TravelNodeId`
   (required), and `PriceGold` and/or `RequiredQuestId` (one is required or it is free-on-touch).
2. Place the deed: an `Entity` with a collider and `PropertyDeedComponent { PropertyId }`
   (`CottageDeed` in `scenes/regions/ember_crown/ashfall_homestead.tscn`).
3. ⚠️ Refusals name themselves in order owned → quest-locked → too expensive (`PropertyClaim.Resolve`,
   read by prompt and interaction).

### Giving a property a stash

1. An `Entity` with a collider, an `InventoryComponent` and `PropertyStorageComponent { PropertyId }`;
   the inventory *is* the storage (`inventory:<PersistentId>`).
2. ⚠️ **A permanent, unique `PersistentId`**, or it empties on reload (or two chests overwrite).
3. ⚠️ `Capacity` on the node (load clamps to it).
4. `StorageOpenedEvent` opens the one `StoragePanel`.
5. ⚠️ Transfers remove by reference for rolled items, by template for stackables, and only what landed.
6. No validator rule (all in `.tscn`); a mistyped `PropertyId` shows no prompt.

### A new placeable prop or a buildable yard

1. **Yard:** `PlacementCenter` (⚠️ **world** space — add the cell's `Center`) and `PlacementRadius`
   (0 = no building) on the property.
2. **Prop:** an id plus a `Build` case in `src/Housing/PlaceableTemplates.cs` (the one id set and builder).
3. **Kit:** a `PlaceableItemResource` with `TemplateId`, plus a recipe — ⚠️ seeded in
   `GameIds.Recipes.Starting` or trainer-taught.
4. ⚠️ Never issue placement ids from a counter; `PlacementIds.Next` scans live ids
   (`place.<propertyId>#<n>`).
5. `--validate` builds every template and requires an `IEntity` with a collider.
6. Props are removed in placement mode; removal refuses on a full pack.

### Giving a property a trophy stand

An `Entity` with a collider, an `InventoryComponent` of **`Capacity = 1`**, and
`TrophyStandComponent { PropertyId }` (leave `PropertyId` empty on placed stands). ⚠️ Permanent
`PersistentId`. Accepts `TrophyDisplay.MinimumRarity` (Epic); taking is never gated.

---

## Code

### A new stat

1. Append to `StatType` (never reorder); update `StatTypes.IsResource` for depleting ones.
2. Field + mapping in `AttributeSet.ToBaseValues`.
3. `Loc` key in `StatNames.Key` + `strings.csv` (a test fails without it).
4. Pin the ordinal in `EnumStabilityTests.StatType_Ordinals`.
5. A stat missing from an `AttributeSet` reads 0. Resistances route through one curve
   (`CombatMath.ResistanceStat` + `ArmorMultiplier`) — never immunity.

### A new event

A `readonly record struct XxxEvent(...) : IGameEvent` in the relevant `*Events.cs`; `Publish` where it
happens, pair `Subscribe`/`Unsubscribe`.

### A new persistent system

Implement `ISaveable` (stable `SaveId`, `Save`/`Load` with a Godot `Dictionary`); components call
`RegisterSaveable()` in `OnInitialize` (registers only with a stable `PersistentId`), world services
register in `_EnterTree`. Load **replaces** (CLAUDE.md §7). Read `SAVE_FORMAT.md` first.

### A new input action

A constant + `Bind(...)` in `GameInput`; read via `Input.IsActionPressed/JustPressed/GetVector`.

### A new sound cue

Pick an id by prefix (`sfx.*`/`step.*` positional SFX; `music.*`, `amb.*`, `ui.*`, `voice.*` 2D —
`AudioCueRouting`), register it in `AudioLibrary.Build()` (a CC0 asset under `assets/audio/` or a
`ProceduralAudio` placeholder), and request it with `SoundCueRequestedEvent` / `MusicCueRequestedEvent`
or `AudioDirector.PlayCue`. An unregistered id plays silence and warns once.

### A new dev-console command

`console.Register(new ConsoleCommand(name, usage, summary, (console, args) => ...))` in
`DevCommands.RegisterAll`. ⚠️ It must go through the real choke point, not edit state directly. Add a
`ReproHarness` scenario for determinism.

### Pooling a high-churn node

A `NodePool<T>` built in `OnInitialize`, cleared in `OnTeardown`; the node builds its children once,
re-arms via `Launch/Configure`, and returns itself instead of `QueueFree` (`SpellProjectile`).

### A new UI panel / HUD widget

Build through `UiTheme` (`Panel`, `Padding`, `Header`/`Body`/`Action`/`Bar`) and the `UiPanel`
framework; a modal panel pauses the world by default (`UiState.Open(owner, pausesWorld:)`). Rebuild from
a dirty flag in `_Process`, never inside a button signal. New palette entries go in `UiTheme`. Render it
with `--panelshots` / `--hudshots`.

### Generators

Committed scripts that print or write authored artefacts — do not hand-write their output:
`tools/gen_regions.py` (regions), `tools/gen_map_locations.py` (map locations), `tools/gen_main_story.py`
(main quests and story dialogue), `tools/gen_guild_dialogue.py`, `tools/gen_merchant_dialogue.py` (scaffold
only; locale rows are hand-written), `tools/gen_cell_props.py` (prop stanzas from a table; `--no-collider`
for scenery), `tools/compose_building.py`, `tools/compose_district.py`, `tools/new_cell_scenes.py`.
