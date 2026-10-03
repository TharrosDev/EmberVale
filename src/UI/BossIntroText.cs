using System;

namespace Embervale.UI;

/// <summary>Picks the boss frame's epithet and intro line from the boss data, falling back to the generic
/// "bars your path" line when the boss authors nothing. Godot-free.</summary>
public static class BossIntroText
{
    public const string FallbackIntroKey = "boss.intro";

    /// <summary>The epithet key to show under the name, or null when none is authored or it does not resolve.</summary>
    public static string? Epithet(string epithetKey, Func<string, bool> resolves) =>
        epithetKey.Length > 0 && resolves(epithetKey) ? epithetKey : null;

    /// <summary>The key of the intro line. <paramref name="usesName"/> is true for the generic fallback, whose
    /// text formats the boss's name in.</summary>
    public static string IntroKey(string introLineKey, Func<string, bool> resolves, out bool usesName)
    {
        if (introLineKey.Length > 0 && resolves(introLineKey))
        {
            usesName = false;
            return introLineKey;
        }

        usesName = true;
        return FallbackIntroKey;
    }
}
