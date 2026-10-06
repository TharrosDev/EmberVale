using System;
using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Stats;

namespace Embervale.UI;

/// <summary>The four steps of the character creator, in the order its rail lists them.</summary>
public enum CreatorStep
{
    Race,
    Appearance,
    Background,
    Name,
}

/// <summary>What the creator's preview camera frames.</summary>
public enum PreviewFraming
{
    /// <summary>The whole figure, head to foot.</summary>
    Body,

    /// <summary>Head and shoulders.</summary>
    Face,
}

/// <summary>
/// The character creator's decisions that are not drawing: stepping its rail, what the preview
/// frames, the light rig cycle, the summary's stat nudges, a random look, and the neighbours of a
/// card grid. Pure, so they are unit-tested (<c>CreatorRulesTests</c>).
/// </summary>
public static class CreatorRules
{
    public const int StepCount = 4;

    /// <summary>How many light rigs the preview cycles through.</summary>
    public const int LightRigCount = 3;

    /// <summary>The yaw the preview's figure starts at, in degrees: turned a little, so the face
    /// and one shoulder both read.</summary>
    public const float DefaultYaw = 22f;

    /// <summary>Degrees a second the figure turns with the stick held over.</summary>
    public const float TurnDegreesPerSecond = 150f;

    /// <summary>Degrees the figure turns per pixel the pointer is dragged.</summary>
    public const float TurnDegreesPerPixel = 0.6f;

    /// <summary>The step <paramref name="delta"/> along the rail from <paramref name="current"/>, wrapping at both ends.</summary>
    public static CreatorStep Step(CreatorStep current, int delta) =>
        (CreatorStep)(((((int)current + delta) % StepCount) + StepCount) % StepCount);

    /// <summary>
    /// What the preview frames. Hair and eyes are chosen looking at a face; everything else,
    /// skin and the ember glow included (both cover the whole body), is chosen looking at the figure.
    /// </summary>
    public static PreviewFraming FramingFor(CreatorStep step, AppearanceSlot? slot) =>
        step == CreatorStep.Appearance && slot is AppearanceSlot.Hair or AppearanceSlot.Eyes
            ? PreviewFraming.Face
            : PreviewFraming.Body;

    /// <summary>The rig after <paramref name="rig"/>, wrapping.</summary>
    public static int NextRig(int rig) => (((rig + 1) % LightRigCount) + LightRigCount) % LightRigCount;

    /// <summary>A yaw brought back into (-180, 180], so a figure turned many times round does
    /// not carry an ever-growing angle.</summary>
    public static float WrapYaw(float degrees)
    {
        float wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped <= -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    /// <summary>
    /// The stat nudges of a race and a background together, one entry per stat in the order first
    /// seen. Two nudges on one stat are summed, and a pair that cancels is left out: the summary
    /// says what the character starts with, not how it got there.
    /// </summary>
    public static List<(StatType Stat, float Amount)> CombineDeltas(IEnumerable<(StatType Stat, float Amount)> deltas)
    {
        var combined = new List<(StatType Stat, float Amount)>();
        foreach ((StatType stat, float amount) in deltas)
        {
            int at = combined.FindIndex(entry => entry.Stat == stat);
            if (at < 0)
            {
                combined.Add((stat, amount));
            }
            else
            {
                combined[at] = (stat, combined[at].Amount + amount);
            }
        }

        combined.RemoveAll(entry => Math.Abs(entry.Amount) < 0.0001f);
        return combined;
    }

    /// <summary>One random option index per slot. A slot with nothing to choose from gets -1.</summary>
    public static int[] RandomPicks(IReadOnlyList<int> optionCounts, Random random)
    {
        var picks = new int[optionCounts.Count];
        for (int i = 0; i < picks.Length; i++)
        {
            picks[i] = optionCounts[i] > 0 ? random.Next(optionCounts[i]) : -1;
        }

        return picks;
    }

    /// <summary>
    /// The cell one step from <paramref name="index"/> in a grid of <paramref name="count"/> cells
    /// laid out <paramref name="columns"/> across, or -1 when the step leaves the grid (the caller
    /// then names whatever lies beyond that edge). The last row may be short.
    /// </summary>
    public static int GridNeighbour(int index, int count, int columns, int dx, int dy)
    {
        if (index < 0 || index >= count || columns <= 0)
        {
            return -1;
        }

        int column = (index % columns) + dx;
        if (column < 0 || column >= columns)
        {
            return -1;
        }

        int target = index + dx + (dy * columns);
        return target >= 0 && target < count ? target : -1;
    }
}
