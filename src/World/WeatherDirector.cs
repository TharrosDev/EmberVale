using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.World;

/// <summary>
/// Drives the world's weather: it holds the active <see cref="WeatherResource"/> and a
/// countdown (measured in in-game hours off the <see cref="WorldClock"/>), and when the
/// spell expires it rolls a new state by selection weight (never the same one twice in a
/// row) and publishes <see cref="WeatherChangedEvent"/>. The <see cref="SkyController"/>
/// renders the change; the <see cref="EncounterDirector"/> reads the current type.
///
/// Created by the bootstrap, registered in the <see cref="ServiceLocator"/>, and an
/// <see cref="ISaveable"/> so the weather (and time-to-change) survive save/load.
///
/// <b>Phase 44.5:</b> the pool is filtered by story flags (<see cref="WeatherEligibility"/>), so the
/// ending changes the sky for good. The filter is derived, never saved: a state the story no longer
/// allows is rolled away whenever it is found current (a flag change, a load, a periodic check), and
/// a state that a newly set flag requires is brought in at once, so the ending arrives with its sky.
/// </summary>
[GlobalClass]
public partial class WeatherDirector : Node, ISaveable
{
    /// <summary>Weather the world starts in on a fresh game.</summary>
    [Export] public string StartWeatherId { get; set; } = "weather.clear";

    private WeatherResource? _current;
    private float _hoursRemaining;

    /// <summary>Seconds until the next story-eligibility check. Covers a load, where the player's
    /// flags may be restored after this node's own <see cref="Load"/>.</summary>
    private double _eligibilityPoll;

    private const double EligibilityPollSeconds = 2.0;

    public string SaveId => "weather";

    public WeatherResource? Current => _current;

    public WeatherType CurrentType => _current?.Type ?? WeatherType.Clear;

    public override void _Ready()
    {
        // Weather should freeze with the game (it is measured against the clock).
        ProcessMode = ProcessModeEnum.Pausable;

        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);

        _current = WeatherDatabase.Get(StartWeatherId)
                   ?? (WeatherDatabase.All.Count > 0 ? WeatherDatabase.All[0] : null);
        _hoursRemaining = _current?.RollDuration() ?? 6f;
        Announce(WeatherType.Clear);
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlagChanged);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlagChanged);
    }

    public override void _Process(double delta)
    {
        if (_current == null || WeatherDatabase.All.Count == 0)
        {
            return;
        }

        _eligibilityPoll -= delta;
        if (_eligibilityPoll <= 0d)
        {
            _eligibilityPoll = EligibilityPollSeconds;
            EnsureEligible();
        }

        _hoursRemaining -= (float)delta * HoursPerSecond();
        if (_hoursRemaining <= 0f)
        {
            RollNext();
        }
    }

    /// <summary>Rolls the weather on if the story no longer allows the current state. Public for the
    /// <c>--story</c> probe; the director calls it itself on flag changes, after a load and every few
    /// seconds.</summary>
    public void EnsureEligible()
    {
        if (_current != null && !_current.IsEligible(StoryHas()))
        {
            RollNext();
        }
    }

    /// <summary>A newly set flag that some state requires brings that state in now — the ending's sky
    /// arrives with the ending, not whenever the current spell happens to run out.</summary>
    private void OnFlagChanged(StoryFlagChangedEvent e)
    {
        if (e.Value)
        {
            foreach (WeatherResource w in WeatherDatabase.All)
            {
                if (w.RequiredFlagId == e.Flag)
                {
                    Force(w.Id);
                    return;
                }
            }
        }

        EnsureEligible();
    }

    /// <summary>The player's story flags as a predicate; none (no session yet) reads as no flags.</summary>
    private static System.Func<string, bool> StoryHas() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player) &&
        player.GetComponent<StoryFlagsComponent>() is { } flags
            ? flags.Has
            : _ => false;

    /// <summary>Forces a specific weather state (dev console). Returns false if the id is unknown.</summary>
    public bool Force(string weatherId)
    {
        if (WeatherDatabase.Get(weatherId) is not { } weather)
        {
            return false;
        }

        WeatherType previous = CurrentType;
        _current = weather;
        _hoursRemaining = weather.RollDuration();
        Announce(previous);
        return true;
    }

    private void RollNext()
    {
        WeatherType previous = CurrentType;
        if (PickWeighted(excluding: _current, StoryHas()) is not { } next)
        {
            _hoursRemaining = _current?.RollDuration() ?? 6f; // nothing else allowed: keep this one
            return;
        }

        _current = next;
        _hoursRemaining = next.RollDuration();
        Announce(previous);
        Log.Info($"The weather turns to {next.DisplayName}.");
    }

    /// <summary>A weighted pick among the states the story allows, never the same one twice while
    /// there is another to choose; null when the story allows none.</summary>
    private static WeatherResource? PickWeighted(WeatherResource? excluding, System.Func<string, bool> has)
    {
        var eligible = new List<WeatherResource>();
        foreach (WeatherResource w in WeatherDatabase.All)
        {
            if (w.IsEligible(has))
            {
                eligible.Add(w);
            }
        }

        var pool = new List<WeatherResource>();
        float total = 0f;
        foreach (WeatherResource w in eligible)
        {
            if (eligible.Count > 1 && ReferenceEquals(w, excluding))
            {
                continue;
            }

            pool.Add(w);
            total += Mathf.Max(0f, w.SelectionWeight);
        }

        if (pool.Count == 0)
        {
            return null;
        }

        float roll = GD.Randf() * total;
        foreach (WeatherResource w in pool)
        {
            roll -= Mathf.Max(0f, w.SelectionWeight);
            if (roll <= 0f)
            {
                return w;
            }
        }

        return pool[pool.Count - 1];
    }

    private void Announce(WeatherType previous)
    {
        if (_current != null)
        {
            EventBus.Instance?.Publish(new WeatherChangedEvent(previous, _current.Type, _current.Id));
        }
    }

    private static float HoursPerSecond()
    {
        if (ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out WorldClock clock) &&
            clock.DayLengthSeconds > 0f)
        {
            return 24f / clock.DayLengthSeconds;
        }

        return 24f / 180f;
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary
        {
            ["weather"] = _current?.Id ?? StartWeatherId,
            ["remaining"] = _hoursRemaining,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        WeatherType previous = CurrentType;

        if (data.TryGetValue("weather", out Variant idVar) &&
            WeatherDatabase.Get(idVar.AsString()) is { } loaded)
        {
            _current = loaded;
        }

        if (data.TryGetValue("remaining", out Variant remVar))
        {
            _hoursRemaining = remVar.AsSingle();
        }

        Announce(previous);

        // A save from before the ending's weather existed can hold a state the story now forbids.
        // Derive, don't store: check again once the rest of the load (the player's flags) has landed.
        _eligibilityPoll = 0d;
        CallDeferred(MethodName.EnsureEligible);
    }
}
