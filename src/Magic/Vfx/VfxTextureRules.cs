using System;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The pictures the spell effects are built from, as functions of a pixel: there are no image files.
/// Every function takes <c>u, v</c> in 0..1 across the texture and returns 0..1. Pure, so the
/// properties the shaders lean on are tests: the noise really tiles, a mask really reaches zero at
/// its border (a sprite that does not is a visible square), and nothing leaves 0..1.
/// <see cref="VfxTextures"/> turns these into <c>ImageTexture</c>s.
/// </summary>
public static class VfxTextureRules
{
    private const float Tau = MathF.PI * 2f;

    /// <summary>A repeatable 0..1 value for a lattice point.</summary>
    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Value noise on a lattice that wraps every <paramref name="period"/> cells.</summary>
    public static float TileNoise(float x, float y, int period, int seed)
    {
        period = Math.Max(1, period);
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        fx = fx * fx * (3f - (2f * fx));
        fy = fy * fy * (3f - (2f * fy));

        int xa = Wrap(x0, period);
        int xb = Wrap(x0 + 1, period);
        int ya = Wrap(y0, period);
        int yb = Wrap(y0 + 1, period);
        float top = Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
        float bottom = Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
        return Lerp(top, bottom, fy);
    }

    /// <summary>Fractal noise over the unit square that tiles in both directions.</summary>
    public static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float sum = 0f;
        float weight = 0f;
        float amplitude = 1f;
        int cells = Math.Max(1, period);
        for (int i = 0; i < Math.Max(1, octaves); i++)
        {
            sum += TileNoise(u * cells, v * cells, cells, seed + (i * 17)) * amplitude;
            weight += amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }

        return sum / weight;
    }

    /// <summary>A soft round glow: 1 at the centre, exactly 0 at the border.</summary>
    public static float Dot(float u, float v)
    {
        float r = Radius(u, v);
        float edge = Math.Clamp(1f - r, 0f, 1f);
        return edge * edge * (3f - (2f * edge)) * edge;
    }

    /// <summary>A thin streak along V, for sparks and lens streaks.</summary>
    public static float Streak(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float across = MathF.Exp(-(x * x) / 0.035f);
        float along = Math.Clamp(1f - MathF.Abs(y), 0f, 1f);
        float border = Math.Clamp((1f - MathF.Abs(x)) * 6f, 0f, 1f);
        return Math.Clamp(across * along * MathF.Sqrt(along) * border, 0f, 1f);
    }

    /// <summary>A hard-edged splinter: a diamond, longer than it is wide.</summary>
    public static float Shard(float u, float v)
    {
        float x = MathF.Abs(u - 0.5f) * 2f;
        float y = MathF.Abs(v - 0.5f) * 2f;
        float inside = 0.94f - ((x * 2.1f) + y);
        return Math.Clamp(inside * 7f, 0f, 1f);
    }

    /// <summary>A leaf: a pointed ellipse with a darker midrib.</summary>
    public static float Leaf(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float width = 0.48f * (1f - (y * y));
        float inside = Math.Clamp((width - MathF.Abs(x)) * 9f, 0f, 1f);
        float rib = 0.72f + (0.28f * Math.Clamp(MathF.Abs(x) * 9f, 0f, 1f));
        return inside * rib * Math.Clamp((1f - MathF.Abs(y)) * 8f, 0f, 1f);
    }

    /// <summary>A ragged puff of smoke: the soft dot, torn by noise.</summary>
    public static float Puff(float u, float v)
    {
        float r = Radius(u, v);
        float torn = r * (0.75f + (0.6f * Fbm(u, v, 4, 3, 5)));
        float edge = Math.Clamp(1f - torn, 0f, 1f);
        return Math.Clamp(edge * edge * 1.6f, 0f, 1f) * Math.Clamp((1f - r) * 5f, 0f, 1f);
    }

    /// <summary>A rune circle: two rings with tick marks between them, an inner ring and a hexagram.</summary>
    public static float Rune(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        if (r >= 0.985f)
        {
            return 0f;
        }

        float angle = MathF.Atan2(y, x);
        float value = Line(r, 0.93f, 0.022f) + Line(r, 0.77f, 0.012f) + Line(r, 0.4f, 0.012f);

        // Twenty-four ticks in the band between the two outer rings, every third one long.
        float turn = ((angle / Tau) + 1f) * 24f;
        float tick = MathF.Abs(turn - MathF.Round(turn));
        bool longTick = ((int)MathF.Round(turn)) % 3 == 0;
        float inner = longTick ? 0.79f : 0.84f;
        if (r > inner && r < 0.91f)
        {
            value += Math.Clamp(1f - (tick / 0.1f), 0f, 1f);
        }

        // Two triangles, one turned half a turn: the hexagram.
        value += Line(r, PolygonRadius(angle, 3, 0.74f, 0f), 0.014f);
        value += Line(r, PolygonRadius(angle, 3, 0.74f, MathF.PI), 0.014f);
        return Math.Clamp(value, 0f, 1f);
    }

    /// <summary>How much of a scorch mark covers a pixel: a ragged dark blot.</summary>
    public static float Scorch(float u, float v)
    {
        float r = Radius(u, v);
        float ragged = r * (0.72f + (0.7f * Fbm(u, v, 5, 3, 11)));
        float blot = 1f - Smooth(0.28f, 0.92f, ragged);
        return Math.Clamp(blot * (0.7f + (0.3f * Fbm(u, v, 12, 2, 3))), 0f, 1f) * Border(r);
    }

    /// <summary>The cracks still glowing in a fresh scorch mark.</summary>
    public static float ScorchHeat(float u, float v)
    {
        float r = Radius(u, v);
        float n = Fbm(u, v, 7, 3, 23);
        float crack = Math.Clamp(1f - (MathF.Abs(n - 0.5f) / 0.035f), 0f, 1f);
        return crack * (1f - Smooth(0.15f, 0.7f, r));
    }

    /// <summary>Hoar frost: six crystalline spokes breaking up into noise.</summary>
    public static float Frost(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        float angle = MathF.Atan2(y, x);
        float spokes = MathF.Pow(MathF.Abs(MathF.Cos(angle * 3f)), 10f);
        float feather = MathF.Pow(MathF.Abs(MathF.Cos((angle * 12f) + (r * 9f))), 6f) * 0.6f;
        float n = Fbm(u, v, 9, 3, 31);
        float cover = 1f - Smooth(0.2f, 0.95f, r * (0.8f + (0.4f * n)));
        return Math.Clamp(cover * (0.35f + (0.65f * MathF.Max(MathF.Max(spokes, feather), n))), 0f, 1f) * Border(r);
    }

    /// <summary>Roots cracking outward from a centre: seven wandering arms.</summary>
    public static float Roots(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        float angle = MathF.Atan2(y, x);
        float wander = (Fbm(u, v, 5, 3, 41) - 0.5f) * 1.3f;
        float turn = (((angle / Tau) + 1f) * 7f) + (wander * r * 2f);
        float arm = MathF.Abs(turn - MathF.Round(turn));
        float width = 0.05f + (0.16f * Math.Clamp(1f - r, 0f, 1f));
        float line = Math.Clamp(1f - (arm / width), 0f, 1f);
        float knot = 1f - Smooth(0.05f, 0.22f, r);
        return Math.Clamp(MathF.Max(line, knot) * (1f - Smooth(0.55f, 0.97f, r)), 0f, 1f);
    }

    private static float Radius(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        return MathF.Sqrt((x * x) + (y * y));
    }

    /// <summary>1 inside a mark, falling to 0 at the border of the texture, so a decal has no edge.</summary>
    private static float Border(float r) => 1f - Smooth(0.86f, 0.98f, r);

    private static float Line(float r, float at, float halfWidth) =>
        Math.Clamp(1f - (MathF.Abs(r - at) / halfWidth), 0f, 1f);

    /// <summary>The distance to the edge of a regular polygon along <paramref name="angle"/>.</summary>
    private static float PolygonRadius(float angle, int sides, float circumradius, float turn)
    {
        float sector = Tau / sides;
        float local = (angle + turn + (Tau * 4f)) % sector;
        return circumradius * MathF.Cos(MathF.PI / sides) / MathF.Cos(local - (sector * 0.5f));
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static int Wrap(int value, int period)
    {
        int wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }
}
