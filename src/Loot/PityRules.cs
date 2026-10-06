using System;
using Embervale.Items;

namespace Embervale.Loot;

/// <summary>
/// Bad-luck protection for rolled gear. The streak counts consecutive rolled pieces that came up
/// below <see cref="ItemRarity.Rare"/>; past <see cref="GraceRolls"/> each further dry roll adds
/// quality to the next one, and at <see cref="GuaranteeAt"/> the next piece is Rare at the least.
/// Any Rare or better resets the streak. Pure, so the whole curve is unit-tested; the counter itself
/// is saved by <see cref="LootLedger"/>.
/// </summary>
public static class PityRules
{
    /// <summary>Dry rolls that cost nothing: an ordinary run of bad luck is not a streak.</summary>
    public const int GraceRolls = 6;

    /// <summary>Quality added per dry roll past the grace.</summary>
    public const float QualityPerRoll = 0.12f;

    /// <summary>The most quality a streak can add (reached well before the guarantee).</summary>
    public const float MaxBonusQuality = 1.5f;

    /// <summary>The streak length at which the next rolled piece is forced to Rare or better.</summary>
    public const int GuaranteeAt = 24;

    /// <summary>Quality added to the next rarity roll after <paramref name="dryStreak"/> dry rolls.</summary>
    public static float BonusQuality(int dryStreak)
    {
        int past = dryStreak - GraceRolls;
        return past <= 0 ? 0f : Math.Min(MaxBonusQuality, past * QualityPerRoll);
    }

    /// <summary>True when the streak has run long enough that the next piece cannot be below Rare.</summary>
    public static bool IsGuaranteed(int dryStreak) => dryStreak >= GuaranteeAt;

    /// <summary>The rarity the next piece actually gets: the roll, lifted to Rare on a guarantee.</summary>
    public static ItemRarity Apply(ItemRarity rolled, int dryStreak)
    {
        return IsGuaranteed(dryStreak) && rolled < ItemRarity.Rare ? ItemRarity.Rare : rolled;
    }

    /// <summary>The streak after a piece of <paramref name="rarity"/> dropped.</summary>
    public static int Next(int dryStreak, ItemRarity rarity)
    {
        return rarity >= ItemRarity.Rare ? 0 : Math.Max(0, dryStreak) + 1;
    }
}
