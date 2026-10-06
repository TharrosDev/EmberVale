using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>
/// What the hotbar's cooldown sweep reads: when each cooldown key was started and for how long,
/// against a clock the owner advances. Pure, so the arithmetic is tested without a scene.
///
/// It is a display mirror, not the rule. Whether a consumable may be used is decided where it is
/// consumed; this only remembers that one was, so the bar can show the wait.
/// </summary>
public sealed class CooldownClock
{
    /// <summary>The player's consumable cooldowns, keyed by
    /// <see cref="Items.ConsumableItemResource.CooldownKey"/>. One per process: it is advanced by the
    /// hotbar, started by whichever screen saw the use, and cleared when a save is loaded.</summary>
    public static CooldownClock Consumables { get; } = new();

    private readonly Dictionary<string, (double Start, double Length)> _running = new();

    public double Now { get; private set; }

    /// <summary>True while anything is still counting down (the bar redraws only then).</summary>
    public bool AnyRunning => _running.Count > 0;

    public void Advance(double seconds)
    {
        if (seconds <= 0d)
        {
            return;
        }

        Now += seconds;
        if (_running.Count == 0)
        {
            return;
        }

        List<string>? done = null;
        foreach ((string key, (double start, double length)) in _running)
        {
            if (Now >= start + length)
            {
                (done ??= new List<string>()).Add(key);
            }
        }

        if (done != null)
        {
            foreach (string key in done)
            {
                _running.Remove(key);
            }
        }
    }

    /// <summary>Starts (or restarts) a cooldown. A blank key or a non-positive length starts nothing.</summary>
    public void Start(string key, double seconds)
    {
        if (string.IsNullOrEmpty(key) || seconds <= 0d)
        {
            return;
        }

        _running[key] = (Now, seconds);
    }

    public double Remaining(string key) =>
        _running.TryGetValue(key, out (double Start, double Length) run)
            ? System.Math.Max(0d, run.Start + run.Length - Now)
            : 0d;

    /// <summary>How much of the cooldown is still to run, 1 at the start falling to 0.</summary>
    public double Fraction(string key) =>
        _running.TryGetValue(key, out (double Start, double Length) run) && run.Length > 0d
            ? System.Math.Clamp((run.Start + run.Length - Now) / run.Length, 0d, 1d)
            : 0d;

    public void Clear() => _running.Clear();
}
