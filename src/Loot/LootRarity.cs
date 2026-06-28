using Embervale.Items;
using Godot;

namespace Embervale.Loot;

/// <summary>
/// Rolls a rarity tier for a generated item and decides how many affixes that tier
/// carries. The base weights favour common drops; a <c>quality</c> term (driven by
/// the loot table, enemy level and luck) shifts the distribution toward the higher
/// tiers without ever guaranteeing them.
/// </summary>
public static class LootRarity
{
    // Relative base weights, Common → Legendary.
    private static readonly float[] BaseWeights = { 64f, 24f, 9f, 2.5f, 0.5f };

    /// <summary>How many affixes an item of each rarity rolls.</summary>
    public static int AffixCount(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => 0,
            ItemRarity.Uncommon => 1,
            ItemRarity.Rare => 2,
            ItemRarity.Epic => 3,
            ItemRarity.Legendary => 4,
            _ => 0,
        };
    }

    /// <summary>
    /// Rolls a rarity. <paramref name="quality"/> (typically 0..1+) biases weight
    /// toward higher tiers by scaling each tier's weight by an increasing factor.
    /// </summary>
    public static ItemRarity Roll(RandomNumberGenerator rng, float quality)
    {
        return Select(quality, rng.Randf());
    }

    /// <summary>
    /// Pure tier selection: maps a uniform <paramref name="roll01"/> sample in <c>[0,1)</c> to a
    /// rarity using the quality-boosted weights. Split out of <see cref="Roll"/> (which just feeds
    /// it <c>rng.Randf()</c>) so the distribution is unit-testable without Godot's RNG.
    /// </summary>
    public static ItemRarity Select(float quality, float roll01)
    {
        quality = System.Math.Max(0f, quality);
        roll01 = System.Math.Clamp(roll01, 0f, 0.99999988f); // keep the sample in [0,1)

        float total = 0f;
        var weights = new float[BaseWeights.Length];
        for (int i = 0; i < BaseWeights.Length; i++)
        {
            // Higher tiers (larger i) gain more from quality.
            float tierBoost = 1f + (quality * i);
            weights[i] = BaseWeights[i] * tierBoost;
            total += weights[i];
        }

        float pick = roll01 * total;
        for (int i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];
            if (pick <= 0f)
            {
                return (ItemRarity)i;
            }
        }

        return ItemRarity.Common;
    }
}
