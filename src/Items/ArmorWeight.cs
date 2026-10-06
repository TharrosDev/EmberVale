namespace Embervale.Items;

/// <summary>
/// The line a piece of body armour belongs to, authored on
/// <see cref="EquippableItemResource.ArmorWeight"/>: cloth, leather or plate. It is a classification
/// for affix pools, perks, set rules and tooltips, not a burden (there is no encumbrance).
/// <see cref="None"/> is the absent-default: weapons, shields, accessories and legacy items.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ArmorWeight
{
    None,
    Light,
    Medium,
    Heavy,
}
