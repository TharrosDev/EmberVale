using System;

namespace Embervale.UI;

/// <summary>
/// The character screen's tabs as one run for the sub-tab step (Z/C, LT/RT). The screen has two
/// levels of tab, the strip across the top (Gear, Progression, Perks, Guilds) and, inside Gear, pack
/// or materials, and one pair of buttons to walk them with. Flattened, the stops are: pack,
/// materials, then each remaining top tab in order. Pure, so the wrap and the mapping are tested.
/// </summary>
public static class InventoryTabRules
{
    /// <summary>The stop a tab and view stand on. Tab 0 is Gear, which is two stops.</summary>
    public static int StopOf(int tab, bool materials) => tab <= 0 ? (materials ? 1 : 0) : tab + 1;

    /// <summary>The tab and view a stop is.</summary>
    public static (int Tab, bool Materials) FromStop(int stop) => stop <= 1 ? (0, stop == 1) : (stop - 1, false);

    /// <summary>The stop <paramref name="delta"/> steps from <paramref name="stop"/>, wrapping at both
    /// ends. <paramref name="tabCount"/> is the number of top tabs; there is one more stop than that.</summary>
    public static int Step(int stop, int delta, int tabCount)
    {
        int stops = Math.Max(1, tabCount) + 1;
        return (((stop + delta) % stops) + stops) % stops;
    }
}
