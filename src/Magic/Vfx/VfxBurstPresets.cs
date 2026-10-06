using System;

namespace Embervale.Magic.Vfx;

/// <summary>The sprite a particle is drawn with.</summary>
public enum VfxSprite
{
    Dot,
    Streak,
    Shard,
    Leaf,
    Puff,
}

/// <summary>One particle preset's numbers, at the High tier (particle multiplier 1).</summary>
/// <param name="Amount">Particles a burst throws.</param>
/// <param name="Life">Seconds a particle lives.</param>
/// <param name="SpeedMin">Slowest launch speed, metres a second.</param>
/// <param name="SpeedMax">Fastest launch speed.</param>
/// <param name="Gravity">Vertical acceleration: negative falls, positive rises.</param>
/// <param name="Damping">How fast a particle slows.</param>
/// <param name="SizeMin">Smallest particle, metres.</param>
/// <param name="SizeMax">Largest particle.</param>
/// <param name="Explosiveness">0 = a steady stream over the lifetime, 1 = all at once.</param>
/// <param name="Sprite">What a particle looks like.</param>
/// <param name="AlignVelocity">The sprite stretches along its travel (a spark) instead of facing the camera.</param>
/// <param name="Spin">The sprite tumbles.</param>
/// <param name="Turbulence">The particles wander on a noise field.</param>
/// <param name="Occlude">Drawn as covering smoke rather than added light.</param>
/// <param name="Grow">The particle swells as it ages instead of shrinking.</param>
/// <param name="Energy">Emission energy against the school's mid colour energy.</param>
public readonly record struct VfxBurstPreset(
    int Amount,
    float Life,
    float SpeedMin,
    float SpeedMax,
    float Gravity,
    float Damping,
    float SizeMin,
    float SizeMax,
    float Explosiveness,
    VfxSprite Sprite,
    bool AlignVelocity,
    bool Spin,
    bool Turbulence,
    bool Occlude,
    bool Grow,
    float Energy);

/// <summary>
/// The seven particle presets as plain numbers. Pure, so the table is in one place and a test can
/// say that every preset a recipe can name exists and is sane. <c>VfxBurst</c> builds one pooled
/// emitter per preset from these and never reshapes one into another: a particle material's shader
/// depends on which features it uses, and swapping features at spawn time is a compile hitch in the
/// middle of a fight.
/// </summary>
public static class VfxBurstPresets
{
    /// <summary>
    /// An emitter is allocated this many times its preset's amount, and the tier then draws a
    /// fraction of it through <c>AmountRatio</c>. Changing the allocated amount restarts and
    /// reallocates the emitter; changing the ratio does not.
    /// </summary>
    public const float Headroom = 2f;

    public static VfxBurstPreset For(VfxParticles kind) => kind switch
    {
        VfxParticles.Sparks => new(44, 0.45f, 5f, 13f, -9f, 2.2f, 0.2f, 0.5f, 0.96f, VfxSprite.Streak, true, false, false, false, false, 1.5f),
        VfxParticles.Embers => new(40, 1.15f, 1.4f, 5f, 1.4f, 1.6f, 0.1f, 0.26f, 0.88f, VfxSprite.Dot, false, false, false, false, false, 1.3f),
        VfxParticles.Shards => new(26, 0.75f, 3.5f, 8.5f, -11f, 0.8f, 0.12f, 0.3f, 0.95f, VfxSprite.Shard, false, true, false, false, false, 1.1f),
        VfxParticles.Motes => new(34, 1.3f, 0.3f, 1.3f, 0.5f, 0.6f, 0.06f, 0.14f, 0.55f, VfxSprite.Dot, false, false, true, false, false, 1.2f),
        VfxParticles.Smoke => new(14, 1.9f, 0.4f, 1.5f, 0.9f, 0.9f, 0.55f, 1.25f, 0.8f, VfxSprite.Puff, false, true, false, true, true, 0.12f),
        VfxParticles.Leaves => new(24, 1.35f, 1.8f, 4.5f, -2.4f, 1.8f, 0.12f, 0.24f, 0.9f, VfxSprite.Leaf, false, true, false, false, false, 0.8f),
        VfxParticles.Wisps => new(20, 1.5f, 0.5f, 1.9f, 0.35f, 0.7f, 0.25f, 0.55f, 0.6f, VfxSprite.Dot, false, false, true, false, false, 0.9f),
        _ => default,
    };

    /// <summary>The emitter's allocated amount for a preset.</summary>
    public static int Allocated(in VfxBurstPreset preset) => Math.Max(1, (int)MathF.Ceiling(preset.Amount * Headroom));

    /// <summary>
    /// The fraction of an emitter's allocated particles to draw for a <paramref name="density"/>
    /// (1 = the preset's own amount; a plan's density already carries the tier's multiplier).
    /// </summary>
    public static float AmountRatio(float density) => Math.Clamp(density / Headroom, 0.04f, 1f);
}
