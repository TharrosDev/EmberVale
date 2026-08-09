using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The appraiser's window (Phase 38P2), on the 30.5F <see cref="UiPanel"/> framework and event-driven
/// exactly like <see cref="StoragePanel"/> and <see cref="VendorPanel"/>: a
/// <see cref="ServiceComponent"/> of kind <see cref="ServiceKind.Appraise"/> publishes an
/// <see cref="AppraisalOpenedEvent"/>, this resolves the player's <see cref="InventoryComponent"/>
/// and lists what everything in the pack is worth and to whom.
///
/// <b>It is the first panel in the game that only reads.</b> There is no button on a row and nothing
/// here changes state — which is why it is much shorter than the two panels it is shaped on: all of
/// their length is the transfer surface.
///
/// ⚠️ <b>Every number comes from <see cref="EconomyReport"/>, which is the same code the arbitrage
/// table reads and which prices through the same <see cref="ShopPricing"/> calls
/// <see cref="VendorPanel"/> charges.</b> That chain is the whole point of the sub-phase: an
/// appraiser that computed its own prices would quote a number the merchant then refuses to pay, and
/// a valuation the game does not honour is worse than no valuation at all.
///
/// ⚠️ <b>Not built on <see cref="VendorPanel"/>'s row.</b> That one is built around a press, its
/// enabled state and its refusal text; sharing it would mean a "no button" branch through every one
/// of its callers to save a dozen lines here.
/// </summary>
public partial class AppraisalPanel : UiPanel
{
    private Label _title = null!;
    private Label _header = null!;
    private VBoxContainer _list = null!;

    private InventoryComponent? _pack;
    private string _appraiser = string.Empty;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.OffsetLeft = -300;
        shell.OffsetRight = 300;
        shell.OffsetTop = 60;
        shell.GrowHorizontal = Control.GrowDirection.Both;

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);
        column.AddChild(new HSeparator());

        _header = UiTheme.Body(string.Empty, UiTheme.Dim);
        column.AddChild(_header);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0, 460);
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
        _title.Text = Loc.TF("appraisal.title", _appraiser);
        UiTheme.ClearChildren(_list);

        if (_pack is not { } pack)
        {
            return;
        }

        // Dearest first: the player opened this to find out what is worth carrying across town, and
        // pack order is the order things were picked up in, which answers a different question.
        var stacks = new List<ItemStack>(pack.Stacks);
        stacks.Sort((a, b) => b.Instance.Value.CompareTo(a.Instance.Value));

        int rows = 0;
        foreach (ItemStack stack in stacks)
        {
            if (AddRow(stack))
            {
                rows++;
            }
        }

        _header.Text = rows > 0 ? Loc.T("appraisal.subtitle") : string.Empty;
        if (rows == 0)
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
        EconomyReport.BestBuyers(instance.Template, tags, out Offer best, out _);
        ConsignQuote quote = EconomyReport.BestConsignment(instance.Template, tags);

        if (!best.Has && !quote.Has)
        {
            return false;
        }

        // Same vocabulary as the vendor window's row (37.5C): a Card spined in the item's rarity, an
        // ItemSlot, and the numbers to the right — so an item looks the same here as where it is sold.
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button slot = ItemSlot.Build(instance, stack.Quantity, selected: false, size: 30f);
        slot.FocusMode = Control.FocusModeEnum.None;
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

        // ⚠️ Quoted PER UNIT even for a stack of twenty, because that is the number that survives
        // contact with the counter: 38H's saturation drops the price as a stack crosses it, so a
        // multiplied total would be the one figure on screen the game genuinely will not honour.
        text.AddChild(UiTheme.Body(
            best.Has ? Loc.TF("appraisal.buyer", ShopName(best.Shop), best.Price) : Loc.T("appraisal.no_buyer"),
            best.Has ? UiTheme.Dim : UiTheme.Disabled));

        // The broker's line only appears when she would take it, so its presence is itself the answer
        // to "is this worth carrying to Mirelle" without the player comparing two numbers.
        if (quote.Has)
        {
            text.AddChild(UiTheme.Body(
                Loc.TF("appraisal.consign", ShopName(quote.Shop), quote.Net, quote.Days), UiTheme.Accent));
        }

        row.AddChild(text);
        card.AddChild(row);
        _list.AddChild(card);
        return true;
    }

    /// <summary>The merchant's own name rather than her id — the id is a debugging string and this is
    /// the one place in the game a shop is named without the player standing in front of it.</summary>
    private static string ShopName(string shopId) =>
        ShopDatabase.Get(shopId) is { } shop && shop.NameKey.Length > 0 ? Loc.T(shop.NameKey) : shopId;
}
