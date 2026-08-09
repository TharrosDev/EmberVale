using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Save;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// What the player has put on consignment and what it has earned (Phase 38P). Shaped on
/// <see cref="ContrabandImpound"/>: one node, registered with both the <see cref="ServiceLocator"/>
/// and the <see cref="SaveManager"/>, unregistered from both.
///
/// <b>The net payout is stamped when the item is listed</b>, which is the one place this improves on
/// the impound's ledger it is otherwise copied from. That one holds template ids and hands goods back
/// plain, so an affixed instance would return without its affixes; here the item is <em>sold</em> and
/// never comes back, so pricing it while it still exists means a rolled Legendary consigns losslessly
/// and no <c>ItemInstance</c> blob has to survive a second save path.
///
/// <b>Nothing ticks.</b> A listing matures because enough days had passed by the time the player
/// walked up to the clerk — the same lazy-on-read rule <see cref="ShopStockService"/>'s restock
/// follows, and for the same reason: nothing in the game can observe the difference, and a
/// <c>_Process</c> cooldown that is not <c>ISaveable</c> is how <c>WorldEventDirector</c> lost its.
///
/// ponytail: every listing eventually sells — there is no failed-to-sell path and nothing to reclaim.
/// Add one if consignment is ever meant to carry risk rather than only time.
/// </summary>
[GlobalClass]
public partial class ConsignmentLedger : Node, ISaveable
{
    public string SaveId => "consignment";

    /// <summary>One thing on the broker's shelf. A record rather than a class because nothing mutates
    /// it: a listing is created, matures, and is paid out whole.</summary>
    private readonly record struct Listing(
        string ShopId,
        string TemplateId,
        int Quantity,
        int NetPerUnit,
        int DayListed,
        int Days)
    {
        public int Gold => NetPerUnit * Quantity;

        public bool Sold(int currentDay) => ConsignmentRules.HasSold(DayListed, currentDay, Days);
    }

    private readonly List<Listing> _listings = new();

    /// <summary>Gold owed for goods already sold and paid for but which would not fit in the player's
    /// pack. Kept separate from the listings because it is no longer a listing — the item is gone and
    /// only the coin is outstanding.</summary>
    private int _owed;

    /// <summary>Bumped on any change, so the vendor window knows to rebuild — the same signal
    /// <see cref="ShopStockService.Revision"/> gives.</summary>
    public int Revision { get; private set; }

    /// <summary>How many things are on the shelf, sold or not. Read by the vendor window's header.</summary>
    public int Pending => _listings.Count;

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

    /// <summary>Puts an item on the shelf. The caller has already taken it out of the pack — this
    /// records what it will be worth and when, and nothing else.</summary>
    public void Add(string shopId, string templateId, int quantity, int netPerUnit, int currentDay, int days)
    {
        if (quantity <= 0 || netPerUnit <= 0)
        {
            return; // the panel disables the row; re-checked here so a bad call cannot record a ghost
        }

        _listings.Add(new Listing(shopId, templateId, quantity, netPerUnit, currentDay, days));
        Revision++;
        Log.Info($"Consignment: listed {quantity}x '{templateId}' at {shopId} for {netPerUnit}g each, " +
            $"selling on day {currentDay + days}.");
    }

    /// <summary>Gold the player could walk away with right now — matured listings plus anything still
    /// owed from a payout that would not fit. Read by the clerk's prompt, so what it says and what the
    /// press pays cannot drift.</summary>
    public int DueGold(int currentDay)
    {
        int gold = _owed;
        foreach (Listing listing in _listings)
        {
            if (listing.Sold(currentDay))
            {
                gold += listing.Gold;
            }
        }

        return gold;
    }

    /// <summary>
    /// Pays out everything that has sold and returns the gold actually delivered.
    ///
    /// ⚠️ <b>A short payment is recorded, not lost.</b> Gold is an ordinary stack and a full pack can
    /// refuse it, so whatever does not fit stays owed and the next visit finishes the job — the same
    /// rule <see cref="ContrabandImpound.ReturnTo"/> follows for goods, and the reason
    /// <c>VendorPanel.Sell</c> refunds a purse rather than paying part of a sale.
    ///
    /// Matured listings are cleared <em>before</em> the add so the amount owed is computed once; the
    /// unsold ones are kept, which is what makes a second visit for the rest of the shelf work.
    /// </summary>
    public int Collect(int currentDay, InventoryComponent pack)
    {
        if (ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return 0;
        }

        int due = DueGold(currentDay);
        if (due <= 0)
        {
            return 0;
        }

        _listings.RemoveAll(listing => listing.Sold(currentDay));
        _owed = 0;

        int paid = pack.AddItem(gold, due);
        if (paid < due)
        {
            _owed = due - paid;
            Log.Warn($"Consignment: paid {paid}g of {due}g; {_owed}g is still owed and stays on the books.");
        }
        else
        {
            Log.Info($"Consignment: paid out {paid}g; {_listings.Count} listing(s) still unsold.");
        }

        Revision++;
        return paid;
    }

    public Godot.Collections.Dictionary Save()
    {
        var listings = new Godot.Collections.Array();
        foreach (Listing listing in _listings)
        {
            listings.Add(new Godot.Collections.Dictionary
            {
                ["shop"] = listing.ShopId,
                ["item"] = listing.TemplateId,
                ["qty"] = listing.Quantity,
                ["net"] = listing.NetPerUnit,
                ["day"] = listing.DayListed,
                ["days"] = listing.Days,
            });
        }

        return new Godot.Collections.Dictionary { { "listings", listings }, { "owed", _owed } };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). A quickload onto a save taken before anything was listed must
        // empty both of these — otherwise gold earned in a timeline being abandoned is still
        // collectable in the one being restored, which is a money printer with a save button.
        _listings.Clear();
        _owed = 0;
        Revision++;

        if (data.TryGetValue("owed", out Variant owed))
        {
            _owed = owed.AsInt32();
        }

        if (!data.TryGetValue("listings", out Variant listingsVariant) ||
            listingsVariant.VariantType != Variant.Type.Array)
        {
            return;
        }

        foreach (Variant entry in listingsVariant.AsGodotArray())
        {
            if (entry.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            var listing = new Listing(
                ShopId: Read(row, "shop"),
                TemplateId: Read(row, "item"),
                Quantity: ReadInt(row, "qty"),
                NetPerUnit: ReadInt(row, "net"),
                DayListed: ReadInt(row, "day"),
                Days: ReadInt(row, "days"));

            if (listing.Quantity > 0 && listing.NetPerUnit > 0)
            {
                _listings.Add(listing);
            }
        }
    }

    private static string Read(Godot.Collections.Dictionary row, string key) =>
        row.TryGetValue(key, out Variant value) ? value.AsString() : string.Empty;

    private static int ReadInt(Godot.Collections.Dictionary row, string key) =>
        row.TryGetValue(key, out Variant value) ? value.AsInt32() : 0;
}
