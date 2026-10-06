using System;
using System.Collections.Generic;
using Embervale.Combat;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spells' icons: one code-drawn stroke glyph per spell, a few polylines on a
/// <see cref="Grid"/>-unit square, drawn over a disc in the school's colour. No textures.
///
/// <para>Every player-learnable spell has a glyph of its own (<c>SpellGlyphTests</c> reads
/// <c>data/spells</c> and fails on one without). A spell with none (an enemy's breath, a spell added
/// later) draws <see cref="Fallback"/>, and each school has an <see cref="Emblem"/>.</para>
///
/// <para>The disc is the circle inscribed in the grid, so a glyph keeps its points inside that
/// circle. The tables are plain arrays of points: nothing here touches the engine until
/// <see cref="Draw"/> is called, which is what lets the tests read them.</para>
/// </summary>
public static class SpellGlyphs
{
    /// <summary>The side of the square a glyph is authored on; (0,0) is its top-left corner.</summary>
    public const float Grid = 24f;

    /// <summary>The fewest and the most polylines a spell's glyph is made of.</summary>
    public const int MinStrokes = 3;
    public const int MaxStrokes = 8;

    /// <summary>Stroke width as a share of the drawn glyph's side.</summary>
    public const float StrokeShare = 1.7f / Grid;

    private static readonly Vector2[][] None = Array.Empty<Vector2[]>();

    /// <summary>The glyph of a spell that has none of its own: a cut gem.</summary>
    public static IReadOnlyList<Vector2[]> Fallback { get; } = new[]
    {
        L(12f, 4f, 17.5f, 12f, 12f, 20f, 6.5f, 12f, 12f, 4f),
        L(6.5f, 12f, 17.5f, 12f),
        L(12f, 4f, 12f, 20f),
    };

    private static readonly Dictionary<string, Vector2[][]> Glyphs = new(StringComparer.Ordinal)
    {
        // --- Fire -------------------------------------------------------------------------------
        // A whip of flame curling over at its tip, throwing sparks.
        ["spell.emberlash"] = new[]
        {
            L(5f, 19f, 8f, 14.5f, 12.5f, 13f, 16f, 9.5f, 15.5f, 6.5f, 12.5f, 6f, 11.5f, 8.5f),
            L(18f, 6f, 19.5f, 4.5f),
            L(17.5f, 11f, 20f, 11f),
            L(8.5f, 9.5f, 7f, 7.5f),
        },

        // A spear thrown point first, fire streaming off the shaft.
        ["spell.flame_lance"] = new[]
        {
            L(5f, 19f, 14f, 10f),
            L(12.5f, 8.5f, 19.5f, 4.5f, 15.5f, 11.5f, 12.5f, 8.5f),
            L(8.5f, 11.5f, 6.5f, 9.5f),
            L(12.5f, 15.5f, 14.5f, 17.5f),
        },

        // A line of flame standing on the ground it was drawn on.
        ["spell.pyre_wall"] = new[]
        {
            L(4f, 15.5f, 6.5f, 9f, 9f, 14f, 12f, 5f, 15f, 14f, 17.5f, 9f, 20f, 15.5f),
            L(4f, 19f, 20f, 19f),
            L(7.5f, 16.5f, 16.5f, 16.5f),
        },

        // A sun coming down.
        ["spell.sunfall"] = new[]
        {
            Ring(12f, 8f, 3.5f, 12),
            L(12f, 13.5f, 12f, 18f),
            L(8.5f, 12f, 6.5f, 16.5f),
            L(15.5f, 12f, 17.5f, 16.5f),
            L(7f, 20.5f, 17f, 20.5f),
        },

        // --- Frost ------------------------------------------------------------------------------
        // One long splinter of ice, with the glint off its face.
        ["spell.rime_shard"] = new[]
        {
            L(6f, 18f, 10f, 10f, 18f, 6f, 14f, 14f, 6f, 18f),
            L(6f, 18f, 18f, 6f),
            L(15.5f, 17.5f, 19f, 17.5f),
            L(17.25f, 15.75f, 17.25f, 19.25f),
        },

        // A ring of cold bursting outward from where the caster stands.
        ["spell.frost_nova"] = new[]
        {
            Ring(12f, 12f, 3f, 10),
            L(12f, 7f, 12f, 3.5f),
            L(16.3f, 9.5f, 19.4f, 7.75f),
            L(16.3f, 14.5f, 19.4f, 16.25f),
            L(12f, 17f, 12f, 20.5f),
            L(7.7f, 14.5f, 4.6f, 16.25f),
            L(7.7f, 9.5f, 4.6f, 7.75f),
        },

        // A cloud and what falls out of it.
        ["spell.blizzard"] = new[]
        {
            L(5f, 11f, 6f, 8f, 9f, 6.5f, 12f, 7.5f, 14.5f, 5.5f, 18f, 7f, 19f, 11f, 5f, 11f),
            L(7.5f, 13.5f, 6f, 17.5f),
            L(12.5f, 13.5f, 11f, 19f),
            L(17f, 13.5f, 15.5f, 17.5f),
        },

        // Three pillars of ice shoulder to shoulder.
        ["spell.glacial_bulwark"] = new[]
        {
            L(4.5f, 19f, 4.5f, 11.5f, 6.75f, 8.5f, 9f, 11.5f, 9f, 19f),
            L(9f, 19f, 9f, 8f, 12f, 4.5f, 15f, 8f, 15f, 19f),
            L(15f, 19f, 15f, 11.5f, 17.25f, 8.5f, 19.5f, 11.5f, 19.5f, 19f),
            L(4f, 19f, 20f, 19f),
        },

        // --- Lightning --------------------------------------------------------------------------
        // A slow orb that spits at whatever it passes.
        ["spell.ball_lightning"] = new[]
        {
            Ring(12f, 12f, 4.5f, 12),
            L(12f, 6.5f, 11f, 5f, 12.5f, 3.5f),
            L(17.5f, 12f, 19f, 11f, 20.5f, 12.5f),
            L(12f, 17.5f, 13f, 19f, 11.5f, 20.5f),
            L(6.5f, 12f, 5f, 13f, 3.5f, 11.5f),
        },

        // A held current between two terminals.
        ["spell.storm_conduit"] = new[]
        {
            L(4.5f, 12f, 7.5f, 9f, 10.5f, 15f, 13.5f, 9f, 16.5f, 15f, 19.5f, 12f),
            L(4.5f, 7f, 4.5f, 17f),
            L(19.5f, 7f, 19.5f, 17f),
        },

        // The bolt the caster becomes, and the air it leaves behind.
        ["spell.thunder_step"] = new[]
        {
            L(14f, 3.5f, 7.5f, 13f, 11.5f, 13f, 9.5f, 20.5f, 16.5f, 10.5f, 12.5f, 10.5f, 14f, 3.5f),
            L(16.5f, 15f, 20f, 15f),
            L(15f, 18f, 18.5f, 18f),
        },

        // A mark burned onto a target for the storm to find.
        ["spell.stormbrand"] = new[]
        {
            Ring(12f, 12f, 6.5f, 14),
            L(13.5f, 7.5f, 10f, 12f, 14f, 12f, 10.5f, 16.5f),
            L(12f, 2.5f, 12f, 5.5f),
            L(21.5f, 12f, 18.5f, 12f),
            L(12f, 21.5f, 12f, 18.5f),
            L(2.5f, 12f, 5.5f, 12f),
        },

        // --- Arcane -----------------------------------------------------------------------------
        // A lance through a nought: the spell that unmakes spells.
        ["spell.null_lance"] = new[]
        {
            Ring(12f, 12f, 5f, 12),
            L(4.5f, 19.5f, 19.5f, 4.5f),
            L(15f, 4.5f, 19.5f, 4.5f, 19.5f, 9f),
        },

        // A shield with a ward cut into its face.
        ["spell.arcane_shield"] = new[]
        {
            L(12f, 3.5f, 18.5f, 6f, 18.5f, 12f, 12f, 20.5f, 5.5f, 12f, 5.5f, 6f, 12f, 3.5f),
            L(8.5f, 8.5f, 12f, 11.5f, 15.5f, 8.5f),
            L(9.5f, 12.5f, 12f, 15f, 14.5f, 12.5f),
        },

        // Here, a leap, and there.
        ["spell.blink"] = new[]
        {
            Ring(6.5f, 16f, 2.5f, 8),
            L(7.5f, 12.5f, 9.5f, 8.5f, 13.5f, 6.5f, 17f, 8f),
            L(16.5f, 4.5f, 17f, 8f, 13.5f, 9.5f),
            L(17f, 12.5f, 17f, 19f),
            L(13.75f, 15.75f, 20.25f, 15.75f),
        },

        // Everything nearby, turning inward.
        ["spell.gravity_well"] = new[]
        {
            Spiral(12f, 12f, 6.5f, 1.2f, 2.25f, 22),
            L(2.5f, 10f, 4.5f, 12f, 2.5f, 14f),
            L(21.5f, 10f, 19.5f, 12f, 21.5f, 14f),
        },

        // --- Nature -----------------------------------------------------------------------------
        // A flower opening.
        ["spell.mending_bloom"] = new[]
        {
            Ring(12f, 10f, 2f, 8),
            L(10.5f, 7.5f, 12f, 3.5f, 13.5f, 7.5f),
            L(9.5f, 8.75f, 6f, 10f, 9.5f, 11.25f),
            L(14.5f, 8.75f, 18f, 10f, 14.5f, 11.25f),
            L(12f, 12.5f, 12f, 20.5f),
            L(12f, 17.5f, 15.5f, 15f, 16f, 17.5f, 12f, 17.5f),
        },

        // A carved post with a bloom on its head, pulsing.
        ["spell.lifebloom_totem"] = new[]
        {
            L(10f, 20f, 10f, 10f, 14f, 10f, 14f, 20f),
            L(7.5f, 20f, 16.5f, 20f),
            L(10f, 14.5f, 14f, 14.5f),
            L(12f, 10f, 9f, 6.5f, 12f, 3.5f, 15f, 6.5f, 12f, 10f),
            L(6.5f, 9f, 5f, 12f, 6.5f, 15f),
            L(17.5f, 9f, 19f, 12f, 17.5f, 15f),
        },

        // Two brambles crossing, thorns out.
        ["spell.thornsnare"] = new[]
        {
            L(5f, 19f, 9f, 14f, 11f, 10f, 17f, 5f),
            L(19f, 19f, 15f, 14f, 13f, 10f, 7f, 5f),
            L(9f, 14f, 6.5f, 13.5f),
            L(15f, 14f, 17.5f, 13.5f),
            L(14.5f, 7f, 14.5f, 4.5f),
            L(9.5f, 7f, 9.5f, 4.5f),
        },

        // A cloud of small things with stings.
        ["spell.stinging_swarm"] = new[]
        {
            L(5f, 8f, 7.5f, 10f, 10f, 8f),
            L(7.5f, 10f, 7.5f, 12.5f),
            L(13f, 6f, 15.5f, 8f, 18f, 6f),
            L(15.5f, 8f, 15.5f, 10.5f),
            L(8f, 15f, 10.5f, 17f, 13f, 15f),
            L(10.5f, 17f, 10.5f, 19.5f),
            L(15f, 13f, 17f, 15f, 19f, 13f),
        },

        // A trunk: two sides, the grain, and the stub of a bough.
        ["spell.barkskin"] = new[]
        {
            L(8f, 3.5f, 7f, 20.5f),
            L(16f, 3.5f, 17f, 20.5f),
            L(10f, 6f, 10.5f, 11f),
            L(13.5f, 8.5f, 13f, 14.5f),
            L(10.5f, 14.5f, 11f, 19f),
            L(16.3f, 9.5f, 19.5f, 7f),
        },

        // --- Necrotic ---------------------------------------------------------------------------
        // A life, and the thread of it drawn back to the caster.
        ["spell.ember_siphon"] = new[]
        {
            Ring(16.5f, 8f, 3.5f, 10),
            L(13.5f, 10.5f, 10.5f, 11.5f, 8.5f, 14f, 7.5f, 18f),
            L(5f, 15.5f, 7.5f, 18.5f, 10.75f, 16.5f),
        },

        // A soul, weighed.
        ["spell.soul_tithe"] = new[]
        {
            L(8f, 17.5f, 8f, 10f, 9.5f, 6f, 12f, 4.5f, 14.5f, 6f, 16f, 10f, 16f, 17.5f, 14f, 15.5f, 12f, 17.5f,
                10f, 15.5f, 8f, 17.5f),
            L(10.5f, 9.5f, 10.5f, 11.5f),
            L(13.5f, 9.5f, 13.5f, 11.5f),
            L(6f, 20.5f, 18f, 20.5f),
        },

        // A bone, stitched across its break.
        ["spell.knit_bone"] = new[]
        {
            L(6f, 18f, 18f, 6f),
            L(4.5f, 16.5f, 7.5f, 19.5f),
            L(16.5f, 4.5f, 19.5f, 7.5f),
            L(9.75f, 11.75f, 12.25f, 14.25f),
            L(11.75f, 9.75f, 14.25f, 12.25f),
        },

        // A headstone with a name already on it.
        ["spell.grave_mark"] = new[]
        {
            L(7f, 20f, 7f, 9f, 8.5f, 6f, 12f, 4.5f, 15.5f, 6f, 17f, 9f, 17f, 20f),
            L(4.5f, 20f, 19.5f, 20f),
            L(12f, 9f, 12f, 16f),
            L(9.5f, 11.5f, 14.5f, 11.5f),
        },
    };

    private static readonly Dictionary<DamageType, Vector2[][]> Emblems = new()
    {
        // A flame with its heart.
        [DamageType.Fire] = new[]
        {
            L(12f, 3.5f, 16.5f, 9.5f, 17.5f, 14f, 15.5f, 18.5f, 12f, 20.5f, 8.5f, 18.5f, 6.5f, 14f, 8.5f, 10f,
                10f, 12.5f, 12f, 3.5f),
            L(12f, 12.5f, 14f, 15.5f, 12f, 18f, 10f, 15.5f, 12f, 12.5f),
        },

        // A snowflake.
        [DamageType.Frost] = new[]
        {
            L(12f, 3.5f, 12f, 20.5f),
            L(4.6f, 7.75f, 19.4f, 16.25f),
            L(4.6f, 16.25f, 19.4f, 7.75f),
            L(10f, 5.5f, 12f, 7.5f, 14f, 5.5f),
            L(10f, 18.5f, 12f, 16.5f, 14f, 18.5f),
        },

        // A bolt.
        [DamageType.Lightning] = new[]
        {
            L(14.5f, 3.5f, 8f, 12.5f, 13f, 12.5f, 9.5f, 20.5f),
            L(16.5f, 15f, 19.5f, 15f),
            L(4.5f, 9f, 7.5f, 9f),
        },

        // A sigil: a triangle in a circle, with an eye.
        [DamageType.Arcane] = new[]
        {
            Ring(12f, 12f, 8.5f, 16),
            L(12f, 4.5f, 18.5f, 15.75f, 5.5f, 15.75f, 12f, 4.5f),
            Ring(12f, 12f, 1.5f, 6),
        },

        // A leaf.
        [DamageType.Nature] = new[]
        {
            L(12f, 3.5f, 17.5f, 9f, 17f, 15f, 12f, 20.5f, 7f, 15f, 6.5f, 9f, 12f, 3.5f),
            L(12f, 6.5f, 12f, 20.5f),
            L(12f, 11.5f, 15f, 9f),
            L(12f, 15f, 9f, 12.5f),
        },

        // A skull.
        [DamageType.Necrotic] = new[]
        {
            L(7f, 13f, 7f, 8.5f, 9f, 5.5f, 12f, 4.5f, 15f, 5.5f, 17f, 8.5f, 17f, 13f, 15f, 15f, 15f, 18.5f,
                9f, 18.5f, 9f, 15f, 7f, 13f),
            L(9.25f, 10.5f, 11f, 10.5f),
            L(13f, 10.5f, 14.75f, 10.5f),
            L(12f, 16f, 12f, 18.5f),
        },
    };

    /// <summary>Every spell id that has a glyph of its own.</summary>
    public static IReadOnlyCollection<string> Ids => Glyphs.Keys;

    /// <summary>Whether <paramref name="spellId"/> has a glyph of its own.</summary>
    public static bool Has(string spellId) => Strokes(spellId).Count > 0;

    /// <summary>The glyph's polylines in grid units, or none for a spell without one.</summary>
    public static IReadOnlyList<Vector2[]> Strokes(string spellId) =>
        spellId != null && Glyphs.TryGetValue(spellId, out Vector2[][]? strokes) ? strokes : None;

    /// <summary>The school's emblem in grid units, or <see cref="Fallback"/> for a damage type that
    /// is not a school of magic.</summary>
    public static IReadOnlyList<Vector2[]> Emblem(DamageType school) =>
        Emblems.TryGetValue(school, out Vector2[][]? strokes) ? strokes : Fallback;

    /// <summary>
    /// Draws <paramref name="spellId"/>'s glyph fitted to <paramref name="rect"/>: a disc in
    /// <paramref name="disc"/> with the strokes over it in <paramref name="ink"/>. Call from the
    /// canvas item's own <c>_Draw</c>. A spell with no glyph of its own draws <see cref="Fallback"/>.
    /// </summary>
    public static void Draw(CanvasItem canvas, string spellId, Rect2 rect, Color disc, Color ink)
    {
        IReadOnlyList<Vector2[]> strokes = Strokes(spellId);
        DrawStrokes(canvas, strokes.Count > 0 ? strokes : Fallback, rect, disc, ink);
    }

    /// <summary>Draws <paramref name="school"/>'s emblem the way <see cref="Draw"/> draws a spell.
    /// A fully transparent <paramref name="disc"/> draws the strokes alone.</summary>
    public static void DrawEmblem(CanvasItem canvas, DamageType school, Rect2 rect, Color disc, Color ink) =>
        DrawStrokes(canvas, Emblem(school), rect, disc, ink);

    /// <summary>Draws a set of grid-unit polylines fitted to <paramref name="rect"/> over a disc.
    /// The disc is skipped when its colour is fully transparent. <paramref name="widthScale"/>
    /// thickens the strokes: a first pass in a dark colour at about twice the width keylines a
    /// glyph that sits on the world rather than on a disc.</summary>
    public static void DrawStrokes(
        CanvasItem canvas, IReadOnlyList<Vector2[]> strokes, Rect2 rect, Color disc, Color ink, float widthScale = 1f)
    {
        float side = Mathf.Min(rect.Size.X, rect.Size.Y);
        if (side <= 0f)
        {
            return;
        }

        Vector2 centre = rect.Position + (rect.Size * 0.5f);
        if (disc.A > 0f)
        {
            canvas.DrawCircle(centre, side * 0.5f, disc, true, -1f, true);
        }

        // The glyph is fitted to the square inside the rect, so a wide rect does not stretch it.
        float scale = side / Grid;
        Vector2 origin = centre - new Vector2(side * 0.5f, side * 0.5f);
        float width = Mathf.Max(1.5f, side * StrokeShare) * widthScale;
        for (int s = 0; s < strokes.Count; s++)
        {
            Vector2[] stroke = strokes[s];
            if (stroke.Length < 2)
            {
                continue;
            }

            var points = new Vector2[stroke.Length];
            for (int i = 0; i < stroke.Length; i++)
            {
                points[i] = origin + (stroke[i] * scale);
            }

            canvas.DrawPolyline(points, ink, width, true);
        }
    }

    // --- Authoring helpers (pure) -------------------------------------------------------------

    /// <summary>A polyline from x, y pairs.</summary>
    private static Vector2[] L(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[i * 2], xy[(i * 2) + 1]);
        }

        return points;
    }

    /// <summary>A closed circle of <paramref name="segments"/> chords.</summary>
    private static Vector2[] Ring(float x, float y, float radius, int segments)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = MathF.Tau * i / segments;
            points[i] = new Vector2(x + (MathF.Sin(angle) * radius), y - (MathF.Cos(angle) * radius));
        }

        return points;
    }

    /// <summary>A spiral winding inward from <paramref name="outer"/> to <paramref name="inner"/>
    /// over <paramref name="turns"/> turns, starting at the left.</summary>
    private static Vector2[] Spiral(float x, float y, float outer, float inner, float turns, int segments)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = MathF.PI + (MathF.Tau * turns * t);
            float radius = outer + ((inner - outer) * t);
            points[i] = new Vector2(x + (MathF.Cos(angle) * radius), y + (MathF.Sin(angle) * radius));
        }

        return points;
    }
}
