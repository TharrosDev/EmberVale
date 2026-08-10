namespace Embervale.Economy;

/// <summary>
/// The one derived-outcome generator the economy uses (Phase 38S) — lifted out of
/// <see cref="WagerRules"/> unchanged when <see cref="HaggleRules"/> became its second caller.
///
/// ⚠️ <b>NOT <c>System.Random</c>, NOT an engine RNG, and above all NOT
/// <c>string.GetHashCode()</c>.</b> .NET randomises string hashing per process, so an id folded in
/// that way gives a different answer after a restart — which is precisely the property both callers
/// exist to prevent, and it would look exactly like an RNG working correctly. The hash below is an
/// explicit FNV-1a and the finalizer is splitmix32, both written out so they are the same tomorrow,
/// on another machine and in another build.
///
/// The result being a pure function of (day, salt, id) is what makes a quickload <b>replay</b> an
/// outcome rather than reroll it. What stops the day being farmed is never this — it is a ledger
/// counting attempts, which is a separate mechanism and does not substitute for this one.
/// </summary>
public static class StableRoll
{
    /// <summary>A stable <c>0..99</c> for the given day, salt and id.</summary>
    public static uint Percent(int day, int salt, string id) =>
        Mix((uint)day * 0x9E3779B9u ^ Mix((uint)salt + 0x85EBCA6Bu) ^ Seed(id)) % 100u;

    /// <summary>A stable 32-bit hash of an id — FNV-1a, for the reason in the type summary.</summary>
    public static uint Seed(string id)
    {
        uint hash = 2166136261u;
        if (string.IsNullOrEmpty(id))
        {
            return hash;
        }

        foreach (char c in id)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>The splitmix32 finalizer <c>ContractRules</c> uses, for its reason: deterministic
    /// across machines and runs, which an engine RNG is not.</summary>
    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}
