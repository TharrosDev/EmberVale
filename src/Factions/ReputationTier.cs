using Godot;

namespace Embervale.Factions;

/// <summary>
/// Named bands of the player's standing with a faction, derived from a numeric
/// reputation value in roughly the range [-100, 100]. Tiers drive consequences
/// (whether a faction's members are hostile) and the reputation UI. Ordered low→high
/// so tier comparisons (e.g. "at or below the hostile threshold") work directly.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
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

    /// <summary>
    /// The tier name as the <b>player</b> reads it, on the character sheet. Separate from
    /// <see cref="Label"/> on purpose: that one is baked into save headers, the analytics sink and
    /// the dev console, so localizing it in place would make a persisted string change with the
    /// language and show a slot saved in one locale under a tier name from another. The invariant
    /// identifier and the display name are two jobs, and this is the display one.
    /// </summary>
    public static string DisplayName(ReputationTier tier) => Localization.Loc.T(tier switch
    {
        ReputationTier.Hated => "rep.tier.hated",
        ReputationTier.Hostile => "rep.tier.hostile",
        ReputationTier.Unfriendly => "rep.tier.unfriendly",
        ReputationTier.Neutral => "rep.tier.neutral",
        ReputationTier.Friendly => "rep.tier.friendly",
        ReputationTier.Honored => "rep.tier.honored",
        _ => "rep.tier.allied",
    });

    /// <summary>The tier's stable, culture-invariant name — for save headers, analytics and the dev
    /// console. Use <see cref="DisplayName"/> for anything the player reads.</summary>
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

    /// <summary>
    /// The standing ramp, retuned to the world's palette in Phase 37.5B and pinned to WCAG AA by
    /// <c>UiContrastTests</c> — these render as text in the character screen's REPUTATION section.
    /// This is the third domain colour authority beside <c>ItemRarities.Color</c> and
    /// <c>SpellSchools.Color</c>; the pattern is the same, and <c>UiTheme</c> reads from it rather
    /// than keeping a copy.
    ///
    /// It is a **diverging** ramp (hostile red ← bone → allied blue), not an ordered one, and
    /// deliberately does not carry the strict luminance ordering the rarity ramp does. It does not
    /// need to: a standing always renders beside its own tier name and value ("Friendly +40"), so
    /// the colour is already redundant. Rarity on a grid slot often has no such words, which is
    /// why that ramp has to work with hue removed and this one does not.
    ///
    /// <c>Hated</c> exceeds the usual saturation ceiling on the same grounds <c>AccentHot</c> does:
    /// it is an alarm state, and the whole point of one is that it is louder than its neighbours.
    /// </summary>
    public static Color Color(ReputationTier tier) => tier switch
    {
        ReputationTier.Hated => new Color(0.90f, 0.38f, 0.30f),      // hotter than Bad — an alarm
        ReputationTier.Hostile => new Color(0.82f, 0.42f, 0.36f),    // UiTheme.Bad
        ReputationTier.Unfriendly => new Color(0.82f, 0.70f, 0.52f), // amber caution
        ReputationTier.Neutral => new Color(0.74f, 0.71f, 0.60f),    // UiTheme.Neutral — bone
        ReputationTier.Friendly => new Color(0.58f, 0.78f, 0.54f),
        ReputationTier.Honored => new Color(0.46f, 0.80f, 0.70f),    // teal, to part from Friendly
        _ => new Color(0.62f, 0.76f, 0.92f),                          // Allied — the cold end
    };
}
