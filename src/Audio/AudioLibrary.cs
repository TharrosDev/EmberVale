using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// The cue-id → <see cref="AudioStream"/> registry (Phase 31). Each cue prefers a real CC0/open audio
/// file under <c>res://assets/audio/</c> (Kenney packs — see <c>assets/audio/CREDITS.md</c>) and falls
/// back to a <see cref="ProceduralAudio"/> placeholder if the file is missing, so a cue is never silent
/// even mid-authoring. An unknown cue id resolves to silence and warns exactly once. This is the one
/// place a cue's <em>sound</em> is defined — repoint an entry's path to swap in a new recording and every
/// caller is unchanged. Routing (which bus, positional or not) lives in the pure <see cref="AudioCueRouting"/>.
/// </summary>
public sealed class AudioLibrary
{
    private readonly Dictionary<string, AudioStream> _streams;
    private readonly HashSet<string> _warned = new();
    private int _realCount;

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

    /// <summary>Number of registered cues.</summary>
    public int Count => _streams.Count;

    /// <summary>How many cues resolved to a real asset file (the rest use procedural placeholders).</summary>
    public int RealCount => _realCount;

    private Dictionary<string, AudioStream> Build()
    {
        return new Dictionary<string, AudioStream>
        {
            // Combat SFX (positional). Real Kenney impact/RPG one-shots, procedural fallback each.
            ["sfx.combat.swing"] = Load("res://assets/audio/sfx/combat/swing.ogg",
                () => ProceduralAudio.NoiseBurst(0.14f, lowpass: 0.5f, gain: 0.30f, seed: 11)),
            ["sfx.combat.hit"] = Load("res://assets/audio/sfx/combat/hit.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(150f, 0.12f, gain: 0.5f, releaseSeconds: 0.09f),
                ProceduralAudio.NoiseBurst(0.10f, lowpass: 0.55f, gain: 0.28f, seed: 22))),
            ["sfx.combat.crit"] = Load("res://assets/audio/sfx/combat/crit.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(240f, 0.16f, gain: 0.5f, releaseSeconds: 0.12f),
                ProceduralAudio.Sine(360f, 0.10f, gain: 0.3f),
                ProceduralAudio.NoiseBurst(0.12f, lowpass: 0.7f, gain: 0.30f, seed: 33))),
            ["sfx.combat.block"] = Load("res://assets/audio/sfx/combat/block.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(620f, 0.10f, gain: 0.4f, releaseSeconds: 0.08f),
                ProceduralAudio.NoiseBurst(0.08f, lowpass: 0.85f, gain: 0.28f, seed: 44))),

            // Interaction SFX (positional). Used by the Phase 31C hooks; registered here now.
            ["sfx.pickup"] = Load("res://assets/audio/sfx/pickup.ogg",
                () => ProceduralAudio.Sine(880f, 0.10f, gain: 0.4f, releaseSeconds: 0.07f)),

            // Footsteps by surface (positional). Used by the Phase 31E FootstepComponent.
            ["step.grass"] = Load("res://assets/audio/sfx/steps/grass.ogg", () => Footstep(0.35f, 7)),
            ["step.wood"] = Load("res://assets/audio/sfx/steps/wood.ogg", () => Footstep(0.55f, 8)),
            ["step.stone"] = Load("res://assets/audio/sfx/steps/stone.ogg", () => Footstep(0.7f, 9)),
            ["step.snow"] = Load("res://assets/audio/sfx/steps/snow.ogg", () => Footstep(0.25f, 10)),

            // UI (2D). Used by the Phase 31C UI hooks.
            ["ui.click"] = Load("res://assets/audio/ui/click.wav",
                () => ProceduralAudio.Sine(1000f, 0.04f, gain: 0.3f, releaseSeconds: 0.03f)),
            ["ui.confirm"] = Load("res://assets/audio/ui/confirm.wav",
                () => ProceduralAudio.Sine(1320f, 0.06f, gain: 0.3f, releaseSeconds: 0.04f)),
            ["ui.back"] = Load("res://assets/audio/ui/back.wav",
                () => ProceduralAudio.Sine(660f, 0.05f, gain: 0.3f, releaseSeconds: 0.04f)),

            // Adaptive music beds (2D, looping) — Phase 31B. Real CC0 tracks swap in per state; the
            // procedural fallbacks are distinct chord pads (calm minor / warm major / tense / dark heavy).
            ["music.explore"] = LoadLooping("res://assets/audio/music/explore.ogg",
                () => ProceduralAudio.Pad(new[] { 220f, 261.6f, 329.6f }, 4f, gain: 0.16f, tremoloHz: 0.2f, tremoloDepth: 0.30f)),
            ["music.safe"] = LoadLooping("res://assets/audio/music/safe.ogg",
                () => ProceduralAudio.Pad(new[] { 261.6f, 329.6f, 392.0f }, 4f, gain: 0.14f, tremoloHz: 0.15f, tremoloDepth: 0.25f)),
            ["music.combat"] = LoadLooping("res://assets/audio/music/combat.ogg",
                () => ProceduralAudio.Pad(new[] { 146.8f, 220f, 233.1f, 293.7f }, 3f, gain: 0.22f, tremoloHz: 0.6f, tremoloDepth: 0.40f)),
            ["music.boss"] = LoadLooping("res://assets/audio/music/boss.ogg",
                () => ProceduralAudio.Pad(new[] { 98.0f, 130.8f, 138.6f, 196.0f }, 3f, gain: 0.26f, tremoloHz: 0.4f, tremoloDepth: 0.45f)),

            // Music sting (2D). Procedural chord — CC0 music is sourced per bed in Phase 31B/31D.
            ["music.boss_defeat"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(262f, 1.3f, gain: 0.32f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(330f, 1.3f, gain: 0.28f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(392f, 1.3f, gain: 0.26f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(523f, 1.1f, gain: 0.22f, attackSeconds: 0.02f, releaseSeconds: 0.5f))),
        };
    }

    /// <summary>Loads a real asset if it exists; otherwise builds a procedural placeholder from
    /// <paramref name="fallback"/>. Counts real hits for boot diagnostics.</summary>
    private AudioStream Load(string resPath, Func<float[]> fallback)
    {
        if (ResourceLoader.Exists(resPath) && GD.Load<AudioStream>(resPath) is { } real)
        {
            _realCount++;
            return real;
        }

        Log.Warn($"Audio asset '{resPath}' missing — using procedural placeholder.");
        return ProceduralAudio.ToStream(fallback());
    }

    /// <summary>Like <see cref="Load"/> but the procedural fallback is a seamless loop (music beds,
    /// ambience). A real asset's own loop flag governs when it is present.</summary>
    private AudioStream LoadLooping(string resPath, Func<float[]> fallback)
    {
        if (ResourceLoader.Exists(resPath) && GD.Load<AudioStream>(resPath) is { } real)
        {
            _realCount++;
            return real;
        }

        // Beds are a real-track-or-procedural design (unlike shipped SFX), so a missing file is an
        // expected state, not a warning — info-level keeps the error channel clean until tracks land.
        Log.Info($"No music track at '{resPath}' yet — using procedural bed.");
        return ProceduralAudio.ToStream(fallback(), loop: true);
    }

    /// <summary>A soft placeholder footstep: a short low-passed noise thud (<paramref name="tone"/>
    /// 0..1 brightness) — only heard if the real surface .ogg is absent.</summary>
    private static float[] Footstep(float tone, int seed) =>
        ProceduralAudio.NoiseBurst(0.09f, lowpass: tone, gain: 0.30f, seed: seed);
}
