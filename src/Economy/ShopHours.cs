using System;

namespace Embervale.Economy;

/// <summary>
/// When a shop trades, and which days its merchant is in town (Phase 38J). The third Godot-free file
/// in <c>src/Economy</c>, beside <see cref="ShopPricing"/> (money) and <see cref="ShopStock"/>
/// (restock, purse, appetite). Clock arithmetic is neither of those, and it is exactly the shape that
/// reads correct and runs wrong: a window that wraps past midnight and a modulo visit cycle are both
/// off-by-one traps that only a test catches.
/// </summary>
public static class ShopHours
{
    /// <summary>The fewest hours a day a shop may trade and still be a shop. Below this the merchant
    /// is open for a window a player has to plan a day around, which reads as the shop being broken
    /// rather than as a schedule; <c>--validate</c> rejects it.</summary>
    public const int MinimumOpenSpan = 4;

    /// <summary>The longest a travelling merchant may be away between visits. A trader the player waits
    /// more than a week of in-game days for is a wall rather than a cadence — and an in-game day is
    /// <c>WorldClock.DayLengthSeconds</c> of real time, so the wall is a real one.</summary>
    public const int MaxVisitGap = 7;

    /// <summary>
    /// Whether a shop with this window is trading at <paramref name="hour"/>.
    ///
    /// <b>Equal hours mean always open</b>, which is the <c>0</c>/<c>0</c> default every shop authored
    /// before 38J carries — so the field arrives without changing a single existing merchant, the same
    /// inverted default 38F's tags and 38I's gates both use. There is no separate "has hours" flag to
    /// disagree with the numbers.
    ///
    /// ⚠️ The window is <b>half-open</b>: a shop opening at 8 and closing at 18 is open at 8 and shut at
    /// 18. Closing hour inclusive would make 18–18 both "always open" and "open one hour".
    ///
    /// A window that wraps past midnight (<c>20</c>–<c>4</c>) is supported because it costs one
    /// comparison and a night market is a thing the Embermarket will want.
    /// </summary>
    public static bool IsOpenAt(int hour, int openHour, int closeHour)
    {
        if (openHour == closeHour)
        {
            return true;
        }

        int h = Wrap(hour);
        int open = Wrap(openHour);
        int close = Wrap(closeHour);

        return open < close
            ? h >= open && h < close
            : h >= open || h < close;   // wraps past midnight
    }

    /// <summary>How many hours a day this window covers; <c>24</c> for the always-open default.</summary>
    public static int OpenSpanHours(int openHour, int closeHour)
    {
        if (openHour == closeHour)
        {
            return 24;
        }

        int open = Wrap(openHour);
        int close = Wrap(closeHour);
        return open < close ? close - open : 24 - open + close;
    }

    /// <summary>
    /// The hour the shop next opens, for a refusal that tells the player when to come back. Returns
    /// <paramref name="openHour"/> normalised; the value is what a closed shop says, so it is worth one
    /// function rather than a literal at two call sites.
    ///
    /// An always-open shop answers with the current hour: it is never shut, so "come back at" is now.
    /// </summary>
    public static int NextOpenHour(int hour, int openHour, int closeHour) =>
        IsOpenAt(hour, openHour, closeHour) ? Wrap(hour) : Wrap(openHour);

    /// <summary>
    /// Whether a travelling merchant is in town on <paramref name="day"/>.
    ///
    /// <c>everyDays &lt;= 0</c> is a <b>resident</b> merchant — always here, and the default, so the
    /// field arrives inert. Otherwise the merchant is present on the days where
    /// <c>day % everyDays == offset</c>.
    ///
    /// ⚠️ The modulo is written to survive a negative day. Nothing in the game produces one today, but
    /// <c>WorldClock.Day</c> is restored from a save and a clamp is cheaper than the bug.
    /// </summary>
    public static bool IsInTown(int day, int everyDays, int offset)
    {
        if (everyDays <= 0)
        {
            return true;
        }

        int cycle = ((day % everyDays) + everyDays) % everyDays;
        return cycle == ((offset % everyDays) + everyDays) % everyDays;
    }

    /// <summary>
    /// The next day this merchant is in town, so a prompt can say when rather than only that he is
    /// gone. Answers <paramref name="day"/> itself when he is already here.
    /// </summary>
    public static int NextVisitDay(int day, int everyDays, int offset)
    {
        if (everyDays <= 0 || IsInTown(day, everyDays, offset))
        {
            return day;
        }

        for (int ahead = 1; ahead <= everyDays; ahead++)
        {
            if (IsInTown(day + ahead, everyDays, offset))
            {
                return day + ahead;
            }
        }

        return day; // unreachable for everyDays >= 1; a loop that cannot fall through still must not lie
    }

    private static int Wrap(int hour) => ((hour % 24) + 24) % 24;
}
