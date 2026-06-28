using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the hour → <see cref="DayPhase"/> boundaries that drive NPC schedules and ambience. Pure
/// (Godot-free), so the transition edges are checked directly.
/// </summary>
public class DayPhaseTests
{
    [Theory]
    [InlineData(0, DayPhase.Night)]
    [InlineData(4, DayPhase.Night)]   // last hour before dawn
    [InlineData(5, DayPhase.Dawn)]    // dawn opens
    [InlineData(7, DayPhase.Dawn)]    // last dawn hour
    [InlineData(8, DayPhase.Day)]     // day opens
    [InlineData(17, DayPhase.Day)]    // last day hour
    [InlineData(18, DayPhase.Dusk)]   // dusk opens
    [InlineData(21, DayPhase.Dusk)]   // last dusk hour
    [InlineData(22, DayPhase.Night)]  // night returns
    [InlineData(23, DayPhase.Night)]
    public void Of_MapsHourToPhaseAtTheBoundaries(int hour, DayPhase expected)
    {
        Assert.Equal(expected, DayPhases.Of(hour));
    }

    [Theory]
    [InlineData(-1, DayPhase.Night)] // 23:00
    [InlineData(-19, DayPhase.Dawn)] //  5:00
    [InlineData(24, DayPhase.Night)] //  0:00
    [InlineData(26, DayPhase.Night)] //  2:00
    [InlineData(32, DayPhase.Day)]   //  8:00
    public void Of_WrapsHoursOutsideZeroTo24(int hour, DayPhase expected)
    {
        Assert.Equal(expected, DayPhases.Of(hour));
    }
}
