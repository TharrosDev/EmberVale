namespace Embervale.World;

/// <summary>Coarse part of the day, derived from the <see cref="WorldClock"/> hour.
/// Drives NPC schedules and (later) lighting/ambience.</summary>
public enum DayPhase
{
    Night,
    Dawn,
    Day,
    Dusk,
}

/// <summary>Helpers mapping an hour-of-day to a <see cref="DayPhase"/>.</summary>
public static class DayPhases
{
    /// <summary>The phase covering the given 24-hour clock hour.</summary>
    public static DayPhase Of(int hour)
    {
        hour = ((hour % 24) + 24) % 24;
        return hour switch
        {
            >= 5 and < 8 => DayPhase.Dawn,
            >= 8 and < 18 => DayPhase.Day,
            >= 18 and < 22 => DayPhase.Dusk,
            _ => DayPhase.Night,
        };
    }

    public static string Label(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => "Dawn",
        DayPhase.Day => "Day",
        DayPhase.Dusk => "Dusk",
        _ => "Night",
    };
}
