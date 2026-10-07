using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Embervale.Debugging;

// The parts of the screenshot harnesses that are arithmetic and text: which shots a run selects,
// what a frame's pixels say about it, how a filmstrip is laid out and what the run's manifest
// holds. No engine type is used here, so tests/Embervale.Tests/ShotToolingTests.cs covers all of it.
// The file is named *Shots.cs because the shipping build excludes that pattern.

/// <summary>
/// The <c>--only=name,name</c> selection. A pattern is a shot name, with <c>*</c> (any run of
/// characters) and <c>?</c> (one character) allowed; matching ignores case. No patterns selects
/// every shot.
/// </summary>
public sealed class ShotFilter
{
    private readonly string[] _patterns;

    public ShotFilter(IEnumerable<string>? patterns)
    {
        var kept = new List<string>();
        foreach (string pattern in patterns ?? Array.Empty<string>())
        {
            string trimmed = pattern.Trim();
            if (trimmed.Length > 0)
            {
                kept.Add(trimmed);
            }
        }

        _patterns = kept.ToArray();
    }

    /// <summary>True when the run names shots; false when it takes them all.</summary>
    public bool Active => _patterns.Length > 0;

    public IReadOnlyList<string> Patterns => _patterns;

    public bool Matches(string name)
    {
        if (_patterns.Length == 0)
        {
            return true;
        }

        foreach (string pattern in _patterns)
        {
            if (Glob(pattern, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The patterns that select none of <paramref name="names"/>: a typing mistake that
    /// would otherwise be a run that quietly photographs nothing.</summary>
    public IReadOnlyList<string> Unmatched(IEnumerable<string> names)
    {
        var all = new List<string>(names);
        var missing = new List<string>();
        foreach (string pattern in _patterns)
        {
            if (!all.Exists(name => Glob(pattern, name)))
            {
                missing.Add(pattern);
            }
        }

        return missing;
    }

    /// <summary>Whole-string wildcard match, case-insensitive.</summary>
    public static bool Glob(string pattern, string text)
    {
        int p = 0;
        int t = 0;
        int star = -1;
        int resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' ||
                                       char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t])) &&
                pattern[p] != '*')
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}

/// <summary>
/// What a frame's pixels say, measured on its thumbnail. Channel values are 0..255.
/// </summary>
/// <param name="Mean">Mean brightness.</param>
/// <param name="Std">Standard deviation of brightness.</param>
/// <param name="Min">Darkest pixel's brightness.</param>
/// <param name="Max">Brightest pixel's brightness.</param>
/// <param name="BlackPct">Percent of pixels with every channel under 8.</param>
/// <param name="MagentaPct">Percent of pixels that are the missing-material magenta.</param>
/// <param name="Hash">A hash of the pixels: equal for two frames only when they are identical.</param>
public readonly record struct ShotStats(
    double Mean, double Std, int Min, int Max, double BlackPct, double MagentaPct, ulong Hash)
{
    public const double BlackFlagPct = 97.0;
    public const double MagentaFlagPct = 0.5;
    public const int FlatRange = 3;

    /// <summary>Nothing in the frame: one colour edge to edge.</summary>
    public bool Flat => Max - Min < FlatRange;

    /// <summary>Measures tightly packed RGB8 pixels.</summary>
    public static ShotStats Compute(ReadOnlySpan<byte> rgb, int width, int height)
    {
        int pixels = width * height;
        if (pixels <= 0 || rgb.Length < pixels * 3)
        {
            return new ShotStats(0, 0, 0, 0, 100, 0, 0);
        }

        double sum = 0;
        double squares = 0;
        int min = 255;
        int max = 0;
        int black = 0;
        int magenta = 0;
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < pixels * 3; i += 3)
        {
            byte r = rgb[i];
            byte g = rgb[i + 1];
            byte b = rgb[i + 2];
            int value = (r + g + b) / 3;
            sum += value;
            squares += (double)value * value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            if (r < 8 && g < 8 && b < 8)
            {
                black++;
            }
            else if (r > 230 && g < 60 && b > 230)
            {
                magenta++;
            }

            hash = (hash ^ r) * 1099511628211UL;
            hash = (hash ^ g) * 1099511628211UL;
            hash = (hash ^ b) * 1099511628211UL;
        }

        double mean = sum / pixels;
        double variance = Math.Max(0, (squares / pixels) - (mean * mean));
        return new ShotStats(
            Math.Round(mean, 2), Math.Round(Math.Sqrt(variance), 2), min, max,
            Math.Round(black * 100.0 / pixels, 2), Math.Round(magenta * 100.0 / pixels, 3), hash);
    }

    /// <summary>The things about this frame worth a second look, as short words: <c>flat</c>,
    /// <c>black</c>, <c>magenta</c>, and <c>same_as:&lt;shot&gt;</c> when it is pixel-identical to the
    /// shot before it (a drive that changed nothing on screen).</summary>
    public List<string> Flags(ShotStats? previous, string? previousName)
    {
        var flags = new List<string>();
        if (Flat)
        {
            flags.Add("flat");
        }

        if (BlackPct >= BlackFlagPct)
        {
            flags.Add("black");
        }

        if (MagentaPct >= MagentaFlagPct)
        {
            flags.Add("magenta");
        }

        if (previous is { } before && previousName != null && before.Hash == Hash)
        {
            flags.Add("same_as:" + previousName);
        }

        return flags;
    }
}

/// <summary>
/// A filmstrip: frames laid left to right, top to bottom, in one image. Each cell has a progress
/// bar along its top edge whose filled part says how far through the strip the frame is.
/// </summary>
public readonly record struct FilmLayout(int Frames, int Columns, int Rows, int CellWidth, int CellHeight, int Gutter)
{
    public const int DefaultFrames = 12;
    public const int DefaultStride = 4;
    public const int MaxFrames = 48;
    public const int MaxStride = 120;
    public const int BarHeight = 4;

    public int Width => (Columns * CellWidth) + ((Columns + 1) * Gutter);

    public int Height => (Rows * CellHeight) + ((Rows + 1) * Gutter);

    public static FilmLayout For(
        int frames, int sourceWidth, int sourceHeight, int cellWidth = 320, int maxColumns = 4, int gutter = 2)
    {
        frames = Math.Max(1, frames);
        int columns = Math.Min(frames, Math.Max(1, maxColumns));
        int rows = (frames + columns - 1) / columns;
        int cellHeight = sourceWidth > 0 ? Math.Max(1, (int)Math.Round(sourceHeight * (double)cellWidth / sourceWidth)) : cellWidth;
        return new FilmLayout(frames, columns, rows, cellWidth, cellHeight, gutter);
    }

    /// <summary>The top-left pixel of a frame's cell.</summary>
    public (int X, int Y) Cell(int index) =>
        (Gutter + ((index % Columns) * (CellWidth + Gutter)), Gutter + ((index / Columns) * (CellHeight + Gutter)));

    /// <summary>Width in pixels of the filled part of a frame's progress bar.</summary>
    public int BarWidth(int index) =>
        Frames <= 1 ? CellWidth : Math.Max(1, (int)Math.Round(CellWidth * (index + 1) / (double)Frames));

    /// <summary>What to say about a film that came out shorter than asked, or null when it did
    /// not: "4 of 16 frames".</summary>
    public static string? Shortfall(int captured, int asked) =>
        captured < asked ? $"{captured} of {asked} frames" : null;

    /// <summary>Reads <c>--film</c>'s value: <c>FRAMESxSTRIDE</c> (e.g. <c>16x3</c>), just
    /// <c>FRAMES</c>, or nothing for 12 frames 4 apart. False when the text is neither.</summary>
    public static bool TryParse(string? text, out int frames, out int stride)
    {
        frames = DefaultFrames;
        stride = DefaultStride;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] parts = text.Trim().ToLowerInvariant().Split('x');
        if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) ||
            (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out stride)))
        {
            frames = DefaultFrames;
            stride = DefaultStride;
            return false;
        }

        frames = Math.Clamp(f, 2, MaxFrames);
        stride = Math.Clamp(stride, 1, MaxStride);
        return true;
    }
}

/// <summary>One shot's entry in the run's manifest.</summary>
public sealed class ShotRecord
{
    public ShotRecord(string name, bool selected)
    {
        Name = name;
        Selected = selected;
    }

    public string Name { get; }

    /// <summary>False for a shot the run's <c>--only</c> left out: it was driven, not photographed.</summary>
    public bool Selected { get; }

    public string? File { get; set; }

    public string? Thumb { get; set; }

    public string? Film { get; set; }

    /// <summary>"4 of 16 frames" when the film holds fewer frames than were asked for (only the
    /// frames between the shot's start and its capture are kept), else null.</summary>
    public string? FilmShort { get; set; }

    public string? Problem { get; set; }

    public long Frame { get; set; }

    public ShotStats? Stats { get; set; }

    public List<string> Flags { get; } = new();

    /// <summary>Photographed and nothing failed. A shot left out of the run is neither ok nor failed.</summary>
    public bool Ok => Selected && Problem == null && File != null;
}

/// <summary>
/// The run's manifest: one entry per shot with its file, checks and pixel measurements, written as
/// <c>manifest.json</c> next to the images so a run is read from one small file instead of a log.
/// </summary>
/// <summary>The effect tier names a run may ask for (<c>--vfxperf=a,b,c</c>,
/// <c>EMBERVALE_SPELLSHOTS_TIER</c>).</summary>
public static class EffectTierNames
{
    public static readonly string[] All = Enum.GetNames<Embervale.Magic.Vfx.VfxTier>();

    /// <summary>The first name that is not a tier (a number is not a name), or null when all are.</summary>
    public static string? FirstUnknown(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (!Array.Exists(All, tier => string.Equals(tier, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return null;
    }
}

public sealed class ShotManifest
{
    public const int Schema = 1;
    public const string FileName = "manifest.json";

    public ShotManifest(string suite)
    {
        Suite = suite;
    }

    public string Suite { get; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double UiScale { get; set; } = 1.0;

    public IReadOnlyList<string> Only { get; set; } = Array.Empty<string>();

    public List<ShotRecord> Shots { get; } = new();

    /// <summary>Failures that belong to the run and not to one shot.</summary>
    public List<string> Failures { get; } = new();

    /// <summary>Shots during which the window lost focus.</summary>
    public List<string> FocusLost { get; } = new();

    public ShotRecord Add(string name, bool selected)
    {
        var record = new ShotRecord(name, selected);
        Shots.Add(record);
        return record;
    }

    public int Captured => Shots.FindAll(shot => shot.File != null).Count;

    public List<string> Failed => Shots.FindAll(shot => shot.Selected && shot.Problem != null).ConvertAll(shot => shot.Name);

    public List<string> Flagged => Shots.FindAll(shot => shot.Flags.Count > 0).ConvertAll(shot => shot.Name);

    public bool Passed => Failures.Count == 0 && Failed.Count == 0;

    public string ToJson(double seconds)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", Schema);
            json.WriteString("suite", Suite);
            json.WriteBoolean("ok", Passed);
            json.WriteNumber("seconds", Math.Round(seconds, 2));
            json.WriteStartArray("resolution");
            json.WriteNumberValue(Width);
            json.WriteNumberValue(Height);
            json.WriteEndArray();
            json.WriteNumber("ui_scale", UiScale);
            WriteStrings(json, "only", Only);
            json.WriteNumber("registered", Shots.Count);
            json.WriteNumber("captured", Captured);
            WriteStrings(json, "failed", Failed);
            WriteStrings(json, "flagged", Flagged);
            WriteStrings(json, "focus_lost", FocusLost);
            WriteStrings(json, "failures", Failures);
            json.WriteStartArray("shots");
            foreach (ShotRecord shot in Shots)
            {
                json.WriteStartObject();
                json.WriteString("name", shot.Name);
                json.WriteBoolean("selected", shot.Selected);
                json.WriteBoolean("ok", shot.Ok);
                if (shot.File != null)
                {
                    json.WriteString("file", shot.File);
                    json.WriteNumber("frame", shot.Frame);
                }

                if (shot.Thumb != null)
                {
                    json.WriteString("thumb", shot.Thumb);
                }

                if (shot.Film != null)
                {
                    json.WriteString("film", shot.Film);
                }

                if (shot.FilmShort != null)
                {
                    json.WriteString("film_short", shot.FilmShort);
                }

                if (shot.Problem != null)
                {
                    json.WriteString("problem", shot.Problem);
                }

                if (shot.Flags.Count > 0)
                {
                    WriteStrings(json, "flags", shot.Flags);
                }

                if (shot.Stats is { } stats)
                {
                    json.WriteStartObject("stats");
                    json.WriteNumber("mean", stats.Mean);
                    json.WriteNumber("std", stats.Std);
                    json.WriteNumber("min", stats.Min);
                    json.WriteNumber("max", stats.Max);
                    json.WriteNumber("black_pct", stats.BlackPct);
                    json.WriteNumber("magenta_pct", stats.MagentaPct);
                    json.WriteString("hash", stats.Hash.ToString("x16", CultureInfo.InvariantCulture));
                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteStrings(Utf8JsonWriter json, string key, IEnumerable<string> values)
    {
        json.WriteStartArray(key);
        foreach (string value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }
}
