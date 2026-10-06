namespace Embervale.Items;

/// <summary>
/// The family a wielded item belongs to, authored on <see cref="EquippableItemResource.WeaponClass"/>.
/// It is what affix pools, perks, set bonuses and tooltips key on when "a weapon" is too coarse:
/// a dagger and a greatsword share a slot and nothing else. <see cref="None"/> is the absent-default,
/// so every item authored before the field existed stays classless rather than being guessed at.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum WeaponClass
{
    None,
    Sword,
    Dagger,
    Axe,
    Mace,
    Spear,
    Staff,
    Greatsword,
    Bow,
    Shield,
}
