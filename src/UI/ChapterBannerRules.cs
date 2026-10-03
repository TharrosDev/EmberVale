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

/// <summary>One sampled moment of the banner's life.</summary>
public readonly record struct BannerFrame(float Alpha, float Rise, bool Finished);

/// <summary>
/// The banner's timeline: fade in, hold, fade out. Reduced motion removes the rise (movement) and keeps the
/// fade (it is how the banner appears and leaves at all), so the fade durations are deliberately NOT routed
/// through <c>UiTheme.Duration</c>, which collapses to zero under that setting.
/// </summary>
public static class BannerTimeline
{
    public const float FadeIn = 0.7f;
    public const float Hold = 3.4f;
    public const float FadeOut = 0.9f;
    public const float Total = FadeIn + Hold + FadeOut;

    /// <summary>Pixels the text rises while fading in, when motion is enabled.</summary>
    public const float RisePixels = 14f;

    public static BannerFrame At(float elapsed, bool motion)
    {
        if (elapsed >= Total)
        {
            return new BannerFrame(0f, 0f, true);
        }

        float rise = motion ? RisePixels * (1f - UiMotion.EaseOut(UiMotion.Progress(elapsed, FadeIn))) : 0f;
        if (elapsed < FadeIn)
        {
            return new BannerFrame(UiMotion.EaseOut(UiMotion.Progress(elapsed, FadeIn)), rise, false);
        }

        if (elapsed < FadeIn + Hold)
        {
            return new BannerFrame(1f, 0f, false);
        }

        return new BannerFrame(1f - UiMotion.EaseIn(UiMotion.Progress(elapsed - FadeIn - Hold, FadeOut)), 0f, false);
    }
}
