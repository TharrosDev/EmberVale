using System;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The moves the item screens share: taking units out of one particular stack, carrying part of a
/// stack between two inventories, and putting a stack on the ground. They live in one place because
/// each has a trap the three screens would otherwise each have to remember.
/// </summary>
public static class ItemTransfer
{
    /// <summary>
    /// Removes <paramref name="quantity"/> units from <paramref name="stack"/> itself.
    ///
    /// <see cref="InventoryComponent.RemoveItem(string, int)"/> takes by template id from whichever
    /// stack it reaches first, which is the right answer only while the owner holds a single stack
    /// of the thing. With two, selling the unlocked stack of potions would drain the locked one, and
    /// with two differently rolled swords it would take the wrong sword. So the id path is used only
    /// when this stack is all there is, and every other case goes unit by unit, by reference.
    /// </summary>
    public static bool Take(InventoryComponent from, ItemStack stack, int quantity)
    {
        if (quantity <= 0 || quantity > stack.Quantity)
        {
            return false;
        }

        ItemInstance instance = stack.Instance;
        if (from.TotalCount(instance.TemplateId) == stack.Quantity)
        {
            return from.RemoveItem(instance.TemplateId, quantity);
        }

        for (int i = 0; i < quantity; i++)
        {
            if (from.RemoveOneInstance(instance) == null)
            {
                return i > 0;
            }
        }

        return true;
    }

    /// <summary>
    /// Moves up to <paramref name="quantity"/> of a stack into another inventory and returns how
    /// many went. Only what the destination accepted leaves the source, so a full chest never
    /// deletes the remainder. A stackable travels as a copy: the two inventories must not end up
    /// sharing one <see cref="ItemInstance"/>, or locking the half left behind would lock the half
    /// that moved.
    /// </summary>
    public static int Move(InventoryComponent from, InventoryComponent to, ItemStack stack, int quantity)
    {
        quantity = Math.Min(quantity, stack.Quantity);
        if (quantity <= 0)
        {
            return 0;
        }

        ItemInstance payload = stack.Instance.IsStackable ? Unmarked(stack.Instance) : stack.Instance;
        int moved = to.AddInstance(payload, quantity);
        if (moved > 0)
        {
            Take(from, stack, moved);
        }

        return moved;
    }

    /// <summary>A copy with the player's marks cleared: a mark belongs to the stack it was set on,
    /// not to units that have left it.</summary>
    public static ItemInstance Unmarked(ItemInstance instance)
    {
        ItemInstance copy = instance.Copy();
        copy.Locked = false;
        copy.Junk = false;
        return copy;
    }

    /// <summary>
    /// Sets a whole stack down at the player's feet as an ordinary pickup, the same object a chest
    /// or a corpse leaves, so a drop made by mistake is one key press from undone. It is marked as
    /// the player's own drop, so the walk-over sweep leaves coin and materials where they were put.
    /// Refuses a locked
    /// stack and a quest item, and does nothing when there is no player in the world to drop beside.
    /// </summary>
    public static bool Drop(InventoryComponent pack, ItemStack stack)
    {
        ItemInstance instance = stack.Instance;
        if (!CanDrop(instance) || Player()?.Body is not Node3D body || body.GetParent() is not { } parent)
        {
            return false;
        }

        int quantity = stack.Quantity;
        if (!Take(pack, stack, quantity))
        {
            return false;
        }

        Vector3 spot = LootComponent.ScatterAround(body.GlobalPosition, 0);
        parent.CallDeferred(Node.MethodName.AddChild, ItemPickupFactory.Create(Unmarked(instance), quantity, spot, playerDropped: true));
        return true;
    }

    /// <summary>Whether an item may be dropped at all. Quest items stay: a dropped one can be
    /// walked away from, and the quest that wants it has no way to ask for it back.</summary>
    public static bool CanDrop(ItemInstance instance) => !instance.Locked && instance.Type != ItemType.Quest;

    /// <summary>Tells the feed the player's pack refused something (see <see cref="InventoryFullEvent"/>).</summary>
    public static void AnnouncePackFull(ItemInstance instance, int quantity)
    {
        if (Player() is { } player && quantity > 0)
        {
            EventBus.Instance?.Publish(new InventoryFullEvent(player, instance.Template, quantity));
        }
    }

    private static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;
}
