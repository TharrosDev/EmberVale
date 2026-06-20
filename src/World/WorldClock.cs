using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.World;

/// <summary>
/// A lightweight game-time clock: it advances a 24-hour day at a configurable real-time
/// rate and announces each new hour via <see cref="TimeOfDayChangedEvent"/>. It is the
/// minimal time source NPC schedules need; <b>Phase 13 (World Systems)</b> will build the
/// fuller day/night + weather model on top of it.
///
/// Created and owned by the bootstrap, registered in the <see cref="ServiceLocator"/> so
/// systems can read the current time, and an <see cref="ISaveable"/> so the time of day
/// survives save/load (routines resume where they left off). It pauses with the game.
/// </summary>
[GlobalClass]
public partial class WorldClock : Node, ISaveable
{
    /// <summary>Real seconds for one full in-game day. Short by default so a routine is
    /// visible within a play session.</summary>
    [Export] public float DayLengthSeconds { get; set; } = 180f;

    /// <summary>Hour the world starts at on a fresh game (0–24).</summary>
    [Export] public float StartHour { get; set; } = 8f;

    /// <summary>Continuous time of day in hours, [0, 24).</summary>
    public float TimeOfDay { get; private set; }

    /// <summary>Whole hour of day, [0, 23].</summary>
    public int Hour => Mathf.FloorToInt(TimeOfDay) % 24;

    public DayPhase Phase => DayPhases.Of(Hour);

    public string SaveId => "worldclock";

    private int _lastHour = -1;

    public override void _Ready()
    {
        // Time should freeze while the game is paused, regardless of the bootstrap's
        // always-on process mode.
        ProcessMode = ProcessModeEnum.Pausable;

        TimeOfDay = Mathf.PosMod(StartHour, 24f);
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
        Announce();
    }

    public override void _ExitTree()
    {
        ServiceLocator.Instance?.Unregister<WorldClock>();
        SaveManager.Instance?.Unregister(this);
    }

    public override void _Process(double delta)
    {
        if (DayLengthSeconds <= 0f)
        {
            return;
        }

        float hoursPerSecond = 24f / DayLengthSeconds;
        TimeOfDay = Mathf.PosMod(TimeOfDay + ((float)delta * hoursPerSecond), 24f);

        if (Hour != _lastHour)
        {
            Announce();
        }
    }

    /// <summary>"HH:00" string for UI.</summary>
    public string Clock() => $"{Hour:00}:00";

    private void Announce()
    {
        _lastHour = Hour;
        EventBus.Instance?.Publish(new TimeOfDayChangedEvent(Hour, Phase));
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary { ["time"] = TimeOfDay };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (data.TryGetValue("time", out Variant t))
        {
            TimeOfDay = Mathf.PosMod(t.AsSingle(), 24f);
        }

        Announce();
    }
}
