using System;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The spell effects' textures, painted in code from <see cref="VfxTextureRules"/> the first time
/// each is asked for. There are no image files and nothing to import. Shared by every effect and
/// dropped by <see cref="Release"/> when the director leaves the tree, so a session ends holding no
/// engine resource through a static.
/// </summary>
internal static class VfxTextures
{
    private static ImageTexture? _dot;
    private static ImageTexture? _streak;
    private static ImageTexture? _shard;
    private static ImageTexture? _leaf;
    private static ImageTexture? _puff;
    private static ImageTexture? _noise;
    private static ImageTexture? _rune;
    private static ImageTexture? _crystal;
    private static ImageTexture? _glint;
    private static ImageTexture? _comet;
    private static ImageTexture? _rays;
    private static ImageTexture? _frostPattern;
    private static ImageTexture? _crackPattern;
    private static ImageTexture? _rootPattern;
    private static readonly ImageTexture?[] MarkAlbedo = new ImageTexture?[5];
    private static readonly ImageTexture?[] MarkEmission = new ImageTexture?[5];

    public static Texture2D Dot => _dot ??= Mask(64, VfxTextureRules.Dot);

    public static Texture2D Streak => _streak ??= Mask(64, VfxTextureRules.Streak);

    public static Texture2D Shard => _shard ??= Mask(64, VfxTextureRules.Shard);

    public static Texture2D Crystal => _crystal ??= Mask(64, VfxTextureRules.Crystal);

    public static Texture2D Glint => _glint ??= Mask(64, VfxTextureRules.Glint);

    public static Texture2D Comet => _comet ??= Mask(64, VfxTextureRules.Comet);

    /// <summary>The burst of rays a blast's flare throws.</summary>
    public static Texture2D Rays => _rays ??= Mask(128, VfxTextureRules.Rays);

    /// <summary>What a ground disc can be patterned with, as a mask for <c>vfx_ground</c>.</summary>
    public static Texture2D? Pattern(VfxDiscPattern pattern) => pattern switch
    {
        VfxDiscPattern.Rune => Rune,
        VfxDiscPattern.Frost => _frostPattern ??= Mask(128, VfxTextureRules.Frost),
        VfxDiscPattern.Cracks => _crackPattern ??= Mask(128, VfxTextureRules.ScorchHeat),
        VfxDiscPattern.Roots => _rootPattern ??= Mask(128, VfxTextureRules.Roots),
        _ => null,
    };

    public static Texture2D Leaf => _leaf ??= Mask(32, VfxTextureRules.Leaf);

    public static Texture2D Puff => _puff ??= Mask(64, VfxTextureRules.Puff);

    /// <summary>Tiling fractal noise, for every scrolling shader.</summary>
    public static Texture2D Noise => _noise ??= Mask(128, static (u, v) => VfxTextureRules.Fbm(u, v, 4, 4, 1), tiles: true);

    /// <summary>The rune circle, as a mask for <c>vfx_ground</c> and the sigil sprites.</summary>
    public static Texture2D Rune => _rune ??= Mask(256, VfxTextureRules.Rune);

    public static Texture2D Sprite(VfxSprite sprite) => sprite switch
    {
        VfxSprite.Streak => Streak,
        VfxSprite.Shard => Shard,
        VfxSprite.Leaf => Leaf,
        VfxSprite.Puff => Puff,
        VfxSprite.Crystal => Crystal,
        VfxSprite.Glint => Glint,
        VfxSprite.Comet => Comet,
        _ => Dot,
    };

    /// <summary>What a ground mark paints over the floor: colour, with its coverage in alpha.</summary>
    public static Texture2D? MarkAlbedoOf(VfxMark mark)
    {
        int index = (int)mark;
        if (mark == VfxMark.None || index >= MarkAlbedo.Length)
        {
            return null;
        }

        return MarkAlbedo[index] ??= mark switch
        {
            VfxMark.Scorch => Rgba(128, new Color(0.035f, 0.028f, 0.025f), VfxTextureRules.Scorch, 0.92f),
            VfxMark.Frost => Rgba(128, new Color(0.82f, 0.92f, 1f), VfxTextureRules.Frost, 0.7f),
            VfxMark.Rune => Rgba(256, new Color(0.9f, 0.9f, 0.9f), VfxTextureRules.Rune, 0.22f),
            _ => Rgba(128, new Color(0.11f, 0.085f, 0.05f), VfxTextureRules.Roots, 0.9f),
        };
    }

    /// <summary>What a ground mark glows with while it is fresh (or, for a rune, while it lasts).</summary>
    public static Texture2D? MarkEmissionOf(VfxMark mark)
    {
        int index = (int)mark;
        if (mark == VfxMark.None || index >= MarkEmission.Length)
        {
            return null;
        }

        return MarkEmission[index] ??= mark switch
        {
            VfxMark.Scorch => Rgba(128, Colors.White, VfxTextureRules.ScorchHeat, 1f, intoColour: true),
            VfxMark.Frost => Rgba(128, Colors.White, VfxTextureRules.Frost, 0.5f, intoColour: true),
            VfxMark.Rune => Rgba(256, Colors.White, VfxTextureRules.Rune, 1f, intoColour: true),
            _ => Rgba(128, Colors.White, VfxTextureRules.Roots, 0.35f, intoColour: true),
        };
    }

    /// <summary>Drops every texture. The next effect to ask paints them again.</summary>
    public static void Release()
    {
        _dot = null;
        _streak = null;
        _shard = null;
        _leaf = null;
        _puff = null;
        _noise = null;
        _rune = null;
        _crystal = null;
        _glint = null;
        _comet = null;
        _rays = null;
        _frostPattern = null;
        _crackPattern = null;
        _rootPattern = null;
        Array.Clear(MarkAlbedo);
        Array.Clear(MarkEmission);
    }

    private static ImageTexture Mask(int size, Func<float, float, float> paint, bool tiles = false)
    {
        var data = new byte[size * size];
        // A tiling texture is sampled on the lattice so its last column meets its first; a mask is
        // sampled at pixel centres so it is symmetric about the middle.
        float offset = tiles ? 0f : 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = paint((x + offset) / size, (y + offset) / size);
                data[(y * size) + x] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.L8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A colour texture: <paramref name="colour"/> everywhere with the painted value in
    /// alpha, or (<paramref name="intoColour"/>) the painted value as brightness with full alpha.</summary>
    private static ImageTexture Rgba(
        int size, Color colour, Func<float, float, float> paint, float strength, bool intoColour = false)
    {
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = Math.Clamp(paint((x + 0.5f) / size, (y + 0.5f) / size) * strength, 0f, 1f);
                int at = ((y * size) + x) * 4;
                float shade = intoColour ? value : 1f;
                data[at] = ToByte(colour.R * shade);
                data[at + 1] = ToByte(colour.G * shade);
                data[at + 2] = ToByte(colour.B * shade);
                data[at + 3] = intoColour ? (byte)255 : ToByte(value);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
