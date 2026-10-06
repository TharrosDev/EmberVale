namespace Embervale.Save;

/// <summary>
/// How a save was made, recorded in its header (<see cref="SaveSlotInfo.Kind"/>) so the slot browser
/// can group and label it. A header that predates the field answers by its slot name
/// (<see cref="SaveSlots.KindOf"/>).
/// </summary>
// APPEND ONLY: ordinals persist in save headers — never reorder/insert/remove (EnumStabilityTests).
public enum SaveKind
{
    /// <summary>Written by the player into a slot they chose.</summary>
    Manual,

    /// <summary>The quick-save slot (F5).</summary>
    Quick,

    /// <summary>Written by the autosave cadence into its rotating ring.</summary>
    Auto,
}
