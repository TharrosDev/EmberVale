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
}
