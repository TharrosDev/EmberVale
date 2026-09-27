using System.Collections.Generic;
using Embervale.Narrative;
using Xunit;

namespace Embervale.Tests;

public class HiddenRealmRevealTests
{
    private static bool Reveal(params string[] held) =>
        HiddenRealmReveal.ShouldReveal(new HashSet<string>(held).Contains);

    [Fact]
    public void RevealsOnlyWhenAllThreeActTwoFlamebearersHaveFallen()
    {
        Assert.False(Reveal());
        Assert.False(Reveal("flag.iron_king_defeated", "flag.storm_tyrant_defeated", "flag.beast_lord_defeated"));
        Assert.True(Reveal("flag.storm_tyrant_defeated", "flag.beast_lord_defeated", "flag.crimson_prophet_defeated"));
    }

    [Fact]
    public void DoesNotRevealTwice()
    {
        Assert.False(Reveal("flag.storm_tyrant_defeated", "flag.beast_lord_defeated",
            "flag.crimson_prophet_defeated", HiddenRealmReveal.RevealedFlag));
    }
}
