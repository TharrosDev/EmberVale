using System;
using System.Collections.Generic;
using Embervale.Settings;

namespace Embervale.UI;

/// <summary>Which blows get a floating number under the player's damage-number mode. Pure.</summary>
public static class DamageNumberRules
{
    // The numbers Settings.DamageNumberMode saves (read through SettingsMath.DamageNumberMode).
    public const int Off = 0;
    public const int All = 1;
    public const int OwnBlows = 2;
    public const int CritsAndKills = 3;

    /// <summary>Whether one blow is drawn. <paramref name="critical"/> and <paramref name="kill"/>
    /// only matter for a blow the player dealt: a mode that thins the numbers out never keeps the
    /// ones the player takes.</summary>
    public static bool Shows(int mode, bool byPlayer, bool onPlayer, bool critical, bool kill) => mode switch
    {
        All => byPlayer || onPlayer,
        OwnBlows => byPlayer,
        CritsAndKills => byPlayer && (critical || kill),
        _ => false,
    };
}

/// <summary>What an enemy plate slot is holding, as far as eviction is concerned.</summary>
public readonly record struct PlateClaim(bool Used, bool Aggro, bool Locked, double TouchedAt);

/// <summary>
/// Which enemies carry a world-anchored plate, and which gives its place up when the pool is full.
/// Pure: <see cref="EnemyPlateLayer"/> supplies the clock and the facts.
/// </summary>
public static class EnemyPlateRules
{
    /// <summary>How long a plate stays after the last blow traded with its enemy.</summary>
    public const double LingerSeconds = HudDynamicRules.CombatLingerSeconds;

    /// <summary>Past this distance a plate is not drawn; it fades out over the last stretch.</summary>
    public const float MaxDistance = 36f;
    public const float FadeDistance = 28f;

    /// <summary>A plate is relevant while its enemy is hunting the player, is the locked target, or
    /// traded a blow with the player a moment ago.</summary>
    public static bool Live(double now, double touchedAt, bool aggro, bool locked) =>
        aggro || locked || HudDynamicRules.Lingering(now, touchedAt, LingerSeconds);

    /// <summary>Always and Dynamic are the same thing for a plate, which is only ever up while it is
    /// relevant; Hidden removes them.</summary>
    public static bool Shows(HudElementMode mode) => mode != HudElementMode.Hidden;

    /// <summary>Opacity of a plate <paramref name="distance"/> metres from the camera.</summary>
    public static float Alpha(float distance)
    {
        if (!float.IsFinite(distance) || distance >= MaxDistance)
        {
            return 0f;
        }

        return distance <= FadeDistance ? 1f : 1f - ((distance - FadeDistance) / (MaxDistance - FadeDistance));
    }

    /// <summary>
    /// The slot a new enemy takes: a free one, else the least relevant plate. The newest claim always
    /// gets a slot, because it is the enemy something just happened to. Among the used slots the
    /// locked target is given up last, then enemies still hunting the player, and the stalest goes
    /// first.
    /// </summary>
    public static int SlotFor(ReadOnlySpan<PlateClaim> slots)
    {
        int best = -1;
        int bestRank = int.MaxValue;
        double bestAt = double.PositiveInfinity;
        for (int i = 0; i < slots.Length; i++)
        {
            PlateClaim slot = slots[i];
            if (!slot.Used)
            {
                return i;
            }

            int rank = slot.Locked ? 2 : slot.Aggro ? 1 : 0;
            if (rank < bestRank || (rank == bestRank && slot.TouchedAt < bestAt))
            {
                best = i;
                bestRank = rank;
                bestAt = slot.TouchedAt;
            }
        }

        return best;
    }
}

/// <summary>How long a toast is read for, and which toasts wait out a fight. Pure.</summary>
public static class ToastRules
{
    /// <summary>The shortest a toast holds at full opacity, however few words it has.</summary>
    public const double MinDwellSeconds = 3.0;

    /// <summary>Reading time allowed per word.</summary>
    public const double SecondsPerWord = 0.25;

    /// <summary>Words in <paramref name="text"/>: runs of anything that is not white space.</summary>
    public static int Words(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            bool space = char.IsWhiteSpace(c);
            if (!space && !inWord)
            {
                words++;
            }

            inWord = !space;
        }

        return words;
    }

    /// <summary>Seconds a toast of <paramref name="words"/> words holds: the floor or its reading
    /// time, whichever is longer, times the player's toast-duration setting.</summary>
    public static double Dwell(int words, float multiplier) =>
        Math.Max(MinDwellSeconds, Math.Max(0, words) * SecondsPerWord) * SettingsMath.ClampToastDuration(multiplier);

    public static double Dwell(string text, string? secondary, float multiplier) =>
        Dwell(Words(text) + Words(secondary), multiplier);

    /// <summary>Whether a toast waits: anything that is not critical, while the player is fighting.</summary>
    public static bool Deferred(bool critical, bool inCombat) => inCombat && !critical;
}

/// <summary>
/// The toasts waiting to be shown. In a fight only the critical ones leave, ahead of everything
/// held back; when the fight ends the rest leave in the order they arrived. Pure.
/// </summary>
public sealed class ToastQueue<T>
{
    private readonly List<(T Item, bool Critical)> _items = new();

    public int Count => _items.Count;

    public void Enqueue(T item, bool critical) => _items.Add((item, critical));

    /// <summary>Whether <see cref="TryTake"/> would hand one over.</summary>
    public bool HasPresentable(bool inCombat) => Next(inCombat) >= 0;

    /// <summary>Takes the oldest toast that may be shown now.</summary>
    public bool TryTake(bool inCombat, out T item)
    {
        int index = Next(inCombat);
        if (index < 0)
        {
            item = default!;
            return false;
        }

        item = _items[index].Item;
        _items.RemoveAt(index);
        return true;
    }

    private int Next(bool inCombat)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (!ToastRules.Deferred(_items[i].Critical, inCombat))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>How a spoken line is cut into captions. Pure.</summary>
public static class SubtitleRules
{
    /// <summary>Characters a caption line holds, give or take a word.</summary>
    public const int LineLength = 40;

    /// <summary>Lines on screen at once.</summary>
    public const int MaxLines = 2;

    /// <summary>The shortest one page of a caption stays up.</summary>
    public const double MinPageSeconds = 1.5;

    /// <summary>The shortest a whole line is captioned for.</summary>
    public const double MinSeconds = 2.5;

    /// <summary>Reading time allowed per word when the speaker gives no duration.</summary>
    public const double SecondsPerWord = 0.4;

    /// <summary>The time a line needs when whoever raised it has no clip length to give.</summary>
    public static double Seconds(string text) => Math.Max(MinSeconds, ToastRules.Words(text) * SecondsPerWord);

    /// <summary>
    /// The line as pages of at most <paramref name="maxLines"/> lines of about
    /// <paramref name="lineLength"/> characters, lines joined with a line break. Words are never
    /// split: one longer than a line takes a line to itself.
    /// </summary>
    public static List<string> Pages(string? text, int lineLength = LineLength, int maxLines = MaxLines)
    {
        var pages = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return pages;
        }

        lineLength = Math.Max(1, lineLength);
        maxLines = Math.Max(1, maxLines);

        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > lineLength)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        for (int i = 0; i < lines.Count; i += maxLines)
        {
            pages.Add(string.Join("\n", lines.GetRange(i, Math.Min(maxLines, lines.Count - i))));
        }

        return pages;
    }

    /// <summary>The share of <paramref name="totalSeconds"/> one page gets: by its length, and never
    /// under <see cref="MinPageSeconds"/>.</summary>
    public static double PageSeconds(double totalSeconds, int pageLength, int totalLength) =>
        Math.Max(MinPageSeconds, totalLength > 0 ? totalSeconds * pageLength / totalLength : totalSeconds);

    /// <summary>The type-scale token for the subtitle size setting (0 small, 1 medium, 2 large).</summary>
    public static int FontToken(int size) => SettingsMath.ClampSubtitleSize(size) switch
    {
        0 => UiTheme.BodyFontSize,
        2 => UiTheme.TitleFontSize,
        _ => UiTheme.HeaderFontSize,
    };

    /// <summary>The speaker's name is set one step under the line it belongs to.</summary>
    public static int SpeakerFontToken(int size) => SettingsMath.ClampSubtitleSize(size) switch
    {
        0 => UiTheme.CaptionFontSize,
        2 => UiTheme.HeaderFontSize,
        _ => UiTheme.BodyFontSize,
    };
}
