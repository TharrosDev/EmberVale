using Godot;

namespace Embervale.UI;

/// <summary>
/// A perk node's state as a shape, drawn beside its name: a padlock (locked), a hollow diamond
/// (available), a filled diamond (owned), a filled diamond inside a ring (maxed). A capstone wears a
/// square bracket around whichever of those it has. The shape is the channel that survives a
/// colour-vision mode; the colour only agrees with it.
/// </summary>
public sealed partial class PerkNodeMark : Control
{
    private readonly PerkNodeVisual _visual;
    private readonly bool _keystone;
    private readonly Color _color;

    public PerkNodeMark(PerkNodeVisual visual, bool keystone, Color color, float size = 14f)
    {
        _visual = visual;
        _keystone = keystone;
        _color = color;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 centre = Size * 0.5f;
        float half = Mathf.Min(Size.X, Size.Y) * 0.5f;

        if (_keystone)
        {
            DrawRect(new Rect2(centre - new Vector2(half - 0.5f, half - 0.5f), new Vector2((half * 2f) - 1f, (half * 2f) - 1f)), _color, false, 1f);
            half -= 3f;
        }

        switch (_visual)
        {
            case PerkNodeVisual.Locked:
                if (UiIcon.Texture(UiIcon.Kind.Lock) is { } padlock)
                {
                    DrawTextureRect(padlock, new Rect2(centre - new Vector2(half, half), new Vector2(half * 2f, half * 2f)), false, _color);
                }
                else
                {
                    DrawRect(new Rect2(centre - new Vector2(half - 2f, half - 2f), new Vector2((half * 2f) - 4f, (half * 2f) - 4f)), _color, false, 1.5f);
                }

                break;

            case PerkNodeVisual.Available:
                DrawPolyline(Diamond(centre, half - 1f, closed: true), _color, 1.5f, true);
                break;

            case PerkNodeVisual.Owned:
                DrawColoredPolygon(Diamond(centre, half - 1f, closed: false), _color);
                break;

            default:
                DrawPolyline(Diamond(centre, half - 0.5f, closed: true), _color, 1f, true);
                DrawColoredPolygon(Diamond(centre, half - 3.5f, closed: false), _color);
                break;
        }
    }

    private static Vector2[] Diamond(Vector2 centre, float radius, bool closed)
    {
        radius = Mathf.Max(1f, radius);
        Vector2 top = centre + new Vector2(0f, -radius);
        Vector2 right = centre + new Vector2(radius, 0f);
        Vector2 bottom = centre + new Vector2(0f, radius);
        Vector2 left = centre + new Vector2(-radius, 0f);
        return closed ? new[] { top, right, bottom, left, top } : new[] { top, right, bottom, left };
    }
}
