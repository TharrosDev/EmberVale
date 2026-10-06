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

    /// <summary>Frost resistance while worn (Phase 35G). Until this, the 34E resistance family was
    /// authorable on an <see cref="Stats.AttributeSet"/> only — enemies could shrug off a school and
    /// the player could not, so no piece of gear could answer the Reach's cold. The other five
    /// resistances are one export and one line below each, when something wants them.</summary>
    [Export] public float BonusFrostResist { get; set; }

    // The other five schools and max mana (ics-base). Absent-default 0, appended after every older
    // bonus so StatBonuses() keeps yielding the original eight in their original order.
    [Export] public float BonusFireResist { get; set; }
    [Export] public float BonusLightningResist { get; set; }
    [Export] public float BonusArcaneResist { get; set; }
    [Export] public float BonusNatureResist { get; set; }
    [Export] public float BonusNecroticResist { get; set; }

    /// <summary>Flat max mana while equipped (the mana twin of <see cref="BonusMaxHealth"/>).</summary>
    [Export] public float BonusMana { get; set; }

    /// <summary>
    /// Flat mana and stamina regeneration per second while equipped. ⚠️ <b>Not in
    /// <see cref="StatBonuses"/>:</b> regeneration is a rate on <c>StatsComponent</c>
    /// (<c>ManaRegen</c> / <c>StaminaRegen</c>), not a <see cref="StatType"/>, so there is no
    /// modifier to yield. Whoever applies equipment adds these to the wearer's rates on equip and
    /// takes them off on unequip.
    /// </summary>
    [Export] public float BonusManaRegen { get; set; }

    /// <summary>See <see cref="BonusManaRegen"/>.</summary>
    [Export] public float BonusStaminaRegen { get; set; }

    /// <summary>True for a weapon that fills both hands: equipping it empties the off hand, and an
    /// off-hand item cannot be equipped beside it.</summary>
    [Export] public bool TwoHanded { get; set; }

    /// <summary>The weapon family (a shield is <see cref="Items.WeaponClass.Shield"/>);
    /// <see cref="Items.WeaponClass.None"/> for armour, accessories and legacy items.</summary>
    [Export] public WeaponClass WeaponClass { get; set; } = WeaponClass.None;

    /// <summary>The armour line (cloth, leather, plate) of a body-armour piece;
    /// <see cref="Items.ArmorWeight.None"/> for everything else and for legacy items.</summary>
    [Export] public ArmorWeight ArmorWeight { get; set; } = ArmorWeight.None;

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
        if (BonusFrostResist != 0f) yield return (StatType.FrostResist, BonusFrostResist);
        if (BonusFireResist != 0f) yield return (StatType.FireResist, BonusFireResist);
        if (BonusLightningResist != 0f) yield return (StatType.LightningResist, BonusLightningResist);
        if (BonusArcaneResist != 0f) yield return (StatType.ArcaneResist, BonusArcaneResist);
        if (BonusNatureResist != 0f) yield return (StatType.NatureResist, BonusNatureResist);
        if (BonusNecroticResist != 0f) yield return (StatType.NecroticResist, BonusNecroticResist);
        if (BonusMana != 0f) yield return (StatType.Mana, BonusMana);
    }
}
