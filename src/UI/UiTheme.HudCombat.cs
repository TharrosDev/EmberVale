using Godot;

namespace Embervale.UI;

/// <summary>
/// The combat HUD's share of the theme: the poise colour the target plate and the enemy plates
/// share, and the lock-on mark.
/// </summary>
public static partial class UiTheme
{
    /// <summary>Poise: cold steel beside the warm health red, so the two bars of a plate differ in
    /// temperature as well as in height.</summary>
    public static readonly Color Poise = new(0.62f, 0.66f, 0.72f);

    /// <summary>Radius of the lock-on dot, in px.</summary>
    public const float LockDotRadius = 4f;

    /// <summary>
    /// Draws the lock-on mark on <paramref name="item"/> at <paramref name="centre"/>: one small
    /// ember dot inside a keyline, which reads over a bright sky and a dark hall alike and covers
    /// nothing of the thing it marks.
    /// </summary>
    public static void DrawLockDot(CanvasItem item, Vector2 centre, float alpha = 1f)
    {
        item.DrawCircle(centre, LockDotRadius + 1.5f, Keyline with { A = Keyline.A * alpha });
        item.DrawCircle(centre, LockDotRadius, AccentHot with { A = alpha });
    }

    /// <summary>The lock-on mark as a control <paramref name="size"/> px square with the dot at its
    /// centre, for a caller that positions a node rather than drawing.</summary>
    public static Control LockDot(float size)
    {
        var dot = new LockDotMark
        {
            CustomMinimumSize = new Vector2(size, size),
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return dot;
    }
}
