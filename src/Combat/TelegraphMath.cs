using System;

namespace Embervale.Combat;

/// <summary>
/// The pure curve behind the wind-up telegraph (Phase 36C), kept Godot-free so the thing a player
/// actually reads mid-fight is unit-testable — the same idiom as <see cref="ShakeMath"/> and
/// <see cref="Enemies.BossPhases"/>.
/// </summary>
public static class TelegraphMath
{
    /// <summary>
    /// Ring size at <paramref name="t"/> through the wind-up, as a fraction of full radius.
    ///
    /// Deliberately <b>eased out</b>: it opens fast and closes on its final size slowly, so most of
    /// the visible growth happens early and the last moments before the blow read as "about to
    /// land" rather than "still growing". A linear ring gives the player no sense of when the window
    /// actually closes, which is the one thing it exists to communicate.
    /// </summary>
    public static float RingScale(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        float inverse = 1f - x;
        return 1f - (inverse * inverse);
    }

    /// <summary>
    /// Ring opacity at <paramref name="t"/>. Rises across the wind-up so the warning is at its most
    /// insistent at the moment it matters — the frame before the hitbox opens.
    /// </summary>
    public static float RingAlpha(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return 0.25f + (0.55f * x);
    }
}
