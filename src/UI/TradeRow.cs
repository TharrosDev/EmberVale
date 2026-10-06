using System;
using Embervale.Items;
using Godot;

namespace Embervale.UI;

/// <summary>
/// One item row of a trade page (a ware, a pack stack, a stored stack): the shared slot, the name,
/// a quiet second line and the price, on one focusable card. The vendor and the stash build their
/// lists from it so an item reads the same at a counter as in a chest.
///
/// The row is its own verb. Focus selects it, which is what the detail column follows; accept
/// (<c>ui_accept</c>) acts on it. A mouse click only selects and a double click acts, so reaching
/// for the detail column's buttons with the pointer never buys what was clicked on the way.
/// </summary>
public static class TradeRow
{
    /// <summary>What a built row hands back: the card to add to a list, the button that takes
    /// focus, and the slot whose selection frame the screen moves.</summary>
    public readonly record struct Built(PanelContainer Card, Button Input, Button Slot);

    /// <param name="live">False draws the name and price greyed: the row's action would be refused.
    /// It stays focusable, because the refusal is said in the detail column.</param>
    /// <param name="note">A second line under the name (their trade, kept back, plenty already), or empty.</param>
    public static Built Build(
        ItemInstance instance,
        int quantity,
        bool selected,
        bool live,
        string price,
        string note,
        Action onSelect,
        Action onAct)
    {
        Color rarity = UiTheme.RarityColor(instance.Rarity);
        PanelContainer card = UiTheme.CardButton(
            rarity, out Button input, out VBoxContainer content, UiTheme.TradeRowStyle(rarity));

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // The slot is the picture, not a second focus stop: the card's own button is the row.
        Button slot = ItemSlot.Build(instance, quantity, selected, ItemSlot.CompactSize);
        slot.FocusMode = Control.FocusModeEnum.None;
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(slot);

        var text = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", 0);

        Label name = UiTheme.Body(instance.DisplayName, live ? rarity : UiTheme.Disabled);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(name);

        if (note.Length > 0)
        {
            Label second = UiTheme.Caption(note);
            second.MouseFilter = Control.MouseFilterEnum.Ignore;
            second.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.AddChild(second);
        }

        row.AddChild(text);

        if (price.Length > 0)
        {
            Label cost = UiTheme.Body(price, live ? UiTheme.Accent : UiTheme.Disabled);
            cost.MouseFilter = Control.MouseFilterEnum.Ignore;
            cost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(cost);
        }

        content.AddChild(row);
        input.TooltipText = instance.DisplayName;
        input.FocusEntered += onSelect;

        // A press that came from the pointer selects; one that came from accept acts. The pointer is
        // seen either as an event on this button or, for a click on the card's outer band (which
        // CardButton forwards as a press), as the button still being down.
        bool pointer = false;
        input.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
            {
                return;
            }

            if (!click.Pressed)
            {
                // The button raises Pressed while it handles this release, after this signal: the
                // mark is cleared once that is over, so a press dragged off the row leaves nothing behind.
                Callable.From(() => { pointer = false; }).CallDeferred();
                return;
            }

            pointer = true;
            if (click.DoubleClick)
            {
                onAct();
            }
        };
        input.Pressed += () =>
        {
            if (pointer || Godot.Input.IsMouseButtonPressed(MouseButton.Left))
            {
                pointer = false;
                onSelect();
                return;
            }

            onAct();
        };

        return new Built(card, input, slot);
    }

    /// <summary>Pixels a second the right stick scrolls a detail column at full tilt.</summary>
    private const float StickScrollSpeed = 600f;

    /// <summary>
    /// Scrolls a column that holds nothing to focus (an item's card) with the right stick, the way
    /// the dialogue page scrolls. Call it each frame while the screen is open.
    /// </summary>
    public static void StickScroll(ScrollContainer scroll, double delta)
    {
        float stick = Godot.Input.GetActionStrength(UiLive.LookDown) - Godot.Input.GetActionStrength(UiLive.LookUp);
        if (Mathf.Abs(stick) > 0.2f)
        {
            scroll.ScrollVertical += Mathf.RoundToInt(stick * StickScrollSpeed * (float)delta);
        }
    }

    /// <summary>
    /// Wires a column of rows for a pad: up and down walk the column, and left and right step to
    /// the columns beside it (or stay put when there is none). Call it after the rows are in the
    /// tree: <c>FocusNeighbor*</c> takes a path, and a detached node has none.
    /// </summary>
    public static void WireColumn(System.Collections.Generic.IReadOnlyList<Button> rows, Control? left, Control? right)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            Button row = rows[i];
            if (!row.IsInsideTree())
            {
                continue;
            }

            NodePath self = row.GetPath();
            row.FocusNeighborLeft = left != null && left.IsInsideTree() ? left.GetPath() : self;
            row.FocusNeighborRight = right != null && right.IsInsideTree() ? right.GetPath() : self;
            if (i > 0 && rows[i - 1].IsInsideTree())
            {
                row.FocusNeighborTop = rows[i - 1].GetPath();
            }

            if (i + 1 < rows.Count && rows[i + 1].IsInsideTree())
            {
                row.FocusNeighborBottom = rows[i + 1].GetPath();
            }
        }
    }
}
