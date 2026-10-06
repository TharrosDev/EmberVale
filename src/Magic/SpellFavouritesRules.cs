using System.Collections.Generic;

namespace Embervale.Magic;

/// <summary>
/// The bookkeeping behind a caster's favourite spells: eight slots, each a spell id or empty, plus
/// the one "previous spell" the selection remembers. Pure (ids only), so the rules the spell wheel
/// and the save read are tests and not habits: a spell sits in at most one slot, a learned spell
/// takes the first free one, and a restore never keeps an id the caster does not know.
/// </summary>
public static class SpellFavouritesRules
{
    /// <summary>How many favourite slots a caster has. The wheel's inner ring draws exactly these.</summary>
    public const int SlotCount = 8;

    /// <summary>What an unpinned slot holds, and what "no previous spell" is.</summary>
    public const string None = "";

    /// <summary>Eight empty slots.</summary>
    public static string[] Empty()
    {
        var slots = new string[SlotCount];
        System.Array.Fill(slots, None);
        return slots;
    }

    /// <summary>The slots a caster has before anyone chose any: the first eight known spells, in the
    /// order they are known. Also what a save from before favourites existed restores to.</summary>
    public static string[] Default(IReadOnlyList<string> known)
    {
        string[] slots = Empty();
        for (int i = 0; i < known.Count; i++)
        {
            Pin(slots, known[i]);
        }

        return slots;
    }

    /// <summary>The slot holding <paramref name="id"/>, or -1.</summary>
    public static int IndexOf(IReadOnlyList<string> slots, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return -1;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The first empty slot, or -1 when all eight are taken.</summary>
    public static int FirstFree(IReadOnlyList<string> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Pins <paramref name="id"/> into the first free slot. False when it is already pinned,
    /// is empty, or there is no room: a ninth spell is known but not on the wheel's inner ring.</summary>
    public static bool Pin(string[] slots, string id)
    {
        if (string.IsNullOrEmpty(id) || IndexOf(slots, id) >= 0)
        {
            return false;
        }

        int free = FirstFree(slots);
        if (free < 0)
        {
            return false;
        }

        slots[free] = id;
        return true;
    }

    /// <summary>
    /// Puts <paramref name="id"/> in <paramref name="slot"/>; an empty id clears the slot. A spell
    /// already pinned elsewhere moves, and what was in the target slot takes the slot it left, so
    /// rearranging never loses a pin. False when nothing changed or the slot does not exist.
    /// </summary>
    public static bool Set(string[] slots, int slot, string id)
    {
        if (slot < 0 || slot >= slots.Length)
        {
            return false;
        }

        id ??= None;
        if (slots[slot] == id)
        {
            return false;
        }

        int from = IndexOf(slots, id);
        if (from >= 0)
        {
            slots[from] = slots[slot];
        }

        slots[slot] = id;
        return true;
    }

    /// <summary>Empties the slot holding <paramref name="id"/>. False when it was not pinned.</summary>
    public static bool Clear(string[] slots, string id)
    {
        int at = IndexOf(slots, id);
        if (at < 0)
        {
            return false;
        }

        slots[at] = None;
        return true;
    }

    /// <summary>
    /// The slots a save restores. <paramref name="saved"/> is null when the save has no favourites
    /// (it predates them): the answer is <see cref="Default"/>. Otherwise each saved id keeps its
    /// slot, except an id the caster does not know or a second copy of one, which leave the slot
    /// empty. A list of the wrong length is padded or cut, never thrown on.
    /// </summary>
    public static string[] Restore(IReadOnlyList<string>? saved, IReadOnlyList<string> known)
    {
        if (saved == null)
        {
            return Default(known);
        }

        string[] slots = Empty();
        for (int i = 0; i < saved.Count && i < SlotCount; i++)
        {
            string id = saved[i] ?? None;
            if (id.Length > 0 && Contains(known, id) && IndexOf(slots, id) < 0)
            {
                slots[i] = id;
            }
        }

        return slots;
    }

    /// <summary>What "previous" is once the selection moves from <paramref name="current"/> to
    /// <paramref name="next"/>: the spell just left. Re-selecting the same spell keeps it.</summary>
    public static string PreviousAfterSelect(string previous, string current, string next) =>
        next == current ? previous : current ?? None;

    /// <summary>The previous spell a save restores: the saved id when the caster knows it and it is
    /// not the selected one, else none.</summary>
    public static string RestorePrevious(string? saved, string selected, IReadOnlyList<string> known) =>
        !string.IsNullOrEmpty(saved) && saved != selected && Contains(known, saved) ? saved : None;

    private static bool Contains(IReadOnlyList<string> known, string id)
    {
        for (int i = 0; i < known.Count; i++)
        {
            if (known[i] == id)
            {
                return true;
            }
        }

        return false;
    }
}
