using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>What a dialogue option is, as the glyph before it says.</summary>
public enum OptionMark
{
    /// <summary>Ordinary talk: no glyph.</summary>
    None,

    /// <summary>Moves the story: starts a quest or sets a story flag. A filled diamond.</summary>
    Plot,

    /// <summary>Carries another consequence (standing, corruption, an item, a companion). A hollow diamond.</summary>
    Special,

    /// <summary>Already asked in this conversation. A tick.</summary>
    Exhausted,

    /// <summary>Ends the conversation with no consequence. A dash.</summary>
    Leave,
}

/// <summary>
/// The pace and the marks of the dialogue window: how long a line takes to write itself out, how
/// many options show before the list scrolls, and which glyph an option carries. Pure.
/// </summary>
public static class DialoguePaceRules
{
    /// <summary>Characters written per second. A sentence of eighty characters takes under two seconds.</summary>
    public const float CharsPerSecond = 48f;

    /// <summary>Options visible before the list scrolls.</summary>
    public const int VisibleOptions = 5;

    /// <summary>How long a line of <paramref name="characters"/> takes to write out.</summary>
    public static float Seconds(int characters) => Math.Max(0, characters) / CharsPerSecond;

    /// <summary>How long after a line finishes by itself a choosing press is ignored. A real pause, not
    /// motion: it does not shorten under reduced motion.</summary>
    public const ulong GraceMs = 300;

    /// <summary>Whether a press at <paramref name="nowMs"/> lands too soon after the line finished on
    /// its own at <paramref name="finishedMs"/> (0 = it did not) to have been meant for an option.</summary>
    public static bool InGrace(ulong nowMs, ulong finishedMs) =>
        finishedMs != 0 && nowMs >= finishedMs && nowMs - finishedMs < GraceMs;

    /// <summary>Rows the option list is sized for.</summary>
    public static int OptionRows(int options) => Math.Clamp(options, 1, VisibleOptions);

    /// <summary>The mark for an option. Having been asked already outranks what it would do: the
    /// consequence has been spent.</summary>
    public static OptionMark MarkOf(IReadOnlyList<ConsequenceTag>? tags, bool chosenBefore, bool ends)
    {
        if (chosenBefore)
        {
            return OptionMark.Exhausted;
        }

        bool special = false;
        if (tags != null)
        {
            foreach (ConsequenceTag tag in tags)
            {
                if (tag.Kind is ConsequenceKind.Quest or ConsequenceKind.Story)
                {
                    return OptionMark.Plot;
                }

                special = true;
            }
        }

        return special ? OptionMark.Special : ends ? OptionMark.Leave : OptionMark.None;
    }
}
