namespace Embervale.Progression;

/// <summary>
/// The gold price of resetting every paid perk rank. Pure and Godot-free. A respec costs
/// <c>30 + 12 per point spent</c>, and each earlier respec makes the next dearer by a quarter of the
/// base, capped after four: spending 20 points costs 270 the first time and 540 from the fifth. That sits
/// between the mount (400) and the hired sword (500) in the DESIGN.md sink table: dear enough to make
/// a build a decision, cheap enough that nobody is locked into a mistake.
/// </summary>
public static class RespecRules
{
    public const int BaseCost = 30;
    public const int CostPerPoint = 12;

    /// <summary>Each earlier respec adds this many quarters of the base price.</summary>
    public const int RepeatCap = 4;

    /// <summary>Gold to respec. 0 when nothing was spent (there is nothing to reset).</summary>
    public static int Cost(int pointsSpent, int respecCount)
    {
        if (pointsSpent <= 0)
        {
            return 0;
        }

        int basis = BaseCost + (CostPerPoint * pointsSpent);
        int quarters = 4 + System.Math.Clamp(respecCount, 0, RepeatCap);
        return ((basis * quarters) + 2) / 4; // integer round-half-up of basis * (1 + 0.25 * repeats)
    }
}
