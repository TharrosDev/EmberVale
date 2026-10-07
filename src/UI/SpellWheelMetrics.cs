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
/// <para>The wheel is a block, from the top: the legend line, room for the names of a fan's spells,
/// the wheel itself out to the fan's edge, room for names again, then the readout plate. The rim is
/// a share of the view's height, so it takes the same part of every screen, and the block is held
/// inside the view at every HUD scale: on the handheld view (853 by 533) the wheel shrinks before
/// anything leaves the screen.</para>
/// </summary>
public static class SpellWheelMetrics
{
    /// <summary>The rim's radius as a share of the view's height at HUD scale 1: a wheel whose
    /// outer ring spans a little under half the screen.</summary>
    public const float RimShare = 0.23f;

    /// <summary>The smallest rim the wheel is drawn at, however little room there is.</summary>
    public const float MinRadius = 84f;

    /// <summary>Clear space kept between the block and the view's edge.</summary>
    public const float Margin = 12f;

    /// <summary>Space between the parts of the block.</summary>
    public const float Gap = 8f;

    /// <summary>Height of the readout plate: a name line and a state line.</summary>
    public const float ReadoutHeight = 56f;

    /// <summary>Height of the legend line.</summary>
    public const float LegendHeight = 28f;

    /// <summary>Room kept past the fan's edge, above and below, for the names of its spells: two
    /// short lines.</summary>
    public const float LabelRoom = 34f;

    /// <summary>Where the wedge contents sit, in wheel units.</summary>
    public const float FavouriteRadius = (SpellWheelRules.DeadZone + SpellWheelRules.InnerEdge) * 0.5f;
    public const float SchoolRadius = (SpellWheelRules.InnerEdge + SpellWheelRules.OuterEdge) * 0.5f;
    public const float FanRadius = (SpellWheelRules.OuterEdge + SpellWheelRules.FanEdge) * 0.5f;

    /// <summary>The gap between the rim and the fan, in wheel units, so the fan reads as a second
    /// thing that opened rather than a thicker rim.</summary>
    public const float FanGap = 0.035f;

    /// <summary>How much larger the spell under the cursor is drawn.</summary>
    public const float HoverGrow = 1.2f;

    /// <summary>How far the opening wheel grows: it starts at this share of its size.</summary>
    public const float BloomFrom = 0.90f;

    /// <summary>Seconds between repaints for cooldowns while nothing else changes.</summary>
    public const float RepaintSeconds = 0.25f;

    /// <summary>What sits over the fan's top edge: the names' room and the legend.</summary>
    public static float AboveWheel => LabelRoom + Gap + LegendHeight;

    /// <summary>What sits under the fan's bottom edge: the names' room and the readout.</summary>
    public static float BelowWheel => LabelRoom + Gap + ReadoutHeight;

    /// <summary>
    /// The rim's radius in px for a view of <paramref name="viewSize"/>: <see cref="RimShare"/> of
    /// its height times the HUD scale, held to what the view has room for.
    /// </summary>
    public static float Radius(Vector2 viewSize, float hudScale)
    {
        float scale = float.IsFinite(hudScale) && hudScale > 0f ? hudScale : 1f;
        float wanted = viewSize.Y * RimShare * scale;
        float tall = (viewSize.Y - (Margin * 2f) - AboveWheel - BelowWheel) / (SpellWheelRules.FanEdge * 2f);
        float wide = (viewSize.X - (Margin * 2f)) / (SpellWheelRules.FanEdge * 2f);
        float fits = MathF.Min(tall, wide);
        if (!float.IsFinite(fits) || !float.IsFinite(wanted))
        {
            return MinRadius;
        }

        return MathF.Max(MinRadius, MathF.Min(wanted, fits));
    }

    /// <summary>
    /// The wheel's centre in a view of <paramref name="viewSize"/>: the middle of the view, moved
    /// only as far as the legend over it and the readout under it need to stay on screen.
    /// </summary>
    public static Vector2 Centre(Vector2 viewSize, float radius)
    {
        float reach = radius * SpellWheelRules.FanEdge;
        float lowest = viewSize.Y - Margin - BelowWheel - reach;
        float highest = Margin + AboveWheel + reach;
        float y = MathF.Max(highest, MathF.Min(viewSize.Y * 0.5f, lowest));
        return new Vector2(viewSize.X * 0.5f, y);
    }

    /// <summary>The top of the legend line, over the wheel.</summary>
    public static float LegendTop(Vector2 centre, float radius) =>
        centre.Y - (radius * SpellWheelRules.FanEdge) - AboveWheel;

    /// <summary>
    /// How far under the centre the open fan of <paramref name="school"/> reaches, in wheel units:
    /// <see cref="SpellWheelRules.FanEdge"/> for a fan that spans straight down, less for one off
    /// to a side, and nothing for one wholly in the top half or for no fan at all.
    /// </summary>
    public static float FanDrop(int school, int spellCount)
    {
        if (school < 0 || spellCount <= 0)
        {
            return 0f;
        }

        float half = SpellWheelRules.FanSpanDegrees(spellCount) * 0.5f;
        float mid = school * SpellWheelRules.SchoolWedgeDegrees;

        // How far the fan's middle is from straight down, folded into 0..180.
        float off = MathF.Abs((((mid - 180f) % 360f) + 540f) % 360f - 180f);
        float nearest = MathF.Max(0f, off - half);
        return MathF.Max(0f, SpellWheelRules.FanEdge * MathF.Cos(nearest * (MathF.PI / 180f)));
    }

    /// <summary>
    /// The top of the readout plate: close under the rim, and under the fan and its names when the
    /// open fan (<paramref name="fanSchool"/>, -1 for none) hangs lower than the rim does.
    /// </summary>
    public static float ReadoutTop(Vector2 centre, float radius, int fanSchool = -1, int fanCount = 0)
    {
        float drop = radius;
        float fan = radius * FanDrop(fanSchool, fanCount);
        if (fan > 0f)
        {
            drop = MathF.Max(drop, fan + LabelRoom);
        }

        return centre.Y + drop + Gap;
    }

    /// <summary>The readout plate's width: wide enough for the longest spell name at header size,
    /// never wider than the view.</summary>
    public static float ReadoutWidth(Vector2 viewSize, float radius) =>
        MathF.Max(120f, MathF.Min(MathF.Max(280f, radius * 1.7f), viewSize.X - (Margin * 2f)));

    /// <summary>The size of the readout's name line for a rim of <paramref name="radius"/>.</summary>
    public static int NameSize(float radius) => (int)Math.Clamp(MathF.Round(radius * 0.115f), 18f, 24f);

    /// <summary>The size of the wheel's small text (the state line, the names on a fan and on the
    /// schools) for a rim of <paramref name="radius"/>.</summary>
    public static int TextSize(float radius) => (int)Math.Clamp(MathF.Round(radius * 0.075f), 12f, 16f);

    /// <summary>
    /// A fan spell's name as the one or two lines it is set in past the fan's edge: broken at the
    /// space nearest its middle, so a two-word name stacks and leaves its neighbours room.
    /// </summary>
    public static (string First, string Second) NameLines(string? name)
    {
        string text = (name ?? string.Empty).Trim();
        int best = -1;
        for (int i = text.IndexOf(' '); i >= 0; i = text.IndexOf(' ', i + 1))
        {
            if (best < 0 || Math.Abs((i * 2) - text.Length) < Math.Abs((best * 2) - text.Length))
            {
                best = i;
            }
        }

        return best < 0 ? (text, string.Empty) : (text[..best], text[(best + 1)..]);
    }

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

    /// <summary>The seconds numeral on a cooling wedge; 0 for none. Every cooling spell carries
    /// one (up to 99), which with the wipe is what tells cooling from too dear at a glance.</summary>
    public static int Numeral(float remaining) =>
        remaining > 0f ? (int)MathF.Min(99f, MathF.Ceiling(remaining)) : 0;

    /// <summary>The opening wheel's size, as a share of its full size, <paramref name="progress"/>
    /// (0..1) of the way through its bloom.</summary>
    public static float Bloom(float progress) =>
        BloomFrom + ((1f - BloomFrom) * UiMotion.EaseOut(progress));
}
