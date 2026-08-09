using System;

namespace Embervale.Economy;

/// <summary>
/// Which supply contracts the caravan board is showing today, and for how much longer (Phase 38Q2).
/// Pure and Godot-free for the reason <see cref="ShopStock"/>, <see cref="ContrabandLaw"/> and
/// <see cref="CommissionRules"/> are: the test project throws an <c>AccessViolationException</c>
/// constructing any Godot object, and <c>ContractResource</c> is one — so the pool arrives here as a
/// size and the caller does the resolving.
///
/// ⚠️ <b>THE BOARD IS DERIVED FROM THE DAY, NEVER STORED, AND THAT IS THE WHOLE TRICK.</b> The day is
/// the only input to <see cref="SlotContract"/>, so a quickload cannot reroll the postings: the same
/// day always yields the same board, on any machine, in any save. That is 38S's stated rule — "bound
/// to the day so a reload cannot reroll it" — arriving a sub-phase early and for free, and it is why
/// <c>ContractLedger</c> records only what has been <em>filled</em> and never what is <em>offered</em>.
/// A rotation rolled through an RNG would have needed saving, and a saved rotation is a second thing
/// that can disagree with the clock.
///
/// <b>There is no deadline state either.</b> The rotation <em>is</em> the deadline: a posting is up
/// until the board turns and then it is gone. Nothing accepts, lapses or fails, so none of that has to
/// persist — see the 38Q2 entry for why that reading was taken.
/// </summary>
public static class ContractRules
{
    /// <summary>
    /// Which rotation a day falls in. <paramref name="periodDays"/> of zero or less is a board that
    /// never turns, which is a legal (if dull) authoring choice and always cycle <c>0</c>.
    ///
    /// Floors rather than truncates, so a clock that has gone backwards past day zero still steps down
    /// one cycle at a time instead of folding two cycles onto one — the same "a rewound clock must
    /// behave, not freeze" case <see cref="ShopStock.IsRestockDue"/> exists for.
    /// </summary>
    public static int Cycle(int day, int periodDays)
    {
        if (periodDays <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(day / (double)periodDays);
    }

    /// <summary>
    /// Days until the board turns over: <c>1</c> on the last day of a rotation, never <c>0</c>, so the
    /// footer can always say "changes in N days" without a special case for the final day.
    /// </summary>
    public static int DaysLeft(int day, int periodDays)
    {
        if (periodDays <= 0)
        {
            return 0; // a board that never turns has nothing to count down to
        }

        return periodDays - (day - (Cycle(day, periodDays) * periodDays));
    }

    /// <summary>
    /// The pool index shown on <paramref name="slot"/> during <paramref name="cycle"/>, or <c>-1</c>
    /// when there is nothing to show.
    ///
    /// ⚠️ <b>Distinct by construction, not by retrying.</b> The stride is forced coprime with the pool,
    /// so <c>start + slot * stride</c> walks every pool index exactly once before repeating — two slots
    /// in the same rotation can therefore never land on the same posting, for any pool size. A
    /// reject-and-resample version would have been shorter and would have failed silently on a pool
    /// barely larger than the board, which is exactly the case a small realm has.
    /// (<c>--validate</c> also insists the pool is bigger than the board, so a full rotation is always
    /// distinct rather than merely non-repeating.)
    /// </summary>
    public static int SlotContract(int cycle, int slot, int poolSize)
    {
        if (poolSize <= 0 || slot < 0)
        {
            return -1;
        }

        if (poolSize == 1)
        {
            return 0;
        }

        int start = (int)(Mix((uint)cycle) % (uint)poolSize);
        int stride = 1 + (int)(Mix((uint)cycle ^ 0x9E3779B9u) % (uint)(poolSize - 1));

        while (Gcd(stride, poolSize) != 1)
        {
            stride = stride % (poolSize - 1) + 1;
        }

        return (int)(((long)start + (long)slot * stride) % poolSize);
    }

    /// <summary>A fixed integer scramble (the splitmix32 finalizer). Deterministic across machines and
    /// runs, which an engine RNG is not — that is the entire requirement here.</summary>
    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
