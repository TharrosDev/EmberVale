using Embervale.Combat;
using Embervale.Magic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// A spell's glyph on its school-colour disc (<see cref="SpellGlyphs"/>), as a control: the HUD
/// spell row and the spellbook show a spell with the same mark the wheel does. With no spell it
/// draws an empty socket. It redraws only when what it shows changes.
/// </summary>
public partial class SpellDisc : Control
{
    private string _spellId = string.Empty;
    private DamageType _school;
    private bool _lit = true;

    /// <summary>Keylined for the HUD, where the disc sits on the world and not on a panel.</summary>
    public bool Keylined { get; set; }

    /// <summary>The spell shown, or empty for a socket.</summary>
    public string SpellId => _spellId;

    public SpellDisc()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public static SpellDisc Create(float size, bool keylined = false) =>
        new() { CustomMinimumSize = new Vector2(size, size), Keylined = keylined };

    /// <summary>Shows <paramref name="spell"/> (null for an empty socket). An unlit disc is the
    /// wheel's look for a spell the caster cannot use right now: the glyph in the school's colour
    /// on a dark ground.</summary>
    public void Display(SpellResource? spell, bool lit = true)
    {
        string id = spell?.Id ?? string.Empty;
        DamageType school = spell?.School ?? default;
        if (id == _spellId && school == _school && lit == _lit)
        {
            return;
        }

        _spellId = id;
        _school = school;
        _lit = lit;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y);
        if (side <= 0f)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        if (_spellId.Length == 0)
        {
            DrawArc(centre, (side * 0.5f) - 1f, 0f, Mathf.Tau, 32, UiTheme.WheelSocket, UiTheme.WheelLine, true);
            return;
        }

        if (Keylined)
        {
            DrawCircle(centre, (side * 0.5f) + 1f, UiTheme.Keyline, true, -1f, true);
        }

        SpellGlyphs.Draw(
            this, _spellId, new Rect2(Vector2.Zero, Size),
            _lit ? UiTheme.SchoolColor(_school) : UiTheme.WheelUnlitDisc(_school),
            _lit ? UiTheme.WheelGlyphInk : UiTheme.WheelUnlitInk(_school));

        if (Keylined)
        {
            DrawArc(centre, (side * 0.5f) - 0.5f, 0f, Mathf.Tau, 40, UiTheme.HudInnerEdge, 1f, true);
        }
    }
}
