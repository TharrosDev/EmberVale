using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Housing;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The stash window (Phase 37B), on the 30.5F <see cref="UiPanel"/> framework. Event-driven exactly
/// like <see cref="CraftingPanel"/>: a <see cref="PropertyStorageComponent"/> publishes a
/// <see cref="StorageOpenedEvent"/> on interact, this panel resolves the player's
/// <see cref="InventoryComponent"/> and shows the two side by side — pack on the left with Store
/// buttons, storage on the right with Take buttons.
///
/// This is the game's <b>first two-way container</b>. <c>ContainerLootComponent</c> only ever popped
/// its contents onto the floor as pickups, so none of the transfer surface below is a reuse.
///
/// Both sides share one sort, one category filter and one search, because the question a player
/// brings here is "where are my potions", and it is the same question about either column. A stack
/// moves whole from its row button, or in part from the row's "Some" picker; the two bulk buttons
/// are the trips made every visit - empty the materials into the chest, or empty the chest into
/// the pack.
/// </summary>
public partial class StoragePanel : UiPanel
{
    private Label _title = null!;
    private Label _packHeader = null!;
    private Label _storeHeader = null!;
    private Label _feedback = null!;
    private VBoxContainer _packList = null!;
    private VBoxContainer _storeList = null!;
    private HFlowContainer _tools = null!;
    private Button _depositMaterials = null!;
    private Button _takeAll = null!;

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

    /// <summary>The stack whose row is open on its quantity picker, and the amount picked.</summary>
    private ItemStack? _partial;
    private int _partialQuantity = 1;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyWorkspace(shell, 0.82f);

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);
        column.AddChild(UiTheme.Divider());

        // Sort and filter buttons: rebuilt with the rows, because which one is lit is state.
        _tools = UiTheme.FlowRow();
        column.AddChild(_tools);

        // The search field and the two bulk moves are built once. ⚠️ They come AFTER the sort row in
        // tree order on purpose: the panel focuses its first focusable control when it opens, and a
        // text field holding focus takes the gameplay keys out of the map - including the E that
        // closes this window.
        // The bulk buttons sit on the left, under the sort buttons, and the field on the right, so a
        // d-pad walking down the left edge to the lists never passes through a text field.
        var bulk = new HBoxContainer();
        bulk.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _depositMaterials = UiTheme.Action(Loc.T("storage.deposit_materials"));
        _depositMaterials.Pressed += DepositMaterials;
        bulk.AddChild(_depositMaterials);

        _takeAll = UiTheme.Action(Loc.T("storage.take_all"));
        _takeAll.Pressed += TakeAll;
        bulk.AddChild(_takeAll);

        bulk.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        Label searchCaption = UiTheme.Caption(Loc.T("item.search"));
        searchCaption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bulk.AddChild(searchCaption);

        var search = new LineEdit
        {
            PlaceholderText = Loc.T("item.search_placeholder"),
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(220f, UiTheme.ControlHeight),
        };
        UiTheme.ApplyType(search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        search.TextChanged += text =>
        {
            _query = text;
            MarkDirty();
        };
        search.FocusEntered += () => GameInput.SetTextEntry(true);
        search.FocusExited += () => GameInput.SetTextEntry(false);
        search.TextSubmitted += _ => search.ReleaseFocus();
        bulk.AddChild(search);
        column.AddChild(bulk);

        // Why the last move did not (wholly) happen. A full chest used to answer a Store press with
        // nothing at all.
        _feedback = UiTheme.Caption(string.Empty, UiTheme.Bad);
        _feedback.Visible = false;
        column.AddChild(_feedback);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        column.AddChild(columns);

        (_packHeader, _packList) = BuildColumn(columns);
        columns.AddChild(new VSeparator());
        (_storeHeader, _storeList) = BuildColumn(columns);
    }

    /// <summary>One titled scroll column. Both sides are the same shape, so they are built the
    /// same way rather than twice by hand.</summary>
    private static (Label Header, VBoxContainer List) BuildColumn(Node parent)
    {
        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
        _partial = null;
        SetFeedback(string.Empty);
        if (!open)
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

        SetOpen(true);

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
        _storage = null;

        if (player != null)
        {
            EventBus.Instance?.Publish(new StorageClosedEvent(player));
        }
    }

    /// <summary>
    /// Moves up to <paramref name="quantity"/> of one stack between the two inventories, through
    /// <see cref="ItemTransfer.Move"/>, which owns the two rules this window used to carry itself:
    /// only what the destination accepted leaves the source, and a rolled item is removed by
    /// reference rather than by template id.
    /// </summary>
    private void Move(InventoryComponent? from, InventoryComponent? to, ItemStack stack, int quantity)
    {
        if (from == null || to == null)
        {
            return;
        }

        int moved = ItemTransfer.Move(from, to, stack, quantity);
        Report(to, stack.Instance, quantity - moved);
        _partial = null;
        MarkDirty(); // rebuild next frame (InventoryChangedEvent also flags it)
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
        _feedback.Text = text;
        _feedback.Visible = text.Length > 0;
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

        _partial = null;
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

        _partial = null;
        MarkDirty();
    }

    protected override void Rebuild()
    {
        _title.Text = Loc.TF("storage.title", _storageName);
        BuildTools();

        // Only the Store direction is gated (37D): a display stand asks for a minimum rarity, and
        // Take must always work or a stand could trap what it was given.
        ItemRarity floor = _minRarity;
        BuildSide(_packList, _packHeader, Loc.T("storage.your_pack"), _pack, Loc.T("storage.store"),
            (stack, quantity) => Move(_pack, _storage, stack, quantity), stack => stack.Instance.Rarity >= floor);
        BuildSide(_storeList, _storeHeader, Loc.T("storage.stored"), _storage, Loc.T("storage.take"),
            (stack, quantity) => Move(_storage, _pack, stack, quantity));

        _depositMaterials.Disabled = !HasMaterials();
        _takeAll.Disabled = _storage == null || _storage.UsedSlots == 0;
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

    /// <summary>The sort buttons, then one filter button per category present on either side.</summary>
    private void BuildTools()
    {
        UiTheme.ClearChildren(_tools);

        Label sortCaption = UiTheme.Caption(Loc.T("item.sort"));
        sortCaption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _tools.AddChild(sortCaption);

        foreach ((ItemPresentation.SortOrder order, string key) in new[]
                 {
                     (ItemPresentation.SortOrder.Type, "item.sort_type"),
                     (ItemPresentation.SortOrder.Name, "item.sort_name"),
                     (ItemPresentation.SortOrder.Rarity, "item.sort_rarity"),
                     (ItemPresentation.SortOrder.Value, "item.sort_value"),
                 })
        {
            ItemPresentation.SortOrder captured = order;
            Button button = UiTheme.Action(Loc.T(key));
            Light(button, _sort == order);
            button.Pressed += () =>
            {
                _sort = captured;
                MarkDirty();
            };
            _tools.AddChild(button);
        }

        var present = new List<ItemType>();
        Collect(_pack, present);
        Collect(_storage, present);
        if (_filter is { } active && !present.Contains(active))
        {
            _filter = null; // the last of that category just moved out of view on both sides
        }

        if (present.Count < 2)
        {
            return;
        }

        _tools.AddChild(new VSeparator());
        Button all = UiTheme.Action(Loc.T("item.filter_all"));
        Light(all, _filter is null);
        all.Pressed += () =>
        {
            _filter = null;
            MarkDirty();
        };
        _tools.AddChild(all);

        present.Sort();
        foreach (ItemType type in present)
        {
            ItemType captured = type;
            Button button = UiTheme.Action(Loc.T(ItemSlot.TypeKey(type)));
            Light(button, _filter == type);
            button.Pressed += () =>
            {
                _filter = captured;
                MarkDirty();
            };
            _tools.AddChild(button);
        }
    }

    private static void Collect(InventoryComponent? inventory, List<ItemType> present)
    {
        if (inventory == null)
        {
            return;
        }

        foreach (ItemStack stack in inventory.AllStacks)
        {
            if (!present.Contains(stack.Instance.Type))
            {
                present.Add(stack.Instance.Type);
            }
        }
    }

    /// <summary>Marks the active choice in a row of buttons the way the character screen does.</summary>
    private static void Light(Button button, bool active)
    {
        if (active)
        {
            button.AddThemeColorOverride("font_color", UiTheme.Accent);
        }
    }

    private void BuildSide(
        VBoxContainer list,
        Label header,
        string label,
        InventoryComponent? inventory,
        string action,
        System.Action<ItemStack, int> onMove,
        System.Func<ItemStack, bool>? accepts = null)
    {
        UiTheme.ClearChildren(list);

        if (inventory == null)
        {
            header.Text = label;
            return;
        }

        header.Text = $"{label}   {Loc.TF("storage.slots", inventory.UsedSlots, inventory.Capacity)}";

        // Snapshot: the button closures mutate these lists, and a row built off a stack that has
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
            AddRow(list, stack, action, onMove, accepts?.Invoke(stack) ?? true);
        }
    }

    private void AddRow(
        VBoxContainer list,
        ItemStack stack,
        string action,
        System.Action<ItemStack, int> onMove,
        bool accepted)
    {
        ItemInstance instance = stack.Instance;

        // 37.5C: the same slot + card vocabulary the character screen uses, so moving an item
        // between the two windows does not feel like moving it between two games. The rarity read
        // now comes from the slot frame and the name colour rather than a "[Epic]" suffix.
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button slot = ItemSlot.Build(instance, stack.Quantity, selected: false, size: ItemSlot.RowSize);
        slot.FocusMode = Control.FocusModeEnum.None; // the action button is the row's focus target
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
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

        bool open = ReferenceEquals(_partial, stack) && accepted && stack.Quantity > 1;

        // Part of a stack: the row opens on a picker instead of growing a second window.
        if (stack.Quantity > 1 && !open)
        {
            Button some = UiTheme.Action(Loc.T("storage.some"));
            some.Disabled = !accepted;
            some.TooltipText = accepted ? Loc.T("storage.some_hint") : Loc.T("storage.too_plain");
            ItemStack target = stack;
            some.Pressed += () =>
            {
                _partial = target;
                _partialQuantity = ItemPresentation.ClampQuantity(target.Quantity / 2, target.Quantity, keepOne: false);
                MarkDirty();
            };
            some.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(some);
        }

        // Refused rows keep their button, greyed and explained. Hiding it would read as the row being
        // unmovable for no reason at all, which is the failure every 37 refusal is written to avoid.
        Button button = UiTheme.Action(action);
        button.Disabled = !accepted;
        button.TooltipText = accepted ? string.Empty : Loc.T("storage.too_plain");
        ItemStack captured = stack;
        button.Pressed += () => onMove(captured, open ? _partialQuantity : captured.Quantity);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        if (open)
        {
            _partialQuantity = ItemPresentation.ClampQuantity(_partialQuantity, stack.Quantity, keepOne: false);

            var pick = new HBoxContainer();
            pick.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
            HBoxContainer picker = QuantityPicker.Build(1, stack.Quantity, _partialQuantity, value => _partialQuantity = value);
            picker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            pick.AddChild(picker);
            pick.AddChild(button);

            Button cancel = UiTheme.Action(Loc.T("item.cancel"));
            cancel.Pressed += () =>
            {
                _partial = null;
                MarkDirty();
            };
            pick.AddChild(cancel);

            body.AddChild(row);
            body.AddChild(pick);
        }
        else
        {
            row.AddChild(button);
            body.AddChild(row);
        }

        card.AddChild(body);
        list.AddChild(card);
    }
}
