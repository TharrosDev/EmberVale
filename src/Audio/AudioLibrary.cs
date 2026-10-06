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

            // Interaction SFX. Pickup is a real Kenney one-shot; cast/level-up are procedural for now
            // (a shimmer and a triumphant chord) with real paths ready to swap in.
            ["sfx.pickup"] = Load("res://assets/audio/sfx/pickup.ogg",
                () => ProceduralAudio.Sine(880f, 0.10f, gain: 0.4f, releaseSeconds: 0.07f)),
            ["sfx.cast"] = Load("res://assets/audio/sfx/cast.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(660f, 0.28f, gain: 0.28f, attackSeconds: 0.01f, releaseSeconds: 0.2f),
                ProceduralAudio.Sine(990f, 0.24f, gain: 0.20f, releaseSeconds: 0.18f),
                ProceduralAudio.NoiseBurst(0.12f, lowpass: 0.9f, gain: 0.14f, seed: 55))),
            ["sfx.levelup"] = Load("res://assets/audio/sfx/levelup.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(523.3f, 0.55f, gain: 0.24f, attackSeconds: 0.04f, releaseSeconds: 0.25f),
                ProceduralAudio.Sine(659.3f, 0.55f, gain: 0.22f, attackSeconds: 0.06f, releaseSeconds: 0.25f),
                ProceduralAudio.Sine(784.0f, 0.55f, gain: 0.20f, attackSeconds: 0.08f, releaseSeconds: 0.25f),
                ProceduralAudio.Sine(1046.5f, 0.5f, gain: 0.18f, attackSeconds: 0.10f, releaseSeconds: 0.2f)), shipped: false),

            // World ambience emitters (positional) — Phase 38K second pass. Placed in a cell scene on an
            // AmbientEmitterComponent, which is why these are sfx.* rather than amb.*: the amb.* beds are
            // 2D and global, and the whole point of a market murmur is that it gets louder as you walk
            // into the market. Procedural until CC0 field recordings land, so shipped: false keeps the
            // "no asset" line at info and out of the warning channel.
            ["sfx.ambient.market"] = Load("res://assets/audio/sfx/ambient/market.ogg",
                () => ProceduralAudio.NoiseBed(2.2f, lowpass: 0.16f, gain: 0.10f, seed: 81), shipped: false),
            ["sfx.ambient.fire"] = Load("res://assets/audio/sfx/ambient/fire.ogg",
                () => ProceduralAudio.NoiseBurst(0.35f, lowpass: 0.30f, gain: 0.16f, seed: 82), shipped: false),

            // Footsteps by surface (positional). Used by the Phase 31E FootstepComponent.
            ["step.grass"] = Load("res://assets/audio/sfx/steps/grass.ogg", () => Footstep(0.35f, 7)),
            ["step.wood"] = Load("res://assets/audio/sfx/steps/wood.ogg", () => Footstep(0.55f, 8)),
            ["step.stone"] = Load("res://assets/audio/sfx/steps/stone.ogg", () => Footstep(0.7f, 9)),
            ["step.snow"] = Load("res://assets/audio/sfx/steps/snow.ogg", () => Footstep(0.25f, 10)),
            // Wading (2026-09 footstep upgrade). No CC0 recording is vendored, so it is procedural
            // until one is: a bright slosh over a low push of water. shipped: false keeps its
            // absence at info, like the other procedural-until-sourced cues.
            ["step.water"] = Load("res://assets/audio/sfx/steps/water.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.NoiseBurst(0.22f, lowpass: 0.65f, gain: 0.22f, seed: 12, releaseSeconds: 0.16f),
                ProceduralAudio.NoiseBurst(0.30f, lowpass: 0.12f, gain: 0.26f, seed: 13, attackSeconds: 0.02f,
                    releaseSeconds: 0.22f)), shipped: false),

            // Wading in past the knee (positional). Published by WorldWading. Procedural wash until a
            // CC0 recording lands, so shipped: false.
            ["sfx.water.wade"] = Load("res://assets/audio/sfx/water/wade.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.NoiseBurst(0.32f, lowpass: 0.22f, gain: 0.26f, seed: 91, releaseSeconds: 0.22f),
                ProceduralAudio.NoiseBurst(0.12f, lowpass: 0.6f, gain: 0.10f, seed: 92)), shipped: false),

            // UI (2D). Used by the Phase 31C UI hooks.
            ["ui.click"] = Load("res://assets/audio/ui/click.wav",
                () => ProceduralAudio.Sine(1000f, 0.04f, gain: 0.3f, releaseSeconds: 0.03f)),
            ["ui.confirm"] = Load("res://assets/audio/ui/confirm.wav",
                () => ProceduralAudio.Sine(1320f, 0.06f, gain: 0.3f, releaseSeconds: 0.04f)),
            ["ui.back"] = Load("res://assets/audio/ui/back.wav",
                () => ProceduralAudio.Sine(660f, 0.05f, gain: 0.3f, releaseSeconds: 0.04f)),

            // The interface's own cues (2D, UI bus), played by UiAudio. Procedural until recordings
            // land at the named paths, and told apart by contour like the quest chimes below: a dry
            // tick for a focus step, a rising pair for a tab, a fifth opening up and closing down
            // for a screen, a low beating pair for a refusal.
            ["ui.focus"] = Load("res://assets/audio/ui/focus.wav", () => ProceduralAudio.Mix(
                ProceduralAudio.NoiseBurst(0.02f, lowpass: 0.5f, gain: 0.08f, seed: 61, releaseSeconds: 0.015f),
                ProceduralAudio.Sine(880f, 0.03f, gain: 0.07f, releaseSeconds: 0.02f)), shipped: false),
            ["ui.tab"] = Load("res://assets/audio/ui/tab.wav", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(740f, 0.05f, gain: 0.18f, releaseSeconds: 0.035f),
                ProceduralAudio.Sine(1110f, 0.07f, gain: 0.12f, attackSeconds: 0.02f, releaseSeconds: 0.04f)), shipped: false),
            ["ui.open"] = Load("res://assets/audio/ui/open.wav", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(392.0f, 0.12f, gain: 0.15f, attackSeconds: 0.008f, releaseSeconds: 0.09f),
                ProceduralAudio.Sine(587.3f, 0.11f, gain: 0.10f, attackSeconds: 0.035f, releaseSeconds: 0.07f)), shipped: false),
            ["ui.close"] = Load("res://assets/audio/ui/close.wav", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(587.3f, 0.08f, gain: 0.11f, releaseSeconds: 0.06f),
                ProceduralAudio.Sine(392.0f, 0.12f, gain: 0.13f, attackSeconds: 0.03f, releaseSeconds: 0.08f)), shipped: false),
            ["ui.denied"] = Load("res://assets/audio/ui/denied.wav", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(196.0f, 0.14f, gain: 0.24f, releaseSeconds: 0.09f),
                ProceduralAudio.Sine(207.7f, 0.14f, gain: 0.20f, releaseSeconds: 0.09f)), shipped: false),
            ["ui.hold_tick"] = Load("res://assets/audio/ui/hold_tick.wav",
                () => ProceduralAudio.Sine(520f, 0.035f, gain: 0.15f, releaseSeconds: 0.028f), shipped: false),

            // Quest feedback (2D, UI bus), published by the toast feed and the chapter banner. No recordings
            // are vendored yet, so each is a procedural chime until a CC0 one lands at the named path; they are
            // told apart by contour (a rising triad, a two-note tick, a full chord, a low pad, a bright blip)
            // so a player can learn which event they heard without looking. shipped: false keeps the absence
            // at info, like the other procedural-until-sourced cues.
            ["ui.quest.started"] = Load("res://assets/audio/ui/quest_started.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(293.7f, 0.42f, gain: 0.22f, attackSeconds: 0.01f, releaseSeconds: 0.3f),
                ProceduralAudio.Sine(440.0f, 0.38f, gain: 0.18f, attackSeconds: 0.05f, releaseSeconds: 0.28f),
                ProceduralAudio.Sine(587.3f, 0.34f, gain: 0.14f, attackSeconds: 0.09f, releaseSeconds: 0.25f)), shipped: false),
            ["ui.quest.updated"] = Load("res://assets/audio/ui/quest_updated.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(659.3f, 0.2f, gain: 0.24f, attackSeconds: 0.005f, releaseSeconds: 0.14f),
                ProceduralAudio.Sine(880.0f, 0.16f, gain: 0.14f, attackSeconds: 0.03f, releaseSeconds: 0.1f)), shipped: false),
            ["ui.quest.completed"] = Load("res://assets/audio/ui/quest_completed.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(392.0f, 0.7f, gain: 0.22f, attackSeconds: 0.02f, releaseSeconds: 0.4f),
                ProceduralAudio.Sine(493.9f, 0.7f, gain: 0.2f, attackSeconds: 0.04f, releaseSeconds: 0.4f),
                ProceduralAudio.Sine(587.3f, 0.65f, gain: 0.18f, attackSeconds: 0.06f, releaseSeconds: 0.38f),
                ProceduralAudio.Sine(784.0f, 0.6f, gain: 0.14f, attackSeconds: 0.08f, releaseSeconds: 0.35f)), shipped: false),
            ["ui.chapter.title"] = Load("res://assets/audio/ui/chapter_title.ogg", () => ProceduralAudio.Pad(
                new[] { 110.0f, 164.8f, 220.0f }, 2.6f, gain: 0.2f, tremoloHz: 0.3f, tremoloDepth: 0.2f), shipped: false),
            ["ui.objective.optional"] = Load("res://assets/audio/ui/objective_optional.ogg", () => ProceduralAudio.Mix(
                ProceduralAudio.Sine(1046.5f, 0.14f, gain: 0.18f, attackSeconds: 0.004f, releaseSeconds: 0.1f),
                ProceduralAudio.Sine(1568.0f, 0.1f, gain: 0.09f, releaseSeconds: 0.07f)), shipped: false),

            // Adaptive music beds (2D, looping) — Phase 31B. Real CC0 tracks swap in per state; the
            // procedural fallbacks are distinct chord pads (calm minor / warm major / tense / dark heavy).
            ["music.explore"] = Load("res://assets/audio/music/explore.ogg",
                () => ProceduralAudio.Pad(new[] { 220f, 261.6f, 329.6f }, 4f, gain: 0.16f, tremoloHz: 0.2f, tremoloDepth: 0.30f), loop: true, shipped: false),
            ["music.safe"] = Load("res://assets/audio/music/safe.ogg",
                () => ProceduralAudio.Pad(new[] { 261.6f, 329.6f, 392.0f }, 4f, gain: 0.14f, tremoloHz: 0.15f, tremoloDepth: 0.25f), loop: true, shipped: false),
            ["music.combat"] = Load("res://assets/audio/music/combat.ogg",
                () => ProceduralAudio.Pad(new[] { 146.8f, 220f, 233.1f, 293.7f }, 3f, gain: 0.22f, tremoloHz: 0.6f, tremoloDepth: 0.40f), loop: true, shipped: false),
            ["music.boss"] = Load("res://assets/audio/music/boss.ogg",
                () => ProceduralAudio.Pad(new[] { 98.0f, 130.8f, 138.6f, 196.0f }, 3f, gain: 0.26f, tremoloHz: 0.4f, tremoloDepth: 0.45f), loop: true, shipped: false),

            // The title screen's bed, held by UiAudio while no session exists: banked embers, a low
            // open minor chord that barely breathes.
            ["music.title"] = Load("res://assets/audio/music/title.ogg",
                () => ProceduralAudio.Pad(new[] { 110f, 130.8f, 164.8f, 220f }, 5f, gain: 0.13f, tremoloHz: 0.2f, tremoloDepth: 0.35f), loop: true, shipped: false),

            // Ambience beds (2D, looping) — Phase 31D. Real CC0 field recordings swap in per bed; the
            // procedural fallbacks are filtered-noise washes (soft wind, night hush, rain, town murmur).
            ["amb.day"] = Load("res://assets/audio/ambience/day.ogg",
                () => ProceduralAudio.NoiseBed(4f, lowpass: 0.06f, gain: 0.10f, seed: 71), loop: true, shipped: false),
            ["amb.night"] = Load("res://assets/audio/ambience/night.ogg",
                () => ProceduralAudio.NoiseBed(4f, lowpass: 0.04f, gain: 0.08f, seed: 72), loop: true, shipped: false),
            ["amb.rain"] = Load("res://assets/audio/ambience/rain.ogg",
                () => ProceduralAudio.NoiseBed(4f, lowpass: 0.5f, gain: 0.14f, seed: 73), loop: true, shipped: false),
            ["amb.town"] = Load("res://assets/audio/ambience/town.ogg",
                () => ProceduralAudio.NoiseBed(4f, lowpass: 0.12f, gain: 0.09f, seed: 74), loop: true, shipped: false),

            // Music sting (2D). Procedural chord — CC0 music is sourced per bed in Phase 31B/31D.
            ["music.boss_defeat"] = ProceduralAudio.ToStream(ProceduralAudio.Mix(
                ProceduralAudio.Sine(262f, 1.3f, gain: 0.32f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(330f, 1.3f, gain: 0.28f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(392f, 1.3f, gain: 0.26f, attackSeconds: 0.01f, releaseSeconds: 0.6f),
                ProceduralAudio.Sine(523f, 1.1f, gain: 0.22f, attackSeconds: 0.02f, releaseSeconds: 0.5f))),
        };
    }

    /// <summary>Loads a real asset if present, else a procedural placeholder from <paramref name="fallback"/>.
    /// <paramref name="loop"/> makes the placeholder a seamless loop (beds/ambience). <paramref name="shipped"/>
    /// distinguishes an asset we ship (missing = a real problem → warn) from one that is procedural-until-
    /// sourced (missing = an expected, designed state → info, keeps the error channel clean).</summary>
    private AudioStream Load(string resPath, Func<float[]> fallback, bool loop = false, bool shipped = true)
    {
        if (ResourceLoader.Exists(resPath) && GD.Load<AudioStream>(resPath) is { } real)
        {
            _realCount++;
            return real;
        }

        string message = $"No audio asset at '{resPath}' — using procedural placeholder.";
        if (shipped)
        {
            Log.Warn(message);
        }
        else
        {
            Log.Info(message);
        }

        return ProceduralAudio.ToStream(fallback(), loop);
    }

    /// <summary>A soft placeholder footstep: a short low-passed noise thud (<paramref name="tone"/>
    /// 0..1 brightness) — only heard if the real surface .ogg is absent.</summary>
    private static float[] Footstep(float tone, int seed) =>
        ProceduralAudio.NoiseBurst(0.09f, lowpass: tone, gain: 0.30f, seed: seed);
}
