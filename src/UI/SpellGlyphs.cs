using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spells' icons: one code-drawn stroke glyph per spell, a few polylines on a
/// <see cref="Grid"/>-unit square, drawn over a disc in the school's colour. No textures.
///
/// <para>Seam: no glyph is authored yet, so <see cref="Has"/> is false for every spell and
/// <see cref="Draw"/> draws nothing.</para>
/// </summary>
public static class SpellGlyphs
{
    /// <summary>The side of the square a glyph is authored on; (0,0) is its top-left corner.</summary>
    public const float Grid = 24f;

    /// <summary>Whether <paramref name="spellId"/> has a glyph.</summary>
    public static bool Has(string spellId) => Strokes(spellId).Count > 0;

    /// <summary>The glyph's polylines in grid units, or none for a spell without one.</summary>
    public static IReadOnlyList<Vector2[]> Strokes(string spellId) => Array.Empty<Vector2[]>();

    /// <summary>
    /// Draws <paramref name="spellId"/>'s glyph fitted to <paramref name="rect"/>: a disc in
    /// <paramref name="disc"/> with the strokes over it in <paramref name="ink"/>. Call from the
    /// canvas item's own <c>_Draw</c>. A spell with no glyph draws nothing.
    /// </summary>
    public static void Draw(CanvasItem canvas, string spellId, Rect2 rect, Color disc, Color ink)
    {
    }
}
