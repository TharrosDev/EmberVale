using System;

namespace Embervale.UI;

/// <summary>
/// The pure decisions of the shell's front: whether a person is at the machine, whether this is
/// their first launch, which painting the title wears, and how the credits roll.
/// </summary>
public static class ShellFrontRules
{
    /// <summary>The last act with a title painting.</summary>
    public const int LastAct = 4;

    /// <summary>
    /// Whether a player, and not a tool, is running the game: a real display, the ordinary user
    /// folder and no arguments after <c>--</c>. The boot splash and first-run setup both wait for
    /// a button, so neither may appear when this is false: every gate, probe, capture and
    /// <c>--play</c> run passes an argument or isolates its user folder.
    /// </summary>
    public static bool Attended(bool headless, bool userDirSet, int userArgCount) =>
        !headless && !userDirSet && userArgCount == 0;

    /// <summary>
    /// Whether this launch is the first on this machine: no saves, and a settings file that is
    /// missing or was written since the process started. The settings service writes the file
    /// during a first boot (its detected graphics preset), so by the time a screen can ask, "no
    /// file" has become "a file younger than this process".
    /// </summary>
    public static bool FirstRun(
        bool attended, int saveCount, bool settingsFileExists, double settingsModifiedUnix, double processStartUnix) =>
        attended && saveCount == 0 &&
        (!settingsFileExists || settingsModifiedUnix >= Math.Floor(processStartUnix) - 1d);

    // --- Title painting -----------------------------------------------------

    private const string CompleteFlag = "\"flag.game_complete\"";

    /// <summary>
    /// The furthest act a save has reached, 1 to <see cref="LastAct"/>, read from the text of the
    /// save file: the highest act among the chapter flags it holds
    /// (<see cref="ChapterBannerRules.FlagPrefix"/>), or the last act for a finished game. A save
    /// header carries no chapter, and the flags are plain strings in the file, so this scans for
    /// them instead of parsing a document that can run to megabytes. Anything unreadable is act 1.
    /// </summary>
    public static int ActFromSaveText(string? saveText)
    {
        if (string.IsNullOrEmpty(saveText))
        {
            return 1;
        }

        if (saveText.Contains(CompleteFlag, StringComparison.Ordinal))
        {
            return LastAct;
        }

        int act = 1;
        int at = 0;
        string prefix = "\"" + ChapterBannerRules.FlagPrefix;
        while ((at = saveText.IndexOf(prefix, at, StringComparison.Ordinal)) >= 0)
        {
            at += prefix.Length;
            int end = saveText.IndexOf('"', at);
            if (end < 0)
            {
                break;
            }

            if (ChapterBannerRules.ActNumber(saveText.Substring(at, end - at)) is { } found)
            {
                act = Math.Max(act, found);
            }

            at = end;
        }

        return Math.Min(act, LastAct);
    }

    /// <summary>The title painting for an act: the causeway for the first (and for anything out
    /// of range), then one per act.</summary>
    public static string TitlePainting(int act) =>
        act >= 2 && act <= LastAct ? $"title_act{act}" : UiTheme.GenericPainting;

    /// <summary>Whole hours and minutes of a playtime, for a save's one-line summary.</summary>
    public static (int Hours, int Minutes) Playtime(double seconds)
    {
        long total = (long)Math.Max(0d, seconds);
        return ((int)(total / 3600), (int)(total % 3600 / 60));
    }

    // --- Credits roll -------------------------------------------------------

    /// <summary>How fast the credits climb, in px a second, and how much faster while held.</summary>
    public const float CreditsSpeed = 34f;
    public const float CreditsFastMultiplier = 6f;

    /// <summary>Where the roll starts: its first line a little under the middle of the view.</summary>
    public static float CreditsStart(float viewHeight) => viewHeight * 0.42f;

    /// <summary>The offset at which the last line has left the top of the view.</summary>
    public static float CreditsEnd(float contentHeight, float viewHeight) => contentHeight + viewHeight;

    /// <summary>
    /// The roll's offset after <paramref name="delta"/> seconds. It climbs by itself unless
    /// <paramref name="auto"/> is off (reduced motion, where the player moves it), faster while
    /// <paramref name="fast"/> is held; <paramref name="manual"/> is the player's own push, -1 to 1.
    /// Never before the start or past the end.
    /// </summary>
    public static float CreditsAdvance(
        float offset, float delta, bool auto, bool fast, float manual, float start, float end)
    {
        float speed = (auto ? CreditsSpeed * (fast ? CreditsFastMultiplier : 1f) : 0f)
                      + (manual * CreditsSpeed * CreditsFastMultiplier);
        return Math.Clamp(offset + (speed * delta), Math.Min(start, end), end);
    }
}
