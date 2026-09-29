using System;
using Embervale.Combat.Actions;

namespace Embervale.Combat;

/// <summary>
/// How one melee swing's trail looks: the colour, the radius it sweeps at, how long the visible streak
/// is behind the leading edge, how far the leading edge travels, the tilt of the plane it sweeps in,
/// which way it goes, and how long it lasts. A heavy swing is a vertical chop; a light chain alternates
/// left-to-right and right-to-left on a slant, so the arc says which way the blade is coming from.
/// </summary>
public readonly record struct TrailStyle(
    float R,
    float G,
    float B,
    float Alpha,
    float Radius,
    float StreakDegrees,
    float TravelDegrees,
    float TiltDegrees,
    bool Vertical,
    int Direction,
    float FadeSeconds);

/// <summary>Pure mapping from a swing to its <see cref="TrailStyle"/>. Godot-free so the look and the
/// timing are unit-tested; <see cref="WeaponTrailComponent"/> draws it.</summary>
public static class TrailStyles
{
    public const float MinFade = 0.12f;
    public const float MaxFade = 0.5f;

    /// <summary>
    /// The style of a swing. <paramref name="windupSeconds"/> and <paramref name="activeFrom"/> recover
    /// the action's whole length (the wind-up is <c>duration x ActiveFrom</c>), and
    /// <paramref name="trailFrom"/>..<paramref name="trailTo"/> (authored on the action) say what share
    /// of it the streak is visible for. A hostile attacker's trail is warm red-white so the player can
    /// tell an incoming arc from their own.
    /// </summary>
    public static TrailStyle For(
        ActionKind kind, int comboIndex, bool hostile, float windupSeconds, float activeFrom,
        float trailFrom, float trailTo)
    {
        bool heavy = kind == ActionKind.HeavyAttack;
        int direction = comboIndex % 2 == 0 ? 1 : -1;

        float duration = activeFrom > 0.01f && windupSeconds > 0f ? windupSeconds / activeFrom : 0.5f;
        float fade = Math.Clamp(duration * Math.Max(trailTo - trailFrom, 0f), MinFade, MaxFade);

        (float r, float g, float b) = hostile
            ? (1.0f, 0.55f, 0.45f)
            : heavy ? (1.0f, 0.68f, 0.30f) : (0.90f, 0.95f, 1.0f);

        int link = Math.Clamp(comboIndex, 0, 3);
        return heavy
            ? new TrailStyle(r, g, b, 0.75f, 1.5f, 95f, 130f, 0f, true, 1, Math.Min(fade * 1.25f, MaxFade))
            : new TrailStyle(r, g, b, 0.55f + (0.05f * link), 1.25f + (0.05f * link),
                60f + (8f * link), 150f, 18f * direction, false, direction, fade);
    }

    /// <summary>The leading edge's angle, degrees from where the sweep starts, at <paramref name="t"/>
    /// (0..1) through the trail's life: eased out, so it whips through the blow and slows at the end.</summary>
    public static float LeadDegrees(in TrailStyle style, float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        float eased = 1f - ((1f - x) * (1f - x));
        return (-style.TravelDegrees * 0.5f) + (style.TravelDegrees * eased);
    }

    /// <summary>The trail's opacity multiplier at <paramref name="t"/>: bright through the blow, gone at
    /// the end.</summary>
    public static float Fade(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return 1f - (x * x);
    }
}
