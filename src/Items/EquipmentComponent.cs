using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Manages what an entity has equipped. Equipping pulls the item from the
/// <see cref="InventoryComponent"/>, applies its flat stat bonuses to the
/// <see cref="StatsComponent"/> as <see cref="StatModifier"/>s sourced to the item
/// (so they're removed cleanly on unequip), and — for weapon slots — swaps the
/// active <see cref="WeaponResource"/> on the <see cref="MeleeWeaponComponent"/>.
/// Unequipping reverses all of that and returns the item to the inventory.
///
/// Persists equipped item ids per slot via <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class EquipmentComponent : EntityComponent, ISaveable
{
    private readonly Dictionary<EquipmentSlot, EquippableItemResource> _equipped = new();

    private StatsComponent? _stats;
    private InventoryComponent? _inventory;
    private MeleeWeaponComponent? _weapon;
    private WeaponResource? _defaultWeapon;

    public string SaveId => $"equipment:{Entity?.RuntimeId ?? 0}";

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _inventory = Entity.GetComponent<InventoryComponent>();
        _weapon = Entity.GetComponent<MeleeWeaponComponent>();
        _defaultWeapon = _weapon?.Weapon;
        SaveManager.Instance?.Register(this);
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public EquippableItemResource? GetEquipped(EquipmentSlot slot)
    {
        return _equipped.TryGetValue(slot, out EquippableItemResource? item) ? item : null;
    }

    public bool IsEquipped(EquipmentSlot slot) => _equipped.ContainsKey(slot);

    /// <summary>Equips an item taken from the inventory. Returns false if it isn't there.</summary>
    public bool Equip(EquippableItemResource item)
    {
        if (item == null || item.Slot == EquipmentSlot.None)
        {
            return false;
        }

        if (_inventory == null || !_inventory.RemoveItem(item, 1))
        {
            return false;
        }

        EquipInternal(item, returnOldToInventory: true);
        return true;
    }

    /// <summary>Unequips the item in a slot, returning it to the inventory.</summary>
    public bool Unequip(EquipmentSlot slot)
    {
        if (!_equipped.Remove(slot, out EquippableItemResource? item))
        {
            return false;
        }

        RemoveBonuses(item);
        RestoreWeapon(item);
        _inventory?.AddItem(item, 1);
        NotifyChanged();
        return true;
    }

    private void EquipInternal(EquippableItemResource item, bool returnOldToInventory)
    {
        if (_equipped.TryGetValue(item.Slot, out EquippableItemResource? old))
        {
            RemoveBonuses(old);
            RestoreWeapon(old);
            if (returnOldToInventory)
            {
                _inventory?.AddItem(old, 1);
            }
        }

        ApplyBonuses(item);
        ApplyWeapon(item);
        _equipped[item.Slot] = item;
        NotifyChanged();
    }

    private void ApplyBonuses(EquippableItemResource item)
    {
        if (_stats == null)
        {
            return;
        }

        foreach ((StatType stat, float value) in item.StatBonuses())
        {
            _stats.GetStat(stat).AddModifier(new StatModifier(value, ModifierType.Flat, item));
        }
    }

    private void RemoveBonuses(EquippableItemResource item)
    {
        if (_stats == null)
        {
            return;
        }

        foreach ((StatType stat, float _) in item.StatBonuses())
        {
            _stats.GetStat(stat).RemoveModifiersFromSource(item);
        }
    }

    private void ApplyWeapon(EquippableItemResource item)
    {
        if (item.Weapon != null && _weapon != null)
        {
            _weapon.Weapon = item.Weapon;
        }
    }

    private void RestoreWeapon(EquippableItemResource item)
    {
        if (item.Weapon != null && _weapon != null)
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
        foreach (KeyValuePair<EquipmentSlot, EquippableItemResource> pair in _equipped)
        {
            slots[(int)pair.Key] = pair.Value.Id;
        }

        return new Godot.Collections.Dictionary { ["slots"] = slots };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        foreach (EquippableItemResource item in _equipped.Values)
        {
            RemoveBonuses(item);
            RestoreWeapon(item);
        }

        _equipped.Clear();

        if (data.TryGetValue("slots", out Variant slotsVariant))
        {
            var slots = slotsVariant.AsGodotDictionary();
            foreach (Variant key in slots.Keys)
            {
                string id = slots[key].AsString();
                if (ItemDatabase.Get(id) is EquippableItemResource item)
                {
                    EquipInternal(item, returnOldToInventory: false);
                }
            }
        }

        NotifyChanged();
    }
}
