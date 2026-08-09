using System.Collections.Generic;
using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Which postings the caravan board shows, and for how long (Phase 38Q2). Two of these guard
/// properties the feature rests on rather than arithmetic:
/// <see cref="TheSameDayAlwaysYieldsTheSameBoard"/> is what makes a quickload unable to reroll the
/// board, and <see cref="AFullRotationNeverRepeatsAPosting"/> is what stops one turning up twice.
/// </summary>
public class ContractRulesTests
{
    [Fact]
    public void ACycleIsAWholeNumberOfPeriods()
    {
        Assert.Equal(0, ContractRules.Cycle(0, 4));
        Assert.Equal(0, ContractRules.Cycle(3, 4));
        Assert.Equal(1, ContractRules.Cycle(4, 4));
        Assert.Equal(3, ContractRules.Cycle(15, 4));

        // A board authored never to turn is legal and is always cycle 0 — the same "0 means off"
        // reading ShopStock.IsRestockDue gives a shop with no restock clock.
        Assert.Equal(0, ContractRules.Cycle(999, 0));
    }

    [Fact]
    public void ARewoundClockStepsDownOneCycleAtATime()
    {
        // Floors rather than truncates. Truncation folds days -3..3 onto cycle 0, so a clock rewound
        // past day zero would show the same board for twice as long as it should — the mirror of the
        // rewound-clock case ShopStock.IsRestockDue was written for.
        Assert.Equal(-1, ContractRules.Cycle(-1, 4));
        Assert.Equal(-1, ContractRules.Cycle(-4, 4));
        Assert.Equal(-2, ContractRules.Cycle(-5, 4));
    }

    [Fact]
    public void TheCountdownNeverReadsZeroDays()
    {
        // 1 on the last day of a rotation, so the footer needs no special case for it.
        Assert.Equal(4, ContractRules.DaysLeft(0, 4));
        Assert.Equal(3, ContractRules.DaysLeft(1, 4));
        Assert.Equal(1, ContractRules.DaysLeft(3, 4));
        Assert.Equal(4, ContractRules.DaysLeft(4, 4));
    }

    [Fact]
    public void TheSameDayAlwaysYieldsTheSameBoard()
    {
        // ⚠️ THE ONE THAT MATTERS. The day is the only input, so nothing about the board is saved and
        // a quickload cannot reroll it. If this ever fails, the rotation has picked up hidden state
        // and 38S's rule has been broken a sub-phase after it was honoured for free.
        for (int day = 0; day < 40; day++)
        {
            int cycle = ContractRules.Cycle(day, 4);
            for (int slot = 0; slot < 3; slot++)
            {
                Assert.Equal(
                    ContractRules.SlotContract(cycle, slot, 8),
                    ContractRules.SlotContract(ContractRules.Cycle(day, 4), slot, 8));
            }
        }
    }

    [Fact]
    public void AFullRotationNeverRepeatsAPosting()
    {
        // Distinct by construction: the stride is forced coprime with the pool, so slot * stride walks
        // every index once before repeating. Checked for every pool size a small realm might have and
        // for a long run of cycles, because a reject-and-resample version would pass a spot check and
        // fail on the pool that is barely larger than the board.
        for (int poolSize = 2; poolSize <= 24; poolSize++)
        {
            for (int cycle = -5; cycle < 60; cycle++)
            {
                var seen = new HashSet<int>();
                for (int slot = 0; slot < poolSize; slot++)
                {
                    int index = ContractRules.SlotContract(cycle, slot, poolSize);
                    Assert.InRange(index, 0, poolSize - 1);
                    Assert.True(seen.Add(index), $"pool {poolSize}, cycle {cycle}: slot {slot} repeated index {index}");
                }
            }
        }
    }

    [Fact]
    public void TheBoardTurnsOverBetweenCycles()
    {
        // Not a guarantee the arithmetic makes — two adjacent cycles may share a posting by chance —
        // but over a run of cycles the first slot must actually move, or the "rotating" in rotating
        // supply contracts is decorative.
        var firstSlots = new HashSet<int>();
        for (int cycle = 0; cycle < 12; cycle++)
        {
            firstSlots.Add(ContractRules.SlotContract(cycle, 0, 8));
        }

        Assert.True(firstSlots.Count > 3, $"the board barely moves: only {firstSlots.Count} openers in 12 cycles");
    }

    [Fact]
    public void AnEmptyPoolShowsNothingRatherThanThrowing()
    {
        Assert.Equal(-1, ContractRules.SlotContract(3, 0, 0));
        Assert.Equal(-1, ContractRules.SlotContract(3, -1, 8));
        Assert.Equal(0, ContractRules.SlotContract(3, 0, 1));
        Assert.Equal(0, ContractRules.SlotContract(3, 5, 1));
    }
}
