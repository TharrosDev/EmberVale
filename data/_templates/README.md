# Content templates

Canonical, copy-me starting points for every authored content type — one minimal,
valid `.tres` per domain. **How to use one:**

1. Copy `XxxTemplate.tres` into the real domain folder (e.g. `data/items/`,
   `data/quests/`). The folder, not this one, is what the databases scan.
2. Rename its `Id` to a real id per the scheme in [`docs/IDS.md`](../../docs/IDS.md)
   (`<domain>[.<subcategory>].<name>`, lowercase `snake_case`).
3. Fill in the fields (legends below), then run `validate-all` (or
   `godot --headless --path . -- --validate`) to confirm it is well-formed.

> **Why these live in `data/_templates/`.** Every content database scans only its own
> `data/<domain>/` directory, so nothing here is ever loaded into the game or seen by the
> `ContentValidator` — the `_` prefix keeps the templates out of the live content set.
> That is deliberate: a template is a *starting point*, not shipped content. (The
> templates reference real ids like `enemy.goblin` / `item.potion.health`, so a copy
> validates immediately once you rename its own `Id`.)
>
> **Comments don't survive in `.tres`.** Godot rewrites resource files and strips
> stray comments, so the field documentation lives here instead of inline. Each template
> mirrors the canonical recipe in **CLAUDE.md §8** — read that recipe alongside the
> template; this README is the field/enum legend for it.

---

## The templates (and their CLAUDE.md §8 recipe)

| Template | `script_class` | Goes in | §8 recipe |
| -------- | -------------- | ------- | --------- |
| `ItemTemplate.tres` | `ItemResource` | `data/items/` | "A new item" |
| `EquippableTemplate.tres` | `EquippableItemResource` | `data/items/` | "A new piece of equipment" |
| `WeaponTemplate.tres` | `WeaponResource` | `data/weapons/` | "A new weapon" |
| `AffixTemplate.tres` | `AffixDefinition` | `data/affixes/` | "A new loot affix" |
| `LootTableTemplate.tres` | `LootTable` | `data/loot/` | "A new loot table / dropper" |
| `PerkTemplate.tres` | `PerkResource` | `data/perks/` | "A new perk" |
| `QuestTemplate.tres` | `QuestResource` | `data/quests/` | "A new quest" |
| `DialogueTemplate.tres` | `DialogueResource` | `data/dialogue/` | "A new conversation" |
| `ScheduleTemplate.tres` | `ScheduleResource` | `data/schedules/` | "A new NPC routine" |
| `WeatherTemplate.tres` | `WeatherResource` | `data/weather/` | "A new weather state" |
| `EncounterTemplate.tres` | `EncounterResource` | `data/encounters/` | "A new encounter" |
| `WorldEventTemplate.tres` | `WorldEventResource` | `data/world_events/` | "A new world event" |
| `RecipeTemplate.tres` | `CraftingRecipeResource` | `data/recipes/` | "A new crafting recipe" |
| `SpellTemplate.tres` | `SpellResource` | `data/spells/` | "A new spell" |
| `StatusEffectTemplate.tres` | `StatusEffectResource` | `data/status_effects/` | "A new status effect" |
| `FactionTemplate.tres` | `FactionResource` | `data/factions/` | "A new faction" |

---

## Enum legends

Enums export as their integer ordinal in `.tres`. **Append-only** — never reorder
(see `docs/ARCHITECTURE.md` §4 / `EnumStabilityTests`). Source of truth in parentheses.

- **Item `Type`** (`src/Items/ItemType.cs`) — incl. `2` Weapon, `3` Armor, `4` Material.
- **`Rarity` / `OutputRarity`** (`src/Loot/LootRarity.cs`) — `0` Common, rising to rarer
  tiers; `0` keeps a crafted output plain (no rolled affixes).
- **Equipment `Slot`** (`src/Items/EquipmentSlot.cs`) — e.g. `1` main-hand weapon,
  `4` chest (see the enum for the full slot list).
- **Affix `Kind`** — `0` Prefix, `1` Suffix.
- **`Stat` / `ModStat`** (`src/Stats/StatType.cs`) — the stat the modifier targets
  (e.g. `9` Physical Power, `11` Move Speed, `13` Crit Chance).
- **`ModifierType` / `ModType`** (`src/Stats/StatModifier.cs`) — `0` Flat, `1` PercentAdd,
  `2` PercentMult (`MoveSpeed` PercentMult `-0.5` = a 50% slow).
- **Objective `Type`** (`src/Quests/QuestTypes.cs`) — `0` Kill (`TargetId` = enemy
  template id), `1` Collect (`TargetId` = item id).
- **Dialogue `Condition` / `Effect`** (`src/Dialogue/DialogueEnums.cs`) — Condition
  `0` Always … `5` HasFlag; Effect `0` None / `1` StartQuest / `2` SetFlag / `3` ClearFlag.
  A choice with an empty `Goto` ends the conversation.
- **Spell `School`, status `School`, weapon `DamageType`** (`src/Combat/DamageType.cs`) —
  `0` Physical, `1` Fire, `2` Frost, `3` Lightning, `4` Arcane, `5` Nature, `6` Necrotic,
  `7` True.
- **Spell `Delivery`** — `0` Projectile, `1` Area, `2` Self.
- **Recipe `Station`** — `0` Hand, `1` Forge, `2` Workbench, `3` Alchemy, `4` Cooking.
- **World event `Kind`** — `0` Raid, `1` Cache, `2` Hunt.
- **Weather `Type`** (`src/World/WeatherType.cs`) — `0` Clear and up.
- **`HostileThreshold`** (`src/Factions/ReputationTier.cs`) — a `ReputationTier` int
  (`2` = Unfriendly).
