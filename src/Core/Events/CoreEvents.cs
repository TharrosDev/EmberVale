using Embervale.Entities;
using Embervale.Stats;

namespace Embervale.Core.Events;

// ---------------------------------------------------------------------------
// Core engine-level events. Gameplay systems (combat, quests, UI, audio) add
// their own event types in their own files; these are the foundational ones
// the architecture itself raises.
// ---------------------------------------------------------------------------

/// <summary>Raised by <see cref="GameManager"/> whenever the top-level state changes.</summary>
public readonly record struct GameStateChangedEvent(GameState Previous, GameState Current) : IGameEvent;

/// <summary>Raised when an <see cref="IEntity"/> enters the world.</summary>
public readonly record struct EntitySpawnedEvent(IEntity Entity) : IGameEvent;

/// <summary>Raised when an <see cref="IEntity"/> is about to leave the world.</summary>
public readonly record struct EntityDespawnedEvent(IEntity Entity) : IGameEvent;

/// <summary>Raised when an entity's health reaches zero.</summary>
public readonly record struct EntityDiedEvent(IEntity Entity) : IGameEvent;

/// <summary>Raised when an entity takes damage. <paramref name="RemainingHealth"/> is post-damage.</summary>
public readonly record struct EntityDamagedEvent(IEntity Entity, float Amount, float RemainingHealth) : IGameEvent;

/// <summary>Raised when an entity is healed. <paramref name="CurrentHealth"/> is post-heal.</summary>
public readonly record struct EntityHealedEvent(IEntity Entity, float Amount, float CurrentHealth) : IGameEvent;

/// <summary>Raised when a current/max resource (health, stamina, mana) changes value.</summary>
public readonly record struct ResourceChangedEvent(IEntity Entity, StatType Stat, float Current, float Max) : IGameEvent;

/// <summary>Raised after a save slot is written to disk.</summary>
public readonly record struct GameSavedEvent(string Slot) : IGameEvent;

/// <summary>Raised after a save slot is successfully loaded.</summary>
public readonly record struct GameLoadedEvent(string Slot) : IGameEvent;
