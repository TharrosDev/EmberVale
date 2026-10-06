namespace Embervale.Items;

/// <summary>
/// What a <see cref="ConsumableItemResource"/> does when used. <see cref="Heal"/> is ordinal 0 on
/// purpose: it is the absent-default, so every consumable authored before the field existed is still
/// a heal driven by its legacy <see cref="ConsumableItemResource.HealAmount"/>.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ConsumableEffectKind
{
    /// <summary>Restores health by the magnitude (or the legacy heal amount when the magnitude is 0).</summary>
    Heal,

    /// <summary>Restores stamina by the magnitude.</summary>
    RestoreStamina,

    /// <summary>Restores mana by the magnitude.</summary>
    RestoreMana,

    /// <summary>Adds a timed stat modifier (buff stat, buff kind, magnitude, duration).</summary>
    Buff,

    /// <summary>Removes harmful status effects (the listed ids, or every one when the list is empty).</summary>
    Cure,
}
