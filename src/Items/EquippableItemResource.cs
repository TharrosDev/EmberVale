using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// An <see cref="ItemResource"/> that can be worn or wielded. It declares its
/// <see cref="Slot"/>, the flat stat bonuses it grants while equipped, and (for
/// weapon slots) the <see cref="WeaponResource"/> it swaps in.
///
/// Flat bonus fields (rather than an arbitrary modifier list) keep the `.tres`
/// simple and authorable without sub-resources; Phase 7 loot layers rolled,
/// per-instance affixes on top of this template.
/// </summary>
[GlobalClass]
public partial class EquippableItemResource : ItemResource
{
    [Export] public EquipmentSlot Slot { get; set; } = EquipmentSlot.MainHand;

    /// <summary>For weapon slots: the weapon swapped onto the wielder while equipped.</summary>
    [Export] public WeaponResource? Weapon { get; set; }

    [ExportGroup("Stat Bonuses (flat, while equipped)")]
    [Export] public float BonusArmor { get; set; }
    [Export] public float BonusPhysicalPower { get; set; }
    [Export] public float BonusSpellPower { get; set; }
    [Export] public float BonusMaxHealth { get; set; }
    [Export] public float BonusMaxStamina { get; set; }
    [Export] public float BonusCritChance { get; set; }
    [Export] public float BonusMoveSpeed { get; set; }

    /// <summary>Enumerates the non-zero stat bonuses as (stat, value) pairs.</summary>
    public IEnumerable<(StatType Stat, float Value)> StatBonuses()
    {
        if (BonusArmor != 0f) yield return (StatType.Armor, BonusArmor);
        if (BonusPhysicalPower != 0f) yield return (StatType.PhysicalPower, BonusPhysicalPower);
        if (BonusSpellPower != 0f) yield return (StatType.SpellPower, BonusSpellPower);
        if (BonusMaxHealth != 0f) yield return (StatType.Health, BonusMaxHealth);
        if (BonusMaxStamina != 0f) yield return (StatType.Stamina, BonusMaxStamina);
        if (BonusCritChance != 0f) yield return (StatType.CritChance, BonusCritChance);
        if (BonusMoveSpeed != 0f) yield return (StatType.MoveSpeed, BonusMoveSpeed);
    }
}
