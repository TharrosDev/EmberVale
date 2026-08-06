using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Economy;

/// <summary>One row of what a shop currently has. <see cref="Remaining"/> below zero is unlimited —
/// the authored <c>Quantity = 0</c> case, kept distinct from a genuine 0 so a sold-out row and a
/// bottomless one cannot be confused by a caller doing arithmetic.</summary>
public readonly record struct ShopOffer(ItemInstance Instance, int Remaining)
{
    public bool Unlimited => Remaining < 0;

    public bool Available => Remaining != 0;
}

/// <summary>
/// What every shop currently holds (Phase 38B) — remaining counts, the day each shop last restocked,
/// and the instances its leveled pool rolled. Shaped on <see cref="Housing.HousingService"/>: one
/// node, registered with both the <see cref="ServiceLocator"/> and the <see cref="SaveManager"/> and
/// unregistered from both.
///
/// State lives here rather than on <see cref="ShopResource"/> because that resource is shared by every
/// vendor naming it and is not <c>ISaveable</c> — a remaining count written into it would leak between
/// merchants and evaporate on reload.
///
/// <b>Restock is evaluated when a shop is opened, not on a tick.</b> No <c>_Process</c>, no
/// <c>TimeOfDayChangedEvent</c> subscription, no day-changed event to add: a shop restocks because
/// enough days had passed by the time the player walked up to it, and nothing in the game can observe
/// the difference. <c>WorldEventDirector</c> is the cautionary case — it ticks real-seconds cooldowns
/// every frame and is not <c>ISaveable</c>, so they vanish on reload.
///
/// <b>The rolled leveled stock persists</b>, which is the whole reason it is in the save at all: if a
/// reload rerolled the pool, a player would reload until a Legendary appeared.
/// </summary>
[GlobalClass]
public partial class ShopStockService : Node, ISaveable
{
    public string SaveId => "shopstock";

    /// <summary>What one shop is holding right now.</summary>
    private sealed class ShopState
    {
        /// <summary>Remaining units per authored item id. Absent means never stocked or unlimited.</summary>
        public readonly Dictionary<string, int> Remaining = new();

        /// <summary>What the leveled pool rolled at the last restock, held by reference so a sale can
        /// remove the exact instance the player is looking at.</summary>
        public readonly List<ItemInstance> Rolled = new();

        public int LastRestockDay { get; set; } = int.MinValue;

        /// <summary>Gold the merchant has left to buy with; <c>-1</c> when the shop authors no purse.</summary>
        public int Purse { get; set; } = ShopStock.UnlimitedPurse;

        public bool Stocked { get; set; }
    }

    private readonly Dictionary<string, ShopState> _shops = new();

    /// <summary>Bumped on any change, so the shop window knows to rebuild — the same signal
    /// <c>HousingService.Revision</c> gives.</summary>
    public int Revision { get; private set; }

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    /// <summary>
    /// What the shop is offering, restocking first if its clock says so. The authored rows come first
    /// in authoring order, then anything the leveled pool rolled — a merchant's staples should not
    /// move around under the player because a roll came up short.
    /// </summary>
    public IReadOnlyList<ShopOffer> OfferFor(ShopResource shop)
    {
        ShopState state = StateFor(shop);
        var offers = new List<ShopOffer>();

        foreach (ShopStockEntry entry in shop.StockList())
        {
            if (string.IsNullOrEmpty(entry.ItemId) || ItemDatabase.Get(entry.ItemId) is not { } template)
            {
                continue; // --validate fails the build on this; the window just does not list it
            }

            int remaining = entry.Quantity <= 0
                ? -1
                : state.Remaining.GetValueOrDefault(entry.ItemId, entry.Quantity);
            offers.Add(new ShopOffer(ItemInstance.Plain(template), remaining));
        }

        foreach (ItemInstance rolled in state.Rolled)
        {
            offers.Add(new ShopOffer(rolled, 1));
        }

        return offers;
    }

    /// <summary>
    /// Takes one unit off the shelf. Returns false when it was not there to take, so a caller that has
    /// already charged the player can tell the difference between a sale and a race.
    ///
    /// An unlimited row succeeds without recording anything — the absence of a key <em>is</em> the
    /// unlimited state, so writing one would quietly make it finite.
    /// </summary>
    public bool TakeOne(ShopResource shop, ItemInstance instance)
    {
        ShopState state = StateFor(shop);

        // A rolled instance is one-of-a-kind and is matched by reference, for the same reason
        // InventoryComponent.RemoveOneInstance exists: two rolls of one template are different items.
        if (state.Rolled.Remove(instance))
        {
            Revision++;
            return true;
        }

        foreach (ShopStockEntry entry in shop.StockList())
        {
            if (entry.ItemId != instance.TemplateId)
            {
                continue;
            }

            if (entry.Quantity <= 0)
            {
                return true; // unlimited — nothing to decrement
            }

            int remaining = state.Remaining.GetValueOrDefault(entry.ItemId, entry.Quantity);
            if (remaining <= 0)
            {
                return false;
            }

            state.Remaining[entry.ItemId] = remaining - 1;
            Revision++;
            return true;
        }

        return false;
    }

    /// <summary>What the merchant has left to spend, or <c>-1</c> for an unlimited purse (38C).</summary>
    public int PurseFor(ShopResource shop) => StateFor(shop).Purse;

    /// <summary>
    /// Spends from the merchant's purse. Returns false when they are short, so a sale is refused rather
    /// than paid part-way — half-paying for an item is the same class of bug as 38A's zero payout, and
    /// the player would have handed over the goods either way.
    /// </summary>
    public bool TakePurse(ShopResource shop, int amount)
    {
        ShopState state = StateFor(shop);

        if (!ShopStock.CanCover(state.Purse, amount))
        {
            return false;
        }

        state.Purse = ShopStock.AfterSpend(state.Purse, amount);
        Revision++;
        return true;
    }

    /// <summary>Puts gold back in the purse after a sale that debited it and then could not complete.
    /// Never pushes the purse above what the shop authored — a failed sale must not mint the merchant
    /// money, which is the mirror of the buy path's refund never minting the player any.</summary>
    public void RefundPurse(ShopResource shop, int amount)
    {
        ShopState state = StateFor(shop);
        state.Purse = ShopStock.AfterRefund(state.Purse, amount, shop.PurseGold);
        Revision++;
    }

    /// <summary>Restocks now regardless of the clock; the <c>shop restock</c> dev command's whole
    /// purpose, since an in-game day is <c>DayLengthSeconds</c> of real waiting.</summary>
    public void ForceRestock(ShopResource shop) => Restock(shop, StateFor(shop));

    /// <summary>Resolves a shop's state, restocking it if it has never been stocked or its clock is
    /// due. Every read goes through here, which is what makes the lazy restock invisible.</summary>
    private ShopState StateFor(ShopResource shop)
    {
        if (!_shops.TryGetValue(shop.Id, out ShopState? state))
        {
            state = new ShopState();
            _shops[shop.Id] = state;
        }

        if (!state.Stocked || ShopStock.IsRestockDue(state.LastRestockDay, CurrentDay(), shop.RestockDays))
        {
            Restock(shop, state);
        }

        return state;
    }

    private void Restock(ShopResource shop, ShopState state)
    {
        state.Remaining.Clear();
        state.Rolled.Clear();
        state.LastRestockDay = CurrentDay();
        state.Stocked = true;

        // The purse refills with the shelves — one clock, both directions of trade.
        state.Purse = shop.PurseGold > 0 ? shop.PurseGold : ShopStock.UnlimitedPurse;

        if (shop.LeveledTable != null)
        {
            // Rolled through an RNG that GD.Seed can actually reach, unlike LootGenerator's own shared
            // one (it calls Randomize()), so `seed <n>` + a repro scenario reproduces a restock.
            var rng = new RandomNumberGenerator { Seed = GD.Randi() };
            float quality = ShopStock.QualityForLevel(PlayerLevel());

            foreach (LootDrop drop in LootGenerator.Generate(shop.LeveledTable, rng, quality))
            {
                // Gold in a shop's pool would be the merchant selling coins; the validator rejects it
                // in authored stock and this covers the rolled path too.
                if (drop.Instance.TemplateId == GameIds.Currency.Gold)
                {
                    continue;
                }

                // One entry per unit: a rolled non-stackable is unique, so a quantity would imply
                // copies sharing affixes they never rolled together — and LootGenerator already emits
                // those one drop at a time.
                // ponytail: a *stackable* in a leveled pool therefore shows as N rows of 1 rather than
                // one row of N. Author stackables as static stock (they need no roll); give Rolled a
                // per-entry quantity if a pool ever genuinely wants them.
                for (int i = 0; i < drop.Quantity; i++)
                {
                    state.Rolled.Add(drop.Instance);
                }
            }

            Log.Info($"Shop '{shop.Id}' restocked (day {state.LastRestockDay}); {state.Rolled.Count} leveled ware(s) at quality {quality:0.00}.");
        }

        Revision++;
    }

    private static int CurrentDay() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock) ? clock.Day : 0;

    /// <summary>Falls back to level 1, which yields the floor quality — a missing player must never
    /// roll a merchant's best stock.</summary>
    private static int PlayerLevel()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
            player.GetComponent<ProgressionComponent>() is { } progression)
        {
            return progression.Level;
        }

        return 1;
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var shops = new Godot.Collections.Dictionary();
        foreach ((string id, ShopState state) in _shops)
        {
            var remaining = new Godot.Collections.Dictionary();
            foreach ((string itemId, int count) in state.Remaining)
            {
                remaining[itemId] = count;
            }

            var rolled = new Godot.Collections.Array();
            foreach (ItemInstance instance in state.Rolled)
            {
                rolled.Add(instance.Save());
            }

            shops[id] = new Godot.Collections.Dictionary
            {
                ["day"] = state.LastRestockDay,
                ["purse"] = state.Purse,
                ["remaining"] = remaining,
                ["rolled"] = rolled,
            };
        }

        return new Godot.Collections.Dictionary { ["shops"] = shops };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7): a shop absent from the save must come back unstocked and roll
        // fresh, not keep the remaining counts and rolled wares of the timeline being abandoned.
        //
        // ponytail: a save written *before* 38B has no "shopstock" entry at all, and SaveManager skips
        // a saveable it cannot find rather than resetting it — so a quickload onto a pre-38B save can
        // leave a shop looking bought out. It self-heals at the next restock (a day), and fixing it
        // properly needs a "no entry, reset yourself" hook SaveManager does not have. Add that hook if
        // a second service ever needs the same thing.
        _shops.Clear();

        if (data.TryGetValue("shops", out Variant shopsVariant) &&
            shopsVariant.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> pair in shopsVariant.AsGodotDictionary())
            {
                string id = pair.Key.AsString();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                _shops[id] = ReadState(pair.Value.AsGodotDictionary());
            }
        }

        Revision++;
    }

    private static ShopState ReadState(Godot.Collections.Dictionary entry)
    {
        var state = new ShopState
        {
            LastRestockDay = entry.TryGetValue("day", out Variant day) ? day.AsInt32() : int.MinValue,

            // Absent means a save from before 38C: -1 (unlimited) rather than 0, or every restored
            // merchant would read as broke until their next restock.
            Purse = entry.TryGetValue("purse", out Variant purse) ? purse.AsInt32() : ShopStock.UnlimitedPurse,

            // A restored shop counts as stocked even with nothing left, or reopening it would restock
            // a shop the player had legitimately bought out.
            Stocked = true,
        };

        if (entry.TryGetValue("remaining", out Variant remaining) &&
            remaining.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> pair in remaining.AsGodotDictionary())
            {
                state.Remaining[pair.Key.AsString()] = pair.Value.AsInt32();
            }
        }

        if (entry.TryGetValue("rolled", out Variant rolled) && rolled.VariantType == Variant.Type.Array)
        {
            foreach (Variant element in rolled.AsGodotArray())
            {
                if (ItemInstance.FromSave(element.AsGodotDictionary()) is { } instance)
                {
                    state.Rolled.Add(instance);
                }
            }
        }

        return state;
    }
}
