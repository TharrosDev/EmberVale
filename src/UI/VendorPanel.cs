using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The shop window (Phase 38A), on the 30.5F <see cref="UiPanel"/> framework and structurally
/// <see cref="StoragePanel"/> with prices: a <see cref="VendorComponent"/> (or the <c>shop</c> dev
/// command) publishes a <see cref="ShopOpenedEvent"/>, this resolves the player's
/// <see cref="InventoryComponent"/>, and the two sides sit side by side — the vendor's wares on the
/// left with Buy, the player's pack on the right with Sell.
///
/// Every number on screen comes from <see cref="ShopPricing"/> over <see cref="ItemInstance.Value"/>,
/// which already folds in rarity and affix count — so the spread applies to rolled loot with no
/// second price table to drift.
/// </summary>
public partial class VendorPanel : UiPanel
{
    private Label _title = null!;
    private Label _standing = null!;
    private Label _localTrade = null!;
    private HBoxContainer _investRow = null!;
    private Label _investLabel = null!;
    private Button _investButton = null!;
    private HBoxContainer _haggleRow = null!;
    private Label _haggleLabel = null!;
    private Button _haggleButton = null!;
    private Label _waresHeader = null!;
    private Label _packHeader = null!;
    private VBoxContainer _waresList = null!;
    private VBoxContainer _packList = null!;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private ShopResource? _shop;
    private bool _justOpened;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.OffsetLeft = -320;
        shell.OffsetRight = 320;
        shell.OffsetTop = 60;
        shell.GrowHorizontal = Control.GrowDirection.Both;

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);

        // A price that moved must say why it moved. Without this line the discount is invisible and
        // reads as the shop being mispriced — the same reason every Phase 37 refusal names itself.
        _standing = UiTheme.Caption(string.Empty);
        column.AddChild(_standing);

        // What the place itself does to the prices (38G), directly under what the merchant thinks of
        // you — two different reasons a number moved, in the order the player meets them.
        _localTrade = UiTheme.Caption(string.Empty);
        _localTrade.AddThemeColorOverride("font_color", UiTheme.Dim);
        column.AddChild(_localTrade);

        // The stake line (38I). It sits with the standing caption rather than in the wares column
        // because it is a fact about the merchant, not a ware: what it buys is her purse and the rows
        // she keeps back, and both are visible from here.
        _investRow = new HBoxContainer { Visible = false };
        _investRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _investLabel = UiTheme.Caption(string.Empty);
        _investLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _investLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _investRow.AddChild(_investLabel);

        _investButton = UiTheme.Action(Loc.T("shop.invest"));
        _investButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _investButton.Pressed += OnInvestPressed;
        _investRow.AddChild(_investButton);
        column.AddChild(_investRow);

        // The haggle line (38S), directly under the stake and for the same reason: it is a fact about
        // the merchant rather than a ware. Hidden entirely on a merchant who will not negotiate, which
        // is every shop authored before this sub-phase — a greyed-out button on twenty counters would
        // teach the player the feature is broken.
        _haggleRow = new HBoxContainer { Visible = false };
        _haggleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _haggleLabel = UiTheme.Caption(string.Empty);
        _haggleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _haggleLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _haggleRow.AddChild(_haggleLabel);

        _haggleButton = UiTheme.Action(Loc.T("shop.haggle"));
        _haggleButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _haggleButton.Pressed += OnHagglePressed;
        _haggleRow.AddChild(_haggleButton);
        column.AddChild(_haggleRow);

        column.AddChild(UiTheme.Divider());

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 500),
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        column.AddChild(columns);

        (_waresHeader, _waresList) = BuildColumn(columns);
        columns.AddChild(new VSeparator());
        (_packHeader, _packList) = BuildColumn(columns);
    }

    /// <summary>One titled scroll column; both sides are the same shape.</summary>
    private static (Label Header, VBoxContainer List) BuildColumn(Node parent)
    {
        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        parent.AddChild(side);

        Label header = UiTheme.Header(string.Empty);
        side.AddChild(header);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        side.AddChild(scroll);
        return (header, list);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<ShopOpenedEvent>(OnShopOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<ShopOpenedEvent>(OnShopOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    private void OnShopOpened(ShopOpenedEvent e)
    {
        // Ignore a second merchant while one is open.
        if (IsOpen || e.Player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        _player = e.Player;
        _pack = pack;
        _shop = e.Shop;

        SetOpen(true);

        // The same interact press that opened the shop is still "just pressed" this frame; swallow it
        // so the close-on-interact below does not fire immediately.
        _justOpened = true;
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => MarkDirty();

    public override void _Process(double delta)
    {
        if (IsOpen)
        {
            if (_justOpened)
            {
                _justOpened = false;
            }
            else if (Godot.Input.IsActionJustPressed(GameInput.Interact))
            {
                // A modal needs an easy out; the interact key both opens and closes it.
                Close();
                return;
            }
        }

        base._Process(delta);
    }

    private void Close()
    {
        IEntity? player = _player;
        SetOpen(false);
        _player = null;
        _pack = null;
        _shop = null;

        if (player != null)
        {
            EventBus.Instance?.Publish(new ShopClosedEvent(player));
        }
    }

    private int Purse() => _pack?.CountOf(GameIds.Currency.Gold) ?? 0;

    /// <summary>
    /// Buys one unit. <b>Charged first, then delivered, with a refund if delivery fails</b> — and that
    /// order is deliberate. Adding first and rolling back on a failed charge cannot work:
    /// <see cref="InventoryComponent.AddInstance"/> merges a stackable into an existing stack, so the
    /// instance handed in is often never stored and <c>RemoveOneInstance</c> would find nothing to
    /// take back. Refunding gold always works, because spending it either freed a slot or left a
    /// stack with room in it.
    ///
    /// Both refusals are separate conditions for the reason <c>PropertyDeedComponent.Interact</c>
    /// spells out: chained into one test, an unresolvable pack falls <em>through</em> to a free item.
    ///
    /// 38B adds the shelf decrement as a fourth step, deliberately last: nothing may consume stock on a
    /// path that ends without the player holding the goods.
    /// </summary>
    private void Buy(ShopResource shop, ShopOffer offer, int price)
    {
        if (_pack is not { } pack || ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return;
        }

        if (!offer.Available ||
            LockFor(shop, offer, StandingWith(shop)) != StockLock.Open ||
            !ShopPricing.CanAfford(price, Purse()))
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        if (!pack.RemoveItem(GameIds.Currency.Gold, price))
        {
            return; // the gold went somewhere between the rebuild and the press; deliver nothing
        }

        if (pack.AddInstance(offer.Instance, 1) <= 0)
        {
            pack.AddItem(gold, price); // pack full — hand the money straight back
            return;
        }

        // Paid for and delivered, so the sale stands either way; a false here would mean the shelf and
        // the window had drifted within one frame, which is worth a line in the log.
        if (Stock() is { } stock && !stock.TakeOne(shop, offer.Instance))
        {
            Log.Warn($"Shop '{shop.Id}': sold '{offer.Instance.TemplateId}' that the shelf no longer had.");
        }

        MarkDirty();
    }

    /// <summary>
    /// Which gate, if any, is holding a row shut for this player (38I). Evaluated here rather than in
    /// <see cref="ShopStockService"/> because it depends on the player's standing and story flags, and
    /// that service is deliberately player-agnostic. A leveled ware carries no authored row and is
    /// therefore never gated — the pool rolled it, so there is nobody's decision to honour.
    /// </summary>
    private StockLock LockFor(ShopResource shop, ShopOffer offer, ReputationTier tier)
    {
        if (offer.Row is not { } row || !row.IsGated)
        {
            return StockLock.Open;
        }

        bool hasFlag = _player?.GetComponent<StoryFlagsComponent>()?.Has(row.RequiredFlagId) ?? false;
        int invested = Stock()?.InvestmentOf(shop) ?? 0;

        return ShopStock.LockOf(
            row.RequiredTier, row.RequiredFlagId, row.RequiredInvestment, tier, hasFlag, invested);
    }

    /// <summary>A locked row says what would open it, never just that it is shut — the same rule 38F's
    /// trade refusal follows, and for the same reason: a refusal that names its condition is a piece of
    /// teaching nobody had to write.</summary>
    private static string LockRefusal(StockLock locked, ShopStockEntry row) => locked switch
    {
        StockLock.Flag => Loc.T("shop.locked_flag"),
        StockLock.Standing => Loc.TF("shop.locked_standing", ReputationTiers.DisplayName(row.RequiredTier)),
        _ => Loc.TF("shop.locked_investment", row.RequiredInvestment),
    };

    /// <summary>
    /// Buys the next rung of a stake (38I). <b>Charged first, then recorded</b> — the same order
    /// <see cref="Buy"/> uses and for the same reason: gold can always be handed back, so a failure
    /// after the charge is recoverable, while a stake recorded before the charge is one the player
    /// never paid for.
    /// </summary>
    private void OnInvestPressed()
    {
        if (_shop is not { } shop || _pack is not { } pack || Stock() is not { } stock ||
            ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return;
        }

        List<ShopInvestmentTier> tiers = shop.InvestmentTierList();
        int held = stock.InvestmentOf(shop);
        if (held >= tiers.Count)
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        int cost = tiers[held].Cost;
        if (!ShopPricing.CanAfford(cost, Purse()) || !pack.RemoveItem(GameIds.Currency.Gold, cost))
        {
            return;
        }

        if (!stock.Invest(shop))
        {
            // The ladder moved between the rebuild and the press. Hand the money straight back.
            pack.AddItem(gold, cost);
            Log.Warn($"Shop '{shop.Id}': stake refused after charging; refunded {cost}g.");
        }

        MarkDirty();
    }

    private static ShopStockService? Stock() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out ShopStockService service)
            ? service
            : null;

    /// <summary>
    /// Sells a whole stack — a pack row <em>is</em> a stack, the same granularity
    /// <see cref="StoragePanel"/>'s Store/Take use.
    ///
    /// ⚠️ The removal branch is load-bearing and copied from <see cref="StoragePanel"/>:
    /// <see cref="InventoryComponent.RemoveItem(string, int)"/> matches by template id across
    /// <em>every</em> stack, so selling one of two differently-affixed copies of one template would
    /// see the first removal satisfy both and evaporate the other. Rolled items go by reference.
    /// </summary>
    private void Sell(ShopResource shop, ItemStack stack, int payout)
    {
        if (_pack is not { } pack || ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return;
        }

        ItemInstance instance = stack.Instance;
        if (!ShopPricing.Sellable(instance.Type, IsCurrency(instance)) ||
            !InTrade(shop, instance) ||
            payout <= 0)
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        // The purse is spent *before* the goods change hands (38C). A merchant who cannot cover the
        // payout refuses the whole sale rather than paying part of it — the player has handed over the
        // item either way, so a short payment is item loss with a receipt.
        ShopStockService? stock = Stock();
        if (stock != null && !stock.TakePurse(shop, payout))
        {
            return;
        }

        bool removed = instance.IsStackable
            ? pack.RemoveItem(instance.TemplateId, stack.Quantity)
            : pack.RemoveOneInstance(instance) != null;

        if (!removed)
        {
            // The purse was already debited, so hand it back: the merchant did not get the goods.
            stock?.RefundPurse(shop, payout);
            Log.Warn($"Shop: could not remove '{instance.TemplateId}' to sell it; paid nothing.");
            return;
        }

        pack.AddItem(gold, payout);

        // Recorded last, and only here (38H): the merchant now has the goods, so their appetite for the
        // next one has genuinely fallen. Every early return above leaves the player holding the item, and
        // none of them may mark a merchant as glutted for a sale that did not happen.
        stock?.Absorb(shop, instance.TemplateId, stack.Quantity);
        FenceStanding(shop, instance);
        MarkDirty();
    }

    /// <summary>
    /// Puts a whole stack on a broker's shelf (Phase 38P). Same granularity as <see cref="Sell"/>, and
    /// the removal branch below is copied from it verbatim for the reason that method records:
    /// <c>RemoveItem</c> matches by template id across every stack, so one of two differently-affixed
    /// copies would take the other with it.
    ///
    /// ⚠️ <b>It calls neither <c>TakePurse</c>, nor <c>Absorb</c>, nor <c>FenceStanding</c>, and all
    /// three absences are the feature rather than oversights.</b> A broker fronts no money, so there is
    /// no purse to spend before the goods change hands and no short payment to guard against; she never
    /// owns the item, so her appetite for the next one has not fallen; and she is not a fence, because
    /// <c>TradeTags.Accepts</c> refuses contraband at any counter that does not name it (38O) and this
    /// row was already gated on that one function.
    ///
    /// What replaces the purse check is the ledger entry: nothing is paid here at all. The gold exists
    /// only as a promise until the clerk's counter is pressed some days later, which is what a
    /// consignment <em>is</em> — and it is why the whole-stack payout above needs no cap.
    /// </summary>
    private void Consign(ShopResource shop, ItemStack stack, int netPerUnit)
    {
        if (_pack is not { } pack || Ledger() is not { } ledger)
        {
            return;
        }

        ItemInstance instance = stack.Instance;
        if (!ShopPricing.Sellable(instance.Type, IsCurrency(instance)) ||
            !InTrade(shop, instance) ||
            netPerUnit <= 0)
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        bool removed = instance.IsStackable
            ? pack.RemoveItem(instance.TemplateId, stack.Quantity)
            : pack.RemoveOneInstance(instance) != null;

        if (!removed)
        {
            Log.Warn($"Shop: could not remove '{instance.TemplateId}' to consign it; listed nothing.");
            return;
        }

        // Recorded only once the goods are actually gone, the ordering 38H and 38O both settled: every
        // early return above leaves the player still holding the item, and none of them may put an
        // entry on a shelf that never received it.
        ledger.Add(
            shop.Id, instance.TemplateId, stack.Quantity, netPerUnit, CurrentDay(), shop.ConsignDays);
        MarkDirty();
    }

    private static ConsignmentLedger? Ledger() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out ConsignmentLedger ledger)
            ? ledger
            : null;

    private static HaggleLedger? Haggles() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out HaggleLedger ledger)
            ? ledger
            : null;

    private static int CurrentDay() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock) ? clock.Day : 0;

    /// <summary>
    /// The two-sided cost of fencing (38O): standing gained with whoever the fence answers to, and lost
    /// with whoever she is hiding from.
    ///
    /// ⚠️ Called from exactly here, beside <c>Absorb</c>, and for exactly the same reason: every early
    /// return above leaves the player still holding the item, and none of them may move a faction for a
    /// sale that did not happen. A reputation change is louder than a glutted shelf — it toasts, and it
    /// reprices every merchant in the region.
    ///
    /// Once per sale, whatever the stack size. See <see cref="ShopResource.ContrabandFactionId"/>.
    /// </summary>
    private void FenceStanding(ShopResource shop, ItemInstance instance)
    {
        if (!TradeTags.IsContraband(instance.Template.TagList()) ||
            _player?.GetComponent<ReputationComponent>() is not { } reputation)
        {
            return;
        }

        reputation.Add(shop.ContrabandFactionId, shop.ContrabandDelta);
        reputation.Add(shop.ContrabandPenaltyFactionId, shop.ContrabandPenaltyDelta);
    }

    private static bool IsCurrency(ItemInstance instance) =>
        instance.TemplateId == GameIds.Currency.Gold;

    /// <summary>Whether this merchant deals in the item at all (38F). One function for the button's
    /// enabled state, the refusal text and the press itself, so they cannot drift.</summary>
    private static bool InTrade(ShopResource shop, ItemInstance instance) =>
        TradeTags.Accepts(instance.Template.TagList(), shop.AcceptedTagList());

    /// <summary>Whether the item is the merchant's own trade — the premium and the keener price.</summary>
    private static bool IsSpecialty(ShopResource shop, ItemInstance instance) =>
        TradeTags.IsSpecialty(instance.Template.TagList(), shop.SpecialtyList());

    /// <summary>
    /// A refusal that says where to take it instead. Naming her trade is the whole teaching moment for
    /// the specialty system: "she won't buy this" leaves the player carrying a pelt around a town, while
    /// "Bryn deals in metal, weapons and armour" tells them what kind of person to look for.
    ///
    /// Falls back to the bare line for a merchant with an accepted list and no specialties — there is
    /// nothing truthful to name in that case.
    ///
    /// ⚠️ <b>Contraband is answered first and separately (38O).</b> Naming a trade is the right answer to
    /// "she does not deal in this"; it is the wrong answer to "nobody with a shopfront deals in this",
    /// because it sends the player looking for a specialist who cannot exist. The contraband line points
    /// somewhere quieter instead, which is the only hint the wharf ever gets.
    /// </summary>
    private static string TradeRefusal(ShopResource shop, ItemInstance instance)
    {
        if (TradeTags.IsContraband(instance.Template.TagList()))
        {
            return Loc.TF("shop.refuses_contraband", Loc.T(shop.NameKey));
        }

        List<string> specialties = shop.SpecialtyList();
        if (specialties.Count == 0)
        {
            return Loc.T("shop.not_my_trade");
        }

        var names = new List<string>();
        foreach (string tag in specialties)
        {
            names.Add(Loc.T($"trade.tag.{tag}"));
        }

        return Loc.TF("shop.refuses_tag", Loc.T(shop.NameKey), string.Join(", ", names));
    }

    /// <summary>
    /// The player's standing with the shop's faction (38C). Falls back to <c>Neutral</c> — the
    /// no-effect tier — whenever the shop authors no faction or the player has no
    /// <see cref="ReputationComponent"/>. ⚠️ That default is the <em>opposite</em> of
    /// <c>EnemyAIComponent.PlayerIsTarget</c>, which treats a missing component as hostile; here a
    /// half-built world must price normally rather than have every merchant turn the player away.
    /// </summary>
    private ReputationTier StandingWith(ShopResource shop)
    {
        if (string.IsNullOrEmpty(shop.FactionId) ||
            _player?.GetComponent<ReputationComponent>() is not { } reputation)
        {
            return ReputationTier.Neutral;
        }

        return reputation.TierOf(shop.FactionId);
    }

    protected override void Rebuild()
    {
        if (_shop is not { } shop)
        {
            return;
        }

        _title.Text = $"{Loc.TF("shop.title", Loc.T(shop.NameKey))}   {Loc.TF("shop.purse", Purse())}";

        ReputationTier tier = StandingWith(shop);

        // Asked once and threaded into both sides, so the wares and the pack cannot disagree about
        // whether a deal was struck — the same reason `tier` is resolved here rather than per row.
        bool haggled = DealStruck(shop);

        BuildStanding(shop, tier);
        BuildLocalTrade(shop);
        BuildInvest(shop);
        BuildHaggle(shop, haggled);
        BuildWares(shop, tier, haggled);
        BuildPack(shop, haggled);
    }

    /// <summary>
    /// Whether today's negotiation with this merchant was won. ⚠️ <b>Two questions, both required:</b>
    /// the ledger says whether the player has asked (bounded, saved), <see cref="HaggleRules.Succeeds"/>
    /// says what the answer was (derived, never saved). An unasked merchant prices normally even on a day
    /// they would have said yes — the discount is something the player does, not something the day gives.
    /// </summary>
    private static bool DealStruck(ShopResource shop)
    {
        if (shop.HaggleChance <= 0 || Haggles() is not { } ledger)
        {
            return false;
        }

        int day = CurrentDay();

        return ledger.TriedToday(shop.Id, day) &&
            HaggleRules.Succeeds(day, shop.Id, shop.HaggleChance);
    }

    /// <summary>Names the standing and what it is doing to the prices, coloured with the same
    /// <c>ReputationTiers.Color</c> ramp the character screen uses so the two cannot disagree.</summary>
    private void BuildStanding(ShopResource shop, ReputationTier tier)
    {
        if (string.IsNullOrEmpty(shop.FactionId))
        {
            _standing.Text = string.Empty;
            return;
        }

        // The percentage is derived from the same multiplier the prices use, not written out again.
        int percent = Mathf.RoundToInt((ShopPricing.PriceMultiplierFor(tier) - 1f) * 100f);
        _standing.Text = Loc.TF(
            "shop.standing", ReputationTiers.DisplayName(tier), percent.ToString("+0;-0;0"));
        _standing.AddThemeColorOverride("font_color", UiTheme.ReputationColor(tier));
    }

    /// <summary>
    /// What this place is awash in and what it is short of (38G). ⚠️ Without it the mine's prices are
    /// simply *different* from the market's with no stated reason, which is the "price that moved must
    /// say why it moved" rule the standing caption above exists for — and here it is doing more work,
    /// because it is also the only in-world hint that carrying goods between settlements can pay.
    ///
    /// Silent at a shop in a cell that authors nothing, which is the town square and the Embermarket:
    /// they are the reference, and a line saying "prices are normal here" is noise.
    /// </summary>
    private void BuildLocalTrade(ShopResource shop)
    {
        if (shop.CellId.Length == 0 || World.RegionDatabase.Cell(shop.CellId) is not { } cell)
        {
            _localTrade.Visible = false;
            return;
        }

        string surplus = TagNames(cell.Surplus);
        string demand = TagNames(cell.Demand);
        _localTrade.Visible = surplus.Length > 0 || demand.Length > 0;

        _localTrade.Text = surplus.Length > 0 && demand.Length > 0
            ? Loc.TF("shop.local_trade", surplus, demand)
            : surplus.Length > 0
                ? Loc.TF("shop.local_surplus", surplus)
                : Loc.TF("shop.local_demand", demand);
    }

    /// <summary>The cell's tags in the player's language — the same `trade.tag.<c>x</c>` keys the
    /// refusal line names a merchant's trade with, so the two cannot describe one tag differently.</summary>
    private static string TagNames(Godot.Collections.Array<string> tags)
    {
        var names = new List<string>();
        foreach (string tag in tags)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                names.Add(Loc.T($"trade.tag.{tag}"));
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>
    /// The stake on offer (38I): what the next rung costs, what it does, and how much of the ladder the
    /// player already owns. Hidden entirely on a merchant who takes no investment, which is every shop
    /// authored before this sub-phase.
    /// </summary>
    private void BuildInvest(ShopResource shop)
    {
        List<ShopInvestmentTier> tiers = shop.InvestmentTierList();
        _investRow.Visible = tiers.Count > 0;
        if (tiers.Count == 0)
        {
            return;
        }

        int held = Stock()?.InvestmentOf(shop) ?? 0;
        if (held >= tiers.Count)
        {
            _investLabel.Text = Loc.T("shop.invest_full");
            _investButton.Disabled = true;
            _investButton.TooltipText = Loc.T("shop.invest_full");
            return;
        }

        ShopInvestmentTier next = tiers[held];

        // A rung that only unlocks stock says so rather than quoting a purse bonus of zero — the two
        // are different offers and a player deciding whether to spend needs to know which one this is.
        string offer = next.PurseBonus > 0
            ? Loc.TF("shop.invest_offer", next.Cost, next.PurseBonus)
            : Loc.TF("shop.invest_access", next.Cost);

        _investLabel.Text = $"{offer}   {Loc.TF("shop.invest_held", held, tiers.Count)}";

        bool affordable = ShopPricing.CanAfford(next.Cost, Purse());
        _investButton.Disabled = !affordable;
        _investButton.TooltipText = affordable ? string.Empty : Loc.T("shop.cannot_afford");
    }

    /// <summary>
    /// The negotiation line (38S): the offer before it is taken, the outcome after. Both states name
    /// what happened — a price that moved must say why it moved, which is the standing caption's rule
    /// above and the reason a failed haggle reads as a refusal rather than as nothing at all.
    /// </summary>
    private void BuildHaggle(ShopResource shop, bool haggled)
    {
        _haggleRow.Visible = shop.HaggleChance > 0;
        if (shop.HaggleChance <= 0)
        {
            return;
        }

        bool tried = Haggles()?.TriedToday(shop.Id, CurrentDay()) ?? false;

        _haggleLabel.Text = tried
            ? haggled ? Loc.T("shop.haggle_won") : Loc.T("shop.haggle_lost")
            : Loc.TF("shop.haggle_offer", shop.HaggleChance);
        _haggleLabel.AddThemeColorOverride(
            "font_color", tried ? haggled ? UiTheme.Good : UiTheme.Bad : UiTheme.Dim);

        _haggleButton.Disabled = tried;
        _haggleButton.TooltipText = tried ? Loc.T("shop.haggle_spent") : string.Empty;
    }

    /// <summary>
    /// One attempt, and everything that can go wrong with it happens before the standing is charged.
    /// ⚠️ <see cref="HaggleLedger.TryTake"/> is what says the attempt is allowed, not the button's
    /// disabled state — a press that arrives between two rebuilds must not buy a second conversation,
    /// and the guard belongs with the record rather than here.
    /// </summary>
    private void OnHagglePressed()
    {
        if (_shop is not { } shop || shop.HaggleChance <= 0 || Haggles() is not { } ledger)
        {
            return;
        }

        int day = CurrentDay();
        if (!ledger.TryTake(shop.Id, day))
        {
            return; // already tried today; the button is disabled and says so
        }

        if (!HaggleRules.Succeeds(day, shop.Id, shop.HaggleChance) &&
            _player?.GetComponent<ReputationComponent>() is { } reputation)
        {
            // The downside, charged once per day because the ledger allows one attempt. Same shape as
            // FenceStanding: nothing above this line may move a faction for a conversation that did not
            // happen, and the toast ReputationComponent.Add publishes is the whole announcement.
            reputation.Add(shop.FactionId, shop.HaggleDelta);
        }

        // Reprices every row through the same two functions the transaction charges.
        MarkDirty();
    }

    private void BuildWares(ShopResource shop, ReputationTier tier, bool haggled)
    {
        UiTheme.ClearChildren(_waresList);

        // Naming the cadence is what lets a player tell "gone" from "gone forever" — a sold-out row
        // with no restock is a different thing from one that will be back tomorrow.
        _waresHeader.Text = shop.RestockDays > 0
            ? $"{Loc.T("shop.wares")}   {Loc.TF("shop.restocks", shop.RestockDays)}"
            : Loc.T("shop.wares");

        IReadOnlyList<ShopOffer> offers = Stock()?.OfferFor(shop) ?? System.Array.Empty<ShopOffer>();
        if (offers.Count == 0)
        {
            _waresList.AddChild(UiTheme.Body(Loc.T("shop.empty"), UiTheme.Dim));
            return;
        }

        int purse = Purse();
        foreach (ShopOffer offer in offers)
        {
            bool specialty = IsSpecialty(shop, offer.Instance);

            // 38G: what the good is worth HERE, not in the realm at large. Both sides of this counter
            // spread over the same local value, so the 38A invariant is untouched at the shop.
            int local = shop.LocalValue(offer.Instance.Value, offer.Instance.Template.TagList());
            int price = ShopPricing.BuyPrice(
                local, ShopPricing.MarkupFor(shop.BuyMarkup, tier, specialty, haggled));
            bool affordable = ShopPricing.CanAfford(price, purse);

            // 38I: a gated row is shown, greyed, with the gate named — the same choice a sold-out row
            // makes below, and for a stronger reason. A hidden row teaches nothing; a locked one is
            // how the player learns that standing and a stake buy something.
            StockLock locked = LockFor(shop, offer, tier);

            // A sold-out row stays on the shelf, greyed. Removing it would read as the shop never
            // having stocked the thing, which is the opposite of what happened.
            ShopOffer captured = offer;
            AddRow(
                _waresList,
                offer.Instance,
                quantity: offer.Unlimited ? 1 : offer.Remaining,
                priceText: Loc.TF("shop.price", price),
                action: Loc.T("shop.buy"),
                enabled: offer.Available && locked == StockLock.Open && affordable,

                // The lock is named before the price: a player who cannot buy this at any amount of
                // gold must not be told to come back with more of it.
                refusal: locked != StockLock.Open ? LockRefusal(locked, offer.Row!)
                    : offer.Available ? Loc.T("shop.cannot_afford")
                    : Loc.T("shop.sold_out"),
                onPressed: () => Buy(shop, captured, price),
                specialty: specialty,
                locked: locked != StockLock.Open);
        }
    }

    private void BuildPack(ShopResource shop, bool haggled)
    {
        UiTheme.ClearChildren(_packList);

        if (_pack is not { } pack)
        {
            _packHeader.Text = Loc.T("shop.your_pack");
            return;
        }

        // The merchant's own coin, when they have a finite amount of it — a player dumping a field of
        // loot has to be able to see why the last few rows stopped being sellable.
        //
        // ⚠️ A broker has none, and the header says what she has instead (38P): a shelf. She fronts no
        // money at all, so there is no purse to run down and nothing to explain a refused row with.
        int purse = shop.IsConsignment ? -1 : Stock()?.PurseFor(shop) ?? -1;
        string header = shop.IsConsignment
            ? $"{Loc.T("shop.your_pack")}   {Loc.TF("shop.consign_shelf", Ledger()?.Pending ?? 0)}"
            : purse >= 0
                ? $"{Loc.T("shop.your_pack")}   {Loc.TF("shop.vendor_purse", purse)}"
                : Loc.T("shop.your_pack");
        _packHeader.Text = $"{header}   {Loc.TF("storage.slots", pack.UsedSlots, pack.Capacity)}";

        if (pack.UsedSlots == 0)
        {
            _packList.AddChild(UiTheme.Body(Loc.T("shop.pack_empty"), UiTheme.Dim));
            return;
        }

        // Snapshot: the button closures mutate this list, and a row built off a stack that has since
        // been removed would sell a ghost.
        foreach (ItemStack stack in new List<ItemStack>(pack.Stacks))
        {
            ItemInstance instance = stack.Instance;
            bool sellable = ShopPricing.Sellable(instance.Type, IsCurrency(instance));
            bool inTrade = InTrade(shop, instance);
            bool specialty = IsSpecialty(shop, instance);

            // 38H: the stack's units are priced one at a time as the merchant's appetite falls, so
            // selling twenty at once pays exactly what selling them singly would. The multiply this
            // replaced made dumping the whole stack strictly optimal.
            //
            // ⚠️ A broker's rows take neither correction (38P). She never touches the goods, so there
            // is no appetite to glut and no purse to run down — a stack of twenty lists for twenty
            // times one, which is the whole reason to walk them across the square to her.
            int absorbed = shop.IsConsignment ? 0 : Stock()?.AbsorbedOf(shop, instance.TemplateId) ?? 0;

            // 38G, and the broker takes it too: she fronts no money and takes no saturation, but she
            // still stands somewhere, and what she can get for a thing depends on where that is.
            int localSell = shop.LocalValue(instance.Value, instance.Template.TagList());
            int unitPrice = shop.IsConsignment
                ? ConsignmentRules.Net(
                    ConsignmentRules.Gross(localSell, shop.ConsignFraction), shop.ConsignCommission)
                : ShopPricing.SellPrice(
                    localSell, ShopPricing.SellFractionFor(shop.SellFraction, specialty, haggled));
            int payout = !sellable || !inTrade ? 0
                : shop.IsConsignment ? unitPrice * stack.Quantity
                : ShopStock.SaturatedPayout(unitPrice, absorbed, stack.Quantity, shop.RestockDays);
            bool glutted = ShopStock.SaturationMultiplier(absorbed, shop.RestockDays) < 1f;

            // Five refusals, each named separately: not for sale at all, not this merchant's trade,
            // nothing an honest merchant will touch, worth nothing, or the merchant cannot cover it.
            // Collapsing them would tell a player with a Legendary to try a cheaper shop when the real
            // answer is to come back after a restock — and 38F's addition is the one that has somewhere
            // to send them, so it names the trade.
            bool afforded = purse < 0 || payout <= purse;
            string refusal = !sellable ? Loc.T("shop.unsellable")
                : !inTrade ? TradeRefusal(shop, instance)
                : payout <= 0 ? Loc.T("shop.worthless")
                : Loc.T("shop.vendor_broke");

            // The broker's price line names the wait as well as the money: an offer that is better
            // than every counter in town and does not pay today is only a good deal if the player can
            // see both halves of it before pressing.
            string priceText = !sellable || !inTrade ? string.Empty
                : shop.IsConsignment ? Loc.TF("shop.consign_price", payout, shop.ConsignDays)
                : Loc.TF("shop.price", payout);

            ItemStack captured = stack;
            AddRow(
                _packList,
                instance,
                stack.Quantity,
                priceText: priceText,
                action: Loc.T(shop.IsConsignment ? "shop.consign" : "shop.sell"),
                enabled: sellable && inTrade && payout > 0 && afforded,
                refusal: refusal,
                onPressed: shop.IsConsignment
                    ? () => Consign(shop, captured, unitPrice)
                    : () => Sell(shop, captured, payout),
                specialty: specialty,
                glutted: glutted);
        }
    }

    /// <summary>
    /// One trade row, on the 37.5C item vocabulary so an item looks the same here as in the pack:
    /// a <see cref="UiTheme.Card"/> spined in its rarity (never <c>UiTheme.Panel()</c>, which is a
    /// full screen carrying a brass rule and a grain shader), an <see cref="ItemSlot"/>, and the
    /// price beside the action. A refused row keeps its button, greyed and explained — the 37 rule
    /// that every refusal names itself, and UI_STYLE §2's that <c>Disabled</c> always carries a
    /// second channel.
    /// </summary>
    private static void AddRow(
        VBoxContainer list,
        ItemInstance instance,
        int quantity,
        string priceText,
        string action,
        bool enabled,
        string refusal,
        System.Action onPressed,
        bool specialty = false,
        bool glutted = false,
        bool locked = false)
    {
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button slot = ItemSlot.Build(instance, quantity, selected: false, size: 34f);
        slot.FocusMode = Control.FocusModeEnum.None; // the action button is the row's focus target
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(slot);

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", 0);

        Label name = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        name.TooltipText = instance.Template.Description;
        text.AddChild(name);

        // A price that moved must say why it moved — the same rule the standing caption follows. The
        // full line-by-line breakdown is 38U's; this is the marker 38F owes, and without it a payout
        // 25% above the shop across the square reads as one of the two being mispriced.
        if (specialty || glutted || locked)
        {
            var trade = new HBoxContainer();
            trade.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            if (specialty)
            {
                trade.AddChild(UiTheme.Chip(Loc.T("shop.specialty"), UiTheme.Accent));
            }

            // 38I: the chip is the glance, the tooltip is the reason. A greyed button alone reads as
            // "you cannot afford this", which for a gated row is the wrong answer at any price.
            if (locked)
            {
                trade.AddChild(UiTheme.Chip(Loc.T("shop.locked"), UiTheme.Disabled));
            }

            // 38H: a payout that fell has to say why, or a merchant who paid 6 gold yesterday and 3 today
            // reads as a pricing bug rather than as a market the player has been filling up.
            if (glutted)
            {
                trade.AddChild(UiTheme.Chip(Loc.T("shop.glutted"), UiTheme.Dim));
            }

            text.AddChild(trade);
        }

        if (instance.HasAffixes)
        {
            var chips = new HBoxContainer();
            chips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            foreach (ItemAffix affix in instance.Affixes)
            {
                chips.AddChild(UiTheme.Chip(affix.DisplayValue, UiTheme.Good));
            }

            text.AddChild(chips);
        }

        row.AddChild(text);

        Label price = UiTheme.Caption(priceText, enabled ? UiTheme.Accent : UiTheme.Disabled);
        price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(price);

        Button button = UiTheme.Action(action);
        button.Disabled = !enabled;
        button.TooltipText = enabled ? string.Empty : refusal;
        button.Pressed += onPressed;
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(button);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(row);
        card.AddChild(pad);
        list.AddChild(card);
    }
}
