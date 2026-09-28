using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// Which weather states the world may roll, given the story (Phase 44.5, the world answering the
/// ending). Pure so the rule is unit-testable apart from <see cref="WeatherDirector"/>: a state with a
/// <c>RequiredFlagId</c> exists only once that flag is set, and a state listing
/// <c>ExcludedByFlagIds</c> is gone once any of them is. The ending flags are the only readers today:
/// Dawnfire clears the storms away and adds a warm clear sky; the Lord of Embers leaves one ember sky.
/// Nothing here is saved — it is derived from story flags, which already persist.
/// </summary>
public static class WeatherEligibility
{
    public static bool IsEligible(string? requiredFlagId, IEnumerable<string>? excludedByFlagIds, Func<string, bool> has)
    {
        if (!string.IsNullOrEmpty(requiredFlagId) && !has(requiredFlagId))
        {
            return false;
        }

        if (excludedByFlagIds != null)
        {
            foreach (string flag in excludedByFlagIds)
            {
                if (!string.IsNullOrEmpty(flag) && has(flag))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
