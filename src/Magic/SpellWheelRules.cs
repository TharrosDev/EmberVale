using System;
using System.Collections.Generic;
using Embervale.Combat;
using Godot;

namespace Embervale.Magic;

/// <summary>What a press of the spell-wheel button turned out to be.</summary>
public enum SpellWheelGesture
{
    /// <summary>A quick press: swap back to the previous spell.</summary>
    Tap,

    /// <summary>A hold: the wheel.</summary>
    Wheel,
}

/// <summary>What kind of thing the wheel's cursor is over.</summary>
public enum SpellWheelPickKind
{
    None,
    Favourite,
    School,
    Spell,
}

/// <summary>
/// What the cursor is over. <see cref="Index"/> is the favourite slot, the school's place in
/// <see cref="SpellWheelRules.Schools"/>, or the spell's place in its school's list.
/// <see cref="SpellId"/> is set for a spell and for a favourite slot that holds one;
/// <see cref="School"/> is set for a school and for a spell in a school's fan.
/// </summary>
public readonly record struct SpellWheelPick(SpellWheelPickKind Kind, int Index = -1, string SpellId = "", int School = -1)
{
    public static readonly SpellWheelPick None = new(SpellWheelPickKind.None);

    /// <summary>True when releasing here selects a spell.</summary>
    public bool Selects => Kind is SpellWheelPickKind.Favourite or SpellWheelPickKind.Spell && SpellId.Length > 0;
}

/// <summary>
/// What the wheel is showing. <paramref name="Favourites"/> is the eight pinned slots (an id or
/// empty); <paramref name="SchoolSpells"/> is the known spells of each school, indexed as
/// <see cref="SpellWheelRules.Schools"/>.
/// </summary>
public sealed record SpellWheelLayout(
    IReadOnlyList<string> Favourites, IReadOnlyList<IReadOnlyList<string>> SchoolSpells);

/// <summary>
/// The spell wheel's geometry and its tap-or-hold decision. Pure.
///
/// <para>The cursor is a point in wheel units: the outer ring's edge is 1, +X is right and +Y is
/// down, as on screen. Angles run clockwise from the top, and every wedge is centred on its angle, so
/// favourite 0 and the first school both sit straight up.</para>
///
/// <para>From the centre out: a dead zone (a release there selects nothing), the eight favourite
/// wedges, the six school wedges, and past the rim the fan of the hovered school's own spells.</para>
/// </summary>
public static class SpellWheelRules
{
    /// <summary>A press shorter than this is a tap.</summary>
    public const float TapSeconds = 0.16f;

    /// <summary>Radius of the dead zone at the centre.</summary>
    public const float DeadZone = 0.18f;

    /// <summary>Where the favourites end and the schools begin.</summary>
    public const float InnerEdge = 0.58f;

    /// <summary>The rim of the school ring.</summary>
    public const float OuterEdge = 1f;

    /// <summary>The outer edge of the fan, and as far as the cursor can go.</summary>
    public const float FanEdge = 1.35f;

    public const int FavouriteWedges = SpellFavouritesRules.SlotCount;

    /// <summary>The least angle a spell gets in a fan; a school with many spells fans wider than its wedge.</summary>
    public const float MinFanSpellDegrees = 20f;

    /// <summary>Stick deflection under this is the dead zone.</summary>
    public const float StickDead = 0.25f;

    /// <summary>Stick deflection over this reaches the schools and the fan.</summary>
    public const float StickOuter = 0.7f;

    /// <summary>Wheel units per pixel of mouse travel: the default <c>scale</c> for <see cref="StepCursor"/>.</summary>
    public const float MouseScale = 1f / 150f;

    /// <summary>The schools in wheel order, clockwise from the top.</summary>
    public static readonly IReadOnlyList<DamageType> Schools = new[]
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
    };

    /// <summary>The angle of one school's wedge.</summary>
    public static float SchoolWedgeDegrees => 360f / Schools.Count;

    /// <summary>
    /// Tap or hold. A tap is a press under <see cref="TapSeconds"/> whose cursor stayed inside the
    /// dead zone; anything longer, or anything that moved the cursor out, is the wheel.
    /// </summary>
    public static SpellWheelGesture Classify(float heldSeconds, float cursorTravel) =>
        heldSeconds < TapSeconds && cursorTravel < DeadZone ? SpellWheelGesture.Tap : SpellWheelGesture.Wheel;

    /// <summary>
    /// What <paramref name="cursor"/> is over. <paramref name="latchedSchool"/> is the school whose
    /// fan is open (-1 for none); feed the result through <see cref="Latch"/> to get the next one.
    /// </summary>
    public static SpellWheelPick Pick(Vector2 cursor, SpellWheelLayout layout, int latchedSchool)
    {
        float radius = cursor.Length();
        if (radius < DeadZone)
        {
            return SpellWheelPick.None;
        }

        float angle = AngleDegrees(cursor);
        if (radius < InnerEdge)
        {
            int slot = Wedge(angle, FavouriteWedges);
            string id = slot < layout.Favourites.Count ? layout.Favourites[slot] ?? string.Empty : string.Empty;
            return new SpellWheelPick(SpellWheelPickKind.Favourite, slot, id);
        }

        if (radius > OuterEdge && latchedSchool >= 0 && latchedSchool < layout.SchoolSpells.Count)
        {
            IReadOnlyList<string> spells = layout.SchoolSpells[latchedSchool];
            int index = FanIndex(angle, latchedSchool, spells.Count);
            if (index >= 0)
            {
                return new SpellWheelPick(SpellWheelPickKind.Spell, index, spells[index], latchedSchool);
            }
        }

        // In the ring, or past the rim with no fan under the cursor: the school at this angle.
        int school = Wedge(angle, Schools.Count);
        return new SpellWheelPick(SpellWheelPickKind.School, school, School: school);
    }

    /// <summary>The school whose fan stays open after <paramref name="pick"/>: the hovered school, the
    /// school of the hovered spell, or none once the cursor is back inside the schools.</summary>
    public static int Latch(SpellWheelPick pick) =>
        pick.Kind is SpellWheelPickKind.School or SpellWheelPickKind.Spell ? pick.School : -1;

    /// <summary>The cursor after <paramref name="mouseDelta"/> pixels of mouse travel, kept inside the fan's edge.</summary>
    public static Vector2 StepCursor(Vector2 cursor, Vector2 mouseDelta, float scale)
    {
        Vector2 moved = cursor + (mouseDelta * scale);
        float length = moved.Length();
        return length > FanEdge ? moved * (FanEdge / length) : moved;
    }

    /// <summary>
    /// The cursor a stick deflection means. Under <see cref="StickDead"/> it rests at the centre; up
    /// to <see cref="StickOuter"/> it sits in the middle of the favourites; past that it sweeps from
    /// the inner edge of the schools out to the edge of the fan at a full push.
    /// </summary>
    public static Vector2 FromStick(Vector2 vector)
    {
        float length = vector.Length();
        if (length < StickDead)
        {
            return Vector2.Zero;
        }

        Vector2 direction = vector / length;
        if (length <= StickOuter)
        {
            return direction * ((DeadZone + InnerEdge) * 0.5f);
        }

        float push = (MathF.Min(length, 1f) - StickOuter) / (1f - StickOuter);
        return direction * (InnerEdge + ((FanEdge - InnerEdge) * push));
    }

    /// <summary>Degrees clockwise from straight up, 0 to 360.</summary>
    public static float AngleDegrees(Vector2 cursor)
    {
        float degrees = MathF.Atan2(cursor.X, -cursor.Y) * (180f / MathF.PI);
        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>Which of <paramref name="count"/> equal wedges <paramref name="angleDegrees"/> falls
    /// in, wedge 0 centred straight up.</summary>
    public static int Wedge(float angleDegrees, int count)
    {
        float size = 360f / count;
        float shifted = (angleDegrees + (size * 0.5f)) % 360f;
        return Math.Clamp((int)(shifted / size), 0, count - 1);
    }

    /// <summary>How wide a school's fan is for <paramref name="spellCount"/> spells: its own wedge, or
    /// wider when that would give a spell less than <see cref="MinFanSpellDegrees"/>.</summary>
    public static float FanSpanDegrees(int spellCount) =>
        Math.Clamp(spellCount * MinFanSpellDegrees, SchoolWedgeDegrees, 360f);

    /// <summary>Which spell of <paramref name="school"/>'s fan sits at <paramref name="angleDegrees"/>,
    /// or -1 when the angle is outside the fan.</summary>
    public static int FanIndex(float angleDegrees, int school, int spellCount)
    {
        if (spellCount <= 0)
        {
            return -1;
        }

        float span = FanSpanDegrees(spellCount);
        float offset = ((angleDegrees - (school * SchoolWedgeDegrees) + 540f) % 360f) - 180f;
        if (MathF.Abs(offset) > span * 0.5f)
        {
            return -1;
        }

        return Math.Clamp((int)((offset + (span * 0.5f)) / (span / spellCount)), 0, spellCount - 1);
    }
}
