using Godot;

namespace Embervale.Items;

/// <summary>Broad category of an item, used for filtering, equipping and UI.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ItemType
{
    Misc,
    Consumable,
    Weapon,
    Armor,
    Material,
    Quest,
}

/// <summary>
/// Rarity tier. Drives UI colour now and the procedural loot tiers of Phase 7
/// (higher rarity = more/stronger affixes).
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

/// <summary>Presentation helpers for <see cref="ItemRarity"/>.</summary>
public static class ItemRarities
{
    public static Color Color(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => new Color(0.82f, 0.82f, 0.82f),
            ItemRarity.Uncommon => new Color(0.30f, 0.85f, 0.35f),
            ItemRarity.Rare => new Color(0.30f, 0.55f, 1.00f),
            ItemRarity.Epic => new Color(0.70f, 0.32f, 0.95f),
            ItemRarity.Legendary => new Color(1.00f, 0.60f, 0.12f),
            _ => Colors.White,
        };
    }
}
