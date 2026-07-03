using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// The cue-id → <see cref="AudioStream"/> registry (Phase 31A). Builds the placeholder streams once via
/// <see cref="ProceduralAudio"/> and hands them out by id; an unknown id resolves to silence and warns
/// exactly once (a missing cue must never spam the log or throw mid-combat). This is the one place a
/// cue's <em>sound</em> is defined — swap a stream here for an authored recording at Phase 52 and every
/// caller is unchanged. Routing (which bus, positional or not) lives in the pure <see cref="AudioCueRouting"/>.
/// </summary>
public sealed class AudioLibrary
{
    private readonly Dictionary<string, AudioStream> _streams;
    private readonly HashSet<string> _warned = new();

    public AudioLibrary()
    {
        _streams = Build();
    }

    /// <summary>Resolves a cue's stream; false (silent) + a one-time warning for an unknown id.</summary>
    public bool TryGet(string cueId, out AudioStream stream)
    {
        if (_streams.TryGetValue(cueId, out AudioStream? found))
        {
            stream = found;
            return true;
        }

        if (_warned.Add(cueId))
        {
            Log.Warn($"No audio cue registered for '{cueId}' — playing silence.");
        }

        stream = null!;
        return false;
    }

    /// <summary>Number of registered cues (for diagnostics/boot logging).</summary>
    public int Count => _streams.Count;

    private static Dictionary<string, AudioStream> Build()
    {
        return new Dictionary<string, AudioStream>
        {
            // Combat SFX (positional). A swing is airy noise; impacts layer a body tone under a
            // transient so crits/blocks read distinct from a plain hit.
            ["sfx.combat.swing"] = ProceduralAudio.ToStream(
                ProceduralAudio.NoiseBurst(0.14f, lowpass: 0.5f, gain: 0.30f, seed: 11)),
            ["sfx.combat.hit"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(150f, 0.12f, gain: 0.5f, releaseSeconds: 0.09f),
                ProceduralAudio.NoiseBurst(0.10f, lowpass: 0.55f, gain: 0.28f, seed: 22))),
            ["sfx.combat.crit"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(240f, 0.16f, gain: 0.5f, releaseSeconds: 0.12f),
                ProceduralAudio.Sine(360f, 0.10f, gain: 0.3f),
                ProceduralAudio.NoiseBurst(0.12f, lowpass: 0.7f, gain: 0.30f, seed: 33))),
            ["sfx.combat.block"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(620f, 0.10f, gain: 0.4f, releaseSeconds: 0.08f),
                ProceduralAudio.NoiseBurst(0.08f, lowpass: 0.85f, gain: 0.28f, seed: 44))),

            // Music sting (2D). A short major chord on the boss's defeat — the existing hook
            // (BossEncounterDirector) already publishes music.boss_defeat.
            ["music.boss_defeat"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(262f, 1.3f, gain: 0.32f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(330f, 1.3f, gain: 0.28f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(392f, 1.3f, gain: 0.26f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(523f, 1.1f, gain: 0.22f, attackSeconds: 0.02f, releaseSeconds: 0.5f))),
        };
    }
}
