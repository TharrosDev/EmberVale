# Architecture

Embervale is built around three deliberately small, composable ideas. Everything
else — combat, AI, quests, magic, loot — is layered on top without changing the
foundation.

## 1. Autoload services (the spine)

A handful of global singletons are registered as Godot autoloads in
`project.godot`. They are the only true globals in the codebase. Order matters
and is fixed in the autoload list:

| Autoload          | Responsibility                                              |
| ----------------- | ----------------------------------------------------------- |
| `EventBus`        | Strongly-typed publish/subscribe message hub.               |
| `ServiceLocator`  | Registry for world-scoped systems (player, spawners, ...).  |
| `GameManager`     | Owns the top-level `GameState` machine.                     |
| `SaveManager`     | Serializes all `ISaveable`s to `user://saves/`.             |

Each exposes a static `Instance`. They never reference gameplay-specific types,
so they remain stable as content grows.

### Why an EventBus instead of Godot signals?

Godot signals require a declared signal per message and couple emitters to a
specific node. The `EventBus` dispatches arbitrary `IGameEvent` records, so a new
event type (`QuestCompletedEvent`, `SpellCastEvent`) can be introduced anywhere
without editing a central file. Publishers never know who listens — combat can
raise `EntityDiedEvent` while quests, audio, and loot all react independently.

```csharp
EventBus.Instance.Subscribe<EntityDiedEvent>(OnEntityDied);
EventBus.Instance.Publish(new EntityDiedEvent(entity));
```

> **Rule:** always `Unsubscribe` in `_ExitTree`/`Dispose`. Handlers hold
> references and will otherwise keep freed objects alive.

## 2. Entities are compositions of components

`Entity : Node3D` is a thin spatial container with identity (`RuntimeId`,
`TemplateId`, `DisplayName`). It contains **no behaviour**. Capabilities come
from `EntityComponent` children:

```
Entity "Dire Wolf"
├── StatsComponent       (health, attributes, modifiers)
├── HealthBarComponent   (future)
├── EnemyAIComponent     (future)
└── LootDropComponent    (future)
```

This avoids deep inheritance chains and makes actors data-driven: an enemy is a
scene/template listing which components it has. Components resolve their owning
entity by walking up the tree, and query siblings via
`Entity.GetComponent<T>()`.

Lifecycle ordering is intentional:
- `Entity` assigns identity in `_EnterTree` (runs **top-down**, parent first).
- `EntityComponent` initializes in `_Ready` (runs **bottom-up**, children first),
  so the owning entity's identity is guaranteed to exist via `OnInitialize()`.

## 3. Stats are the universal gameplay currency

`StatsComponent` owns one `Stat` per `StatType`. A `Stat` is a base value plus a
list of `StatModifier`s, combined with the standard ARPG formula:

```
final = (base + Σ flat) × (1 + Σ percentAdd) × Π (1 + percentMult)
```

Modifiers carry a `Source` so an unequipped item or expired buff can remove all
of its bonuses in one call. Resource stats (Health/Stamina/Mana) additionally
track a depleting *current* value and raise `ResourceChangedEvent` /
`EntityDamagedEvent` / `EntityDiedEvent`.

Base values are authored as **`AttributeSet` resources** (`.tres`) — the
resource-driven content pipeline. A new enemy's balance is a new file, not new
code. See `data/attributes/DummyAttributes.tres`.

## 4. Persistence is non-optional

Any system that holds state implements `ISaveable` (`SaveId`, `Save`, `Load`)
exchanging a Godot `Dictionary` that serializes straight to JSON. `SaveManager`
gathers every registered saveable into a versioned envelope. New systems are not
considered done until they can round-trip through save/load.

## Data flow at a glance

```
Input ──▶ GameBootstrap ──▶ StatsComponent.ApplyDamage
                                   │
                                   ├─▶ EventBus.Publish(EntityDamagedEvent)
                                   └─▶ EventBus.Publish(EntityDiedEvent)
                                            │
              DebugHud / Bootstrap ◀────────┘  (subscribers react)
```

## Conventions

- **Namespaces** mirror folders: `Embervale.Core.Events`, `Embervale.Stats`, ...
- One primary type per file; file name matches the type.
- Components end in `Component`; events are past-tense records ending in `Event`.
- Prefer reacting to events over polling singletons.
- Nullable reference types are enabled — honor them.
