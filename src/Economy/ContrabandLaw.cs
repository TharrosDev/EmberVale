using System;

namespace Embervale.Economy;

/// <summary>
/// What the wardens charge to give confiscated goods back (Phase 38O). Pure and Godot-free for the
/// reason <see cref="ShopStock"/> and <c>ScheduleMath</c> are: the test project throws an
/// <c>AccessViolationException</c> constructing any Godot object, so the arithmetic that decides a
/// player-facing number lives where it can be pinned by a test and the node does the plumbing.
///
/// <b>There is no separate confiscation rule here, deliberately.</b> What a search takes is exactly
/// what <see cref="TradeTags.IsContraband"/> says, and that already has its own tests — a second
/// predicate would be a second authority on the same question, which is what
/// <see cref="ShopPricing"/>'s "there is exactly one price authority and this is not it" is about.
/// </summary>
public static class ContrabandLaw
{
    /// <summary>
    /// The fine for the whole impound: a per-unit charge on the service, times what is held.
    ///
    /// <b>Linear, and that is a choice 38U has to be able to explain on a hover.</b> A scaling or
    /// tapering fine would be a second curve nobody could read off the prompt, and the number the
    /// player is being asked to accept is the whole mechanic — the wardens' cut is per item, the way a
    /// duty is.
    ///
    /// Floors at <c>1</c> for anything held, the same rule and the same reason as
    /// <see cref="ShopPricing.BuyPrice"/>: a nil fine on a non-empty impound is a free unconfiscation,
    /// which makes the search a mild inconvenience rather than a cost. Returns <c>0</c> for an empty
    /// impound, which is the "there is nothing of yours here" case and not a free redemption —
    /// <c>ServiceComponent</c> refuses that before the price is ever read.
    ///
    /// ⚠️ Saturates rather than overflowing. A 32-bit wrap on a very large impound would hand the
    /// player a <em>negative</em> fine, which <see cref="ShopPricing.ServicePrice"/> would floor to
    /// free — the one arithmetic path here that could pay the player to be caught.
    /// </summary>
    public static int Fine(int perUnit, int units)
    {
        if (units <= 0)
        {
            return 0;
        }

        long total = (long)Math.Max(0, perUnit) * units;
        return (int)Math.Clamp(total, 1, int.MaxValue);
    }
}
