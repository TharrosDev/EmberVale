using Godot;

namespace Embervale.UI;

/// <summary>A small filled triangle pointing up or down: the shape half of a stat delta
/// (<see cref="UiTheme.DeltaArrow"/>).</summary>
public sealed partial class UiDeltaArrow : Control
{
    private readonly bool _up;
    private readonly Color _color;

    public UiDeltaArrow(bool up, Color color, float size = 9f)
    {
        _up = up;
        _color = color;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        Vector2[] points = _up
            ? new[] { new Vector2(w * 0.5f, 1f), new Vector2(w - 0.5f, h - 1f), new Vector2(0.5f, h - 1f) }
            : new[] { new Vector2(0.5f, 1f), new Vector2(w - 0.5f, 1f), new Vector2(w * 0.5f, h - 1f) };
        DrawColoredPolygon(points, _color);
    }
}
