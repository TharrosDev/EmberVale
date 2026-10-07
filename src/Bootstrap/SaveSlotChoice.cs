using System.Collections.Generic;

namespace Embervale.Bootstrap;

/// <summary>
/// Which save a command-line session continues. Pure, so the rule is unit-tested.
///
/// <para><c>--slot=&lt;name&gt;</c> names the save and nothing else will do: a name that matches no
/// save is a failed run, because continuing a different save runs the caller's script or capture
/// against a world it did not ask for. The older <c>EMBERVALE_SLOT</c> variable keeps its looser
/// rule (fall back to the newest save, with a warning). With neither, the newest save.</para>
/// </summary>
public static class SaveSlotChoice
{
    /// <param name="slots">Every save, with its timestamp.</param>
    /// <param name="argument">The value of <c>--slot</c>, or null/empty.</param>
    /// <param name="environment">The value of <c>EMBERVALE_SLOT</c>, or null/empty.</param>
    /// <param name="problem">Why no slot is returned although one was asked for.</param>
    /// <param name="fellBack">True when the variable named no save and the newest was taken.</param>
    /// <returns>The slot to continue, or null when there is none to continue.</returns>
    public static string? Pick(
        IEnumerable<KeyValuePair<string, double>> slots, string? argument, string? environment,
        out string? problem, out bool fellBack)
    {
        problem = null;
        fellBack = false;
        bool strict = !string.IsNullOrEmpty(argument);
        string? wanted = strict ? argument : string.IsNullOrEmpty(environment) ? null : environment;

        string? latest = null;
        double latestTime = double.NegativeInfinity;
        var names = new List<string>();
        foreach (KeyValuePair<string, double> slot in slots)
        {
            if (wanted != null && slot.Key == wanted)
            {
                return wanted;
            }

            names.Add(slot.Key);
            if (latest == null || slot.Value > latestTime)
            {
                latest = slot.Key;
                latestTime = slot.Value;
            }
        }

        if (strict)
        {
            names.Sort(System.StringComparer.Ordinal);
            problem = $"--slot={wanted} names no save" +
                      (names.Count > 0 ? $" (saves: {string.Join(", ", names)})" : " (there are no saves)");
            return null;
        }

        fellBack = wanted != null && latest != null;
        return latest;
    }
}
