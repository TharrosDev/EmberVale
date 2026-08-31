using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.World;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Hands an item to an actor, and puts on the ground whatever would not fit.
///
/// ⚠️ <b>IT EXISTS BECAUSE A FULL PACK USED TO DESTROY REWARDS.</b>
/// <see cref="InventoryComponent.AddInstance"/> returns how much it actually stored — less than
/// asked when the pack ran out of room — and every reward path in the game discarded that number:
/// a quest's gold and items, a world event's cache and payout. The player finished the errand,
/// watched the completion toast, and received nothing, with no message anywhere saying why. This is
/// the one place that answer is handled, so a new reward path gets it by calling the same method
/// the old ones do rather than by remembering a rule.
///
/// The overflow becomes a world pickup at the actor's feet — the same
/// <see cref="ItemPickupFactory"/> object a chest or a corpse drops — so nothing is lost and the
/// player can free a slot and take it. Dropping is the honest failure: refusing the quest's
/// completion would be worse, and holding the item in limbo needs a mailbox this game has not got.
/// </summary>
public static class ItemGrant
{
    /// <summary>
    /// Adds <paramref name="quantity"/> of <paramref name="item"/> to <paramref name="inventory"/>,
    /// spilling the remainder at <paramref name="recipient"/>'s feet.
    /// </summary>
    /// <returns>How many were stored in the pack (the rest are on the ground).</returns>
    public static int Give(InventoryComponent? inventory, ItemResource? item, int quantity, IEntity? recipient)
    {
        if (item == null || quantity <= 0)
        {
            return 0;
        }

        return Give(inventory, ItemInstance.Plain(item), quantity, recipient);
    }

    /// <inheritdoc cref="Give(InventoryComponent?, ItemResource?, int, IEntity?)"/>
    public static int Give(InventoryComponent? inventory, ItemInstance? instance, int quantity, IEntity? recipient)
    {
        if (instance == null || quantity <= 0)
        {
            return 0;
        }

        int stored = inventory?.AddInstance(instance, quantity) ?? 0;
        int overflow = quantity - stored;
        if (overflow <= 0)
        {
            return stored;
        }

        if (recipient?.Body is not Node3D body || body.GetParent() is not { } parent)
        {
            // Nowhere to put it. Say so loudly — this is the case the whole class exists to stop
            // being silent, and a caller with no world presence is an authoring error, not a state.
            Log.Error($"{overflow}x '{instance.TemplateId}' could not be granted and could not be " +
                      "dropped (the recipient is not in the world); it has been lost.");
            return stored;
        }

        Vector3 spot = Loot.LootComponent.ScatterAround(body.GlobalPosition, 0);
        parent.CallDeferred(Node.MethodName.AddChild, ItemPickupFactory.Create(instance, overflow, spot));
        Log.Info($"{recipient.DisplayName}'s pack is full: {overflow}x {instance.DisplayName} " +
                 "dropped at their feet.");
        return stored;
    }
}
