using System;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// Synthesizes placeholder audio (Phase 31A) as mono 16-bit PCM <see cref="AudioStreamWav"/> buffers —
/// so the audio system is audibly functional with zero binary assets in the repo. Everything here is
/// the deliberate swap-point for authored recordings at Phase 52: replace a cue's stream in
/// <see cref="AudioLibrary"/> and the rest of the pipeline is unchanged.
///
/// The public surface works in the float-sample domain (<see cref="Sine"/>, <see cref="NoiseBurst"/>,
/// <see cref="Mix"/>) and finishes with <see cref="ToStream"/>, so cues compose layers without decoding
/// PCM back and forth. Deterministic — noise uses a seeded PRNG so a given cue is byte-identical every
/// boot (stable for reasoning and for the census, no RNG at play time).
/// </summary>
public static class ProceduralAudio
{
    /// <summary>Sample rate for generated placeholders. 22.05 kHz is ample for UI/impact/ambience beds.</summary>
    public const int MixRate = 22050;

    /// <summary>A sine tone with a short linear attack/release envelope, in the float-sample domain.</summary>
    public static float[] Sine(float freqHz, float durSeconds, float gain = 0.6f, float attackSeconds = 0.005f, float releaseSeconds = 0.05f)
    {
        int n = Samples(durSeconds);
        var buffer = new float[n];
        int attack = Samples(attackSeconds);
        int release = Samples(releaseSeconds);
        double phase = 0d;
        double inc = 2d * Math.PI * freqHz / MixRate;
        for (int i = 0; i < n; i++)
        {
            buffer[i] = (float)Math.Sin(phase) * gain * Envelope(i, n, attack, release);
            phase += inc;
        }

        return buffer;
    }

    /// <summary>A one-pole-lowpassed noise burst (<paramref name="lowpass"/> 0..1: higher = brighter).</summary>
    public static float[] NoiseBurst(float durSeconds, float lowpass = 0.4f, float gain = 0.5f, int seed = 1, float attackSeconds = 0.002f, float releaseSeconds = 0.08f)
    {
        int n = Samples(durSeconds);
        var buffer = new float[n];
        int attack = Samples(attackSeconds);
        int release = Samples(releaseSeconds);
        var rng = new Random(seed);
        float k = Mathf.Clamp(lowpass, 0.01f, 1f);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2d - 1d);
            prev += k * (white - prev); // one-pole lowpass
            buffer[i] = prev * gain * Envelope(i, n, attack, release);
        }

        return buffer;
    }

    /// <summary>A steady, loop-friendly chord pad (no attack/release envelope) with optional slow
    /// amplitude tremolo — the placeholder music-bed generator (Phase 31B). Sum of equal-weight sines
    /// over <paramref name="freqs"/>, normalized and soft-clipped. Intended for <c>ToStream(..., loop: true)</c>.</summary>
    public static float[] Pad(float[] freqs, float durSeconds, float gain, float tremoloHz = 0f, float tremoloDepth = 0f)
    {
        int n = Samples(durSeconds);
        var buffer = new float[n];
        foreach (float freq in freqs)
        {
            double phase = 0d;
            double inc = 2d * Math.PI * freq / MixRate;
            for (int i = 0; i < n; i++)
            {
                buffer[i] += (float)Math.Sin(phase);
                phase += inc;
            }
        }

        float norm = freqs.Length > 0 ? 1f / freqs.Length : 1f;
        double tremInc = 2d * Math.PI * tremoloHz / MixRate;
        for (int i = 0; i < n; i++)
        {
            float trem = 1f - tremoloDepth * (0.5f - 0.5f * (float)Math.Cos(tremInc * i));
            buffer[i] = (float)Math.Tanh(buffer[i] * norm * gain * trem);
        }

        return buffer;
    }

    /// <summary>Sums layers (length = longest), then soft-clips to avoid summed overshoot clipping hard.</summary>
    public static float[] Mix(params float[][] layers)
    {
        int n = 0;
        foreach (float[] layer in layers)
        {
            n = Math.Max(n, layer.Length);
        }

        var buffer = new float[n];
        foreach (float[] layer in layers)
        {
            for (int i = 0; i < layer.Length; i++)
            {
                buffer[i] += layer[i];
            }
        }

        for (int i = 0; i < n; i++)
        {
            buffer[i] = (float)Math.Tanh(buffer[i]); // soft clip: keeps peaks < 1 without hard distortion
        }

        return buffer;
    }

    /// <summary>Packs float samples [-1,1] into a mono 16-bit PCM <see cref="AudioStreamWav"/>.</summary>
    public static AudioStreamWav ToStream(float[] samples, bool loop = false)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };

        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }

        return wav;
    }

    private static int Samples(float seconds) => Math.Max(1, (int)(seconds * MixRate));

    /// <summary>Linear attack-then-release gain in [0,1] over a buffer of <paramref name="n"/> samples.</summary>
    private static float Envelope(int i, int n, int attack, int release)
    {
        if (attack > 0 && i < attack)
        {
            return (float)i / attack;
        }

        int releaseStart = n - release;
        if (release > 0 && i >= releaseStart)
        {
            return Math.Max(0f, (float)(n - i) / release);
        }

        return 1f;
    }
}
