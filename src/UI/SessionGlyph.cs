using Godot;

namespace Embervale.UI;

/// <summary>
/// The small drawn marks of a save row: how the save was made (a ribbon for one made by hand, a
/// bolt for the quick slot, a turning arrow for an autosave), the corruption mark and its pips.
/// Drawn rather than loaded, like <see cref="UiDeltaArrow"/>: nothing to import, nothing to fail
/// to load, and they retint with the palette. Each sits beside the word for the same thing.
/// </summary>
public sealed partial class SessionGlyph : Control
{
    public enum Shape
    {
        /// <summary>A bookmark ribbon: a save the player made by hand.</summary>
        Manual,

        /// <summary>A bolt: the quick slot.</summary>
        Quick,

        /// <summary>A turning arrow: an autosave.</summary>
        Auto,

        /// <summary>A hollow thorn with a filled heart: corruption.</summary>
        Corruption,

        /// <summary>A row of pips, the first few of them filled.</summary>
        Pips,
    }

    private readonly Shape _shape;
    private readonly Color _color;
    private readonly int _level;
    private readonly int _count;

    /// <param name="level">For <see cref="Shape.Pips"/>: how many are filled.</param>
    /// <param name="count">For <see cref="Shape.Pips"/>: how many there are.</param>
    public SessionGlyph(Shape shape, Color color, float size, int level = 0, int count = 0)
    {
        _shape = shape;
        _color = color;
        _level = level;
        _count = count;
        float width = shape == Shape.Pips ? (count * PipStep(size)) - PipGap(size) : size;
        CustomMinimumSize = new Vector2(Mathf.Max(width, 0f), size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private static float PipEdge(float size) => Mathf.Round(size * 0.45f);

    private static float PipGap(float size) => Mathf.Round(size * 0.25f);

    private static float PipStep(float size) => PipEdge(size) + PipGap(size);

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        switch (_shape)
        {
            case Shape.Manual:
                DrawColoredPolygon(
                    new[] { P(w, h, 0.25f, 0.08f), P(w, h, 0.75f, 0.08f), P(w, h, 0.75f, 0.92f), P(w, h, 0.5f, 0.66f), P(w, h, 0.25f, 0.92f) },
                    _color);
                break;

            case Shape.Quick:
                // Two triangles, so nothing depends on a concave polygon triangulating.
                DrawColoredPolygon(new[] { P(w, h, 0.62f, 0.04f), P(w, h, 0.2f, 0.56f), P(w, h, 0.52f, 0.56f) }, _color);
                DrawColoredPolygon(new[] { P(w, h, 0.38f, 0.96f), P(w, h, 0.8f, 0.44f), P(w, h, 0.48f, 0.44f) }, _color);
                break;

            case Shape.Auto:
            {
                Vector2 centre = new(w * 0.5f, h * 0.5f);
                float radius = Mathf.Min(w, h) * 0.34f;
                const float Start = -Mathf.Pi * 0.5f;
                const float End = Start + (Mathf.Tau * 0.8f);
                DrawArc(centre, radius, Start, End, 20, _color, 1.5f, true);

                // The head, at the open end and pointing the way the arc turns.
                Vector2 tip = centre + (Vector2.FromAngle(End) * radius);
                Vector2 along = Vector2.FromAngle(End + (Mathf.Pi * 0.5f));
                Vector2 across = Vector2.FromAngle(End);
                float head = Mathf.Min(w, h) * 0.22f;
                DrawColoredPolygon(
                    new[] { tip + (along * head), tip + (across * head), tip - (across * head) },
                    _color);
                break;
            }

            case Shape.Corruption:
            {
                Vector2[] thorn =
                {
                    P(w, h, 0.5f, 0.04f), P(w, h, 0.9f, 0.5f), P(w, h, 0.5f, 0.96f), P(w, h, 0.1f, 0.5f), P(w, h, 0.5f, 0.04f),
                };
                DrawPolyline(thorn, _color, 1.5f, true);
                DrawColoredPolygon(
                    new[] { P(w, h, 0.5f, 0.3f), P(w, h, 0.68f, 0.5f), P(w, h, 0.5f, 0.7f), P(w, h, 0.32f, 0.5f) },
                    _color);
                break;
            }

            default:
            {
                // Filled pips are solid; the rest are an outline, so the count reads without colour.
                float edge = PipEdge(h);
                float step = PipStep(h);
                float top = Mathf.Round((h - edge) * 0.5f);
                for (int i = 0; i < _count; i++)
                {
                    var pip = new Rect2(i * step, top, edge, edge);
                    if (i < _level)
                    {
                        DrawRect(pip, _color);
                    }
                    else
                    {
                        DrawRect(pip.Grow(-0.5f), UiTheme.Dim, filled: false, width: 1f);
                    }
                }

                break;
            }
        }
    }

    private static Vector2 P(float w, float h, float x, float y) => new(w * x, h * y);
}
