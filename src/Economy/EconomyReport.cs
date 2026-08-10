using System.Collections.Generic;
using System.Text;
using Embervale.Core.Services;
using Embervale.Crafting;
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
/// <summary>
/// One merchant's offer for one item: who, and how much (38P2). "No such buyer" is
/// <see cref="Has"/> being false — kept distinct from a price of <c>0</c>, which is a real merchant
/// who values the thing at nothing.
///
/// ⚠️ <b>Ask <see cref="Has"/>, never <c>Shop.Length</c>.</b> <c>default(Offer)</c> bypasses the
/// primary constructor, so <see cref="Shop"/> is <c>null</c> in the empty case however carefully the
/// constructor is written — a defensive <c>?? string.Empty</c> on the property does not run either,
/// which is exactly the false confidence that shipped an NRE into <c>--economy</c> mid-38P2 and hung
/// the headless run.
/// </summary>
public readonly record struct Offer(string Shop, int Price)
{
    public bool Has => !string.IsNullOrEmpty(Shop);
}

/// <summary>What a broker would list an item for (38P2): the net per unit after her commission, and
/// the days it takes to sell. <see cref="Has"/> false means nobody will take it on consignment —
/// same null caveat as <see cref="Offer"/>.</summary>
public readonly record struct ConsignQuote(string Shop, int Net, int Days)
{
    public bool Has => !string.IsNullOrEmpty(Shop);
}

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
            text.AppendLine("No route turns a profit: ShopPricing clamps every markup to >= 1 and " +
                "every sell fraction to <= 1, so sell <= local value <= buy holds at each shop and a " +
                "carry between two of them loses money. What is ranked below is the CHEAPEST way to " +
                "be wrong. 38G's settlement demand is authored on the cells — if this line is showing, " +
                "no pair of surplus and demand tags is wide enough to clear a specialist's spread.");
        }
        else if (routes.Count > 0)
        {
            // 38G. The clamps still hold AT A SHOP — both sides of one counter spread over the same
            // local value — so this is not the money printer 38A closed; it is the one thing in the
            // economy that pays for walking, and it pays only where a surplus faces a demand.
            int paying = 0;
            foreach (Route r in routes)
            {
                if (r.Margin > 0)
                {
                    paying++;
                }
            }

            text.AppendLine($"{paying} route(s) turn a profit, and they are the whole point of 38G: " +
                "an item's value now moves by settlement, so buying where a good is a surplus and " +
                "selling where it is in demand pays for the walk. sell <= local value <= buy still " +
                "holds at every counter, so nothing here is a loop — a round trip at ONE shop still " +
                "costs, and the profit is the carry.");
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

        text.Append(Brokers());
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

        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (IsHostile(shop.FactionId) || !Stocks(shop, itemId))
            {
                continue;
            }

            bool specialty = TradeTags.IsSpecialty(tags, shop.SpecialtyList());
            int price = ShopPricing.BuyPrice(
                shop.LocalValue(item.Value, tags),
                ShopPricing.MarkupFor(shop.BuyMarkup, TierOf(shop.FactionId), specialty));
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

        BestBuyers(item, tags, out Offer sell1, out Offer sell2);

        if (buy1.Shop.Length == 0 || !sell1.Has)
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
        bool useSecondSeller = sell2.Has;
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

    /// <summary>
    /// What the realm's brokers pay, appended to the arbitrage table (38P2).
    ///
    /// ⚠️ <b>The table above cannot carry it, and that is why this is a separate block.</b> Every row
    /// up there is a two-shop <em>route</em> with a margin; a consignment is one counter, paid days
    /// later, with no second end to subtract. Forcing it into a route column would have meant either a
    /// fake buy price or a margin that means something different on one row than on all the others.
    ///
    /// Without this the report omitted the best payout in the realm — 38P shipped that gap knowingly
    /// and recorded it in <c>NOW.md</c>, because the extraction it needed is this sub-phase's job.
    /// </summary>
    private static string Brokers()
    {
        var houses = new List<ShopResource>();
        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (shop.IsConsignment)
            {
                houses.Add(shop);
            }
        }

        if (houses.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("=== On consignment — paid later, and better than any counter ===");

        foreach (ShopResource house in houses)
        {
            int perHundred = ConsignmentRules.Net(
                ConsignmentRules.Gross(100, house.ConsignFraction), house.ConsignCommission);
            text.AppendLine(
                $"{house.Id,-34} {perHundred,3}g per 100 of value in {house.ConsignDays}d, " +
                $"takes {string.Join(", ", house.AcceptedTagList())}");
        }

        text.AppendLine("No purse and no saturation apply here, so a whole stack lists at once — but " +
            "the payout is still capped at an item's value (ShopPricing.SellPrice), so this is the " +
            "best price in the realm and still not arbitrage.");
        return text.ToString();
    }

    /// <summary>
    /// The two best outright buyers for one item, ranked, at the player's standing. Empty
    /// <see cref="Offer.Shop"/> means there is no such buyer.
    ///
    /// <b>Extracted in 38P2 so the arbitrage table and the appraiser share one authority</b> on what a
    /// merchant will pay. It returns <em>two</em> because <see cref="TryBestRoute"/> excludes the
    /// self-pair and needs a fallback; a single-best version would have forced a second pass over
    /// every shop for the one item where it matters.
    ///
    /// ⚠️ <b>A consignment house is skipped, and that is a defect fix rather than a refinement</b>
    /// (38P2). A broker's <see cref="ShopResource.SellFraction"/> is inert data — <c>VendorPanel</c>
    /// branches on <see cref="ShopResource.IsConsignment"/> and never reads it — but this loop used to
    /// quote it like any other counter, so the report offered a sale at a price the game would refuse
    /// to make. That is precisely the failure this class's own header warns about. Her real offer is
    /// <see cref="BestConsignment"/>, which is a different shape: it pays later and takes a cut.
    /// </summary>
    /// <param name="view">Which day to price for (38T). <c>ContentValidator</c> passes
    /// <see cref="PriceView.Peak"/> so the rules measured against "what the best buyer pays" are proved
    /// against the keenest buyer a supply shock can ever produce, not the one standing there today.</param>
    public static void BestBuyers(
        ItemResource item, List<string> tags, out Offer first, out Offer second,
        PriceView view = PriceView.Today)
    {
        first = default;
        second = default;

        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (shop.IsConsignment || IsHostile(shop.FactionId) ||
                !TradeTags.Accepts(tags, shop.AcceptedTagList()))
            {
                continue;
            }

            bool specialty = TradeTags.IsSpecialty(tags, shop.SpecialtyList());
            int price = ShopPricing.SellPrice(
                shop.LocalValue(item.Value, tags, view),
                ShopPricing.SellFractionFor(shop.SellFraction, specialty));

            if (!first.Has || price > first.Price)
            {
                second = first;
                first = new Offer(shop.Id, price);
            }
            else if (!second.Has || price > second.Price)
            {
                second = new Offer(shop.Id, price);
            }
        }
    }

    /// <summary>
    /// The best broker quote for one item (38P2): what the player is paid per unit once the house has
    /// taken its cut, and how many days it takes. Empty <see cref="ConsignQuote.Shop"/> means no
    /// broker in the realm will take it.
    ///
    /// <b>It prices through <see cref="ConsignmentRules"/>, the same two calls
    /// <c>VendorPanel.BuildPack</c> makes</b> — 38P's carried lesson, and it bites harder here than in
    /// the panel: this code exists to <em>report</em> a price, so a second computation of it would stay
    /// invisible until a player was quoted a number the game refuses to pay.
    /// </summary>
    public static ConsignQuote BestConsignment(
        ItemResource item, List<string> tags, PriceView view = PriceView.Today)
    {
        var best = default(ConsignQuote);

        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (!shop.IsConsignment || IsHostile(shop.FactionId) ||
                !TradeTags.Accepts(tags, shop.AcceptedTagList()))
            {
                continue;
            }

            int net = ConsignmentRules.Net(
                ConsignmentRules.Gross(shop.LocalValue(item.Value, tags, view), shop.ConsignFraction),
                shop.ConsignCommission);

            if (!best.Has || net > best.Net)
            {
                best = new ConsignQuote(shop.Id, net, shop.ConsignDays);
            }
        }

        return best;
    }

    /// <summary>
    /// What a master charges to make <paramref name="recipe"/> for the player (38Q): his labour, plus
    /// every ingredient <paramref name="pack"/> is short, at <paramref name="shop"/>'s own counter
    /// price and the player's standing with it.
    ///
    /// <b>It lives here rather than in a third file because the panel and <c>--validate</c> must agree
    /// exactly</b> — this is 38P2's extraction argument in its sharpest form. The window <em>quotes</em>
    /// this number and then charges it; the validator proves no recipe's output can be sold for more
    /// than it. A second computation of either would be invisible until a player found the loop.
    ///
    /// <paramref name="pack"/> is <c>null</c> for "the player supplies nothing", which is the case the
    /// validator checks: buying every material from the master and selling the piece he makes is the
    /// only unbounded loop a commission can create. Supplying materials yourself only ever makes the
    /// bill <em>smaller</em>, and those materials cost something to get — the free forge already does
    /// that trade without the labour fee.
    /// </summary>
    public static int CommissionCost(
        CraftingRecipeResource recipe, ShopResource shop, InventoryComponent? pack, int labourFee) =>
        CommissionCost(recipe, shop, TierOf(shop.FactionId), pack, labourFee);

    /// <inheritdoc cref="CommissionCost(CraftingRecipeResource, ShopResource, InventoryComponent, int)"/>
    /// <remarks>
    /// The explicit-tier overload is <c>--validate</c>'s: it checks the cheapest standing the ramp
    /// allows rather than the live one, because the loop only has to be closed at its cheapest.
    /// ⚠️ <b><paramref name="haggled"/> is the other half of "cheapest", and 38S missed it</b> — a
    /// master whose materials shop can be talked down is 10% cheaper again, and the validator was still
    /// asking at the un-haggled price. The live quote passes <c>false</c>: a commission is a service and
    /// has no access to that shop's ledger, so the number the window quotes is the number it charges.
    /// </remarks>
    public static int CommissionCost(
        CraftingRecipeResource recipe,
        ShopResource shop,
        ReputationTier tier,
        InventoryComponent? pack,
        int labourFee,
        bool haggled = false,
        PriceView view = PriceView.Today)
    {
        var shortfall = new List<(int UnitValue, int Missing, float Markup)>();
        List<string> specialties = shop.SpecialtyList();

        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int missing = ingredient.Quantity - (pack?.CountOf(ingredient.ItemId) ?? 0);

            // An ingredient the item database has lost is skipped rather than force-dereferenced, the
            // same call CraftingComponent.Deconstruct makes: a broken recipe should not crash a quote.
            if (missing <= 0 || ItemDatabase.Get(ingredient.ItemId) is not { } item)
            {
                continue;
            }

            // 38G: the master charges his own counter's price for what he supplies, and his counter
            // stands somewhere. ⚠️ This is half of the commission exploit check — the other half is
            // BestBuyers above, which now scans every settlement's local value for the best sale.
            List<string> itemTags = item.TagList();
            shortfall.Add((shop.LocalValue(item.Value, itemTags, view), missing, ShopPricing.MarkupFor(
                shop.BuyMarkup, tier, TradeTags.IsSpecialty(itemTags, specialties), haggled)));
        }

        return CommissionRules.Cost(labourFee, shortfall);
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
