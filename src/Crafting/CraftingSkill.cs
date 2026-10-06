using System;
using Embervale.Economy;
using Embervale.Items;

namespace Embervale.Crafting;

/// <summary>
/// The pure half of the crafting skill: how crafting experience becomes a rank, what rank a recipe
/// tier asks for, and how rank turns into the workmanship (<see cref="CraftQuality"/>) of a crafted
/// piece. Godot-free so every rule is unit-testable; <see cref="CraftingComponent"/> holds the saved
/// experience and applies these.
///
/// ⚠️ <b>Quality is derived, never rolled.</b> It is <see cref="StableRoll.Percent"/> of the saved
/// craft serial and the recipe id against odds that depend only on rank and recipe tier, so a
/// quickload replays the piece it was taken before instead of letting the player fish for a
/// Masterwork. The same rule <see cref="MaterialSaving"/> follows, on its own salt.
/// </summary>
public static class CraftingSkill
{
    /// <summary>The highest rank. Past it, experience still accrues but buys nothing.</summary>
    public const int MaxRank = 10;

    /// <summary>The highest recipe tier (the six realms).</summary>
    public const int MaxTier = 6;

    /// <summary>Keeps the quality roll distinct from the other <see cref="StableRoll"/> users.</summary>
    public const int Salt = 0x5155414C;   // 'QUAL'

    /// <summary>Experience for one craft of a tier-1 (or legacy, tier-0) recipe.</summary>
    public const int XpPerTier = 10;

    /// <summary>Total experience needed to hold <paramref name="rank"/>: 0, 100, 300, 600, 1000 ...
    /// Each rank costs about ten crafts at the best tier the rank below it can make.</summary>
    public static int XpForRank(int rank)
    {
        int r = Math.Clamp(rank, 0, MaxRank);
        return 50 * r * (r + 1);
    }

    /// <summary>The rank <paramref name="xp"/> experience buys, 0..<see cref="MaxRank"/>.</summary>
    public static int RankOf(int xp)
    {
        int rank = 0;
        while (rank < MaxRank && xp >= XpForRank(rank + 1))
        {
            rank++;
        }

        return rank;
    }

    /// <summary>The locale key of a rank's title: <c>craft.rank.0</c> .. <c>craft.rank.10</c>.</summary>
    public static string RankNameKey(int rank) => "craft.rank." + Math.Clamp(rank, 0, MaxRank);

    /// <summary>Progress through the current rank, 0..1 (1 at the cap).</summary>
    public static float RankProgress(int xp)
    {
        int rank = RankOf(xp);
        if (rank >= MaxRank)
        {
            return 1f;
        }

        int floor = XpForRank(rank);
        return Math.Clamp((xp - floor) / (float)(XpForRank(rank + 1) - floor), 0f, 1f);
    }

    /// <summary>The rank a crafter needs before their own hands can make a recipe of
    /// <paramref name="tier"/>: nothing for tiers 0 and 1, then one rank per realm.</summary>
    public static int RequiredRank(int tier) => Math.Clamp(tier, 1, MaxTier) - 1;

    /// <summary>How far above a recipe's requirement the crafter stands (never negative).</summary>
    public static int Mastery(int rank, int tier) => Math.Max(0, rank - RequiredRank(tier));

    /// <summary>
    /// Experience for one completed craft. Work at the edge of the crafter's skill pays in full;
    /// a recipe two or more ranks beneath them pays a quarter, so ranks are earned on the realm's
    /// own materials and not by cooking oatcakes.
    /// </summary>
    public static int XpForCraft(int tier, int rank)
    {
        int full = XpPerTier * Math.Clamp(tier, 1, MaxTier);
        return Mastery(rank, tier) >= 2 ? Math.Max(1, full / 4) : full;
    }

    /// <summary>
    /// The chance, in whole percent, of each tier above Standard for a crafter of
    /// <paramref name="rank"/> making a recipe of <paramref name="tier"/>. Monotonic in rank; the
    /// three never sum past 90, so Standard always remains possible.
    /// </summary>
    public static (int Fine, int Superior, int Masterwork) Odds(int rank, int tier)
    {
        int mastery = Mastery(rank, tier);
        int fine = Math.Min(45, 15 + (10 * mastery));
        int superior = Math.Min(30, 5 * mastery);
        int masterwork = Math.Clamp((2 * mastery) - 2, 0, 15);
        return (fine, superior, masterwork);
    }

    /// <summary>The quality a percent roll (0..99) lands on against <paramref name="odds"/>: the
    /// best tiers take the lowest rolls.</summary>
    public static CraftQuality FromRoll((int Fine, int Superior, int Masterwork) odds, uint roll)
    {
        if (roll < odds.Masterwork)
        {
            return CraftQuality.Masterwork;
        }

        if (roll < odds.Masterwork + odds.Superior)
        {
            return CraftQuality.Superior;
        }

        return roll < odds.Masterwork + odds.Superior + odds.Fine ? CraftQuality.Fine : CraftQuality.Standard;
    }

    /// <summary>The workmanship of the craft numbered <paramref name="serial"/>.</summary>
    public static CraftQuality Roll(int rank, int tier, int serial, string recipeId) =>
        FromRoll(Odds(rank, tier), StableRoll.Percent(serial, Salt, recipeId));

    /// <summary>How much a crafter's mastery biases a crafted piece's affix values toward their
    /// maximum (added to the loot generator's value quality), capped at 0.3.</summary>
    public static float AffixLuck(int rank, int tier) => Math.Min(0.3f, 0.05f * Mastery(rank, tier));
}
