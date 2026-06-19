namespace Embervale.Items;

/// <summary>
/// The body/gear slots an <see cref="EquippableItemResource"/> can occupy. One
/// item per slot. <see cref="None"/> marks an item that is not equippable.
/// </summary>
public enum EquipmentSlot
{
    None,
    MainHand,
    OffHand,
    Head,
    Chest,
    Hands,
    Legs,
    Feet,
    Ring,
    Amulet,
}

/// <summary>Display helpers for <see cref="EquipmentSlot"/>.</summary>
public static class EquipmentSlots
{
    /// <summary>The slots shown in the equipment UI, in order.</summary>
    public static readonly EquipmentSlot[] DisplayOrder =
    {
        EquipmentSlot.MainHand,
        EquipmentSlot.OffHand,
        EquipmentSlot.Head,
        EquipmentSlot.Chest,
        EquipmentSlot.Hands,
        EquipmentSlot.Legs,
        EquipmentSlot.Feet,
        EquipmentSlot.Ring,
        EquipmentSlot.Amulet,
    };

    public static string Label(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.MainHand => "Main Hand",
            EquipmentSlot.OffHand => "Off Hand",
            _ => slot.ToString(),
        };
    }
}
