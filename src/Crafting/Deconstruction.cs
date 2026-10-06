using Embervale.Items;

namespace Embervale.Crafting;

/// <summary>
/// Pure salvage maths behind deconstruction — the inverse of crafting. Deconstructing an item
/// returns a floored fraction of the materials its recipe would consume, plus XP. Kept Godot-free
/// so the recovery + XP formulas are unit-testable without an engine; <see cref="CraftingComponent"/>
/// applies them against the inventory and progression.
/// </summary>
public static class Deconstruction
{
    /// <summary>Fraction of each ingredient recovered. Below 1 (and floored), so salvaging then
    /// re-crafting always nets a loss — deconstruction can never duplicate materials.</summary>
    public const float RecoveryRate = 0.5f;

    /// <summary>The most a salvage perk can lift <see cref="RecoveryRate"/> to. Held below 1 so that salvaging
    /// then re-crafting still nets a loss however many perks the player stacks.</summary>
    public const float MaxRecoveryRate = 0.75f;

    /// <summary>Flat XP granted for any salvage, before the worth/rarity bonuses below.</summary>
    public const int BaseXp = 3;

    /// <summary>XP added per rarity tier (Common 0 … Legendary 4): better gear is worth more.</summary>
    public const int XpPerRarityTier = 8;

    /// <summary>Materials returned for an ingredient the recipe needs <paramref name="ingredientQuantity"/>
    /// of — floored, so a 1-quantity ingredient yields nothing (the anti-duplication guard).</summary>
    public static int RecoveredQuantity(int ingredientQuantity) => RecoveredQuantity(ingredientQuantity, 0f);

    /// <summary>As above with a salvage perk's <paramref name="yieldBonus"/> (a fraction added to
    /// <see cref="RecoveryRate"/>), the sum capped at <see cref="MaxRecoveryRate"/>.</summary>
    public static int RecoveredQuantity(int ingredientQuantity, float yieldBonus) =>
        (int)(ingredientQuantity * System.Math.Min(RecoveryRate + System.Math.Max(0f, yieldBonus), MaxRecoveryRate));

    /// <summary>XP for salvaging an item worth <paramref name="itemValue"/> at the given
    /// <paramref name="rarity"/>. Always at least 1, so salvage is never pointless.</summary>
    public static int Xp(int itemValue, ItemRarity rarity)
    {
        int xp = BaseXp + (itemValue / 2) + ((int)rarity * XpPerRarityTier);
        return xp < 1 ? 1 : xp;
    }

    /// <summary>Generic <c>Scrap</c> returned when an item has no crafting recipe to reverse — so any
    /// item is still worth salvaging. Scales with rarity (Common 1 … Legendary 5).</summary>
    public static int ScrapYield(ItemRarity rarity) => ScrapYield(rarity, 0f);

    /// <summary>As above with a salvage perk's <paramref name="yieldBonus"/> (a fraction of the base yield,
    /// floored): a Common piece still gives 1, a Legendary one 5 to 7.</summary>
    public static int ScrapYield(ItemRarity rarity, float yieldBonus) =>
        (int)((1 + (int)rarity) * (1f + System.Math.Max(0f, yieldBonus)));
}
