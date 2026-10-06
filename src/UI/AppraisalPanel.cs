using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The appraiser's window (Phase 38P2), on the 30.5F <see cref="UiPanel"/> framework and event-driven
/// exactly like <see cref="StoragePanel"/> and <see cref="VendorPanel"/>: a
/// <see cref="ServiceComponent"/> of kind <see cref="ServiceKind.Appraise"/> publishes an
/// <see cref="AppraisalOpenedEvent"/>, this resolves the player's <see cref="InventoryComponent"/>
/// and lists what everything in the pack is worth and to whom.
///
/// <b>It is the first panel in the game that only reads.</b> Nothing here changes state — which is
/// why it is much shorter than the two panels it is shaped on: all of their length is the transfer
/// surface. A row takes focus so a pad can walk the list, and pressing it does nothing.
///
/// Each row leads with one number, the best price a counter will pay for one of the thing, and says
/// under the name who pays it.
///
/// ⚠️ <b>Every number comes from <see cref="EconomyReport"/>, which is the same code the arbitrage
/// table reads and which prices through the same <see cref="ShopPricing"/> calls
/// <see cref="VendorPanel"/> charges.</b> That chain is the whole point of the sub-phase: an
/// appraiser that computed its own prices would quote a number the merchant then refuses to pay, and
/// a valuation the game does not honour is worse than no valuation at all.
/// </summary>
public partial class AppraisalPanel : UiPanel
{
    private Label _title = null!;
    private Control _wipe = null!;
    private Label _appraiserLine = null!;
    private Label _header = null!;
    private VBoxContainer _list = null!;

    private InventoryComponent? _pack;
    private string _appraiser = string.Empty;
    private int _rows;

    protected override bool Dims => true;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_rows > 1)
            {
                entries.Add(new LegendEntry("ui_up", Loc.T("trade.legend.browse"), "ui_down"));
            }

            entries.AddRange(base.Legend);
            return entries;
        }
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyWorkspace(shell, 0.70f);

        VBoxContainer column = UiTheme.TradePage(
            shell, UiIcon.Kind.Currency, out _title, out HBoxContainer aside, out _wipe);
        UiTheme.SetTradeTitle(_title, Loc.T("appraisal.heading"));

        _appraiserLine = UiTheme.Caption(string.Empty);
        aside.AddChild(_appraiserLine);

        _header = UiTheme.Caption(string.Empty);
        _header.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_header);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        column.AddChild(scroll);
        _list = list;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<AppraisalOpenedEvent>(OnAppraisalOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<AppraisalOpenedEvent>(OnAppraisalOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open)
        {
            UiOrnament.PlayEmberWipe(_wipe);
        }
    }

    private void OnAppraisalOpened(AppraisalOpenedEvent e)
    {
        if (IsOpen || e.Player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        _pack = pack;
        _appraiser = e.AppraiserName;
        SetOpen(true);
    }

    /// <summary>Repriced while open, because standing and the pack both move underneath it — a
    /// valuation that went stale on the counter would be the drift this panel exists to prevent.</summary>
    private void OnInventoryChanged(InventoryChangedEvent e)
    {
        if (IsOpen)
        {
            MarkDirty();
        }
    }

    protected override void Rebuild()
    {
        _appraiserLine.Text = Loc.TF("appraisal.by", _appraiser);
        UiTheme.ClearChildren(_list);
        _rows = 0;

        if (_pack is not { } pack)
        {
            return;
        }

        // Dearest first: the player opened this to find out what is worth carrying across town, and
        // pack order is the order things were picked up in, which answers a different question.
        var stacks = new List<ItemStack>(pack.AllStacks);
        stacks.Sort((a, b) => b.Instance.Value.CompareTo(a.Instance.Value));

        foreach (ItemStack stack in stacks)
        {
            if (AddRow(stack))
            {
                _rows++;
            }
        }

        _header.Text = _rows > 0 ? Loc.T("appraisal.subtitle") : string.Empty;
        _header.Visible = _rows > 0;
        if (_rows == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("appraisal.nothing"), UiTheme.Dim));
        }
    }

    /// <summary>
    /// One valued item: the best outright buyer, and the broker's offer underneath when there is one.
    /// Returns false for anything with no market at all, which is skipped rather than listed as
    /// worthless — a row that says nothing is noise in a list the player is scanning.
    /// </summary>
    private bool AddRow(ItemStack stack)
    {
        ItemInstance instance = stack.Instance;
        if (!ShopPricing.Sellable(instance.Type, instance.TemplateId == GameIds.Currency.Gold))
        {
            return false;
        }

        List<string> tags = instance.Template.TagList();
        EconomyReport.BestBuyers(
            instance.Template, tags, out Offer best, out _,
            sellPerkFactor: PerkEffectMath.SellFactor(PerkQuery.Of(_pack?.Entity, PerkEffectKind.SellBonus)));
        ConsignQuote quote = EconomyReport.BestConsignment(instance.Template, tags);

        if (!best.Has && !quote.Has)
        {
            return false;
        }

        // Same vocabulary as a counter's row: a card spined in the item's rarity, the shared slot,
        // and the number on the right — so an item looks the same here as where it is sold.
        Color rarity = UiTheme.RarityColor(instance.Rarity);
        PanelContainer card = UiTheme.CardButton(
            rarity, out Button input, out VBoxContainer content, UiTheme.TradeRowStyle(rarity));
        input.TooltipText = instance.Template.Description;

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        Button slot = ItemSlot.Build(instance, stack.Quantity, selected: false, size: ItemSlot.RowSize);
        slot.FocusMode = Control.FocusModeEnum.None;
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(slot);

        var text = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label name = UiTheme.Body(instance.DisplayName, rarity);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(name);

        text.AddChild(Quiet(
            best.Has ? Loc.TF("appraisal.best_at", ShopName(best.Shop)) : Loc.T("appraisal.no_buyer"),
            best.Has ? UiTheme.Dim : UiTheme.Disabled));

        // The broker's line only appears when she would take it, so its presence is itself the answer
        // to "is this worth carrying to her" without the player comparing two numbers.
        if (quote.Has)
        {
            text.AddChild(Quiet(
                Loc.TF("appraisal.consign", ShopName(quote.Shop), quote.Net, quote.Days), UiTheme.Accent));
        }

        row.AddChild(text);

        // ⚠️ Quoted PER UNIT even for a stack of twenty, because that is the number that survives
        // contact with the counter: 38H's saturation drops the price as a stack crosses it, so a
        // multiplied total would be the one figure on screen the game genuinely will not honour.
        if (best.Has)
        {
            var worth = new VBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            worth.AddThemeConstantOverride("separation", 0);
            Label price = UiTheme.Header(Loc.TF("shop.price", best.Price));
            price.HorizontalAlignment = HorizontalAlignment.Right;
            worth.AddChild(price);
            Label unit = UiTheme.Caption(Loc.T("appraisal.each"));
            unit.MouseFilter = Control.MouseFilterEnum.Ignore;
            unit.HorizontalAlignment = HorizontalAlignment.Right;
            worth.AddChild(unit);
            row.AddChild(worth);
        }

        content.AddChild(row);
        _list.AddChild(card);
        return true;
    }

    private static Label Quiet(string text, Color color)
    {
        Label label = UiTheme.Caption(text, color);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    /// <summary>The merchant's own name rather than her id — the id is a debugging string and this is
    /// the one place in the game a shop is named without the player standing in front of it.</summary>
    private static string ShopName(string shopId) =>
        ShopDatabase.Get(shopId) is { } shop && shop.NameKey.Length > 0 ? Loc.T(shop.NameKey) : shopId;

    // --- Capture hooks (src/Debugging/TradeShots.cs) ------------------------

    /// <summary>Valued rows drawn on the last rebuild.</summary>
    public int ShownRowCount => _rows;
}
