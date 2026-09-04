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

    /// <summary>
    /// In-game days elapsed since the run began. <see cref="TimeOfDay"/> wraps through
    /// <c>PosMod</c> and until Phase 38B nothing counted the wraps, so the game had no notion of a
    /// date at all — only an hour. Shop restock is the first system to need one
    /// (<c>Economy.ShopStockService</c>); it is a plain counter so anything later can key off it too.
    /// </summary>
    public int Day { get; private set; }

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
        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
        Announce();
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public override void _Process(double delta)
    {
        if (DayLengthSeconds <= 0f)
        {
            return;
        }

        float hoursPerSecond = 24f / DayLengthSeconds;

        // Advance unwrapped first: PosMod loses the crossing, so counting days has to happen before
        // the wrap. A long frame can cross more than one midnight, hence the division rather than ++.
        float advanced = TimeOfDay + ((float)delta * hoursPerSecond);
        Day += Mathf.FloorToInt(advanced / 24f);
        TimeOfDay = Mathf.PosMod(advanced, 24f);

        if (Hour != _lastHour)
        {
            Announce();
        }
    }

    /// <summary>"HH:00" string for UI.</summary>
    public string Clock() => $"{Hour:00}:00";

    /// <summary>
    /// Jumps the clock to a given hour; used by the dev console / repro harness. An hour of 24 or more
    /// rolls the date forward too — <c>time 26</c> is tomorrow at 02:00. That is the only way to
    /// advance a day without waiting <see cref="DayLengthSeconds"/> per day in real time, which
    /// matters for anything keyed off <see cref="Day"/>. Jumping backwards sets the hour and leaves
    /// the date alone: this command names a time, not a date.
    /// </summary>
    public void SetTimeOfDay(float hour)
    {
        if (hour >= 24f)
        {
            Day += Mathf.FloorToInt(hour / 24f);
        }

        TimeOfDay = Mathf.PosMod(hour, 24f);
        Announce();
    }

    private void Announce()
    {
        _lastHour = Hour;
        EventBus.Instance?.Publish(new TimeOfDayChangedEvent(Hour, Phase));
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary { ["time"] = TimeOfDay, ["day"] = Day };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (data.TryGetValue("time", out Variant t))
        {
            TimeOfDay = Mathf.PosMod(t.AsSingle(), 24f);
        }

        // Replaced, never merged (§7): a save predating Phase 38B carries no day, and inheriting the
        // abandoned timeline's count would read as days having passed and restock every shop on load.
        Day = data.TryGetValue("day", out Variant d) ? Mathf.Max(0, d.AsInt32()) : 0;

        Announce();
    }
}
