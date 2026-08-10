using System.Collections.Generic;
using Embervale.Loot;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// A shop's wares and its spread (Phase 38A), authored as a <c>.tres</c> under <c>data/shops/</c> and
/// indexed by <see cref="ShopDatabase"/>. A <see cref="VendorComponent"/> names one by id, so a new
/// merchant is a resource plus a component in a scene, with no code — the same shape
/// <c>PropertyResource</c> + <c>PropertyDeedComponent</c> have.
///
/// <b>This resource stays immutable</b> even now that stock depletes (Phase 38B). It is shared by
/// every vendor naming it and it is not <c>ISaveable</c>, so writing a remaining count into it would
/// both leak between merchants and vanish on reload. Runtime stock lives in
/// <see cref="ShopStockService"/>, keyed by <see cref="Id"/>. Vendor purses are still 38C's gold sink.
/// </summary>
[GlobalClass]
public partial class ShopResource : Resource
{
    /// <summary>Stable id, e.g. <c>shop.ember_crown.goods</c>.</summary>
    [Export] public string Id { get; set; } = "shop.unknown";

    /// <summary>Player-facing name. A <c>Loc</c> key — it reaches the interaction prompt and the
    /// window title, and CLAUDE.md §6 admits no literals in either.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>
    /// Whose standing the merchant prices by (Phase 38C). Empty means standing has no effect at all.
    ///
    /// Authored <b>here rather than read off the vendor entity's <c>FactionComponent</c></b>, even
    /// though every town NPC already carries one: <c>ShopOpenedEvent</c> carries no vendor entity, the
    /// <c>shop</c> dev command has no vendor at all (so the console would silently price without a
    /// discount and disagree with the game), and <c>ContentValidator</c> cannot scan a <c>.tscn</c>, so
    /// an entity-sourced faction would be unvalidatable. <c>CompanionResource.FactionId</c> is the same
    /// call already made elsewhere.
    /// </summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    [ExportGroup("Wares")]

    /// <summary>
    /// The shop's authored rows, sold as plain instances (<c>ItemInstance.Plain</c>). Untyped so
    /// authored <c>.tres</c> sub-resource arrays bind cleanly; read it back through
    /// <see cref="StockList"/>. The validator rejects an empty list, an unknown id, gold, and any
    /// <c>ItemType.Quest</c> item.
    /// </summary>
    [Export] public Godot.Collections.Array Stock { get; set; } = new();

    /// <summary>
    /// Whole in-game days between restocks; <c>0</c> means this shop never restocks, which is only
    /// legal when every row is unlimited. Restock is evaluated when the shop is <em>opened</em>, not
    /// on a tick — see <see cref="ShopStockService"/>.
    /// </summary>
    [Export] public int RestockDays { get; set; }

    /// <summary>
    /// Optional pool rolled through <see cref="LootGenerator"/> at each restock, at a quality scaled
    /// by the player's level (<see cref="ShopStock.QualityForLevel"/>) — the "leveled" half of 38B.
    /// A <c>LootTable</c> rather than a bespoke type because it already carries drop chances,
    /// quantities, <c>RollAffixes</c> and a quality bonus, and its item ids are already cross-checked
    /// by the validator's loot pass.
    /// </summary>
    [Export] public LootTable? LeveledTable { get; set; }

    [ExportGroup("Trade")]

    /// <summary>
    /// The trades this merchant will buy from at all (Phase 38F) — words from <see cref="TradeTags"/>,
    /// matched against <c>ItemResource.TradeTags</c>.
    ///
    /// <b>Empty means she buys anything, and that is how a general store is authored.</b> Not a gap in
    /// the data: a merchant who deals in everything says nothing here, which is also what every shop
    /// authored before 38F says, so the field arrives without changing a single existing merchant.
    ///
    /// ⚠️ <b>A settlement needs one merchant with an empty list</b>, or loot becomes unsellable by
    /// authoring accident. In the Ember Crown that is Aldreth.
    /// </summary>
    [Export] public Godot.Collections.Array<string> AcceptedTags { get; set; } = new();

    /// <summary>
    /// The trades she is expert in — she pays <see cref="ShopPricing.SpecialtySellBonus"/> over the odds
    /// for them and asks <see cref="ShopPricing.SpecialtyBuyDiscount"/> less. This is what makes
    /// <em>where</em> the player sells matter, and it is the first merchant property that is about the
    /// merchant rather than about the player's standing with her.
    ///
    /// ⚠️ Must be a subset of <see cref="AcceptedTags"/> when that list is non-empty — a specialist who
    /// refuses her own specialty is well-formed data that reads in game as the premium being broken, so
    /// <c>--validate</c> rejects it.
    /// </summary>
    [Export] public Godot.Collections.Array<string> Specialties { get; set; } = new();

    [ExportGroup("Spread")]

    /// <summary>
    /// Multiplier on an item's value when the player buys. Must be at least <c>1</c>: a vendor
    /// selling below base value is a vendor the player farms.
    /// </summary>
    [Export] public float BuyMarkup { get; set; } = 1.5f;

    /// <summary>
    /// Fraction of an item's value the vendor pays when the player sells. Must be above <c>0</c> and
    /// <b>below <see cref="BuyMarkup"/></b> — the two together are the spread, and inverting them
    /// prints money. <c>--validate</c> rejects that, and <see cref="ShopPricing"/> clamps so a
    /// hand-edited resource cannot do it either.
    /// </summary>
    [Export] public float SellFraction { get; set; } = 0.4f;

    /// <summary>
    /// Gold the merchant can spend buying from the player before they run dry, refilled at each restock
    /// (Phase 38C). <c>0</c> is unlimited, which is 38A/38B's behaviour and stays the default.
    ///
    /// This is a sink from the other end: it caps how fast a player can convert a field of corpses into
    /// coin, without a single new piece of timing machinery — 38B's restock clock now governs income as
    /// well as stock. ⚠️ A positive purse with <c>RestockDays = 0</c> is a merchant permanently out of
    /// money, the same shape as a finite stock row with no clock, and <c>--validate</c> rejects it for
    /// the same reason.
    /// </summary>
    [Export] public int PurseGold { get; set; }

    [ExportGroup("Haggling")]

    /// <summary>
    /// The chance in a hundred that this merchant is talked down, asked once per day (Phase 38S).
    /// <c>0</c> — the default, and every shop authored before 38S — is a merchant who will not
    /// negotiate at all, and the panel shows no button for them.
    ///
    /// ⚠️ <b>A haggling shop must have a <see cref="FactionId"/></b>, because <see cref="HaggleDelta"/>
    /// is the whole downside and a shop with no faction cannot be thought worse of. <c>--validate</c>
    /// refuses the pairing, exactly as it refuses a gambling house with no faction (38R2) and for the
    /// same reason: an authored risk that cannot land is not a risk.
    /// </summary>
    [Export] public int HaggleChance { get; set; }

    /// <summary>
    /// What a <em>failed</em> attempt costs the player with <see cref="FactionId"/> — negative, and
    /// charged once per day because <see cref="HaggleLedger"/> allows one attempt.
    ///
    /// ⚠️ It is a standing hit rather than a price surcharge deliberately: a surcharge would be
    /// invisible against a spread the player never memorised, while standing is shown on the panel,
    /// carried between every counter of the faction and slow to earn back. That is also why the numbers
    /// are small — a failed conversation must not cost what fencing contraband costs.
    /// </summary>
    [Export] public int HaggleDelta { get; set; }

    [ExportGroup("Hours")]

    /// <summary>
    /// Hour the shop opens, and the hour it shuts (Phase 38J). <b>Equal values mean always open</b>,
    /// which is the <c>0</c>/<c>0</c> default — so every shop authored before 38J keeps trading around
    /// the clock and the fields arrive inert. The window is half-open (open <em>at</em>
    /// <see cref="OpenHour"/>, shut <em>at</em> <see cref="CloseHour"/>) and may wrap past midnight.
    ///
    /// ⚠️ These should agree with the merchant's <c>ScheduleComponent</c> routine — she should shut
    /// around the hour she walks away from her stall. <c>--validate</c> <b>cannot</b> check that: a
    /// <c>ScheduleId</c> lives in a <c>.tscn</c>, which the validator does not scan. Author them
    /// together by hand, the same way <c>VendorComponent.ShopId</c> has to be.
    /// </summary>
    [Export] public int OpenHour { get; set; }

    /// <inheritdoc cref="OpenHour"/>
    [Export] public int CloseHour { get; set; }

    /// <summary>
    /// How often the merchant is in town (Phase 38J). <c>0</c> is a <b>resident</b> merchant — always
    /// here, and the default. <c>n</c> means one day in every <c>n</c>, on the day given by
    /// <see cref="VisitDayOffset"/>.
    ///
    /// Presence is a pure function of <c>WorldClock.Day</c> (<see cref="ShopHours.IsInTown"/>), which is
    /// why a merchant who comes and goes needs <b>no save state at all</b> — there is nothing to persist
    /// about a fact that can be recomputed, and nothing to drift out of step with a reloaded clock.
    ///
    /// ⚠️ A travelling merchant may never be the only seller of a consumable. <c>--validate</c> enforces
    /// that: attrition supplies behind a merchant who may not be in town is the one closure in this
    /// sub-phase that is a hard gate rather than a wait.
    /// </summary>
    [Export] public int VisitEveryDays { get; set; }

    /// <summary>Which day of the <see cref="VisitEveryDays"/> cycle he arrives on, <c>0..n-1</c>. An
    /// offset outside that range is a cycle position that never comes round, so the merchant never
    /// appears at all — the quietest failure in 38J, and a validator rule for exactly that reason.</summary>
    [Export] public int VisitDayOffset { get; set; }

    [ExportGroup("Contraband")]

    /// <summary>
    /// The faction a fenced sale <em>pleases</em>, and by how much (Phase 38O). Empty/<c>0</c> is a
    /// merchant who deals in nothing illicit — which is every shop authored before 38O, so both pairs
    /// arrive without changing a single existing one (38I/38M's "the default is the ungated case").
    ///
    /// ⚠️ <b>Per sale, not per unit.</b> A stack sale is one price multiplied across a quantity (38H),
    /// but standing is not divisible the same way: charged per unit, one click on a stack of twenty
    /// would move the player three tiers. This is deliberately the opposite of 38H's per-unit ruling on
    /// the payout, and the two are only in tension if you read "a stack decays across its own units" as
    /// a rule about stacks rather than about appetite.
    ///
    /// A fence prices flat: she authors no <see cref="FactionId"/>, because the natural owner
    /// (<c>faction.outlaws</c>) starts at <c>-30</c> — tier <c>Hostile</c>, at or below its own
    /// <c>HostileThreshold</c> — so a shop factioned to it would be hidden by
    /// <see cref="VendorComponent"/> and refuse to trade from the first minute of the game. The
    /// standing she moves and the standing she prices by are two different questions, and 38O only
    /// answers the first.
    /// </summary>
    [Export] public string ContrabandFactionId { get; set; } = string.Empty;

    /// <inheritdoc cref="ContrabandFactionId"/>
    [Export] public int ContrabandDelta { get; set; }

    /// <summary>
    /// The faction a fenced sale <em>offends</em>, and by how much — the other half of the two-sided
    /// cost (Phase 38O). Negative, and the same machinery 38M's bribe uses: standing lost with the
    /// villagers is charged again at every honest counter in the realm, forever, because
    /// <see cref="ShopPricing.PriceMultiplierFor"/> reads it.
    ///
    /// ⚠️ A fence must author <b>both</b> sides. One-sided is well-formed data that reads in game as
    /// the penalty being broken, so <c>--validate</c> rejects it.
    /// </summary>
    [Export] public string ContrabandPenaltyFactionId { get; set; } = string.Empty;

    /// <inheritdoc cref="ContrabandPenaltyFactionId"/>
    [Export] public int ContrabandPenaltyDelta { get; set; }

    [ExportGroup("Investment")]

    /// <summary>
    /// The ladder of stakes the player may buy in this merchant (Phase 38I) — an array of
    /// <see cref="ShopInvestmentTier"/> sub-resources, ordered cheapest first. Empty is a merchant who
    /// takes no investment, which is every shop authored before 38I, so the field arrives without
    /// changing a single existing one.
    ///
    /// This is the arc's flagship late-game sink: every other sink in the game is a purchase, so once
    /// the gear stops improving gold only climbs. A stake is permanent, it never pays back in coin, and
    /// what it buys is the merchant's capacity to absorb loot and access to the rows gated behind it.
    ///
    /// Untyped for the same reason <see cref="Stock"/> is — authored <c>.tres</c> sub-resource arrays
    /// bind cleanly that way; read it back through <see cref="InvestmentTierList"/>.
    /// </summary>
    [Export] public Godot.Collections.Array InvestmentTiers { get; set; } = new();

    [ExportGroup("Consignment")]

    /// <summary>
    /// Fraction of an item's value a broker puts it on the shelf for (Phase 38P), before her
    /// commission. <b><c>0</c> means this is not a consignment house</b>, which is every shop
    /// authored before 38P — the flag and the number are deliberately one field, because a broker
    /// with a zero fraction is not a thing that can exist.
    ///
    /// It is authored <em>above</em> every <see cref="SellFraction"/> in the realm, which is the whole
    /// offer: the broker pays better than any counter and takes days about it. <c>--validate</c>
    /// enforces that ordering, because a broker who pays less than a shop is dead content nobody would
    /// ever use.
    ///
    /// ⚠️ It cannot be authored into a money printer. <see cref="ConsignmentRules.Gross"/> routes
    /// through <see cref="ShopPricing.SellPrice"/>, whose <c>0..1</c> clamp holds a payout to the
    /// item's value — so <c>sell &lt;= value &lt;= buy</c> survives 38P untouched.
    /// </summary>
    [Export] public float ConsignFraction { get; set; }

    /// <summary>
    /// In-game days a listing takes to sell. Must be at least <c>1</c> on a consignment house:
    /// <see cref="ConsignmentRules.HasSold"/> inherits <see cref="ShopStock.IsRestockDue"/>'s
    /// treatment of a non-positive period as "never", so a <c>0</c> here is a shelf the player can
    /// never collect from — the same shape as a finite stock row with no restock clock, and
    /// <c>--validate</c> rejects it for the same reason.
    /// </summary>
    [Export] public int ConsignDays { get; set; }

    /// <summary>
    /// The house's cut, <c>0..1</c>. This is the sink half of consignment: the player is paid more
    /// than any shop would give and still hands a slice back, so the mechanism moves gold out of the
    /// economy rather than only around it. A cut of <c>1</c> is a free item and <c>--validate</c>
    /// rejects it.
    /// </summary>
    [Export] public float ConsignCommission { get; set; }

    /// <summary>
    /// Which cell this counter stands in (Phase 38G) — <c>&lt;region&gt;.&lt;cell&gt;</c>, matching
    /// <c>RegionCellResource.Id</c>. It exists for one purpose: the cell's <c>Surplus</c> and
    /// <c>Demand</c> tags say what a good is worth <em>here</em>.
    ///
    /// ⚠️ <b>Empty means the realm reference, not "unknown"</b> — the town square and the Embermarket
    /// price at par deliberately, so only a shop in a cell that authors demand needs this filled in.
    /// The cost of that default is real and is the sub-phase's trap: <b>a new shop at the mine that
    /// forgets this prices as though it were in town, and nothing says so.</b> It shows in
    /// <c>--economy</c> as a route that does not appear, which is a thing you have to already suspect.
    /// A non-empty id that resolves to no cell is refused by <c>--validate</c>.
    /// </summary>
    [Export] public string CellId { get; set; } = string.Empty;

    /// <summary>Whether this shop is a broker rather than a counter — read by the vendor window to
    /// decide whether the pack's rows sell or list. One function so the panel, the sale and the
    /// validator cannot disagree about what <see cref="ConsignFraction"/> means.</summary>
    public bool IsConsignment => ConsignFraction > 0f;

    /// <summary>
    /// The authored rows as a typed list. Deliberately <b>does not</b> filter malformed rows the way
    /// <c>CraftingRecipeResource.IngredientList</c> does: an empty id or a negative quantity is
    /// something <c>--validate</c> has to be able to see and report, and a silent skip is how
    /// <c>ValidateLootTables</c> can pass a table with a blank entry in it.
    /// </summary>
    /// <summary>The accepted trades as a plain list, for the Godot-free <see cref="TradeTags"/>
    /// helpers.</summary>
    public List<string> AcceptedTagList() => Plain(AcceptedTags);

    /// <summary>The specialist trades as a plain list.</summary>
    public List<string> SpecialtyList() => Plain(Specialties);

    /// <summary>
    /// What this counter reckons a good is worth (Phase 38G): the item's value moved by the surplus and
    /// demand tags of the cell the shop stands in.
    ///
    /// ⚠️ <b>Every price in the game goes through here or it is wrong</b>, and that is four call sites
    /// in two files plus the two shared helpers in <see cref="EconomyReport"/>. The failure mode is not
    /// a crash: a site left on the raw <c>Value</c> quotes one number and charges another, or lets
    /// <c>--validate</c> ask the commission question at a price no player pays.
    ///
    /// Falls through to the plain value when the shop authors no <see cref="CellId"/>, when the id has
    /// gone stale, or when the cell authors nothing — the same tolerant default
    /// <c>VendorPanel.StandingWith</c> takes, and for the same reason: a half-built world must price
    /// normally rather than refuse to trade.
    /// </summary>
    /// <param name="view">
    /// Which day to price for (Phase 38T). <see cref="PriceView.Today"/> applies whatever supply shock
    /// is running at this cell right now and is what every live transaction passes; the other two ask
    /// for the ends of the band a shock can move this counter through, and exist for
    /// <c>ContentValidator</c> — a rule proved only against today's prices is a rule that breaks on a
    /// day nobody was playing on.
    /// </param>
    public int LocalValue(int baseValue, List<string> itemTags, PriceView view = PriceView.Today)
    {
        if (CellId.Length == 0 || World.RegionDatabase.Cell(CellId) is not { } cell)
        {
            return baseValue;
        }

        (List<string> surplus, List<string> demand) = view == PriceView.Today
            ? LiveTags(cell)
            : SupplyShockRules.Extremes(
                Plain(cell.Surplus), Plain(cell.Demand), Plain(cell.ShockTags), view);

        return RegionDemand.ValueAt(baseValue, itemTags, surplus, demand);
    }

    /// <summary>
    /// <see cref="LocalValue"/>'s answer <em>with its reason</em> (Phase 38U): the local value, the tag
    /// the cell had an opinion about, and whether that opinion is a supply shock rather than the place's
    /// standing character.
    ///
    /// ⚠️ <b>It resolves the live tags once and answers both questions off that one pair.</b> A caller
    /// that asked <see cref="LocalValue"/> for the number and re-derived the tag itself would be running
    /// the match twice — and on a day a shock begins between the two calls, running it against two
    /// different worlds. Always <see cref="PriceView.Today"/>, because a breakdown of a price the player
    /// is being charged has no business quoting a band.
    /// </summary>
    public (int Value, string Tag, bool Shocked) LocalQuote(int baseValue, List<string> itemTags)
    {
        if (CellId.Length == 0 || World.RegionDatabase.Cell(CellId) is not { } cell)
        {
            return (baseValue, string.Empty, false);
        }

        (List<string> surplus, List<string> demand) = LiveTags(cell);
        int value = RegionDemand.ValueAt(baseValue, itemTags, surplus, demand);
        if (value == baseValue)
        {
            return (value, string.Empty, false);
        }

        // Surplus is answered first here for the reason RegionDemand.ValueAt answers it first: a tag in
        // both lists is treated as a surplus, and the explanation must name the list that actually won.
        string tag = RegionDemand.MatchedTag(itemTags, surplus);
        bool fromSurplus = tag.Length > 0;
        if (!fromSurplus)
        {
            tag = RegionDemand.MatchedTag(itemTags, demand);
        }

        // A shock is exactly "a tag in today's list that the .tres did not author into it" — there is no
        // flag to read, because 38T deliberately added no multiplier and no marker, only a list edit.
        bool shocked = tag.Length > 0 && !Plain(fromSurplus ? cell.Surplus : cell.Demand).Contains(tag);

        return (value, tag, shocked);
    }

    /// <summary>
    /// The cell's tags as they stand today. Falls back to the authored pair when no
    /// <see cref="SupplyShockService"/> is in the tree — which is every headless run (<c>--validate</c>,
    /// <c>--economy</c>) and is the right answer there: those tools ask their own questions about the
    /// shocked band explicitly rather than inheriting one session's dice.
    /// </summary>
    private static (List<string> Surplus, List<string> Demand) LiveTags(World.RegionCellResource cell)
    {
        if (Core.Services.ServiceLocator.Instance is { } locator &&
            locator.TryGet(out SupplyShockService shocks) &&
            locator.TryGet(out World.WorldClock clock))
        {
            return shocks.TagsFor(cell, clock.Day);
        }

        return (Plain(cell.Surplus), Plain(cell.Demand));
    }

    /// <summary>A Godot string array as a plain list, for the Godot-free helpers. Public because
    /// <see cref="SupplyShockService"/> converts the same cell arrays this does.</summary>
    public static List<string> Plain(Godot.Collections.Array<string> tags)
    {
        var list = new List<string>();
        foreach (string tag in tags)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                list.Add(tag);
            }
        }

        return list;
    }

    public List<ShopStockEntry> StockList()
    {
        var list = new List<ShopStockEntry>();
        foreach (Variant element in Stock)
        {
            if (element.As<ShopStockEntry>() is { } entry)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    /// <summary>The authored investment ladder as a typed list, cheapest rung first (Phase 38I).</summary>
    public List<ShopInvestmentTier> InvestmentTierList()
    {
        var list = new List<ShopInvestmentTier>();
        foreach (Variant element in InvestmentTiers)
        {
            if (element.As<ShopInvestmentTier>() is { } tier)
            {
                list.Add(tier);
            }
        }

        return list;
    }
}
