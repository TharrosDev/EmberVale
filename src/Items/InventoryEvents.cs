using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Items;

/// <summary>Raised whenever the contents of an inventory change (add/remove/load).</summary>
public readonly record struct InventoryChangedEvent(IEntity Owner) : IGameEvent;

/// <summary>Raised when an entity picks an item up from the world.</summary>
public readonly record struct ItemPickedUpEvent(IEntity Owner, ItemResource Item, int Quantity) : IGameEvent;

/// <summary>Raised when the player's hotbar assignments change (assign/clear/load).</summary>
public readonly record struct HotbarChangedEvent(IEntity Owner) : IGameEvent;
