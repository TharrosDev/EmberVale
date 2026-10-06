using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Housing;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The stash window (Phase 37B), on the 30.5F <see cref="UiPanel"/> framework. Event-driven exactly
/// like <see cref="CraftingPanel"/>: a <see cref="PropertyStorageComponent"/> publishes a
/// <see cref="StorageOpenedEvent"/> on interact, this panel resolves the player's
/// <see cref="InventoryComponent"/> and shows the two side by side — pack on the left, storage in
/// the middle, and the inspected item's card on the right, the same three columns a counter uses.
///
/// This is the game's <b>first two-way container</b>. <c>ContainerLootComponent</c> only ever popped
/// its contents onto the floor as pickups, so none of the transfer surface below is a reuse.
///
/// Both sides share one sort, one category filter and one search, because the question a player
/// brings here is "where are my potions", and it is the same question about either column. A row is
/// its own verb: accept moves the whole stack across, and the order bar under the lists moves part
/// of it. The two bulk buttons are the trips made every visit - empty the materials into the chest,
/// or empty the chest into the pack.
/// </summary>
public partial class StoragePanel : UiPanel
{
    /// <summary>Which list the inspected stack is on.</summary>
    private enum Side
    {
        Pack,
        Stored,
    }

    private static readonly (ItemPresentation.SortOrder Order, string Key)[] Sorts =
    {
        (ItemPresentation.SortOrder.Type, "item.sort_type"),
        (ItemPresentation.SortOrder.Name, "item.sort_name"),
        (ItemPresentation.SortOrder.Rarity, "item.sort_rarity"),
        (ItemPresentation.SortOrder.Value, "item.sort_value"),
    };

    private static readonly ItemType[] Categories = System.Enum.GetValues<ItemType>();

    private Label _title = null!;
    private Control _wipe = null!;
    private Label _packHeader = null!;
    private Label _packNote = null!;
    private Label _storeHeader = null!;
    private Label _storeNote = null!;
    private VBoxContainer _packList = null!;
    private VBoxContainer _storeList = null!;
    private ScrollContainer _detailScroll = null!;
    private VBoxContainer _detail = null!;
    private HFlowContainer _order = null!;
    private Button _depositMaterials = null!;
    private Button _takeAll = null!;

    private readonly List<Button> _packRows = new();
    private readonly List<Button> _storeRows = new();

    private IEntity? _player;
    private InventoryComponent? _pack;
    private InventoryComponent? _storage;
    private string _storageName = string.Empty;
    private ItemRarity _minRarity = ItemRarity.Common;
    private bool _justOpened;

    /// <summary>Static so it survives the UI root being rebuilt between sessions; never saved.</summary>
    private static ItemPresentation.SortOrder _sort = ItemPresentation.SortOrder.Type;

    private ItemType? _filter;
    private string _query = string.Empty;

    private ItemStack? _selected;
    private Side _selectedSide;
    private Button? _selectedSlot;
    private Button? _selectedRow;
    private bool _focusSelection;
    private bool _detailDirty;

    /// <summary>How many of the selected stack the order bar's picker is set to.</summary>
    private int _partialQuantity = 1;

    /// <summary>Why the last move did not (wholly) happen. A full chest used to answer a Store press
    /// with nothing at all.</summary>
    private string _feedback = string.Empty;

    protected override bool Dims => true;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_selected is { } stack)
            {
                entries.Add(new LegendEntry("ui_accept", MoveVerb(_selectedSide)));
                if (stack.Instance.IsEquippable)
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

    private static string MoveVerb(Side side) => Loc.T(side == Side.Pack ? "storage.store" : "storage.take");

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        VBoxContainer column = UiTheme.TradePage(shell, UiIcon.Kind.Inventory, out _title, out _, out _wipe);

        // One wrapping row of tools. ⚠️ The search field is LAST in tree order on purpose: the panel
        // focuses its first focusable control when it opens, and a text field holding focus takes the
        // gameplay keys out of the map - including the E that closes this window.
        HFlowContainer tools = UiTheme.FlowRow();

        var sortNames = new string[Sorts.Length];
        int sortIndex = 0;
        for (int i = 0; i < Sorts.Length; i++)
        {
            sortNames[i] = Loc.TF("storage.sort_by", Loc.T(Sorts[i].Key));
            if (Sorts[i].Order == _sort)
            {
                sortIndex = i;
            }
        }

        OptionButton sort = UiTheme.Dropdown(sortNames, sortIndex);
        sort.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        sort.ItemSelected += index =>
        {
            _sort = Sorts[(int)index].Order;
            MarkDirty();
        };
        tools.AddChild(sort);

        var filterNames = new string[Categories.Length + 1];
        filterNames[0] = Loc.T("item.filter_all");
        for (int i = 0; i < Categories.Length; i++)
        {
            filterNames[i + 1] = Loc.T(ItemSlot.TypeKey(Categories[i]));
        }

        OptionButton filter = UiTheme.Dropdown(filterNames, 0);
        filter.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        filter.ItemSelected += index =>
        {
            _filter = index <= 0 ? null : Categories[(int)index - 1];
            MarkDirty();
        };
        tools.AddChild(filter);

        _depositMaterials = UiTheme.Action(Loc.T("storage.deposit_materials"), UiCue.Confirm);
        _depositMaterials.Pressed += DepositMaterials;
        tools.AddChild(_depositMaterials);

        _takeAll = UiTheme.Action(Loc.T("storage.take_all"), UiCue.Confirm);
        _takeAll.Pressed += TakeAll;
        tools.AddChild(_takeAll);

        var search = new LineEdit
        {
            PlaceholderText = Loc.T("item.search_placeholder"),
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(SearchWidth, UiTheme.ControlHeight),
        };
        UiSkin.Apply(search);
        UiTheme.ApplyType(search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        search.TextChanged += text =>
        {
            _query = text;
            MarkDirty();
        };
        search.FocusEntered += () => GameInput.SetTextEntry(true);
        search.FocusExited += () => GameInput.SetTextEntry(false);
        search.TextSubmitted += _ => search.ReleaseFocus();
        tools.AddChild(search);
        column.AddChild(tools);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        column.AddChild(columns);

        columns.AddChild(UiTheme.TradeColumn(0f, out _packHeader, out _packNote, out _packList));
        columns.AddChild(UiTheme.ColumnRule());
        columns.AddChild(UiTheme.TradeColumn(0f, out _storeHeader, out _storeNote, out _storeList));
        columns.AddChild(UiTheme.ColumnRule());

        (_detailScroll, _detail) = UiTheme.ScrollList();
        _detailScroll.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _detail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        columns.AddChild(_detailScroll);

        // How many, and the verbs: fixed under the lists, like a counter's order bar, and wrapping
        // like it when a handheld cannot hold them in one row.
        _order = UiTheme.FlowRow();
        _order.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        column.AddChild(_order);
    }

    private const float SearchWidth = 180f;
    private const float OrderNoteMin = 96f;

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<StorageOpenedEvent>(OnStorageOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StorageOpenedEvent>(OnStorageOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        GameInput.SetTextEntry(false);
    }

    protected override void OnOpenChanged(bool open)
    {
        _feedback = string.Empty;
        if (open)
        {
            UiOrnament.PlayEmberWipe(_wipe);
        }
        else
        {
            GameInput.SetTextEntry(false);
        }
    }

    private void OnStorageOpened(StorageOpenedEvent e)
    {
        // Ignore a second container while one is open.
        if (IsOpen)
        {
            return;
        }

        if (e.Player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        _player = e.Player;
        _pack = pack;
        _storage = e.Storage;
        _storageName = e.StorageName;
        _minRarity = e.MinRarity;
        _selected = null;
        _selectedSide = Side.Pack;
        _partialQuantity = 1;

        SetOpen(true);
        _focusSelection = true;

        // The same interact press that opened the chest is still "just pressed" this frame;
        // swallow it so the close-on-interact below doesn't fire immediately.
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
            else if (Godot.Input.IsActionJustPressed(UiLive.Interact) &&
                     GetViewport().GuiGetFocusOwner() is not LineEdit)
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

        // The panel focuses its first control when it opens (a tool); a chest starts on a stack.
        if (_focusSelection)
        {
            _focusSelection = false;
            if (_selectedRow != null && IsInstanceValid(_selectedRow))
            {
                _selectedRow.GrabFocus();
            }
        }

        if (_detailDirty)
        {
            RebuildDetail();
        }

        TradeRow.StickScroll(_detailScroll, delta);
    }

    private void Close()
    {
        IEntity? player = _player;
        SetOpen(false);
        _player = null;
        _pack = null;
        _storage = null;
        _selected = null;

        if (player != null)
        {
            EventBus.Instance?.Publish(new StorageClosedEvent(player));
        }
    }

    private InventoryComponent? Holder(Side side) => side == Side.Pack ? _pack : _storage;

    private InventoryComponent? Other(Side side) => side == Side.Pack ? _storage : _pack;

    /// <summary>Only the Store direction is gated (37D): a display stand asks for a minimum rarity,
    /// and Take must always work or a stand could trap what it was given.</summary>
    private bool Accepted(Side side, ItemStack stack) => side == Side.Stored || stack.Instance.Rarity >= _minRarity;

    /// <summary>
    /// Moves up to <paramref name="quantity"/> of one stack across, through
    /// <see cref="ItemTransfer.Move"/>, which owns the two rules this window used to carry itself:
    /// only what the destination accepted leaves the source, and a rolled item is removed by
    /// reference rather than by template id. A refused or empty move says why and plays the refusal.
    /// </summary>
    private void TryMove(Side side, ItemStack stack, int quantity)
    {
        if (Holder(side) is not { } from || Other(side) is not { } to)
        {
            return;
        }

        if (!Accepted(side, stack))
        {
            Deny(Loc.T("storage.too_plain"));
            return;
        }

        ItemInstance instance = stack.Instance;
        int moved = ItemTransfer.Move(from, to, stack, quantity);
        Report(to, instance, quantity - moved);
        UiAudio.Play(moved > 0 ? UiCue.Confirm : UiCue.Denied);
        MarkDirty(); // rebuild next frame (InventoryChangedEvent also flags it)
    }

    private void Deny(string reason)
    {
        SetFeedback(reason);
        UiAudio.Play(UiCue.Denied);
    }

    /// <summary>Says what did not fit, and where. Nothing left over clears the line.</summary>
    private void Report(InventoryComponent destination, ItemInstance instance, int leftOver)
    {
        if (leftOver <= 0)
        {
            SetFeedback(string.Empty);
            return;
        }

        if (ReferenceEquals(destination, _pack))
        {
            SetFeedback(Loc.T("storage.pack_full"));
            ItemTransfer.AnnouncePackFull(instance, leftOver);
        }
        else
        {
            SetFeedback(Loc.T("storage.full"));
        }
    }

    private void SetFeedback(string text)
    {
        if (_feedback != text)
        {
            _feedback = text;
            _detailDirty = true;
        }
    }

    /// <summary>
    /// Stores every crafting material the pack holds, material bag included. A locked stack stays:
    /// a lock means "this one stays with me", and a bulk button is exactly the kind of press it is
    /// there to survive. A stand that asks for a minimum rarity still gets only what it would take
    /// one at a time.
    /// </summary>
    private void DepositMaterials()
    {
        if (_pack is not { } pack || _storage is not { } storage)
        {
            return;
        }

        int leftOver = 0;
        ItemInstance? refused = null;
        foreach (ItemStack stack in new List<ItemStack>(pack.AllStacks))
        {
            ItemInstance instance = stack.Instance;
            if (instance.Type != ItemType.Material || instance.Locked || instance.Rarity < _minRarity)
            {
                continue;
            }

            int wanted = stack.Quantity;
            int moved = ItemTransfer.Move(pack, storage, stack, wanted);
            if (moved < wanted)
            {
                leftOver += wanted - moved;
                refused ??= instance;
            }
        }

        if (refused != null)
        {
            Report(storage, refused, leftOver);
        }
        else
        {
            SetFeedback(string.Empty);
        }

        MarkDirty();
    }

    /// <summary>Takes everything, in the order it is stored, until the pack is full. Whatever does
    /// not fit stays in the chest and is said so.</summary>
    private void TakeAll()
    {
        if (_pack is not { } pack || _storage is not { } storage)
        {
            return;
        }

        int leftOver = 0;
        ItemInstance? refused = null;
        foreach (ItemStack stack in new List<ItemStack>(storage.AllStacks))
        {
            int wanted = stack.Quantity;
            int moved = ItemTransfer.Move(storage, pack, stack, wanted);
            if (moved < wanted)
            {
                leftOver += wanted - moved;
                refused ??= stack.Instance;
            }
        }

        if (refused != null)
        {
            Report(pack, refused, leftOver);
        }
        else
        {
            SetFeedback(string.Empty);
        }

        MarkDirty();
    }

    protected override void Rebuild()
    {
        // Re-read on every rebuild: the UI scale can change mid-session.
        UiTheme.ApplyScreenInset(Shell);
        _detailScroll.CustomMinimumSize = new Vector2(TradeRules.DetailWidth(UiTheme.UsableWidth(Shell)), 0f);

        UiTheme.SetTradeTitle(_title, _storageName);

        // The inspected stack has gone across. If the press came from the order bar, focus follows
        // the selection to its row: left on the bar it would sit on a verb that now moves a
        // different stack.
        if (EnsureSelection() && GetViewport()?.GuiGetFocusOwner() is { } focus && _order.IsAncestorOf(focus))
        {
            _focusSelection = true;
        }
        _selectedRow = null;
        _selectedSlot = null;
        _packRows.Clear();
        _storeRows.Clear();
        BuildSide(Side.Pack, _packList, _packRows, _packHeader, _packNote, Loc.T("storage.your_pack"));
        BuildSide(Side.Stored, _storeList, _storeRows, _storeHeader, _storeNote, Loc.T("storage.stored"));

        _depositMaterials.Disabled = !HasMaterials();
        _takeAll.Disabled = _storage == null || _storage.UsedSlots == 0;
        RebuildDetail();
    }

    private bool HasMaterials()
    {
        if (_pack == null)
        {
            return false;
        }

        foreach (ItemStack stack in _pack.AllStacks)
        {
            if (stack.Instance.Type == ItemType.Material && !stack.Instance.Locked && stack.Instance.Rarity >= _minRarity)
            {
                return true;
            }
        }

        return false;
    }

    private bool Holds(Side side, ItemStack stack)
    {
        if (Holder(side) is not { } inventory)
        {
            return false;
        }

        foreach (ItemStack held in inventory.AllStacks)
        {
            if (ReferenceEquals(held, stack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Keeps the selection on a stack that is still where it was, and says whether it had
    /// to move it. One that went across hands over to the next stack on the side it left, so the
    /// verb under the player's thumb does not turn from Store into Take; then to the other side.</summary>
    private bool EnsureSelection()
    {
        if (_selected is { } current && Holds(_selectedSide, current))
        {
            return false;
        }

        bool had = _selected != null;
        _partialQuantity = 1;
        _selected = null;
        Side other = _selectedSide == Side.Pack ? Side.Stored : Side.Pack;
        foreach (Side side in new[] { _selectedSide, other })
        {
            if (Holder(side) is not { } inventory)
            {
                continue;
            }

            foreach (ItemStack stack in inventory.AllStacks)
            {
                _selected = stack;
                _selectedSide = side;
                return had;
            }
        }

        return had;
    }

    private void Select(Side side, ItemStack stack, Button? slot)
    {
        if (_focusSelection || (_selectedSide == side && ReferenceEquals(_selected, stack)))
        {
            return; // a rebuild restoring focus must not undo a selection made for it
        }

        if (_selectedSlot != null && IsInstanceValid(_selectedSlot))
        {
            ItemSlot.SetSelected(_selectedSlot, _selected?.Instance, false);
        }

        bool crossed = side != _selectedSide || _selected == null;
        _selectedSide = side;
        _selected = stack;
        _selectedSlot = slot;
        if (slot != null)
        {
            ItemSlot.SetSelected(slot, stack.Instance, true);
        }

        _partialQuantity = 1;
        _feedback = string.Empty;
        _detailDirty = true;
        if (crossed)
        {
            MarkDirty(); // the legend's verb changes with the side
        }
    }

    private void BuildSide(Side side, VBoxContainer list, List<Button> rows, Label header, Label note, string label)
    {
        UiTheme.ClearChildren(list);
        header.Text = label;
        note.Text = string.Empty;

        if (Holder(side) is not { } inventory)
        {
            return;
        }

        note.Text = Loc.TF("storage.slots", inventory.UsedSlots, inventory.Capacity);

        // Snapshot: the row closures mutate these lists, and a row built off a stack that has
        // since been removed would transfer a ghost. Pack then material bag, so a player's ore is
        // on the list even though it takes no slot.
        var held = new List<ItemStack>(inventory.AllStacks);
        if (held.Count == 0)
        {
            list.AddChild(UiTheme.Body(Loc.T("storage.empty"), UiTheme.Dim));
            return;
        }

        var shown = new List<ItemStack>();
        foreach (ItemStack stack in held)
        {
            ItemInstance instance = stack.Instance;
            if ((_filter is null || instance.Type == _filter) &&
                ItemPresentation.Matches(_query, instance.DisplayName, Loc.T(ItemSlot.TypeKey(instance.Type))))
            {
                shown.Add(stack);
            }
        }

        if (shown.Count == 0)
        {
            list.AddChild(UiTheme.Body(Loc.T("item.no_match"), UiTheme.Dim));
            return;
        }

        foreach (ItemStack stack in ItemPresentation.Sort(shown, _sort, st => ItemPresentation.KeyOf(st.Instance)))
        {
            AddRow(side, list, rows, stack);
        }
    }

    /// <summary>One stack on the shared trade row. A refused row (too plain for a display stand)
    /// stays, greyed, and its press says why: hiding it would read as the row being unmovable for
    /// no reason at all, which is the failure every 37 refusal is written to avoid.</summary>
    private void AddRow(Side side, VBoxContainer list, List<Button> rows, ItemStack stack)
    {
        ItemInstance instance = stack.Instance;
        bool accepted = Accepted(side, stack);
        bool selected = _selectedSide == side && ReferenceEquals(_selected, stack);

        Button? slot = null;
        ItemStack captured = stack;
        TradeRow.Built row = TradeRow.Build(
            instance, stack.Quantity, selected, accepted,
            price: string.Empty,
            note: accepted ? string.Empty : Loc.T("storage.too_plain"),
            onSelect: () => Select(side, captured, slot),
            onAct: () => TryMove(side, captured, captured.Quantity));
        slot = row.Slot;
        if (selected)
        {
            _selectedRow = row.Input;
            _selectedSlot = row.Slot;
        }

        list.AddChild(row.Card);
        rows.Add(row.Input);
    }

    /// <summary>The inspected stack's card, compared against what the player is wearing, and under
    /// the lists the order bar: part of the stack through the picker, or all of it.</summary>
    private void RebuildDetail()
    {
        _detailDirty = false;
        UiTheme.ClearChildren(_detail);
        UiTheme.ClearChildren(_order);

        Label note = UiTheme.Caption(string.Empty);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        note.CustomMinimumSize = new Vector2(OrderNoteMin, 0f);
        _order.AddChild(note);

        if (_selected is not { } stack)
        {
            note.Text = _feedback;
            note.AddThemeColorOverride("font_color", UiTheme.Bad);
            WireFocus();
            return;
        }

        Side side = _selectedSide;
        string verb = MoveVerb(side);
        _detail.AddChild(ItemSlot.Detail(stack.Instance, new ItemSlot.DetailContext(
            _player?.GetComponent<EquipmentComponent>(),
            _player?.GetComponent<ProgressionComponent>()?.Level ?? 0,
            Compare: true)
        {
            Actions = new[] { new LegendEntry("ui_accept", verb) },
        }));

        bool accepted = Accepted(side, stack);
        string why = _feedback.Length > 0 ? _feedback : accepted ? string.Empty : Loc.T("storage.too_plain");
        note.Text = why.Length > 0 ? why : Loc.T("storage.order_hint");
        note.AddThemeColorOverride("font_color", why.Length > 0 ? UiTheme.Bad : UiTheme.Dim);

        bool partial = accepted && stack.Quantity > 1;
        _partialQuantity = partial
            ? ItemPresentation.ClampQuantity(_partialQuantity, stack.Quantity, keepOne: false)
            : stack.Quantity;

        Button move = UiTheme.Action(verb);
        move.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (!accepted)
        {
            move.AddThemeColorOverride("font_color", UiTheme.Disabled);
        }

        void Requote(int quantity)
        {
            _partialQuantity = quantity;
            move.Text = partial ? Loc.TF("storage.move_many", verb, quantity) : verb;
        }

        if (partial)
        {
            // The picker reports and this row keeps the number: a rebuild here would free the
            // slider mid-drag.
            _order.AddChild(QuantityPicker.Build(1, stack.Quantity, _partialQuantity, Requote));
        }

        move.Pressed += () => TryMove(side, stack, partial ? _partialQuantity : stack.Quantity);
        _order.AddChild(move);

        if (partial)
        {
            Button all = UiTheme.Action(Loc.TF("storage.move_all", verb, stack.Quantity));
            all.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            all.Pressed += () => TryMove(side, stack, stack.Quantity);
            _order.AddChild(all);
        }

        Requote(_partialQuantity);
        WireFocus();
    }

    /// <summary>Up and down inside a list, left and right between the two, and right from the
    /// stored column to the order bar. Run once the rows are in the tree.</summary>
    private void WireFocus()
    {
        Control? order = null;
        foreach (Node child in _order.GetChildren())
        {
            if (child is Button button)
            {
                order = button;
                break;
            }
        }

        Control? pack = _packRows.Count > 0 ? _packRows[0] : null;
        Control? store = _storeRows.Count > 0 ? _storeRows[0] : order;
        TradeRow.WireColumn(_packRows, null, store);
        TradeRow.WireColumn(_storeRows, pack, order);
    }

    // --- Capture hooks (src/Debugging/TradeShots.cs) ------------------------

    /// <summary>Rows drawn across both lists on the last rebuild.</summary>
    public int ShownRowCount => _packRows.Count + _storeRows.Count;
}
