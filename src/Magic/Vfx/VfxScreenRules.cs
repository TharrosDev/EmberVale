using System;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// How bright, how often and what colour a spell may flash the screen. Pure, because these are the
/// comfort limits and a limit nobody can test is a limit that drifts:
/// the peak never exceeds <see cref="MaxPeak"/> times the player's own flash setting, an enemy's
/// spell flashes only when it struck the player and then no brighter than <see cref="EnemyHitCap"/>,
/// Reduced Motion caps everything at <see cref="ReducedMotionCap"/>, two flashes are never closer
/// than <see cref="MinInterval"/> seconds, and the tint is pulled nearly all the way to white so a
/// coloured full-screen wash never happens. The flash is deliberately slight and brief (the first
/// renders showed anything stronger reads as the whole view being dyed), and only a blast centred
/// on the player earns one (<see cref="PlayerCentred"/>).
/// </summary>
public static class VfxScreenRules
{
    public const float MaxPeak = 0.14f;
    public const float EnemyHitCap = 0.1f;
    public const float ReducedMotionCap = 0.05f;
    public const double MinInterval = 0.3d;

    /// <summary>Seconds the flash takes to reach its peak, and to fade from it.</summary>
    public const float AttackSeconds = 0.04f;

    public const float DecaySeconds = 0.14f;

    /// <summary>How far the school colour is pulled toward warm white.</summary>
    public const float WarmWhiteMix = 0.88f;

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

    /// <summary>How far past a blast's radius the player may stand and still have it flash the screen.</summary>
    public const float CentredMargin = 1.5f;

    /// <summary>
    /// Whether a blast of <paramref name="radius"/> metres is centred on the player closely enough
    /// to flash the screen: the player stands <paramref name="distance"/> metres from its centre. A
    /// screen flash is for the blast the player is inside, not for one across the field.
    /// </summary>
    public static bool PlayerCentred(float distance, float radius) =>
        distance <= Math.Max(0f, radius) + CentredMargin;

    /// <summary>Whether enough time has passed since the last flash.</summary>
    public static bool Allowed(double now, double lastFlash) => now - lastFlash >= MinInterval;

    /// <summary>The flash colour for a school colour: mostly warm white, a little of the school.</summary>
    public static Color Tint(Color school)
    {
        var warm = new Color(1f, 0.98f, 0.94f);
        return new Color(
            Mix(school.R, warm.R),
            Mix(school.G, warm.G),
            Mix(school.B, warm.B));

        static float Mix(float from, float to) => Math.Clamp(from + ((to - from) * WarmWhiteMix), 0f, 1f);
    }

    // --- the edge shimmer ------------------------------------------------------------------------

    /// <summary>The share of the way from the middle of the frame to its edge that the edge
    /// shimmer leaves completely clear.</summary>
    public const float EdgeClear = 0.62f;

    /// <summary>The most the edge shimmer is ever drawn with, at the very edge of the frame.</summary>
    public const float EdgeMaxPeak = 0.3f;

    /// <summary>The same under Reduced Motion.</summary>
    public const float EdgeReducedCap = 0.12f;

    /// <summary>Seconds the shimmer takes to come up.</summary>
    public const float EdgeAttackSeconds = 0.12f;

    /// <summary>How far the school colour of the shimmer is pulled toward white: far less than a
    /// flash, because it never covers the middle of the frame.</summary>
    public const float EdgeWhiteMix = 0.3f;

    /// <summary>
    /// The peak alpha, at the frame's edge, of an edge shimmer of <paramref name="strength"/> (0..1)
    /// under the player's flash setting. The shimmer is what the local player sees of an effect on
    /// their own body in first person (a ward taking a blow, a self-cast), so it is always theirs.
    /// </summary>
    public static float EdgePeak(float strength, float comfortFlash, bool reducedMotion)
    {
        float peak = Math.Clamp(strength, 0f, 1f) * EdgeMaxPeak * Math.Clamp(comfortFlash, 0f, 1f);
        return reducedMotion ? Math.Min(peak, EdgeReducedCap) : peak;
    }

    /// <summary>The shimmer's alpha <paramref name="age"/> seconds into one of
    /// <paramref name="seconds"/>: up quickly, then easing away over the rest.</summary>
    public static float EdgeEnvelope(double age, float peak, float seconds)
    {
        seconds = Math.Max(EdgeAttackSeconds * 2f, seconds);
        if (age < 0d || age >= seconds)
        {
            return 0f;
        }

        if (age < EdgeAttackSeconds)
        {
            return peak * (float)(age / EdgeAttackSeconds);
        }

        float left = 1f - (float)((age - EdgeAttackSeconds) / (seconds - EdgeAttackSeconds));
        return peak * left * left;
    }

    /// <summary>The shimmer's colour for a school colour.</summary>
    public static Color EdgeTint(Color school) => new(
        school.R + ((1f - school.R) * EdgeWhiteMix),
        school.G + ((1f - school.G) * EdgeWhiteMix),
        school.B + ((1f - school.B) * EdgeWhiteMix));

    // --- a ring around the camera ----------------------------------------------------------------

    /// <summary>The least a ring the camera stands in the middle of is drawn with.</summary>
    public const float SelfRingFloor = 0.16f;

    /// <summary>
    /// How strongly a flat ring or disc on the ground is drawn when the camera stands at its centre:
    /// the player's own self-cast in first person (a bark ring, a mending circle, a ward's sigil).
    /// Seen from the middle, a ring a couple of metres out is a bright band across the bottom of the
    /// view, straight through the hotbar. So a ring is cut to <see cref="SelfRingFloor"/> while the
    /// camera is within a metre of its centre line, 0.6 to 2.8 m above it, and its radius is under
    /// about three metres; by four and a half metres out (a nova, well up the view and clear of the
    /// hotbar) it is drawn in full. A camera that is not over the centre (third person, any ring
    /// about somebody else) always gets 1.
    /// </summary>
    /// <param name="horizontal">Metres from the camera to the ring's centre, along the ground.</param>
    /// <param name="height">Metres the camera is above the ring's plane.</param>
    /// <param name="radius">The ring's radius now, metres.</param>
    public static float SelfRing(float horizontal, float height, float radius)
    {
        if (height < 0.6f || height > 2.8f)
        {
            return 1f;
        }

        float centred = 1f - Smooth(0.8f, 2f, horizontal);
        float close = 1f - Smooth(2.8f, 4.5f, radius);
        return 1f - (centred * close * (1f - SelfRingFloor));

        static float Smooth(float from, float to, float value)
        {
            float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
            return t * t * (3f - (2f * t));
        }
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
