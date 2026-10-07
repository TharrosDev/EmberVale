using System;
using Embervale.Combat;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>How the sprites of a <see cref="VfxMotif"/> move.</summary>
public enum VfxMotion
{
    /// <summary>Tongues standing on their base, leaning and flickering: flame in a hand.</summary>
    Lick,

    /// <summary>Circling the centre, point first, bobbing: shards of ice about a hand.</summary>
    Orbit,

    /// <summary>Short arcs that jump to a new place a dozen times a second: lightning crackling.</summary>
    Crackle,

    /// <summary>Climbing in a turning column and tumbling: leaves on a rising draught.</summary>
    Swirl,

    /// <summary>Born at the rim and drawn in to the centre, curling: wisps being gathered.</summary>
    Inward,

    /// <summary>One sprite leading along an axis and the rest strung out behind it: the head of a bolt.</summary>
    Lance,
}

/// <summary>Where one sprite of a motif is this frame.</summary>
/// <param name="Offset">Metres from the motif's centre.</param>
/// <param name="Along">The way its long axis points (unit).</param>
/// <param name="Scale">Its size against the motif's.</param>
/// <param name="Alpha">0..1.</param>
/// <param name="Roll">Radians it is turned about the view axis, for a sprite that faces the camera.</param>
public readonly record struct VfxMotifPose(Vector3 Offset, Vector3 Along, float Scale, float Alpha, float Roll);

/// <summary>What a school's motif is built from.</summary>
/// <param name="Sprite">What each piece is drawn with.</param>
/// <param name="Motion">How the pieces move.</param>
/// <param name="Count">Pieces at the High tier.</param>
/// <param name="Reach">The motif's radius against the glow it is drawn around.</param>
/// <param name="Size">A piece's length against that glow.</param>
/// <param name="Aspect">A piece's width against its length.</param>
/// <param name="Energy">Emission against the school's body energy.</param>
/// <param name="Pale">Drawn in the school's pale core colour instead of its body colour.</param>
/// <param name="Scatter">For a <see cref="VfxMotion.Lance"/>: how far the followers stray sideways, in lengths.</param>
public readonly record struct VfxMotifStyle(
    VfxSprite Sprite, VfxMotion Motion, int Count, float Reach, float Size, float Aspect, float Energy, bool Pale,
    float Scatter = 0f)
{
    /// <summary>No motif: the school keeps what it has (arcane keeps its glyph).</summary>
    public bool IsNone => Count <= 0;
}

/// <summary>
/// The structured part of a cast, by school: what gathers in the hand while a spell winds up and what
/// the head of its bolt is shaped like. Pure, so the table is in one place and a test can say every
/// school has a shape of its own. <see cref="VfxMotif"/> draws these; <see cref="Pose"/> is where
/// each piece of one stands.
/// </summary>
public static class VfxMotifRules
{
    private const float Tau = MathF.PI * 2f;

    /// <summary>The most pieces a motif is ever drawn with (what <see cref="VfxMotif"/> allocates).</summary>
    public const int MaxCount = 12;

    /// <summary>
    /// What gathers in the casting hand while a spell of <paramref name="school"/> winds up: fire is
    /// licking flames, frost is orbiting shards, lightning is small arcs crackling around the hand,
    /// nature is leaves on a rising swirl, necrotic is wisps drawn inward. Arcane has none: its
    /// rune glyph is its structure.
    /// </summary>
    public static VfxMotifStyle Windup(DamageType school) => school switch
    {
        DamageType.Fire => new(VfxSprite.Flame, VfxMotion.Lick, 5, 0.9f, 1.7f, 0.5f, 1.05f, false),
        DamageType.Frost => new(VfxSprite.Crystal, VfxMotion.Orbit, 4, 1.7f, 1.25f, 0.36f, 0.9f, true),
        DamageType.Lightning => new(VfxSprite.Spark, VfxMotion.Crackle, 5, 1.9f, 1.6f, 0.26f, 1.5f, true),
        DamageType.Nature => new(VfxSprite.Leaf, VfxMotion.Swirl, 6, 1.8f, 0.85f, 0.62f, 0.75f, false),
        DamageType.Necrotic => new(VfxSprite.Wisp, VfxMotion.Inward, 5, 2.6f, 1.7f, 0.5f, 0.85f, false),
        _ => default,
    };

    /// <summary>
    /// The head of a bolt of <paramref name="school"/>, leading along its flight: a flame with licks
    /// strung out behind it, a shard of ice, a jagged bolt, an arcane lance, a cluster of thorns, a
    /// dark comet with wisps trailing.
    /// </summary>
    public static VfxMotifStyle Head(DamageType school) => school switch
    {
        DamageType.Fire => new(VfxSprite.Comet, VfxMotion.Lance, 4, 0f, 2.4f, 0.5f, 1.1f, false, 0.14f),
        DamageType.Frost => new(VfxSprite.Crystal, VfxMotion.Lance, 3, 0f, 2.6f, 0.3f, 1f, true, 0.2f),
        DamageType.Lightning => new(VfxSprite.Spark, VfxMotion.Lance, 3, 0f, 3f, 0.24f, 1.6f, true, 0.16f),
        DamageType.Arcane => new(VfxSprite.Streak, VfxMotion.Lance, 2, 0f, 3.6f, 0.2f, 1.3f, true, 0.05f),
        DamageType.Nature => new(VfxSprite.Shard, VfxMotion.Lance, 6, 0f, 1.3f, 0.42f, 0.85f, false, 0.55f),
        DamageType.Necrotic => new(VfxSprite.Wisp, VfxMotion.Lance, 4, 0f, 2.6f, 0.5f, 0.8f, false, 0.2f),
        _ => default,
    };

    /// <summary>How many pieces a style is drawn with at a tier: all of them (and a half more) at the
    /// top, three at the leanest, never more than <see cref="MaxCount"/>. A lance always keeps its
    /// head.</summary>
    public static int CountAt(in VfxMotifStyle style, VfxTier tier)
    {
        if (style.IsNone)
        {
            return 0;
        }

        float share = tier switch
        {
            VfxTier.Performance => 0.5f,
            VfxTier.Low => 0.65f,
            VfxTier.Medium => 0.85f,
            VfxTier.High => 1f,
            _ => 1.5f,
        };
        int least = style.Motion == VfxMotion.Lance ? 1 : 3;
        return Math.Clamp((int)MathF.Round(style.Count * share), Math.Min(least, style.Count), MaxCount);
    }

    /// <summary>
    /// Where piece <paramref name="index"/> of <paramref name="count"/> stands <paramref name="time"/>
    /// seconds in. <paramref name="radius"/> is the motif's reach and <paramref name="size"/> a
    /// piece's length, both in metres; <paramref name="axis"/> is a lance's line of flight;
    /// <paramref name="seed"/> makes two motifs differ. Every offset stays within about one and a half
    /// radii (or, for a lance, behind its head).
    /// </summary>
    public static VfxMotifPose Pose(
        VfxMotion motion, int index, int count, float time, float radius, float size, Vector3 axis, float scatter,
        int seed)
    {
        count = Math.Max(1, count);
        float phase = VfxTextureRules.Hash(index, seed, 7);
        float wobble = VfxTextureRules.Hash(index, seed, 19);
        float share = index / (float)count;
        switch (motion)
        {
            case VfxMotion.Lick:
            {
                float angle = (share + (phase * 0.3f)) * Tau;
                float reach = radius * 0.55f * (0.3f + (0.7f * wobble));
                float flick = 0.5f + (0.5f * MathF.Sin((time * (9f + (5f * phase))) + (phase * Tau)));
                float flutter = 0.5f + (0.5f * MathF.Sin((time * (17f + (7f * wobble))) + (wobble * Tau)));
                float lean = 0.18f + (0.12f * MathF.Sin((time * 6f) + (phase * 9f)));
                Vector3 along = new Vector3(MathF.Cos(angle) * lean, 1f, MathF.Sin(angle) * lean).Normalized();
                float scale = 0.55f + (0.45f * flick);

                // It stands on its base: the middle of the sprite is half its length up the lean.
                Vector3 foot = new(MathF.Cos(angle) * reach, -radius * 0.2f, MathF.Sin(angle) * reach);
                return new VfxMotifPose(foot + (along * (size * scale * 0.5f)), along, scale, 0.7f + (0.3f * flutter), 0f);
            }

            case VfxMotion.Orbit:
            {
                float angle = (share + (time * 0.55f)) * Tau;
                float bob = (angle * 2f) + (phase * Tau);
                var offset = new Vector3(MathF.Cos(angle) * radius, MathF.Sin(bob) * 0.22f * radius, MathF.Sin(angle) * radius);
                Vector3 tangent = new Vector3(-MathF.Sin(angle), 0.35f * MathF.Cos(bob), MathF.Cos(angle)).Normalized();
                return new VfxMotifPose(offset, tangent, 0.8f + (0.4f * wobble), 1f, 0f);
            }

            case VfxMotion.Crackle:
            {
                float clock = (time * 13f) + (phase * 7f);
                int step = (int)MathF.Floor(clock);
                float within = clock - step;
                float turn = VfxTextureRules.Hash(step, index, seed + 31) * Tau;
                float height = (VfxTextureRules.Hash(step, index, seed + 47) * 2f) - 1f;
                float ring = MathF.Sqrt(MathF.Max(0f, 1f - (height * height)));
                var out3 = new Vector3(MathF.Cos(turn) * ring, height, MathF.Sin(turn) * ring);

                // An arc runs across the hand, not out of it: mostly sideways, skewed a little.
                Vector3 side = Perpendicular(out3);
                float skew = (VfxTextureRules.Hash(step, index, seed + 59) - 0.5f) * 1.4f;
                Vector3 along = (side + (out3 * skew)).Normalized();
                bool struck = VfxTextureRules.Hash(step, index, seed + 71) < 0.7f;
                float alpha = struck ? 1f - (within * within) : 0f;
                float scale = 0.6f + (0.6f * VfxTextureRules.Hash(step, index, seed + 83));
                return new VfxMotifPose(out3 * (radius * 0.7f), along, scale, alpha, 0f);
            }

            case VfxMotion.Swirl:
            {
                float climb = Frac((time * 0.45f) + share + (phase * 0.2f));
                float angle = (phase + (time * 0.6f) + (climb * 0.8f)) * Tau;
                float wide = radius * (0.55f + (0.45f * MathF.Sin(climb * MathF.PI)));
                var offset = new Vector3(MathF.Cos(angle) * wide, (climb - 0.4f) * radius * 1.8f, MathF.Sin(angle) * wide);
                float roll = (time * (2f + (2f * wobble))) + (phase * Tau);
                return new VfxMotifPose(offset, Vector3.Up, 0.75f + (0.5f * wobble), MathF.Sin(climb * MathF.PI), roll);
            }

            case VfxMotion.Inward:
            {
                float arrived = Frac((time * 0.7f) + share + (phase * 0.15f));
                float angle = (phase + share + (arrived * 0.35f)) * Tau;
                Vector3 from = new Vector3(MathF.Cos(angle), (wobble - 0.5f) * 1.6f, MathF.Sin(angle)).Normalized();
                Vector3 offset = from * (radius * (1.08f - arrived));
                var curl = new Vector3(-MathF.Sin(angle), 0f, MathF.Cos(angle));
                Vector3 along = ((curl * 0.45f) - from).Normalized();
                float alpha = MathF.Sqrt(MathF.Max(0f, MathF.Sin(arrived * MathF.PI)));
                return new VfxMotifPose(offset, along, 0.6f + (0.5f * (1f - arrived)), alpha, 0f);
            }

            default:
            {
                Vector3 forward = axis.LengthSquared() < 0.0001f ? Vector3.Forward : axis.Normalized();
                if (index == 0)
                {
                    return new VfxMotifPose(Vector3.Zero, forward, 1f, 1f, 0f);
                }

                // The followers: strung out behind the head, straying sideways, flickering.
                Vector3 side = Perpendicular(forward);
                Vector3 up = side.Cross(forward);
                float around = (phase + (time * 0.9f)) * Tau;
                Vector3 stray = ((side * MathF.Cos(around)) + (up * MathF.Sin(around))) *
                                (scatter * size * (0.4f + (0.6f * wobble)));
                float flick = 0.7f + (0.3f * MathF.Sin((time * 21f) + (phase * Tau)));
                return new VfxMotifPose(
                    (forward * (-share * 1.1f * size)) + stray, forward, 1f - (share * 0.55f), (1f - (share * 0.5f)) * flick, 0f);
            }
        }
    }

    /// <summary>A unit vector across <paramref name="direction"/>.</summary>
    public static Vector3 Perpendicular(Vector3 direction)
    {
        Vector3 side = direction.Cross(Vector3.Up);
        if (side.LengthSquared() < 0.0004f)
        {
            side = direction.Cross(Vector3.Right);
        }

        return side.LengthSquared() < 0.000001f ? Vector3.Right : side.Normalized();
    }

    private static float Frac(float value) => value - MathF.Floor(value);
}
