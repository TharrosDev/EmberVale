using System.Collections.Generic;
using Embervale.Magic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spell wheel's favourite ring in miniature, beside the spellbook's pin slots: one numbered dot
/// per slot, laid out exactly where the wheel puts it (<see cref="SpellWheelMetrics.WedgeCentre"/>:
/// slot 1 straight up, then clockwise), in its spell's school colour or as an empty socket, with the
/// slot chosen in the book ringed. The slots are a grid because names need the width; this says where
/// on the wheel each number is, so the player does not have to fold a grid into a circle in their head.
/// It redraws only when what it shows changes.
/// </summary>
public partial class SpellPinDial : Control
{
    private readonly List<string> _pins = new();
    private int _chosen = SpellPinRules.NoSlot;

    public SpellPinDial()
    {
        // Pass, not Ignore: it carries a tooltip and takes no input of its own.
        MouseFilter = MouseFilterEnum.Pass;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public static SpellPinDial Create(float size) => new() { CustomMinimumSize = new Vector2(size, size) };

    /// <summary>Shows the favourite slots (an id or empty each) and which one is chosen (-1 for none).</summary>
    public void Display(IReadOnlyList<string> pins, int chosen)
    {
        _pins.Clear();
        _pins.AddRange(pins);
        _chosen = chosen;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y);
        int count = _pins.Count;
        if (side <= 0f || count == 0)
        {
            return;
        }

        // Dots as large as eight will go round the ring without touching, so the numbers stay legible.
        Vector2 centre = Size * 0.5f;
        float dot = side * 0.11f;
        float ring = (side * 0.5f) - dot - UiTheme.WheelLitLine - 1f;
        Font font = UiTheme.WheelTextFont ?? ThemeDB.FallbackFont;
        int size = Mathf.Max(UiTheme.CaptionFontSize, Mathf.RoundToInt(dot * 1.25f));

        DrawArc(centre, ring, 0f, Mathf.Tau, 48, UiTheme.WheelSocket, 1f, true);

        // A small notch on the hub pointing up: where the count starts.
        DrawLine(centre, centre + (Vector2.Up * (ring - dot - 3f)), UiTheme.WheelSocket, 1f, true);
        DrawCircle(centre, 2.5f, UiTheme.WheelSocket, true, -1f, true);

        for (int i = 0; i < count; i++)
        {
            Vector2 at = centre + (SpellWheelMetrics.Direction(SpellWheelMetrics.WedgeCentre(i, count)) * ring);
            SpellResource? spell = _pins[i].Length > 0 ? SpellDatabase.Get(_pins[i]) : null;
            Color ink;
            if (spell != null)
            {
                DrawCircle(at, dot, UiTheme.SchoolColor(spell.School), true, -1f, true);
                ink = UiTheme.WheelGlyphInk;
            }
            else
            {
                DrawCircle(at, dot, UiTheme.WheelWell, true, -1f, true);
                DrawArc(at, dot - 0.5f, 0f, Mathf.Tau, 24, UiTheme.WheelSocket, 1f, true);
                ink = UiTheme.Dim;
            }

            if (i == _chosen)
            {
                DrawArc(at, dot + 2f, 0f, Mathf.Tau, 28, UiTheme.WheelLit, UiTheme.WheelLitLine, true);
            }

            string number = SpellPinRules.SlotNumber(i).ToString();
            Vector2 text = font.GetStringSize(number, HorizontalAlignment.Left, -1f, size);
            float baseline = at.Y + ((font.GetAscent(size) - font.GetDescent(size)) * 0.5f);
            DrawString(font, new Vector2(at.X - (text.X * 0.5f), baseline), number, HorizontalAlignment.Left, -1f, size, ink);
        }
    }
}
