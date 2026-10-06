using System;
using System.Collections.Generic;

namespace Embervale.Save;

/// <summary>
/// The slot ids the game writes to, and what kind of save each one is. Pure and Godot-free, so the
/// rosters are unit-testable and <see cref="SaveSlotInfo"/> can answer its own
/// <see cref="SaveSlotInfo.Kind"/> without the <see cref="SaveManager"/> node. Slots are only
/// directory names; nothing stops a save in a slot outside these rosters (a dev or probe slot), and
/// such a slot simply reads as <see cref="SaveKind.Manual"/>.
/// </summary>
public static class SaveSlots
{
    /// <summary>The reserved quick-save slot (F5 / F9). Not part of <see cref="Manual"/>.</summary>
    public const string Quick = "quick";

    private static readonly string[] ManualRoster = { "slot1", "slot2", "slot3", "slot4", "slot5", "slot6" };

    /// <summary>The autosave ring as an array, shared with <see cref="AutosaveService.RingSlots"/> so
    /// the two can never disagree.</summary>
    internal static readonly string[] AutoRing = { "auto1", "auto2", "auto3" };

    /// <summary>The slots the player saves into by hand, in display order. The first three are the
    /// roster that predates this list, so existing manual saves keep their place.</summary>
    public static IReadOnlyList<string> Manual => ManualRoster;

    /// <summary>The rotating autosave ring, oldest overwritten.</summary>
    public static IReadOnlyList<string> Auto => AutoRing;

    /// <summary>What a slot id is by convention: <see cref="Quick"/> is Quick, a member of
    /// <see cref="Auto"/> is Auto, and everything else (unknown ids included) is Manual.</summary>
    public static SaveKind KindOf(string slot)
    {
        if (slot == Quick)
        {
            return SaveKind.Quick;
        }

        return Array.IndexOf(AutoRing, slot) >= 0 ? SaveKind.Auto : SaveKind.Manual;
    }
}
