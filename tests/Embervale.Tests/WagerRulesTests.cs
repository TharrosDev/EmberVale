using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The gambling house's arithmetic (Phase 38R2). This is the whole of the feature that can be tested:
/// the ledger is a Godot node and the press needs a human, so the guarantees the design rests on —
/// derived, stable, and a losing proposition — are pinned here or nowhere.
/// </summary>
public class WagerRulesTests
{
    private const string House = "service.hollowreach.bones";

    /// <summary>The property the entire design exists for: a reload replays a throw, never rerolls it.
    /// If this ever fails, a quickload is a re-roll and the daily allowance stops meaning anything.</summary>
    [Fact]
    public void Won_IsDeterministic()
    {
        for (int day = 0; day < 50; day++)
        {
            for (int play = 0; play < 3; play++)
            {
                Assert.Equal(
                    WagerRules.Won(day, play, House, 30),
                    WagerRules.Won(day, play, House, 30));
            }
        }
    }

    /// <summary>⚠️ The reason <c>HouseSeed</c> is a hand-written FNV-1a and not
    /// <c>string.GetHashCode()</c>: .NET randomises string hashing per process, so this literal would
    /// drift between runs and the same day would pay differently after a restart. A hard-coded
    /// expectation is what catches that — a self-consistent test never would.</summary>
    [Fact]
    public void Won_IsStableAcrossProcesses()
    {
        Assert.Equal(
            "LLLLLLWWLL",
            Sequence(day: 7, plays: 10, winPercent: 30));
    }

    /// <summary>Two houses must not share a day's luck, or a player who loses at one walks to the other
    /// already knowing the answer.</summary>
    [Fact]
    public void Won_DiffersBetweenHouses()
    {
        string a = Sequence(day: 3, plays: 24, winPercent: 50, house: "service.a");
        string b = Sequence(day: 3, plays: 24, winPercent: 50, house: "service.b");

        Assert.NotEqual(a, b);
    }

    /// <summary>Consecutive throws in one day must differ too — a day that is all wins or all losses is
    /// the "one throw repeated" bug, and it would look like a losing streak rather than a fault.</summary>
    [Fact]
    public void Won_VariesWithinADay()
    {
        string day = Sequence(day: 11, plays: 24, winPercent: 50);

        Assert.Contains('W', day);
        Assert.Contains('L', day);
    }

    /// <summary>Not a distribution proof — a sanity band. An authored 30% that actually paid 70% would
    /// invert the sink rule while every other test still passed.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(75)]
    public void Won_RoughlyMatchesTheAuthoredChance(int winPercent)
    {
        int wins = 0;
        const int Trials = 4000;
        for (int i = 0; i < Trials; i++)
        {
            if (WagerRules.Won(i / 3, i % 3, House, winPercent))
            {
                wins++;
            }
        }

        double rate = wins * 100.0 / Trials;
        Assert.InRange(rate, winPercent - 4, winPercent + 4);
    }

    [Fact]
    public void Won_HonoursTheDegenerateChances()
    {
        Assert.False(WagerRules.Won(1, 0, House, 0));
        Assert.False(WagerRules.Won(1, 0, House, -5));
        Assert.True(WagerRules.Won(1, 0, House, 100));
    }

    [Fact]
    public void PlaysLeft_CountsDownAndFloorsAtZero()
    {
        Assert.Equal(3, WagerRules.PlaysLeft(playsUsed: 0, playsPerDay: 3));
        Assert.Equal(1, WagerRules.PlaysLeft(playsUsed: 2, playsPerDay: 3));
        Assert.Equal(0, WagerRules.PlaysLeft(playsUsed: 3, playsPerDay: 3));

        // A PlaysPerDay lowered between saves must not hand back a negative count for a day already
        // played out — the prompt would read "-2 throws left" and the already-held test would let the
        // player keep throwing.
        Assert.Equal(0, WagerRules.PlaysLeft(playsUsed: 5, playsPerDay: 3));
    }

    /// <summary>The rule that keeps a game of chance a sink. Break-even is exploitable too: a table the
    /// player cannot lose money at over time is not a sink, it is a slow tap.</summary>
    [Fact]
    public void Exploitable_RefusesANonNegativeExpectation()
    {
        Assert.False(WagerRules.Exploitable(stake: 50, winPercent: 30, payoutGold: 150));
        Assert.True(WagerRules.Exploitable(stake: 50, winPercent: 30, payoutGold: 170));
        Assert.True(WagerRules.Exploitable(stake: 50, winPercent: 50, payoutGold: 100));
    }

    /// <summary>⚠️ The case the validator actually asks about, and the one a reader would miss: the
    /// stake is discounted by standing and the payout is not, so a table that is a sink at Neutral can
    /// be a printer at Allied. 38Q's <c>CommissionRules</c> trap, in a place with no clamps behind it.</summary>
    [Fact]
    public void Exploitable_CatchesAHouseThatOnlyTipsAtBestStanding()
    {
        const int Authored = 100;
        const int Payout = 300;
        const int Chance = 32;

        Assert.False(WagerRules.Exploitable(Authored, Chance, Payout));

        // 15% off the stake at Allied (ShopPricing.PriceMultiplierFor), payout unmoved.
        Assert.True(WagerRules.Exploitable(stake: 85, winPercent: Chance, payoutGold: Payout));
    }

    private static string Sequence(int day, int plays, int winPercent, string house = House)
    {
        var text = new System.Text.StringBuilder(plays);
        for (int i = 0; i < plays; i++)
        {
            text.Append(WagerRules.Won(day, i, house, winPercent) ? 'W' : 'L');
        }

        return text.ToString();
    }
}
