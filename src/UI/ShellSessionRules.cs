using System;
using System.Collections.Generic;
using System.Globalization;
using Embervale.Corruption;
using Embervale.Save;

namespace Embervale.UI;

/// <summary>
/// The decisions of the session shell that are not drawing: what a save row says, when the death
/// screen may appear and take input, which painting a narration plays over. Pure and Godot-free,
/// so they are unit-tested (<c>ShellSessionRulesTests</c>) and the screens only lay the answers out.
/// </summary>
public static class ShellSessionRules
{
    // --- Save slots -----------------------------------------------------------

    /// <summary>
    /// The corruption tier a save header names. The header stores the tier's stable label
    /// (<see cref="CorruptionTiers.Label"/>, which is the enum's own name); anything it does not
    /// recognise, including a header that predates the field, reads as untainted.
    /// </summary>
    public static CorruptionTier TierOf(string? label) =>
        Enum.TryParse(label, ignoreCase: true, out CorruptionTier tier) && Enum.IsDefined(tier)
            ? tier
            : CorruptionTier.Untainted;

    /// <summary>How many of the four corruption pips a tier fills: none untainted, all four at Embers.</summary>
    public static int CorruptionPips(CorruptionTier tier) => Math.Clamp((int)tier, 0, CorruptionPipCount);

    /// <summary>Pips on a save row's corruption gauge: one per tier above untainted.</summary>
    public const int CorruptionPipCount = 4;

    /// <summary>A save's play time as whole hours and the minutes left over (never negative).</summary>
    public static (int Hours, int Minutes) Playtime(double seconds)
    {
        int total = double.IsFinite(seconds) && seconds > 0d ? (int)Math.Min(seconds, int.MaxValue) : 0;
        return (total / 3600, (total % 3600) / 60);
    }

    /// <summary>
    /// When a save was written, on the player's own clock: <c>2026-10-06 14:32</c>. The header
    /// stores Unix seconds; the row used to print them as UTC, which is the wrong day for half the
    /// world every evening. Digits only, so it is not language-sensitive.
    /// </summary>
    public static string LocalDate(double unixSeconds, TimeZoneInfo zone)
    {
        long seconds = double.IsFinite(unixSeconds)
            ? (long)Math.Clamp(unixSeconds, 0d, 253402300799d) // 9999-12-31, the last second DateTimeOffset holds
            : 0L;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(seconds), zone);
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether writing into a slot destroys something and so has to be held: any slot
    /// that holds a file, a damaged or newer-format one included (it may be recoverable).</summary>
    public static bool WriteNeedsHold(SaveHealth health) => health != SaveHealth.Missing;

    // --- Death screen ---------------------------------------------------------

    /// <summary>Seconds the death screen is up before it shows its choices or takes a press, so
    /// the button being mashed when the blow landed does not answer it.</summary>
    public const double DeathInputDelaySeconds = 1.5d;

    /// <summary>Whether the death screen has been up long enough to take input.</summary>
    public static bool DeathAcceptsInput(double elapsedSeconds) => elapsedSeconds >= DeathInputDelaySeconds;

    /// <summary>
    /// Whether nobody is at the controls: a headless display, an isolated user folder
    /// (<c>EMBERVALE_USER_DIR</c>, which every probe and capture run sets) or any <c>-- --flag</c>
    /// mode. A pausing modal there would hold a gate still until its timeout, so the death screen
    /// stays out of the way and death is the same-frame respawn it has always been.
    /// </summary>
    public static bool Unattended(string? displayName, string? userDir, int userArgCount) =>
        displayName == "headless" || !string.IsNullOrWhiteSpace(userDir) || userArgCount > 0;

    // --- Narration ------------------------------------------------------------

    /// <summary>The painting behind the Dawnfire ending.</summary>
    public const string DawnfireBackdrop = "res://assets/ui/backgrounds/ending_dawnfire.png";

    /// <summary>The painting behind the Lord of Embers ending.</summary>
    public const string EmbersBackdrop = "res://assets/ui/backgrounds/ending_embers.png";

    /// <summary>Seconds a narration's painting takes to come up out of the black.</summary>
    public const float BackdropFadeInSeconds = 3f;

    /// <summary>
    /// The painting a card script plays over, or null for the plain black field. Only the two
    /// endings have one, and they are told apart by the script itself: <c>EndingSequence.Script</c>
    /// opens with <c>ending.dawnfire.*</c> or <c>ending.embers.*</c> according to the ending flag
    /// the throne set, so the painting can never disagree with the cards on top of it.
    /// </summary>
    public static string? NarrationBackdrop(IReadOnlyList<string> cards)
    {
        if (cards.Count == 0)
        {
            return null;
        }

        if (cards[0].StartsWith("ending.dawnfire.", StringComparison.Ordinal))
        {
            return DawnfireBackdrop;
        }

        return cards[0].StartsWith("ending.embers.", StringComparison.Ordinal) ? EmbersBackdrop : null;
    }

    /// <summary>
    /// How much of the painting shows <paramref name="elapsed"/> seconds into a sequence
    /// <paramref name="duration"/> long: up slowly at the start, and down with the last card
    /// (<paramref name="fadeOutSeconds"/>) so the sequence ends on black as it always has.
    /// </summary>
    public static float BackdropAlpha(float elapsed, float duration, float fadeOutSeconds)
    {
        float rise = Math.Clamp(elapsed / BackdropFadeInSeconds, 0f, 1f);
        float fall = fadeOutSeconds <= 0f ? 1f : Math.Clamp((duration - elapsed) / fadeOutSeconds, 0f, 1f);
        return Math.Min(rise, fall);
    }

    /// <summary>The width a card's text wraps at: its measure, or what a narrow view leaves
    /// between two margins.</summary>
    public static float NarrationWidth(float viewWidth, float measure, float margin) =>
        Math.Max(0f, Math.Min(measure, viewWidth - (margin * 2f)));

    /// <summary>The longest line of a wrapped tooltip, in characters.</summary>
    public const int TooltipLineLength = 56;

    /// <summary>
    /// Breaks <paramref name="text"/> into lines of at most <paramref name="lineLength"/> characters
    /// at its spaces. The engine draws a tooltip as one line however long it is, and a paragraph of
    /// description ran off both sides of the screen. A word longer than a line keeps its own line;
    /// line breaks already in the text are kept.
    /// </summary>
    public static string WrapTooltip(string? text, int lineLength = TooltipLineLength)
    {
        if (string.IsNullOrEmpty(text) || lineLength <= 0 || text.Length <= lineLength)
        {
            return text ?? string.Empty;
        }

        var wrapped = new System.Text.StringBuilder(text.Length + 8);
        foreach (string paragraph in text.Split('\n'))
        {
            if (wrapped.Length > 0)
            {
                wrapped.Append('\n');
            }

            int line = 0;
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line > 0 && line + 1 + word.Length > lineLength)
                {
                    wrapped.Append('\n');
                    line = 0;
                }
                else if (line > 0)
                {
                    wrapped.Append(' ');
                    line++;
                }

                wrapped.Append(word);
                line += word.Length;
            }
        }

        return wrapped.ToString();
    }
}
