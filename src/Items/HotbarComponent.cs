using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Items;

/// <summary>
/// The player's <b>consumables</b> quick-use bar: five slots, each holding a consumable item
/// <b>template id</b>. Pressing the matching number key (1-5, bound in <see cref="GameInput.Hotbar"/>)
/// drinks/uses the slot's consumable from the bag. Assignments are made from the inventory panel (only
/// consumables can be assigned) and persisted via <see cref="ISaveable"/>.
///
/// Stored by id rather than instance so it survives save/load; <see cref="Activate"/> resolves the id to
/// a live instance in the bag.
/// </summary>
[GlobalClass]
public partial class HotbarComponent : EntityComponent, ISaveable
{
    public const int SlotCount = 5;

    private readonly string[] _slots = { "", "", "", "", "" };
    private InventoryComponent? _inventory;

    public string SaveId => SaveKey("hotbar");

    protected override void OnInitialize()
    {
        _inventory = Entity!.GetComponent<InventoryComponent>();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public string Get(int slot) => slot >= 0 && slot < SlotCount ? _slots[slot] : string.Empty;

    /// <summary>Assigns a <b>consumable</b> <paramref name="itemId"/> to <paramref name="slot"/>, clearing
    /// it from any other slot first so an item lives in exactly one place. Non-consumables are ignored.</summary>
    public void Assign(int slot, string itemId)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            return;
        }

        // Consumables-only bar: silently reject anything that isn't a consumable.
        if (ItemDatabase.Get(itemId) is not ConsumableItemResource)
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

    /// <summary>Uses the consumable assigned to <paramref name="slot"/> from the bag. No-op for an empty
    /// slot, a missing item, or a non-consumable (a stale id from an old save).</summary>
    public bool Activate(int slot)
    {
        if (slot < 0 || slot >= SlotCount || _slots[slot].Length == 0)
        {
            return false;
        }

        // The cooldown group, a full resource and "nothing to cure" are all refused (and explained
        // to the player) by Consume itself, so a key press and a click in the pack obey one rule.
        ItemInstance? instance = _inventory?.FirstInstanceOf(_slots[slot]);
        return instance?.Template is ConsumableItemResource && _inventory!.Consume(instance);
    }

    /// <summary>Seconds until the consumable in <paramref name="slot"/> can be used again; 0 when it
    /// is ready or the slot is empty. Slots sharing a cooldown group count down together.</summary>
    public float CooldownRemaining(int slot) =>
        SlotItem(slot) is { } item ? Timed?.CooldownRemaining(item) ?? 0f : 0f;

    /// <summary>The same cooldown as a 1-to-0 fraction, for the sweep the bar draws over a slot.</summary>
    public float CooldownFraction(int slot) =>
        SlotItem(slot) is { } item ? Timed?.CooldownFraction(item) ?? 0f : 0f;

    private ConsumableEffectsComponent? Timed => Entity?.GetComponent<ConsumableEffectsComponent>();

    private ConsumableItemResource? SlotItem(int slot) =>
        slot >= 0 && slot < SlotCount && _slots[slot].Length > 0
            ? ItemDatabase.Get(_slots[slot]) as ConsumableItemResource
            : null;

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
        // Clear first: the loop below only writes the slots the save actually carries, so an absent
        // key — or a save written when the bar had fewer slots — left this session's assignments
        // sitting in the gaps.
        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i] = string.Empty;
        }

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
