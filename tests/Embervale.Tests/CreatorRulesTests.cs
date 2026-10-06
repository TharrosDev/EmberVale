using System;
using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Stats;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>The character creator's pure rules: the rail, the preview's framing, yaw and light rigs,
/// the summary's stat nudges, a random look and a card grid's neighbours.</summary>
public class CreatorRulesTests
{
    [Fact]
    public void TheRailHasOneStepPerEnumValue() =>
        Assert.Equal(CreatorRules.StepCount, Enum.GetValues<CreatorStep>().Length);

    [Fact]
    public void SteppingTheRailWrapsAtBothEnds()
    {
        Assert.Equal(CreatorStep.Appearance, CreatorRules.Step(CreatorStep.Race, 1));
        Assert.Equal(CreatorStep.Race, CreatorRules.Step(CreatorStep.Name, 1));
        Assert.Equal(CreatorStep.Name, CreatorRules.Step(CreatorStep.Race, -1));
        Assert.Equal(CreatorStep.Background, CreatorRules.Step(CreatorStep.Background, CreatorRules.StepCount * 3));
    }

    [Theory]
    [InlineData(AppearanceSlot.Hair, PreviewFraming.Face)]
    [InlineData(AppearanceSlot.Eyes, PreviewFraming.Face)]
    [InlineData(AppearanceSlot.Skin, PreviewFraming.Body)]
    [InlineData(AppearanceSlot.Ember, PreviewFraming.Body)]
    [InlineData(AppearanceSlot.Build, PreviewFraming.Body)]
    public void HeadLooksAreChosenAtTheFaceAndTheRestAtTheFigure(AppearanceSlot slot, PreviewFraming expected) =>
        Assert.Equal(expected, CreatorRules.FramingFor(CreatorStep.Appearance, slot));

    [Fact]
    public void OnlyTheAppearanceStepEverFramesTheFace()
    {
        Assert.Equal(PreviewFraming.Body, CreatorRules.FramingFor(CreatorStep.Appearance, null));
        foreach (CreatorStep step in Enum.GetValues<CreatorStep>())
        {
            if (step != CreatorStep.Appearance)
            {
                Assert.Equal(PreviewFraming.Body, CreatorRules.FramingFor(step, AppearanceSlot.Hair));
            }
        }
    }

    [Fact]
    public void EverySlotHasAFraming()
    {
        // A slot added to the enum must be given a framing on purpose, not by falling through.
        Assert.Equal(AppearanceRules.SlotCount, Enum.GetValues<AppearanceSlot>().Length);
    }

    [Fact]
    public void TheLightRigsCycle()
    {
        int rig = 0;
        var seen = new HashSet<int>();
        for (int i = 0; i < CreatorRules.LightRigCount; i++)
        {
            Assert.True(seen.Add(rig));
            rig = CreatorRules.NextRig(rig);
        }

        Assert.Equal(0, rig);
        Assert.Equal(CreatorRules.LightRigCount, seen.Count);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(22f, 22f)]
    [InlineData(180f, 180f)]
    [InlineData(181f, -179f)]
    [InlineData(-180f, 180f)]
    [InlineData(-190f, 170f)]
    [InlineData(742f, 22f)]
    [InlineData(-698f, 22f)]
    public void AYawStaysWithinOneTurn(float degrees, float expected) =>
        Assert.Equal(expected, CreatorRules.WrapYaw(degrees), 3);

    [Fact]
    public void TheSummaryAddsARaceAndABackgroundTogether()
    {
        List<(StatType Stat, float Amount)> combined = CreatorRules.CombineDeltas(new[]
        {
            (StatType.Strength, 4f),
            (StatType.MoveSpeed, -0.4f),
            (StatType.Strength, 2f),
        });

        Assert.Equal(2, combined.Count);
        Assert.Equal((StatType.Strength, 6f), combined[0]); // the order first seen
        Assert.Equal(StatType.MoveSpeed, combined[1].Stat);
        Assert.Equal(-0.4f, combined[1].Amount, 3);
    }

    [Fact]
    public void NudgesThatCancelAreNotListed()
    {
        List<(StatType Stat, float Amount)> combined = CreatorRules.CombineDeltas(new[]
        {
            (StatType.Strength, 3f),
            (StatType.Strength, -3f),
        });

        Assert.Empty(combined);
        Assert.Empty(CreatorRules.CombineDeltas(Array.Empty<(StatType, float)>()));
    }

    [Fact]
    public void ARandomLookPicksInsideWhatEachSlotOffers()
    {
        int[] counts = { 6, 1, 0, 3, 2 };
        var random = new Random(1234);
        for (int run = 0; run < 200; run++)
        {
            int[] picks = CreatorRules.RandomPicks(counts, random);
            Assert.Equal(counts.Length, picks.Length);
            for (int i = 0; i < picks.Length; i++)
            {
                if (counts[i] == 0)
                {
                    Assert.Equal(-1, picks[i]);
                }
                else
                {
                    Assert.InRange(picks[i], 0, counts[i] - 1);
                }
            }
        }
    }

    [Fact]
    public void ARandomLookIsTheSameForTheSameSeed() =>
        Assert.Equal(
            CreatorRules.RandomPicks(new[] { 6, 5, 4, 3, 2 }, new Random(7)),
            CreatorRules.RandomPicks(new[] { 6, 5, 4, 3, 2 }, new Random(7)));

    [Fact]
    public void AGridsNeighboursAreTheCellsBesideAboveAndBelow()
    {
        // Six cards, two across:   0 1
        //                          2 3
        //                          4 5
        Assert.Equal(1, CreatorRules.GridNeighbour(0, 6, 2, 1, 0));
        Assert.Equal(2, CreatorRules.GridNeighbour(0, 6, 2, 0, 1));
        Assert.Equal(1, CreatorRules.GridNeighbour(3, 6, 2, 0, -1));
        Assert.Equal(2, CreatorRules.GridNeighbour(3, 6, 2, -1, 0));
        Assert.Equal(5, CreatorRules.GridNeighbour(3, 6, 2, 0, 1));
    }

    [Fact]
    public void AStepOffTheGridHasNoNeighbour()
    {
        Assert.Equal(-1, CreatorRules.GridNeighbour(0, 6, 2, -1, 0)); // left edge: the caller names the rail
        Assert.Equal(-1, CreatorRules.GridNeighbour(1, 6, 2, 1, 0));  // right edge never wraps to the next row
        Assert.Equal(-1, CreatorRules.GridNeighbour(0, 6, 2, 0, -1));
        Assert.Equal(-1, CreatorRules.GridNeighbour(5, 6, 2, 0, 1));
        Assert.Equal(-1, CreatorRules.GridNeighbour(9, 6, 2, 0, 0));
        Assert.Equal(-1, CreatorRules.GridNeighbour(0, 6, 0, 1, 0));
    }

    [Fact]
    public void AShortLastRowHasNothingUnderItsMissingCell()
    {
        // Five cards, two across: the fifth sits alone, so nothing is below the fourth.
        Assert.Equal(4, CreatorRules.GridNeighbour(2, 5, 2, 0, 1));
        Assert.Equal(-1, CreatorRules.GridNeighbour(3, 5, 2, 0, 1));
        Assert.Equal(-1, CreatorRules.GridNeighbour(4, 5, 2, 1, 0));
    }
}
