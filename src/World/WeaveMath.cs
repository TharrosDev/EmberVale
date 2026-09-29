namespace Embervale.World;

/// <summary>How strongly a region's Weave flows, in four readable bands. Ordinals are not persisted.</summary>
public enum WeaveBand
{
    /// <summary>Potency 0.9 and up: the Weave answers freely and the indicator stays hidden.</summary>
    Strong,

    /// <summary>0.65 to 0.9: a little thin.</summary>
    Thinning,

    /// <summary>0.4 to 0.65: ordinary magic is clearly weaker and dearer.</summary>
    Frayed,

    /// <summary>Under 0.4: the Weave is failing; only the corrupted path thrives.</summary>
    Failing,
}

/// <summary>
/// Pure maths for the fading <b>Weave</b>: how a region's magic <em>potency</em> (0 = dead, 1 = full)
/// bends the cost and power of a cast. Godot-free so the dial is unit-testable; <see cref="Weave"/> holds
/// the live value and <see cref="Embervale.Magic.SpellcastingComponent"/> applies it.
///
/// The rule is one sentence: <b>as the Weave fades, ordinary magic loses up to 40% of its power and
/// costs up to 40% more, and corrupted magic gains up to 35% power and costs up to 35% less.</b>
/// Fairness is built into the constants, not patched on top:
/// <list type="bullet">
/// <item>Ordinary magic is clamped: it never falls under <see cref="MinOrdinaryPower"/> nor costs more than
/// <see cref="MaxOrdinaryCost"/>, so a dead Weave makes a mage worse, never helpless.</item>
/// <item>Corrupted magic is only ever neutral or better; it is at its plain strength while the Weave is full.</item>
/// <item>Both dials are linear in potency and meet at exactly 1 when the Weave is full.</item>
/// </list>
/// </summary>
public static class WeaveMath
{
    /// <summary>The weakest an ordinary spell ever gets (a dead Weave).</summary>
    public const float MinOrdinaryPower = 0.6f;

    /// <summary>The dearest an ordinary spell ever gets (a dead Weave).</summary>
    public const float MaxOrdinaryCost = 1.4f;

    /// <summary>The strongest a corrupted spell gets (a dead Weave).</summary>
    public const float MaxCorruptPower = 1.35f;

    /// <summary>The cheapest a corrupted spell gets (a dead Weave).</summary>
    public const float MinCorruptCost = 0.65f;

    private const float StrongFrom = 0.9f;
    private const float ThinningFrom = 0.65f;
    private const float FrayedFrom = 0.4f;

    /// <summary>The damage/healing multiplier a cast gets at <paramref name="potency"/>.</summary>
    public static float PowerMultiplier(float potency, bool corrupted)
    {
        float p = Clamp01(potency);
        return corrupted ? Lerp(MaxCorruptPower, 1f, p) : Lerp(MinOrdinaryPower, 1f, p);
    }

    /// <summary>The mana-cost multiplier a cast pays at <paramref name="potency"/>.</summary>
    public static float CostMultiplier(float potency, bool corrupted)
    {
        float p = Clamp01(potency);
        return corrupted ? Lerp(MinCorruptCost, 1f, p) : Lerp(MaxOrdinaryCost, 1f, p);
    }

    /// <summary>The band a potency falls in (drives the region indicator's wording and colour).</summary>
    public static WeaveBand BandOf(float potency)
    {
        float p = Clamp01(potency);
        if (p >= StrongFrom)
        {
            return WeaveBand.Strong;
        }

        return p >= ThinningFrom ? WeaveBand.Thinning : p >= FrayedFrom ? WeaveBand.Frayed : WeaveBand.Failing;
    }

    /// <summary>Whether the indicator should show at all: it hides while the Weave is strong.</summary>
    public static bool ShowsIndicator(float potency) => BandOf(potency) != WeaveBand.Strong;

    /// <summary>The whole-number percentage change to power at <paramref name="potency"/>
    /// (-28 for an ordinary cast at 0.3). What the indicator prints.</summary>
    public static int PowerPercent(float potency, bool corrupted) =>
        (int)System.Math.Round((PowerMultiplier(potency, corrupted) - 1f) * 100f);

    private static float Clamp01(float v) =>
        float.IsNaN(v) ? 1f : (v < 0f ? 0f : (v > 1f ? 1f : v));

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
