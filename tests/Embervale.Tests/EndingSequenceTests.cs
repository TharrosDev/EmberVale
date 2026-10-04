using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

public class EndingSequenceTests
{
    [Theory]
    [InlineData(true, 0, "ending.dawnfire.epilogue_none")]
    [InlineData(true, 3, "ending.dawnfire.epilogue_some")]
    [InlineData(false, 1, "ending.embers.epilogue_some")]
    [InlineData(false, 6, "ending.embers.epilogue_all")]
    public void EpilogueBandsByEmbersTaken(bool dawnfire, int absorbed, string expected) =>
        Assert.Equal(expected, EndingSequence.EpilogueKey(dawnfire, absorbed));

    [Fact]
    public void ScriptIsEndingThenEpilogueThenCredits()
    {
        string[] cards = EndingSequence.Script(dawnfire: false, absorbed: 0);
        Assert.StartsWith("ending.embers.", cards[0]);
        Assert.Equal("ending.embers.epilogue_none", cards[4]);
        Assert.StartsWith("ending.credits.", cards[^1]);
    }

    [Fact]
    public void CountsOnlyHeldAbsorbFlags() =>
        Assert.Equal(2, EndingSequence.CountAbsorbed(f => f is "flag.iron_king_absorbed" or "flag.hollow_queen_absorbed"));

    private static Func<string, bool> Holding(params string[] flags) => new HashSet<string>(flags).Contains;

    [Fact]
    public void NoForksAddsNoCards()
    {
        string[] plain = EndingSequence.Script(dawnfire: true, absorbed: 2);
        Assert.Equal(plain, EndingSequence.Script(dawnfire: true, absorbed: 2, Holding()));
        Assert.Empty(EndingSequence.ForkCards(Holding()));
        Assert.Equal(8, plain.Length); // four ending cards, the epilogue, three credit cards
    }

    [Fact]
    public void EveryForkAddsOneCardInActOrderBetweenTheEpilogueAndTheCredits()
    {
        Func<string, bool> all = Holding(
            "flag.fork.dray_pressed", "flag.fork.succession_halvar", "flag.fork.herd_calmed",
            "flag.fork.flock_kin", "flag.fork.queen_kept", "flag.rival.gate_draw");

        string[] cards = EndingSequence.Script(dawnfire: false, absorbed: 6, all);

        Assert.Equal(
            new[]
            {
                "ending.embers.1", "ending.embers.2", "ending.embers.3", "ending.embers.4",
                "ending.embers.epilogue_all",
                "ending.epilogue.dray.b", "ending.epilogue.succession.b", "ending.epilogue.herd.b",
                "ending.epilogue.flock.c", "ending.epilogue.queen.b", "ending.epilogue.vigil.b",
                "ending.credits.1", "ending.credits.2", "ending.credits.3",
            },
            cards);
    }

    [Theory]
    [InlineData("flag.fork.dray_spared", "ending.epilogue.dray.a")]
    [InlineData("flag.fork.dray_pressed", "ending.epilogue.dray.b")]
    [InlineData("flag.fork.succession_hjalvar", "ending.epilogue.succession.a")]
    [InlineData("flag.fork.succession_halvar", "ending.epilogue.succession.b")]
    [InlineData("flag.fork.herd_slain", "ending.epilogue.herd.a")]
    [InlineData("flag.fork.herd_calmed", "ending.epilogue.herd.b")]
    [InlineData("flag.fork.flock_exposed", "ending.epilogue.flock.a")]
    [InlineData("flag.fork.flock_turned", "ending.epilogue.flock.b")]
    [InlineData("flag.fork.flock_kin", "ending.epilogue.flock.c")]
    [InlineData("flag.fork.queen_released", "ending.epilogue.queen.a")]
    [InlineData("flag.fork.queen_kept", "ending.epilogue.queen.b")]
    [InlineData("flag.rival.gate_kneel", "ending.epilogue.vigil.a")]
    [InlineData("flag.rival.gate_draw", "ending.epilogue.vigil.b")]
    public void ASingleForkPlaysExactlyItsCard(string flag, string expected)
    {
        Assert.Equal(new[] { expected }, EndingSequence.ForkCards(Holding(flag)));

        string[] cards = EndingSequence.Script(dawnfire: true, absorbed: 0, Holding(flag));
        Assert.Equal(9, cards.Length);
        Assert.Equal(expected, cards[5]);
        Assert.StartsWith("ending.credits.", cards[6]);
    }

    [Fact]
    public void TheKnightsThirdAnswerPlaysNoCard() =>
        Assert.Empty(EndingSequence.ForkCards(Holding("flag.beat.gate_vigil_done")));

    [Fact]
    public void BothEndingsCarryTheSameForkCards()
    {
        Func<string, bool> has = Holding("flag.fork.dray_spared", "flag.fork.queen_released");
        string[] dawn = EndingSequence.Script(true, 1, has);
        string[] embers = EndingSequence.Script(false, 1, has);

        Assert.Equal(dawn.Length, embers.Length);
        Assert.Equal(dawn.Skip(5).Take(2), embers.Skip(5).Take(2));
        Assert.Equal(new[] { "ending.epilogue.dray.a", "ending.epilogue.queen.a" }, dawn.Skip(5).Take(2));
        Assert.StartsWith("ending.dawnfire.", dawn[0]);
        Assert.StartsWith("ending.embers.", embers[0]);
    }

    [Fact]
    public void AForkWithSeveralFlagsHeldPlaysItsFirstAnswerOnce() =>
        Assert.Equal(
            new[] { "ending.epilogue.dray.a" },
            EndingSequence.ForkCards(Holding("flag.fork.dray_spared", "flag.fork.dray_pressed")));

    [Fact]
    public void EveryForkCardHasTextAndOnlyPaleKeysNameTheConcord()
    {
        string root = StringsCsv.RepositoryRoot();
        Dictionary<string, string> rows = StringsCsv.Rows();

        string graph = File.ReadAllText(Path.Combine(root, "data/story/campaign_graph.json"));
        foreach ((string slug, string[] flags) in EndingSequence.ForkEpilogues)
        {
            for (int i = 0; i < flags.Length; i++)
            {
                string key = $"ending.epilogue.{slug}.{(char)('a' + i)}";
                Assert.True(rows.TryGetValue(key, out string? text) && text.Length > 2, $"'{key}' has no text");
                Assert.False(text!.Contains("Pale Concord", StringComparison.OrdinalIgnoreCase), $"'{key}' names the Pale Concord");
                Assert.True(graph.Contains($"\"{flags[i]}\"", StringComparison.Ordinal), $"nothing in the campaign writes {flags[i]}");
            }
        }
    }
}
