namespace Embervale.Items;

/// <summary>
/// The body/gear slots an <see cref="EquippableItemResource"/> can occupy. One
/// item per slot. <see cref="None"/> marks an item that is not equippable.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
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

    /// <summary>Arrows and bolts. Appended 2026-09-05 with the ranged system; every ordinal above it
    /// was already in shipped saves and must not move.</summary>
    Ammo,
}

/// <summary>
/// Broad gear family of an <see cref="EquipmentSlot"/>, used to decide which
/// procedural affixes can roll on an item (weapons take offensive affixes, armor
/// defensive ones, accessories a bit of everything).
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum GearFamily
{
    None,
    Weapon,
    Armor,
    Accessory,
}

/// <summary>Display helpers for <see cref="EquipmentSlot"/>.</summary>
public static class EquipmentSlots
{
    /// <summary>Maps a slot onto its <see cref="GearFamily"/>.</summary>
    public static GearFamily FamilyOf(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.Ammo => GearFamily.Weapon,
            EquipmentSlot.Head or EquipmentSlot.Chest or EquipmentSlot.Hands
                or EquipmentSlot.Legs or EquipmentSlot.Feet => GearFamily.Armor,
            EquipmentSlot.Ring or EquipmentSlot.Amulet => GearFamily.Accessory,
            _ => GearFamily.None,
        };
    }

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
        EquipmentSlot.Ammo,
    };

    public static string Label(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.MainHand => "Main Hand",
            EquipmentSlot.OffHand => "Off Hand",
            EquipmentSlot.Ammo => "Ammo",
            _ => slot.ToString(),
        };
    }
}
