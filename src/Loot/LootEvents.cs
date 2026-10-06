using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;

namespace Embervale.Loot;

/// <summary>Raised when a pickup could not go into <paramref name="Owner"/>'s pack because it is
/// full. <paramref name="Quantity"/> is what was left on the ground. Published at most once every
/// couple of seconds per pickup, so the hold-to-collect sweep does not flood a listener.</summary>
public readonly record struct InventoryFullEvent(IEntity Owner, ItemResource Item, int Quantity) : IGameEvent;

/// <summary>Raised when a slain boss leaves a reward chest. <paramref name="Chest"/> is the chest
/// entity, already in the world at the death position.</summary>
public readonly record struct RewardChestSpawnedEvent(IEntity Chest, string TablePath) : IGameEvent;
