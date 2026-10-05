using System.Collections.Generic;
using Embervale.Economy;

namespace Embervale.Crafting;

/// <summary>
/// The pure half of a perk's material-saving crafts: whether a given craft saves material, and which
/// ingredient it hands back. Godot-free so it can be tested; <see cref="CraftingComponent"/> applies it.
///
/// ⚠️ <b>The roll is derived from a saved craft serial, never rolled.</b> It is
/// <see cref="StableRoll.Percent"/> of (serial, <see cref="Salt"/>, recipe id), so a quickload replays the
/// outcome of the craft it was taken before instead of letting the player reroll it for a refund; the serial
/// is the component's saved <c>crafts</c> counter. ⚠️ <b>A craft never becomes free</b>: only an ingredient
/// the recipe needs two or more of can be saved, and only one unit of it, so every craft still consumes
/// something real.
/// </summary>
public static class MaterialSaving
{
    /// <summary>Keeps this roll distinct from the haggle and wager rolls that share <see cref="StableRoll"/>.</summary>
    public const int Salt = 0x4D415453;   // 'MATS'

    /// <summary>Whether the craft numbered <paramref name="serial"/> of <paramref name="recipeId"/> saves
    /// material at <paramref name="chancePercent"/> (0 never, 100 or more always).</summary>
    public static bool Saves(int serial, string recipeId, int chancePercent) =>
        chancePercent > 0 &&
        (chancePercent >= 100 || StableRoll.Percent(serial, Salt, recipeId) < (uint)chancePercent);

    /// <summary>The index of the ingredient a saving craft hands one unit of back: the one the recipe needs
    /// most of (the first on a tie), and only when that is at least 2. -1 when the recipe has nothing safe to
    /// give back (every ingredient needs a single unit).</summary>
    public static int SavedIngredient(IReadOnlyList<int> quantities)
    {
        int best = -1;
        for (int i = 0; i < quantities.Count; i++)
        {
            if (quantities[i] >= 2 && (best < 0 || quantities[i] > quantities[best]))
            {
                best = i;
            }
        }

        return best;
    }
}
