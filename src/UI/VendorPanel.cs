using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
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

        if (!offer.Available || !ShopPricing.CanAfford(price, Purse()))
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
        MarkDirty();
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
    /// </summary>
    private static string TradeRefusal(ShopResource shop)
    {
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
        BuildStanding(shop, tier);
        BuildWares(shop, tier);
        BuildPack(shop);
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

    private void BuildWares(ShopResource shop, ReputationTier tier)
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
            int price = ShopPricing.BuyPrice(
                offer.Instance.Value, ShopPricing.MarkupFor(shop.BuyMarkup, tier, specialty));
            bool affordable = ShopPricing.CanAfford(price, purse);

            // A sold-out row stays on the shelf, greyed. Removing it would read as the shop never
            // having stocked the thing, which is the opposite of what happened.
            ShopOffer captured = offer;
            AddRow(
                _waresList,
                offer.Instance,
                quantity: offer.Unlimited ? 1 : offer.Remaining,
                priceText: Loc.TF("shop.price", price),
                action: Loc.T("shop.buy"),
                enabled: offer.Available && affordable,
                refusal: offer.Available ? Loc.T("shop.cannot_afford") : Loc.T("shop.sold_out"),
                onPressed: () => Buy(shop, captured, price),
                specialty: specialty);
        }
    }

    private void BuildPack(ShopResource shop)
    {
        UiTheme.ClearChildren(_packList);

        if (_pack is not { } pack)
        {
            _packHeader.Text = Loc.T("shop.your_pack");
            return;
        }

        // The merchant's own coin, when they have a finite amount of it — a player dumping a field of
        // loot has to be able to see why the last few rows stopped being sellable.
        int purse = Stock()?.PurseFor(shop) ?? -1;
        string header = purse >= 0
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
            int payout = sellable && inTrade
                ? ShopPricing.SellPrice(
                    instance.Value, ShopPricing.SellFractionFor(shop.SellFraction, specialty))
                    * stack.Quantity
                : 0;

            // Four refusals, each named separately: not for sale at all, not this merchant's trade,
            // worth nothing, or the merchant cannot cover it. Collapsing them would tell a player with a
            // Legendary to try a cheaper shop when the real answer is to come back after a restock — and
            // 38F's addition is the one that has somewhere to send them, so it names the trade.
            bool afforded = purse < 0 || payout <= purse;
            string refusal = !sellable ? Loc.T("shop.unsellable")
                : !inTrade ? TradeRefusal(shop)
                : payout <= 0 ? Loc.T("shop.worthless")
                : Loc.T("shop.vendor_broke");

            ItemStack captured = stack;
            AddRow(
                _packList,
                instance,
                stack.Quantity,
                priceText: sellable && inTrade ? Loc.TF("shop.price", payout) : string.Empty,
                action: Loc.T("shop.sell"),
                enabled: sellable && inTrade && payout > 0 && afforded,
                refusal: refusal,
                onPressed: () => Sell(shop, captured, payout),
                specialty: specialty);
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
        bool specialty = false)
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
        if (specialty)
        {
            var trade = new HBoxContainer();
            trade.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            trade.AddChild(UiTheme.Chip(Loc.T("shop.specialty"), UiTheme.Accent));
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
