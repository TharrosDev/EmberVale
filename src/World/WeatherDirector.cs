using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
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
/// </summary>
[GlobalClass]
public partial class WeatherDirector : Node, ISaveable
{
    /// <summary>Weather the world starts in on a fresh game.</summary>
    [Export] public string StartWeatherId { get; set; } = "weather.clear";

    private WeatherResource? _current;
    private float _hoursRemaining;

    public string SaveId => "weather";

    public WeatherResource? Current => _current;

    public WeatherType CurrentType => _current?.Type ?? WeatherType.Clear;

    public override void _Ready()
    {
        // Weather should freeze with the game (it is measured against the clock).
        ProcessMode = ProcessModeEnum.Pausable;

        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);

        _current = WeatherDatabase.Get(StartWeatherId)
                   ?? (WeatherDatabase.All.Count > 0 ? WeatherDatabase.All[0] : null);
        _hoursRemaining = _current?.RollDuration() ?? 6f;
        Announce(WeatherType.Clear);
    }

    public override void _ExitTree()
    {
        ServiceLocator.Instance?.Unregister<WeatherDirector>();
        SaveManager.Instance?.Unregister(this);
    }

    public override void _Process(double delta)
    {
        if (_current == null || WeatherDatabase.All.Count == 0)
        {
            return;
        }

        _hoursRemaining -= (float)delta * HoursPerSecond();
        if (_hoursRemaining <= 0f)
        {
            RollNext();
        }
    }

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
        WeatherResource next = PickWeighted(excluding: _current);
        _current = next;
        _hoursRemaining = next.RollDuration();
        Announce(previous);
        Log.Info($"The weather turns to {next.DisplayName}.");
    }

    private static WeatherResource PickWeighted(WeatherResource? excluding)
    {
        IReadOnlyList<WeatherResource> all = WeatherDatabase.All;
        var pool = new List<WeatherResource>();
        float total = 0f;
        foreach (WeatherResource w in all)
        {
            if (all.Count > 1 && ReferenceEquals(w, excluding))
            {
                continue;
            }

            pool.Add(w);
            total += Mathf.Max(0f, w.SelectionWeight);
        }

        if (pool.Count == 0)
        {
            return excluding ?? all[0];
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
    }
}
