using Godot;

namespace Embervale.UI;

/// <summary>
/// A minimal screen-centre crosshair so the first-person aim point (where melee
/// raycasts, interaction and spells are directed) is visible. Drawn in code as two
/// short lines; it ignores mouse input so it never blocks menu clicks.
/// </summary>
public partial class Crosshair : Control
{
    [Export] public float Length { get; set; } = 6f;
    [Export] public float Thickness { get; set; } = 2f;

    private static readonly Color LineColor = new(0.92f, 0.94f, 0.98f, 0.65f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Notification(int what)
    {
        // Re-centre the drawing when the viewport (and thus our size) changes.
        if (what == NotificationResized)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        DrawLine(c - new Vector2(Length, 0f), c + new Vector2(Length, 0f), LineColor, Thickness);
        DrawLine(c - new Vector2(0f, Length), c + new Vector2(0f, Length), LineColor, Thickness);
    }
}
