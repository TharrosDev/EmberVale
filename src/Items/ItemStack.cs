using Godot;

namespace Embervale.Items;

/// <summary>
/// A runtime quantity of one <see cref="ItemInstance"/> occupying a single
/// inventory slot. Mutable quantity; the owning <see cref="InventoryComponent"/>
/// enforces stacking rules against <see cref="ItemInstance.MaxStack"/> (only
/// affix-less instances stack — rolled loot is unique).
/// </summary>
public sealed class ItemStack
{
    public ItemStack(ItemInstance instance, int quantity)
    {
        Instance = instance;
        Quantity = quantity;
    }

    public ItemInstance Instance { get; }

    /// <summary>Convenience access to the underlying template.</summary>
    public ItemResource Item => Instance.Template;

    public int Quantity { get; set; }

    public bool IsFull => Quantity >= Instance.MaxStack;

    public int SpaceLeft => Mathf.Max(0, Instance.MaxStack - Quantity);

    public float Weight => Quantity * Instance.Weight;
}
