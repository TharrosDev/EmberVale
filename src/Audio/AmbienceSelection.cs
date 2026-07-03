using Embervale.World;

namespace Embervale.Audio;

/// <summary>
/// Pure selector for the environmental ambience bed (Phase 31D): weather wins over locale, which wins
/// over time of day. Wet weather (rain/storm) always plays the rain wash; otherwise a town plays its
/// murmur, open country plays a day or night bed by the clock. Godot-free so the mapping unit-tests
/// under <c>dotnet test</c>; the <see cref="AmbienceDirector"/> feeds it from EventBus signals and
/// crossfades on the resolved change.
/// </summary>
public static class AmbienceSelection
{
    public const string Day = "amb.day";
    public const string Night = "amb.night";
    public const string Rain = "amb.rain";
    public const string Town = "amb.town";

    /// <summary>The ambience cue id for the current context.</summary>
    public static string Resolve(bool inTown, WeatherType weather, DayPhase phase)
    {
        if (weather is WeatherType.Rain or WeatherType.Storm)
        {
            return Rain;
        }

        if (inTown)
        {
            return Town;
        }

        return phase == DayPhase.Night ? Night : Day;
    }
}
