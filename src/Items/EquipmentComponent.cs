using Embervale.Combat.Actions;
using System.Collections.Generic;
using Embervale.Animation;
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
/// <see cref="CharacterActionComponent"/>. Unequipping reverses all of that and returns
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
    private CharacterActionComponent? _weapon;
    private EquipmentPresentationComponent? _presentation;
    private WeaponResource? _defaultWeapon;

    /// <summary>The name the drawn main-hand weapon hangs under, so a swap replaces it rather than
    /// stacking a second sword in the same fist.</summary>
    private const string MainHandVisual = "MainHand";

    public string SaveId => SaveKey("equipment");

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _inventory = Entity.GetComponent<InventoryComponent>();
        _weapon = Entity.GetComponent<CharacterActionComponent>();
        _presentation = Entity.GetComponent<EquipmentPresentationComponent>();
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

    /// <summary>Unequips the item in a slot, returning it to the inventory. Refuses if there is no
    /// room for it, rather than taking it off into nothing.</summary>
    public bool Unequip(EquipmentSlot slot)
    {
        if (!_equipped.TryGetValue(slot, out ItemInstance? instance))
        {
            return false;
        }

        // Secure the destination BEFORE vacating the slot. This is a pure add with no matching
        // removal to free a slot first, so unlike the swap in EquipInternal it can genuinely fail —
        // and the old order discarded AddInstance's return, so taking a sword off with a full pack
        // deleted it: gone from the slot, never in the bag, quite possibly a rolled legendary.
        // An actor with no inventory at all (an enemy) keeps the previous behaviour and just
        // unequips, since there was never anywhere for it to go.
        if (_inventory != null && _inventory.AddInstance(instance, 1) < 1)
        {
            return false;
        }

        _equipped.Remove(slot);
        RemoveBonuses(instance);
        RestoreWeapon(instance);
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
        if (instance.Equippable?.Weapon is not { } weapon)
        {
            return;
        }

        if (_weapon != null)
        {
            _weapon.Weapon = weapon;
        }

        ShowWeapon(instance.Equippable.WorldModelPath);
    }

    private void RestoreWeapon(ItemInstance instance)
    {
        if (instance.Equippable?.Weapon == null)
        {
            return;
        }

        if (_weapon != null)
        {
            _weapon.Weapon = _defaultWeapon;
        }

        ShowWeapon(DefaultWeaponModelPath);
    }

    /// <summary>
    /// Puts a weapon in the hand, or takes it out.
    ///
    /// ⚠️ <b>Equipping used to change the numbers and nothing else.</b> This component had zero
    /// visual code: swapping a rusted blade for a steel sword moved the damage and left the same
    /// iron sword in the fist, because the only weapon mesh in the game was hung once by
    /// <c>PlayerFactory</c> and never touched again. An item without a
    /// <c>WorldModelPath</c> keeps whatever is already there rather than emptying the hand, so
    /// unauthored weapons degrade to the old behaviour instead of to nothing.
    /// </summary>
    private void ShowWeapon(string modelPath)
    {
        if (_presentation is not { HasRig: true } presentation || modelPath.Length == 0)
        {
            return;
        }

        presentation.Attach(EquipmentSocket.HandR, modelPath, MainHandVisual,
            rotationDegrees: WeaponGrip.HandRotationDegrees);
    }

    /// <summary>The model restored when a weapon is unequipped — the actor's starting weapon.
    /// Set by the actor's factory before this component enters the tree.</summary>
    [Export] public string DefaultWeaponModelPath { get; set; } = "";

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
