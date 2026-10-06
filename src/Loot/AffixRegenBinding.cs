using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Stats;

namespace Embervale.Loot;

/// <summary>
/// Applies the regeneration affixes on a character's worn gear to its <see cref="StatsComponent"/>
/// rates. Regeneration is a rate, not a <see cref="StatType"/>, so it cannot ride the stat-modifier
/// path every other affix uses; this is the one place a regen affix takes effect.
///
/// It re-totals on every <see cref="EquipmentChangedEvent"/> for its owner (equip, unequip and the
/// restore at the end of a load all publish one) and moves each rate by the difference from what
/// it last applied, so it composes with anything else that adjusts the same rates.
/// </summary>
public sealed class AffixRegenBinding
{
    private readonly IEntity _owner;
    private float _health;
    private float _stamina;
    private float _mana;
    private bool _bound;

    public AffixRegenBinding(IEntity owner)
    {
        _owner = owner;
    }

    /// <summary>Sums one regen effect across a set of worn items. Pure.</summary>
    public static float Total(IEnumerable<ItemInstance> worn, AffixEffect effect)
    {
        float total = 0f;
        foreach (ItemInstance instance in worn)
        {
            foreach (ItemAffix affix in instance.Affixes)
            {
                if (affix.Effect == effect)
                {
                    total += affix.Magnitude;
                }
            }
        }

        return total;
    }

    public void Bind()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        EventBus.Instance?.Subscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        Refresh();
    }

    /// <summary>Stops listening. It does not hand the rates back: this is called while the owner is
    /// leaving the tree, and its stats component is going with it.</summary>
    public void Release()
    {
        if (!_bound)
        {
            return;
        }

        _bound = false;
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
    }

    private void OnEquipmentChanged(EquipmentChangedEvent e)
    {
        if (ReferenceEquals(e.Owner, _owner))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_owner.GetComponent<EquipmentComponent>() is not { } equipment)
        {
            return;
        }

        var worn = new List<ItemInstance>(equipment.EquippedInstances);
        Apply(
            Total(worn, AffixEffect.HealthRegen),
            Total(worn, AffixEffect.StaminaRegen),
            Total(worn, AffixEffect.ManaRegen));
    }

    private void Apply(float health, float stamina, float mana)
    {
        if (_owner.GetComponent<StatsComponent>() is not { } stats)
        {
            return;
        }

        stats.HealthRegen += health - _health;
        stats.StaminaRegen += stamina - _stamina;
        stats.ManaRegen += mana - _mana;
        _health = health;
        _stamina = stamina;
        _mana = mana;
    }
}
