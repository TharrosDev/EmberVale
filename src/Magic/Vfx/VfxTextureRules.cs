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

    /// <summary>
    /// A comet: a round bright head at the top of the quad (the end that leads, on a particle that
    /// follows its travel) and a tail that narrows and dims behind it. A flying ember, a lick of
    /// flame, a wisp: what the soft dot was drawn for and read as a ball of bokeh.
    /// </summary>
    public static float Comet(float u, float v)
    {
        const float Head = 0.2f;
        const float Margin = 0.05f;

        // Clear of the top edge of the quad, so the head is round and not cut flat.
        v = (v - Margin) / (1f - Margin);
        if (v <= 0f)
        {
            return 0f;
        }

        float x = MathF.Abs(u - 0.5f) * 2f;
        float width;
        float bright;
        if (v < Head)
        {
            float back = (Head - v) / Head;
            width = 0.5f * MathF.Sqrt(Math.Clamp(1f - (back * back), 0f, 1f));
            bright = 1f;
        }
        else
        {
            float along = Math.Clamp(1f - ((v - Head) / (1f - Head)), 0f, 1f);
            width = 0.5f * along * MathF.Sqrt(along);
            bright = along;
        }

        if (width <= 0.001f)
        {
            return 0f;
        }

        float inside = Math.Clamp(1f - (x / width), 0f, 1f);
        return Math.Clamp(inside * inside * (3f - (2f * inside)) * bright, 0f, 1f);
    }

    /// <summary>A hard-edged splinter: a diamond, longer than it is wide.</summary>
    public static float Shard(float u, float v)
    {
        float x = MathF.Abs(u - 0.5f) * 2f;
        float y = MathF.Abs(v - 0.5f) * 2f;
        float inside = 0.94f - ((x * 2.1f) + y);
        return Math.Clamp(inside * 7f, 0f, 1f);
    }

    /// <summary>
    /// A crystal of ice, long along V and pointed at both ends: a bright rim, a dimmer body and one
    /// facet line down its length, so a thrown shard reads as cut ice rather than a flat diamond.
    /// </summary>
    public static float Crystal(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = MathF.Abs(v - 0.5f) * 2f;
        float width = 0.38f * MathF.Min(1f, (1f - y) * 1.9f);
        float margin = width - MathF.Abs(x);
        float inside = Math.Clamp(margin * 14f, 0f, 1f) * Math.Clamp((0.96f - y) * 20f, 0f, 1f);
        float rim = 1f - Math.Clamp(margin * 6f, 0f, 1f);
        float facet = x > 0f ? 0.22f : 0f;
        return Math.Clamp(inside * (0.45f + (0.55f * MathF.Max(rim, facet))), 0f, 1f);
    }

    /// <summary>A glint: a four-pointed star with a bright heart, for a twinkle of frost or magic.</summary>
    public static float Glint(float u, float v)
    {
        float x = MathF.Abs(u - 0.5f) * 2f;
        float y = MathF.Abs(v - 0.5f) * 2f;
        float reachX = Math.Clamp(1f - x, 0f, 1f);
        float reachY = Math.Clamp(1f - y, 0f, 1f);
        float horizontal = MathF.Exp(-y * 11f) * reachX * reachX;
        float vertical = MathF.Exp(-x * 11f) * reachY * reachY;
        float heart = MathF.Exp(-((x * x) + (y * y)) * 22f);
        float border = Math.Clamp((1f - MathF.Max(x, y)) * 8f, 0f, 1f);
        return Math.Clamp((MathF.Max(horizontal, vertical) * 0.9f) + heart, 0f, 1f) * border;
    }

    /// <summary>
    /// A burst of rays: thin spikes of uneven length thrown out from a bright heart. The flash of a
    /// blast is this, turned and swelling, over its core: structure where a bigger disc would be a
    /// white-out.
    /// </summary>
    public static float Rays(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        if (r >= 1f)
        {
            return 0f;
        }

        // The angle runs -0.5..0.5 of a turn; the noise below wraps on exactly that period, so there
        // is no seam where the angle jumps.
        float turn = MathF.Atan2(y, x) / Tau;
        float spike = TileNoise(turn * 40f, 0.5f, 40, 71);
        spike = spike * spike * spike * spike;
        float length = 0.3f + (0.7f * TileNoise(turn * 20f, 0.5f, 20, 83));
        float along = Math.Clamp(1f - (r / length), 0f, 1f);
        float heart = MathF.Exp(-r * r * 38f);
        float border = Math.Clamp((1f - r) * 6f, 0f, 1f);
        return Math.Clamp((spike * 2.4f * along * MathF.Sqrt(along)) + heart, 0f, 1f) * border;
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

    /// <summary>A ragged puff of smoke: the soft dot, torn by noise and lobed around its outline,
    /// so a cloud of them is billows and not a stack of discs.</summary>
    public static float Puff(float u, float v)
    {
        float r = Radius(u, v);
        float turn = MathF.Atan2((v - 0.5f) * 2f, (u - 0.5f) * 2f) / Tau;
        float lobe = 1.1f - (0.22f * TileNoise(turn * 6f, 0.5f, 6, 77));
        float torn = r * (0.72f + (0.66f * Fbm(u, v, 4, 3, 5))) * lobe;
        float edge = Math.Clamp(1f - torn, 0f, 1f);
        return Math.Clamp(edge * edge * 1.6f, 0f, 1f) * Math.Clamp((1f - r) * 5f, 0f, 1f);
    }

    /// <summary>
    /// A tongue of flame: a pointed tip at the top of the quad, leaning as it climbs, widest three
    /// quarters of the way down and rounded in at its base, with a ragged edge and a hotter heart
    /// low in it. What a puff of fire is drawn with, so a fireball is licks of flame and a trail is
    /// flame, not orange balls.
    /// </summary>
    public static float Flame(float u, float v)
    {
        float y = (v - 0.04f) / 0.92f;
        if (y <= 0f || y >= 1f)
        {
            return 0f;
        }

        float lean = 0.12f * MathF.Sin((1f - y) * 3.4f) * (1f - y);
        float x = (u - 0.5f - lean) * 2f;
        float opens = MathF.Pow(y, 0.75f);
        float closes = MathF.Sqrt(Math.Clamp((1f - y) * 4.5f, 0f, 1f));
        float width = 0.6f * opens * closes * (0.78f + (0.44f * Fbm(u, v, 4, 2, 53)));
        if (width <= 0.001f)
        {
            return 0f;
        }

        float inside = Math.Clamp(1f - (MathF.Abs(x) / width), 0f, 1f);
        float body = inside * inside * (3f - (2f * inside));
        float heat = 0.55f + (0.45f * y);
        return Math.Clamp(body * heat * 1.25f, 0f, 1f) * SideBorder(u);
    }

    /// <summary>A streak of driven snow: a small bright head at the top of the quad (the end that
    /// leads) and a soft tail behind it. Many, small and fast, on a slant, they are a blizzard.</summary>
    public static float Snow(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float across = MathF.Exp(-(x * x) / 0.09f);
        float lead = Math.Clamp((v - 0.02f) / 0.12f, 0f, 1f);
        float tail = Math.Clamp((1f - v) / 0.88f, 0f, 1f);
        return Math.Clamp(across * lead * tail * tail * 1.15f, 0f, 1f) * SideBorder(u);
    }

    /// <summary>A snowflake: six thin arms with a barb on each and a bright heart. A mote of frost.</summary>
    public static float Flake(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        if (r >= 1f)
        {
            return 0f;
        }

        float spoke = MathF.Abs(MathF.Cos(MathF.Atan2(y, x) * 3f));
        float arm = MathF.Pow(spoke, 26f) * MathF.Sqrt(Math.Clamp(1f - (r / 0.9f), 0f, 1f));
        float barb = MathF.Pow(spoke, 5f) * Line(r, 0.52f, 0.07f) * 0.7f;
        float heart = MathF.Exp(-r * r * 30f);
        return Math.Clamp((arm * 0.95f) + barb + heart, 0f, 1f) * Math.Clamp((1f - r) * 8f, 0f, 1f);
    }

    /// <summary>A jagged spark: a thin line along V that kinks three times on its way, so thrown
    /// lightning crackles instead of flying as straight needles.</summary>
    public static float Spark(float u, float v)
    {
        float along = Math.Clamp(v, 0f, 0.9999f) * 4f;
        int knot = (int)MathF.Floor(along);
        float centre = Lerp(SparkKink(knot), SparkKink(knot + 1), along - knot);
        float x = ((u - 0.5f) * 2f) - centre;
        float across = MathF.Exp(-(x * x) / 0.03f);
        float ends = Math.Clamp((v - 0.02f) * 8f, 0f, 1f) * Math.Clamp((0.98f - v) * 8f, 0f, 1f);
        return Math.Clamp(across * ends, 0f, 1f) * SideBorder(u);
    }

    /// <summary>
    /// A wisp: a soft rounded head at the top of the quad and a tendril behind it that sways wider
    /// and thins to nothing. A soul being drawn out, a curl of necrotic smoke: what a drain is made
    /// of in place of a ball.
    /// </summary>
    public static float Wisp(float u, float v)
    {
        float t = (v - 0.05f) / 0.9f;
        if (t <= 0f || t >= 1f)
        {
            return 0f;
        }

        float centre = 0.34f * MathF.Sin(t * 5.2f) * t;
        float x = ((u - 0.5f) * 2f) - centre;
        float width = 0.36f * MathF.Pow(1f - t, 0.7f) * MathF.Sqrt(Math.Clamp(t * 9f, 0f, 1f));
        if (width <= 0.001f)
        {
            return 0f;
        }

        float inside = Math.Clamp(1f - (MathF.Abs(x) / width), 0f, 1f);
        float bright = (0.25f + (0.75f * (1f - t))) * Math.Clamp((1f - t) * 6f, 0f, 1f);
        return Math.Clamp(inside * inside * (3f - (2f * inside)) * bright * 1.2f, 0f, 1f) * SideBorder(u);
    }

    /// <summary>A flake of ash: a small uneven scrap with a grainy face, for what a fire leaves
    /// hanging in the air.</summary>
    public static float Ash(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float y = (v - 0.5f) * 2f;
        float r = MathF.Sqrt((x * x) + (y * y));
        float turn = MathF.Atan2(y, x) / Tau;
        float edge = 0.4f + (0.32f * TileNoise(turn * 5f, 0.5f, 5, 61));
        float inside = Math.Clamp((edge - r) * 9f, 0f, 1f);
        return Math.Clamp(inside * (0.62f + (0.38f * Fbm(u, v, 6, 2, 67))), 0f, 1f);
    }

    /// <summary>A mote: a tight bright heart with four short points. The small drifting light of a
    /// spell, which the soft dot drew as a ball of bokeh.</summary>
    public static float Mote(float u, float v)
    {
        float x = MathF.Abs(u - 0.5f) * 2f;
        float y = MathF.Abs(v - 0.5f) * 2f;
        float reachX = Math.Clamp(1f - x, 0f, 1f);
        float reachY = Math.Clamp(1f - y, 0f, 1f);
        float heart = MathF.Exp(-((x * x) + (y * y)) * 14f);
        float points = MathF.Max(
            MathF.Exp(-y * 9f) * reachX * reachX * reachX, MathF.Exp(-x * 9f) * reachY * reachY * reachY);
        return Math.Clamp(heart + (points * 0.55f), 0f, 1f) * Math.Clamp((1f - MathF.Max(x, y)) * 8f, 0f, 1f);
    }

    /// <summary>How far a jagged spark's line is off its centre at one of its knots: nothing at
    /// either end, up to half the quad's half width between.</summary>
    private static float SparkKink(int knot) => knot switch
    {
        1 => 0.42f,
        2 => -0.5f,
        3 => 0.3f,
        _ => 0f,
    };

    /// <summary>1 across a sprite, falling to 0 at its left and right edges.</summary>
    private static float SideBorder(float u) => Math.Clamp((1f - (MathF.Abs(u - 0.5f) * 2f)) * 6f, 0f, 1f);

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
