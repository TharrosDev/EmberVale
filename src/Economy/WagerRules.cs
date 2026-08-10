namespace Embervale.Economy;

/// <summary>
/// Whether a throw at a gambling house comes good, how many are left today, and whether the house is
/// authored as a losing proposition (Phase 38R2). Pure and Godot-free like <see cref="ContractRules"/>,
/// <see cref="ShopPricing"/> and <see cref="CommissionRules"/> — the test project throws an
/// <c>AccessViolationException</c> constructing any Godot object, and this is the half of the feature
/// that can therefore actually be tested.
///
/// ⚠️ <b>THE OUTCOME IS DERIVED FROM THE DAY, THE THROW AND THE HOUSE — NEVER ROLLED AND NEVER SAVED.</b>
/// 38Q2's board took that shape for its rotation; a wager needs it more, because a saved roll is a roll
/// a quickload can re-take. Reloading here replays the <em>same</em> result: the player can decline to
/// throw, but cannot fish for a better one. What stops the day being farmed is not the arithmetic at
/// all — it is <c>WagerLedger</c> counting the throws.
///
/// ⚠️ <b>And that means no engine RNG, no <c>System.Random</c> and no <c>string.GetHashCode</c>.</b>
/// .NET randomises string hashing per process, so a house folded in that way would pay differently
/// after a restart — which is the one thing this design exists to prevent, and would look exactly like
/// an RNG working correctly. <see cref="StableRoll"/> is an explicit FNV-1a for that reason.
/// </summary>
public static class WagerRules
{
    /// <summary>
    /// Whether the throw numbered <paramref name="playIndex"/> (0-based, within the day) at
    /// <paramref name="houseId"/> wins.
    ///
    /// The three inputs are mixed into one scrambled integer and compared against the authored chance,
    /// so two houses never share a day's results and consecutive throws never share a throw's.
    /// </summary>
    public static bool Won(int day, int playIndex, string houseId, int winPercent)
    {
        if (winPercent <= 0)
        {
            return false;
        }

        if (winPercent >= 100)
        {
            return true;
        }

        // 38S lifted this arithmetic into StableRoll when haggling became its second caller. It is the
        // same expression to the character — the test pinning a hard-coded win/loss string is what
        // proves the move changed no outcome.
        return StableRoll.Percent(day, playIndex, houseId) < (uint)winPercent;
    }

    /// <summary>Throws still available today. Clamped at zero, so a <c>PlaysPerDay</c> lowered between
    /// saves cannot hand back a negative count for a day already played out.</summary>
    public static int PlaysLeft(int playsUsed, int playsPerDay)
    {
        int left = playsPerDay - playsUsed;

        return left > 0 ? left : 0;
    }

    /// <summary>
    /// Whether a house pays out more than it takes in, on average — the rule that keeps a game of
    /// chance a <b>sink</b> rather than a tap. <c>payout × chance ≥ stake × 100</c> is refused, so the
    /// authored expectation must be strictly negative for the player.
    ///
    /// ⚠️ <b>The stake this is asked about must be the CHEAPEST one on the standing ramp, not the
    /// authored number.</b> <c>ServiceComponent.PriceOf</c> runs every price through
    /// <see cref="ShopPricing.ServicePrice"/>, so an Allied player stakes 15% less against a payout
    /// that does not move — and a house that is a sink at Neutral can be a printer at Allied. This is
    /// <c>CommissionRules.Exploitable</c>'s trap exactly, one sub-phase later and in a place where the
    /// clamps genuinely cannot help: a wager is not a spread over anything.
    /// </summary>
    public static bool Exploitable(int stake, int winPercent, int payoutGold) =>
        (long)payoutGold * winPercent >= (long)stake * 100L;

}
