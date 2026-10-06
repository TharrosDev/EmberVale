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
    /// <summary>Which list the inspected item is on. It decides what accept does to it.</summary>
    private enum Side
    {
        Wares,
        Buyback,
        Pack,
    }

    private Label _title = null!;
    private Control _wipe = null!;
    private Label _purse = null!;
    private Label _vendorPurseOwner = null!;
    private Label _vendorPurse = null!;
    private Label _standing = null!;
    private Label _localTrade = null!;
    private Label _localShock = null!;
    private VBoxContainer _investRow = null!;
    private Label _investLabel = null!;
    private Button _investButton = null!;
    private VBoxContainer _haggleRow = null!;
    private Label _haggleLabel = null!;
    private Button _haggleButton = null!;
    private Label _waresHeader = null!;
    private Label _waresNote = null!;
    private Label _packHeader = null!;
    private Label _packNote = null!;
    private VBoxContainer _waresList = null!;
    private VBoxContainer _packList = null!;
    private ScrollContainer _detailScroll = null!;
    private VBoxContainer _tradeDetail = null!;
    private HFlowContainer _order = null!;

    private readonly List<Button> _waresRows = new();
    private readonly List<Button> _packRows = new();

    private ItemInstance? _selectedTrade;
    private Side _selectedSide;

    /// <summary>The slot wearing the selection frame, so a selection that follows focus moves the
    /// frame in place instead of rebuilding both lists on every step.</summary>
    private Button? _selectedSlot;
    private ItemInstance? _selectedSlotItem;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private ShopResource? _shop;
    private bool _justOpened;
    private bool _detailDirty;

    /// <summary>Focus is to go to the selected row after the next rebuild (on opening, and after a
    /// capture hook chose the selection). Until it has, a row taking focus does not reselect.</summary>
    private bool _focusSelection;
    private Button? _selectedRow;

    /// <summary>The total the junk confirm named on the last rebuild; zero when it is not showing.</summary>
    private int _junkTotalShown;

    /// <summary>Why the last press did nothing, shown in the order bar until the next press or selection.</summary>
    private string _feedback = string.Empty;

    /// <summary>"Sell all junk" has been pressed once and is waiting for its confirm.</summary>
    private bool _junkArmed;

    /// <summary>How many of the selected pack stack the order bar's picker is set to.</summary>
    private int _sellQuantity = 1;

    /// <summary>How many of the selected ware the order bar's picker is set to.</summary>
    private int _buyQuantity = 1;

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

    /// <summary>
    /// Units bought back per (shop, template) that have not been sold again yet. A sale of those
    /// units is the same goods crossing the counter a second time, so it earns none of a sale's
    /// side effects (shortage relief, fence standing, the merchant's glut): without this, one item
    /// sold and bought back twelve times broke a shortage. Session state like the shelf, and cleared
    /// with it - but NOT when an entry falls off the shelf or the window closes.
    /// </summary>
    private static readonly Dictionary<(string ShopId, string TemplateId), int> BoughtBack = new();

    /// <summary>Empties the shelf and its credits. A new session is another playthrough: nothing sold
    /// in the last one may be bought by this one's character. Called from
    /// <c>SessionLifecycleCoordinator.ResetSessionStatics</c>, because New Game publishes no
    /// <see cref="GameLoadedEvent"/>.</summary>
    internal static void ResetSession()
    {
        Buybacks.Clear();
        BoughtBack.Clear();
    }

    /// <summary>A counter is a blocking screen of its own: the world recedes behind the same scrim
    /// the hub screens use.</summary>
    protected override bool Dims => true;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_selectedTrade is { } item && _shop is { } shop)
            {
                entries.Add(new LegendEntry("ui_accept", AcceptVerb(shop)));
                if (item.IsEquippable)
                {
                    entries.Add(new LegendEntry(ItemSlot.CompareAction, Loc.T("item.detail.compare")));
                }
            }

            if (InputDevice.GamepadActive)
            {
                entries.Add(new LegendEntry(GameInput.LookDown, Loc.T("trade.legend.details")));
            }

            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>What accept does to the selected row, as the legend and the card's footer name it.</summary>
    private string AcceptVerb(ShopResource shop) => _selectedSide switch
    {
        Side.Wares => Loc.T("shop.buy"),
        Side.Buyback => Loc.T("shop.buy_back"),
        _ => shop.IsConsignment ? Loc.T("shop.consign") : Loc.T("trade.sell_stack"),
    };

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        VBoxContainer column = UiTheme.TradePage(
            shell, UiIcon.Kind.Service, out _title, out HBoxContainer purses, out _wipe);

        // Both purses, always in view: what the player can spend and what the merchant can pay are
        // the two numbers every refusal at a counter comes down to.
        purses.AddChild(UiTheme.PurseReadout(out Label mine, out _purse));
        mine.Text = Loc.T("trade.purse.yours");
        purses.AddChild(UiTheme.PurseReadout(out _vendorPurseOwner, out _vendorPurse));

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        column.AddChild(columns);

        // Bare on the panel: a Band around a list of Cards was a frame inside a frame that cost
        // every row 32 px of width. Each list names itself.
        columns.AddChild(UiTheme.TradeColumn(0f, out _waresHeader, out _waresNote, out _waresList));
        columns.AddChild(UiTheme.ColumnRule());
        columns.AddChild(UiTheme.TradeColumn(0f, out _packHeader, out _packNote, out _packList));
        columns.AddChild(UiTheme.ColumnRule());

        // The detail column: the inspected item's card and its price, then what is true of the
        // counter itself. It scrolls as one, so a tall card never pushes the frame.
        (_detailScroll, VBoxContainer detail) = UiTheme.ScrollList();
        _detailScroll.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        detail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        columns.AddChild(_detailScroll);

        // The detail is a Card already (ItemSlot.Detail), so it sits straight on the panel: wrapping it in a
        // Band drew two frames and two left spines around one item.
        _tradeDetail = new VBoxContainer();
        _tradeDetail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        detail.AddChild(_tradeDetail);
        detail.AddChild(BuildCounter());

        // The order bar: how many, for how much, and the verbs. Fixed under the lists so choosing a
        // quantity never moves a row, and so a refusal is said where the press was made. It wraps
        // rather than widen the page when a handheld cannot hold the picker and three verbs in a row.
        _order = UiTheme.FlowRow();
        _order.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        column.AddChild(_order);
    }

    /// <summary>What is true of this merchant rather than of any one ware: how they take to the
    /// player, what the place does to prices, the news from the road, and the two dealings a
    /// counter may offer (a haggle, a stake).</summary>
    private Control BuildCounter()
    {
        var counter = new VBoxContainer();
        counter.AddThemeConstantOverride("separation", UiTheme.LineGap);
        counter.AddChild(UiTheme.SectionRule(Loc.T("trade.counter")));

        // A price that moved must say why it moved. Without this line the discount is invisible and
        // reads as the shop being mispriced — the same reason every Phase 37 refusal names itself.
        _standing = Wrapped(UiTheme.Caption(string.Empty));
        counter.AddChild(_standing);

        // What the place itself does to the prices (38G), directly under what the merchant thinks of
        // you — two different reasons a number moved, in the order the player meets them.
        _localTrade = Wrapped(UiTheme.Caption(string.Empty, UiTheme.Dim));
        counter.AddChild(_localTrade);

        // The event, under the standing state of the trade (38T): the line above says what this place
        // is normally like, this one says what has happened to it this week. Two lines rather than one
        // sentence because the first is a fact about the place and the second expires.
        _localShock = Wrapped(UiTheme.Caption(string.Empty));
        counter.AddChild(_localShock);

        // The haggle line (38S). Hidden entirely on a merchant who will not negotiate, which is every
        // shop authored before that sub-phase — a greyed-out button on twenty counters would teach the
        // player the feature is broken.
        _haggleRow = new VBoxContainer { Visible = false };
        _haggleRow.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _haggleLabel = Wrapped(UiTheme.Caption(string.Empty));
        _haggleRow.AddChild(_haggleLabel);
        _haggleButton = UiTheme.Action(Loc.T("shop.haggle"));
        _haggleButton.Pressed += OnHagglePressed;
        _haggleRow.AddChild(_haggleButton);
        counter.AddChild(_haggleRow);

        // The stake line (38I). It is a fact about the merchant, not a ware: what it buys is her purse
        // and the rows she keeps back.
        _investRow = new VBoxContainer { Visible = false };
        _investRow.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _investLabel = Wrapped(UiTheme.Caption(string.Empty));
        _investRow.AddChild(_investLabel);
        _investButton = UiTheme.Action(Loc.T("shop.invest"));
        _investButton.Pressed += OnInvestPressed;
        _investRow.AddChild(_investButton);
        counter.AddChild(_investRow);
        return counter;
    }

    private static Label Wrapped(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<ShopOpenedEvent>(OnShopOpened);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<ShopOpenedEvent>(OnShopOpened);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    /// <summary>A load is another timeline: nothing sold in the abandoned one can be bought back.</summary>
    private void OnGameLoaded(GameLoadedEvent e)
    {
        ResetSession();
        MarkDirty();
    }

    /// <summary>Why the last press did nothing: a full pack on a purchase used to refund the gold and
    /// say nothing at all, which reads as the Buy button being broken. Shown in the order bar.</summary>
    private void SetFeedback(string text)
    {
        if (_feedback != text)
        {
            _feedback = text;
            _detailDirty = true;
        }
    }

    /// <summary>A press that was refused: the reason where the press was made, and the refusal cue.
    /// The cue outranks a button's own click, so one press makes one sound.</summary>
    private void Deny(string reason)
    {
        SetFeedback(reason);
        UiAudio.Play(UiCue.Denied);
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
        _selectedSide = Side.Wares;
        _junkArmed = false;
        _buyQuantity = 1;
        _sellQuantity = 1;
        _feedback = string.Empty;

        SetOpen(true);
        _focusSelection = true;

        // The same interact press that opened the shop is still "just pressed" this frame; swallow it
        // so the close-on-interact below does not fire immediately.
        _justOpened = true;
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => MarkDirty();

    /// <summary>The legend carries an entry only a pad has (scroll details).</summary>
    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

    public override void _Process(double delta)
    {
        if (IsOpen)
        {
            if (_justOpened)
            {
                _justOpened = false;
            }
            else if (Godot.Input.IsActionJustPressed(UiLive.Interact))
            {
                // A modal needs an easy out; the interact key both opens and closes it.
                Close();
                return;
            }
        }

        base._Process(delta);
        if (!IsOpen)
        {
            return;
        }

        // The panel focuses its first control when it opens; a counter starts on the inspected row.
        if (_focusSelection)
        {
            _focusSelection = false;
            if (_selectedRow != null && IsInstanceValid(_selectedRow))
            {
                _selectedRow.GrabFocus();
            }
        }

        // A selection that only moved within a list: the card and the order bar follow it, the
        // lists stay as they are.
        if (_detailDirty)
        {
            // A refused press sets only the reason, and the bar it is said in is rebuilt around the
            // pressed verb: focus goes back to the same place on the new bar.
            int[]? focusPath = UiFocus.PathOf(_order);
            RebuildTradeDetail();
            UiFocus.Restore(_order, focusPath);
        }

        TradeRow.StickScroll(_detailScroll, delta);
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open)
        {
            UiOrnament.PlayEmberWipe(_wipe);
        }
    }

    private void Close()
    {
        IEntity? player = _player;
        SetOpen(false);
        _player = null;
        _pack = null;
        _shop = null;
        _selectedTrade = null;

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
    private bool Buy(ShopResource shop, ShopOffer offer, int price)
    {
        if (_pack is not { } pack || ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return false;
        }

        if (!offer.Available ||
            LockFor(shop, offer, StandingWith(shop)) != StockLock.Open ||
            !ShopPricing.CanAfford(price, Purse()))
        {
            return false; // TryBuy has already said why; re-checked on the press
        }

        if (!pack.RemoveItem(GameIds.Currency.Gold, price))
        {
            return false; // the gold went somewhere between the rebuild and the press; deliver nothing
        }

        if (pack.AddInstance(offer.Instance, 1) <= 0)
        {
            pack.AddItem(gold, price); // pack full — hand the money straight back
            SetFeedback(Loc.T("shop.pack_full"));
            ItemTransfer.AnnouncePackFull(offer.Instance, 1);
            return false;
        }

        SetFeedback(string.Empty);

        // Paid for and delivered, so the sale stands either way; a false here would mean the shelf and
        // the window had drifted within one frame, which is worth a line in the log.
        if (Stock() is { } stock && !stock.TakeOne(shop, offer.Instance))
        {
            Log.Warn($"Shop '{shop.Id}': sold '{offer.Instance.TemplateId}' that the shelf no longer had.");
        }

        MarkDirty();
        return true;
    }

    /// <summary>
    /// The press on a ware: buys up to <paramref name="count"/> of it, one whole <see cref="Buy"/> at a
    /// time, and stops at the first that fails (the pack filled). A refused press says why in the
    /// order bar and plays the refusal cue; it is never a silent button.
    /// </summary>
    private void TryBuy(ShopResource shop, ShopOffer offer, int price, int count)
    {
        StockLock locked = LockFor(shop, offer, StandingWith(shop));
        TradeRules.BuyRefusal refusal = TradeRules.RefusalOf(
            locked == StockLock.Open, offer.Available, ShopPricing.CanAfford(price, Purse()));
        if (refusal != TradeRules.BuyRefusal.None)
        {
            Deny(BuyRefusalText(refusal, locked, offer, price));
            return;
        }

        int bought = 0;
        for (int i = 0; i < count; i++)
        {
            // Every unit after the first is its own copy: handing one instance to the pack twice
            // would leave two slots sharing a lock, a junk mark and an upgrade level.
            ShopOffer unit = i == 0 ? offer : offer with { Instance = ItemInstance.Plain(offer.Instance.Template) };
            if (!Buy(shop, unit, price))
            {
                break;
            }

            bought++;
        }

        UiAudio.Play(bought > 0 ? UiCue.Confirm : UiCue.Denied);
    }

    private string BuyRefusalText(TradeRules.BuyRefusal refusal, StockLock locked, ShopOffer offer, int price) => refusal switch
    {
        TradeRules.BuyRefusal.Locked => LockRefusal(locked, offer.Row!),
        TradeRules.BuyRefusal.SoldOut => Loc.T("shop.sold_out"),
        TradeRules.BuyRefusal.CannotAfford => Loc.TF("trade.need_more", TradeRules.Shortfall(price, Purse())),
        _ => string.Empty,
    };

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
        //
        // ⚠️ Only for units that are new to this counter. Whatever was bought back and is now being
        // sold again already moved the glut, the shortage and the faction once, and the buyback
        // undid none of them - so counting it again is the same cart paid for twice.
        int fresh = ItemPresentation.SpendCredit(BoughtBack, (shop.Id, instance.TemplateId), quantity);
        if (fresh > 0)
        {
            stock?.Absorb(shop, instance.TemplateId, fresh);
        }

        // The goods have reached the settlement (38T), so a shortage of their kind is that much nearer
        // broken. ⚠️ Here beside Absorb for exactly its reason: every early return above leaves the
        // player holding the item, and none of them may credit a haul that did not arrive. A sale to a
        // broker deliberately does NOT count — she is holding it for the player, not selling it here.
        if (fresh > 0 && shop.CellId.Length > 0)
        {
            Shocks()?.Deliver(shop.CellId, instance.Template.TagList(), fresh);
        }

        if (fresh > 0)
        {
            FenceStanding(shop, instance);
        }

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

        (string, string) credit = (shop.Id, entry.Instance.TemplateId);
        BoughtBack[credit] = BoughtBack.GetValueOrDefault(credit) + moved;

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
    /// consignment <em>is</em> — and it is why the whole-stack payout above needs no purse.
    ///
    /// ⚠️ Her one cap is the shelf itself: one unsold lot of a kind at a time
    /// (<see cref="ConsignmentRules.BlocksListing"/>). Checked here before the goods move.
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
            netPerUnit <= 0 ||
            ledger.Holds(shop.Id, instance.TemplateId, CurrentDay()))
        {
            return; // the button is already disabled and says why; re-checked on the press
        }

        // Read before the removal: Take decrements this very stack object, so afterwards it says 0
        // and a listing of nothing is no listing - the goods would simply be gone.
        int quantity = stack.Quantity;
        bool removed = ItemTransfer.Take(pack, stack, quantity);

        if (!removed)
        {
            Log.Warn($"Shop: could not remove '{instance.TemplateId}' to consign it; listed nothing.");
            return;
        }

        // Recorded only once the goods are actually gone, the ordering 38H and 38O both settled: every
        // early return above leaves the player still holding the item, and none of them may put an
        // entry on a shelf that never received it.
        ledger.Add(
            shop.Id, instance.TemplateId, quantity, netPerUnit, CurrentDay(), shop.ConsignDays);
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

        // Re-read on every rebuild: the UI scale can change mid-session, and offsets applied once
        // would keep a stale gutter until the game restarted.
        UiTheme.ApplyScreenInset(Shell);
        _detailScroll.CustomMinimumSize = new Vector2(TradeRules.DetailWidth(UiTheme.UsableWidth(Shell)), 0f);

        UiTheme.SetTradeTitle(_title, Loc.T(shop.NameKey));
        _purse.Text = Loc.TF("shop.price", Purse());
        BuildVendorPurse(shop);

        ReputationTier tier = StandingWith(shop);

        // Asked once and threaded into both sides, so the wares and the pack cannot disagree about
        // whether a deal was struck — the same reason `tier` is resolved here rather than per row.
        bool haggled = DealStruck(shop);

        BuildStanding(shop, tier);
        BuildLocalTrade(shop);
        BuildInvest(shop);
        BuildHaggle(shop, haggled);

        // What was inspected has gone (sold, bought out). If the press came from the order bar, focus
        // follows the selection to its row: left on the bar it would sit on a verb that now acts on
        // a different item, and a second press meant for the first would land on the second.
        if (EnsureSelection(shop) && GetViewport()?.GuiGetFocusOwner() is { } focus && _order.IsAncestorOf(focus))
        {
            _focusSelection = true;
        }
        _selectedRow = null;
        _selectedSlot = null;
        _selectedSlotItem = null;
        _waresRows.Clear();
        _packRows.Clear();
        BuildWares(shop, tier, haggled);
        BuildBuyback(shop);
        BuildPack(shop, haggled);
        RebuildTradeDetail();
    }

    /// <summary>
    /// The merchant's side of the title row. A merchant with a finite purse shows it, because a
    /// player dumping a field of loot has to be able to see why the last few rows stopped being
    /// sellable. ⚠️ A broker has none, and the readout says what she has instead (38P): a shelf.
    /// </summary>
    private void BuildVendorPurse(ShopResource shop)
    {
        if (shop.IsConsignment)
        {
            _vendorPurseOwner.Text = Loc.T("trade.purse.shelf");
            _vendorPurse.Text = Loc.TF("trade.purse.listed", Ledger()?.Pending ?? 0);
            return;
        }

        int purse = Stock()?.PurseFor(shop) ?? ShopStock.UnlimitedPurse;
        _vendorPurseOwner.Text = Loc.T("trade.purse.theirs");
        _vendorPurse.Text = purse < 0 ? Loc.T("trade.purse.deep") : Loc.TF("shop.price", purse);
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

    // --- Selection ----------------------------------------------------------

    private static IReadOnlyList<ShopOffer> Offers(ShopResource shop) =>
        Stock()?.OfferFor(shop) ?? System.Array.Empty<ShopOffer>();

    private bool IsSelected(Side side, ItemInstance instance)
    {
        if (_selectedSide != side || _selectedTrade is not { } selected)
        {
            return false;
        }

        if (ReferenceEquals(selected, instance))
        {
            return true;
        }

        // A ware on an authored row is a fresh copy on every listing, so it is matched by what it is.
        return side == Side.Wares && !selected.HasAffixes && !instance.HasAffixes &&
            selected.TemplateId == instance.TemplateId && selected.Rarity == instance.Rarity;
    }

    /// <summary>
    /// Makes a row the inspected one. Called when a row takes focus, so on a pad the card follows the
    /// cursor. Within one list only the detail column and the order bar are rebuilt and the selection
    /// frame moves in place; crossing to another list rebuilds, because the legend's verb changes.
    /// </summary>
    private void Select(Side side, ItemInstance instance, Button? slot, Button? row)
    {
        if (_focusSelection || IsSelected(side, instance))
        {
            return; // a rebuild restoring focus must not undo a selection made for it
        }

        if (_selectedSlot != null && IsInstanceValid(_selectedSlot))
        {
            ItemSlot.SetSelected(_selectedSlot, _selectedSlotItem, false);
        }

        // The legend names the accept verb by side and offers Compare only for a piece that can be worn.
        bool legendChanged = side != _selectedSide || _selectedTrade?.IsEquippable != instance.IsEquippable;
        _selectedSide = side;
        _selectedTrade = instance;
        _selectedSlot = slot;
        _selectedSlotItem = instance;
        _selectedRow = row;
        if (slot != null)
        {
            ItemSlot.SetSelected(slot, instance, true);
        }

        // A picker left at 30 for ore must not open at 30 for potions.
        _buyQuantity = 1;
        _sellQuantity = 1;
        _feedback = string.Empty;
        _detailScroll.ScrollVertical = 0; // a new card opens at its head
        _detailDirty = true;
        if (legendChanged)
        {
            MarkDirty();
        }
    }

    private ShopOffer? SelectedOffer(ShopResource shop)
    {
        foreach (ShopOffer offer in Offers(shop))
        {
            if (IsSelected(Side.Wares, offer.Instance))
            {
                return offer;
            }
        }

        return null;
    }

    private BuybackEntry? SelectedBuyback(ShopResource shop)
    {
        foreach (BuybackEntry entry in Buybacks)
        {
            if (entry.ShopId == shop.Id && IsSelected(Side.Buyback, entry.Instance))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the selection on something that is still there, and says whether it had to move it.
    /// What was sold out of the pack hands over to the next thing in the pack, never to a ware: the
    /// verb under the player's thumb must not turn from Sell into Buy. Anything else hands over to
    /// the first ware, then the first thing in the pack.
    /// </summary>
    private bool EnsureSelection(ShopResource shop)
    {
        bool found = _selectedTrade is { } item && _selectedSide switch
        {
            Side.Wares => SelectedOffer(shop) != null,
            Side.Buyback => SelectedBuyback(shop) != null,
            _ => HeldStack(item) != null,
        };
        if (found)
        {
            return false;
        }

        bool had = _selectedTrade != null;
        _buyQuantity = 1;
        _sellQuantity = 1;
        ItemInstance? firstHeld = null;
        if (_pack != null)
        {
            foreach (ItemStack stack in _pack.AllStacks)
            {
                firstHeld = stack.Instance;
                break;
            }
        }

        IReadOnlyList<ShopOffer> offers = Offers(shop);
        if (offers.Count > 0 && (_selectedSide != Side.Pack || firstHeld == null))
        {
            _selectedSide = Side.Wares;
            _selectedTrade = offers[0].Instance;
        }
        else
        {
            _selectedSide = Side.Pack;
            _selectedTrade = firstHeld;
        }

        return had;
    }

    // --- Lists --------------------------------------------------------------

    private const string NoteGap = "   ·   ";

    /// <summary>
    /// The buy-side quote for one unit of a ware. 38G: what the good is worth HERE, not in the realm at
    /// large; both sides of this counter spread over the same local value. 38U: the quote IS the price —
    /// the row shows <c>Total</c> and <see cref="Buy"/> charges it, which is the only way an explanation
    /// cannot drift from a bill.
    /// </summary>
    private PriceQuote BuyQuoteFor(ShopResource shop, ItemInstance instance, ReputationTier tier, bool haggled)
    {
        (int local, string localTag, bool shocked) = shop.LocalQuote(instance.Value, instance.Template.TagList());
        return PriceBreakdown.Buy(
            instance.Value, local, TagName(localTag), shocked,
            shop.BuyMarkup, tier, IsSpecialty(shop, instance), haggled, BuyPerkFactor());
    }

    private void BuildWares(ShopResource shop, ReputationTier tier, bool haggled)
    {
        UiTheme.ClearChildren(_waresList);

        // Naming the cadence is what lets a player tell "gone" from "gone forever" — a sold-out row
        // with no restock is a different thing from one that will be back tomorrow.
        _waresHeader.Text = Loc.T("shop.wares");
        _waresNote.Text = shop.RestockDays > 0 ? Loc.TF("shop.restocks", shop.RestockDays) : string.Empty;

        IReadOnlyList<ShopOffer> offers = Offers(shop);
        if (offers.Count == 0)
        {
            _waresList.AddChild(UiTheme.Body(Loc.T("shop.empty"), UiTheme.Dim));
            return;
        }

        int purse = Purse();
        foreach (ShopOffer offer in offers)
        {
            int price = BuyQuoteFor(shop, offer.Instance, tier, haggled).Total;

            // 38I: a gated row is shown, greyed, with the gate named — the same choice a sold-out row
            // makes, and for a stronger reason. A hidden row teaches nothing; a locked one is how the
            // player learns that standing and a stake buy something. A sold-out row stays on the shelf
            // too: removing it would read as the shop never having stocked the thing.
            StockLock locked = LockFor(shop, offer, tier);
            TradeRules.BuyRefusal refusal = TradeRules.RefusalOf(
                locked == StockLock.Open, offer.Available, ShopPricing.CanAfford(price, purse));

            var notes = new List<string>();
            if (IsSpecialty(shop, offer.Instance))
            {
                notes.Add(Loc.T("shop.specialty"));
            }

            if (refusal == TradeRules.BuyRefusal.Locked)
            {
                notes.Add(Loc.T("shop.locked"));
            }
            else if (refusal == TradeRules.BuyRefusal.SoldOut)
            {
                notes.Add(Loc.T("trade.sold_out"));
            }

            ShopOffer captured = offer;
            AddRow(
                _waresList, _waresRows, Side.Wares, offer.Instance,
                quantity: offer.Unlimited ? 1 : offer.Remaining,
                live: refusal == TradeRules.BuyRefusal.None,
                price: Loc.TF("shop.price", price),
                note: string.Join(NoteGap, notes),
                act: () => TryBuy(shop, captured, price, 1));
        }
    }

    /// <summary>What a pack stack would fetch at this counter, and why it might not.</summary>
    private readonly record struct PackState(
        PriceQuote Quote, int Payout, bool Enabled, bool Priced, string Refusal, bool Specialty, bool Glutted);

    /// <summary>
    /// The sell-side state of <paramref name="quantity"/> of a stack: one place asked by the row, the
    /// order bar's picker and the press itself, so they cannot disagree.
    ///
    /// Six refusals, each named separately: not for sale at all, not this merchant's trade, nothing an
    /// honest merchant will touch, worth nothing, locked by the player, or the merchant cannot cover
    /// it. Collapsing them would tell a player with a Legendary to try a cheaper shop when the real
    /// answer is to come back after a restock — and 38F's addition is the one that has somewhere to
    /// send them, so it names the trade. The lock comes after the merchant's own reasons: unlocking
    /// something she would not buy anyway is a wasted trip to the pack. A seventh, the broker's own:
    /// she already shows a lot of this kind, and takes the next when that one has sold
    /// (<see cref="ConsignmentRules.BlocksListing"/>).
    /// </summary>
    private PackState StateOf(ShopResource shop, ItemStack stack, bool haggled, int quantity)
    {
        ItemInstance instance = stack.Instance;
        bool sellable = ShopPricing.Sellable(instance.Type, IsCurrency(instance));
        bool inTrade = InTrade(shop, instance);
        bool kept = instance.Locked;

        // The merchant's own coin, when they have a finite amount of it. ⚠️ A broker has none (38P):
        // she fronts no money at all, so there is no purse to run down.
        int purse = shop.IsConsignment ? -1 : Stock()?.PurseFor(shop) ?? -1;
        int absorbed = shop.IsConsignment ? 0 : Stock()?.AbsorbedOf(shop, instance.TemplateId) ?? 0;

        // The quote itself, and the 38H / 38G / 38P / 38U reasoning behind each argument, is in
        // SellQuoteFor.
        PriceQuote quote = SellQuoteFor(shop, instance, quantity, haggled);
        bool priced = sellable && inTrade;
        int payout = priced ? quote.Total : 0;
        bool afforded = purse < 0 || payout <= purse;
        bool shelved = shop.IsConsignment &&
            (Ledger()?.Holds(shop.Id, instance.TemplateId, CurrentDay()) ?? false);

        string refusal = !sellable ? Loc.T("shop.unsellable")
            : !inTrade ? TradeRefusal(shop, instance)
            : payout <= 0 ? Loc.T("shop.worthless")
            : kept ? Loc.T("shop.locked_item")
            : shelved ? Loc.T("shop.consign_listed")
            : Loc.T("shop.vendor_broke");

        return new PackState(
            quote, payout,
            Enabled: priced && payout > 0 && afforded && !kept && !shelved,
            Priced: priced,
            Refusal: refusal,
            Specialty: IsSpecialty(shop, instance),
            Glutted: ShopStock.SaturationMultiplier(absorbed, shop.RestockDays) < 1f);
    }

    /// <summary>The broker's price names the wait as well as the money: an offer that is better than
    /// every counter in town and does not pay today is only a good deal if the player can see both
    /// halves of it before pressing.</summary>
    private static string PayoutText(ShopResource shop, int payout) => shop.IsConsignment
        ? Loc.TF("shop.consign_price", payout, shop.ConsignDays)
        : Loc.TF("shop.price", payout);

    private void BuildPack(ShopResource shop, bool haggled)
    {
        UiTheme.ClearChildren(_packList);
        _packHeader.Text = Loc.T("shop.your_pack");
        _packNote.Text = string.Empty;

        if (_pack is not { } pack)
        {
            return;
        }

        _packNote.Text = Loc.TF("storage.slots", pack.UsedSlots, pack.Capacity);

        // Snapshot: the row closures mutate this list, and a row built off a stack that has since
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
            PackState state = StateOf(shop, stack, haggled, stack.Quantity);

            // A price that moved must say why it moved. The line-by-line breakdown is in the detail
            // column; these are the markers a glance down the list owes.
            var notes = new List<string>();
            if (state.Specialty)
            {
                notes.Add(Loc.T("shop.specialty"));
            }

            if (state.Glutted)
            {
                notes.Add(Loc.T("shop.glutted"));
            }

            if (stack.Instance.Locked)
            {
                notes.Add(Loc.T("item.locked"));
            }

            ItemStack captured = stack;
            AddRow(
                _packList, _packRows, Side.Pack, stack.Instance, stack.Quantity,
                live: state.Enabled,

                // A refused row explains the refusal, not the arithmetic — quoting a payout nobody is
                // being offered is the "come back with more gold" mistake in another costume.
                price: state.Priced ? PayoutText(shop, state.Payout) : string.Empty,
                note: string.Join(NoteGap, notes),
                act: () => TrySell(shop, captured, captured.Quantity));
        }
    }

    /// <summary>
    /// One trade row, on the shared item vocabulary so an item looks the same here as in the pack
    /// (<see cref="TradeRow"/>). Taking focus selects it; accept is its verb. A refused row stays
    /// focusable and greyed, and the press on it says why — the 37 rule that every refusal names
    /// itself, and UI_STYLE §2's that <c>Disabled</c> always carries a second channel.
    /// </summary>
    private void AddRow(
        VBoxContainer list,
        List<Button> rows,
        Side side,
        ItemInstance instance,
        int quantity,
        bool live,
        string price,
        string note,
        System.Action act)
    {
        bool selected = IsSelected(side, instance) && _selectedRow == null;
        Button? slot = null;
        Button? input = null;
        TradeRow.Built row = TradeRow.Build(
            instance, quantity, selected, live, price, note, () => Select(side, instance, slot, input), act);
        slot = row.Slot;
        input = row.Input;
        if (selected)
        {
            _selectedRow = row.Input;
            _selectedSlot = row.Slot;
            _selectedSlotItem = instance;
        }

        list.AddChild(row.Card);
        rows.Add(row.Input);
    }

    /// <summary>The press on a pack row or the order bar's Sell: sells (or lists) that many, or says
    /// why not.</summary>
    private void TrySell(ShopResource shop, ItemStack stack, int quantity)
    {
        PackState state = StateOf(shop, stack, DealStruck(shop), quantity);
        if (!state.Enabled)
        {
            Deny(state.Refusal);
            return;
        }

        // Read before the sale: Take decrements this very stack object.
        int before = stack.Quantity;
        if (shop.IsConsignment)
        {
            Consign(shop, stack, state.Quote.Unit);
        }
        else
        {
            Sell(shop, stack, quantity, state.Payout);
        }

        if (stack.Quantity < before)
        {
            UiAudio.Play(UiCue.Confirm);
        }
        else
        {
            Deny(Loc.T("shop.vendor_broke"));
        }
    }

    /// <summary>
    /// The "sell all junk" line at the head of the pack column: how many stacks will go and for how
    /// much, before the press. Absent when nothing is marked. When something is marked and none of
    /// it can be sold here the line stays, saying so, and the button goes: a junk pile this merchant
    /// will not touch is worth knowing about at the counter rather than after walking away, but a
    /// button that can only refuse reads as one that works.
    ///
    /// The press asks first. The confirm names the total the merchant will pay, from the same plan
    /// the sale then runs, and Cancel is beside it. It is a second press and not a hold: the buyback
    /// shelf can still undo it.
    /// </summary>
    private void BuildJunkRow(ShopResource shop, bool haggled)
    {
        _junkTotalShown = 0;
        if (_pack is not { } pack || shop.IsConsignment || pack.JunkStacks().Count == 0)
        {
            _junkArmed = false;
            return;
        }

        List<(ItemStack Stack, int Payout)> plan = JunkPlan(shop, haggled, out int skipped);
        int total = 0;
        foreach ((ItemStack _, int payout) in plan)
        {
            total += payout;
        }

        bool armed = _junkArmed && plan.Count > 0;
        _junkArmed = armed;

        PanelContainer band = UiTheme.Compact(UiTheme.Band(armed ? UiTheme.Accent : UiTheme.IronLit));
        var copy = new VBoxContainer();
        copy.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        string line = armed ? Loc.TF("trade.junk_confirm", plan.Count, total)
            : plan.Count > 0 ? Loc.TF("shop.junk_preview", plan.Count, total)
            : Loc.T("shop.junk_none_sellable");
        copy.AddChild(Wrapped(UiTheme.Body(line, plan.Count > 0 ? UiTheme.Accent : UiTheme.Dim)));
        if (skipped > 0 && plan.Count > 0)
        {
            copy.AddChild(Wrapped(UiTheme.Caption(Loc.TF("shop.junk_skipped", skipped))));
        }

        if (plan.Count == 0)
        {
            band.AddChild(copy);
            _packList.AddChild(band);
            return;
        }

        HFlowContainer verbs = UiTheme.FlowRow();
        if (armed)
        {
            _junkTotalShown = total;
            Button confirm = UiTheme.Action(Loc.TF("trade.junk_confirm_verb", total), UiCue.Confirm);
            confirm.Pressed += () =>
            {
                _junkArmed = false;
                SellAllJunk(shop);
                MarkDirty();
            };
            verbs.AddChild(confirm);

            Button cancel = UiTheme.Action(Loc.T("item.cancel"), UiCue.Back);
            cancel.Pressed += () =>
            {
                _junkArmed = false;
                MarkDirty();
            };
            verbs.AddChild(cancel);
        }
        else
        {
            Button sell = UiTheme.Action(Loc.T("shop.sell_junk"));
            sell.TooltipText = Loc.T("shop.sell_junk_hint");
            sell.Pressed += () =>
            {
                _junkArmed = true;
                MarkDirty();
            };
            verbs.AddChild(sell);
        }

        copy.AddChild(verbs);
        band.AddChild(copy);
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
                _waresList, _waresRows, Side.Buyback, entry.Instance, entry.Quantity,
                live: ShopPricing.CanAfford(entry.Price, purse),
                price: Loc.TF("shop.price", entry.Price),
                note: string.Empty,
                act: () => TryBuyBack(shop, captured));
        }
    }

    private void TryBuyBack(ShopResource shop, BuybackEntry entry)
    {
        if (!ShopPricing.CanAfford(entry.Price, Purse()))
        {
            Deny(Loc.TF("trade.need_more", TradeRules.Shortfall(entry.Price, Purse())));
            return;
        }

        int before = Purse();
        BuyBack(shop, entry);
        UiAudio.Play(Purse() < before ? UiCue.Confirm : UiCue.Denied);
    }

    // --- The detail column and the order bar --------------------------------

    /// <summary>
    /// The inspected item: its card, compared against what the player is wearing, the reasons for
    /// its price, and under the lists the order bar with how many and the verbs. The comparison is
    /// the point of inspecting a ware at all: without it the question "is this better than mine"
    /// meant closing the shop, opening the pack and remembering two sets of numbers.
    /// </summary>
    private void RebuildTradeDetail()
    {
        _detailDirty = false;
        UiTheme.ClearChildren(_tradeDetail);
        UiTheme.ClearChildren(_order);
        if (_shop is not { } shop)
        {
            return;
        }

        if (_selectedTrade is not { } item)
        {
            _tradeDetail.AddChild(Wrapped(UiTheme.Caption(Loc.T("shop.inspect_hint"))));
            OrderNote(_feedback, bad: true);
            WireFocus();
            return;
        }

        _tradeDetail.AddChild(ItemSlot.Detail(item, new ItemSlot.DetailContext(
            _player?.GetComponent<EquipmentComponent>(),
            _player?.GetComponent<ProgressionComponent>()?.Level ?? 0,
            Compare: true)
        {
            Actions = new[] { new LegendEntry("ui_accept", AcceptVerb(shop)) },
        }));

        bool haggled = DealStruck(shop);
        if (_selectedSide == Side.Wares && SelectedOffer(shop) is { } offer)
        {
            BuildBuyOrder(shop, offer, haggled);
        }
        else if (_selectedSide == Side.Buyback && SelectedBuyback(shop) is { } entry)
        {
            BuildBuybackOrder(shop, entry);
        }
        else if (_selectedSide == Side.Pack && HeldStack(item) is { } stack)
        {
            BuildSellOrder(shop, stack, haggled);
        }
        else
        {
            OrderNote(_feedback, bad: true);
        }

        WireFocus();
    }

    /// <summary>
    /// Why the price is what it is, as lines under the card rather than only as a tooltip: a pad has
    /// no pointer to hover with. One line per reason with the running gold (38U), then the counter's
    /// spread, which is the answer to "why did she pay me a third of what she charges".
    /// </summary>
    private void AddPriceLines(PriceQuote quote, ShopResource shop)
    {
        VBoxContainer block = UiTheme.PriceLedger(quote);

        // A broker has no spread: she lists at a fraction and takes a cut, and both are rows above.
        if (!shop.IsConsignment)
        {
            (int asks, int pays) = TradeRules.Spread(shop.BuyMarkup, shop.SellFraction);
            block.AddChild(Wrapped(UiTheme.Caption(PriceTooltip.Spread(asks, pays))));
        }

        _tradeDetail.AddChild(block);
    }

    /// <summary>The order bar's words: why the last press did nothing, why this one would not, or
    /// what the bar is for. Takes the width the picker and the verbs leave.</summary>
    private Label OrderNote(string text, bool bad)
    {
        Label note = Wrapped(UiTheme.Caption(text, bad ? UiTheme.Bad : UiTheme.Dim));
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        note.CustomMinimumSize = new Vector2(OrderNoteMin, 0f);
        _order.AddChild(note);
        return note;
    }

    private const float OrderNoteMin = 96f;
    private const float OrderTotalMin = 72f;

    private Label OrderTotal()
    {
        Label total = UiTheme.Body(string.Empty, UiTheme.Accent);
        total.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        total.CustomMinimumSize = new Vector2(OrderTotalMin, 0f);
        total.HorizontalAlignment = HorizontalAlignment.Right;
        return total;
    }

    private Button OrderVerb(string text)
    {
        Button button = UiTheme.Action(text);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return button;
    }

    /// <summary>
    /// Buying: one, a chosen number, or as many as the purse, the shelf and a stack allow in one
    /// press. A ware the player cannot have keeps its Buy, greyed, with the reason beside it, and
    /// pressing it plays the refusal rather than nothing.
    /// </summary>
    private void BuildBuyOrder(ShopResource shop, ShopOffer offer, bool haggled)
    {
        ReputationTier tier = StandingWith(shop);
        PriceQuote quote = BuyQuoteFor(shop, offer.Instance, tier, haggled);
        int price = quote.Total;
        AddPriceLines(quote, shop);

        StockLock locked = LockFor(shop, offer, tier);
        TradeRules.BuyRefusal refusal = TradeRules.RefusalOf(
            locked == StockLock.Open, offer.Available, ShopPricing.CanAfford(price, Purse()));
        string reason = _feedback.Length > 0 ? _feedback : BuyRefusalText(refusal, locked, offer, price);
        OrderNote(reason.Length > 0 ? reason : Loc.T("trade.order.buy_hint"), bad: reason.Length > 0);

        // A rolled ware is one of a kind; an authored row sells as many as will go.
        int most = refusal != TradeRules.BuyRefusal.None ? 0
            : offer.Row is null ? 1
            : TradeRules.MaxBuy(
                price, Purse(), offer.Remaining,
                offer.Instance.IsStackable ? offer.Instance.MaxStack : TradeRules.MaxOrder);
        _buyQuantity = Mathf.Clamp(_buyQuantity, 1, Mathf.Max(1, most));

        Label total = OrderTotal();
        total.TooltipText = PriceTooltip.Render(quote);
        Button buy = OrderVerb(Loc.T("shop.buy"));
        if (refusal != TradeRules.BuyRefusal.None)
        {
            buy.AddThemeColorOverride("font_color", UiTheme.Disabled);
            total.AddThemeColorOverride("font_color", UiTheme.Disabled);
        }

        void Requote(int quantity)
        {
            _buyQuantity = quantity;
            total.Text = Loc.TF("shop.price", price * quantity);
            buy.Text = quantity > 1 ? Loc.TF("trade.buy_many", quantity) : Loc.T("shop.buy");
        }

        if (most > 1)
        {
            _order.AddChild(QuantityPicker.Build(1, most, _buyQuantity, Requote));
        }

        _order.AddChild(total);

        ShopOffer captured = offer;
        buy.Pressed += () => TryBuy(shop, captured, price, _buyQuantity);
        _order.AddChild(buy);

        if (most > 1)
        {
            Button max = OrderVerb(Loc.TF("trade.buy_max", most));
            max.Pressed += () => TryBuy(shop, captured, price, most);
            _order.AddChild(max);
        }

        Requote(_buyQuantity);
    }

    private void BuildBuybackOrder(ShopResource shop, BuybackEntry entry)
    {
        bool affordable = ShopPricing.CanAfford(entry.Price, Purse());
        string reason = _feedback.Length > 0 ? _feedback
            : affordable ? string.Empty
            : Loc.TF("trade.need_more", TradeRules.Shortfall(entry.Price, Purse()));
        OrderNote(reason.Length > 0 ? reason : Loc.T("shop.buyback_hint"), bad: reason.Length > 0);

        Label total = OrderTotal();
        total.Text = Loc.TF("shop.price", entry.Price);
        _order.AddChild(total);

        Button back = OrderVerb(Loc.T("shop.buy_back"));
        if (!affordable)
        {
            back.AddThemeColorOverride("font_color", UiTheme.Disabled);
        }

        back.Pressed += () => TryBuyBack(shop, entry);
        _order.AddChild(back);
    }

    /// <summary>
    /// Selling: the whole stack in one press, or part of it through the picker. The price beside the
    /// picker is requoted in place as it moves, from the same <see cref="StateOf"/> the press then
    /// charges - never by rebuilding, which would free the slider mid-drag. A quantity the merchant's
    /// purse cannot cover greys the verb and says why. A broker lists whole stacks, so she gets no
    /// picker; neither does a single item, a locked one, or anything this counter refuses outright.
    /// </summary>
    private void BuildSellOrder(ShopResource shop, ItemStack stack, bool haggled)
    {
        ItemInstance instance = stack.Instance;
        PackState whole = StateOf(shop, stack, haggled, stack.Quantity);
        if (whole.Priced)
        {
            AddPriceLines(whole.Quote, shop);
        }

        bool partial = stack.Quantity >= 2 && !shop.IsConsignment && !instance.Locked && whole.Priced;
        _sellQuantity = partial
            ? ItemPresentation.ClampQuantity(_sellQuantity, stack.Quantity, keepOne: false)
            : stack.Quantity;

        string verb = shop.IsConsignment ? Loc.T("shop.consign") : Loc.T("shop.sell");
        Label note = OrderNote(string.Empty, bad: false);
        Label total = OrderTotal();
        Button sell = OrderVerb(verb);

        void Requote(int quantity)
        {
            _sellQuantity = quantity;
            PackState state = StateOf(shop, stack, haggled, quantity);
            total.Text = state.Priced ? PayoutText(shop, state.Payout) : string.Empty;
            total.TooltipText = state.Priced ? PriceTooltip.Render(state.Quote) : string.Empty;
            sell.Text = partial && quantity > 1 ? Loc.TF("trade.sell_many", quantity) : verb;

            string why = _feedback.Length > 0 ? _feedback : state.Enabled ? string.Empty : state.Refusal;
            note.Text = why.Length > 0 ? why : Loc.T("trade.order.sell_hint");
            note.AddThemeColorOverride("font_color", why.Length > 0 ? UiTheme.Bad : UiTheme.Dim);
            sell.AddThemeColorOverride("font_color", state.Enabled ? UiTheme.Text : UiTheme.Disabled);
            total.AddThemeColorOverride("font_color", state.Enabled ? UiTheme.Accent : UiTheme.Disabled);
        }

        if (partial)
        {
            _order.AddChild(QuantityPicker.Build(1, stack.Quantity, _sellQuantity, quantity =>
            {
                _feedback = string.Empty; // the reason belonged to the amount just moved away from
                Requote(quantity);
            }));
        }

        _order.AddChild(total);

        sell.Pressed += () => TrySell(shop, stack, partial ? _sellQuantity : stack.Quantity);
        _order.AddChild(sell);

        if (partial)
        {
            Button all = OrderVerb(Loc.TF("trade.sell_all", stack.Quantity));
            all.Pressed += () => TrySell(shop, stack, stack.Quantity);
            _order.AddChild(all);
        }

        Requote(_sellQuantity);
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

    // --- Focus --------------------------------------------------------------

    /// <summary>
    /// The explicit neighbours a pad walks: up and down inside a list, right from the wares to the
    /// pack, and right again to the counter's own dealings or the order bar. Left from the first list
    /// goes round to the order bar: stepping there through the other list would select a row on the
    /// way and turn the bar into that row's order. The bar leads back to the row it acts on. Run
    /// after every rebuild of the lists or of the order bar, once the rows are in the tree.
    /// </summary>
    private void WireFocus()
    {
        Control? order = FirstFocusable(_order);
        Control? counter = _haggleRow.Visible && !_haggleButton.Disabled ? _haggleButton
            : _investRow.Visible && !_investButton.Disabled ? _investButton
            : order;
        Control? pack = _packRows.Count > 0 ? _packRows[0] : counter;
        Control? wares = _waresRows.Count > 0 ? _waresRows[0] : null;

        TradeRow.WireColumn(_waresRows, order, pack);
        TradeRow.WireColumn(_packRows, wares ?? order, counter);
        TradeRow.WireOrderBar(_order, _selectedRow);
    }

    private static Control? FirstFocusable(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Control { Visible: true } control)
            {
                if (control.FocusMode == Control.FocusModeEnum.All && control is not BaseButton { Disabled: true })
                {
                    return control;
                }

                if (FirstFocusable(control) is { } inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    // --- Capture hooks (src/Debugging/TradeShots.cs) ------------------------

    /// <summary>The total the "sell all junk" confirm is naming right now; zero when it is not showing.</summary>
    public int JunkConfirmTotal => _junkTotalShown;

    /// <summary>Whether the inspected item is one of the merchant's wares (as opposed to the pack's).</summary>
    public bool InspectingWare => _selectedTrade != null && _selectedSide == Side.Wares;

    /// <summary>Presses "Sell all junk" once, leaving the confirm that names the total on screen.</summary>
    public void ArmJunkSaleForCapture()
    {
        _junkArmed = true;
        MarkDirty();
    }

    /// <summary>
    /// Inspects the first ware (then the first pack item) that has something worn to be compared
    /// with, and shows the card's one-item or side-by-side view. False when nothing on either list
    /// has a worn rival.
    /// </summary>
    public bool CompareForCapture(bool sideBySide)
    {
        if (_shop is not { } shop || _player?.GetComponent<EquipmentComponent>() is not { } worn)
        {
            return false;
        }

        ItemInstance? pick = null;
        Side side = Side.Wares;
        foreach (ShopOffer offer in Offers(shop))
        {
            if (HasRival(worn, offer.Instance))
            {
                pick = offer.Instance;
                break;
            }
        }

        if (pick == null && _pack != null)
        {
            side = Side.Pack;
            foreach (ItemStack stack in _pack.Stacks)
            {
                if (HasRival(worn, stack.Instance))
                {
                    pick = stack.Instance;
                    break;
                }
            }
        }

        if (pick == null)
        {
            return false;
        }

        _selectedSide = side;
        _selectedTrade = pick;
        _focusSelection = true;
        ItemSlot.CompareOpen = sideBySide;
        MarkDirty();
        return true;
    }

    private static bool HasRival(EquipmentComponent worn, ItemInstance instance)
    {
        if (instance.Equippable is not { } gear || worn.IsInstanceEquipped(instance))
        {
            return false;
        }

        foreach (EquipmentSlot slot in ItemPresentation.RivalSlots(gear.Slot))
        {
            if (worn.GetEquipped(slot) != null)
            {
                return true;
            }
        }

        return false;
    }
}
