using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Makes a world entity a collectable item. Interacting transfers the held item
/// into the instigator's <see cref="InventoryComponent"/> and despawns the pickup
/// once empty (or leaves the remainder if the inventory filled up).
/// </summary>
[GlobalClass]
public partial class ItemPickupComponent : InteractableComponent
{
    /// <summary>Template for an editor/mundane pickup; wrapped as a plain instance
    /// on first use. Ignored once <see cref="Instance"/> is set directly (loot).</summary>
    [Export] public ItemResource? Item { get; set; }
    [Export] public int Quantity { get; set; } = 1;

    /// <summary>The concrete instance carried by this pickup (rolled loot sets this
    /// directly; mundane pickups derive it from <see cref="Item"/>).</summary>
    public ItemInstance? Instance { get; set; }

    private ItemInstance? Resolved => Instance ??= Item != null ? ItemInstance.Plain(Item) : null;

    public override string Prompt
    {
        get
        {
            ItemInstance? instance = Resolved;
            if (instance == null)
            {
                return "Pick up";
            }

            return Quantity > 1
                ? $"Pick up {instance.DisplayName} x{Quantity}"
                : $"Pick up {instance.DisplayName}";
        }
    }

    public override void Interact(IEntity instigator)
    {
        ItemInstance? instance = Resolved;
        if (instance == null)
        {
            return;
        }

        InventoryComponent? inventory = instigator.GetComponent<InventoryComponent>();
        if (inventory == null)
        {
            return;
        }

        int added = inventory.AddInstance(instance, Quantity);
        if (added <= 0)
        {
            Log.Info($"{instigator.DisplayName}'s inventory is full.");
            return;
        }

        EventBus.Instance?.Publish(new ItemPickedUpEvent(instigator, instance.Template, added));
        Log.Info($"{instigator.DisplayName} picked up {instance.DisplayName} x{added}.");

        Quantity -= added;
        if (Quantity <= 0 && Entity != null)
        {
            ((Node)Entity.Body).QueueFree();
        }
    }
}
