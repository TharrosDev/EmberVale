using System;

namespace Embervale.Combat;

/// <summary>How one floating number looks: tint, size multiplier, whether it is italic (a blow the
/// defender shrugged off), the locale key of a word that goes with it, and whether the number is shown
/// at all (a parry has none — nothing was dealt).</summary>
public readonly record struct NumberStyle(
    float R, float G, float B, float Scale, bool Italic, string? WordKey, bool ShowNumber);

/// <summary>
/// Pure rules for floating damage numbers: what each outcome looks like, how the text is composed,
/// how a number rises and fades, and when two land close enough to merge into one. Godot-free so the
/// look table and the motion curve are unit-tested; <c>DamageNumberLayer</c> draws them.
/// </summary>
public static class DamageNumberMath
{
    public const float LifeSeconds = 0.9f;
    public const float CritLifeSeconds = 1.25f;

    /// <summary>Two numbers on the same target with the same outcome closer than this merge into one
    /// running total, so a burn tick or a multi-hit does not carpet the screen.</summary>
    public const float MergeWindow = 0.28f;

    /// <summary>How far a number rises over its life, in pixels.</summary>
    public const float RisePixels = 70f;

    /// <summary>The damage a number of full size stands for; bigger blows swell up to
    /// <see cref="MaxSize"/>.</summary>
    public const float ReferenceDamage = 30f;

    public const float MinSize = 0.85f;
    public const float MaxSize = 1.7f;

    /// <summary>The damage number for an amount: whole points, never below 1 for a blow that did
    /// something.</summary>
    public static string Text(float amount) =>
        amount <= 0f ? "0" : Math.Max(1, (int)MathF.Round(amount)).ToString();

    /// <summary>What an outcome looks like. <paramref name="taken"/>: the player took it, which turns
    /// an ordinary hit red instead of bone-pale, so the two directions never read as the same.</summary>
    public static NumberStyle Style(HitOutcome outcome, bool taken) => outcome switch
    {
        HitOutcome.Critical => new NumberStyle(1.0f, 0.72f, 0.28f, 1.35f, false, null, true),
        HitOutcome.Blocked => new NumberStyle(0.60f, 0.66f, 0.72f, 0.85f, false, null, true),
        HitOutcome.Parried => new NumberStyle(1.0f, 0.90f, 0.55f, 1.25f, false, "combat.feedback.num_parried", false),
        HitOutcome.GuardBroken => new NumberStyle(0.82f, 0.35f, 0.30f, 1.3f, false, "combat.feedback.num_guard_broken", true),
        HitOutcome.PoiseBroken => new NumberStyle(0.95f, 0.62f, 0.30f, 1.15f, false, "combat.feedback.num_broken", true),
        HitOutcome.Resisted => new NumberStyle(0.55f, 0.57f, 0.66f, 0.8f, true, "combat.feedback.num_resisted", true),
        _ => taken
            ? new NumberStyle(0.86f, 0.36f, 0.30f, 1f, false, null, true)
            : new NumberStyle(0.94f, 0.90f, 0.80f, 1f, false, null, true),
    };

    /// <summary>The label text: the number (in parentheses for a block, with a bang for a crit), then
    /// the localised word if the outcome has one.</summary>
    public static string Compose(HitOutcome outcome, float amount, string? localisedWord)
    {
        string number = outcome switch
        {
            HitOutcome.Critical => Text(amount) + "!",
            HitOutcome.Blocked => "(" + Text(amount) + ")",
            _ => Text(amount),
        };

        bool showNumber = Style(outcome, false).ShowNumber;
        if (string.IsNullOrEmpty(localisedWord))
        {
            return showNumber ? number : string.Empty;
        }

        return showNumber ? number + " " + localisedWord : localisedWord;
    }

    /// <summary>The size multiplier for an amount: a scratch is small, a blow near the reference is
    /// full, a bigger one swells.</summary>
    public static float SizeScale(float amount)
    {
        float t = Math.Clamp(amount / ReferenceDamage, 0f, 1.5f);
        return Math.Clamp(MinSize + ((MaxSize - MinSize) * MathF.Sqrt(t / 1.5f)), MinSize, MaxSize);
    }

    /// <summary>How long a number lives.</summary>
    public static float Life(HitOutcome outcome) =>
        outcome is HitOutcome.Critical or HitOutcome.GuardBroken or HitOutcome.Parried
            ? CritLifeSeconds
            : LifeSeconds;

    /// <summary>Progress (0..1) up its path at <paramref name="t"/> through its life: fast off the
    /// mark, slowing to a hover.</summary>
    public static float Rise(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        float inverse = 1f - x;
        return 1f - (inverse * inverse * inverse);
    }

    /// <summary>Opacity at <paramref name="t"/>: solid for the first 55% of its life, then out.</summary>
    public static float Alpha(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x < 0.55f ? 1f : 1f - ((x - 0.55f) / 0.45f);
    }

    /// <summary>A pop on the first frames: it lands at 1.35x and settles to 1x over the first 12% of its
    /// life, which is what makes a number read as struck rather than placed.</summary>
    public static float Pop(float t)
    {
        float x = Math.Clamp(t / 0.12f, 0f, 1f);
        return 1f + (0.35f * (1f - x) * (1f - x));
    }

    /// <summary>A stable sideways offset in -1..1 for a number, from a seed (its target and time), so
    /// numbers on the same target fan out instead of stacking.</summary>
    public static float Drift(ulong seed)
    {
        ulong h = (seed * 0x9E3779B97F4A7C15UL) >> 40;
        return ((h % 2001UL) / 1000f) - 1f;
    }

    /// <summary>Whether a new number should fold into the one already on screen for the same
    /// target.</summary>
    public static bool ShouldMerge(float existingAgeSeconds, bool sameTarget, HitOutcome existing, HitOutcome incoming) =>
        sameTarget && existing == incoming && existingAgeSeconds < MergeWindow &&
        incoming is HitOutcome.Hit or HitOutcome.Blocked or HitOutcome.Resisted;
}
