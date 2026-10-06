using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
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

    // ⚠️ MaxWeight and IsOverEncumbered were removed in Phase 40 (2026-08-12). They shipped in Phase 5
    // as "not yet enforced (drives encumbrance later)" and sat with ZERO readers for thirty-five
    // phases — MaxWeight was read only by IsOverEncumbered, and IsOverEncumbered by nothing at all.
    // Encumbrance is a survival need, and survival needs are CUT, so "later" never comes and the pair
    // was the stub 40B's rule forbids. TotalWeight STAYS: it is printed on the character sheet
    // (char.backpack_full) as an item fact, not a limit, and nothing about it implies a budget.
    private readonly List<ItemStack> _stacks = new();

    /// <summary>The material bag: one stack per template, any quantity, no slot cost. Only ever
    /// populated while <see cref="UseMaterialBag"/> is on.</summary>
    private readonly List<ItemStack> _materials = new();

    /// <summary>Set only while <see cref="Load"/> is restoring. See its remarks.</summary>
    private bool _ignoreCapacity;

    /// <summary>
    /// Routes crafting materials (<see cref="IsBagItem"/>) into the uncapped <see cref="Materials"/>
    /// bag instead of pack slots. <b>Off by default</b>, which is every inventory's behaviour before
    /// the bag existed: a chest, a merchant's pack and a corpse keep their materials in
    /// <see cref="Stacks"/> where their readers look. It is meant for the player's pack, and must be
    /// set before the component initializes (a factory property, like <see cref="Capacity"/>).
    /// Whatever it is set to, the count / contains / remove queries span both stores and a load
    /// loses nothing: a save written with the bag restores into the pack when it is off, and a save
    /// written without it migrates its materials into the bag when it is on.
    /// </summary>
    [Export] public bool UseMaterialBag { get; set; }

    /// <summary>The pack: slot-limited stacks, up to <see cref="Capacity"/> of them.</summary>
    public IReadOnlyList<ItemStack> Stacks => _stacks;

    /// <summary>The material bag: one stack per material template with an uncapped quantity
    /// (<see cref="ItemStack.IsFull"/> / <see cref="ItemStack.SpaceLeft"/> mean nothing for these).
    /// Empty unless <see cref="UseMaterialBag"/> is on.</summary>
    public IReadOnlyList<ItemStack> Materials => _materials;

    /// <summary>Every stack held: the pack, then the material bag. For readers that must see all
    /// the owner has (selling, impounding, appraising) rather than what occupies slots.</summary>
    public IEnumerable<ItemStack> AllStacks
    {
        get
        {
            foreach (ItemStack stack in _stacks)
            {
                yield return stack;
            }

            foreach (ItemStack stack in _materials)
            {
                yield return stack;
            }
        }
    }

    /// <summary>Pack slots in use. The material bag never counts.</summary>
    public int UsedSlots => _stacks.Count;

    /// <summary>What the material bag takes: an affix-less <see cref="ItemType.Material"/>.</summary>
    public static bool IsBagItem(ItemInstance instance) => instance.Type == ItemType.Material && !instance.HasAffixes;

    public string SaveId => SaveKey("inventory");

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (ItemStack stack in _stacks)
            {
                // Crafting materials are weightless — they never count against carry capacity.
                if (stack.Item.Type == ItemType.Material)
                {
                    continue;
                }

                total += stack.Weight;
            }

            return total;
        }
    }

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

        if (UseMaterialBag && IsBagItem(instance))
        {
            AddToBag(instance, quantity);
            NotifyChanged();
            return quantity;
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

                // A restore must not fold a marked stack and an unmarked one together: the player
                // marked one of them, not both. In play a pickup joins whatever stack is there.
                if (_ignoreCapacity && (stack.Instance.Locked != instance.Locked || stack.Instance.Junk != instance.Junk))
                {
                    continue;
                }

                int put = Mathf.Min(stack.SpaceLeft, remaining);
                stack.Quantity += put;
                remaining -= put;
            }
        }

        // 2) Consume new slots while there is room. The first new slot holds the instance it was
        // handed (callers find it again by reference); any further slot gets its own copy, so two
        // stacks never share one identity and one mark.
        bool handedOver = false;
        while (remaining > 0 && (_ignoreCapacity || _stacks.Count < Capacity))
        {
            int put = Mathf.Min(remaining, instance.MaxStack);
            _stacks.Add(new ItemStack(handedOver ? instance.Copy() : instance, put));
            handedOver = true;
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

    /// <summary>Removes <paramref name="quantity"/> across the pack and the material bag (pack
    /// first, so slots free up before the bag is touched). Fails, removing nothing, if there isn't
    /// enough. ⚠️ It does not look at <see cref="ItemInstance.Locked"/>: a lock guards the actions
    /// that ask the player (sell, salvage, drop), and those check it themselves.</summary>
    public bool RemoveItem(string itemId, int quantity)
    {
        if (quantity <= 0)
        {
            return true;
        }

        if (TotalCount(itemId) < quantity)
        {
            return false;
        }

        int remaining = TakeFrom(_stacks, itemId, quantity);
        TakeFrom(_materials, itemId, remaining);

        NotifyChanged();
        return true;
    }

    /// <summary>Takes up to <paramref name="quantity"/> of an id out of one store, last stack
    /// first, and returns what is still owed.</summary>
    private static int TakeFrom(List<ItemStack> store, string itemId, int quantity)
    {
        int remaining = quantity;
        for (int i = store.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (store[i].Item.Id != itemId)
            {
                continue;
            }

            int take = Mathf.Min(store[i].Quantity, remaining);
            store[i].Quantity -= take;
            remaining -= take;
            if (store[i].Quantity <= 0)
            {
                store.RemoveAt(i);
            }
        }

        return remaining;
    }

    /// <summary>The first held instance of <paramref name="itemId"/> (pack, then material bag), or
    /// null if none — used by the hotbar to resolve an assigned id back to a usable instance.</summary>
    public ItemInstance? FirstInstanceOf(string itemId)
    {
        foreach (ItemStack stack in AllStacks)
        {
            if (stack.Item.Id == itemId)
            {
                return stack.Instance;
            }
        }

        return null;
    }

    public int CountOf(ItemResource item) => TotalCount(item.Id);

    /// <summary>Same as <see cref="TotalCount"/>: every existing caller asks "how many do I have",
    /// and the answer includes the material bag.</summary>
    public int CountOf(string itemId) => TotalCount(itemId);

    /// <summary>How many of <paramref name="itemId"/> the owner holds: pack plus material bag.</summary>
    public int TotalCount(string itemId) => PackCount(itemId) + BagCount(itemId);

    /// <summary>How many of <paramref name="itemId"/> sit in pack slots.</summary>
    public int PackCount(string itemId) => CountIn(_stacks, itemId);

    /// <summary>How many of <paramref name="itemId"/> sit in the material bag.</summary>
    public int BagCount(string itemId) => CountIn(_materials, itemId);

    private static int CountIn(List<ItemStack> store, string itemId)
    {
        int total = 0;
        foreach (ItemStack stack in store)
        {
            if (stack.Item.Id == itemId)
            {
                total += stack.Quantity;
            }
        }

        return total;
    }

    /// <summary>True when pack plus material bag hold at least <paramref name="quantity"/>.</summary>
    public bool Contains(string itemId, int quantity = 1) => TotalCount(itemId) >= quantity;

    /// <summary>
    /// Splits <paramref name="quantity"/> units off a pack stack into a new stack placed in the
    /// slot after it. Returns the new stack, or null when it cannot be done: the stack is not in
    /// this pack, the quantity is not strictly between 0 and the stack's size, or the pack has no
    /// free slot. The new stack carries its own <see cref="ItemInstance.Copy"/>, marks included.
    /// The material bag does not split (it has one stack per material by definition).
    /// </summary>
    public ItemStack? SplitStack(ItemStack stack, int quantity)
    {
        int index = _stacks.IndexOf(stack);
        if (index < 0 || quantity <= 0 || quantity >= stack.Quantity || _stacks.Count >= Capacity)
        {
            return null;
        }

        stack.Quantity -= quantity;
        var split = new ItemStack(stack.Instance.Copy(), quantity);
        _stacks.Insert(index + 1, split);
        NotifyChanged();
        return split;
    }

    /// <summary>Sets <see cref="ItemInstance.Locked"/> on a held instance (pack or material bag).
    /// Locking clears <see cref="ItemInstance.Junk"/>. Returns false if this inventory does not
    /// hold that instance.</summary>
    public bool SetLocked(ItemInstance instance, bool locked)
    {
        if (!Holds(instance))
        {
            return false;
        }

        if (instance.Locked == locked)
        {
            return true;
        }

        instance.Locked = locked;
        if (locked)
        {
            instance.Junk = false;
        }

        NotifyChanged();
        return true;
    }

    /// <summary>Sets <see cref="ItemInstance.Junk"/> on a held instance. Returns false if this
    /// inventory does not hold that instance, or it is locked and <paramref name="junk"/> is true
    /// (unlock it first).</summary>
    public bool SetJunk(ItemInstance instance, bool junk)
    {
        if (!Holds(instance) || (junk && !CanDispose(instance)))
        {
            return false;
        }

        if (instance.Junk == junk)
        {
            return true;
        }

        instance.Junk = junk;
        NotifyChanged();
        return true;
    }

    /// <summary>A snapshot of every stack marked junk and not locked (pack, then material bag), in
    /// held order: what a "sell all junk" would take, for pricing it before committing.</summary>
    public List<ItemStack> JunkStacks()
    {
        var junk = new List<ItemStack>();
        foreach (ItemStack stack in AllStacks)
        {
            if (stack.Instance.Junk && !stack.Instance.Locked)
            {
                junk.Add(stack);
            }
        }

        return junk;
    }

    /// <summary>Removes every stack <see cref="JunkStacks"/> lists and returns them, so the caller
    /// can pay for or salvage exactly what left. One change notification, and none if nothing was
    /// junk.</summary>
    public List<ItemStack> RemoveAllJunk()
    {
        List<ItemStack> junk = JunkStacks();
        if (junk.Count == 0)
        {
            return junk;
        }

        foreach (ItemStack stack in junk)
        {
            if (!_stacks.Remove(stack))
            {
                _materials.Remove(stack);
            }
        }

        NotifyChanged();
        return junk;
    }

    /// <summary>
    /// Stores all of it, whatever the room: what fits goes in normally and the rest is forced in
    /// past <see cref="Capacity"/>, the way <see cref="Load"/> restores a pack. For the one case
    /// where the alternative is deleting an item the owner already had (gear coming off in a swap).
    /// Over capacity is recoverable; deletion is not.
    /// </summary>
    public void AddOrOverflow(ItemInstance instance, int quantity)
    {
        int stored = AddInstance(instance, quantity);
        if (stored >= quantity)
        {
            return;
        }

        // The remainder needs its own identity when part of it already went in under this one.
        ItemInstance rest = stored > 0 ? instance.Copy() : instance;
        _ignoreCapacity = true;
        try
        {
            AddInstance(rest, quantity - stored);
        }
        finally
        {
            _ignoreCapacity = false;
        }

        Log.Warn($"{Entity?.DisplayName ?? "An inventory"} is over capacity: '{instance.TemplateId}' had no free slot and was kept anyway.");
    }

    /// <summary>The size of the pack stack holding exactly <paramref name="instance"/> (by
    /// reference); 0 when it is not in the pack (absent, or in the material bag).</summary>
    public int PackStackQuantity(ItemInstance instance)
    {
        foreach (ItemStack stack in _stacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return stack.Quantity;
            }
        }

        return 0;
    }

    /// <summary>True when this inventory holds exactly <paramref name="instance"/> (by reference),
    /// in the pack or the material bag.</summary>
    public bool Holds(ItemInstance instance)
    {
        foreach (ItemStack stack in AllStacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return true;
            }
        }

        return false;
    }

    private void AddToBag(ItemInstance instance, int quantity)
    {
        foreach (ItemStack stack in _materials)
        {
            if (stack.Item.Id == instance.TemplateId)
            {
                stack.Quantity += quantity;
                return;
            }
        }

        _materials.Add(new ItemStack(instance, quantity));
    }

    /// <summary>
    /// Removes exactly one unit of a specific instance (by reference), used to pull
    /// a rolled item out for equipping. Returns the stack's instance on success so
    /// the caller keeps its affixes; null if the instance wasn't found.
    /// </summary>
    public ItemInstance? RemoveOneInstance(ItemInstance instance)
    {
        return RemoveOneFrom(_stacks, instance) ?? RemoveOneFrom(_materials, instance);
    }

    private ItemInstance? RemoveOneFrom(List<ItemStack> store, ItemInstance instance)
    {
        for (int i = 0; i < store.Count; i++)
        {
            if (!ReferenceEquals(store[i].Instance, instance))
            {
                continue;
            }

            ItemInstance held = store[i].Instance;
            store[i].Quantity--;
            if (store[i].Quantity <= 0)
            {
                store.RemoveAt(i);
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
        if (instance?.Template is not ConsumableItemResource consumable)
        {
            return false;
        }

        if (Entity is not { } owner || owner.GetComponent<StatsComponent>() is not { IsAlive: true })
        {
            return false;
        }

        // Refused before anything is spent, and said out loud to the player: a potion that silently
        // does nothing reads as a dead key.
        ConsumeRefusal refusal = Holds(instance) ? ConsumableEffectsComponent.Check(owner, consumable) : ConsumeRefusal.None;
        if (refusal != ConsumeRefusal.None)
        {
            if (CombatPerspective.IsPlayer(owner))
            {
                EventBus.Instance?.Publish(new WorldHazardNoticeEvent(ConsumableRules.ReasonKey(refusal)));
            }

            return false;
        }

        // Secure the exact held unit before publishing healing events. An instance from a
        // previous load (or another bag) must not heal for free just because its template
        // is still present, and a reentrant heal listener must not consume this unit twice.
        if (RemoveOneInstance(instance) == null)
        {
            return false;
        }

        ConsumableEffectsComponent.Apply(owner, consumable);

        Log.Info($"Consumed {consumable.DisplayName}.");
        return true;
    }

    /// <summary>Why <paramref name="instance"/> cannot be used right now (cooldown, a full
    /// resource, nothing to cure), or <see cref="ConsumeRefusal.None"/>. For a UI that greys the
    /// button; <see cref="ConsumableRules.ReasonKey"/> turns the answer into a locale key.</summary>
    public ConsumeRefusal CanConsume(ItemInstance? instance)
    {
        return instance?.Template is ConsumableItemResource consumable && Entity is { } owner
            ? ConsumableEffectsComponent.Check(owner, consumable)
            : ConsumeRefusal.None;
    }

    // --- Room, and what may leave --------------------------------------------

    /// <summary>Empty pack slots. 0 when the pack is at or over <see cref="Capacity"/>.</summary>
    public int FreeSlots => Mathf.Max(0, Capacity - _stacks.Count);

    /// <summary>How many new pack slots storing <paramref name="quantity"/> of
    /// <paramref name="instance"/> would take, after topping up the stacks it can join. 0 for
    /// anything the material bag takes.</summary>
    public int SlotsNeededFor(ItemInstance instance, int quantity)
    {
        if (UseMaterialBag && IsBagItem(instance))
        {
            return 0;
        }

        int mergeSpace = 0;
        if (instance.IsStackable)
        {
            foreach (ItemStack stack in _stacks)
            {
                if (stack.Instance.CanStackWith(instance))
                {
                    mergeSpace += stack.SpaceLeft;
                }
            }
        }

        return InventoryRules.SlotsNeeded(quantity, instance.MaxStack, mergeSpace);
    }

    /// <summary>True when <see cref="AddInstance"/> would store all of it. Ask this before taking
    /// something from somewhere it cannot be put back.</summary>
    public bool CanAccept(ItemInstance instance, int quantity = 1) =>
        SlotsNeededFor(instance, quantity) <= FreeSlots;

    /// <summary>Removes the whole stack holding exactly <paramref name="instance"/> (by reference)
    /// and returns how many units it held; 0 if this inventory does not hold it.</summary>
    public int TakeAll(ItemInstance instance)
    {
        int taken = TakeAllFrom(_stacks, instance);
        if (taken == 0)
        {
            taken = TakeAllFrom(_materials, instance);
        }

        if (taken > 0)
        {
            NotifyChanged();
        }

        return taken;
    }

    private static int TakeAllFrom(List<ItemStack> store, ItemInstance instance)
    {
        for (int i = 0; i < store.Count; i++)
        {
            if (ReferenceEquals(store[i].Instance, instance))
            {
                int quantity = store[i].Quantity;
                store.RemoveAt(i);
                return quantity;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether the player may be allowed to lose <paramref name="instance"/> by an action that asks
    /// them: selling, salvaging, dropping, marking as junk. A <see cref="ItemInstance.Locked"/> item
    /// may not. Every such action checks this first; <see cref="RemoveItem(string, int)"/> does not,
    /// because a quest turn-in or a recipe is not the player throwing something away by mistake.
    /// </summary>
    public static bool CanDispose(ItemInstance? instance) => instance is { Locked: false };

    public void Clear()
    {
        if (_stacks.Count == 0 && _materials.Count == 0)
        {
            return;
        }

        _stacks.Clear();
        _materials.Clear();
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

        var data = new Godot.Collections.Dictionary { ["stacks"] = stacks };

        // Additive key, written only when the bag holds something: an inventory that never used the
        // bag saves exactly what it always did.
        if (_materials.Count > 0)
        {
            var materials = new Godot.Collections.Array();
            foreach (ItemStack stack in _materials)
            {
                materials.Add(new Godot.Collections.Dictionary
                {
                    ["instance"] = stack.Instance.Save(),
                    ["qty"] = stack.Quantity,
                });
            }

            data[MaterialsKey] = materials;
        }

        return data;
    }

    /// <summary>Save key of the material bag's stacks (same entry shape as <c>stacks</c>).</summary>
    public const string MaterialsKey = "materials";

    /// <summary>
    /// ⚠️ <b>A RESTORE IGNORES <see cref="Capacity"/>, AND IT HAS TO.</b> The contents used to come
    /// back through the ordinary <see cref="AddInstance"/> path, which stops at the slot limit and
    /// returns how much it dropped — a number this method discarded. So any change that made the
    /// pack smaller than the save deleted the difference, permanently and silently, on the next
    /// load: a rebalance of the default <see cref="Capacity"/>, a save written while a since-removed
    /// bonus was granting slots, a hand-edited pack. The player's response to "my things are gone"
    /// is to reload, which does it again.
    ///
    /// What the save held is what the player owned. It comes back whole and the pack is allowed to
    /// be over its limit until they make room — which is exactly what a full pack already does to
    /// every new pickup, and is recoverable. Deletion is not.
    ///
    /// <b>The material bag follows the same rule in both directions.</b> Every entry of both lists
    /// goes back through <see cref="AddInstance"/>, which is what sorts it: with
    /// <see cref="UseMaterialBag"/> on, a pre-bag save's materials leave <c>stacks</c> for the bag
    /// (the migration); with it off, a bag written by another build lands in the pack. An absent
    /// <c>materials</c> key is an empty bag, and the bag is cleared first either way.
    /// </summary>
    public void Load(Godot.Collections.Dictionary data)
    {
        _stacks.Clear();
        _materials.Clear();

        _ignoreCapacity = true;
        try
        {
            RestoreStacks(data, "stacks");
            RestoreStacks(data, MaterialsKey);
        }
        finally
        {
            _ignoreCapacity = false;
        }

        if (_stacks.Count > Capacity)
        {
            Log.Warn($"{Entity?.DisplayName ?? "An inventory"} restored {_stacks.Count} stack(s) into " +
                     $"{Capacity} slot(s); it is over capacity until the player makes room. Nothing " +
                     "was discarded.");
        }

        NotifyChanged();
    }

    private void RestoreStacks(Godot.Collections.Dictionary data, string key)
    {
        if (!data.TryGetValue(key, out Variant stacksVariant))
        {
            return;
        }

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
}
