using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Quests;

namespace Embervale.UI;

/// <summary>The journal's index tabs. Order is display order.</summary>
public enum JournalSection
{
    Main,
    Errands,
    Completed,
    Failed,
}

/// <summary>One quest as the journal's index sees it. <paramref name="Sequence"/> is the quest's position in
/// the log's enumeration order (oldest first); it stands in for "when it started" because the log keeps no
/// completion time.</summary>
public readonly record struct JournalEntry(
    string Id, QuestStatus Status, bool IsMain, bool IsLedger, string ChapterKey, int OrderInAct, int Sequence);

/// <summary>A run of main-thread quests under one chapter heading. An empty key is the key-less group.</summary>
public sealed record JournalGroup(string ChapterKey, IReadOnlyList<JournalEntry> Entries);

/// <summary>
/// The journal index's pure decisions: which tabs exist, how the Main tab groups into chapters, what the
/// Completed list shows and what it folds away. Godot-free so the ordering can be pinned by tests.
/// </summary>
public static class JournalIndexRules
{
    /// <summary>Completed quests listed before the rest fold behind a "show more" row.</summary>
    public const int CompletedVisible = 5;

    public static JournalSection SectionOf(JournalEntry entry) => entry.Status switch
    {
        QuestStatus.Completed => JournalSection.Completed,
        QuestStatus.Failed => JournalSection.Failed,
        _ => entry.IsMain || entry.IsLedger ? JournalSection.Main : JournalSection.Errands,
    };

    /// <summary>The sections that have something in them, in display order. A section arrives with its
    /// state, never before it (UI_STYLE 37.5E).</summary>
    public static List<JournalSection> Sections(IEnumerable<JournalEntry> entries)
    {
        var present = new HashSet<JournalSection>();
        foreach (JournalEntry entry in entries)
        {
            present.Add(SectionOf(entry));
        }

        return Enum.GetValues<JournalSection>().Where(present.Contains).ToList();
    }

    /// <summary>The tab to open on: the selected quest's, else the tracked quest's, else the first present.</summary>
    public static JournalSection? DefaultSection(
        IReadOnlyList<JournalEntry> entries, string? selectedId, string? trackedId)
    {
        foreach (string? id in new[] { selectedId, trackedId })
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            foreach (JournalEntry entry in entries)
            {
                if (entry.Id == id)
                {
                    return SectionOf(entry);
                }
            }
        }

        List<JournalSection> sections = Sections(entries);
        return sections.Count > 0 ? sections[0] : null;
    }

    /// <summary>The live main-thread quests (ledger umbrellas excluded) grouped by chapter. Chapters order by
    /// their earliest quest's <c>OrderInAct</c>, then by key; quests order by <c>OrderInAct</c>, then
    /// by id. A quest with no chapter key forms the key-less group.</summary>
    public static List<JournalGroup> MainGroups(IEnumerable<JournalEntry> entries)
    {
        List<JournalEntry> live = entries
            .Where(e => e.Status == QuestStatus.Active && e.IsMain && !e.IsLedger)
            .ToList();

        return live
            .GroupBy(e => e.ChapterKey ?? string.Empty)
            .Select(g => new JournalGroup(
                g.Key,
                g.OrderBy(e => e.OrderInAct).ThenBy(e => e.Id, StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Entries.Min(e => e.OrderInAct))
            .ThenBy(g => g.ChapterKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when at least one main-thread group carries a chapter key, i.e. headings say something.</summary>
    public static bool NeedsChapterHeadings(IReadOnlyList<JournalGroup> groups) =>
        groups.Any(g => g.ChapterKey.Length > 0);

    /// <summary>Live non-main, non-ledger quests, oldest first.</summary>
    public static List<JournalEntry> Errands(IEnumerable<JournalEntry> entries) => entries
        .Where(e => e.Status == QuestStatus.Active && !e.IsMain && !e.IsLedger)
        .OrderBy(e => e.Sequence)
        .ToList();

    /// <summary>Live ledger quests: shown, collapsed, never tracked.</summary>
    public static List<JournalEntry> Ledger(IEnumerable<JournalEntry> entries) => entries
        .Where(e => e.Status == QuestStatus.Active && e.IsLedger)
        .OrderBy(e => e.OrderInAct)
        .ThenBy(e => e.Sequence)
        .ToList();

    /// <summary>Completed quests newest first. When not <paramref name="showAll"/>, only the first
    /// <see cref="CompletedVisible"/> come back and <paramref name="hidden"/> counts the rest.</summary>
    public static List<JournalEntry> Completed(IEnumerable<JournalEntry> entries, bool showAll, out int hidden)
    {
        List<JournalEntry> all = entries
            .Where(e => e.Status == QuestStatus.Completed)
            .OrderByDescending(e => e.Sequence)
            .ToList();

        if (showAll || all.Count <= CompletedVisible)
        {
            hidden = 0;
            return all;
        }

        hidden = all.Count - CompletedVisible;
        return all.Take(CompletedVisible).ToList();
    }

    /// <summary>Failed quests newest first.</summary>
    public static List<JournalEntry> Failed(IEnumerable<JournalEntry> entries) => entries
        .Where(e => e.Status == QuestStatus.Failed)
        .OrderByDescending(e => e.Sequence)
        .ToList();

    /// <summary>The next tab when stepping by <paramref name="delta"/> (wraps). Returns the input when
    /// there is nothing to step to.</summary>
    public static JournalSection Step(IReadOnlyList<JournalSection> sections, JournalSection current, int delta)
    {
        if (sections.Count == 0)
        {
            return current;
        }

        int at = -1;
        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i] == current)
            {
                at = i;
            }
        }

        int next = at < 0 ? 0 : (at + delta) % sections.Count;
        if (next < 0)
        {
            next += sections.Count;
        }

        return sections[next];
    }

    /// <summary>Locale keys to try, in order, for a chapter's heading: the main set, then the secret
    /// (Pale) set. The key-less group has no candidates.</summary>
    public static string[] ChapterTitleKeys(string chapterKey) => chapterKey.Length == 0
        ? Array.Empty<string>()
        : new[] { $"chapter.{chapterKey}.title", $"pale.chapter.{chapterKey}.title" };

    /// <summary>The first candidate key that resolves, or null.</summary>
    public static string? FirstResolving(IEnumerable<string> candidates, Func<string, bool> resolves)
    {
        foreach (string key in candidates)
        {
            if (resolves(key))
            {
                return key;
            }
        }

        return null;
    }
}
