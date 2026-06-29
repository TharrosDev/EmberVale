using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// A slot-based, stacking item container attached to an entity. Adding merges
/// into existing stacks of the same item (respecting <see cref="ItemResource.MaxStack"/>)
/// before consuming new slots up to <see cref="Capacity"/>. Tracks total weight
/// for future encumbrance. Persists its contents via <see cref="ISaveable"/>
/// (item ids + quantities, resolved through the <see cref="ItemDatabase"/> on load).
///
/// This is the substrate for equipment (Phase 6), loot (Phase 7) and crafting
/// (Phase 14).
/// </summary>
[GlobalClass]
public partial class InventoryComponent : EntityComponent, ISaveable
{
    [Export] public int Capacity { get; set; } = 24;

    /// <summary>Informational weight budget; not yet enforced (drives encumbrance later).</summary>
    [Export] public float MaxWeight { get; set; } = 100f;

    private readonly List<ItemStack> _stacks = new();

    public IReadOnlyList<ItemStack> Stacks => _stacks;

    public int UsedSlots => _stacks.Count;

    public string SaveId => SaveKey("inventory");

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (ItemStack stack in _stacks)
            {
                total += stack.Weight;
            }

            return total;
        }
    }

    public bool IsOverEncumbered => TotalWeight > MaxWeight;

    protected override void OnInitialize()
    {
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// Adds up to <paramref name="quantity"/> of a plain (affix-less) item template,
    /// stacking then filling empty slots. Returns the amount actually stored.
    /// </summary>
    public int AddItem(ItemResource item, int quantity)
    {
        if (item == null || quantity <= 0)
        {
            return 0;
        }

        return AddInstance(ItemInstance.Plain(item), quantity);
    }

    /// <summary>
    /// Adds up to <paramref name="quantity"/> of an item instance. Affix-less
    /// instances merge into matching stackable stacks before consuming new slots;
    /// rolled (unique) instances each take their own slot. Returns the amount
    /// actually stored (less than requested if the inventory ran out of room).
    /// </summary>
    public int AddInstance(ItemInstance instance, int quantity)
    {
        if (instance == null || quantity <= 0)
        {
            return 0;
        }

        int remaining = quantity;

        // 1) Top up existing compatible stacks (only affix-less instances stack).
        if (instance.IsStackable)
        {
            foreach (ItemStack stack in _stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (stack.SpaceLeft <= 0 || !stack.Instance.CanStackWith(instance))
                {
                    continue;
                }

                int put = Mathf.Min(stack.SpaceLeft, remaining);
                stack.Quantity += put;
                remaining -= put;
            }
        }

        // 2) Consume new slots while there is room.
        while (remaining > 0 && _stacks.Count < Capacity)
        {
            int put = Mathf.Min(remaining, instance.MaxStack);
            _stacks.Add(new ItemStack(instance, put));
            remaining -= put;
        }

        int added = quantity - remaining;
        if (added > 0)
        {
            NotifyChanged();
        }

        return added;
    }

    public bool RemoveItem(ItemResource item, int quantity) => RemoveItem(item.Id, quantity);

    /// <summary>Removes <paramref name="quantity"/> across stacks. Fails if there isn't enough.</summary>
    public bool RemoveItem(string itemId, int quantity)
    {
        if (quantity <= 0)
        {
            return true;
        }

        if (CountOf(itemId) < quantity)
        {
            return false;
        }

        int remaining = quantity;
        for (int i = _stacks.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (_stacks[i].Item.Id != itemId)
            {
                continue;
            }

            int take = Mathf.Min(_stacks[i].Quantity, remaining);
            _stacks[i].Quantity -= take;
            remaining -= take;
            if (_stacks[i].Quantity <= 0)
            {
                _stacks.RemoveAt(i);
            }
        }

        NotifyChanged();
        return true;
    }

    public int CountOf(ItemResource item) => CountOf(item.Id);

    public int CountOf(string itemId)
    {
        int total = 0;
        foreach (ItemStack stack in _stacks)
        {
            if (stack.Item.Id == itemId)
            {
                total += stack.Quantity;
            }
        }

        return total;
    }

    public bool Contains(string itemId, int quantity = 1) => CountOf(itemId) >= quantity;

    /// <summary>
    /// Removes exactly one unit of a specific instance (by reference), used to pull
    /// a rolled item out for equipping. Returns the stack's instance on success so
    /// the caller keeps its affixes; null if the instance wasn't found.
    /// </summary>
    public ItemInstance? RemoveOneInstance(ItemInstance instance)
    {
        for (int i = 0; i < _stacks.Count; i++)
        {
            if (!ReferenceEquals(_stacks[i].Instance, instance))
            {
                continue;
            }

            ItemInstance held = _stacks[i].Instance;
            _stacks[i].Quantity--;
            if (_stacks[i].Quantity <= 0)
            {
                _stacks.RemoveAt(i);
            }

            NotifyChanged();
            return held;
        }

        return null;
    }

    /// <summary>Uses one <paramref name="instance"/> of a consumable: applies its effect to the owner
    /// and removes it from the bag. Returns false if it isn't a held consumable.</summary>
    public bool Consume(ItemInstance? instance)
    {
        if (instance?.Template is not ConsumableItemResource consumable || CountOf(instance.TemplateId) <= 0)
        {
            return false;
        }

        StatsComponent? stats = Entity?.GetComponent<StatsComponent>();
        if (stats == null)
        {
            return false;
        }

        if (consumable.HealAmount > 0f)
        {
            stats.Heal(consumable.HealAmount);
        }

        RemoveOneInstance(instance);
        Log.Info($"Consumed {consumable.DisplayName}.");
        return true;
    }

    public void Clear()
    {
        if (_stacks.Count == 0)
        {
            return;
        }

        _stacks.Clear();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new InventoryChangedEvent(Entity));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var stacks = new Godot.Collections.Array();
        foreach (ItemStack stack in _stacks)
        {
            stacks.Add(new Godot.Collections.Dictionary
            {
                ["instance"] = stack.Instance.Save(),
                ["qty"] = stack.Quantity,
            });
        }

        return new Godot.Collections.Dictionary { ["stacks"] = stacks };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _stacks.Clear();

        if (data.TryGetValue("stacks", out Variant stacksVariant))
        {
            foreach (Variant entry in stacksVariant.AsGodotArray())
            {
                var dict = entry.AsGodotDictionary();
                int qty = dict["qty"].AsInt32();
                ItemInstance? instance = ItemInstance.FromSave(dict["instance"].AsGodotDictionary());
                if (instance != null)
                {
                    AddInstance(instance, qty);
                }
            }
        }

        NotifyChanged();
    }
}
