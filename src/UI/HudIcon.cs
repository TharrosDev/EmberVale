using Godot;

namespace Embervale.UI;

/// <summary>
/// A <see cref="UiIcon"/> glyph drawn for the live world: the shape in its tint over a
/// <see cref="UiTheme.Keyline"/> copy of itself, the way <see cref="UiTheme.HudInk{T}"/> keylines
/// text. The glyphs are thin strokes, and a thin red stroke over brown ground or a gold one over a
/// bright sky is not there; the keyline is what makes the shape readable on both.
///
/// It draws only when its texture, tint or size changes: there is no frame callback.
/// </summary>
public partial class HudIcon : Control
{
    // The eight neighbours the keyline copy is stamped at, one pixel out.
    private static readonly Vector2[] KeylineOffsets =
    {
        new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
        new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f),
    };

    private Texture2D? _texture;
    private Color _tint = UiTheme.Text;

    public HudIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The glyph. Written only when it changes.</summary>
    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            if (!ReferenceEquals(_texture, value))
            {
                _texture = value;
                QueueRedraw();
            }
        }
    }

    /// <summary>The colour the glyph is drawn in. Written only when it changes.</summary>
    public Color Tint
    {
        get => _tint;
        set
        {
            if (_tint != value)
            {
                _tint = value;
                QueueRedraw();
            }
        }
    }

    /// <summary>A keylined <paramref name="kind"/> glyph, <paramref name="size"/> px square.</summary>
    public static HudIcon Create(UiIcon.Kind kind, float size, Color tint) => new()
    {
        CustomMinimumSize = new Vector2(size, size),
        Texture = UiIcon.Texture(kind),
        Tint = tint,
    };

    public override void _Draw()
    {
        if (_texture == null)
        {
            return;
        }

        // The glyphs are square: centred in the largest square the control holds.
        float side = Mathf.Min(Size.X, Size.Y);
        var rect = new Rect2((Size - new Vector2(side, side)) / 2f, new Vector2(side, side));
        Color keyline = UiTheme.Keyline;
        foreach (Vector2 offset in KeylineOffsets)
        {
            DrawTextureRect(_texture, new Rect2(rect.Position + offset, rect.Size), false, keyline);
        }

        DrawTextureRect(_texture, rect, false, _tint);
    }
}
