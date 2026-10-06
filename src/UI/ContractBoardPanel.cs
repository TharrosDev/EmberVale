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
    private Control _wipe = null!;
    private Label _rotation = null!;
    private Label _refusal = null!;
    private VBoxContainer _list = null!;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private string _board = string.Empty;
    private int _slots = 3;
    private int _rotationDays = 4;

    /// <summary>Postings drawn on the last rebuild, and how many of them are still open.</summary>
    private int _postings;
    private int _openPostings;

    /// <summary>Why the last Deliver press did nothing.</summary>
    private string _feedback = string.Empty;

    protected override bool Dims => true;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_openPostings > 0)
            {
                entries.Add(new LegendEntry("ui_accept", Loc.T("contracts.deliver")));
            }

            entries.AddRange(base.Legend);
            return entries;
        }
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyWorkspace(shell, 0.68f);

        // When the board turns over sits beside its name: it is the one deadline every posting shares.
        VBoxContainer column = UiTheme.TradePage(
            shell, UiIcon.Kind.Travel, out _title, out HBoxContainer aside, out _wipe);
        _rotation = UiTheme.Caption(string.Empty);
        aside.AddChild(_rotation);

        // Why the last Deliver was refused. Fixed above the list and never one of its children: a
        // line added at the head would shift every card by one, and focus is restored across a
        // rebuild by position - it would come back on the posting above the one that was pressed.
        _refusal = UiTheme.Caption(string.Empty, UiTheme.Bad);
        _refusal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _refusal.Visible = false;
        column.AddChild(_refusal);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        column.AddChild(scroll);
        _list = list;
    }

    protected override void OnOpenChanged(bool open)
    {
        _feedback = string.Empty;
        if (open)
        {
            UiOrnament.PlayEmberWipe(_wipe);
        }
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
        UiTheme.SetTradeTitle(_title, _board);
        UiTheme.ClearChildren(_list);
        _postings = 0;
        _openPostings = 0;

        int day = Day();
        int cycle = ContractRules.Cycle(day, _rotationDays);
        int pool = ContractDatabase.All.Count;

        int daysLeft = ContractRules.DaysLeft(day, _rotationDays);

        _refusal.Text = _feedback;
        _refusal.Visible = _feedback.Length > 0;

        // The postings come first: they are what the board is for, and their Deliver buttons are
        // what a pad lands on. The road news reads underneath.
        for (int slot = 0; slot < _slots; slot++)
        {
            int index = ContractRules.SlotContract(cycle, slot, pool);
            if (index < 0)
            {
                continue;
            }

            AddRow(ContractDatabase.All[index], cycle, daysLeft);
            _postings++;
        }

        if (_postings == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("contracts.none"), UiTheme.Dim));
            _rotation.Text = string.Empty;
        }
        else
        {
            _rotation.Text = Loc.TF("contracts.rotation", daysLeft);
        }

        AddNotices(day);
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

        _list.AddChild(UiTheme.SectionRule(Loc.T("contracts.notices"), first: _postings == 0));

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

            // A shortage and a windfall differ by icon as well as by colour.
            bool shortage = shock.Kind == ShockKind.Shortage;
            var notice = new HBoxContainer();
            notice.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
            TextureRect mark = UiIcon.Create(
                shortage ? UiIcon.Kind.Warning : UiIcon.Kind.Travel,
                UiTheme.BodyFontSize + 3f,
                shortage ? UiTheme.Bad : UiTheme.Good);
            mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            notice.AddChild(mark);

            Label words = UiTheme.Body(text);
            words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            notice.AddChild(words);
            _list.AddChild(notice);
        }
    }

    private static SupplyShockService? Shocks() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SupplyShockService service)
            ? service
            : null;

    /// <summary>
    /// One posting as a card: the goods wanted (their picture, how many, whose job it is), what the
    /// job risks as chips that say it in words, and on the right the one number the card is read
    /// for - the gold it pays - over its Deliver button.
    ///
    /// A filled card stays on the board, greyed, rather than disappearing — 38I's rule that a locked row
    /// teaches and a hidden one does not. A board that silently shrank as the player worked it would
    /// read as postings being withdrawn.
    /// </summary>
    private void AddRow(ContractResource contract, int cycle, int daysLeft)
    {
        ItemResource? item = ItemDatabase.Get(contract.ItemId);
        bool filled = Ledger()?.Filled(contract.Id, cycle) ?? false;
        int have = _pack?.CountOf(contract.ItemId) ?? 0;
        bool deliverable = !filled && item != null && have >= contract.Quantity;
        if (!filled)
        {
            _openPostings++;
        }

        PanelContainer card = UiTheme.Card(filled ? UiTheme.Disabled : deliverable ? UiTheme.Good : UiTheme.Accent);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        if (item != null)
        {
            Button slot = ItemSlot.Build(ItemInstance.Plain(item), contract.Quantity, selected: false, size: ItemSlot.RowSize);
            slot.FocusMode = Control.FocusModeEnum.None;
            slot.MouseFilter = Control.MouseFilterEnum.Ignore;
            slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(slot);
        }

        // Text stack on the left, the reward and its verb on the right, centred against the whole stack.
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        column.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label headline = UiTheme.Body(
            Loc.TF("contracts.wanted", contract.Quantity, item?.DisplayName ?? contract.ItemId),
            filled ? UiTheme.Disabled : UiTheme.Text);
        headline.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        headline.TooltipText = Loc.T(contract.NameKey);
        column.AddChild(headline);

        Label job = UiTheme.Caption(Loc.T(contract.NameKey), UiTheme.Dim);
        job.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(job);

        HFlowContainer chips = UiTheme.FlowRow();
        chips.AddChild(UiTheme.Chip(
            Loc.TF("contracts.carried", have, contract.Quantity), deliverable || filled ? UiTheme.Good : UiTheme.Dim));

        if (contract.ReputationDelta != 0 && contract.FactionId.Length > 0)
        {
            chips.AddChild(UiTheme.Chip(
                Loc.TF("contracts.reward_standing", contract.ReputationDelta, FactionName(contract.FactionId)),
                UiTheme.Accent));
        }

        // What the job asks the player to put up with, each as a chip with its own icon and words.
        TradeRules.ContractRisk risks = TradeRules.RisksOf(
            item != null && TradeTags.IsContraband(item.TagList()), daysLeft, have, contract.Quantity, filled);
        if ((risks & TradeRules.ContractRisk.Short) != 0)
        {
            chips.AddChild(UiTheme.IconChip(
                UiIcon.Kind.Warning, Loc.TF("contracts.risk.short", contract.Quantity - have), UiTheme.Bad));
        }

        if ((risks & TradeRules.ContractRisk.ClosingSoon) != 0)
        {
            chips.AddChild(UiTheme.IconChip(UiIcon.Kind.Moon, Loc.T("contracts.risk.closing"), UiTheme.Bad));
        }

        if ((risks & TradeRules.ContractRisk.Contraband) != 0)
        {
            chips.AddChild(UiTheme.IconChip(UiIcon.Kind.Lock, Loc.T("contracts.risk.contraband"), UiTheme.Bad));
        }

        column.AddChild(chips);
        row.AddChild(column);

        var pay = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        pay.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        pay.AddChild(UiTheme.HeroFact(
            contract.RewardGold.ToString(), Loc.T("contracts.reward_unit"), filled ? UiTheme.Disabled : UiTheme.Accent));

        if (filled)
        {
            PanelContainer chip = UiTheme.Chip(Loc.T("contracts.filled"), UiTheme.Disabled);
            chip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            pay.AddChild(chip);
        }
        else
        {
            // Pressable either way: a posting the player cannot fill yet says how much is missing
            // on its chip, and the press answers with the refusal rather than with nothing.
            Button deliver = UiTheme.Action(Loc.T("contracts.deliver"));
            if (!deliverable)
            {
                deliver.AddThemeColorOverride("font_color", UiTheme.Disabled);
            }

            ContractResource captured = contract;
            deliver.Pressed += () => Deliver(captured);
            pay.AddChild(deliver);
        }

        row.AddChild(pay);
        card.AddChild(row);
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
            ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            return;
        }

        int have = pack.CountOf(contract.ItemId);
        if (have < contract.Quantity || !pack.RemoveItem(contract.ItemId, contract.Quantity))
        {
            // Short, or the goods went somewhere between the draw and the press: pay nothing, say why.
            _feedback = Loc.TF("contracts.refused_short", Mathf.Max(1, contract.Quantity - have));
            UiAudio.Play(UiCue.Denied);
            MarkDirty();
            return;
        }

        _feedback = string.Empty;
        UiAudio.Play(UiCue.Confirm);

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

    // --- Capture hooks (src/Debugging/TradeShots.cs) ------------------------

    /// <summary>Postings drawn as cards on the last rebuild.</summary>
    public int PostingCount => _postings;
}
