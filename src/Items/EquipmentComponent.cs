using Embervale.Combat.Actions;
using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Progression;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
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
///
/// <para>Three things ride on top of the per-item bonuses and are re-derived from what is worn after
/// every change (see <c>EquipmentComponent.Derived.cs</c>): flat mana and stamina regeneration,
/// item-set threshold bonuses, and the list of active unique effects. The
/// <see cref="EquipmentSlot.Ammo"/> slot is the one slot that holds a quantity
/// (<see cref="AmmoCount"/>): equipping arrows moves the whole stack in.</para>
/// </summary>
[GlobalClass]
public partial class EquipmentComponent : EntityComponent, ISaveable
{
    /// <summary>Save key of the ammo slot's quantity. Additive: written only when it is not 1, and an
    /// occupied ammo slot with no key (every save before quantities existed) holds one arrow.</summary>
    public const string AmmoQuantityKey = "ammo_qty";

    private readonly Dictionary<EquipmentSlot, ItemInstance> _equipped = new();
    private int _ammoCount;

    private StatsComponent? _stats;
    private InventoryComponent? _inventory;
    private CharacterActionComponent? _weapon;
    private EquipmentPresentationComponent? _presentation;
    private WeaponResource? _defaultWeapon;

    /// <summary>The name the drawn main-hand weapon hangs under, so a swap replaces it rather than
    /// stacking a second sword in the same fist.</summary>
    private const string MainHandVisual = "MainHand";
    private const string OffHandVisual = "OffHand";

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

    /// <summary>The weapon family in the main hand; <see cref="WeaponClass.None"/> when the hand is
    /// empty or holds a classless legacy weapon.</summary>
    public WeaponClass MainHandClass =>
        GetEquipped(EquipmentSlot.MainHand)?.Equippable?.WeaponClass ?? WeaponClass.None;

    // --- Ammunition -----------------------------------------------------------

    /// <summary>The arrows in the <see cref="EquipmentSlot.Ammo"/> slot, or null.</summary>
    public ItemInstance? Ammo => GetEquipped(EquipmentSlot.Ammo);

    /// <summary>How many of <see cref="Ammo"/> are in the slot; 0 when it is empty.</summary>
    public int AmmoCount => _equipped.ContainsKey(EquipmentSlot.Ammo) ? _ammoCount : 0;

    /// <summary>
    /// Spends one unit from the ammo slot. When that was the last, the slot refills itself with
    /// every matching arrow in the pack (so arrows picked up mid-fight are not stranded behind a
    /// menu), and empties only when there are none. Returns false when there was nothing to spend.
    /// </summary>
    public bool ConsumeAmmo()
    {
        if (!_equipped.TryGetValue(EquipmentSlot.Ammo, out ItemInstance? ammo) || _ammoCount <= 0)
        {
            return false;
        }

        _ammoCount--;
        if (_ammoCount <= 0)
        {
            int spare = _inventory?.TotalCount(ammo.TemplateId) ?? 0;
            if (spare > 0 && _inventory!.RemoveItem(ammo.TemplateId, spare))
            {
                _ammoCount = spare;
            }
            else
            {
                Vacate(EquipmentSlot.Ammo, toInventory: false);
                RefreshDerived();
            }
        }

        NotifyChanged();
        return true;
    }

    // --- Equip / unequip ------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="instance"/> may go on right now, and if not, why: the wearer's level
    /// is below <see cref="ItemResource.RequiredLevel"/>, it is an off-hand item beside a two-handed
    /// weapon, or what it would take off has no room in the pack.
    /// <see cref="InventoryRules.ReasonKey"/> turns the answer into a locale key.
    /// </summary>
    public EquipRefusal CanEquip(ItemInstance? instance)
    {
        if (instance?.Equippable is not { } equippable || equippable.Slot == EquipmentSlot.None)
        {
            return EquipRefusal.NotEquippable;
        }

        if (_inventory == null || !_inventory.Holds(instance))
        {
            return EquipRefusal.NotHeld;
        }

        // An actor with no progression (an NPC handed gear by a factory) has no level to fall short of.
        int level = Entity?.GetComponent<ProgressionComponent>()?.Level ?? int.MaxValue;
        bool mainIsTwoHanded = GetEquipped(EquipmentSlot.MainHand)?.Equippable?.TwoHanded == true;

        int needed = 0;
        int free = _inventory.FreeSlots;
        if (!IsAmmoTopUp(instance))
        {
            foreach (EquipmentSlot slot in DisplacedBy(equippable))
            {
                needed += _inventory.SlotsNeededFor(_equipped[slot], QuantityIn(slot));
            }

            // The new item leaves the pack first. That frees its slot when it was the whole stack:
            // always for arrows (the stack moves in), and for a single item otherwise.
            int stack = _inventory.PackStackQuantity(instance);
            bool freesSlot = stack > 0 && (equippable.Slot == EquipmentSlot.Ammo || stack == 1);
            free = Mathf.Max(0, _inventory.Capacity - (_inventory.UsedSlots - (freesSlot ? 1 : 0)));
        }

        return InventoryRules.CheckEquip(
            equippable.RequiredLevel, level, equippable.Slot == EquipmentSlot.OffHand, mainIsTwoHanded, needed, free);
    }

    /// <summary>Equips a specific instance taken from the inventory. Returns false if
    /// <see cref="CanEquip"/> refuses it; the player is told why. Arrows move in as a whole stack,
    /// and equipping more of the arrows already in the slot tops it up.</summary>
    public bool Equip(ItemInstance instance)
    {
        EquipRefusal refusal = CanEquip(instance);
        if (refusal != EquipRefusal.None)
        {
            Announce(refusal);
            return false;
        }

        EquipmentSlot slot = instance.Equippable!.Slot;
        if (IsAmmoTopUp(instance))
        {
            _ammoCount += _inventory!.TakeAll(instance);
            NotifyChanged();
            return true;
        }

        int quantity = slot == EquipmentSlot.Ammo
            ? _inventory!.TakeAll(instance)
            : (_inventory!.RemoveOneInstance(instance) != null ? 1 : 0);
        if (quantity <= 0)
        {
            return false;
        }

        foreach (EquipmentSlot displaced in DisplacedBy(instance.Equippable))
        {
            Vacate(displaced, toInventory: true);
        }

        Place(instance, slot, quantity);
        RefreshDerived();
        NotifyChanged();
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

        // Secure the destination BEFORE vacating the slot: taking a sword off with a full pack used
        // to delete it. An actor with no inventory at all (an enemy) just unequips, since there was
        // never anywhere for it to go.
        if (_inventory != null && !_inventory.CanAccept(instance, QuantityIn(slot)))
        {
            Announce(EquipRefusal.PackFull);
            return false;
        }

        Vacate(slot, toInventory: true);
        RefreshDerived();
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Takes an equipped instance off <b>without</b> putting it in the pack and hands it to the
    /// caller, who now owns it. This is the door for anything that destroys worn gear (salvage): it
    /// needs no free slot, so it cannot fail on a full pack the way unequip-then-remove does.
    /// Returns null if that instance is not equipped.
    /// </summary>
    public ItemInstance? TakeEquipped(ItemInstance instance)
    {
        foreach (KeyValuePair<EquipmentSlot, ItemInstance> pair in _equipped)
        {
            if (ReferenceEquals(pair.Value, instance))
            {
                Vacate(pair.Key, toInventory: false);
                RefreshDerived();
                NotifyChanged();
                return instance;
            }
        }

        return null;
    }

    /// <summary>The slots equipping <paramref name="equippable"/> empties: its own, and the off
    /// hand when it is a two-handed weapon.</summary>
    private List<EquipmentSlot> DisplacedBy(EquippableItemResource equippable)
    {
        var slots = new List<EquipmentSlot>(2);
        if (_equipped.ContainsKey(equippable.Slot))
        {
            slots.Add(equippable.Slot);
        }

        if (equippable is { Slot: EquipmentSlot.MainHand, TwoHanded: true } && _equipped.ContainsKey(EquipmentSlot.OffHand))
        {
            slots.Add(EquipmentSlot.OffHand);
        }

        return slots;
    }

    private bool IsAmmoTopUp(ItemInstance instance) =>
        instance.Equippable?.Slot == EquipmentSlot.Ammo
        && _equipped.TryGetValue(EquipmentSlot.Ammo, out ItemInstance? current)
        && current.CanStackWith(instance);

    private int QuantityIn(EquipmentSlot slot) => slot == EquipmentSlot.Ammo ? Mathf.Max(1, _ammoCount) : 1;

    /// <summary>Empties a slot and undoes what the item did. With <paramref name="toInventory"/> the
    /// item goes back to the pack, and is never dropped: callers check for room first, and if that
    /// check was ever wrong the pack goes over capacity rather than the item being deleted.</summary>
    private void Vacate(EquipmentSlot slot, bool toInventory)
    {
        if (!_equipped.TryGetValue(slot, out ItemInstance? instance))
        {
            return;
        }

        int quantity = QuantityIn(slot);
        _equipped.Remove(slot);
        if (slot == EquipmentSlot.Ammo)
        {
            _ammoCount = 0;
        }

        RemoveBonuses(instance);
        RestoreWeapon(instance);
        if (toInventory)
        {
            _inventory?.AddOrOverflow(instance, quantity);
        }
    }

    private void Place(ItemInstance instance, EquipmentSlot slot, int quantity)
    {
        ApplyBonuses(instance);
        ApplyWeapon(instance);
        _equipped[slot] = instance;
        if (slot == EquipmentSlot.Ammo)
        {
            _ammoCount = Mathf.Max(1, quantity);
        }
    }

    /// <summary>Tells the player why, through the warning toast. Only the player hears it.</summary>
    private void Announce(EquipRefusal refusal)
    {
        string key = InventoryRules.ReasonKey(refusal);
        if (key.Length > 0 && CombatPerspective.IsPlayer(Entity))
        {
            EventBus.Instance?.Publish(new WorldHazardNoticeEvent(key));
        }
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
        // ⚠️ An off-hand piece is not a weapon and must be handled BEFORE the weapon guard below.
        // A shield has no <see cref="WeaponResource"/>, so it fell straight out of this method and
        // out of RestoreWeapon too: EquipmentSocket.Shield existed, eqp_shield_round.glb existed,
        // and nothing in the game ever put one on the other.
        if (instance.Equippable?.Slot == EquipmentSlot.OffHand)
        {
            ShowOffHand(EquipmentPresentationComponent.ModelFor(
                instance.Template.WorldModelPath, instance.Equippable.WeaponClass).Path);
            return;
        }

        if (instance.Equippable?.Weapon is not { } weapon)
        {
            return;
        }

        if (_weapon != null)
        {
            _weapon.Weapon = weapon;
        }

        // An item with no model of its own takes its weapon family's stand-in, so a looted axe is
        // not drawn as whatever the last weapon happened to be.
        EquipmentPresentationComponent.WeaponModel model = EquipmentPresentationComponent.ModelFor(
            instance.Template.WorldModelPath, instance.Equippable.WeaponClass);
        ShowWeapon(model.Path, model.Scale);
        if (_presentation != null)
        {
            _presentation.WieldedClass = instance.Equippable.WeaponClass;
        }
    }

    private void RestoreWeapon(ItemInstance instance)
    {
        if (instance.Equippable?.Slot == EquipmentSlot.OffHand)
        {
            _presentation?.Detach(OffHandVisual);
            return;
        }

        if (instance.Equippable?.Weapon == null)
        {
            return;
        }

        if (_weapon != null)
        {
            _weapon.Weapon = _defaultWeapon;
        }

        ShowWeapon(DefaultWeaponModelPath, 1f);
        if (_presentation != null)
        {
            _presentation.WieldedClass = WeaponClass.None;
        }
    }

    /// <summary>Straps an off-hand piece to the forearm, or leaves the arm bare when the item
    /// authors no model. <see cref="EquipmentSocket.Shield"/> owns the bone choice and the space —
    /// a shield goes on the forearm, not the hand, or it counter-rotates with every grip roll.</summary>
    private void ShowOffHand(string modelPath)
    {
        if (_presentation is not { HasRig: true } presentation)
        {
            return;
        }

        presentation.Detach(OffHandVisual);
        if (modelPath.Length > 0)
        {
            presentation.Attach(EquipmentSocket.Shield, modelPath, OffHandVisual);
        }
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
    private void ShowWeapon(string modelPath, float scale)
    {
        if (_presentation is not { HasRig: true } presentation || modelPath.Length == 0)
        {
            return;
        }

        presentation.Attach(EquipmentSocket.HandR, modelPath, MainHandVisual,
            rotationDegrees: WeaponGrip.HandRotationDegrees,
            scale: Mathf.IsEqualApprox(scale, 1f) ? null : Vector3.One * scale);
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

        var data = new Godot.Collections.Dictionary { ["slots"] = slots };
        if (_equipped.ContainsKey(EquipmentSlot.Ammo) && _ammoCount != 1)
        {
            data[AmmoQuantityKey] = _ammoCount;
        }

        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        foreach (ItemInstance instance in _equipped.Values)
        {
            RemoveBonuses(instance);
            RestoreWeapon(instance);
        }

        _equipped.Clear();
        _ammoCount = 0;

        // A restore does not re-judge what the save holds: no level check, no two-handed check, no
        // pack room. Whatever was worn comes back on.
        int ammo = data.TryGetValue(AmmoQuantityKey, out Variant ammoVariant) ? ammoVariant.AsInt32() : 1;
        if (data.TryGetValue("slots", out Variant slotsVariant))
        {
            var slots = slotsVariant.AsGodotDictionary();
            foreach (Variant key in slots.Keys)
            {
                ItemInstance? instance = ItemInstance.FromSave(SaveRead.AsSection(slots[key]));
                if (instance?.Equippable is { } equippable && equippable.Slot != EquipmentSlot.None)
                {
                    if (_equipped.TryGetValue(equippable.Slot, out ItemInstance? doubled))
                    {
                        RemoveBonuses(doubled);
                        RestoreWeapon(doubled);
                    }

                    Place(instance, equippable.Slot, ammo);
                }
            }
        }

        // Strips the set bonuses and regeneration the abandoned timeline applied, then rebuilds
        // them from what was just restored (including "nothing").
        RefreshDerived();
        NotifyChanged();
    }
}
