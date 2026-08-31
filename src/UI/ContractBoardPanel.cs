using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The caravan board (Phase 38Q2), on the 30.5F <see cref="UiPanel"/> framework and event-driven like
/// <see cref="AppraisalPanel"/>: a <see cref="ServiceComponent"/> of kind
/// <see cref="ServiceKind.Contracts"/> publishes a <see cref="ContractBoardOpenedEvent"/> and this
/// lists what the caravans want this rotation.
///
/// ⚠️ <b>NOTHING HERE TOUCHES THE QUEST LOG, AND THAT IS THE BRIEF RATHER THAN AN OMISSION.</b>
/// <c>QuestLogPanel</c> deliberately carries no Contracts heading — "the journal shows the states the
/// data actually has" — so a haulage job lives and dies on this board. There is no
/// <c>QuestResource</c>, no objective and no journal entry anywhere in the feature.
///
/// ⚠️ <b>The board is asked of the clock, never handed in.</b> The postings come from
/// <see cref="ContractRules.SlotContract"/> against the current day every rebuild, so a rotation that
/// turns while the window is open corrects itself, and nothing about the offer is saved or could go
/// stale. The only state read from the save is which postings have already been filled
/// (<see cref="ContractLedger"/>), which is also the only thing stopping one being filled twice.
/// </summary>
public partial class ContractBoardPanel : UiPanel
{
    private Label _title = null!;
    private Label _footer = null!;
    private VBoxContainer _list = null!;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private string _board = string.Empty;
    private int _slots = 3;
    private int _rotationDays = 4;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyWorkspace(shell, 0.68f);

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);
        column.AddChild(new HSeparator());

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0, 260);
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(scroll);
        _list = list;

        column.AddChild(new HSeparator());
        _footer = UiTheme.Body(string.Empty, UiTheme.Dim);
        column.AddChild(_footer);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<ContractBoardOpenedEvent>(OnBoardOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<ContractBoardOpenedEvent>(OnBoardOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    private void OnBoardOpened(ContractBoardOpenedEvent e)
    {
        if (IsOpen || e.Player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        _player = e.Player;
        _pack = pack;
        _board = e.BoardName;
        _slots = Mathf.Max(1, e.Slots);
        _rotationDays = Mathf.Max(1, e.RotationDays);
        SetOpen(true);
    }

    private void OnInventoryChanged(InventoryChangedEvent e)
    {
        if (IsOpen)
        {
            MarkDirty();
        }
    }

    protected override void Rebuild()
    {
        _title.Text = Loc.TF("contracts.title", _board);
        UiTheme.ClearChildren(_list);

        int day = Day();
        int cycle = ContractRules.Cycle(day, _rotationDays);
        int pool = ContractDatabase.All.Count;

        AddNotices(day);

        int rows = 0;
        for (int slot = 0; slot < _slots; slot++)
        {
            int index = ContractRules.SlotContract(cycle, slot, pool);
            if (index < 0)
            {
                continue;
            }

            AddRow(ContractDatabase.All[index], cycle);
            rows++;
        }

        if (rows == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("contracts.none"), UiTheme.Dim));
            _footer.Text = string.Empty;
            return;
        }

        _footer.Text = Loc.TF("contracts.rotation", ContractRules.DaysLeft(day, _rotationDays));
    }

    /// <summary>
    /// What the roads are saying (Phase 38T): every settlement whose trade is disturbed today, what has
    /// happened to it and for how much longer.
    ///
    /// ⚠️ <b>This board is the feature's only unprompted voice.</b> A shock is otherwise visible solely
    /// as a caption inside a vendor window the player has to already be standing in — which means the
    /// one thing a shock is *for*, going somewhere else, could only be discovered after arriving. Posted
    /// here beside the haulage contracts, where a player is already asking what is worth carrying where.
    ///
    /// The postings above are derived from the day and these are read from the save, and they sit on one
    /// board because the player has no reason to care which is which.
    /// </summary>
    private void AddNotices(int day)
    {
        if (Shocks() is not { } service)
        {
            return;
        }

        IReadOnlyList<SupplyShock> live = service.ActiveOn(day);
        if (live.Count == 0)
        {
            return;
        }

        _list.AddChild(UiTheme.Caption(Loc.T("contracts.notices"), UiTheme.Dim));

        foreach (SupplyShock shock in live)
        {
            string place = Loc.T($"cell.{shock.CellId}");
            string what = shock.Kind == ShockKind.Fair ? string.Empty : Loc.T($"trade.tag.{shock.Tag}");
            int left = shock.DaysLeft(day);

            string text = shock.Kind switch
            {
                ShockKind.Shortage => Loc.TF("contracts.notice_shortage", place, what, left),
                ShockKind.Glut => Loc.TF("contracts.notice_glut", place, what, left),
                _ => Loc.TF("contracts.notice_fair", place, left),
            };

            _list.AddChild(UiTheme.Body(
                text, shock.Kind == ShockKind.Shortage ? UiTheme.Bad : UiTheme.Good));
        }

        _list.AddChild(new HSeparator());
    }

    private static SupplyShockService? Shocks() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SupplyShockService service)
            ? service
            : null;

    /// <summary>
    /// One posting: what is wanted, how much of it the player is carrying, and what it pays.
    ///
    /// A filled row stays on the board, greyed, rather than disappearing — 38I's rule that a locked row
    /// teaches and a hidden one does not. A board that silently shrank as the player worked it would
    /// read as postings being withdrawn.
    /// </summary>
    private void AddRow(ContractResource contract, int cycle)
    {
        ItemResource? item = ItemDatabase.Get(contract.ItemId);
        bool filled = Ledger()?.Filled(contract.Id, cycle) ?? false;
        int have = _pack?.CountOf(contract.ItemId) ?? 0;
        bool deliverable = !filled && item != null && have >= contract.Quantity;

        PanelContainer card = UiTheme.Card(filled ? UiTheme.Disabled : deliverable ? UiTheme.Good : UiTheme.Accent);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label headline = UiTheme.Body(
            Loc.TF("contracts.wanted", contract.Quantity, item?.DisplayName ?? contract.ItemId),
            filled ? UiTheme.Disabled : UiTheme.Text);
        headline.TooltipText = Loc.T(contract.NameKey);
        headline.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headline.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(headline);

        if (filled)
        {
            PanelContainer chip = UiTheme.Chip(Loc.T("contracts.filled"), UiTheme.Disabled);
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(chip);
        }
        else
        {
            Button deliver = UiTheme.Action(Loc.T("contracts.deliver"));
            deliver.Disabled = !deliverable;
            ContractResource captured = contract;
            deliver.Pressed += () => Deliver(captured);
            deliver.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(deliver);
        }

        column.AddChild(titleRow);
        column.AddChild(UiTheme.Caption(Loc.T(contract.NameKey), UiTheme.Dim));

        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        chips.AddChild(UiTheme.Chip(
            $"{have}/{contract.Quantity}", deliverable || filled ? UiTheme.Good : UiTheme.Bad));
        chips.AddChild(UiTheme.Chip(Loc.TF("contracts.reward_gold", contract.RewardGold), UiTheme.Accent));

        if (contract.ReputationDelta != 0 && contract.FactionId.Length > 0)
        {
            chips.AddChild(UiTheme.Chip(
                Loc.TF("contracts.reward_standing", contract.ReputationDelta, FactionName(contract.FactionId)),
                UiTheme.Accent));
        }

        column.AddChild(chips);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(column);
        card.AddChild(pad);
        _list.AddChild(card);
    }

    /// <summary>
    /// Hands the goods over and takes the reward.
    ///
    /// ⚠️ <b>The goods leave first and the reward is only paid if they did</b> — <c>VendorPanel.Sell</c>'s
    /// ordering, and here it is what stops a failed <c>RemoveItem</c> minting gold. Every refusal is
    /// re-checked rather than trusted from the button: the pack can change between the rebuild that
    /// drew the row and the press that reaches this.
    ///
    /// The gold fits by construction — taking a stack of forty out of the pack frees at least as much
    /// room as one gold stack needs — but a short payment is logged rather than assumed, which is the
    /// lesson <see cref="ConsignmentLedger.Collect"/> and <c>ContrabandImpound.ReturnTo</c> both carry.
    /// </summary>
    private void Deliver(ContractResource contract)
    {
        int cycle = ContractRules.Cycle(Day(), _rotationDays);

        if (_pack is not { } pack || Ledger() is not { } ledger || ledger.Filled(contract.Id, cycle) ||
            ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold ||
            _pack.CountOf(contract.ItemId) < contract.Quantity)
        {
            return;
        }

        if (!pack.RemoveItem(contract.ItemId, contract.Quantity))
        {
            return; // the goods went somewhere between the draw and the press; pay nothing
        }

        if (contract.RewardGold > 0 && pack.AddItem(gold, contract.RewardGold) < contract.RewardGold)
        {
            Core.Diagnostics.Log.Warn(
                $"Contract '{contract.Id}': the pack could not take the whole {contract.RewardGold}g reward.");
        }

        if (contract.ReputationDelta != 0 && contract.FactionId.Length > 0)
        {
            _player?.GetComponent<ReputationComponent>()?.Add(contract.FactionId, contract.ReputationDelta);
        }

        ledger.MarkFilled(contract.Id, cycle);
        MarkDirty();
    }

    private static string FactionName(string factionId) =>
        FactionDatabase.Get(factionId) is { } faction && faction.DisplayName.Length > 0
            ? faction.DisplayName
            : factionId;

    private static int Day() => Resolve<WorldClock>()?.Day ?? 0;

    private static ContractLedger? Ledger() => Resolve<ContractLedger>();

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
