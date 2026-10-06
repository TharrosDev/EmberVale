using System;
using System.Collections.Generic;
using Embervale.Items;

namespace Embervale.UI;

/// <summary>
/// The damage chunk of a <see cref="JuicedBar"/>: the pale length left behind when the value drops.
/// The fill itself is always the true value; the chunk is where it was a moment ago. It holds for a
/// beat so a hit can be read, then slides down to meet the fill. Pure.
/// </summary>
public static class JuicedBarRules
{
    /// <summary>Seconds the chunk stays put after a drop before it starts to close.</summary>
    public const double LagHoldSeconds = 0.35;

    /// <summary>Normalized units the chunk closes per second.</summary>
    public const double LagDrainPerSecond = 0.9;

    /// <summary>The chunk's trailing edge after a drop from <paramref name="shownBefore"/>: the
    /// further of where it already was and where the fill was, so a second hit extends a chunk in
    /// flight instead of restarting it shorter.</summary>
    public static double OnDrop(double lag, double shownBefore) => Math.Max(lag, shownBefore);

    /// <summary>One frame of the chunk: wait out the hold, then close on <paramref name="value"/>.
    /// A chunk at or under the value is gone (a heal swallows it).</summary>
    public static (double Lag, double Hold) Step(double lag, double hold, double value, double delta)
    {
        if (lag <= value)
        {
            return (value, 0d);
        }

        delta = Math.Max(0d, delta);
        if (hold > 0d)
        {
            return (lag, Math.Max(0d, hold - delta));
        }

        return (Math.Max(value, lag - (delta * LagDrainPerSecond)), 0d);
    }

    /// <summary>Whether there is no chunk left to draw or animate.</summary>
    public static bool Settled(double lag, double value) => lag <= value + 1e-6;
}

/// <summary>What one hotbar cell is showing. Each state has its own shape, not only its own tint.</summary>
public enum HotbarSlotState
{
    /// <summary>Nothing assigned.</summary>
    Empty,

    /// <summary>Pressing the key uses it.</summary>
    Ready,

    /// <summary>Waiting out its cooldown: the wipe and, near the end, the seconds.</summary>
    Cooling,

    /// <summary>Nothing for it to do right now: the pool it restores is full, or nothing to cure.</summary>
    Unusable,

    /// <summary>The player is below the level it needs: a padlock.</summary>
    Locked,

    /// <summary>Assigned, and none left in the pack.</summary>
    Depleted,
}

/// <summary>The hotbar's decisions that are not drawing. Pure.</summary>
public static class HotbarRules
{
    /// <summary>A cooldown shows its seconds only this close to the end; before that the wipe alone.</summary>
    public const int NumeralSeconds = 9;

    /// <summary>How many distinct wedges the cooldown wipe is drawn at; it repaints when it crosses one.</summary>
    public const int WipeSteps = 96;

    /// <summary>The whole seconds to print over a cooling cell, or 0 for none.</summary>
    public static int Numeral(float remaining)
    {
        if (!(remaining > 0f))
        {
            return 0;
        }

        int seconds = (int)MathF.Ceiling(remaining);
        return seconds <= NumeralSeconds ? seconds : 0;
    }

    /// <summary>The wipe's step for a 1-to-0 cooldown fraction.</summary>
    public static int WipeStep(float fraction) =>
        float.IsFinite(fraction) ? (int)MathF.Ceiling(Math.Clamp(fraction, 0f, 1f) * WipeSteps) : 0;

    /// <summary>
    /// The state of a cell. Order matters: an empty pack beats everything, a level lock beats a
    /// cooldown (it will still be locked when the wait ends), and a running cooldown beats "nothing
    /// to do", because the wait is the thing with a clock on it.
    /// </summary>
    public static HotbarSlotState State(bool filled, int count, float cooldownRemaining, ConsumeRefusal refusal)
    {
        if (!filled)
        {
            return HotbarSlotState.Empty;
        }

        if (count <= 0)
        {
            return HotbarSlotState.Depleted;
        }

        if (refusal == ConsumeRefusal.LevelTooLow)
        {
            return HotbarSlotState.Locked;
        }

        if (cooldownRemaining > 0f || refusal == ConsumeRefusal.OnCooldown)
        {
            return HotbarSlotState.Cooling;
        }

        return refusal == ConsumeRefusal.None ? HotbarSlotState.Ready : HotbarSlotState.Unusable;
    }
}

/// <summary>
/// Which objectives the HUD tracker draws when a quest has more than fit. The journal lists them
/// all; the tracker is a glance, so it keeps <see cref="MaxLines"/> and names the rest as a count.
/// Pure.
/// </summary>
public static class TrackerFoldRules
{
    public const int MaxLines = 3;

    /// <summary>
    /// The objectives to draw, as indices in authored order. Kept in this order of claim: the current
    /// objective, the other live required ones, live optional ones, the steps still locked (nearest
    /// first), and last the finished ones (most recent first). Objectives outside the player's branch
    /// are never drawn and never counted.
    /// </summary>
    public static List<int> Visible(IReadOnlyList<ObjectiveState> states, int current, int max = MaxLines)
    {
        var picked = new List<int>();
        if (max <= 0)
        {
            return picked;
        }

        for (int rank = 0; rank <= 4 && picked.Count < max; rank++)
        {
            // Finished steps are taken latest first: the one just ticked off is the one worth a line.
            bool reverse = rank == 4;
            for (int n = 0; n < states.Count && picked.Count < max; n++)
            {
                ObjectiveState state = states[reverse ? states.Count - 1 - n : n];
                if (state.InBranch && Rank(state, current) == rank)
                {
                    picked.Add(state.Index);
                }
            }
        }

        picked.Sort();
        return picked;
    }

    /// <summary>How many in-branch objectives are folded away.</summary>
    public static int Hidden(IReadOnlyList<ObjectiveState> states, int max = MaxLines)
    {
        int inBranch = 0;
        foreach (ObjectiveState state in states)
        {
            if (state.InBranch)
            {
                inBranch++;
            }
        }

        return Math.Max(0, inBranch - Math.Max(0, max));
    }

    private static int Rank(ObjectiveState state, int current)
    {
        if (state.Index == current)
        {
            return 0;
        }

        if (state.Complete)
        {
            return 4;
        }

        if (!state.Active)
        {
            return 3;
        }

        return state.Optional ? 2 : 1;
    }
}

/// <summary>The vitals' thresholds, shared by the bar's tick marks and the state that reads them. Pure.</summary>
public static class VitalsRules
{
    /// <summary>Health fraction at or below which the bar starts asking for attention.</summary>
    public const float LowHealth = 0.30f;

    /// <summary>...and at or below which it insists.</summary>
    public const float CriticalHealth = 0.15f;

    /// <summary>0 healthy, 1 low, 2 critical.</summary>
    public static int HealthBand(float fraction) =>
        fraction > LowHealth ? 0 : fraction <= CriticalHealth ? 2 : 1;
}

/// <summary>
/// When a HUD element's content has changed enough to bring a Dynamic element back up
/// (<c>GameHud.MarkChanged</c>). Walking in a straight line is not news; turning round, or the
/// thing being pointed at changing, is. Pure.
/// </summary>
public static class HudChangeRules
{
    /// <summary>A turn of this much since the compass last came up brings it up again (20 degrees).</summary>
    public const float HeadingStep = MathF.PI / 9f;

    /// <summary>A destination that has moved this far is a different place, not the same one seen
    /// from a step further on.</summary>
    public const float TargetStepMetres = 8f;

    public static bool HeadingMoved(float markedHeading, float heading) =>
        MathF.Abs(CompassMath.WrapPi(heading - markedHeading)) >= HeadingStep;

    /// <summary>Whether a destination appeared, went away or jumped. Positions are on the ground plane.</summary>
    public static bool TargetMoved(bool hadBefore, float beforeX, float beforeZ, bool hasNow, float nowX, float nowZ)
    {
        if (hadBefore != hasNow)
        {
            return true;
        }

        if (!hasNow)
        {
            return false;
        }

        float dx = nowX - beforeX;
        float dz = nowZ - beforeZ;
        return (dx * dx) + (dz * dz) >= TargetStepMetres * TargetStepMetres;
    }
}

/// <summary>How the interaction prompt lays out its phrase. Pure.</summary>
public static class PromptRules
{
    /// <summary>
    /// Splits a prompt into what to do and what to do it to, when the phrase ends with the name of
    /// the thing aimed at ("Loot" + "Iron chest"). The phrase is one localized string and its word
    /// order is the translator's, so anything else (the name in the middle, a count after it, a
    /// phrase that does not use the name) comes back whole with no noun.
    /// </summary>
    public static (string Verb, string Noun) Split(string prompt, string? noun)
    {
        if (string.IsNullOrEmpty(noun) || prompt.Length <= noun.Length ||
            !prompt.EndsWith(noun, StringComparison.Ordinal) ||
            !char.IsWhiteSpace(prompt[prompt.Length - noun.Length - 1]))
        {
            return (prompt, string.Empty);
        }

        string verb = prompt[..^noun.Length].TrimEnd();
        return verb.Length > 0 ? (verb, noun) : (prompt, string.Empty);
    }
}
