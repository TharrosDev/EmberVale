using System;
using System.Collections.Generic;
using Embervale.Progression;

namespace Embervale.Bootstrap;

/// <summary>
/// What the perk probes compare a live <see cref="PerksComponent"/> against: the same total worked out
/// independently from the authored perk data (every effect at full rank, the qualifier rule, the cap). The
/// catalogue is retuned by editing <c>tools/gen_perks.py</c>, so a probe must not hard-code a perk's number.
/// </summary>
internal static class PerkProbeMath
{
    /// <summary>The capped total of <paramref name="kind"/> that holding every perk in <paramref name="ids"/> at full
    /// rank gives. An effect with no qualifier counts for every <paramref name="arg"/>.</summary>
    public static float Expected(IEnumerable<string> ids, PerkEffectKind kind, string? arg = null)
    {
        float sum = 0f;
        foreach (string id in ids)
        {
            if (PerkDatabase.Get(id) is not { } perk)
            {
                continue;
            }

            foreach (PerkEffectEntry entry in perk.EffectEntries())
            {
                if (entry.Kind == kind && (entry.Arg.Length == 0 || entry.Arg == arg))
                {
                    sum += entry.ValuePerRank * perk.MaxRank;
                }
            }
        }

        return PerkEffectMath.Clamp(kind, sum);
    }

    /// <summary>Checks the live total equals <see cref="Expected"/> and that it is not vacuously zero.</summary>
    public static void CheckTotal(
        Entities.IEntity player, IReadOnlyCollection<string> ids, PerkEffectKind kind, string? arg,
        string label, Action<bool, string> check)
    {
        float expected = Expected(ids, kind, arg);
        float live = PerkQuery.Of(player, kind, arg);
        check(MathF.Abs(expected) > 0.0001f && MathF.Abs(live - expected) < 0.001f,
            $"{label}: {kind}{(string.IsNullOrEmpty(arg) ? "" : "/" + arg)} is {live:0.####}, expected {expected:0.####} from the authored perks.");
    }
}
