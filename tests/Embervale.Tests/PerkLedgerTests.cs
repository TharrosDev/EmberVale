using System.Collections.Generic;
using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>The pure half of <c>PerksComponent</c>: bookkeeping, respec and the save restore. The
/// component's <c>Load</c> only parses Godot variants and hands them to <see cref="PerkLedger.Restore"/>,
/// so replace-not-merge and the absent-key defaults are pinned here.</summary>
public class PerkLedgerTests
{
    private static int Cost(string id) => id == "perk.dear" ? 3 : 1;

    private static bool Innate(string id) => id == "perk.innate";

    private static void Restore(
        PerkLedger ledger, Dictionary<string, int> ranks, Dictionary<string, int>? free = null,
        int? spent = null, int? respecs = null)
    {
        ledger.Restore(ranks, free, spent, respecs, Cost, Innate);
    }

    [Fact]
    public void PaidAndFreeRanks_AreTrackedSeparately()
    {
        var ledger = new PerkLedger();
        ledger.AddFree("perk.innate");
        ledger.AddPaid("perk.innate", 1);
        ledger.AddPaid("perk.dear", 3);

        Assert.Equal(2, ledger.RankOf("perk.innate"));
        Assert.Equal(1, ledger.FreeOf("perk.innate"));
        Assert.Equal(4, ledger.PointsSpent);
    }

    [Fact]
    public void Respec_KeepsFreeRanks_RefundsPaid_AndCounts()
    {
        var ledger = new PerkLedger();
        ledger.AddFree("perk.innate");
        ledger.AddPaid("perk.innate", 1);
        ledger.AddPaid("perk.dear", 3);

        int refund = ledger.Respec();

        Assert.Equal(4, refund);
        Assert.Equal(1, ledger.RankOf("perk.innate"));
        Assert.Equal(0, ledger.RankOf("perk.dear"));
        Assert.Equal(0, ledger.PointsSpent);
        Assert.Equal(1, ledger.RespecCount);
        Assert.DoesNotContain("perk.dear", ledger.Ranks.Keys);
    }

    [Fact]
    public void Restore_ReadsAllV2Keys()
    {
        var ledger = new PerkLedger();
        Restore(
            ledger,
            new() { ["perk.innate"] = 3, ["perk.dear"] = 2 },
            new() { ["perk.innate"] = 1 },
            spent: 8,
            respecs: 2);

        Assert.Equal(3, ledger.RankOf("perk.innate"));
        Assert.Equal(1, ledger.FreeOf("perk.innate"));
        Assert.Equal(8, ledger.PointsSpent);
        Assert.Equal(2, ledger.RespecCount);
    }

    [Fact]
    public void Restore_AbsentKeys_DeriveFromAnOldSave()
    {
        var ledger = new PerkLedger();
        Restore(ledger, new() { ["perk.innate"] = 3, ["perk.dear"] = 2 });

        // Innate perk: one rank was free. Spent: 2 bought innate ranks at 1 + 2 dear ranks at 3.
        Assert.Equal(1, ledger.FreeOf("perk.innate"));
        Assert.Equal(0, ledger.FreeOf("perk.dear"));
        Assert.Equal(2 + 6, ledger.PointsSpent);
        Assert.Equal(0, ledger.RespecCount);
    }

    [Fact]
    public void Restore_ReplacesLiveStateWhenSavedValuesAreAbsentOrZero()
    {
        var ledger = new PerkLedger();
        ledger.AddFree("perk.innate");
        ledger.AddPaid("perk.dear", 3);
        ledger.AddPaid("perk.dear", 3);
        ledger.Respec();
        ledger.AddPaid("perk.dear", 3);
        Assert.True(ledger.PointsSpent > 0 && ledger.RespecCount > 0);

        // A save with no ranks, and every v2 key absent, must not leave anything live behind.
        Restore(ledger, new());

        Assert.Empty(ledger.Ranks);
        Assert.Empty(ledger.FreeRanks);
        Assert.Equal(0, ledger.PointsSpent);
        Assert.Equal(0, ledger.RespecCount);

        // Explicit zeroes behave the same as absence.
        ledger.AddPaid("perk.dear", 3);
        Restore(ledger, new(), new(), spent: 0, respecs: 0);
        Assert.Empty(ledger.Ranks);
        Assert.Equal(0, ledger.PointsSpent);
        Assert.Equal(0, ledger.RespecCount);
    }

    [Fact]
    public void Restore_DropsNonPositiveRanks_ClampsFree_AndNeverChecksPrerequisites()
    {
        var ledger = new PerkLedger();
        Restore(
            ledger,
            new() { ["perk.dear"] = 0, ["perk.innate"] = 1, ["perk.locked_tier5"] = 1 },
            new() { ["perk.innate"] = 5, ["perk.gone"] = 2 },
            spent: -4);

        Assert.Equal(0, ledger.RankOf("perk.dear"));
        Assert.Equal(1, ledger.RankOf("perk.locked_tier5"));
        Assert.Equal(1, ledger.FreeOf("perk.innate"));
        Assert.Equal(0, ledger.FreeOf("perk.gone"));
        Assert.Equal(0, ledger.PointsSpent);
    }
}
