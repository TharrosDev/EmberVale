using System;

namespace Embervale.UI;

/// <summary>
/// Pure easing/progress maths behind the 30.5I microinteractions (panel fades, toast slides,
/// the XP/level-up pops). The animation driving runs in-engine (`_Process` timers, tweens);
/// the curve arithmetic is pure and pinned by unit tests, mirroring <c>CompassMath</c>.
/// Per the style guide (§5): ease-out entrances, ease-in exits, no bounces.
/// </summary>
public static class UiMotion
{
    /// <summary>Normalized progress of <paramref name="elapsed"/> through
    /// <paramref name="duration"/>, clamped to 0..1. A non-positive duration is complete
    /// immediately (the reduced-motion collapse).</summary>
    public static float Progress(float elapsed, float duration) =>
        duration <= 0f ? 1f : Math.Clamp(elapsed / duration, 0f, 1f);

    /// <summary>Cubic ease-out: fast start, gentle settle — entrances.</summary>
    public static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float inv = 1f - t;
        return 1f - (inv * inv * inv);
    }

    /// <summary>Cubic ease-in: gentle start, fast finish — exits.</summary>
    public static float EaseIn(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * t;
    }

    /// <summary>
    /// One frame of a hold-to-confirm gauge (<see cref="HoldRing"/>): fills over
    /// <paramref name="holdSeconds"/> while held, drains over <paramref name="drainSeconds"/> once
    /// let go, clamped to 0..1. A non-positive hold time is full the moment it is held, and a
    /// non-positive drain time empties at once (the reduced-motion collapse).
    /// </summary>
    public static float HoldStep(float progress, bool held, float delta, float holdSeconds, float drainSeconds)
    {
        delta = Math.Max(0f, delta);
        if (held)
        {
            return holdSeconds <= 0f ? 1f : Math.Clamp(progress + (delta / holdSeconds), 0f, 1f);
        }

        return drainSeconds <= 0f ? 0f : Math.Clamp(progress - (delta / drainSeconds), 0f, 1f);
    }
}
