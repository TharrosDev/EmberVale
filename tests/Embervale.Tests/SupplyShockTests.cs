using System.Collections.Generic;
using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Supply shocks (Phase 38T) — the timed half of 38G's demand table. Godot-free, like the type under
/// test: the service that stores the windows is a Node and cannot be constructed here, so what is
/// pinned is the roll, the tag arithmetic and the band the validator prices against.
/// </summary>
public class SupplyShockTests
{
    private static readonly List<string> Ore = new() { "ore" };
    private static readonly List<string> None = new();

    private static readonly List<string> MineSurplus = new() { "ore", "metal", "fuel" };
    private static readonly List<string> MineDemand = new() { "food", "fish", "textile" };
    private static readonly List<string> MineCandidates = new() { "ore", "food" };

    [Fact]
    public void ActiveOn_CoversItsWholeWindowAndNotTheDayAfter()
    {
        var shock = new SupplyShock("c", "ore", ShockKind.Shortage, StartDay: 10, Days: 3);

        Assert.False(shock.ActiveOn(9));
        Assert.True(shock.ActiveOn(10));
        Assert.True(shock.ActiveOn(12));
        Assert.False(shock.ActiveOn(13));
    }

    /// <summary>The last day of a shock says "1 more day", never "0" — the caption and the board both
    /// print this number straight.</summary>
    [Fact]
    public void DaysLeft_IsOneOnTheLastDay()
    {
        var shock = new SupplyShock("c", "ore", ShockKind.Shortage, StartDay: 10, Days: 3);

        Assert.Equal(3, shock.DaysLeft(10));
        Assert.Equal(1, shock.DaysLeft(12));
        Assert.Equal(0, shock.DaysLeft(13));
    }

    /// <summary>⚠️ The property the whole design rests on: the roll is a pure function of (day, cell), so
    /// a quickload replays it rather than rerolling it. Same reason
    /// <c>HaggleRulesTests.Succeeds_IsStableAcrossProcesses</c> pins a hard-coded string — a
    /// self-consistent test would let a refactor change every result silently.</summary>
    [Fact]
    public void Begins_IsStableAcrossProcesses()
    {
        var days = new System.Text.StringBuilder();
        for (int day = 0; day < 20; day++)
        {
            days.Append(SupplyShockRules.Begins(day, "ember_crown.emberdeep_mine") ? '1' : '0');
        }

        Assert.Equal("00001000100000000000", days.ToString());
    }

    [Fact]
    public void Duration_StaysInsideItsBounds()
    {
        for (int day = 0; day < 200; day++)
        {
            int days = SupplyShockRules.Duration(day, "ember_crown.tarn_landing");
            Assert.InRange(days, SupplyShockRules.MinDays, SupplyShockRules.MaxDays);
        }
    }

    /// <summary>⚠️ The rule an authored candidate list can break: a shock must invert what the cell
    /// already says, or it is a notice that nothing has happened.</summary>
    [Fact]
    public void Roll_NeverPicksATagTheCellAlreadyTreatsThatWay()
    {
        for (int day = 0; day < 500; day++)
        {
            if (SupplyShockRules.Roll(day, "ember_crown.emberdeep_mine", MineCandidates, MineSurplus, MineDemand)
                is not { } shock)
            {
                continue;
            }

            if (shock.Kind == ShockKind.Shortage)
            {
                Assert.DoesNotContain(shock.Tag, MineDemand);
            }
            else if (shock.Kind == ShockKind.Glut)
            {
                Assert.DoesNotContain(shock.Tag, MineSurplus);
            }
        }
    }

    [Fact]
    public void Roll_IsNullWithNoCandidates()
    {
        for (int day = 0; day < 50; day++)
        {
            Assert.Null(SupplyShockRules.Roll(day, "c", None, None, None));
        }
    }

    /// <summary>A shortage takes its tag out of the surplus list on the way in. Left in both,
    /// <see cref="RegionDemand.ValueAt"/> resolves it as a surplus and the shock silently does
    /// nothing — which is indistinguishable from the feature being off.</summary>
    [Fact]
    public void Apply_ShortageMovesTheTagRatherThanCopyingIt()
    {
        var shock = new SupplyShock("mine", "ore", ShockKind.Shortage, 5, 3);

        (List<string> surplus, List<string> demand) = SupplyShockRules.Apply(
            MineSurplus, MineDemand, MineCandidates, new List<SupplyShock> { shock }, day: 5);

        Assert.DoesNotContain("ore", surplus);
        Assert.Contains("ore", demand);
        Assert.True(RegionDemand.ValueAt(100, Ore, surplus, demand) > 100);
    }

    [Fact]
    public void Apply_GlutMovesTheTagTheOtherWay()
    {
        var shock = new SupplyShock("mine", "food", ShockKind.Glut, 5, 3);
        var food = new List<string> { "food" };

        (List<string> surplus, List<string> demand) = SupplyShockRules.Apply(
            MineSurplus, MineDemand, MineCandidates, new List<SupplyShock> { shock }, day: 5);

        Assert.Contains("food", surplus);
        Assert.DoesNotContain("food", demand);
        Assert.True(RegionDemand.ValueAt(100, food, surplus, demand) < 100);
    }

    [Fact]
    public void Apply_FairFloodsEveryCandidate()
    {
        var shock = new SupplyShock("mine", string.Empty, ShockKind.Fair, 5, 3);

        (List<string> surplus, List<string> demand) = SupplyShockRules.Apply(
            MineSurplus, MineDemand, MineCandidates, new List<SupplyShock> { shock }, day: 5);

        Assert.Contains("ore", surplus);
        Assert.Contains("food", surplus);
        Assert.DoesNotContain("food", demand);
    }

    [Fact]
    public void Apply_IgnoresAShockThatIsNotRunningToday()
    {
        var shock = new SupplyShock("mine", "ore", ShockKind.Shortage, 5, 3);

        (List<string> surplus, List<string> demand) = SupplyShockRules.Apply(
            MineSurplus, MineDemand, MineCandidates, new List<SupplyShock> { shock }, day: 99);

        Assert.Equal(MineSurplus, surplus);
        Assert.Equal(MineDemand, demand);
    }

    /// <summary>Hauling goods into a shortage is the one thing a player can do about a shock, and the
    /// twelfth unit is the one that ends it.</summary>
    [Fact]
    public void Relieves_CountsTowardTheThresholdAndBreaksOnIt()
    {
        var shock = new SupplyShock("mine", "ore", ShockKind.Shortage, 5, 3);

        Assert.True(SupplyShockRules.Relieves(shock, Ore, hauled: 0, units: 5, out bool breaks));
        Assert.False(breaks);

        Assert.True(SupplyShockRules.Relieves(
            shock, Ore, hauled: SupplyShockRules.ReliefUnits - 1, units: 1, out breaks));
        Assert.True(breaks);
    }

    /// <summary>⚠️ A glut and a fair are not problems to be fixed — relieving one would pay the player
    /// twice for the same cart, once at the counter and once for "breaking" the price they sold into.
    /// A tag the shock is not about does not count either.</summary>
    [Fact]
    public void Relieves_RefusesAGlutAFairAndTheWrongGood()
    {
        var glut = new SupplyShock("mine", "ore", ShockKind.Glut, 5, 3);
        var fair = new SupplyShock("mine", string.Empty, ShockKind.Fair, 5, 3);
        var shortage = new SupplyShock("mine", "ore", ShockKind.Shortage, 5, 3);

        Assert.False(SupplyShockRules.Relieves(glut, Ore, 0, 99, out _));
        Assert.False(SupplyShockRules.Relieves(fair, Ore, 0, 99, out _));
        Assert.False(SupplyShockRules.Relieves(shortage, new List<string> { "fish" }, 0, 99, out _));
        Assert.False(SupplyShockRules.Relieves(shortage, Ore, 0, 0, out _));
    }

    /// <summary>⚠️ The band <c>ContentValidator</c> proves the contract and commission rules against is
    /// deliberately wider than any day the game can roll — one shock runs at a cell at a time.</summary>
    [Fact]
    public void Extremes_BracketEveryShockTheGameCanRoll()
    {
        (List<string> peakSurplus, List<string> peakDemand) = SupplyShockRules.Extremes(
            MineSurplus, MineDemand, MineCandidates, PriceView.Peak);
        (List<string> lowSurplus, List<string> lowDemand) = SupplyShockRules.Extremes(
            MineSurplus, MineDemand, MineCandidates, PriceView.Trough);

        int peak = RegionDemand.ValueAt(100, Ore, peakSurplus, peakDemand);
        int trough = RegionDemand.ValueAt(100, Ore, lowSurplus, lowDemand);

        Assert.True(peak > trough);

        for (int day = 0; day < 500; day++)
        {
            if (SupplyShockRules.Roll(day, "ember_crown.emberdeep_mine", MineCandidates, MineSurplus, MineDemand)
                is not { } shock)
            {
                continue;
            }

            (List<string> surplus, List<string> demand) = SupplyShockRules.Apply(
                MineSurplus, MineDemand, MineCandidates, new List<SupplyShock> { shock }, shock.StartDay);

            int value = RegionDemand.ValueAt(100, Ore, surplus, demand);
            Assert.InRange(value, trough, peak);
        }
    }
}
