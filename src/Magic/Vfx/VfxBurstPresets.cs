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
    Crystal,
    Glint,
}

/// <summary>
/// The emitter presets beyond the seven a recipe can name (<see cref="VfxParticles"/>): the pieces a
/// richer effect is built from. The generic interpreter throws them by school and tier (a fire blast
/// gets <see cref="Flame"/> and <see cref="Chunks"/>, a frost one <see cref="Crystals"/>,
/// <see cref="Mist"/> and <see cref="Glints"/>); a special case throws one with
/// <c>cast.Fx.Burst(VfxEmitter.Flame, spec)</c>. The numbers continue <see cref="VfxParticles"/>'s so
/// the two share one table of pools.
/// </summary>
public enum VfxEmitter
{
    None = 0,

    /// <summary>Puffs of flame that swell, cool through the school's edge colour and end as dark
    /// smoke: the billow of an explosion, the body of a fire bolt's trail, a breath.</summary>
    Flame = 8,

    /// <summary>Small dark chunks thrown hard and pulled down by gravity: debris off the ground.</summary>
    Chunks = 9,

    /// <summary>Wide, faint, slow puffs that sink: cold mist hugging the floor.</summary>
    Mist = 10,

    /// <summary>Long cut-ice shards that fly point first and fall.</summary>
    Crystals = 11,

    /// <summary>Four-pointed twinkles that hang and flicker.</summary>
    Glints = 12,
}

/// <summary>How a particle's colour and alpha run over its life.</summary>
public enum VfxRamp
{
    /// <summary>Born hotter than its colour, cools into it and fades (the default for added light).</summary>
    Hot,

    /// <summary>Fades in, hangs and fades out, darkening (covering smoke).</summary>
    Smoke,

    /// <summary>Carries a heat value from 1 to 0: the sprite shader turns it from the hot tint to the
    /// cool one, and from added light to cover.</summary>
    Heat,

    /// <summary>Solid until the last third of its life (a chunk of debris).</summary>
    Solid,

    /// <summary>A soft swell and fade at low alpha (mist).</summary>
    Soft,

    /// <summary>Flickers (a glint).</summary>
    Twinkle,
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
/// <param name="Ramp">How its colour and alpha run over its life.</param>
/// <param name="Dissolve">How strongly the sprite is torn by noise (0 = a clean sprite).</param>
/// <param name="CoolOcclude">For a <see cref="VfxRamp.Heat"/> preset: how much it covers once cold.</param>
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
    float Energy,
    VfxRamp Ramp = VfxRamp.Hot,
    float Dissolve = 0f,
    float CoolOcclude = 0f);

/// <summary>
/// The particle presets as plain numbers: the seven a recipe names and the extra emitters
/// (<see cref="VfxEmitter"/>). Pure, so the table is in one place and a test can
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
        VfxParticles.Smoke => new(14, 1.9f, 0.4f, 1.5f, 0.9f, 0.9f, 0.55f, 1.25f, 0.8f, VfxSprite.Puff, false, true, false, true, true, 0.12f, VfxRamp.Smoke, 0.55f),
        VfxParticles.Leaves => new(24, 1.35f, 1.8f, 4.5f, -2.4f, 1.8f, 0.12f, 0.24f, 0.9f, VfxSprite.Leaf, false, true, false, false, false, 0.8f),
        VfxParticles.Wisps => new(20, 1.5f, 0.5f, 1.9f, 0.35f, 0.7f, 0.25f, 0.55f, 0.6f, VfxSprite.Dot, false, false, true, false, false, 0.9f),
        _ => default,
    };

    /// <summary>One more than the highest slot: the size of the table of pools.</summary>
    public const int SlotCount = 13;

    /// <summary>The numbers of an extra emitter preset.</summary>
    public static VfxBurstPreset For(VfxEmitter kind) => kind switch
    {
        VfxEmitter.Flame => new(16, 0.95f, 0.7f, 3f, 1.5f, 2.4f, 0.5f, 1.05f, 0.9f, VfxSprite.Puff, false, true, false, false, true, 0.85f, VfxRamp.Heat, 0.85f, 0.8f),
        VfxEmitter.Chunks => new(16, 1.1f, 4f, 10f, -15f, 0.3f, 0.05f, 0.13f, 1f, VfxSprite.Shard, false, true, false, true, false, 1f, VfxRamp.Solid),
        VfxEmitter.Mist => new(12, 2.4f, 0.3f, 1.3f, -0.12f, 1.2f, 0.8f, 1.6f, 0.65f, VfxSprite.Puff, false, true, false, false, true, 0.1f, VfxRamp.Soft, 0.5f),
        VfxEmitter.Crystals => new(22, 0.75f, 5f, 11f, -12f, 0.6f, 0.26f, 0.6f, 0.97f, VfxSprite.Crystal, true, false, false, false, false, 1.15f),
        VfxEmitter.Glints => new(14, 1f, 0.1f, 0.8f, -0.3f, 1f, 0.1f, 0.24f, 0.3f, VfxSprite.Glint, false, false, false, false, false, 1.7f, VfxRamp.Twinkle),
        _ => default,
    };

    /// <summary>The preset in a slot of the pool table: a <see cref="VfxParticles"/> or a
    /// <see cref="VfxEmitter"/> number. An empty slot has an amount of 0.</summary>
    public static VfxBurstPreset ForSlot(int slot) =>
        slot < (int)VfxEmitter.Flame ? For((VfxParticles)slot) : For((VfxEmitter)slot);

    /// <summary>
    /// Seconds from firing a one-shot burst until its last particle has died. An emitter spreads its
    /// births over <c>(1 - explosiveness)</c> of a lifetime, so the last particle is born that late
    /// and lives a full <paramref name="life"/> after it. An emitter returned to its pool sooner
    /// than this cuts its youngest particles off mid-fade.
    /// </summary>
    public static float BurstSeconds(float life, float explosiveness) =>
        MathF.Max(0f, life) * (2f - Math.Clamp(explosiveness, 0f, 1f));

    /// <summary>The emitter's allocated amount for a preset.</summary>
    public static int Allocated(in VfxBurstPreset preset) => Math.Max(1, (int)MathF.Ceiling(preset.Amount * Headroom));

    /// <summary>
    /// The fraction of an emitter's allocated particles to draw for a <paramref name="density"/>
    /// (1 = the preset's own amount; a plan's density already carries the tier's multiplier).
    /// </summary>
    public static float AmountRatio(float density) => Math.Clamp(density / Headroom, 0.04f, 1f);
}
