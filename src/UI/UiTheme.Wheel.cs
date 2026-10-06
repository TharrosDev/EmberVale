using Embervale.Combat;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spell wheel's share of the theme. The wheel is HUD chrome drawn over a live world, so it
/// follows the HUD's rules (<c>UiTheme.Hud.cs</c>): dark keylined wells, one lit edge on the thing
/// under the cursor, inked text, and every ground opaque under high contrast. Its measurements are
/// <see cref="SpellWheelMetrics"/>.
/// </summary>
public static partial class UiTheme
{
    /// <summary>The ink of a spell glyph on its school-colour disc.</summary>
    public static readonly Color WheelGlyphInk = new(0.055f, 0.050f, 0.044f);

    /// <summary>How much of its colour a spell the caster cannot use right now keeps.</summary>
    public const float WheelDimmed = 0.42f;

    /// <summary>The disc of a spell the caster cannot use right now: the well, barely warmed by the
    /// school, dark enough that <see cref="WheelUnlitInk"/> reads on it.</summary>
    public static Color WheelUnlitDisc(DamageType school) =>
        WellBg.Lerp(SchoolColor(school), 0.18f) with { A = 1f };

    /// <summary>The glyph of a spell the caster cannot use right now: the school's colour, where a
    /// castable spell's is dark ink on that colour.</summary>
    public static Color WheelUnlitInk(DamageType school) =>
        SchoolColor(school).Lerp(Text, HighContrast ? 0.5f : 0.15f) with { A = 1f };

    /// <summary>Width, in px, of the wheel's keylines.</summary>
    public static float WheelLine => HighContrast ? 3f : 2f;

    /// <summary>Width, in px, of the lit edge on the wedge under the cursor and of a spell's mark.</summary>
    public static float WheelLitLine => HighContrast ? 4f : 3f;

    /// <summary>The soft dark disc under the whole wheel, which lifts it off a bright scene.</summary>
    public static Color WheelBackdrop => ScrimBg with { A = HighContrast ? 0.85f : 0.42f };

    /// <summary>The ground of a favourite wedge, a fan wedge and the centre.</summary>
    public static Color WheelWell => WellBg with { A = HighContrast ? 1f : 0.80f };

    /// <summary>The ground of the wedge under the cursor.</summary>
    public static Color WheelWellHover => ButtonFaceHover with { A = HighContrast ? 1f : 0.94f };

    /// <summary>The ground of a school's wedge: the school's colour, banked down until the emblem on
    /// it reads, and raised when it is the one hovered or the one whose fan is open.</summary>
    public static Color WheelSchoolGround(DamageType school, bool lit) =>
        SchoolColor(school).Darkened(lit ? 0.52f : 0.74f) with { A = HighContrast ? 1f : lit ? 0.94f : 0.84f };

    /// <summary>The ground of a spell's wedge in a school's fan: the well, warmed by the school.</summary>
    public static Color WheelFanGround(DamageType school, bool lit) =>
        lit ? WheelSchoolGround(school, true) : WellBg.Lerp(SchoolColor(school), 0.10f) with { A = HighContrast ? 1f : 0.86f };

    /// <summary>The outline of an empty favourite slot: a socket, faint on purpose.</summary>
    public static Color WheelSocket => IronLit with { A = HighContrast ? 0.95f : 0.38f };

    /// <summary>The lit edge of the wedge under the cursor, and the ring on the prepared spell.</summary>
    public static Color WheelLit => HighContrast ? FocusRing : Accent;

    /// <summary>The bracket on the spell a tap goes back to.</summary>
    public static Color WheelPrevious => Text;

    /// <summary>The pointer tick.</summary>
    public static Color WheelPointer => FocusRing;

    /// <summary>The face a spell's name is set in on the wheel: the carved capitals, or the interface
    /// face under the readable-font setting. Null when no font is imported.</summary>
    public static Font? WheelNameFont => FontFor(FontRole.Display) ?? UiFont;

    /// <summary>The face of the wheel's numbers and state line.</summary>
    public static Font? WheelTextFont => UiFont;

    /// <summary>
    /// Restyles <paramref name="box"/> as the wheel's readout plate: a HUD plate whose one lit edge
    /// is <paramref name="edge"/>. The wheel keeps one box and restyles it per draw rather than
    /// building one, so high contrast is picked up on the next repaint.
    /// </summary>
    public static void StyleWheelReadout(StyleBoxFlat box, Color edge)
    {
        box.BgColor = HudPlateBg;
        box.BorderColor = edge;
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = HudPlateEdge;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(0);
    }
}
