using System;

namespace Embervale.Items;

/// <summary>Why an item could not be equipped. Runtime only, never saved.</summary>
public enum EquipRefusal
{
    None,

    /// <summary>Not an equippable, or one authored with no slot.</summary>
    NotEquippable,

    /// <summary>The wearer does not hold that exact instance.</summary>
    NotHeld,

    /// <summary>The wearer is below the item's required level.</summary>
    LevelTooLow,

    /// <summary>An off-hand item beside a two-handed weapon.</summary>
    HandsFull,

    /// <summary>What it would take off has nowhere to go.</summary>
    PackFull,
}

/// <summary>
/// The arithmetic of pack room and of whether a piece of gear may go on, free of the engine so it is
/// unit-testable. <see cref="InventoryComponent"/> and <see cref="EquipmentComponent"/> feed it live
/// numbers.
/// </summary>
public static class InventoryRules
{
    public const string LevelReasonKey = "item.equip.refused.level";
    public const string HandsReasonKey = "item.equip.refused.two_handed";
    public const string PackFullReasonKey = "item.equip.refused.pack_full";

    /// <summary>The highest level an item may require (the level cap).</summary>
    public const int MaxRequiredLevel = 50;

    /// <summary>
    /// New pack slots needed to store <paramref name="quantity"/> units that stack to
    /// <paramref name="maxStack"/>, given <paramref name="mergeSpace"/> free units in the stacks
    /// they can already join. A non-stackable item (max stack 1) takes one slot each.
    /// </summary>
    public static int SlotsNeeded(int quantity, int maxStack, int mergeSpace)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        int perSlot = Math.Max(1, maxStack);
        int left = perSlot > 1 ? quantity - Math.Max(0, mergeSpace) : quantity;
        return left <= 0 ? 0 : ((left - 1) / perSlot) + 1;
    }

    /// <summary>A required level of 0 (or less) asks nothing; otherwise the wearer must have reached it.</summary>
    public static bool MeetsLevel(int requiredLevel, int level) => requiredLevel <= 0 || level >= requiredLevel;

    /// <summary>
    /// Whether a held, equippable item may go on. Checked in the order the player would want to
    /// hear about: their level first, then the two-handed conflict, then pack room for whatever the
    /// swap takes off (<paramref name="slotsNeeded"/> against <paramref name="slotsFree"/>, the
    /// latter counted after the new item has left the pack).
    /// </summary>
    public static EquipRefusal CheckEquip(
        int requiredLevel, int level, bool isOffHand, bool mainHandIsTwoHanded, int slotsNeeded, int slotsFree)
    {
        if (!MeetsLevel(requiredLevel, level))
        {
            return EquipRefusal.LevelTooLow;
        }

        if (isOffHand && mainHandIsTwoHanded)
        {
            return EquipRefusal.HandsFull;
        }

        return slotsNeeded > slotsFree ? EquipRefusal.PackFull : EquipRefusal.None;
    }

    /// <summary>The locale key that tells the player why; empty when there is nothing to say (the
    /// item is not equippable or not held, which a UI never offers).</summary>
    public static string ReasonKey(EquipRefusal refusal)
    {
        return refusal switch
        {
            EquipRefusal.LevelTooLow => LevelReasonKey,
            EquipRefusal.HandsFull => HandsReasonKey,
            EquipRefusal.PackFull => PackFullReasonKey,
            _ => string.Empty,
        };
    }
}
