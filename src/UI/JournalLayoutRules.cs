using System;

namespace Embervale.UI;

/// <summary>How a stage-log line is marked. A shape, never a colour alone.</summary>
public enum ObjectiveMark
{
    /// <summary>Finished: a tick.</summary>
    Done,

    /// <summary>Can no longer be done: a cross.</summary>
    Failed,

    /// <summary>The step to do now: a filled diamond.</summary>
    Current,

    /// <summary>A live optional step: a hollow diamond.</summary>
    Optional,

    /// <summary>Not open yet: a padlock.</summary>
    Locked,
}

/// <summary>
/// The measurements and marks the knowledge screens (journal, bestiary, dialogue) share. Pure, so
/// the reading measure and the mark for each objective state are pinned by a test.
/// </summary>
public static class JournalLayoutRules
{
    /// <summary>The longest line prose is set to, in characters.</summary>
    public const int ProseChars = 80;

    /// <summary>Average advance of the book serif's running text, in ems.</summary>
    public const float SerifAdvance = 0.44f;

    /// <summary>The width that holds <paramref name="chars"/> characters of prose at a font size.</summary>
    public static float ProseWidth(int fontPx, int chars = ProseChars) => fontPx * SerifAdvance * chars;

    /// <summary>The right-hand inset that brings a prose column of <paramref name="available"/>
    /// width back to the reading measure. Zero when the column is already narrower.</summary>
    public static int ProseInset(float available, int fontPx) =>
        (int)Math.Max(0f, available - ProseWidth(fontPx));

    /// <summary>The index column of a list and detail screen: a third of the usable width, held
    /// between a width that still fits a title and one that leaves the page its measure.</summary>
    public static float IndexWidth(float usableWidth) => Math.Clamp(usableWidth * 0.32f, 240f, 360f);

    /// <summary>The mark a stage-log line carries.</summary>
    public static ObjectiveMark MarkOf(StageKind kind) => kind switch
    {
        StageKind.Done or StageKind.OptionalDone => ObjectiveMark.Done,
        StageKind.Missed => ObjectiveMark.Failed,
        StageKind.Current => ObjectiveMark.Current,
        StageKind.Optional => ObjectiveMark.Optional,
        _ => ObjectiveMark.Locked,
    };
}
