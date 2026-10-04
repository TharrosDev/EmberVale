using Embervale.Factions;

namespace Embervale.UI;

/// <summary>Presentation rules for a quest's rewards. Godot-free.</summary>
public static class RewardRules
{
    /// <summary>
    /// The standing tier whose colour a faction reward of <paramref name="amount"/> is drawn in. A reward is
    /// a change, not a standing, so this reads the sign and size of the change rather than classifying
    /// the number as a standing: a gain warms toward Friendly/Honored, a loss cools toward
    /// Unfriendly/Hostile. The sign is always printed too, so colour is never the only channel.
    /// </summary>
    public static ReputationTier FactionTier(int amount) => amount switch
    {
        >= 20 => ReputationTier.Honored,
        > 0 => ReputationTier.Friendly,
        0 => ReputationTier.Neutral,
        > -10 => ReputationTier.Unfriendly,
        _ => ReputationTier.Hostile,
    };

    /// <summary>"+5" / "-5" / "0". ASCII sign so it survives every font.</summary>
    public static string Signed(int amount) => amount > 0 ? $"+{amount}" : amount.ToString();
}
