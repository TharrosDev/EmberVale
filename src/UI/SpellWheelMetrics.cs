using System;
using Embervale.Magic;
using Godot;

namespace Embervale.UI;

/// <summary>What the wheel says about the thing under the cursor.</summary>
public enum SpellWheelCellState
{
    /// <summary>No spell here (an empty favourite slot).</summary>
    Empty,

    /// <summary>Castable now.</summary>
    Ready,

    /// <summary>On cooldown.</summary>
    Cooling,

    /// <summary>Off cooldown, but the mana is not there.</summary>
    Unaffordable,

    /// <summary>The caster's corruption is too shallow for it.</summary>
    Locked,
}

/// <summary>
/// The spell wheel's measurements and the small decisions its drawing makes. Pure: the geometry of
/// what the cursor is over is <see cref="SpellWheelRules"/>; this is where the wheel sits, how big
/// it is and what a wedge shows.
///
/// <para>The wheel is a block: the wheel itself out to the fan's edge, then the readout plate, then
/// the legend line. The block is held inside the view at every HUD scale, so on the handheld view
/// (853 by 533) the wheel shrinks before anything leaves the screen.</para>
/// </summary>
public static class SpellWheelMetrics
{
    /// <summary>The rim's radius, in px, at HUD scale 1: the mouse travel that crosses it
    /// (<see cref="SpellWheelRules.MouseScale"/>).</summary>
    public const float BaseRadius = 150f;

    /// <summary>The smallest rim the wheel is drawn at, however little room there is.</summary>
    public const float MinRadius = 84f;

    /// <summary>Clear space kept between the block and the view's edge.</summary>
    public const float Margin = 12f;

    /// <summary>Space between the fan's edge, the readout and the legend.</summary>
    public const float Gap = 8f;

    /// <summary>Height of the readout plate: a name line and a state line.</summary>
    public const float ReadoutHeight = 48f;

    /// <summary>Height of the legend line.</summary>
    public const float LegendHeight = 28f;

    /// <summary>Where the wedge contents sit, in wheel units.</summary>
    public const float FavouriteRadius = (SpellWheelRules.DeadZone + SpellWheelRules.InnerEdge) * 0.5f;
    public const float SchoolRadius = (SpellWheelRules.InnerEdge + SpellWheelRules.OuterEdge) * 0.5f;
    public const float FanRadius = (SpellWheelRules.OuterEdge + SpellWheelRules.FanEdge) * 0.5f;

    /// <summary>The gap between the rim and the fan, in wheel units, so the fan reads as a second
    /// thing that opened rather than a thicker rim.</summary>
    public const float FanGap = 0.035f;

    /// <summary>How far the opening wheel grows: it starts at this share of its size.</summary>
    public const float BloomFrom = 0.90f;

    /// <summary>Seconds between repaints for cooldowns while nothing else changes.</summary>
    public const float RepaintSeconds = 0.25f;

    /// <summary>What sits under the wheel: the readout and the legend with their gaps.</summary>
    public static float BelowWheel => Gap + ReadoutHeight + Gap + LegendHeight;

    /// <summary>
    /// The rim's radius in px for a view of <paramref name="viewSize"/>:
    /// <see cref="BaseRadius"/> times the HUD scale, held to what the view has room for.
    /// </summary>
    public static float Radius(Vector2 viewSize, float hudScale)
    {
        float scale = float.IsFinite(hudScale) && hudScale > 0f ? hudScale : 1f;
        float wanted = BaseRadius * scale;
        float tall = (viewSize.Y - (Margin * 2f) - BelowWheel) / (SpellWheelRules.FanEdge * 2f);
        float wide = (viewSize.X - (Margin * 2f)) / (SpellWheelRules.FanEdge * 2f);
        float fits = MathF.Min(tall, wide);
        if (!float.IsFinite(fits))
        {
            return MinRadius;
        }

        return MathF.Max(MinRadius, MathF.Min(wanted, fits));
    }

    /// <summary>
    /// The wheel's centre in a view of <paramref name="viewSize"/>: the middle of the view, lifted
    /// only as far as the readout and legend under it need to stay on screen.
    /// </summary>
    public static Vector2 Centre(Vector2 viewSize, float radius)
    {
        float reach = radius * SpellWheelRules.FanEdge;
        float lowest = viewSize.Y - Margin - BelowWheel - reach;
        float highest = Margin + reach;
        float y = MathF.Max(highest, MathF.Min(viewSize.Y * 0.5f, lowest));
        return new Vector2(viewSize.X * 0.5f, y);
    }

    /// <summary>The top of the readout plate.</summary>
    public static float ReadoutTop(Vector2 centre, float radius) =>
        centre.Y + (radius * SpellWheelRules.FanEdge) + Gap;

    /// <summary>The top of the legend line.</summary>
    public static float LegendTop(Vector2 centre, float radius) =>
        ReadoutTop(centre, radius) + ReadoutHeight + Gap;

    /// <summary>The readout plate's width: wide enough for the longest spell name at header size,
    /// never wider than the view.</summary>
    public static float ReadoutWidth(Vector2 viewSize, float radius) =>
        MathF.Max(120f, MathF.Min(MathF.Max(260f, radius * 2f), viewSize.X - (Margin * 2f)));

    /// <summary>The side of a glyph in a wedge of <paramref name="wedgeDegrees"/> centred
    /// <paramref name="atRadius"/> from the middle and <paramref name="depth"/> deep (all px): as
    /// large as fits both ways with air round it.</summary>
    public static float GlyphSide(float atRadius, float wedgeDegrees, float depth)
    {
        float half = MathF.Min(wedgeDegrees, 180f) * 0.5f * (MathF.PI / 180f);
        float chord = 2f * atRadius * MathF.Sin(half);
        return MathF.Max(8f, MathF.Min(chord * 0.78f, depth * 0.72f));
    }

    /// <summary>The unit vector at <paramref name="degrees"/> clockwise from straight up, on a
    /// screen (+Y down). The inverse of <see cref="SpellWheelRules.AngleDegrees"/>.</summary>
    public static Vector2 Direction(float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        return new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    /// <summary>A wheel angle (clockwise from up, degrees) as the angle the engine's arcs take
    /// (radians from +X, clockwise on screen).</summary>
    public static float ArcAngle(float degrees) => (degrees - 90f) * (MathF.PI / 180f);

    /// <summary>The centre angle of wedge <paramref name="index"/> of <paramref name="count"/>.</summary>
    public static float WedgeCentre(int index, int count) => index * (360f / count);

    /// <summary>The centre angle of spell <paramref name="index"/> in <paramref name="school"/>'s
    /// fan of <paramref name="spellCount"/>.</summary>
    public static float FanCentre(int school, int index, int spellCount)
    {
        float span = SpellWheelRules.FanSpanDegrees(spellCount);
        float each = span / Math.Max(1, spellCount);
        return (school * SpellWheelRules.SchoolWedgeDegrees) - (span * 0.5f) + (each * (index + 0.5f));
    }

    /// <summary>
    /// The outline of a ring sector, for one filled polygon: along the outer arc from
    /// <paramref name="fromDegrees"/> to <paramref name="toDegrees"/> (clockwise from up), then back
    /// along the inner arc. One chord per <paramref name="stepDegrees"/>.
    /// </summary>
    public static Vector2[] Sector(
        Vector2 centre, float inner, float outer, float fromDegrees, float toDegrees, float stepDegrees = 5f)
    {
        float span = toDegrees - fromDegrees;
        int steps = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(span) / MathF.Max(1f, stepDegrees)));
        var points = new Vector2[(steps + 1) * 2];
        for (int i = 0; i <= steps; i++)
        {
            Vector2 direction = Direction(fromDegrees + (span * i / steps));
            points[i] = centre + (direction * outer);
            points[points.Length - 1 - i] = centre + (direction * inner);
        }

        return points;
    }

    /// <summary>What a spell's wedge says. A lock outranks everything, a cooldown outranks the
    /// price: the player reads the reason that will last longest.</summary>
    public static SpellWheelCellState State(bool hasSpell, bool locked, float cooldownRemaining, float mana, float cost)
    {
        if (!hasSpell)
        {
            return SpellWheelCellState.Empty;
        }

        if (locked)
        {
            return SpellWheelCellState.Locked;
        }

        if (cooldownRemaining > 0f)
        {
            return SpellWheelCellState.Cooling;
        }

        return mana < cost ? SpellWheelCellState.Unaffordable : SpellWheelCellState.Ready;
    }

    /// <summary>The share of a cooldown still to run, 0..1. A spell with no cooldown of its own that
    /// is somehow waiting shows a full wipe rather than none.</summary>
    public static float CooldownFraction(float remaining, float total)
    {
        if (!(remaining > 0f))
        {
            return 0f;
        }

        return total > 0f ? Math.Clamp(remaining / total, 0f, 1f) : 1f;
    }

    /// <summary>The seconds numeral on a cooling wedge: shown for the last nine, as on the hotbar;
    /// 0 for none.</summary>
    public static int Numeral(float remaining) =>
        remaining > 0f && remaining <= 9f ? (int)MathF.Ceiling(remaining) : 0;

    /// <summary>The opening wheel's size, as a share of its full size, <paramref name="progress"/>
    /// (0..1) of the way through its bloom.</summary>
    public static float Bloom(float progress) =>
        BloomFrom + ((1f - BloomFrom) * UiMotion.EaseOut(progress));
}
