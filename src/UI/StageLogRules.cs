using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>How the journal draws one objective of the selected quest.</summary>
public enum StageKind
{
    /// <summary>Met. Shows the journal-entry line with a tick.</summary>
    Done,

    /// <summary>A live required objective. Shows its hint.</summary>
    Current,

    /// <summary>A live optional objective. Shown with an "Optional" chip.</summary>
    Optional,

    /// <summary>An optional objective that was met.</summary>
    OptionalDone,

    /// <summary>In the player's branch but gated behind an earlier step.</summary>
    Locked,

    /// <summary>Unmet in a quest that is no longer active (completed or failed).</summary>
    Missed,
}

public readonly record struct StageLine(int Index, StageKind Kind);

/// <summary>
/// Which objectives the journal's detail pane lists, and in what order. Out-of-branch objectives are
/// never listed (a fork shows only the path taken). The order is a reading order: what is done first,
/// then what to do now, then what is optional, then what is still locked.
/// </summary>
public static class StageLogRules
{
    public static List<StageLine> Build(IReadOnlyList<ObjectiveState> states, bool questActive)
    {
        var done = new List<StageLine>();
        var current = new List<StageLine>();
        var optional = new List<StageLine>();
        var locked = new List<StageLine>();

        foreach (ObjectiveState state in states)
        {
            if (!state.InBranch)
            {
                continue;
            }

            if (state.Complete)
            {
                done.Add(new StageLine(state.Index, state.Optional ? StageKind.OptionalDone : StageKind.Done));
            }
            else if (!questActive)
            {
                if (state.Optional)
                {
                    optional.Add(new StageLine(state.Index, StageKind.Optional));
                }
                else
                {
                    locked.Add(new StageLine(state.Index, StageKind.Missed));
                }
            }
            else if (!state.Active)
            {
                locked.Add(new StageLine(state.Index, StageKind.Locked));
            }
            else if (state.Optional)
            {
                optional.Add(new StageLine(state.Index, StageKind.Optional));
            }
            else
            {
                current.Add(new StageLine(state.Index, StageKind.Current));
            }
        }

        var all = new List<StageLine>(done.Count + current.Count + optional.Count + locked.Count);
        all.AddRange(done);
        all.AddRange(current);
        all.AddRange(optional);
        all.AddRange(locked);
        return all;
    }

    /// <summary>The key a stage line is drawn from: the journal entry when authored and resolvable, else the
    /// objective's own description.</summary>
    public static string LogTextKey(string journalEntryKey, string description, Func<string, bool> resolves) =>
        journalEntryKey.Length > 0 && resolves(journalEntryKey) ? journalEntryKey : description;

    /// <summary>Whether a line carries a hint row: live objectives with an authored, resolvable hint.</summary>
    public static bool ShowsHint(StageKind kind, string hintKey, Func<string, bool> resolves) =>
        kind is StageKind.Current or StageKind.Optional && hintKey.Length > 0 && resolves(hintKey);
}
