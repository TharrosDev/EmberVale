using System;

namespace Embervale.UI;

/// <summary>
/// The HUD tracker's timing rules: when to surface the current objective's hint, and how long to
/// announce that the tracked quest changed. Godot-free; the HUD feeds them frame deltas.
/// </summary>
public static class TrackerRules
{
    /// <summary>Seconds on one objective before its hint shows under it.</summary>
    public const float HintDelaySeconds = 90f;

    /// <summary>Seconds the "now tracking" line holds.</summary>
    public const float TitleFlashSeconds = 3f;

    public static bool ShouldShowHint(bool hasHint, float dwellSeconds) =>
        hasHint && dwellSeconds >= HintDelaySeconds;
}

/// <summary>Seconds spent on the same objective. Resets the moment the objective key changes, including
/// to nothing.</summary>
public sealed class ObjectiveDwell
{
    private string _key = string.Empty;

    public float Seconds { get; private set; }

    public void Tick(string? objectiveKey, float delta)
    {
        string key = objectiveKey ?? string.Empty;
        if (key != _key)
        {
            _key = key;
            Seconds = 0f;
        }

        if (key.Length > 0 && delta > 0f)
        {
            Seconds += delta;
        }
    }
}

/// <summary>Holds the "now tracking" announcement. The first observation of a session only records the
/// quest (a load must not announce what was already tracked); every later change flashes.</summary>
public sealed class TrackerTitleFlash
{
    private bool _observed;
    private string _id = string.Empty;

    public float Remaining { get; private set; }

    public bool Active => Remaining > 0f;

    public void Observe(string? trackedQuestId, float delta)
    {
        string id = trackedQuestId ?? string.Empty;
        if (!_observed)
        {
            _observed = true;
            _id = id;
        }
        else if (id != _id)
        {
            _id = id;
            Remaining = id.Length > 0 ? TrackerRules.TitleFlashSeconds : 0f;
        }

        if (Remaining > 0f && delta > 0f)
        {
            Remaining = Math.Max(0f, Remaining - delta);
        }
    }
}
