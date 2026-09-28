using System.Collections.Generic;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the story gate on the weather pool (Phase 44.5): the ending skies exist only after their
/// ending, and the storms leave for good once either ending is reached.
/// </summary>
public class WeatherEligibilityTests
{
    private const string Dawnfire = "flag.ending_dawnfire";
    private const string Embers = "flag.ending_embers";

    private static System.Func<string, bool> Flags(params string[] set)
    {
        var flags = new HashSet<string>(set);
        return flags.Contains;
    }

    [Fact]
    public void AnUngatedStateIsAlwaysEligible()
    {
        Assert.True(WeatherEligibility.IsEligible("", null, Flags()));
        Assert.True(WeatherEligibility.IsEligible(null, new List<string>(), Flags(Dawnfire)));
    }

    [Fact]
    public void AnEndingSkyWaitsForItsEnding()
    {
        Assert.False(WeatherEligibility.IsEligible(Embers, null, Flags()));
        Assert.False(WeatherEligibility.IsEligible(Embers, null, Flags(Dawnfire)));
        Assert.True(WeatherEligibility.IsEligible(Embers, null, Flags(Embers)));
    }

    [Fact]
    public void AStormIsStruckByEitherEnding()
    {
        string[] storm = { Dawnfire, Embers };

        Assert.True(WeatherEligibility.IsEligible("", storm, Flags()));
        Assert.False(WeatherEligibility.IsEligible("", storm, Flags(Dawnfire)));
        Assert.False(WeatherEligibility.IsEligible("", storm, Flags(Embers)));
    }

    [Fact]
    public void BlankExclusionsAreIgnored()
    {
        Assert.True(WeatherEligibility.IsEligible("", new[] { "", null! }, Flags(Dawnfire)));
    }
}
