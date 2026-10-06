using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Items;

/// <summary>Raised whenever the contents of an inventory change (add/remove/load).</summary>
public readonly record struct InventoryChangedEvent(IEntity Owner) : IGameEvent;

/// <summary>Raised when an entity picks an item up from the world. <paramref name="Instance"/>
/// is the rolled item when the publisher has it (its rarity and affixed name can differ from the
/// template's); null from a publisher that only knows the template.</summary>
public readonly record struct ItemPickedUpEvent(
    IEntity Owner, ItemResource Item, int Quantity, ItemInstance? Instance = null) : IGameEvent;

/// <summary>Raised when the player's hotbar assignments change (assign/clear/load).</summary>
public readonly record struct HotbarChangedEvent(IEntity Owner) : IGameEvent;
