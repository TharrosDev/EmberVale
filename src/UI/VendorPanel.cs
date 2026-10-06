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
using Embervale.Progression;
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
    private Label _purse = null!;
    private Label _standing = null!;
    private Label _localTrade = null!;
    private Label _localShock = null!;
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
    private VBoxContainer _tradeDetail = null!;
    private Label _feedback = null!;
    private ItemInstance? _selectedTrade;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private ShopResource? _shop;
    private bool _justOpened;

    /// <summary>How many of the selected pack stack the detail's "sell some" picker is set to.</summary>
    private int _sellQuantity = 1;

    /// <summary>The most a buyback shelf remembers. Twelve is a counter's worth: enough to undo a
    /// "sell all junk" that took something it should not have, small enough to read at a glance.</summary>
    private const int BuybackCapacity = 12;

    /// <summary>One thing the player sold and can still have back for what they were paid.</summary>
    private sealed class BuybackEntry
    {
        public required string ShopId { get; init; }
        public required ItemInstance Instance { get; init; }
        public int Quantity { get; set; }
        public int Price { get; set; }
    }

    /// <summary>
    /// The buyback shelf, newest first. ⚠️ <b>Session state, deliberately never saved</b>: it is an
    /// undo for a slip of the hand, not a second inventory. Static so it outlives the window being
    /// rebuilt, and emptied on every load - a save loaded from before the sale still holds the item,
    /// so an entry that survived the load would hand the player a second copy of it.
    /// </summary>
    private static readonly List<BuybackEntry> Buybacks = new();

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        var identity = new HBoxContainer();
        identity.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        identity.AddChild(UiIcon.Create(UiIcon.Kind.Service, 30f, UiTheme.Accent));
        _title = UiTheme.Title(string.Empty);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.AddChild(_title);
        var purseLockup = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        purseLockup.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        purseLockup.AddChild(UiIcon.Create(UiIcon.Kind.Currency, 20f, UiTheme.Accent));
        _purse = UiTheme.Body(string.Empty, UiTheme.Accent);
        purseLockup.AddChild(_purse);
        identity.AddChild(purseLockup);

        // The standing band is its own group, so the title gets a full SpaceMd under it (the column's
        // SpaceSm plus this) instead of the banner sitting flush against it.
        var identityGap = new MarginContainer();
        identityGap.AddThemeConstantOverride("margin_bottom", UiTheme.SpaceXs);
        identityGap.AddChild(identity);
        column.AddChild(identityGap);

        PanelContainer context = UiTheme.Band(UiTheme.IronLit);
        var contextCopy = new VBoxContainer();
        contextCopy.AddThemeConstantOverride("separation", UiTheme.LineGap);
        context.AddChild(contextCopy);

        // A price that moved must say why it moved. Without this line the discount is invisible and
        // reads as the shop being mispriced — the same reason every Phase 37 refusal names itself.
        _standing = UiTheme.Caption(string.Empty);
        contextCopy.AddChild(_standing);

        // What the place itself does to the prices (38G), directly under what the merchant thinks of
        // you — two different reasons a number moved, in the order the player meets them.
        _localTrade = UiTheme.Caption(string.Empty);
        _localTrade.AddThemeColorOverride("font_color", UiTheme.Dim);
        contextCopy.AddChild(_localTrade);

        // The event, under the standing state of the trade (38T): the line above says what this place
        // is normally like, this one says what has happened to it this week. Two lines rather than one
        // sentence because the first is a fact about the place and the second expires.
        _localShock = UiTheme.Caption(string.Empty);
        contextCopy.AddChild(_localShock);

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
        contextCopy.AddChild(_investRow);

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
        contextCopy.AddChild(_haggleRow);
        column.AddChild(context);

        // The detail is a Card already (ItemSlot.Detail), so it sits straight on the panel: wrapping it in a
        // Band drew two frames and two left spines around one item.
        _tradeDetail = new VBoxContainer();
        _tradeDetail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        column.AddChild(_tradeDetail);

        // Why the last press did nothing: a full pack on a purchase used to refund the gold and say
        // nothing at all, which reads as the Buy button being broken.
        _feedback = UiTheme.Caption(string.Empty, UiTheme.Bad);
        _feedback.Visible = false;
        column.AddChild(_feedback);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceLg);

        // A section's worth of space above the lists, in place of a rule: each list names itself.
        var body = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("margin_top", UiTheme.SpaceXs);
        body.AddChild(columns);
        column.AddChild(body);

        (_waresHeader, _waresList) = BuildColumn(columns);
        (_packHeader, _packList) = BuildColumn(columns);
    }

    /// <summary>One titled scroll column; both sides are the same shape.</summary>
    private static (Label Header, VBoxContainer List) BuildColumn(Node parent)
    {
        // Bare on the panel, like the stash: a Band around a list of Cards was a frame inside a frame
        // that cost every row 32 px of width.
        var side = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        side.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
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
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<ShopOpenedEvent>(OnShopOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    /// <summary>A load is another timeline: nothing sold in the abandoned one can be bought back.</summary>
    private void OnGameLoaded(GameLoadedEvent e)
    {
        Buybacks.Clear();
        MarkDirty();
    }

    private void SetFeedback(string text)
    {
        _feedback.Text = text;
        _feedback.Visible = text.Length > 0;
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
        _selectedTrade = null;
        SetFeedback(string.Empty);

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
            SetFeedback(Loc.T("shop.pack_full"));
            ItemTransfer.AnnouncePackFull(offer.Instance, 1);
            return;
        }

        SetFeedback(string.Empty);

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
    /// <remarks>
    /// <paramref name="quantity"/> is how much of the stack goes (the row button passes all of it,
    /// the detail's picker passes part), and <paramref name="payout"/> is the quote for exactly that
    /// many. A locked stack is refused here as well as on the button: the lock is the player's own
    /// word that this one is not for sale, and "sell all junk" must not be able to argue with it.
    /// What was sold goes on the buyback shelf at the price it fetched.
    /// </remarks>
    private void Sell(ShopResource shop, ItemStack stack, int quantity, int payout)
    {
        if (_pack is not { } pack || ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return;
        }

        ItemInstance instance = stack.Instance;
        if (!ShopPricing.Sellable(instance.Type, IsCurrency(instance)) ||
            !InTrade(shop, instance) ||
            instance.Locked ||
            quantity <= 0 || quantity > stack.Quantity ||
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

        // ItemTransfer.Take keeps the split the comment above describes (by id only when this stack
        // is all the player holds of the thing, by reference otherwise) and adds the partial case.
        bool removed = ItemTransfer.Take(pack, stack, quantity);

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
        stock?.Absorb(shop, instance.TemplateId, quantity);

        // The goods have reached the settlement (38T), so a shortage of their kind is that much nearer
        // broken. ⚠️ Here beside Absorb for exactly its reason: every early return above leaves the
        // player holding the item, and none of them may credit a haul that did not arrive. A sale to a
        // broker deliberately does NOT count — she is holding it for the player, not selling it here.
        if (shop.CellId.Length > 0)
        {
            Shocks()?.Deliver(shop.CellId, instance.Template.TagList(), quantity);
        }

        FenceStanding(shop, instance);

        // On the shelf at what it fetched, unmarked: a junk mark on something the player went back
        // for would put it straight into the next "sell all junk".
        ItemPresentation.PushRecent(
            Buybacks,
            new BuybackEntry
            {
                ShopId = shop.Id,
                Instance = ItemTransfer.Unmarked(instance),
                Quantity = quantity,
                Price = payout,
            },
            BuybackCapacity);

        SetFeedback(string.Empty);
        MarkDirty();
    }

    /// <summary>
    /// Buys a sold item back for exactly what the merchant paid. Delivered first and charged for
    /// what actually fitted: the player was checked to hold the full price a line earlier, so the
    /// charge cannot fall short, and a stack that only partly fits leaves its remainder on the
    /// shelf at the remainder of its price rather than being lost or refused whole. The merchant's
    /// purse gets the coin back (clamped to its ceiling, like every refund).
    /// </summary>
    private void BuyBack(ShopResource shop, BuybackEntry entry)
    {
        if (_pack is not { } pack || !Buybacks.Contains(entry) || !ShopPricing.CanAfford(entry.Price, Purse()))
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        int moved = pack.AddInstance(entry.Instance.Copy(), entry.Quantity);
        if (moved <= 0)
        {
            SetFeedback(Loc.T("shop.pack_full"));
            ItemTransfer.AnnouncePackFull(entry.Instance, entry.Quantity);
            return;
        }

        int charge = ItemPresentation.ShareOf(entry.Price, moved, entry.Quantity);
        if (!pack.RemoveItem(GameIds.Currency.Gold, charge))
        {
            Log.Warn($"Shop '{shop.Id}': bought back '{entry.Instance.TemplateId}' but could not charge {charge}g.");
        }

        Stock()?.RefundPurse(shop, charge);

        if (moved >= entry.Quantity)
        {
            Buybacks.Remove(entry);
            SetFeedback(string.Empty);
        }
        else
        {
            entry.Quantity -= moved;
            entry.Price -= charge;
            SetFeedback(Loc.T("shop.pack_full"));
        }

        MarkDirty();
    }

    /// <summary>
    /// What "sell all junk" would do at this counter right now: the junk stacks this merchant will
    /// take, in held order, each with the payout it would get at that point in the run. The run is
    /// simulated the way it will execute - every stack sold deepens the merchant's glut for the next
    /// one of its kind and drains the purse the rest must fit in - so the total on the button is the
    /// total paid. <paramref name="skipped"/> counts junk stacks left behind (not this merchant's
    /// trade, worth nothing here, or more than the purse can still cover).
    /// </summary>
    private List<(ItemStack Stack, int Payout)> JunkPlan(ShopResource shop, bool haggled, out int skipped)
    {
        var plan = new List<(ItemStack, int)>();
        skipped = 0;
        if (_pack is not { } pack || shop.IsConsignment)
        {
            return plan;
        }

        int purse = Stock()?.PurseFor(shop) ?? -1;
        var soldThisRun = new Dictionary<string, int>();
        foreach (ItemStack stack in pack.JunkStacks())
        {
            ItemInstance instance = stack.Instance;
            soldThisRun.TryGetValue(instance.TemplateId, out int already);

            int payout = ShopPricing.Sellable(instance.Type, IsCurrency(instance)) && InTrade(shop, instance)
                ? SellQuoteFor(shop, instance, stack.Quantity, haggled, already).Total
                : 0;

            if (payout <= 0 || (purse >= 0 && payout > purse))
            {
                skipped++;
                continue;
            }

            plan.Add((stack, payout));
            soldThisRun[instance.TemplateId] = already + stack.Quantity;
            if (purse >= 0)
            {
                purse -= payout;
            }
        }

        return plan;
    }

    /// <summary>Sells the plan, in the plan's order, through the one <see cref="Sell"/> every other
    /// sale takes - so the purse, the glut, the shortage relief and the fence's standing all move
    /// exactly as they would for the same stacks sold by hand.</summary>
    private void SellAllJunk(ShopResource shop)
    {
        foreach ((ItemStack stack, int payout) in JunkPlan(shop, DealStruck(shop), out _))
        {
            Sell(shop, stack, stack.Quantity, payout);
        }
    }

    /// <summary>
    /// The sell-side quote for <paramref name="quantity"/> of an item at this counter: the same two
    /// <see cref="PriceBreakdown"/> calls, with the same arguments, that priced a pack row before
    /// this was a method. It exists so the row, the "sell some" picker and the junk total ask one
    /// place. <paramref name="soldThisRun"/> is units of the same template a bulk sale has already
    /// handed over in this pass, which the merchant's appetite has to count.
    /// </summary>
    private PriceQuote SellQuoteFor(ShopResource shop, ItemInstance instance, int quantity, bool haggled, int soldThisRun = 0)
    {
        bool specialty = IsSpecialty(shop, instance);

        // 38H: the stack's units are priced one at a time as the merchant's appetite falls, so
        // selling twenty at once pays exactly what selling them singly would. The multiply this
        // replaced made dumping the whole stack strictly optimal.
        //
        // ⚠️ A broker's rows take neither correction (38P). She never touches the goods, so there
        // is no appetite to glut and no purse to run down — a stack of twenty lists for twenty
        // times one, which is the whole reason to walk them across the square to her.
        int absorbed = shop.IsConsignment
            ? 0
            : (Stock()?.AbsorbedOf(shop, instance.TemplateId) ?? 0) + soldThisRun;

        // 38G, and the broker takes it too: she fronts no money and takes no saturation, but she
        // still stands somewhere, and what she can get for a thing depends on where that is.
        //
        // 38U: one quote per row, and it is the payout rather than a commentary on it. ⚠️ The two
        // branches differ in more than a fraction — a broker's stack is a multiply and a counter's
        // is 38H's decaying sum — so the split lives in PriceBreakdown where both endings are
        // written down beside each other, not in a ternary that hides which one ran.
        (int localSell, string localTag, bool shocked) =
            shop.LocalQuote(instance.Value, instance.Template.TagList());
        return shop.IsConsignment
            ? PriceBreakdown.Consign(
                instance.Value, localSell, TagName(localTag), shocked,
                shop.ConsignFraction, shop.ConsignCommission, quantity)
            : PriceBreakdown.Sell(
                instance.Value, localSell, TagName(localTag), shocked,
                shop.SellFraction, specialty, haggled, quantity, absorbed, shop.RestockDays,
                SellPerkFactor());
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
            instance.Locked ||
            netPerUnit <= 0)
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        bool removed = ItemTransfer.Take(pack, stack, stack.Quantity);

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

    private static SupplyShockService? Shocks() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SupplyShockService service)
            ? service
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

        _title.Text = Loc.TF("shop.title", Loc.T(shop.NameKey));
        _purse.Text = Loc.TF("shop.purse", Purse());

        ReputationTier tier = StandingWith(shop);

        // Asked once and threaded into both sides, so the wares and the pack cannot disagree about
        // whether a deal was struck — the same reason `tier` is resolved here rather than per row.
        bool haggled = DealStruck(shop);

        BuildStanding(shop, tier);
        BuildLocalTrade(shop);
        BuildInvest(shop);
        BuildHaggle(shop, haggled);
        BuildWares(shop, tier, haggled);
        BuildBuyback(shop);
        BuildPack(shop, haggled);
        RebuildTradeDetail(shop, haggled);
    }

    /// <summary>
    /// Whether today's negotiation with this merchant was won. ⚠️ <b>Two questions, both required:</b>
    /// the ledger says whether the player has asked (bounded, saved), <see cref="HaggleRules.Succeeds"/>
    /// says what the answer was (derived, never saved). An unasked merchant prices normally even on a day
    /// they would have said yes — the discount is something the player does, not something the day gives.
    /// </summary>
    private bool DealStruck(ShopResource shop)
    {
        if (shop.HaggleChance <= 0 || Haggles() is not { } ledger)
        {
            return false;
        }

        int day = CurrentDay();

        return ledger.TriedToday(shop.Id, day) &&
            HaggleRules.Succeeds(day, shop.Id, HaggleChanceFor(shop));
    }

    /// <summary>The chance the window quotes and the roll uses: the shop's authored chance plus the player's
    /// haggle perks. ⚠️ <c>PerkEffectMath.HaggleChance</c> leaves a merchant who never haggles at 0, so a perk
    /// shapes how often a deal lands and never opens one that was not authored.</summary>
    private int HaggleChanceFor(ShopResource shop) =>
        PerkEffectMath.HaggleChance(shop.HaggleChance, PerkQuery.Of(_player, PerkEffectKind.HaggleChanceBonus));

    /// <summary>The player's perk factor on what a shop asks (1 with no perks). Threaded into every quote.</summary>
    private float BuyPerkFactor() => PerkEffectMath.BuyFactor(PerkQuery.Of(_player, PerkEffectKind.BuyDiscount));

    /// <summary>The player's perk factor on what a shop pays (1 with no perks).</summary>
    private float SellPerkFactor() => PerkEffectMath.SellFactor(PerkQuery.Of(_player, PerkEffectKind.SellBonus));

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
            _localShock.Visible = false;
            return;
        }

        // ⚠️ The caption reads the SHOCKED lists, not the authored ones (38T). The prices beside it come
        // through ShopResource.LocalValue, which applies whatever is running today — a caption built
        // from the .tres would calmly explain the wrong numbers on exactly the days the feature exists
        // for, which is worse than no caption at all.
        int day = CurrentDay();
        SupplyShock? shock = Shocks()?.At(cell.Id, day);
        (List<string> live, List<string> wanted) = Shocks() is { } service
            ? service.TagsFor(cell, day)
            : (ShopResource.Plain(cell.Surplus), ShopResource.Plain(cell.Demand));

        BuildShockLine(shock, day);

        string surplus = TagNames(live);
        string demand = TagNames(wanted);
        _localTrade.Visible = surplus.Length > 0 || demand.Length > 0;

        _localTrade.Text = surplus.Length > 0 && demand.Length > 0
            ? Loc.TF("shop.local_trade", surplus, demand)
            : surplus.Length > 0
                ? Loc.TF("shop.local_surplus", surplus)
                : Loc.TF("shop.local_demand", demand);
    }

    /// <summary>The cell's tags in the player's language — the same `trade.tag.<c>x</c>` keys the
    /// refusal line names a merchant's trade with, so the two cannot describe one tag differently.</summary>
    /// <summary>
    /// The caravan news, in one line (38T): what has happened, and how much longer it lasts. A shortage
    /// also says how far along breaking it is, because hauling goods in is the one thing the player can
    /// do about a shock and nothing else in the game would ever mention it.
    /// </summary>
    private void BuildShockLine(SupplyShock? active, int day)
    {
        if (active is not { } shock)
        {
            _localShock.Visible = false;
            return;
        }

        string what = shock.Kind == ShockKind.Fair ? string.Empty : Loc.T($"trade.tag.{shock.Tag}");
        int left = shock.DaysLeft(day);

        string text = shock.Kind switch
        {
            ShockKind.Shortage => Loc.TF("shop.shock_shortage", what, left),
            ShockKind.Glut => Loc.TF("shop.shock_glut", what, left),
            _ => Loc.TF("shop.shock_fair", left),
        };

        if (shock.Kind == ShockKind.Shortage && Shocks() is { } service)
        {
            text += " " + Loc.TF(
                "shop.shock_relief", service.DeliveredTo(shock), SupplyShockRules.ReliefUnits);
        }

        _localShock.Text = text;
        _localShock.Visible = true;
        _localShock.AddThemeColorOverride(
            "font_color", shock.Kind == ShockKind.Shortage ? UiTheme.Bad : UiTheme.Good);
    }

    /// <summary>One trade tag in the player's language, or empty for no tag — the same
    /// <c>trade.tag.*</c> keys <see cref="TagNames"/> and the refusal line use, so a breakdown line and
    /// the caption above it cannot name one tag two ways.</summary>
    private static string TagName(string tag) =>
        tag.Length > 0 ? Loc.T($"trade.tag.{tag}") : string.Empty;

    private static string TagNames(List<string> tags)
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
            : Loc.TF("shop.haggle_offer", HaggleChanceFor(shop));
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

        if (!HaggleRules.Succeeds(day, shop.Id, HaggleChanceFor(shop)) &&
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
        if (_selectedTrade == null && offers.Count > 0)
        {
            _selectedTrade = offers[0].Instance;
        }
        foreach (ShopOffer offer in offers)
        {
            bool specialty = IsSpecialty(shop, offer.Instance);

            // 38G: what the good is worth HERE, not in the realm at large. Both sides of this counter
            // spread over the same local value, so the 38A invariant is untouched at the shop.
            //
            // 38U: the quote below IS the price — the row shows `quote.Total` and Buy charges it. The
            // breakdown is not a second opinion about a number computed elsewhere, which is the only
            // way an explanation cannot drift from a bill.
            (int local, string localTag, bool shocked) =
                shop.LocalQuote(offer.Instance.Value, offer.Instance.Template.TagList());
            PriceQuote quote = PriceBreakdown.Buy(
                offer.Instance.Value, local, TagName(localTag), shocked,
                shop.BuyMarkup, tier, specialty, haggled, BuyPerkFactor());
            int price = quote.Total;
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
                priceTooltip: PriceTooltip.Render(quote),
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

        // Snapshot: the button closures mutate this list, and a row built off a stack that has since
        // been removed would sell a ghost. Pack then material bag: trade goods are materials, and a
        // player whose ore lives in the bag still has ore to sell.
        var held = new List<ItemStack>(pack.AllStacks);
        if (held.Count == 0)
        {
            _packList.AddChild(UiTheme.Body(Loc.T("shop.pack_empty"), UiTheme.Dim));
            return;
        }

        BuildJunkRow(shop, haggled);

        foreach (ItemStack stack in held)
        {
            ItemInstance instance = stack.Instance;
            bool sellable = ShopPricing.Sellable(instance.Type, IsCurrency(instance));
            bool inTrade = InTrade(shop, instance);
            bool specialty = IsSpecialty(shop, instance);
            bool kept = instance.Locked;

            // The quote itself, and the 38H / 38G / 38P / 38U reasoning behind each argument, is in
            // SellQuoteFor: one place asked by this row, the "sell some" picker and the junk total.
            int absorbed = shop.IsConsignment ? 0 : Stock()?.AbsorbedOf(shop, instance.TemplateId) ?? 0;
            PriceQuote quote = SellQuoteFor(shop, instance, stack.Quantity, haggled);

            int unitPrice = quote.Unit;
            int payout = !sellable || !inTrade ? 0 : quote.Total;
            bool glutted = ShopStock.SaturationMultiplier(absorbed, shop.RestockDays) < 1f;

            // Six refusals, each named separately: not for sale at all, not this merchant's trade,
            // nothing an honest merchant will touch, worth nothing, locked by the player, or the
            // merchant cannot cover it. Collapsing them would tell a player with a Legendary to try a
            // cheaper shop when the real answer is to come back after a restock — and 38F's addition
            // is the one that has somewhere to send them, so it names the trade. The lock comes after
            // the merchant's own reasons: unlocking something she would not buy anyway is a wasted trip
            // to the pack.
            bool afforded = purse < 0 || payout <= purse;
            string refusal = !sellable ? Loc.T("shop.unsellable")
                : !inTrade ? TradeRefusal(shop, instance)
                : payout <= 0 ? Loc.T("shop.worthless")
                : kept ? Loc.T("shop.locked_item")
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
                enabled: sellable && inTrade && payout > 0 && afforded && !kept,
                refusal: refusal,
                onPressed: shop.IsConsignment
                    ? () => Consign(shop, captured, unitPrice)
                    : () => Sell(shop, captured, captured.Quantity, payout),

                // A refused row explains the refusal, not the arithmetic — quoting a breakdown of a
                // payout nobody is being offered is the "come back with more gold" mistake in another
                // costume.
                priceTooltip: sellable && inTrade ? PriceTooltip.Render(quote) : string.Empty,
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
    private void AddRow(
        VBoxContainer list,
        ItemInstance instance,
        int quantity,
        string priceText,
        string action,
        bool enabled,
        string refusal,
        System.Action onPressed,
        string priceTooltip = "",
        bool specialty = false,
        bool glutted = false,
        bool locked = false)
    {
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button slot = ItemSlot.Build(instance, quantity, selected: false, size: ItemSlot.RowSize);
        slot.FocusMode = Control.FocusModeEnum.All;
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        slot.TooltipText = Loc.T("shop.inspect_hint");
        slot.Pressed += () =>
        {
            if (!ReferenceEquals(_selectedTrade, instance))
            {
                _sellQuantity = 1; // a picker left at 30 for ore must not open at 30 for potions
            }

            _selectedTrade = instance;
            MarkDirty();
        };
        row.AddChild(slot);

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label name = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        name.TooltipText = instance.Template.Description;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(name);

        // A price that moved must say why it moved — the same rule the standing caption follows. The
        // full line-by-line breakdown is 38U's; this is the marker 38F owes, and without it a payout
        // 25% above the shop across the square reads as one of the two being mispriced.
        if (specialty || glutted || locked)
        {
            HFlowContainer trade = UiTheme.FlowRow();
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
            HFlowContainer chips = UiTheme.FlowRow();
            foreach (ItemAffix affix in instance.Affixes)
            {
                chips.AddChild(UiTheme.Chip(affix.DisplayValue, UiTheme.Good));
            }

            text.AddChild(chips);
        }

        row.AddChild(text);

        Label price = UiTheme.Caption(priceText, enabled ? UiTheme.Accent : UiTheme.Disabled);
        price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // 38U's whole deliverable: the number says why it is that number, line by line, from the same
        // quote the button charges. ⚠️ This only shows because UiTheme's label builders set
        // MouseFilterEnum.Pass — a Godot Label defaults to Ignore and is never the node under the
        // cursor, which is why the item description one line up had been dead since 38A.
        price.TooltipText = priceTooltip;
        row.AddChild(price);

        Button button = UiTheme.Action(action);
        button.Disabled = !enabled;
        button.TooltipText = enabled ? string.Empty : refusal;
        button.Pressed += onPressed;
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(button);

        card.AddChild(row);
        list.AddChild(card);
    }

    /// <summary>
    /// The "sell all junk" line at the head of the pack column: how many stacks will go and for how
    /// much, before the press. Absent when nothing is marked. When something is marked and none of
    /// it can be sold here the button stays, greyed, saying so - a junk pile this merchant will not
    /// touch is worth knowing about at the counter rather than after walking away.
    /// </summary>
    private void BuildJunkRow(ShopResource shop, bool haggled)
    {
        if (_pack is not { } pack || shop.IsConsignment || pack.JunkStacks().Count == 0)
        {
            return;
        }

        List<(ItemStack Stack, int Payout)> plan = JunkPlan(shop, haggled, out int skipped);
        int total = 0;
        foreach ((ItemStack _, int payout) in plan)
        {
            total += payout;
        }

        PanelContainer band = UiTheme.Band(UiTheme.IronLit);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var copy = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        copy.AddThemeConstantOverride("separation", UiTheme.LineGap);
        copy.AddChild(UiTheme.Body(
            plan.Count > 0 ? Loc.TF("shop.junk_preview", plan.Count, total) : Loc.T("shop.junk_none_sellable"),
            plan.Count > 0 ? UiTheme.Accent : UiTheme.Dim));
        if (skipped > 0 && plan.Count > 0)
        {
            copy.AddChild(UiTheme.Caption(Loc.TF("shop.junk_skipped", skipped)));
        }

        row.AddChild(copy);

        Button sell = UiTheme.Action(Loc.T("shop.sell_junk"));
        sell.Disabled = plan.Count == 0;
        sell.TooltipText = plan.Count == 0 ? Loc.T("shop.junk_none_sellable") : Loc.T("shop.sell_junk_hint");
        sell.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        sell.Pressed += () => SellAllJunk(shop);
        row.AddChild(sell);

        band.AddChild(row);
        _packList.AddChild(band);
    }

    /// <summary>
    /// The buyback shelf, under the merchant's own wares: what the player sold at this counter this
    /// session, newest first, each for the price it fetched. Nothing is drawn when the shelf is
    /// empty - an empty "Buyback" heading on every counter would be furniture.
    /// </summary>
    private void BuildBuyback(ShopResource shop)
    {
        var mine = new List<BuybackEntry>();
        foreach (BuybackEntry entry in Buybacks)
        {
            if (entry.ShopId == shop.Id)
            {
                mine.Add(entry);
            }
        }

        if (mine.Count == 0)
        {
            return;
        }

        _waresList.AddChild(UiTheme.SectionRule(Loc.T("shop.buyback")));

        int purse = Purse();
        foreach (BuybackEntry entry in mine)
        {
            BuybackEntry captured = entry;
            AddRow(
                _waresList,
                entry.Instance,
                entry.Quantity,
                priceText: Loc.TF("shop.price", entry.Price),
                action: Loc.T("shop.buy_back"),
                enabled: ShopPricing.CanAfford(entry.Price, purse),
                refusal: Loc.T("shop.cannot_afford"),
                onPressed: () => BuyBack(shop, captured),
                priceTooltip: Loc.T("shop.buyback_hint"));
        }
    }

    /// <summary>
    /// The inspected item: its card, compared against what the player is wearing, and - for a stack
    /// in the player's own pack - the "sell some" picker. The comparison is the point of inspecting
    /// a ware at all: without it the question "is this better than mine" meant closing the shop,
    /// opening the pack and remembering two sets of numbers.
    /// </summary>
    private void RebuildTradeDetail(ShopResource shop, bool haggled)
    {
        UiTheme.ClearChildren(_tradeDetail);
        if (_selectedTrade is not { } item)
        {
            _tradeDetail.AddChild(UiTheme.IconLabel(
                UiIcon.Kind.Inventory, Loc.T("shop.inspect_hint"), tint: UiTheme.Dim));
            return;
        }

        _tradeDetail.AddChild(ItemSlot.Detail(item, new ItemSlot.DetailContext(
            _player?.GetComponent<EquipmentComponent>(),
            _player?.GetComponent<ProgressionComponent>()?.Level ?? 0,
            Compare: true)));

        if (HeldStack(item) is { } stack && SellSomeRow(shop, stack, haggled) is { } some)
        {
            _tradeDetail.AddChild(some);
        }
    }

    private ItemStack? HeldStack(ItemInstance instance)
    {
        if (_pack == null)
        {
            return null;
        }

        foreach (ItemStack stack in _pack.AllStacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return stack;
            }
        }

        return null;
    }

    /// <summary>
    /// The quantity picker for selling part of a stack, or null when there is no "part" to sell: a
    /// single item, a broker (she lists whole stacks), or anything this counter refuses outright.
    ///
    /// The price beside it is requoted in place as the slider moves, from the same
    /// <see cref="SellQuoteFor"/> the press then charges - never by rebuilding, which would free the
    /// slider mid-drag. A quantity the merchant's purse cannot cover greys the button and says why.
    /// </summary>
    private Control? SellSomeRow(ShopResource shop, ItemStack stack, bool haggled)
    {
        ItemInstance instance = stack.Instance;
        if (stack.Quantity < 2 || shop.IsConsignment || instance.Locked ||
            !ShopPricing.Sellable(instance.Type, IsCurrency(instance)) || !InTrade(shop, instance))
        {
            return null;
        }

        _sellQuantity = ItemPresentation.ClampQuantity(_sellQuantity, stack.Quantity, keepOne: false);
        int merchantPurse = Stock()?.PurseFor(shop) ?? -1;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label caption = UiTheme.Caption(Loc.T("shop.sell_some"));
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(caption);

        Label price = UiTheme.Body(string.Empty, UiTheme.Accent);
        price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        price.CustomMinimumSize = new Vector2(72f, 0f);
        price.HorizontalAlignment = HorizontalAlignment.Right;

        Button sell = UiTheme.Action(Loc.T("shop.sell"));
        sell.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        void Requote(int quantity)
        {
            _sellQuantity = quantity;
            PriceQuote quote = SellQuoteFor(shop, instance, quantity, haggled);
            bool covered = merchantPurse < 0 || quote.Total <= merchantPurse;
            price.Text = Loc.TF("shop.price", quote.Total);
            price.TooltipText = PriceTooltip.Render(quote);
            sell.Disabled = quote.Total <= 0 || !covered;
            sell.TooltipText = quote.Total <= 0 ? Loc.T("shop.worthless")
                : covered ? string.Empty
                : Loc.T("shop.vendor_broke");
        }

        HBoxContainer picker = QuantityPicker.Build(1, stack.Quantity, _sellQuantity, Requote);
        picker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(picker);
        row.AddChild(price);

        sell.Pressed += () =>
        {
            int quantity = ItemPresentation.ClampQuantity(_sellQuantity, stack.Quantity, keepOne: false);
            Sell(shop, stack, quantity, SellQuoteFor(shop, instance, quantity, DealStruck(shop)).Total);
        };
        row.AddChild(sell);

        Requote(_sellQuantity);
        return row;
    }
}
