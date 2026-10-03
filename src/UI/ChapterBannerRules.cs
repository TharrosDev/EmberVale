using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>
/// The chapter banner's pure rules: which flag records "already shown", where its text comes from, which
/// act a chapter belongs to, and the queue that holds banners back behind cinematics and menus.
/// </summary>
public static class ChapterBannerRules
{
    public const string FlagPrefix = "flag.chapter.";

    /// <summary>The story flag that records a chapter's banner as shown (once per save).</summary>
    public static string FlagFor(string chapterKey) => FlagPrefix + chapterKey;

    /// <summary>Locale keys tried for the title, in order: the main set, then the secret (Pale) set.</summary>
    public static string[] TitleKeys(string chapterKey) =>
        new[] { $"chapter.{chapterKey}.title", $"pale.chapter.{chapterKey}.title" };

    public static string[] SubtitleKeys(string chapterKey) =>
        new[] { $"chapter.{chapterKey}.subtitle", $"pale.chapter.{chapterKey}.subtitle" };

    /// <summary>A banner needs a title to be worth showing; a chapter with no text is skipped silently
    /// rather than drawing a raw key.</summary>
    public static bool ShouldShow(string chapterKey, bool flagHeld, bool hasTitle) =>
        chapterKey.Length > 0 && !flagHeld && hasTitle;

    /// <summary>The act a chapter key names: the first run of digits ("ch.2.frostfang" is 2, "chapter.act1"
    /// is 1). Null when the key carries none.</summary>
    public static int? ActNumber(string chapterKey)
    {
        int start = -1;
        for (int i = 0; i < chapterKey.Length; i++)
        {
            if (char.IsDigit(chapterKey[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                return Parse(chapterKey, start, i);
            }
        }

        return start >= 0 ? Parse(chapterKey, start, chapterKey.Length) : null;
    }

    private static int? Parse(string text, int start, int end) =>
        int.TryParse(text.AsSpan(start, end - start), out int value) && value > 0 ? value : null;

    /// <summary>Roman numeral for a small act number; digits beyond the table fall back to the digits.</summary>
    public static string Roman(int number)
    {
        string[] table = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return number >= 1 && number <= table.Length ? table[number - 1] : number.ToString();
    }
}

/// <summary>
/// FIFO of banners waiting to be shown. A key already queued or on screen is not queued again, so a
/// chapter started twice in quick succession (a quest start and a dialogue banner) shows once.
/// </summary>
public sealed class BannerQueue
{
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _known = new();

    public int Count => _queue.Count;

    public bool Enqueue(string key)
    {
        if (key.Length == 0 || !_known.Add(key))
        {
            return false;
        }

        _queue.Enqueue(key);
        return true;
    }

    public bool TryDequeue(out string key) => _queue.TryDequeue(out key!);

    /// <summary>Marks a key finished, so it may be queued again (a no-op while the flag guards repeats).</summary>
    public void Done(string key) => _known.Remove(key);
}
