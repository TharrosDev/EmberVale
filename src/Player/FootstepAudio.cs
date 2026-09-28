using System;

namespace Embervale.Player;

/// <summary>
/// How loud and at what pitch a footfall, a jump or a landing sounds (the 2026-09 footstep upgrade).
/// Godot-free so it unit-tests; <see cref="FootstepComponent"/> supplies the speeds and random draws.
///
/// <para><b>Why any of it.</b> One recording per surface played at one level is the machine-gun
/// footstep: every stride identical, a creep and a sprint the same loudness. Gait-scaled volume and
/// pitch, a detune that never repeats twice running, and a landing that is louder the harder you hit
/// are what make the same four samples read as a body moving over ground.</para>
/// </summary>
public static class FootstepAudio
{
    /// <summary>Gait intensity 0..1: 0 at or below <paramref name="walkSpeed"/>, 1 at or above
    /// <paramref name="sprintSpeed"/>.</summary>
    public static float Intensity(float speed, float walkSpeed, float sprintSpeed)
    {
        if (sprintSpeed <= walkSpeed)
        {
            return speed >= sprintSpeed ? 1f : 0f;
        }

        return Math.Clamp((speed - walkSpeed) / (sprintSpeed - walkSpeed), 0f, 1f);
    }

    /// <summary>Linear interpolation in decibels between a walk and a sprint footfall.</summary>
    public static float VolumeDb(float intensity, float quietDb, float loudDb) =>
        quietDb + ((loudDb - quietDb) * Math.Clamp(intensity, 0f, 1f));

    /// <summary>
    /// Pitch for a footfall: detune variant <paramref name="variant"/> of <paramref name="variants"/>
    /// spread evenly across ±<paramref name="jitter"/>, raised by up to <paramref name="rise"/> at a
    /// sprint (a hurried step is shorter and brighter).
    /// </summary>
    public static float Pitch(int variant, int variants, float jitter, float intensity, float rise)
    {
        float spread = variants <= 1 ? 0f : ((variant / (float)(variants - 1)) * 2f) - 1f;
        return 1f + (spread * jitter) + (Math.Clamp(intensity, 0f, 1f) * rise);
    }

    /// <summary>
    /// The next detune variant, never the same as <paramref name="last"/> when there is a choice.
    /// <paramref name="sample"/> is a uniform draw in [0, 1). With one recording per surface this is
    /// the "never the same sample twice" rule: the same recording is never played at the same pitch
    /// twice running.
    /// </summary>
    public static int NextVariant(int last, int count, double sample)
    {
        if (count <= 1)
        {
            return 0;
        }

        bool excludeLast = last >= 0 && last < count;
        int choices = excludeLast ? count - 1 : count;
        int pick = Math.Clamp((int)(Math.Clamp(sample, 0d, 0.999999d) * choices), 0, choices - 1);
        return excludeLast && pick >= last ? pick + 1 : pick;
    }

    /// <summary>
    /// How heavy a landing was, 0..1, from the downward speed at touchdown; null below
    /// <paramref name="minFall"/>, where coming off a kerb is just the next footfall.
    /// </summary>
    public static float? LandingWeight(float fallSpeed, float minFall, float maxFall)
    {
        if (fallSpeed < minFall)
        {
            return null;
        }

        return maxFall <= minFall ? 1f : Math.Clamp((fallSpeed - minFall) / (maxFall - minFall), 0f, 1f);
    }

    /// <summary>Pitch of a landing: a heavy one is lower, as a body hitting ground is.</summary>
    public static float LandingPitch(float weight, float drop) =>
        1f - (Math.Clamp(weight, 0f, 1f) * drop);

    /// <summary>
    /// True when the feet are in declared water at least <paramref name="minDepth"/> deep. Compares
    /// the water SURFACE with the feet rather than asking for a depth over the terrain, so a bridge
    /// over a river is dry and a jetty's planks sound like planks.
    /// </summary>
    public static bool IsWading(float? waterSurfaceY, float feetY, float minDepth) =>
        waterSurfaceY is { } surface && surface - feetY >= minDepth;
}
