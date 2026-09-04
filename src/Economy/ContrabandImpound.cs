using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Save;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// What the Crossway wardens have taken off the player and have not yet given back (Phase 38O).
/// Shaped on <see cref="ShopStockService"/>: one node, registered with both the
/// <see cref="ServiceLocator"/> and the <see cref="SaveManager"/>, unregistered from both.
///
/// <b>Confiscation has to be recoverable or it is theft with extra steps</b>, which is why the ledger
/// exists at all: the alternative — deleting the goods on the spot — makes carrying contraband a
/// coin-flip the player cannot price, and 38H's ruling against a hard cap applies here too. A fine is
/// a decision; a deletion is a punishment.
///
/// ⚠️ <b>Goods are held by template id and handed back plain.</b> An affixed instance would come back
/// without its affixes. Nothing in the game currently rolls affixes onto a contraband item — all five
/// are stackable materials whose loot rows set <c>RollAffixes = false</c> — so this is lossless today.
/// ponytail: template ids, because a full instance ledger means persisting rolled affixes through a
/// second save path; store <c>ItemInstance.Save()</c> blobs here if contraband ever rolls.
/// </summary>
[GlobalClass]
public partial class ContrabandImpound : Node, ISaveable
{
    public string SaveId => "contraband_impound";

    /// <summary>Template id -> units held. Plain <see cref="Dictionary{TKey,TValue}"/> rather than a
    /// Godot one so the conversion happens only at the save boundary.</summary>
    private readonly Dictionary<string, int> _held = new();

    /// <summary>Total units held — what <see cref="ContrabandLaw.Fine"/> is charged against, and what
    /// the prompt reports.</summary>
    public int Units
    {
        get
        {
            int total = 0;
            foreach (int count in _held.Values)
            {
                total += count;
            }

            return total;
        }
    }

    public override void _EnterTree()
    {
        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>How many units of contraband a pack is carrying — what a search would take. Read by
    /// the prompt so "nothing to declare" and "the warden takes it" cannot disagree with each other,
    /// the rule <c>PropertyDeedComponent</c> set for a deed.</summary>
    public static int ContrabandIn(InventoryComponent pack)
    {
        int units = 0;
        foreach (ItemStack stack in pack.Stacks)
        {
            if (TradeTags.IsContraband(stack.Instance.Template.TagList()))
            {
                units += stack.Quantity;
            }
        }

        return units;
    }

    /// <summary>
    /// Takes every contraband stack out of <paramref name="pack"/> and records it. Returns the units
    /// taken.
    ///
    /// ⚠️ Iterates a <b>snapshot</b> of the stacks: <see cref="InventoryComponent.RemoveItem(string,int)"/>
    /// mutates the live list, and the same trap sank the first draft of <c>StoragePanel</c>'s Store.
    /// Removal is recorded only when it actually succeeded — a ledger entry for goods still in the
    /// player's pack would let one stack be redeemed twice.
    /// </summary>
    public int SeizeFrom(InventoryComponent pack)
    {
        int taken = 0;
        foreach (ItemStack stack in new List<ItemStack>(pack.Stacks))
        {
            if (!TradeTags.IsContraband(stack.Instance.Template.TagList()))
            {
                continue;
            }

            string id = stack.Instance.TemplateId;
            int quantity = stack.Quantity;
            if (!pack.RemoveItem(id, quantity))
            {
                Log.Warn($"Impound: could not seize {quantity}x '{id}'; left it where it was.");
                continue;
            }

            _held[id] = _held.GetValueOrDefault(id) + quantity;
            taken += quantity;
        }

        if (taken > 0)
        {
            Log.Info($"Impound: seized {taken} units of contraband; {Units} now held.");
        }

        return taken;
    }

    /// <summary>
    /// Hands back everything that fits, and reports how many units did not. The caller refunds the fine
    /// on the remainder — the player has paid for goods the wardens are still holding, which is
    /// <c>VendorPanel.Sell</c>'s purse rule seen from the other side of the counter.
    ///
    /// Anything that does not fit stays in the ledger, so a second visit with a lighter pack finishes
    /// the job. Recoverable means recoverable.
    /// </summary>
    public int ReturnTo(InventoryComponent pack)
    {
        var undelivered = new Dictionary<string, int>();
        int left = 0;

        foreach ((string id, int quantity) in _held)
        {
            if (ItemDatabase.Get(id) is not { } template)
            {
                // The item was removed from the catalogue between the save and now. Dropping it is the
                // only option, and it is worth a line rather than a silent vanish.
                Log.Warn($"Impound: holding {quantity}x unknown '{id}'; discarded.");
                continue;
            }

            int stored = pack.AddItem(template, quantity);
            if (stored < quantity)
            {
                undelivered[id] = quantity - stored;
                left += quantity - stored;
            }
        }

        _held.Clear();
        foreach ((string id, int quantity) in undelivered)
        {
            _held[id] = quantity;
        }

        Log.Info($"Impound: returned goods; {left} units would not fit and are still held.");
        return left;
    }

    public Godot.Collections.Dictionary Save()
    {
        var held = new Godot.Collections.Dictionary();
        foreach ((string id, int quantity) in _held)
        {
            held[id] = quantity;
        }

        return new Godot.Collections.Dictionary { { "held", held } };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). A save taken before anything was ever confiscated has an empty
        // ledger, and a quickload onto it must empty this one — otherwise goods seized in a timeline
        // being abandoned are still redeemable in the one being restored, which is free contraband.
        _held.Clear();

        if (!data.TryGetValue("held", out Variant heldVariant) ||
            heldVariant.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        foreach (KeyValuePair<Variant, Variant> pair in heldVariant.AsGodotDictionary())
        {
            string id = pair.Key.AsString();
            int quantity = pair.Value.AsInt32();
            if (!string.IsNullOrEmpty(id) && quantity > 0)
            {
                _held[id] = quantity;
            }
        }
    }
}
