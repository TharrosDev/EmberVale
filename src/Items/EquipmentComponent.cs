using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Manages what an entity has equipped. Equipping pulls a specific
/// <see cref="ItemInstance"/> from the <see cref="InventoryComponent"/>, applies its
/// combined stat bonuses (template flats + rolled affixes) to the
/// <see cref="StatsComponent"/> as <see cref="StatModifier"/>s sourced to the
/// instance (so they're removed cleanly on unequip), and — for weapon slots —
/// swaps the active <see cref="WeaponResource"/> on the
/// <see cref="MeleeWeaponComponent"/>. Unequipping reverses all of that and returns
/// the instance (with its affixes intact) to the inventory.
///
/// Persists the full equipped instance per slot via <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class EquipmentComponent : EntityComponent, ISaveable
{
    private readonly Dictionary<EquipmentSlot, ItemInstance> _equipped = new();

    private StatsComponent? _stats;
    private InventoryComponent? _inventory;
    private MeleeWeaponComponent? _weapon;
    private WeaponResource? _defaultWeapon;

    public string SaveId => SaveKey("equipment");

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _inventory = Entity.GetComponent<InventoryComponent>();
        _weapon = Entity.GetComponent<MeleeWeaponComponent>();
        _defaultWeapon = _weapon?.Weapon;
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public ItemInstance? GetEquipped(EquipmentSlot slot)
    {
        return _equipped.TryGetValue(slot, out ItemInstance? item) ? item : null;
    }

    public bool IsEquipped(EquipmentSlot slot) => _equipped.ContainsKey(slot);

    /// <summary>Every currently-equipped instance — for UIs that list equipped gear (e.g. salvage).</summary>
    public IEnumerable<ItemInstance> EquippedInstances => _equipped.Values;

    /// <summary>True if <paramref name="instance"/> is the exact item equipped in some slot.</summary>
    public bool IsInstanceEquipped(ItemInstance instance)
    {
        foreach (ItemInstance equipped in _equipped.Values)
        {
            if (ReferenceEquals(equipped, instance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first equipped instance of <paramref name="itemId"/>, or null — lets the hotbar
    /// re-activate a weapon that's already worn (not in the bag).</summary>
    public ItemInstance? FirstEquippedInstanceOf(string itemId)
    {
        foreach (ItemInstance equipped in _equipped.Values)
        {
            if (equipped.TemplateId == itemId)
            {
                return equipped;
            }
        }

        return null;
    }

    /// <summary>Unequips a specific instance (whichever slot holds it), returning it to the inventory.
    /// Returns true if it was equipped.</summary>
    public bool UnequipInstance(ItemInstance instance)
    {
        foreach (KeyValuePair<EquipmentSlot, ItemInstance> pair in _equipped)
        {
            if (ReferenceEquals(pair.Value, instance))
            {
                return Unequip(pair.Key);
            }
        }

        return false;
    }

    /// <summary>Equips a specific instance taken from the inventory. Returns false
    /// if it isn't equippable or isn't present in the inventory.</summary>
    public bool Equip(ItemInstance instance)
    {
        if (instance?.Equippable is not { } equippable || equippable.Slot == EquipmentSlot.None)
        {
            return false;
        }

        if (_inventory == null || _inventory.RemoveOneInstance(instance) == null)
        {
            return false;
        }

        EquipInternal(instance, equippable.Slot, returnOldToInventory: true);
        return true;
    }

    /// <summary>Makes an <b>already-equipped</b> weapon the active one (swaps the
    /// <see cref="MeleeWeaponComponent"/>'s weapon) without unequipping anything — lets the hotbar
    /// switch between two equipped weapons (e.g. sword ↔ off-hand bow). Returns false if the instance
    /// isn't equipped or carries no weapon.</summary>
    public bool ActivateWeapon(ItemInstance instance)
    {
        if (!IsInstanceEquipped(instance) || instance.Equippable?.Weapon is not { } weapon || _weapon == null)
        {
            return false;
        }

        _weapon.Weapon = weapon;
        NotifyChanged();
        return true;
    }

    /// <summary>Unequips the item in a slot, returning it to the inventory.</summary>
    public bool Unequip(EquipmentSlot slot)
    {
        if (!_equipped.Remove(slot, out ItemInstance? instance))
        {
            return false;
        }

        RemoveBonuses(instance);
        RestoreWeapon(instance);
        _inventory?.AddInstance(instance, 1);
        NotifyChanged();
        return true;
    }

    private void EquipInternal(ItemInstance instance, EquipmentSlot slot, bool returnOldToInventory)
    {
        if (_equipped.TryGetValue(slot, out ItemInstance? old))
        {
            RemoveBonuses(old);
            RestoreWeapon(old);
            if (returnOldToInventory)
            {
                _inventory?.AddInstance(old, 1);
            }
        }

        ApplyBonuses(instance);
        ApplyWeapon(instance);
        _equipped[slot] = instance;
        NotifyChanged();
    }

    private void ApplyBonuses(ItemInstance instance)
    {
        if (_stats == null)
        {
            return;
        }

        foreach ((StatType stat, float value, ModifierType type) in instance.StatBonuses())
        {
            _stats.GetStat(stat).AddModifier(new StatModifier(value, type, instance));
        }
    }

    private void RemoveBonuses(ItemInstance instance)
    {
        if (_stats == null)
        {
            return;
        }

        foreach ((StatType stat, float _, ModifierType _) in instance.StatBonuses())
        {
            _stats.GetStat(stat).RemoveModifiersFromSource(instance);
        }
    }

    private void ApplyWeapon(ItemInstance instance)
    {
        if (instance.Equippable?.Weapon is { } weapon && _weapon != null)
        {
            _weapon.Weapon = weapon;
        }
    }

    private void RestoreWeapon(ItemInstance instance)
    {
        if (instance.Equippable?.Weapon != null && _weapon != null)
        {
            _weapon.Weapon = _defaultWeapon;
        }
    }

    private void NotifyChanged()
    {
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new EquipmentChangedEvent(Entity));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var slots = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<EquipmentSlot, ItemInstance> pair in _equipped)
        {
            slots[(int)pair.Key] = pair.Value.Save();
        }

        return new Godot.Collections.Dictionary { ["slots"] = slots };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        foreach (ItemInstance instance in _equipped.Values)
        {
            RemoveBonuses(instance);
            RestoreWeapon(instance);
        }

        _equipped.Clear();

        if (data.TryGetValue("slots", out Variant slotsVariant))
        {
            var slots = slotsVariant.AsGodotDictionary();
            foreach (Variant key in slots.Keys)
            {
                ItemInstance? instance = ItemInstance.FromSave(slots[key].AsGodotDictionary());
                if (instance?.Equippable is { } equippable)
                {
                    EquipInternal(instance, equippable.Slot, returnOldToInventory: false);
                }
            }
        }

        NotifyChanged();
    }
}
