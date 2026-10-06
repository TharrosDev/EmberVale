using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Combat;
using Embervale.Magic;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The spells' code-drawn icons. The learnable spells are read straight from <c>data/spells</c>, so a
/// spell added there cannot ship with the fallback gem for a face; and every glyph is held to the
/// grid it is authored on and to the disc it is drawn over.
/// </summary>
public class SpellGlyphTests
{
    private static string Root()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Embervale.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
    }

    /// <summary>Every spell id in <c>data/spells</c> with whether the player can learn it (the
    /// resource's default is that they can).</summary>
    private static List<(string Id, bool Learnable)> LoadSpells()
    {
        var list = new List<(string, bool)>();
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", "spells"), "*.tres"))
        {
            string id = string.Empty;
            bool learnable = true;
            bool inResource = false;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line == "[resource]")
                {
                    inResource = true;
                    continue;
                }

                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (!inResource || eq <= 0)
                {
                    continue;
                }

                string value = line[(eq + 3)..];
                switch (line[..eq])
                {
                    case "Id":
                        id = value.Trim('"');
                        break;
                    case "PlayerLearnable":
                        learnable = value != "false";
                        break;
                }
            }

            Assert.False(string.IsNullOrEmpty(id), $"{Path.GetFileName(file)} has no Id");
            list.Add((id, learnable));
        }

        return list;
    }

    private static IEnumerable<(string Name, IReadOnlyList<Vector2[]> Strokes)> EveryGlyph()
    {
        foreach (string id in SpellGlyphs.Ids)
        {
            yield return (id, SpellGlyphs.Strokes(id));
        }

        foreach (DamageType school in SpellWheelRules.Schools)
        {
            yield return ($"emblem {school}", SpellGlyphs.Emblem(school));
        }

        yield return ("fallback", SpellGlyphs.Fallback);
    }

    [Fact]
    public void EveryLearnableSpell_HasAGlyphOfItsOwn()
    {
        List<string> learnable = LoadSpells().Where(s => s.Learnable).Select(s => s.Id).ToList();
        Assert.Equal(25, learnable.Count);

        List<string> missing = learnable.Where(id => !SpellGlyphs.Has(id)).ToList();
        Assert.True(missing.Count == 0, "Learnable spells with no glyph: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryGlyph_BelongsToALearnableSpell()
    {
        // A glyph for an id nothing has is a spell that was renamed under it.
        HashSet<string> learnable = LoadSpells().Where(s => s.Learnable).Select(s => s.Id).ToHashSet();
        Assert.All(SpellGlyphs.Ids, id => Assert.Contains(id, learnable));
        Assert.Equal(learnable.Count, SpellGlyphs.Ids.Count);
    }

    [Fact]
    public void ASpellsGlyph_IsThreeToEightStrokes()
    {
        foreach (string id in SpellGlyphs.Ids)
        {
            int strokes = SpellGlyphs.Strokes(id).Count;
            Assert.True(
                strokes >= SpellGlyphs.MinStrokes && strokes <= SpellGlyphs.MaxStrokes,
                $"{id} has {strokes} strokes");
        }
    }

    [Fact]
    public void EveryPoint_StaysOnTheGrid_AndInsideTheDisc()
    {
        var middle = new Vector2(SpellGlyphs.Grid * 0.5f, SpellGlyphs.Grid * 0.5f);
        foreach ((string name, IReadOnlyList<Vector2[]> strokes) in EveryGlyph())
        {
            Assert.NotEmpty(strokes);
            foreach (Vector2[] stroke in strokes)
            {
                Assert.True(stroke.Length >= 2, $"{name} has a stroke with fewer than two points");
                foreach (Vector2 point in stroke)
                {
                    Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y), $"{name} has a point that is not a number");
                    Assert.InRange(point.X, 0f, SpellGlyphs.Grid);
                    Assert.InRange(point.Y, 0f, SpellGlyphs.Grid);

                    // The disc is the circle inscribed in the grid; a corner point would hang off it.
                    Assert.True(
                        point.DistanceTo(middle) <= (SpellGlyphs.Grid * 0.5f) - 0.4f,
                        $"{name} has a point at {point} outside the disc");
                }
            }
        }
    }

    [Fact]
    public void EveryStroke_HasLength_AndEveryGlyphFillsItsDisc()
    {
        foreach ((string name, IReadOnlyList<Vector2[]> strokes) in EveryGlyph())
        {
            float left = float.MaxValue, right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
            foreach (Vector2[] stroke in strokes)
            {
                float length = 0f;
                for (int i = 1; i < stroke.Length; i++)
                {
                    float step = stroke[i].DistanceTo(stroke[i - 1]);
                    Assert.True(step > 0.2f, $"{name} repeats a point at {stroke[i]}");
                    length += step;
                }

                Assert.True(length >= 1.4f, $"{name} has a stroke too short to see ({length:0.0})");
                foreach (Vector2 point in stroke)
                {
                    left = MathF.Min(left, point.X);
                    right = MathF.Max(right, point.X);
                    top = MathF.Min(top, point.Y);
                    bottom = MathF.Max(bottom, point.Y);
                }
            }

            // A glyph huddled in a corner of its disc is unreadable at wheel size.
            Assert.True(right - left >= 10f, $"{name} is only {right - left:0.0} wide");
            Assert.True(bottom - top >= 10f, $"{name} is only {bottom - top:0.0} tall");
            Assert.InRange((left + right) * 0.5f, 9.5f, 14.5f);
            Assert.InRange((top + bottom) * 0.5f, 9.5f, 14.5f);
        }
    }

    [Fact]
    public void NoTwoGlyphs_AreTheSameDrawing()
    {
        static string Key(IReadOnlyList<Vector2[]> strokes) =>
            string.Join("|", strokes.Select(s => string.Join(" ", s.Select(p => $"{p.X:0.00},{p.Y:0.00}"))));

        var seen = new Dictionary<string, string>();
        foreach ((string name, IReadOnlyList<Vector2[]> strokes) in EveryGlyph())
        {
            string key = Key(strokes);
            Assert.False(seen.TryGetValue(key, out string? other), $"{name} is the same drawing as {other}");
            seen[key] = name;
        }

        Assert.Equal(25 + 6 + 1, seen.Count);
    }

    [Fact]
    public void ASpellWithNoGlyph_HasNone_AndASchoolAlwaysHasAnEmblem()
    {
        Assert.False(SpellGlyphs.Has("spell.no_such_spell"));
        Assert.Empty(SpellGlyphs.Strokes("spell.no_such_spell"));
        Assert.Empty(SpellGlyphs.Strokes(string.Empty));

        // An enemy-only spell draws the fallback rather than nothing; it has no glyph of its own.
        foreach ((string id, bool learnable) in LoadSpells())
        {
            Assert.Equal(learnable, SpellGlyphs.Has(id));
        }

        foreach (DamageType school in SpellWheelRules.Schools)
        {
            Assert.NotSame(SpellGlyphs.Fallback, SpellGlyphs.Emblem(school));
        }

        Assert.Same(SpellGlyphs.Fallback, SpellGlyphs.Emblem(DamageType.Physical));
    }
}
