using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Items;

/// <summary>
/// The player's quick-use bar: five slots, each holding an item <b>template id</b>. Pressing the
/// matching number key (1-5, bound in <see cref="GameInput.Hotbar"/>) activates the slot — a
/// consumable is drunk, an equippable is equipped (which, for a weapon, swaps it in as the active
/// weapon). Assignments are made from the inventory panel and persisted via <see cref="ISaveable"/>.
///
/// Stored by id rather than instance so it survives save/load; <see cref="Activate"/> resolves the id to
/// a live instance in the bag or — for an already-worn weapon — the equipment.
/// </summary>
[GlobalClass]
public partial class HotbarComponent : EntityComponent, ISaveable
{
    public const int SlotCount = 5;

    private readonly string[] _slots = { "", "", "", "", "" };
    private InventoryComponent? _inventory;
    private EquipmentComponent? _equipment;

    public string SaveId => SaveKey("hotbar");

    protected override void OnInitialize()
    {
        _inventory = Entity!.GetComponent<InventoryComponent>();
        _equipment = Entity!.GetComponent<EquipmentComponent>();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public string Get(int slot) => slot >= 0 && slot < SlotCount ? _slots[slot] : string.Empty;

    /// <summary>Assigns <paramref name="itemId"/> to <paramref name="slot"/>, clearing it from any other
    /// slot first so an item lives in exactly one place.</summary>
    public void Assign(int slot, string itemId)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] == itemId)
            {
                _slots[i] = string.Empty;
            }
        }

        _slots[slot] = itemId ?? string.Empty;
        NotifyChanged();
    }

    public void Clear(int slot)
    {
        if (slot < 0 || slot >= SlotCount || _slots[slot].Length == 0)
        {
            return;
        }

        _slots[slot] = string.Empty;
        NotifyChanged();
    }

    /// <summary>Uses the item assigned to <paramref name="slot"/>: drink a consumable, equip an
    /// equippable (re-applying a worn weapon as active). No-op for an empty slot or a missing item.</summary>
    public void Activate(int slot)
    {
        if (slot < 0 || slot >= SlotCount || _slots[slot].Length == 0)
        {
            return;
        }

        ItemInstance? instance = _inventory?.FirstInstanceOf(_slots[slot]) ?? _equipment?.FirstEquippedInstanceOf(_slots[slot]);
        if (instance == null)
        {
            return;
        }

        if (instance.Template is ConsumableItemResource)
        {
            _inventory?.Consume(instance);
        }
        else if (instance.IsEquippable && _equipment != null)
        {
            // Already worn (e.g. switching to the off-hand bow) → just make it the active weapon;
            // Equip() can't, since it requires the item to be in the bag. Otherwise equip from the bag.
            if (_equipment.IsInstanceEquipped(instance))
            {
                _equipment.ActivateWeapon(instance);
            }
            else
            {
                _equipment.Equip(instance);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (GameManager.Instance is not { IsPlaying: true } || UiState.MenuOpen)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (Input.IsActionJustPressed(GameInput.Hotbar[i]))
            {
                Activate(i);
            }
        }
    }

    private void NotifyChanged()
    {
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new HotbarChangedEvent(Entity));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var slots = new Godot.Collections.Array();
        foreach (string id in _slots)
        {
            slots.Add(id);
        }

        return new Godot.Collections.Dictionary { ["slots"] = slots };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (data.TryGetValue("slots", out Variant slotsVar))
        {
            Godot.Collections.Array slots = slotsVar.AsGodotArray();
            for (int i = 0; i < SlotCount && i < slots.Count; i++)
            {
                _slots[i] = slots[i].AsString();
            }
        }

        NotifyChanged();
    }
}
