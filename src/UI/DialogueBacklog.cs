using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>Whether a backlog row is something an NPC said or the choice the player took.</summary>
public enum BacklogKind
{
    Line,
    Choice,
}

public readonly record struct BacklogEntry(BacklogKind Kind, string Text);

/// <summary>
/// The conversation so far, for the dialogue panel's history toggle: the node texts shown and the choices
/// taken, for the current conversation only. <see cref="Recent"/> returns the last few NPC lines with the
/// choices that sit between them, so a player who clicked through can re-read what was just said.
/// </summary>
public sealed class DialogueBacklog
{
    /// <summary>NPC lines shown in the history view.</summary>
    public const int RecentLines = 3;

    private readonly List<BacklogEntry> _entries = new();

    // Choices taken in this conversation, by the panel's own key for a choice.
    private readonly HashSet<string> _chosen = new();

    public int Count => _entries.Count;

    public void Clear()
    {
        _entries.Clear();
        _chosen.Clear();
    }

    /// <summary>Remembers that a choice was taken, so the panel can mark it as already asked if the
    /// conversation comes back round to it.</summary>
    public void MarkChosen(string choiceKey) => _chosen.Add(choiceKey);

    public bool WasChosen(string choiceKey) => _chosen.Contains(choiceKey);

    /// <summary>Records a node's text. The same text twice in a row (a rebuild of the same node) is one line.</summary>
    public void AddLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_entries.Count > 0 && _entries[^1].Kind == BacklogKind.Line && _entries[^1].Text == text)
        {
            return;
        }

        _entries.Add(new BacklogEntry(BacklogKind.Line, text));
    }

    public void AddChoice(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _entries.Add(new BacklogEntry(BacklogKind.Choice, text));
        }
    }

    /// <summary>The entries from the start of the last <see cref="RecentLines"/> NPC lines to the end,
    /// excluding the current (most recent) line, which the panel is already showing.</summary>
    public List<BacklogEntry> Recent()
    {
        int lastLine = -1;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Kind == BacklogKind.Line)
            {
                lastLine = i;
                break;
            }
        }

        var result = new List<BacklogEntry>();
        if (lastLine <= 0)
        {
            return result;
        }

        int seen = 0;
        int start = 0;
        for (int i = lastLine - 1; i >= 0; i--)
        {
            if (_entries[i].Kind == BacklogKind.Line && ++seen == RecentLines)
            {
                start = i;
                break;
            }
        }

        for (int i = start; i < lastLine; i++)
        {
            result.Add(_entries[i]);
        }

        return result;
    }
}

/// <summary>Finds the active Talk objective a conversation fulfils, so the panel can say why the player
/// is talking to this person.</summary>
public static class DialogueQuestContext
{
    /// <summary>One objective of the tracked quest, reduced to what the match needs.</summary>
    public readonly record struct TalkCandidate(int Index, bool IsTalk, string TargetId, bool Live);

    /// <summary>The index of the live Talk objective whose target is <paramref name="dialogueId"/>, or -1.
    /// Required objectives are tried before optional ones by the caller's ordering.</summary>
    public static int Find(string dialogueId, IReadOnlyList<TalkCandidate> candidates)
    {
        if (string.IsNullOrEmpty(dialogueId))
        {
            return -1;
        }

        foreach (TalkCandidate candidate in candidates)
        {
            if (candidate.IsTalk && candidate.Live && candidate.TargetId == dialogueId)
            {
                return candidate.Index;
            }
        }

        return -1;
    }
}
