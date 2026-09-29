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

    /// <summary>A beat added to a parry window so the cue lights just before the guard has to go up,
    /// not at the last instant: roughly a fast human reaction.</summary>
    public const float ParryReactionLead = 0.08f;

    /// <summary>
    /// Where in the wind-up (0..1) the "raise your guard now" cue lights. A block raised inside the
    /// parry window before contact parries, so the cue opens that window plus a reaction beat before
    /// the blow, but never in the first half of a wind-up (a very short one would light at once) and
    /// never after 95% (a long one would light too late to act on). Derived from the real wind-up,
    /// never a constant.
    /// </summary>
    public static float ParryCueStart(float windupSeconds, float parryWindow)
    {
        if (windupSeconds <= 0f)
        {
            return 1f;
        }

        float lead = Math.Max(parryWindow, 0f) + ParryReactionLead;
        return Math.Clamp(1f - (lead / windupSeconds), 0.5f, 0.95f);
    }

    /// <summary>Whether <paramref name="t"/> through the wind-up is inside the parry cue.</summary>
    public static bool InParryCue(float t, float windupSeconds, float parryWindow) =>
        t >= ParryCueStart(windupSeconds, parryWindow);

    /// <summary>Scale of the closing timing ring of a <see cref="TelegraphClass.Parryable"/> blow, as a
    /// fraction of the outer ring: it starts on the outer edge and closes to a quarter of it as the
    /// blow lands, so the moment it meets the centre is the moment of contact.</summary>
    public static float TimingRingScale(float t) => 1f - (0.75f * Math.Clamp(t, 0f, 1f));

    /// <summary>Opacity multiplier a class adds on top of <see cref="RingAlpha"/>: an unblockable pulses
    /// (a warning that will not sit still), everything else is steady.</summary>
    public static float ClassPulse(TelegraphClass cls, float seconds)
    {
        if (cls != TelegraphClass.Unblockable)
        {
            return 1f;
        }

        return 0.8f + (0.2f * MathF.Sin(seconds * 22f));
    }
}
