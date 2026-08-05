using Embervale.Items;

namespace Embervale.Housing;

/// <summary>Why a display stand can or cannot be used right now.</summary>
public enum TrophyOutcome
{
    /// <summary>The stand names a property id nothing in the database answers to.</summary>
    UnknownProperty,

    /// <summary>A real holding, but not one the player has claimed.</summary>
    NotOwned,

    /// <summary>Usable now.</summary>
    Open,
}

/// <summary>
/// Whether a display stand will open, and what it will accept (Phase 37D). Fourth in the
/// <see cref="PropertyClaim"/> / <see cref="PropertyStorage"/> / <see cref="PlacementCheck"/> line,
/// and pure for the same reason all three are: the prompt, the interaction and the panel's Store
/// button all read these two functions, so what the player is told and what actually happens cannot
/// drift apart.
/// </summary>
public static class TrophyDisplay
{
    /// <summary>
    /// The floor a trophy has to clear. Deliberately a <b>rarity</b> rather than a marker item type:
    /// it costs no authoring, it takes the Iron Heart (Legendary) and every future boss reward on the
    /// day they land, and it lets a rolled Epic drop the player actually earned go on the wall.
    /// Anything below it is inventory, not achievement.
    /// </summary>
    public const ItemRarity MinimumRarity = ItemRarity.Epic;

    /// <summary>
    /// Resolves an attempt to use a stand. <paramref name="propertyKnown"/> is <c>false</c> when the
    /// stand's holding does not resolve against <see cref="PropertyDatabase"/>.
    ///
    /// The order matches <see cref="PropertyStorage.Resolve"/> and for the same reason: an
    /// unresolvable id is an <b>authoring</b> fault, not a gate the player can pass, and reporting it
    /// as "not yours" would hide a typo behind a plausible-looking refusal.
    /// </summary>
    public static TrophyOutcome Resolve(bool propertyKnown, bool owned)
    {
        if (!propertyKnown)
        {
            return TrophyOutcome.UnknownProperty;
        }

        return owned ? TrophyOutcome.Open : TrophyOutcome.NotOwned;
    }

    /// <summary>True when an item of this rarity is worth standing on a plinth.</summary>
    public static bool CanDisplay(ItemRarity rarity) => rarity >= MinimumRarity;
}
