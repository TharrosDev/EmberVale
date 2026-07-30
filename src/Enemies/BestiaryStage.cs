namespace Embervale.Enemies;

/// <summary>How much the bestiary will tell you about a creature (Phase 34G).</summary>
public enum BestiaryStage
{
    /// <summary>Never killed one. The entry shows as an unknown silhouette.</summary>
    Unseen,

    /// <summary>Killed at least one — the name and a teaser, but not the full entry.</summary>
    Sighted,

    /// <summary>Hunted enough of them to have written the page. Full lore.</summary>
    Known,
}

/// <summary>
/// The reveal rule behind the bestiary: how many kills it takes before a creature's page opens up.
/// Pure (Godot-free) so the one piece of 34G logic that can be interestingly wrong is unit-tested,
/// the same way <see cref="GuardCycle"/> and <see cref="PackFlank"/> are.
/// </summary>
public static class BestiaryStages
{
    /// <summary>
    /// The stage a creature's entry is at. No kills is <see cref="BestiaryStage.Unseen"/>;
    /// <paramref name="killsToKnow"/> or more is <see cref="BestiaryStage.Known"/>; anything between
    /// is <see cref="BestiaryStage.Sighted"/>.
    ///
    /// A <paramref name="killsToKnow"/> of 1 or less means the first kill tells you everything, so
    /// Sighted collapses out — useful for a boss you only ever fight once.
    /// </summary>
    public static BestiaryStage Of(int kills, int killsToKnow)
    {
        if (kills <= 0)
        {
            return BestiaryStage.Unseen;
        }

        return kills >= (killsToKnow < 1 ? 1 : killsToKnow) ? BestiaryStage.Known : BestiaryStage.Sighted;
    }
}
