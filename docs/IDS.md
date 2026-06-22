# Embervale — ID & Naming Registry

> **What this is.** Every piece of authored content (`.tres`) and every actor/flag the
> game refers to by string carries a stable **id**. Authoring against typos and ad-hoc
> naming is the #1 content-scale bug, so this document pins **one namespace scheme for
> every content domain** — the rule new content follows and the audit current content was
> checked against (Phase 22C).
>
> **Authority.** Ids are *contracts*. They live in authored `.tres`, are referenced from
> code via [`src/Core/GameIds.cs`](../src/Core/GameIds.cs) (the central constants for the
> ids code touches), and every cross-reference is resolved at boot by
> [`ContentValidator`](../src/Debugging/ContentValidator.cs) (`validate` console command) —
> so drift is *reported*, not silently shipped. An id, once authored and saved, is
> effectively permanent: it persists in saves. **Renaming an id is a migration, not an
> edit.**

---

## 1. The scheme

Every id is **lowercase**, dot-separated, with `snake_case` leaf segments:

```
<domain>[.<subcategory>].<name>
        └ required for item.* and affix.* only ┘
```

- **`<domain>`** — the content type (singular): `item`, `quest`, `spell`, … (§2).
- **`<subcategory>`** — required for `item.*` and `affix.*`, absent everywhere else.
- **`<name>`** — `snake_case`, descriptive, unique within its domain.

Regex the validator can enforce: `^[a-z]+(\.[a-z0-9_]+)+$`.

**Rules:**

1. **Lowercase + `snake_case` only.** No PascalCase, no camelCase, no hyphens, no spaces.
   The `.tres` *filename* may be PascalCase (`SteelSword.tres`); the **id inside** is
   `item.weapon.steel_sword`.
2. **Domain prefix is mandatory** — never a bare `steel_sword`.
3. **`item.*` and `affix.*` always carry a subcategory**; all other domains are flat
   `domain.name`.
4. **Unique within a domain.** `ContentValidator` flags duplicates.
5. **Stable forever.** Ids referenced from code also appear as constants in `GameIds.cs`;
   keep the two in lockstep (the validator catches drift).

---

## 2. Domain registry

Every domain in use today (✅) plus the reserved domains future phases will add (⏳).
Pattern column shows the canonical shape; examples are real ids from `data/**`.

| Domain | Pattern | Example | Source / notes |
| ------ | ------- | ------- | -------------- |
| `item.*` | `item.<cat>.<name>` | `item.weapon.steel_sword` | ✅ subcats below. Indexed by `ItemDatabase`. |
| `affix.*` | `affix.{prefix,suffix}.<name>` | `affix.suffix.swiftness` | ✅ `AffixDatabase`. Subcat = `AffixKind`. |
| `quest.*` | `quest.<name>` | `quest.cull_goblins` | ✅ `QuestDatabase` |
| `dialogue.*` | `dialogue.<name>` | `dialogue.elder` | ✅ graph id; **node ids are graph-local** (§4) |
| `spell.*` | `spell.<name>` | `spell.frost_nova` | ✅ `SpellDatabase` |
| `status.*` | `status.<name>` | `status.burning` | ✅ `StatusEffectDatabase` |
| `recipe.*` | `recipe.<name>` | `recipe.iron_ingot` | ✅ `RecipeDatabase` |
| `perk.*` | `perk.<name>` | `perk.endurance_training` | ✅ `PerkDatabase` |
| `faction.*` | `faction.<name>` | `faction.goblins` | ✅ `FactionDatabase` |
| `weather.*` | `weather.<name>` | `weather.storm` | ✅ `WeatherDatabase` |
| `encounter.*` | `encounter.<name>` | `encounter.goblin_warband` | ✅ `EncounterDatabase` |
| `event.*` | `event.<name>` | `event.goblin_raid` | ✅ world events (`WorldEventDatabase`). Canonical prefix is **`event`**, not `world_event`. |
| `schedule.*` | `schedule.<name>` | `schedule.elder` | ✅ `ScheduleDatabase` |
| `enemy.*` | `enemy.<name>` | `enemy.goblin` | ✅ hostile actor template (`EnemyTemplateRegistry`) — §3 |
| `npc.*` | `npc.<name>` | `npc.elder` | ✅ friendly/neutral actor template — §3 |
| `prop.*` | `prop.<name>` | `prop.cache` | ✅ persistent-actor template (`PersistentActorRegistry`) — §3 |
| `flag.*` | `flag.<name>` | `flag.elder_thanked` | ✅ story flag (`StoryFlagsComponent`); not a `.tres` — set/read in dialogue/code |
| `region.*` | `region.<name>` | — | ⏳ Phase 25 (`RegionResource`) |
| `boss.*` | `boss.<name>` | — | ⏳ Phase 28/36 (`BossResource`) |
| `relic.*` | `relic.<name>` | — | ⏳ Phase 23/51 (divine relics; likely an `item.*` subcat too — decide at Phase 51) |
| `race.*` | `race.<name>` | — | ⏳ Phase 26 (`RaceResource`) |
| `companion.*` | `companion.<name>` | — | ⏳ Phase 32 (`CompanionResource`) |

### `item.*` subcategories (in use)

`currency` · `potion` · `material` · `gem` · `armor` · `weapon` · `ring`

e.g. `item.currency.gold`, `item.potion.health`, `item.material.iron_ore`,
`item.gem.ruby`, `item.armor.leather_vest`, `item.weapon.steel_sword`,
`item.ring.iron`. **Convention:** add a new subcategory only for a genuinely new item
*family*; accessories currently use a slot-specific category (`ring`) — widen to
`accessory.*` only if a future slot makes `ring` too narrow (a Phase 51 call).

---

## 3. Actor templates split three ways

An in-world actor's template id picks its domain by *role*, not by being "an actor":

- **`enemy.*`** — hostile combat templates (built by `EnemyFactory`, keyed in
  `EnemyTemplateRegistry`; quests credit kills by this `TemplateId`).
- **`npc.*`** — friendly/neutral templates (the elder, vendors, quest givers).
- **`prop.*`** — persistent non-character objects (caches, chests) restored by
  `PersistentSpawnDirector`.

Keep the role split: a goblin is `enemy.goblin`, never `npc.goblin`.

---

## 4. Two-tier ids: dialogue nodes are graph-local

A `DialogueResource` has a **global** id (`dialogue.elder`) *and* internal **node** ids
that are bare words local to that graph: `root`, `offer`, `accepted`, `inprogress`,
`thanks`, `friend`. This is intentional — node ids are addressed only by `StartNodeId` /
`Goto` *within their own graph*, so they do **not** take a domain prefix. **Do not
"fix" them** to `dialogue.elder.root`. Node ids must be unique *within* their graph only.

---

## 5. Conformance audit (Phase 22C)

Audited every `Id` in `data/**.tres` plus the `flag.*` ids in code/dialogue against §1.

**Result: 100% conformant — zero violators.** All 60+ authored ids match
`^[a-z]+(\.[a-z0-9_]+)+$` with a registered domain; `item.*`/`affix.*` all carry a
subcategory; all others are flat. No drift between `GameIds.cs` constants and authored
ids (and `ContentValidator` would flag any). Conventions that were *implicit* and are now
*codified* here:

- **`event.*`** is the canonical world-event prefix (not `world_event.*`).
- **`item.*` / `affix.*` require a subcategory**; every other domain is flat.
- **Actor templates split `enemy.*` / `npc.*` / `prop.*`** by role (§3).
- **Dialogue node ids are graph-local and prefix-free** (§4).

No renames are required. Future content follows §1–§4; new domains land in §2 as their
phase builds them. If the validator ever reports a non-conformant id, this document — not
the new id — is the source of truth (fix the id, or amend this doc by decision).

---

> **House rule.** When you add a content domain (region, boss, relic, race, companion, …):
> add its row to §2, add any code-referenced constants to `GameIds.cs`, and — where it
> helps — extend `ContentValidator` to enforce its shape. The registry and the validator
> move together.
