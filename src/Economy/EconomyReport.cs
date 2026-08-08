using System.Collections.Generic;
using System.Text;
using Embervale.Core.Services;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Player;

namespace Embervale.Economy;

/// <summary>
/// The realm's price landscape, printed (Phase 38N1). One buy-low/sell-high table across every
/// authored shop: what the mine sells cheap that the market pays for, and what the market sells cheap
/// that the mine is starving for.
///
/// <b>It prices through the same calls <c>VendorPanel</c> makes</b> — <see cref="ShopPricing.BuyPrice"/>
/// over <see cref="ShopPricing.MarkupFor"/>, <see cref="ShopPricing.SellPrice"/> over
/// <see cref="ShopPricing.SellFractionFor"/>, with <see cref="TradeTags.Accepts"/> deciding whether a
/// merchant is a buyer at all. A report that computed its own prices would advertise routes the game
/// then refuses to honour, which is worse than no report: 38C's travel fee shipped a first draft that
/// resolved the region two different ways and would have shown a price it did not charge.
///
/// ⚠️ <b>It deliberately ignores two things that move a real payout</b>, and says so in its own
/// output rather than quietly: 38H's saturation (the price falls as you dump a stack) and 38C's purse
/// (a merchant runs out of coin). Both need live session state; a static table that pretended
/// otherwise would read as a promise.
///
/// Reached two ways, deliberately: the <c>economy</c> dev console command, and
/// <c>--economy</c> headless (<see cref="Bootstrap.HeadlessEconomy"/>). The console cannot be driven
/// from a remote session at all — CLAUDE.md §3 — so a console-only report would have shipped
/// unexercised, which is the shape of every dead feature this project has had to dig out.
/// </summary>
public static class EconomyReport
{
    /// <summary>How many routes the table prints. Anything past this is noise: the tail is the same
    /// goods at a coin either way.</summary>
    private const int DefaultLimit = 15;

    private readonly struct Route
    {
        public Route(string itemName, string from, int buy, string to, int sell)
        {
            ItemName = itemName;
            From = from;
            Buy = buy;
            To = to;
            Sell = sell;
        }

        public string ItemName { get; }

        public string From { get; }

        public int Buy { get; }

        public string To { get; }

        public int Sell { get; }

        public int Margin => Sell - Buy;
    }

    /// <summary>
    /// Builds the arbitrage table: for every item any shop stocks, the cheapest place to buy it and
    /// the best place to sell it, ranked by margin.
    /// </summary>
    public static string Arbitrage(int limit = DefaultLimit)
    {
        var routes = new List<Route>();

        foreach (KeyValuePair<string, ItemResource> pair in ItemDatabase.All)
        {
            ItemResource item = pair.Value;
            if (!ShopPricing.Sellable(item.Type, pair.Key == Core.GameIds.Currency.Gold))
            {
                continue; // a quest object or coin has no market; 38A's rule, not a special case here
            }

            List<string> tags = item.TagList();
            if (TryBestRoute(pair.Key, item, tags, out Route route))
            {
                routes.Add(route);
            }
        }

        routes.Sort((a, b) => b.Margin.CompareTo(a.Margin));

        var text = new StringBuilder();
        text.AppendLine("=== Embervale arbitrage — buy low, sell high ===");
        text.AppendLine($"{routes.Count} tradeable goods across {ShopDatabase.All.Count} shops. " +
            "Prices include standing and trade specialties.");
        text.AppendLine("Ignores 38H saturation (the price falls as you sell a stack) and 38C purses " +
            "(a merchant runs out of coin), so a real run pays less than the tail of this table.");

        // ⚠️ 38N1's finding, printed rather than left for someone to rediscover from a table of
        // negative numbers. ShopPricing.BuyPrice clamps its markup to >= 1 and SellPrice clamps its
        // fraction to 0..1, so sell <= value <= buy holds at EVERY shop — which means buying from one
        // merchant and selling to another can never turn a profit, whatever the content says. Carrying
        // goods only pays once something moves an item's *value* by settlement, which is 38G.
        if (routes.Count > 0 && routes[0].Margin <= 0)
        {
            text.AppendLine("No route turns a profit, and none can yet: ShopPricing clamps every " +
                "markup to >= 1 and every sell fraction to <= 1, so sell <= value <= buy holds at " +
                "each shop and a carry between two of them is always a loss. What is ranked below is " +
                "the CHEAPEST way to be wrong. Regional demand (38G) is what moves an item's value " +
                "per settlement and turns these positive.");
        }

        text.AppendLine();

        int shown = 0;
        foreach (Route route in routes)
        {
            if (shown++ >= limit)
            {
                text.AppendLine($"... and {routes.Count - limit} more, all at a thinner margin.");
                break;
            }

            string margin = route.Margin >= 0 ? $"+{route.Margin}" : route.Margin.ToString();
            text.AppendLine(
                $"{margin,5}  {route.ItemName,-22} buy {route.Buy,4} at {route.From,-28} " +
                $"sell {route.Sell,4} to {route.To}");
        }

        return text.ToString();
    }

    /// <summary>
    /// The best <b>two-shop</b> route for one item: cheapest seller, best buyer, and never the same
    /// merchant on both ends.
    ///
    /// ⚠️ Excluding the self-pair is the difference between a route and a rounding error. Buying a
    /// thing from Aldreth and selling it straight back to Aldreth is not arbitrage, it is the spread,
    /// and the first draft of this table led with three of them. Because the buy price depends only on
    /// the seller and the sell price only on the buyer, the best distinct pair is found by keeping the
    /// two cheapest sellers and the two best buyers and taking whichever pairing is legal.
    ///
    /// A <c>LeveledTable</c> row is not a seller: it rolls a different item every restock, so there is
    /// no price to quote.
    /// </summary>
    private static bool TryBestRoute(string itemId, ItemResource item, List<string> tags, out Route route)
    {
        route = default;
        (string Shop, int Price) buy1 = (string.Empty, int.MaxValue);
        (string Shop, int Price) buy2 = (string.Empty, int.MaxValue);
        (string Shop, int Price) sell1 = (string.Empty, int.MinValue);
        (string Shop, int Price) sell2 = (string.Empty, int.MinValue);

        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (IsHostile(shop.FactionId))
            {
                continue;
            }

            bool specialty = TradeTags.IsSpecialty(tags, shop.SpecialtyList());
            ReputationTier tier = TierOf(shop.FactionId);

            if (Stocks(shop, itemId))
            {
                int price = ShopPricing.BuyPrice(item.Value, ShopPricing.MarkupFor(shop.BuyMarkup, tier, specialty));
                if (price < buy1.Price)
                {
                    buy2 = buy1;
                    buy1 = (shop.Id, price);
                }
                else if (price < buy2.Price)
                {
                    buy2 = (shop.Id, price);
                }
            }

            if (TradeTags.Accepts(tags, shop.AcceptedTagList()))
            {
                int price = ShopPricing.SellPrice(item.Value, ShopPricing.SellFractionFor(shop.SellFraction, specialty));
                if (price > sell1.Price)
                {
                    sell2 = sell1;
                    sell1 = (shop.Id, price);
                }
                else if (price > sell2.Price)
                {
                    sell2 = (shop.Id, price);
                }
            }
        }

        if (buy1.Shop.Length == 0 || sell1.Shop.Length == 0)
        {
            return false; // nobody sells it, or nobody deals in it
        }

        if (buy1.Shop != sell1.Shop)
        {
            route = new Route(item.DisplayName, buy1.Shop, buy1.Price, sell1.Shop, sell1.Price);
            return true;
        }

        // The same merchant is both ends, so take the better of the two legal fallbacks.
        bool useSecondBuyer = buy2.Shop.Length > 0;
        bool useSecondSeller = sell2.Shop.Length > 0;
        int viaSecondBuyer = useSecondBuyer ? sell1.Price - buy2.Price : int.MinValue;
        int viaSecondSeller = useSecondSeller ? sell2.Price - buy1.Price : int.MinValue;

        if (!useSecondBuyer && !useSecondSeller)
        {
            return false; // one shop is the entire market for this good
        }

        route = viaSecondBuyer >= viaSecondSeller
            ? new Route(item.DisplayName, buy2.Shop, buy2.Price, sell1.Shop, sell1.Price)
            : new Route(item.DisplayName, buy1.Shop, buy1.Price, sell2.Shop, sell2.Price);
        return true;
    }

    private static bool Stocks(ShopResource shop, string itemId)
    {
        foreach (Godot.GodotObject? entry in shop.Stock)
        {
            if (entry is ShopStockEntry row && row.ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The player's standing with a shop's faction, or <see cref="ReputationTier.Neutral"/> when there
    /// is no player — which is the headless case, and the reason the table is stable enough to diff.
    /// Matches <c>VendorComponent</c>'s inverted default: an unresolvable standing trades normally.
    /// </summary>
    private static ReputationTier TierOf(string factionId) =>
        string.IsNullOrEmpty(factionId) || Reputation() is not { } reputation
            ? ReputationTier.Neutral
            : reputation.TierOf(factionId);

    private static bool IsHostile(string factionId) =>
        !string.IsNullOrEmpty(factionId) && Reputation() is { } reputation && reputation.IsHostile(factionId);

    private static ReputationComponent? Reputation() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<ReputationComponent>()
            : null;
}
