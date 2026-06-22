using Godot;

namespace Embervale.UI;

/// <summary>
/// A screen-centre crosshair so the third-person aim point (where melee raycasts,
/// interaction and spells are directed) is visible. Drawn in code as four short arms
/// around a small centre dot, with a soft dark outline so it reads on any background.
/// It ignores mouse input so it never blocks menu clicks.
///
/// The reticle is positioned from the <em>viewport</em> centre rather than this
/// control's own rect: a <see cref="Control"/> parented to a <see cref="CanvasLayer"/>
/// can momentarily report a zero size, which would otherwise draw the cross in the
/// top-left corner instead of the middle of the screen.
/// </summary>
public partial class Crosshair : Control
{
    /// <summary>Gap between the centre and the start of each arm.</summary>
    [Export] public float Gap { get; set; } = 4f;

    /// <summary>Length of each arm.</summary>
    [Export] public float Length { get; set; } = 7f;

    [Export] public float Thickness { get; set; } = 2f;

    private static readonly Color LineColor = new(0.96f, 0.97f, 1.00f, 0.9f);
    private static readonly Color Outline = new(0f, 0f, 0f, 0.5f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Keep it centred when the window/viewport is resized.
        GetViewport().SizeChanged += QueueRedraw;
    }

    public override void _Draw()
    {
        Vector2 c = (GetViewportRect().Size * 0.5f).Round();

        DrawArm(c, Vector2.Right);
        DrawArm(c, Vector2.Left);
        DrawArm(c, Vector2.Up);
        DrawArm(c, Vector2.Down);

        DrawRect(new Rect2(c - new Vector2(1f, 1f), new Vector2(2f, 2f)), LineColor);
    }

    private void DrawArm(Vector2 centre, Vector2 direction)
    {
        Vector2 from = centre + (direction * Gap);
        Vector2 to = centre + (direction * (Gap + Length));

        // A 1px-offset dark line first gives the bright reticle contrast on light scenes.
        DrawLine(from + Vector2.One, to + Vector2.One, Outline, Thickness + 1f);
        DrawLine(from, to, LineColor, Thickness);
    }
}
