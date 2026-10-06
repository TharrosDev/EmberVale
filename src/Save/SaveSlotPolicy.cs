using System;
using System.Collections.Generic;

namespace Embervale.Save;

/// <summary>
/// Which slot a player action writes to or reads from (ics save-ui). Pure and Godot-free so the
/// rules are unit-tested rather than rediscovered by losing a save: the pause menu, F5 / F9 and the
/// slot browser all ask here instead of each deciding for itself.
///
/// <para>The rules: the player writes by hand to three fixed manual slots or the quick slot, never
/// to the autosave ring; F5 always writes the quick slot; a one-press manual save writes the session's
/// own slot only when that is a manual slot, and otherwise has no target (the caller prompts), so a
/// session loaded from an autosave or the quick slot picks its slot once; F9 loads whichever of the
/// quick slot and the session's own slot is newer.</para>
/// </summary>
public static class SaveSlotPolicy
{
    /// <summary>How many of <see cref="SaveSlots.Manual"/> the player may save into by hand. The
    /// roster is longer so a save in a later slot stays loadable; only these are offered for writing.</summary>
    public const int PlayerManualSlotCount = 3;

    /// <summary>Active play seconds without a save after which loading or quitting asks first.</summary>
    public const double UnsavedWarningSeconds = 60d;

    private static readonly string[] PlayerManual = BuildPlayerManual();

    /// <summary>The manual slots offered for a new game and for a manual save, in display order.</summary>
    public static IReadOnlyList<string> PlayerManualSlots => PlayerManual;

    /// <summary>The slot F5 writes. Always the quick slot, whatever the session was loaded from.</summary>
    public static string QuickSaveTarget => SaveSlots.Quick;

    /// <summary>Whether a player action may write <paramref name="slot"/>: anything except a blank id
    /// and the autosave ring, which only the autosave cadence rotates.</summary>
    public static bool IsPlayerWritable(string? slot) =>
        !string.IsNullOrWhiteSpace(slot) && SaveSlots.KindOf(slot) != SaveKind.Auto;

    /// <summary>
    /// The slot a one-press manual save writes, or null when the player has to pick one: the
    /// session's own slot when that is a manual slot, and nothing otherwise.
    ///
    /// <para>There is deliberately no fallback that looks for "this character's" manual slot. A
    /// save carries no playthrough identity beyond the character's name, which defaults to the same
    /// word for everyone who leaves it blank, so a match on it once wrote a second playthrough over
    /// the first one's slot with no confirmation. The slot browser the caller opens instead asks
    /// before it overwrites.</para>
    /// </summary>
    public static string? ManualSaveTarget(string? activeSlot) =>
        !string.IsNullOrWhiteSpace(activeSlot) && SaveSlots.KindOf(activeSlot) == SaveKind.Manual ? activeSlot : null;

    /// <summary>
    /// The slot F9 loads, or null when there is nothing to load: the newer of the session's own slot
    /// and the quick slot. The quick slot only counts when it holds <paramref name="characterName"/>'s
    /// save, so a quick save left by another playthrough cannot replace the running character. A tie
    /// goes to the session's own slot.
    /// </summary>
    public static string? QuickLoadTarget(string? activeSlot, string characterName, IEnumerable<SaveSlotInfo> saves)
    {
        SaveSlotInfo? best = null;
        foreach (SaveSlotInfo info in saves)
        {
            if (info.Health != SaveHealth.Ok)
            {
                continue;
            }

            bool isActive = !string.IsNullOrWhiteSpace(activeSlot) && info.Slot == activeSlot;
            bool isQuick = info.Slot == SaveSlots.Quick &&
                           string.Equals(info.CharacterName, characterName, StringComparison.Ordinal);
            if (!isActive && !isQuick)
            {
                continue;
            }

            if (best == null || info.TimestampUnix > best.TimestampUnix ||
                (info.TimestampUnix == best.TimestampUnix && isActive))
            {
                best = info;
            }
        }

        return best?.Slot;
    }

    /// <summary>Whether enough unsaved play has built up that a load or a quit should ask first.</summary>
    public static bool NeedsUnsavedConfirm(double secondsSinceSave) => secondsSinceSave >= UnsavedWarningSeconds;

    /// <summary>The load browser's order: every slot that holds something, newest first. A slot
    /// whose date could not be read (a corrupt file) sorts last; ties keep the order given.</summary>
    public static List<SaveSlotInfo> NewestFirst(IEnumerable<SaveSlotInfo> saves)
    {
        var ordered = new List<SaveSlotInfo>();
        foreach (SaveSlotInfo info in saves)
        {
            if (info.Health != SaveHealth.Missing)
            {
                ordered.Add(info);
            }
        }

        // Insertion sort: at most ten slots, and it is stable, which List.Sort is not.
        for (int i = 1; i < ordered.Count; i++)
        {
            SaveSlotInfo moving = ordered[i];
            int at = i - 1;
            while (at >= 0 && ordered[at].TimestampUnix < moving.TimestampUnix)
            {
                ordered[at + 1] = ordered[at];
                at--;
            }

            ordered[at + 1] = moving;
        }

        return ordered;
    }

    /// <summary>Whole minutes in <paramref name="seconds"/>, for "unsaved for N min" (never negative).</summary>
    public static int WholeMinutes(double seconds) =>
        double.IsFinite(seconds) && seconds > 0d ? (int)Math.Min(seconds / 60d, int.MaxValue) : 0;

    private static string[] BuildPlayerManual()
    {
        var slots = new string[Math.Min(PlayerManualSlotCount, SaveSlots.Manual.Count)];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = SaveSlots.Manual[i];
        }

        return slots;
    }
}
