using Embervale.Items;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The marks an item slot draws over its picture, in one node and one draw call each:
/// rarity ticks along the bottom edge (the count is the rarity, for when the frame colours cannot
/// be told apart), a diamond pip for an item that is new since the pack was last opened, a lit left
/// edge for something worn, and a strike across a slot marked as junk. Every one of them is a
/// shape, with a dark keyline under it so it reads over painted art.
///
/// It never processes; it redraws only when <see cref="ClearNew"/> changes what it shows.
/// </summary>
public sealed partial class ItemSlotOverlay : Control
{
    private readonly ItemRarity _rarity;
    private readonly bool _equipped;
    private readonly bool _junk;
    private readonly float _pipLeft;
    private bool _isNew;

    /// <param name="pipLeft">Where the new pip starts from the left edge: past the lock or junk
    /// badge when the slot has one.</param>
    public ItemSlotOverlay(ItemRarity rarity, bool isNew, bool equipped, bool junk, float pipLeft)
    {
        _rarity = rarity;
        _isNew = isNew;
        _equipped = equipped;
        _junk = junk;
        _pipLeft = pipLeft;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Whether the new pip is showing.</summary>
    public bool IsNew => _isNew;

    /// <summary>Takes the new pip off, once the player has looked at the item.</summary>
    public void ClearNew()
    {
        if (_isNew)
        {
            _isNew = false;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float size = Mathf.Min(Size.X, Size.Y);
        float inset = Mathf.Max(3f, size * 0.07f);

        if (_junk)
        {
            // Corner to corner, bottom-left to top-right: a struck slot, whatever is painted in it.
            Vector2 from = new(inset, Size.Y - inset);
            Vector2 to = new(Size.X - inset, inset);
            DrawLine(from, to, UiTheme.Keyline, 4f, true);
            DrawLine(from, to, UiTheme.Dim, 1.5f, true);
        }

        if (_equipped)
        {
            DrawRect(new Rect2(1f, 1f, 3f, Size.Y - 2f), UiTheme.Keyline);
            DrawRect(new Rect2(1f, 2f, 2f, Size.Y - 4f), UiTheme.RuleLit);
        }

        int ticks = ItemPresentation.RarityTicks(_rarity);
        if (ticks > 0)
        {
            float tick = size >= 44f ? 4f : 3f;
            float x = inset + (_equipped ? 2f : 0f);
            float y = Size.Y - inset - tick;
            Color color = UiTheme.RarityColor(_rarity);
            for (int i = 0; i < ticks; i++)
            {
                DrawRect(new Rect2(x - 1f, y - 1f, tick + 2f, tick + 2f), UiTheme.Keyline);
                DrawRect(new Rect2(x, y, tick, tick), color);
                x += tick + 2f;
            }
        }

        if (_isNew)
        {
            float radius = Mathf.Max(3.5f, size * 0.09f);
            Vector2 centre = new(_pipLeft + radius + 1f, inset + radius);
            DrawColoredPolygon(Diamond(centre, radius + 1.5f), UiTheme.Keyline);
            DrawColoredPolygon(Diamond(centre, radius), UiTheme.Accent);
        }
    }

    private static Vector2[] Diamond(Vector2 centre, float radius) => new[]
    {
        centre + new Vector2(0f, -radius),
        centre + new Vector2(radius, 0f),
        centre + new Vector2(0f, radius),
        centre + new Vector2(-radius, 0f),
    };
}
