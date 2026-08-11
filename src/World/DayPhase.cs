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

    /// <summary>
    /// The locale key naming a phase.
    ///
    /// ⚠️ <b><see cref="Label"/> returned hard-coded English and the player could read it</b> — the
    /// gameplay HUD's clock printed "10:00 (Day)" straight from it, which is exactly the §46 / CLAUDE
    /// §6 rule against player-facing string literals, sitting in the most-visible widget in the game
    /// since Phase 18. Found by 39.5B's audit.
    ///
    /// ⚠️ It is a **computed** key (invariant 26): built from an enum member, named by no `.tres`, so
    /// no database walk can reach it. `ContentValidator.ValidateHudComputedKeys` enumerates the enum.
    /// </summary>
    public static string NameKey(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => "time.phase.dawn",
        DayPhase.Day => "time.phase.day",
        DayPhase.Dusk => "time.phase.dusk",
        _ => "time.phase.night",
    };

    /// <summary>Diagnostics only — the dev console and `DebugHud` are exempt from localization
    /// (CLAUDE.md §6). Anything the player reads goes through <see cref="NameKey"/>.</summary>
    public static string Label(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => "Dawn",
        DayPhase.Day => "Day",
        DayPhase.Dusk => "Dusk",
        _ => "Night",
    };
}
