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
/// </summary>
public partial class StoragePanel : UiPanel
{
    private Label _title = null!;
    private Label _packHeader = null!;
    private Label _storeHeader = null!;
    private VBoxContainer _packList = null!;
    private VBoxContainer _storeList = null!;

    private IEntity? _player;
    private InventoryComponent? _pack;
    private InventoryComponent? _storage;
    private string _storageName = string.Empty;
    private ItemRarity _minRarity = ItemRarity.Common;
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
        column.AddChild(new HSeparator());

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 500),
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
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
        EventBus.Instance?.Subscribe<StorageOpenedEvent>(OnStorageOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StorageOpenedEvent>(OnStorageOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
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
    /// Moves one stack between two inventories. The removal mode is the load-bearing part:
    /// <see cref="InventoryComponent.RemoveItem(string, int)"/> matches by <em>template id</em>
    /// across every stack, which is fine for stackables (they carry no affixes, so all copies are
    /// interchangeable) but wrong for a rolled item — two distinct affixed instances of the same
    /// template would see the first removal satisfy both, and one of them would evaporate. Rolled
    /// items go through the reference-based <see cref="InventoryComponent.RemoveOneInstance"/>
    /// instead. <c>ContainerLootComponent.Interact</c> now makes the same split — it drained
    /// everything, so its counts happened to cancel, but only by accident of popping the lot.
    /// </summary>
    private static void Transfer(InventoryComponent from, InventoryComponent to, ItemStack stack)
    {
        // AddInstance returns what actually fit — a full destination must never delete the
        // remainder, so only what landed is taken off the source.
        int moved = to.AddInstance(stack.Instance, stack.Quantity);
        if (moved <= 0)
        {
            return;
        }

        if (stack.Instance.IsStackable)
        {
            from.RemoveItem(stack.Instance.TemplateId, moved);
        }
        else
        {
            from.RemoveOneInstance(stack.Instance);
        }
    }

    private void Move(InventoryComponent? from, InventoryComponent? to, ItemStack stack)
    {
        if (from == null || to == null)
        {
            return;
        }

        Transfer(from, to, stack);
        MarkDirty(); // rebuild next frame (InventoryChangedEvent also flags it)
    }

    protected override void Rebuild()
    {
        _title.Text = Loc.TF("storage.title", _storageName);

        // Only the Store direction is gated (37D): a display stand asks for a minimum rarity, and
        // Take must always work or a stand could trap what it was given.
        ItemRarity floor = _minRarity;
        BuildSide(_packList, _packHeader, Loc.T("storage.your_pack"), _pack, Loc.T("storage.store"),
            stack => Move(_pack, _storage, stack), stack => stack.Instance.Rarity >= floor);
        BuildSide(_storeList, _storeHeader, Loc.T("storage.stored"), _storage, Loc.T("storage.take"),
            stack => Move(_storage, _pack, stack));
    }

    private static void BuildSide(
        VBoxContainer list,
        Label header,
        string label,
        InventoryComponent? inventory,
        string action,
        System.Action<ItemStack> onPressed,
        System.Func<ItemStack, bool>? accepts = null)
    {
        UiTheme.ClearChildren(list);

        if (inventory == null)
        {
            header.Text = label;
            return;
        }

        header.Text = $"{label}   {Loc.TF("storage.slots", inventory.UsedSlots, inventory.Capacity)}";

        if (inventory.UsedSlots == 0)
        {
            list.AddChild(UiTheme.Body(Loc.T("storage.empty"), UiTheme.Dim));
            return;
        }

        // Snapshot: the button closures mutate this list, and a row built off a stack that has
        // since been removed would transfer a ghost.
        foreach (ItemStack stack in new List<ItemStack>(inventory.Stacks))
        {
            AddRow(list, stack, action, onPressed, accepts?.Invoke(stack) ?? true);
        }
    }

    private static void AddRow(
        VBoxContainer list,
        ItemStack stack,
        string action,
        System.Action<ItemStack> onPressed,
        bool accepted)
    {
        ItemInstance instance = stack.Instance;
        string count = stack.Quantity > 1 ? $"  x{stack.Quantity}" : string.Empty;
        string rarity = instance.Rarity != ItemRarity.Common ? $"  [{instance.Rarity}]" : string.Empty;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label text = UiTheme.Body(
            $"{instance.DisplayName}{count}{rarity}", ItemRarities.Color(instance.Rarity));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        text.TooltipText = instance.Template.Description;
        row.AddChild(text);

        // Refused rows keep their button, greyed and explained. Hiding it would read as the row being
        // unmovable for no reason at all, which is the failure every 37 refusal is written to avoid.
        Button button = UiTheme.Action(action);
        button.Disabled = !accepted;
        button.TooltipText = accepted ? string.Empty : Loc.T("storage.too_plain");
        ItemStack captured = stack;
        button.Pressed += () => onPressed(captured);
        row.AddChild(button);

        list.AddChild(row);

        foreach (ItemAffix affix in instance.Affixes)
        {
            list.AddChild(UiTheme.Caption($"      {affix.DisplayValue}", UiTheme.Good));
        }
    }
}
