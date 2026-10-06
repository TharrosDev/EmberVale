using System;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// How bright, how often and what colour a spell may flash the screen. Pure, because these are the
/// comfort limits and a limit nobody can test is a limit that drifts:
/// the peak never exceeds <see cref="MaxPeak"/> times the player's own flash setting, an enemy's
/// spell flashes only when it struck the player and then no brighter than <see cref="EnemyHitCap"/>,
/// Reduced Motion caps everything at <see cref="ReducedMotionCap"/>, two flashes are never closer
/// than <see cref="MinInterval"/> seconds, and the tint is pulled most of the way to a warm white so
/// a saturated full-screen colour never happens.
/// </summary>
public static class VfxScreenRules
{
    public const float MaxPeak = 0.35f;
    public const float EnemyHitCap = 0.2f;
    public const float ReducedMotionCap = 0.09f;
    public const double MinInterval = 0.3d;

    /// <summary>Seconds the flash takes to reach its peak, and to fade from it.</summary>
    public const float AttackSeconds = 0.04f;

    public const float DecaySeconds = 0.26f;

    /// <summary>How far the school colour is pulled toward warm white.</summary>
    public const float WarmWhiteMix = 0.65f;

    /// <summary>
    /// The peak alpha of a flash of <paramref name="strength"/> (0..1) under the player's flash
    /// setting <paramref name="comfortFlash"/> (0..1, <c>CombatComfort.ScreenFlash</c>). 0 means
    /// no flash at all.
    /// </summary>
    public static float Peak(float strength, float comfortFlash, bool reducedMotion, bool byPlayer, bool hitsPlayer)
    {
        if (!byPlayer && !hitsPlayer)
        {
            return 0f;
        }

        float peak = Math.Clamp(strength, 0f, 1f) * MaxPeak * Math.Clamp(comfortFlash, 0f, 1f);
        if (!byPlayer)
        {
            peak = Math.Min(peak, EnemyHitCap);
        }

        if (reducedMotion)
        {
            peak = Math.Min(peak, ReducedMotionCap);
        }

        return peak;
    }

    /// <summary>Whether enough time has passed since the last flash.</summary>
    public static bool Allowed(double now, double lastFlash) => now - lastFlash >= MinInterval;

    /// <summary>The flash colour for a school colour: mostly warm white, a little of the school.</summary>
    public static Color Tint(Color school)
    {
        var warm = new Color(1f, 0.96f, 0.88f);
        return new Color(
            Mix(school.R, warm.R),
            Mix(school.G, warm.G),
            Mix(school.B, warm.B));

        static float Mix(float from, float to) => Math.Clamp(from + ((to - from) * WarmWhiteMix), 0f, 1f);
    }

    /// <summary>The smallest shell (largest half extent, metres) the engulf rule applies to. A bolt's
    /// body in the casting hand is smaller than this and is always drawn.</summary>
    public const float EngulfMinHalfExtent = 0.9f;

    /// <summary>Metres outside a shell's surface the camera still counts as inside it (the near
    /// plane and the shader's own near fade live in this band).</summary>
    public const float EngulfMargin = 0.3f;

    /// <summary>
    /// Whether the camera sits inside a body-sized or larger ellipsoid shell (a blast's ball of fire,
    /// a ward or ice shell on the first-person player, a breath's body in the face).
    /// <paramref name="localOffset"/> is the camera's position from the shell's centre in the shell's
    /// own axes, <paramref name="halfExtents"/> its radii. Seen from inside, such a shell is its
    /// colour across the whole screen, which is a screen flash none of the caps above would cover,
    /// so it is left undrawn; the flare, ring and debris of the same effect still are.
    /// </summary>
    public static bool Engulfs(Vector3 localOffset, Vector3 halfExtents)
    {
        float largest = Math.Max(halfExtents.X, Math.Max(halfExtents.Y, halfExtents.Z));
        if (largest < EngulfMinHalfExtent)
        {
            return false;
        }

        float x = localOffset.X / (Math.Max(0.01f, halfExtents.X) + EngulfMargin);
        float y = localOffset.Y / (Math.Max(0.01f, halfExtents.Y) + EngulfMargin);
        float z = localOffset.Z / (Math.Max(0.01f, halfExtents.Z) + EngulfMargin);
        return (x * x) + (y * y) + (z * z) < 1f;
    }

    /// <summary>The flash's alpha <paramref name="age"/> seconds in: a fast rise and a slower fall.</summary>
    public static float Envelope(double age, float peak)
    {
        if (age < 0d || age >= AttackSeconds + DecaySeconds)
        {
            return 0f;
        }

        if (age < AttackSeconds)
        {
            return peak * (float)(age / AttackSeconds);
        }

        float left = 1f - (float)((age - AttackSeconds) / DecaySeconds);
        return peak * left * left;
    }
}
