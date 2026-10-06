using Godot;

namespace Embervale.UI;

/// <summary>The shapes a <see cref="MarkGlyph"/> draws.</summary>
public enum MarkKind
{
    Tick,
    Cross,
    Diamond,
    DiamondHollow,
    Dash,
}

/// <summary>
/// A small drawn mark: the state of an objective or the kind of a dialogue option, as a shape. It
/// is drawn rather than typed, so it needs no glyph in any of the three faces and reads the same
/// under every colour-vision setting.
/// </summary>
public partial class MarkGlyph : Control
{
    private readonly MarkKind _kind;
    private readonly Color _color;

    public MarkGlyph(MarkKind kind, Color color, float size = 16f)
    {
        _kind = kind;
        _color = color;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    /// <summary>The mark for a stage-log line.</summary>
    public static MarkGlyph For(ObjectiveMark mark, Color color, float size = 16f) => new(
        mark switch
        {
            ObjectiveMark.Done => MarkKind.Tick,
            ObjectiveMark.Failed => MarkKind.Cross,
            ObjectiveMark.Optional => MarkKind.DiamondHollow,
            _ => MarkKind.Diamond,
        },
        color, size);

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        Vector2 c = Size * 0.5f;
        float r = s * 0.34f;
        float width = UiTheme.HighContrast ? 2.5f : 2f;

        switch (_kind)
        {
            case MarkKind.Tick:
                DrawPolyline(
                    new[] { c + new Vector2(-r, 0f), c + new Vector2(-r * 0.3f, r * 0.7f), c + new Vector2(r, -r * 0.7f) },
                    _color, width, true);
                break;

            case MarkKind.Cross:
                DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), _color, width, true);
                DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), _color, width, true);
                break;

            case MarkKind.Dash:
                DrawLine(c + new Vector2(-r, 0f), c + new Vector2(r, 0f), _color, width, true);
                break;

            default:
                Vector2[] diamond =
                {
                    c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f),
                };
                if (_kind == MarkKind.Diamond)
                {
                    DrawColoredPolygon(diamond, _color);
                }
                else
                {
                    DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, _color, width, true);
                }

                break;
        }
    }
}
