using Godot;

namespace Embervale.UI;

/// <summary>
/// Focus-navigation helpers (30.5J). Godot's built-in ui_* actions walk focusable controls;
/// what the engine does not do is decide where focus <b>starts</b> when a menu opens, or where
/// it goes when a panel's dirty-flag rebuild frees the focused row. These helpers do: grab the
/// first focusable control on open, and record/restore a child-index path across a rebuild so
/// a controller/keyboard user is never dropped into a focusless screen.
/// </summary>
public static class UiFocus
{
    /// <summary>Focuses the first visible, enabled, focusable control under
    /// <paramref name="root"/> (depth-first). Returns true if one was found.</summary>
    public static bool GrabFirst(Control root)
    {
        Control? target = FirstFocusable(root);
        target?.GrabFocus();
        return target != null;
    }

    /// <summary>The child-index path from <paramref name="root"/> down to the currently
    /// focused control, or null when focus is not inside <paramref name="root"/>. Capture it
    /// before a rebuild clears the tree.</summary>
    public static int[]? PathOf(Control root)
    {
        if (root.GetViewport()?.GuiGetFocusOwner() is not { } focus || !root.IsAncestorOf(focus))
        {
            return null;
        }

        var path = new System.Collections.Generic.List<int>();
        Node current = focus;
        while (current != root)
        {
            path.Insert(0, current.GetIndex());
            current = current.GetParent();
        }

        return path.ToArray();
    }

    /// <summary>Re-focuses after a rebuild: walk <paramref name="path"/> from
    /// <paramref name="root"/> as far as the new tree allows (clamping each index), then focus
    /// the nearest focusable at or after that point, falling back to the first in the panel.
    /// A null path is a no-op (focus wasn't ours to restore).</summary>
    public static void Restore(Control root, int[]? path)
    {
        if (path == null)
        {
            return;
        }

        Node node = root;
        foreach (int index in path)
        {
            int count = node.GetChildCount();
            if (count == 0)
            {
                break;
            }

            node = node.GetChild(System.Math.Clamp(index, 0, count - 1));
        }

        Control? target = node is Control landing ? FirstFocusable(landing) : null;
        target ??= FirstFocusable(root);
        target?.GrabFocus();
    }

    private static Control? FirstFocusable(Node node)
    {
        if (node is Control control)
        {
            if (!control.IsVisibleInTree())
            {
                return null;
            }

            if (control.FocusMode == Control.FocusModeEnum.All &&
                control is not BaseButton { Disabled: true })
            {
                return control;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            if (FirstFocusable(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
