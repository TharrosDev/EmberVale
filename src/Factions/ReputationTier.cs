using Godot;

namespace Embervale.Factions;

/// <summary>
/// Named bands of the player's standing with a faction, derived from a numeric
/// reputation value in roughly the range [-100, 100]. Tiers drive consequences
/// (whether a faction's members are hostile) and the reputation UI. Ordered low→high
/// so tier comparisons (e.g. "at or below the hostile threshold") work directly.
/// </summary>
public enum ReputationTier
{
    Hated,
    Hostile,
    Unfriendly,
    Neutral,
    Friendly,
    Honored,
    Allied,
}

/// <summary>Maps reputation values to <see cref="ReputationTier"/>s and provides labels/colours.</summary>
public static class ReputationTiers
{
    public const int Min = -100;
    public const int Max = 100;

    /// <summary>The tier a numeric reputation value falls into.</summary>
    public static ReputationTier Of(int value)
    {
        return value switch
        {
            <= -75 => ReputationTier.Hated,
            <= -25 => ReputationTier.Hostile,
            < 0 => ReputationTier.Unfriendly,
            < 25 => ReputationTier.Neutral,
            < 60 => ReputationTier.Friendly,
            < 90 => ReputationTier.Honored,
            _ => ReputationTier.Allied,
        };
    }

    public static string Label(ReputationTier tier) => tier switch
    {
        ReputationTier.Hated => "Hated",
        ReputationTier.Hostile => "Hostile",
        ReputationTier.Unfriendly => "Unfriendly",
        ReputationTier.Neutral => "Neutral",
        ReputationTier.Friendly => "Friendly",
        ReputationTier.Honored => "Honored",
        _ => "Allied",
    };

    public static Color Color(ReputationTier tier) => tier switch
    {
        ReputationTier.Hated => new Color(0.85f, 0.25f, 0.25f),
        ReputationTier.Hostile => new Color(0.86f, 0.42f, 0.34f),
        ReputationTier.Unfriendly => new Color(0.82f, 0.62f, 0.38f),
        ReputationTier.Neutral => new Color(0.78f, 0.80f, 0.84f),
        ReputationTier.Friendly => new Color(0.55f, 0.78f, 0.52f),
        ReputationTier.Honored => new Color(0.40f, 0.78f, 0.62f),
        _ => new Color(0.45f, 0.70f, 0.95f),
    };
}
