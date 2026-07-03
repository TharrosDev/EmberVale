using Embervale.Audio;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the ambience selection priority (Phase 31D): weather &gt; locale &gt; time of day. Godot-free;
/// the AmbienceDirector's event/crossfade wiring is exercised in-engine.
/// </summary>
public class AmbienceSelectionTests
{
    [Theory]
    [InlineData(WeatherType.Rain)]
    [InlineData(WeatherType.Storm)]
    public void WetWeather_AlwaysRain_EvenInTownAtNight(WeatherType weather) =>
        Assert.Equal(AmbienceSelection.Rain, AmbienceSelection.Resolve(inTown: true, weather, DayPhase.Night));

    [Fact]
    public void Town_WhenDry() =>
        Assert.Equal(AmbienceSelection.Town, AmbienceSelection.Resolve(inTown: true, WeatherType.Clear, DayPhase.Day));

    [Fact]
    public void OpenCountry_Day_WhenDryAndNotNight() =>
        Assert.Equal(AmbienceSelection.Day, AmbienceSelection.Resolve(inTown: false, WeatherType.Fog, DayPhase.Dusk));

    [Fact]
    public void OpenCountry_Night() =>
        Assert.Equal(AmbienceSelection.Night, AmbienceSelection.Resolve(inTown: false, WeatherType.Clear, DayPhase.Night));
}
