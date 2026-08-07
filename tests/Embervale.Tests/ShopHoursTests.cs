using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins a shop's trading window and a travelling merchant's visit cycle (Phase 38J). Both are the
/// shape that reads correct and runs wrong — a half-open window has two boundaries and only one of
/// them is obvious, a window that wraps past midnight inverts the comparison, and a modulo cycle can
/// be authored so that it never comes round at all. None of the three is visible from play without
/// waiting out an in-game day at three real minutes each.
/// </summary>
public class ShopHoursTests
{
    [Fact]
    public void EqualHoursMeanAlwaysOpen()
    {
        // The 0/0 default every shop authored before 38J carries. If this ever fails, the whole
        // existing realm shuts at once and the symptom is "shops stopped working".
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.True(ShopHours.IsOpenAt(hour, 0, 0), $"a 0/0 shop was shut at {hour}:00");
            Assert.True(ShopHours.IsOpenAt(hour, 9, 9));
        }

        Assert.Equal(24, ShopHours.OpenSpanHours(0, 0));
    }

    [Fact]
    public void TheWindowIsHalfOpenAtBothEnds()
    {
        // ⚠️ Open *at* the opening hour and shut *at* the closing hour. Inclusive closing would make
        // 18–18 mean both "always open" and "open for one hour", which no caller could tell apart.
        Assert.True(ShopHours.IsOpenAt(8, 8, 18));
        Assert.True(ShopHours.IsOpenAt(17, 8, 18));
        Assert.False(ShopHours.IsOpenAt(18, 8, 18));
        Assert.False(ShopHours.IsOpenAt(7, 8, 18));
        Assert.False(ShopHours.IsOpenAt(3, 8, 18));

        Assert.Equal(10, ShopHours.OpenSpanHours(8, 18));
    }

    [Fact]
    public void AWindowMayWrapPastMidnight()
    {
        // A night market: 20:00 through 04:00. The comparison inverts here, which is exactly the
        // off-by-one no amount of reading catches.
        Assert.True(ShopHours.IsOpenAt(20, 20, 4));
        Assert.True(ShopHours.IsOpenAt(23, 20, 4));
        Assert.True(ShopHours.IsOpenAt(0, 20, 4));
        Assert.True(ShopHours.IsOpenAt(3, 20, 4));
        Assert.False(ShopHours.IsOpenAt(4, 20, 4));
        Assert.False(ShopHours.IsOpenAt(12, 20, 4));

        Assert.Equal(8, ShopHours.OpenSpanHours(20, 4));
    }

    [Fact]
    public void EveryHourIsEitherOpenOrShutAndTheSpanCountsThem()
    {
        // The span and the window are two answers to one question, so they must agree for every shape
        // — including the wrapping ones, where the subtraction changes form.
        foreach ((int open, int close) in new[] { (8, 18), (20, 4), (0, 1), (23, 22), (6, 20) })
        {
            int openHours = 0;
            for (int hour = 0; hour < 24; hour++)
            {
                if (ShopHours.IsOpenAt(hour, open, close))
                {
                    openHours++;
                }
            }

            Assert.Equal(ShopHours.OpenSpanHours(open, close), openHours);
        }
    }

    [Fact]
    public void AClosedShopSaysWhenItOpens()
    {
        Assert.Equal(8, ShopHours.NextOpenHour(3, 8, 18));
        Assert.Equal(8, ShopHours.NextOpenHour(22, 8, 18));

        // Asked while open, the answer is now: "come back at" has nothing to say to someone standing
        // at an open stall.
        Assert.Equal(12, ShopHours.NextOpenHour(12, 8, 18));
        Assert.Equal(5, ShopHours.NextOpenHour(5, 0, 0));
    }

    [Fact]
    public void AResidentMerchantIsAlwaysInTown()
    {
        for (int day = 0; day < 30; day++)
        {
            Assert.True(ShopHours.IsInTown(day, everyDays: 0, offset: 0), $"resident absent on day {day}");
        }
    }

    [Fact]
    public void ATravellerIsInTownExactlyOneDayInTheCycle()
    {
        const int cycle = 4;

        for (int offset = 0; offset < cycle; offset++)
        {
            int visits = 0;
            for (int day = 0; day < cycle * 5; day++)
            {
                if (ShopHours.IsInTown(day, cycle, offset))
                {
                    visits++;
                    Assert.Equal(offset, day % cycle);
                }
            }

            // ⚠️ Five turns of the cycle, five visits — the failure this catches is an offset that
            // never comes round, which in game is a merchant nobody ever meets and no log line at all.
            Assert.Equal(5, visits);
        }
    }

    [Fact]
    public void ATravellerSurvivesADayThatWentBackwards()
    {
        // WorldClock.Day is restored from a save, and a quickload can rewind it. A modulo over a
        // negative day must not answer "never" for the rest of the run — the same class of bug
        // ShopStock.IsRestockDue had to widen to long for.
        Assert.True(ShopHours.IsInTown(-4, everyDays: 4, offset: 0));
        Assert.True(ShopHours.IsInTown(-3, everyDays: 4, offset: 1));
        Assert.False(ShopHours.IsInTown(-3, everyDays: 4, offset: 2));
    }

    [Fact]
    public void TheNextVisitIsTodayWhenHeIsAlreadyHere()
    {
        Assert.Equal(8, ShopHours.NextVisitDay(8, everyDays: 4, offset: 0));
        Assert.Equal(12, ShopHours.NextVisitDay(9, everyDays: 4, offset: 0));
        Assert.Equal(11, ShopHours.NextVisitDay(9, everyDays: 4, offset: 3));

        // A resident has no next visit but must still answer with a real day rather than a sentinel a
        // caller would print.
        Assert.Equal(9, ShopHours.NextVisitDay(9, everyDays: 0, offset: 0));
    }

    [Fact]
    public void TheAuthoringBoundsAreTheOnesTheValidatorEnforces()
    {
        // The constants are a design decision (a shop open under 4h a day, a merchant away more than a
        // week) and the validator's messages quote them. Pinning them here means changing one is a
        // deliberate act rather than a silent loosening.
        Assert.Equal(4, ShopHours.MinimumOpenSpan);
        Assert.Equal(7, ShopHours.MaxVisitGap);
    }
}
