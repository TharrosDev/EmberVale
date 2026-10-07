using System.Collections.Generic;
using Embervale.Magic;

namespace Embervale.UI;

/// <summary>What a spell card's pin button would do if pressed now.</summary>
public enum SpellPinKind
{
    /// <summary>Pins the spell to <see cref="SpellPinChoice.Slot"/>.</summary>
    Pin,

    /// <summary>Clears <see cref="SpellPinChoice.Slot"/>, which holds the spell.</summary>
    Unpin,

    /// <summary>Every slot is taken and none is chosen: the press is refused.</summary>
    Full,
}

/// <summary>A pin button's verb and the favourite slot it acts on (-1 for <see cref="SpellPinKind.Full"/>).</summary>
public readonly record struct SpellPinChoice(SpellPinKind Kind, int Slot);

/// <summary>
/// The spellbook's pin decisions and the HUD spell row's two small ones. Pure: ids and slot
/// numbers only, so which slot a pin lands in is a test and not a habit. The slots themselves are
/// <see cref="SpellFavouritesRules"/>; the wheel draws them clockwise from the top.
/// </summary>
public static class SpellPinRules
{
    /// <summary>No slot chosen.</summary>
    public const int NoSlot = -1;

    /// <summary>The number a slot is shown under: 1 is the wheel's top wedge, counting clockwise.</summary>
    public static int SlotNumber(int slot) => slot + 1;

    /// <summary>
    /// What pinning <paramref name="spellId"/> does. With a slot chosen in the book the spell goes
    /// there, replacing what it holds (or leaves it, if that is the spell already in it). With none
    /// chosen a pinned spell is unpinned and an unpinned one takes the first free slot.
    /// </summary>
    public static SpellPinChoice Decide(IReadOnlyList<string> favourites, string spellId, int chosenSlot)
    {
        if (chosenSlot >= 0 && chosenSlot < favourites.Count)
        {
            return new SpellPinChoice(
                favourites[chosenSlot] == spellId ? SpellPinKind.Unpin : SpellPinKind.Pin, chosenSlot);
        }

        int at = SpellFavouritesRules.IndexOf(favourites, spellId);
        if (at >= 0)
        {
            return new SpellPinChoice(SpellPinKind.Unpin, at);
        }

        int free = SpellFavouritesRules.FirstFree(favourites);
        return free >= 0 ? new SpellPinChoice(SpellPinKind.Pin, free) : new SpellPinChoice(SpellPinKind.Full, NoSlot);
    }

    /// <summary>Whether the HUD names the wheel under the prepared spell: only a caster with a
    /// second spell has anything to hold the key for.</summary>
    public static bool ShowsWheelHint(int spellCount) => spellCount > 1;

    /// <summary>The spell a tap swaps back to, as the HUD ghosts it: the previous spell, or none
    /// when there is no previous spell or it is the one already prepared.</summary>
    public static string Ghost(string? previousId, string? selectedId) =>
        string.IsNullOrEmpty(previousId) || previousId == selectedId ? SpellFavouritesRules.None : previousId;
}
