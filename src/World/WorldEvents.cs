using Embervale.Core.Events;

namespace Embervale.World;

/// <summary>Raised by the <see cref="WorldClock"/> when the hour-of-day changes (and
/// once on start/load). Schedules and ambience react to this rather than polling.</summary>
public readonly record struct TimeOfDayChangedEvent(int Hour, DayPhase Phase) : IGameEvent;
