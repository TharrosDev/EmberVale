#!/usr/bin/env python3
"""Synthesises the 25 spell sound cues (numpy only) and encodes them to Ogg Vorbis.

    python tools/gen_spell_sfx.py            write assets/audio/sfx/spell/*.ogg + manifest.json
    python tools/gen_spell_sfx.py --check    resynthesise and compare PCM hashes with the manifest
    python tools/gen_spell_sfx.py --keep-wav DIR   also leave the 16-bit wavs in DIR (for listening)

Every cue is 44.1 kHz mono 16-bit, peak -3 dBFS, built from a fixed seed, so a rerun on the same
numpy build reproduces the PCM bit for bit; `--check` proves the committed manifest still describes
what this script makes. File names are the cue id after `sfx.spell.` with dots as underscores
(`sfx.spell.fire.cast` -> `fire_cast.ogg`), which is what `SpellAudio.AssetPath` loads.

Six schools times cast / impact / blast, and seven shared one-shots. A school is a set of layers
(fire: noise whoosh, low thump, crackle; frost: glassy inharmonic partials, shatter, hiss; lightning:
broadband crack, buzzing saw arc, rolling rumble; arcane: detuned and FM shimmer with a glide;
nature: woody knock, leafy rustle, rising chime; necrotic: sub drone, reversed swell, formant
whisper) and the event is the shape: a cast leaves, an impact lands, a blast has a sub-bass body
and a long decaying tail.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

SR = 44100
PEAK_DBFS = -3.0
PEAK = 10.0 ** (PEAK_DBFS / 20.0)
SEED_BASE = 20261006
REPO = Path(__file__).resolve().parent.parent
OUT_DIR = REPO / "assets" / "audio" / "sfx" / "spell"
MANIFEST = OUT_DIR / "manifest.json"
CUE_PREFIX = "sfx.spell."

SCHOOLS = ("fire", "frost", "lightning", "arcane", "nature", "necrotic")
EVENTS = ("cast", "impact", "blast")
ONE_SHOTS = ("windup", "fizzle", "ward_break", "freeze", "heal", "blink", "thunder")

# Allowed length in seconds, by the cue's kind.
DURATION_RANGE = {
    "cast": (0.35, 0.70),
    "impact": (0.30, 0.60),
    "blast": (0.90, 1.60),
    "oneshot": (0.30, 2.00),
}


# --------------------------------------------------------------------------------------------
# Building blocks
# --------------------------------------------------------------------------------------------

def samples(seconds: float) -> int:
    return int(round(seconds * SR))


def axis(seconds: float) -> np.ndarray:
    return np.arange(samples(seconds), dtype=np.float64) / SR


def noise(n: int, rng: np.random.Generator) -> np.ndarray:
    return rng.uniform(-1.0, 1.0, n)


def spectral(x: np.ndarray, lo: float | None = None, hi: float | None = None, order: int = 2) -> np.ndarray:
    """Zero-phase band limit with Butterworth-shaped skirts (zero-padded, so nothing wraps)."""
    n = len(x)
    pad = samples(0.25)
    size = 1 << int(math.ceil(math.log2(n + (2 * pad))))
    buf = np.zeros(size)
    buf[pad:pad + n] = x
    spectrum = np.fft.rfft(buf)
    freqs = np.fft.rfftfreq(size, 1.0 / SR)
    gain = np.ones_like(freqs)
    if hi is not None:
        gain /= np.sqrt(1.0 + (freqs / hi) ** (2 * order))
    if lo is not None:
        safe = np.maximum(freqs, 1e-9)
        gain /= np.sqrt(1.0 + (lo / safe) ** (2 * order))
        gain[0] = 0.0
    return np.fft.irfft(spectrum * gain, size)[pad:pad + n]


def svf(x: np.ndarray, cutoff, q: float, mode: str = "bp") -> np.ndarray:
    """A swept state-variable filter (trapezoidal, stable at any cutoff). `cutoff` may be an array."""
    n = len(x)
    fc = np.clip(np.broadcast_to(np.asarray(cutoff, dtype=np.float64), (n,)), 20.0, SR * 0.45)
    g = np.tan(np.pi * fc / SR)
    k = 1.0 / q
    a1 = 1.0 / (1.0 + (g * (g + k)))
    a2 = g * a1
    a3 = g * a2
    a1l, a2l, a3l, xl = a1.tolist(), a2.tolist(), a3.tolist(), x.tolist()
    out = [0.0] * n
    ic1 = 0.0
    ic2 = 0.0
    for i in range(n):
        v3 = xl[i] - ic2
        v1 = (a1l[i] * ic1) + (a2l[i] * v3)
        v2 = ic2 + (a2l[i] * ic1) + (a3l[i] * v3)
        ic1 = (2.0 * v1) - ic1
        ic2 = (2.0 * v2) - ic2
        if mode == "bp":
            out[i] = v1
        elif mode == "lp":
            out[i] = v2
        else:
            out[i] = xl[i] - (k * v1) - v2
    return np.asarray(out)


def glide(start: float, end: float, n: int, curve: float = 1.0) -> np.ndarray:
    """An exponential frequency glide; curve > 1 arrives late, < 1 arrives early."""
    u = np.linspace(0.0, 1.0, n) ** curve
    return start * (end / start) ** u


def osc(freq) -> np.ndarray:
    """A sine following a per-sample frequency array."""
    return np.sin(2.0 * np.pi * np.cumsum(freq) / SR)


def saw(freq) -> np.ndarray:
    phase = np.cumsum(freq) / SR
    return (2.0 * (phase % 1.0)) - 1.0


def decay(n: int, tau: float, attack: float = 0.0) -> np.ndarray:
    """Exponential decay with an optional raised-cosine attack."""
    t = np.arange(n) / SR
    env = np.exp(-t / tau)
    if attack > 0.0:
        a = min(n, samples(attack))
        env[:a] *= 0.5 - (0.5 * np.cos(np.pi * np.arange(a) / a))
    return env


def swell(n: int, peak_at: float, rise: float = 3.0, fall_tau: float = 0.03) -> np.ndarray:
    """A reversed envelope: grows to `peak_at` seconds, then drops away quickly."""
    t = np.arange(n) / SR
    up = np.clip(t / peak_at, 0.0, 1.0) ** rise
    down = np.where(t > peak_at, np.exp(-(t - peak_at) / fall_tau), 1.0)
    return up * down


def unit(x: np.ndarray) -> np.ndarray:
    peak = float(np.max(np.abs(x)))
    return x / peak if peak > 0.0 else x


def place(buf: np.ndarray, sig: np.ndarray, at: float, gain: float = 1.0) -> None:
    start = samples(at)
    if start >= len(buf):
        return
    end = min(len(buf), start + len(sig))
    buf[start:end] += sig[:end - start] * gain


def convolve(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    size = 1 << int(math.ceil(math.log2(len(a) + len(b))))
    return np.fft.irfft(np.fft.rfft(a, size) * np.fft.rfft(b, size), size)[:len(a) + len(b) - 1]


def grains(n: int, rng: np.random.Generator, count: int, start: float, end: float, tau: float,
           lo: float, hi: float, cluster: float = 1.0, fade: float | None = None) -> np.ndarray:
    """Crackle: `count` clicks between `start` and `end`, each a tiny filtered noise burst.
    cluster > 1 crowds them toward `start`; `fade` (seconds) makes later ones quieter."""
    impulses = np.zeros(n)
    times = start + ((end - start) * rng.random(count) ** cluster)
    amps = rng.uniform(0.25, 1.0, count) * rng.choice((-1.0, 1.0), count)
    if fade is not None:
        amps *= np.exp(-(times - start) / fade)
    index = np.minimum((times * SR).astype(int), n - 1)
    np.add.at(impulses, index, amps)
    length = max(8, samples(tau * 6.0))
    kernel = spectral(noise(length, rng) * decay(length, tau), lo, hi)
    return convolve(impulses, kernel)[:n]


def pings(n: int, rng: np.random.Generator, count: int, start: float, end: float, f_lo: float,
          f_hi: float, tau_lo: float, tau_hi: float, cluster: float = 1.0,
          fade: float | None = None) -> np.ndarray:
    """Shatter: `count` short sine pings at random high pitches (glass and ice fragments)."""
    out = np.zeros(n)
    times = start + ((end - start) * rng.random(count) ** cluster)
    for at in times:
        tau = rng.uniform(tau_lo, tau_hi)
        length = samples(tau * 7.0)
        freq = f_lo * (f_hi / f_lo) ** rng.random()
        t = np.arange(length) / SR
        ping = np.sin((2.0 * np.pi * freq * t) + rng.uniform(0.0, 6.283)) * np.exp(-t / tau)
        ping[:8] *= np.linspace(0.0, 1.0, 8)
        gain = rng.uniform(0.3, 1.0)
        if fade is not None:
            gain *= math.exp(-(at - start) / fade)
        place(out, ping, at, gain)
    return out


def partials(n: int, base, ratios, taus, gains, rng: np.random.Generator | None = None) -> np.ndarray:
    """A struck inharmonic body: one decaying sine per ratio of `base` (a number or an array)."""
    out = np.zeros(n)
    base_arr = np.broadcast_to(np.asarray(base, dtype=np.float64), (n,))
    for ratio, tau, gain in zip(ratios, taus, gains):
        phase = rng.uniform(0.0, 6.283) if rng is not None else 0.0
        out += np.sin((2.0 * np.pi * np.cumsum(base_arr * ratio) / SR) + phase) * decay(n, tau, 0.002) * gain
    return out


def thump(n: int, f_start: float, f_end: float, tau: float, pitch_tau: float = 0.04) -> np.ndarray:
    """A low body hit: a sine dropping from f_start to f_end."""
    t = np.arange(n) / SR
    freq = f_end + ((f_start - f_end) * np.exp(-t / pitch_tau))
    return osc(freq) * decay(n, tau, 0.003)


def crack(n: int, rng: np.random.Generator, tau: float, lo: float = 500.0, hi: float = 16000.0) -> np.ndarray:
    """A sharp broadband transient."""
    return spectral(noise(n, rng), lo, hi) * decay(n, tau, 0.0005)


def slow(n: int, rng: np.random.Generator, rate: float) -> np.ndarray:
    """Slow random movement in 0..1 (for flutter and rolling)."""
    wobble = spectral(noise(n, rng), None, rate, order=2)
    wobble = unit(wobble)
    return 0.5 + (0.5 * wobble)


def saturate(x: np.ndarray, drive: float) -> np.ndarray:
    return np.tanh(unit(x) * drive) / math.tanh(drive)


def reverb(x: np.ndarray, rng: np.random.Generator, seconds: float, wet: float, lo: float = 200.0,
           hi: float = 7000.0, predelay: float = 0.012) -> np.ndarray:
    """A short synthetic room: convolution with band-limited noise that decays by 60 dB."""
    length = samples(seconds)
    t = np.arange(length) / SR
    impulse = spectral(noise(length, rng), lo, hi) * np.exp(-6.91 * t / seconds)
    impulse /= math.sqrt(float(np.sum(impulse ** 2)))
    tail = convolve(x, impulse)
    out = np.zeros(len(x) + length + samples(predelay))
    out[:len(x)] += x
    place(out, tail, predelay, wet)
    return out


def finish(x: np.ndarray, seconds: float, rng: np.random.Generator, drive: float = 1.6,
           room: float = 0.3, wet: float = 0.2, room_lo: float = 200.0, room_hi: float = 7000.0,
           fade_in: float = 0.002, fade_out: float = 0.06) -> np.ndarray:
    """Saturate, add the room, cut to length, remove DC, fade both ends to zero, set the peak."""
    x = saturate(x, drive)
    x = reverb(x, rng, room, wet, room_lo, room_hi)
    n = samples(seconds)
    if len(x) < n:
        x = np.concatenate((x, np.zeros(n - len(x))))
    x = spectral(x[:n], lo=25.0, order=2)
    x = x - float(np.mean(x))
    a = samples(fade_in)
    b = samples(fade_out)
    x[:a] *= 0.5 - (0.5 * np.cos(np.pi * np.arange(a) / a))
    x[n - b:] *= 0.5 + (0.5 * np.cos(np.pi * np.arange(b) / b))
    x[0] = 0.0
    x[-1] = 0.0
    return unit(x) * PEAK


# --------------------------------------------------------------------------------------------
# Fire: filtered-noise whoosh, low thump, crackle
# --------------------------------------------------------------------------------------------

def fire_cast(rng):
    n = samples(0.55)
    sweep = np.concatenate((glide(350, 2600, samples(0.16)), glide(2600, 700, n - samples(0.16), 0.7)))
    whoosh = svf(noise(n, rng), sweep, 1.4) * decay(n, 0.17, 0.05)
    roar = spectral(noise(n, rng), 120, 700) * (0.4 + (0.6 * slow(n, rng, 22.0))) * decay(n, 0.2, 0.03)
    body = np.zeros(n)
    place(body, thump(samples(0.3), 130, 58, 0.09), 0.035)
    crackle = grains(n, rng, 26, 0.03, 0.5, 0.0016, 1800, 9000, cluster=1.3, fade=0.3)
    hiss = spectral(noise(n, rng), 5000, 12000) * decay(n, 0.1, 0.06)
    mix = (unit(whoosh) * 1.0) + (unit(roar) * 0.5) + (body * 0.3) + (unit(crackle) * 0.45) + (unit(hiss) * 0.15)
    return finish(mix, 0.55, rng, drive=2.0, room=0.25, wet=0.16, fade_out=0.09)


def fire_impact(rng):
    n = samples(0.45)
    burst = spectral(noise(n, rng), 180, 4200) * decay(n, 0.055, 0.001)
    puff = spectral(noise(n, rng), 90, 520) * decay(n, 0.12, 0.004) * (0.5 + (0.5 * slow(n, rng, 30.0)))
    body = thump(n, 160, 52, 0.1, 0.03)
    crackle = grains(n, rng, 30, 0.02, 0.4, 0.0014, 2000, 10000, cluster=1.8, fade=0.16)
    sizzle = svf(noise(n, rng), glide(6000, 1500, n), 2.0) * decay(n, 0.09, 0.01)
    mix = (unit(burst) * 1.0) + (unit(puff) * 0.4) + (body * 0.4) + (unit(crackle) * 0.45) + (unit(sizzle) * 0.35)
    return finish(mix, 0.45, rng, drive=2.4, room=0.22, wet=0.15, fade_out=0.08)


def fire_blast(rng):
    n = samples(1.25)
    hit = crack(n, rng, 0.012, 300, 14000)
    explosion = svf(noise(n, rng), glide(7000, 220, n, 0.45), 0.8, "lp") * decay(n, 0.3, 0.002)
    rumble = spectral(noise(n, rng), 30, 130) * (0.45 + (0.55 * slow(n, rng, 9.0))) * decay(n, 0.5, 0.02)
    sub = thump(n, 95, 34, 0.42, 0.09)
    second = np.zeros(n)
    place(second, thump(samples(0.5), 70, 40, 0.22, 0.06), 0.11)
    roar = spectral(noise(n, rng), 150, 900) * (0.3 + (0.7 * slow(n, rng, 16.0))) * decay(n, 0.42, 0.04)
    debris = grains(n, rng, 70, 0.05, 1.15, 0.0018, 1500, 9000, cluster=1.5, fade=0.5)
    mix = ((unit(hit) * 0.9) + (unit(explosion) * 1.2) + (unit(rumble) * 0.4) + (sub * 0.5)
           + (second * 0.22) + (unit(roar) * 0.8) + (unit(debris) * 0.45))
    return finish(mix, 1.4, rng, drive=3.0, room=0.55, wet=0.22, room_lo=80, room_hi=5000, fade_out=0.3)


# --------------------------------------------------------------------------------------------
# Frost: glassy inharmonic partials, shatter grains, airy hiss
# --------------------------------------------------------------------------------------------

GLASS = (1.0, 1.47, 2.09, 2.56, 3.39, 4.08, 5.21)


def frost_cast(rng):
    n = samples(0.5)
    ring = partials(n, 1480.0, GLASS, (0.16, 0.13, 0.11, 0.09, 0.07, 0.06, 0.05),
                    (1.0, 0.7, 0.55, 0.45, 0.35, 0.25, 0.18), rng)
    shing = osc(glide(2200, 5600, n, 0.5)) * decay(n, 0.05, 0.004)
    hiss = spectral(noise(n, rng), 5500, 15000) * swell(n, 0.09, 1.5, 0.12)
    shards = pings(n, rng, 22, 0.04, 0.42, 3200, 9500, 0.004, 0.02, cluster=1.2, fade=0.25)
    breath = svf(noise(n, rng), glide(1800, 6500, n), 3.0) * decay(n, 0.12, 0.03)
    mix = (unit(ring) * 0.8) + (shing * 0.35) + (unit(hiss) * 0.4) + (unit(shards) * 0.5) + (unit(breath) * 0.35)
    return finish(mix, 0.5, rng, drive=1.3, room=0.3, wet=0.24, room_lo=900, room_hi=12000, fade_out=0.09)


def frost_impact(rng):
    n = samples(0.42)
    snap = crack(n, rng, 0.005, 2500, 16000)
    ring = partials(n, 2150.0, GLASS, (0.15, 0.12, 0.10, 0.08, 0.065, 0.05, 0.04),
                    (1.0, 0.8, 0.6, 0.5, 0.35, 0.3, 0.2), rng)
    tock = thump(n, 420, 190, 0.035, 0.012)
    shards = pings(n, rng, 46, 0.0, 0.34, 2800, 11000, 0.003, 0.016, cluster=2.0, fade=0.12)
    hiss = spectral(noise(n, rng), 6000, 15000) * decay(n, 0.07, 0.002)
    mix = (unit(snap) * 0.7) + (unit(ring) * 0.9) + (tock * 0.6) + (unit(shards) * 0.8) + (unit(hiss) * 0.45)
    return finish(mix, 0.42, rng, drive=1.4, room=0.28, wet=0.22, room_lo=900, room_hi=12000, fade_out=0.08)


def frost_blast(rng):
    n = samples(1.15)
    snap = crack(n, rng, 0.008, 1500, 16000)
    sub = thump(n, 110, 42, 0.28, 0.06)
    crash = pings(n, rng, 190, 0.0, 1.0, 1800, 11000, 0.004, 0.03, cluster=2.2, fade=0.3)
    wash = svf(noise(n, rng), glide(11000, 2200, n, 0.6), 0.9, "hp") * decay(n, 0.22, 0.002)
    low_ring = partials(n, 610.0, GLASS, (0.5, 0.42, 0.34, 0.28, 0.22, 0.18, 0.14),
                        (1.0, 0.8, 0.7, 0.5, 0.4, 0.3, 0.2), rng)
    high_ring = partials(n, 1890.0, GLASS[:5], (0.3, 0.24, 0.2, 0.16, 0.12), (0.8, 0.6, 0.5, 0.35, 0.25), rng)
    crackle = grains(n, rng, 60, 0.05, 1.05, 0.001, 3500, 14000, cluster=1.4, fade=0.4)
    air = spectral(noise(n, rng), 4500, 13000) * decay(n, 0.4, 0.05) * (0.5 + (0.5 * slow(n, rng, 12.0)))
    mix = ((unit(snap) * 0.8) + (sub * 0.38) + (unit(crash) * 0.75) + (unit(wash) * 0.55)
           + (unit(low_ring) * 0.5) + (unit(high_ring) * 0.3) + (unit(crackle) * 0.3) + (unit(air) * 0.25))
    return finish(mix, 1.3, rng, drive=1.8, room=0.6, wet=0.26, room_lo=500, room_hi=12000, fade_out=0.3)


# --------------------------------------------------------------------------------------------
# Lightning: broadband crack, buzzing saw arc, rolling rumble
# --------------------------------------------------------------------------------------------

def arc(n: int, rng: np.random.Generator, pitch: float, stutter_hz: float) -> np.ndarray:
    """An electric arc: a jittering saw, chopped by a random gate and pushed through a band."""
    freq = pitch * (1.0 + (0.3 * ((2.0 * slow(n, rng, 60.0)) - 1.0)))
    buzz = saw(freq) + (0.5 * saw(freq * 2.01))
    steps = int(math.ceil(n / (SR / stutter_hz))) + 1
    gate = np.repeat(rng.choice((0.15, 0.6, 1.0, 1.0), steps), int(SR / stutter_hz))[:n]
    gate = spectral(gate, None, 900.0, order=1)
    rough = buzz * (0.6 + (0.4 * noise(n, rng)))
    return spectral(rough * gate, 700, 7500)


def lightning_cast(rng):
    n = samples(0.42)
    snap = crack(n, rng, 0.012, 900, 16000)
    zap = osc(glide(5200, 700, n, 0.35)) * decay(n, 0.035, 0.001)
    buzz = arc(n, rng, 118.0, 85.0) * decay(n, 0.12, 0.004)
    fizz = grains(n, rng, 34, 0.01, 0.36, 0.0007, 3000, 15000, cluster=1.3, fade=0.16)
    low = thump(n, 150, 70, 0.06, 0.02)
    mix = (unit(snap) * 0.9) + (zap * 0.45) + (unit(buzz) * 0.8) + (unit(fizz) * 0.4) + (low * 0.2)
    return finish(mix, 0.42, rng, drive=2.2, room=0.25, wet=0.16, room_lo=400, room_hi=9000, fade_out=0.07)


def lightning_impact(rng):
    n = samples(0.38)
    snap = crack(n, rng, 0.007, 700, 16000)
    second = np.zeros(n)
    place(second, crack(samples(0.1), rng, 0.006, 1500, 15000), 0.028)
    zap = osc(glide(6800, 380, n, 0.3)) * decay(n, 0.028, 0.0008)
    buzz = arc(n, rng, 96.0, 110.0) * decay(n, 0.07, 0.002)
    thud = thump(n, 190, 62, 0.075, 0.025)
    fizz = grains(n, rng, 24, 0.02, 0.3, 0.0006, 4000, 15000, cluster=1.8, fade=0.1)
    mix = ((unit(snap) * 1.0) + (second * 0.5) + (zap * 0.5) + (unit(buzz) * 0.7) + (thud * 0.35)
           + (unit(fizz) * 0.35))
    return finish(mix, 0.38, rng, drive=2.6, room=0.22, wet=0.15, room_lo=400, room_hi=9000, fade_out=0.07)


def rolling(n: int, rng: np.random.Generator, hi: float, rate: float, tau: float) -> np.ndarray:
    """Thunder's roll: low noise that swells and falls back unevenly as it dies."""
    roll = slow(n, rng, rate) ** 1.6
    return spectral(noise(n, rng), 28, hi) * (0.25 + (0.75 * roll)) * decay(n, tau, 0.02)


def lightning_blast(rng):
    n = samples(1.35)
    cracks = np.zeros(n)
    for at, gain, tau in ((0.0, 1.0, 0.016), (0.034, 0.7, 0.012), (0.085, 0.55, 0.02), (0.15, 0.3, 0.025)):
        place(cracks, crack(samples(0.25), rng, tau, 600, 16000), at, gain)
    tear = svf(noise(n, rng), glide(9000, 900, n, 0.4), 1.2) * decay(n, 0.11, 0.001)
    buzz = arc(n, rng, 82.0, 70.0) * decay(n, 0.2, 0.003)
    boom = thump(n, 120, 33, 0.4, 0.07)
    rumble = rolling(n, rng, 190.0, 7.0, 0.55)
    mid = spectral(noise(n, rng), 150, 700) * (0.3 + (0.7 * slow(n, rng, 11.0))) * decay(n, 0.32, 0.01)
    fizz = grains(n, rng, 50, 0.05, 0.9, 0.0007, 3500, 15000, cluster=1.6, fade=0.3)
    mix = ((unit(cracks) * 1.2) + (unit(tear) * 0.8) + (unit(buzz) * 0.8) + (boom * 0.42) + (unit(rumble) * 0.4)
           + (unit(mid) * 0.6) + (unit(fizz) * 0.3))
    return finish(mix, 1.5, rng, drive=3.0, room=0.6, wet=0.22, room_lo=70, room_hi=6500, fade_out=0.35)


def thunder(rng):
    n = samples(1.75)
    cracks = np.zeros(n)
    for at, gain, tau in ((0.0, 1.0, 0.03), (0.06, 0.6, 0.025), (0.17, 0.45, 0.04), (0.33, 0.25, 0.05)):
        place(cracks, crack(samples(0.4), rng, tau, 250, 9000), at, gain)
    boom = thump(n, 90, 28, 0.6, 0.12)
    roll_low = rolling(n, rng, 140.0, 4.5, 0.85)
    roll_mid = spectral(noise(n, rng), 120, 520) * (0.2 + (0.8 * slow(n, rng, 6.0) ** 2)) * decay(n, 0.6, 0.01)
    late = np.zeros(n)
    place(late, rolling(samples(1.0), rng, 110.0, 5.0, 0.4), 0.55, 0.7)
    mix = (unit(cracks) * 1.1) + (boom * 0.45) + (unit(roll_low) * 0.55) + (unit(roll_mid) * 0.85) + (unit(late) * 0.3)
    return finish(mix, 1.9, rng, drive=3.2, room=0.8, wet=0.28, room_lo=50, room_hi=4000, fade_out=0.5)


# --------------------------------------------------------------------------------------------
# Arcane: detuned sines and FM shimmer with a pitch glide
# --------------------------------------------------------------------------------------------

def fm(carrier, ratio: float, index) -> np.ndarray:
    """Two-operator FM: `carrier` and `index` may be arrays."""
    phase = 2.0 * np.pi * np.cumsum(carrier) / SR
    return np.sin(phase + (index * np.sin(phase * ratio)))


def shimmer(n: int, rng: np.random.Generator, count: int, f_lo: float, f_hi: float, bend: float) -> np.ndarray:
    """A cloud of close detuned sines that beat against each other, all bending by `bend`."""
    out = np.zeros(n)
    curve = glide(1.0, bend, n)
    for _ in range(count):
        freq = f_lo * (f_hi / f_lo) ** rng.random()
        pair = freq * (1.0 + rng.uniform(0.003, 0.012))
        phase = rng.uniform(0.0, 6.283)
        out += np.sin((2.0 * np.pi * np.cumsum(freq * curve) / SR) + phase)
        out += np.sin((2.0 * np.pi * np.cumsum(pair * curve) / SR) + (phase * 1.7))
    return out / count


def arcane_cast(rng):
    n = samples(0.6)
    t = np.arange(n) / SR
    rise = glide(1.0, 1.5, n, 0.8)
    voices = np.zeros(n)
    for ratio, gain in ((1.0, 1.0), (1.007, 0.9), (1.5, 0.55), (1.493, 0.5), (2.01, 0.4), (3.0, 0.18)):
        voices += np.sin(2.0 * np.pi * np.cumsum(440.0 * ratio * rise) / SR) * gain
    voices *= (0.75 + (0.25 * np.sin(2.0 * np.pi * 17.0 * t))) * decay(n, 0.2, 0.04)
    bell = fm(880.0 * rise, 3.01, 4.0 * decay(n, 0.12) + 0.4) * decay(n, 0.16, 0.012)
    sparkle = shimmer(n, rng, 6, 2400, 6200, 1.3) * decay(n, 0.14, 0.05)
    air = svf(noise(n, rng), glide(2500, 8500, n), 4.0) * decay(n, 0.15, 0.06)
    mix = (unit(voices) * 0.85) + (bell * 0.5) + (unit(sparkle) * 0.4) + (unit(air) * 0.22)
    return finish(mix, 0.6, rng, drive=1.3, room=0.35, wet=0.26, room_lo=500, room_hi=10000, fade_out=0.1)


def arcane_impact(rng):
    n = samples(0.46)
    fall = glide(1.0, 0.5, n, 0.45)
    bwomp = fm(620.0 * fall, 1.41, 6.0 * decay(n, 0.06) + 0.2) * decay(n, 0.1, 0.002)
    low = thump(n, 210, 78, 0.09, 0.03)
    tick = crack(n, rng, 0.003, 1500, 12000)
    glints = np.zeros(n)
    for freq, tau, gain in ((2210.0, 0.09, 1.0), (2236.0, 0.09, 0.9), (3310.0, 0.06, 0.6), (3352.0, 0.06, 0.5),
                            (4975.0, 0.04, 0.35)):
        glints += np.sin(2.0 * np.pi * np.cumsum(freq * glide(1.0, 0.9, n)) / SR) * decay(n, tau, 0.002) * gain
    suck = svf(noise(n, rng), glide(5000, 500, n, 0.5), 3.0) * decay(n, 0.07, 0.002)
    mix = (bwomp * 0.9) + (low * 0.8) + (unit(tick) * 0.4) + (unit(glints) * 0.5) + (unit(suck) * 0.35)
    return finish(mix, 0.46, rng, drive=1.6, room=0.3, wet=0.24, room_lo=400, room_hi=10000, fade_out=0.09)


def arcane_blast(rng):
    n = samples(1.15)
    t = np.arange(n) / SR
    sub = thump(n, 100, 37, 0.36, 0.08)
    fall = glide(1.0, 0.42, n, 0.5)
    bells = (fm(520.0 * fall, 1.41, 7.0 * decay(n, 0.15) + 0.3) * decay(n, 0.3, 0.002)
             + (fm(783.0 * fall, 2.76, 4.0 * decay(n, 0.1) + 0.2) * decay(n, 0.22, 0.002) * 0.6))
    cloud = shimmer(n, rng, 12, 900, 4600, 0.7) * decay(n, 0.4, 0.01)
    cloud *= 0.7 + (0.3 * np.sin(2.0 * np.pi * 11.0 * t))
    sweep = svf(noise(n, rng), glide(6500, 350, n, 0.5), 2.5) * decay(n, 0.26, 0.002)
    tick = crack(n, rng, 0.006, 800, 14000)
    hum = (np.sin(2.0 * np.pi * 110.0 * t) + np.sin(2.0 * np.pi * 110.8 * t)) * decay(n, 0.45, 0.03)
    mix = ((sub * 0.45) + (unit(bells) * 0.9) + (unit(cloud) * 0.6) + (unit(sweep) * 0.65) + (unit(tick) * 0.6)
           + (unit(hum) * 0.2))
    return finish(mix, 1.3, rng, drive=2.0, room=0.6, wet=0.28, room_lo=200, room_hi=9000, fade_out=0.3)


# --------------------------------------------------------------------------------------------
# Nature: soft woody thump, leafy rustle, rising chime
# --------------------------------------------------------------------------------------------

def knock(n: int, rng: np.random.Generator, modes, tau: float) -> np.ndarray:
    """Struck wood: a few short resonances and a soft click."""
    body = np.zeros(n)
    for index, mode in enumerate(modes):
        body += np.sin(2.0 * np.pi * mode * np.arange(n) / SR) * decay(n, tau / (1.0 + (0.6 * index)), 0.001) \
            / (1.0 + (0.5 * index))
    click = spectral(noise(n, rng), 350, 2200) * decay(n, 0.006, 0.0005)
    return unit(body) + (unit(click) * 0.5)


def rustle(n: int, rng: np.random.Generator, lo: float, hi: float, rate: float) -> np.ndarray:
    """Leaves: bright noise broken up by a fast uneven gate."""
    gate = np.abs(spectral(noise(n, rng), 6.0, rate, order=1))
    gate = unit(gate) ** 1.5
    return spectral(noise(n, rng), lo, hi) * gate


def chime(n: int, notes, spacing: float, tau: float) -> np.ndarray:
    """A run of soft bell notes, each `spacing` seconds after the last."""
    out = np.zeros(n)
    for index, note in enumerate(notes):
        length = n - samples(spacing * index)
        if length <= 0:
            break
        t = np.arange(length) / SR
        voice = (np.sin(2.0 * np.pi * note * t) + (0.28 * np.sin(2.0 * np.pi * note * 2.76 * t) * np.exp(-t / (tau * 0.4)))
                 + (0.12 * np.sin(2.0 * np.pi * note * 5.4 * t) * np.exp(-t / (tau * 0.2))))
        place(out, voice * decay(length, tau, 0.004), spacing * index)
    return out


def nature_cast(rng):
    n = samples(0.55)
    wood = np.zeros(n)
    place(wood, knock(samples(0.2), rng, (420.0, 910.0, 1480.0), 0.03), 0.01)
    low = thump(n, 190, 115, 0.06, 0.03)
    leaves = rustle(n, rng, 2500, 9500, 55.0) * swell(n, 0.16, 1.2, 0.14)
    notes = chime(n, (660.0, 880.0, 1320.0), 0.075, 0.16)
    breeze = svf(noise(n, rng), glide(700, 2600, n), 1.5) * decay(n, 0.18, 0.08)
    mix = (wood * 0.7) + (low * 0.7) + (unit(leaves) * 0.55) + (unit(notes) * 0.6) + (unit(breeze) * 0.3)
    return finish(mix, 0.55, rng, drive=1.3, room=0.3, wet=0.22, room_lo=300, room_hi=8000, fade_out=0.1)


def nature_impact(rng):
    n = samples(0.4)
    wood = knock(n, rng, (300.0, 640.0, 1180.0, 1910.0), 0.045)
    low = thump(n, 150, 82, 0.075, 0.025)
    leaves = rustle(n, rng, 2200, 9000, 70.0) * decay(n, 0.09, 0.004)
    snapped = grains(n, rng, 10, 0.01, 0.2, 0.003, 700, 3500, cluster=1.5, fade=0.08)
    note = chime(n, (990.0,), 0.0, 0.1)
    mix = (wood * 0.9) + (low * 0.4) + (unit(leaves) * 0.55) + (unit(snapped) * 0.4) + (unit(note) * 0.25)
    return finish(mix, 0.4, rng, drive=1.6, room=0.25, wet=0.2, room_lo=250, room_hi=8000, fade_out=0.08)


def nature_blast(rng):
    n = samples(1.05)
    sub = thump(n, 105, 40, 0.32, 0.07)
    wood = np.zeros(n)
    for _ in range(9):
        modes = tuple(rng.uniform(0.8, 1.3) * mode for mode in (260.0, 580.0, 1090.0))
        place(wood, knock(samples(0.2), rng, modes, rng.uniform(0.03, 0.06)), rng.random() ** 2.2 * 0.5,
              rng.uniform(0.4, 1.0))
    leaves = rustle(n, rng, 1800, 9500, 45.0) * decay(n, 0.3, 0.01)
    earth = spectral(noise(n, rng), 40, 260) * (0.4 + (0.6 * slow(n, rng, 10.0))) * decay(n, 0.34, 0.01)
    burst = spectral(noise(n, rng), 250, 3000) * decay(n, 0.07, 0.001)
    notes = chime(n, (523.3, 659.3, 784.0, 1046.5, 1318.5), 0.07, 0.28)
    splinters = grains(n, rng, 40, 0.03, 0.8, 0.0025, 900, 5000, cluster=1.7, fade=0.3)
    mix = ((sub * 0.42) + (unit(wood) * 0.85) + (unit(leaves) * 0.6) + (unit(earth) * 0.4) + (unit(burst) * 0.7)
           + (unit(notes) * 0.3) + (unit(splinters) * 0.35))
    return finish(mix, 1.2, rng, drive=2.0, room=0.5, wet=0.22, room_lo=120, room_hi=7500, fade_out=0.28)


# --------------------------------------------------------------------------------------------
# Necrotic: sub drone, reversed-envelope swell, hollow formant whisper
# --------------------------------------------------------------------------------------------

def whisper(n: int, rng: np.random.Generator, f1, f2, breath_rate: float) -> np.ndarray:
    """A hollow voice with no words: noise through two narrow moving formants."""
    source = noise(n, rng)
    voiced = (svf(source, f1, 9.0) * 1.0) + (svf(source, f2, 11.0) * 0.7)
    return voiced * (0.35 + (0.65 * slow(n, rng, breath_rate)))


def drone(n: int, pitches, lowpass: float) -> np.ndarray:
    """Close low saws that beat, darkened to a growl."""
    out = np.zeros(n)
    for pitch in pitches:
        out += saw(np.full(n, pitch))
    return spectral(out, 30, lowpass)


def necrotic_cast(rng):
    n = samples(0.65)
    t = np.arange(n) / SR
    peak = 0.46
    sub = (np.sin(2.0 * np.pi * 55.0 * t) + np.sin(2.0 * np.pi * 58.2 * t)) * swell(n, peak, 1.4, 0.07)
    growl = drone(n, (55.0, 58.2, 82.4), 420.0) * swell(n, peak, 2.0, 0.05)
    rise = spectral(noise(n, rng), 300, 1400) * swell(n, peak, 3.2, 0.02)
    voice = whisper(n, rng, glide(520, 760, n), glide(1050, 1320, n), 14.0) * swell(n, peak + 0.02, 1.6, 0.09)
    thunk = np.zeros(n)
    place(thunk, thump(samples(0.2), 120, 48, 0.07, 0.03), peak)
    mix = (unit(sub) * 0.4) + (unit(growl) * 0.7) + (unit(rise) * 0.6) + (unit(voice) * 0.75) + (thunk * 0.5)
    return finish(mix, 0.65, rng, drive=2.0, room=0.3, wet=0.2, room_lo=150, room_hi=4500, fade_in=0.01,
                  fade_out=0.08)


def necrotic_impact(rng):
    n = samples(0.5)
    hit_at = 0.07
    rise = spectral(noise(n, rng), 250, 1600) * swell(n, hit_at, 2.5, 0.015)
    thud = np.zeros(n)
    place(thud, thump(samples(0.4), 135, 46, 0.12, 0.04), hit_at)
    growl = np.zeros(n)
    place(growl, drone(samples(0.4), (55.0, 58.4, 69.3), 480.0) * decay(samples(0.4), 0.1, 0.003), hit_at)
    slap = np.zeros(n)
    place(slap, spectral(noise(samples(0.2), rng), 200, 1800) * decay(samples(0.2), 0.025, 0.001), hit_at)
    voice = whisper(n, rng, glide(680, 430, n), glide(1250, 880, n), 18.0) * swell(n, hit_at + 0.03, 1.0, 0.13)
    mix = (unit(rise) * 0.55) + (thud * 0.5) + (unit(growl) * 0.7) + (unit(slap) * 0.7) + (unit(voice) * 0.7)
    return finish(mix, 0.5, rng, drive=2.4, room=0.3, wet=0.2, room_lo=120, room_hi=4000, fade_in=0.006,
                  fade_out=0.1)


def necrotic_blast(rng):
    n = samples(1.3)
    t = np.arange(n) / SR
    sub = thump(n, 85, 30, 0.5, 0.12)
    beat = (np.sin(2.0 * np.pi * 43.0 * t) + np.sin(2.0 * np.pi * 45.6 * t)) * decay(n, 0.5, 0.02)
    growl = drone(n, (55.0, 58.3, 82.0, 87.3), 520.0) * decay(n, 0.34, 0.004)
    slam = spectral(noise(n, rng), 120, 1500) * decay(n, 0.06, 0.001)
    dark = svf(noise(n, rng), glide(1800, 220, n, 0.5), 0.9, "lp") * decay(n, 0.36, 0.004)
    choir = whisper(n, rng, glide(820, 400, n, 0.7), glide(1400, 760, n, 0.7), 9.0) * decay(n, 0.5, 0.06)
    high = whisper(n, rng, glide(2300, 1500, n), glide(3100, 2100, n), 16.0) * decay(n, 0.3, 0.08)
    mix = ((sub * 0.5) + (unit(beat) * 0.3) + (unit(growl) * 0.8) + (unit(slam) * 0.8) + (unit(dark) * 0.8)
           + (unit(choir) * 0.75) + (unit(high) * 0.18))
    return finish(mix, 1.5, rng, drive=2.6, room=0.65, wet=0.26, room_lo=80, room_hi=3800, fade_out=0.35)


# --------------------------------------------------------------------------------------------
# Shared one-shots
# --------------------------------------------------------------------------------------------

def windup(rng):
    """A riser that ends on the release: the director pitches it to the length of the wind-up."""
    n = samples(0.6)
    t = np.arange(n) / SR
    env = np.clip(t / 0.57, 0.0, 1.0) ** 2.2
    air = svf(noise(n, rng), glide(300, 5200, n, 1.3), 2.2) * env
    tone = osc(glide(190, 1150, n, 1.4)) + (0.5 * osc(glide(285, 1730, n, 1.4)))
    flutter = 0.6 + (0.4 * np.sin(2.0 * np.pi * np.cumsum(glide(7.0, 34.0, n)) / SR))
    motes = pings(n, rng, 18, 0.1, 0.56, 1800, 6500, 0.004, 0.012, cluster=0.6)
    mix = (unit(air) * 0.8) + (unit(tone * flutter * env) * 0.6) + (unit(motes * env) * 0.3)
    return finish(mix, 0.6, rng, drive=1.4, room=0.12, wet=0.1, fade_in=0.02, fade_out=0.03)


def fizzle(rng):
    """A spell that came apart: a falling wobble and a sputter that chokes off."""
    n = samples(0.45)
    t = np.arange(n) / SR
    fall = glide(950, 110, n, 0.6) * (1.0 + (0.08 * np.sin(2.0 * np.pi * 27.0 * t)))
    tone = osc(fall) * decay(n, 0.13, 0.004)
    steps = int(math.ceil(n / (SR / 48.0))) + 1
    gate = np.repeat(rng.choice((0.0, 0.3, 1.0), steps), int(SR / 48.0))[:n]
    gate = spectral(gate, None, 700.0, order=1)
    sputter = svf(noise(n, rng), glide(4500, 280, n, 0.6), 1.2, "lp") * gate * decay(n, 0.15, 0.002)
    pops = grains(n, rng, 14, 0.02, 0.36, 0.002, 500, 4000, cluster=1.2, fade=0.18)
    mix = (tone * 0.6) + (unit(sputter) * 0.9) + (unit(pops) * 0.45)
    return finish(mix, 0.45, rng, drive=1.6, room=0.2, wet=0.12, fade_out=0.09)


def ward_break(rng):
    """A ward giving way: glass going and the spell in it falling out of tune."""
    n = samples(0.62)
    snap = crack(n, rng, 0.006, 1200, 16000)
    fall = glide(1.0, 0.46, n, 0.6)
    bell = fm(1500.0 * fall, 2.76, 5.0 * decay(n, 0.1) + 0.3) * decay(n, 0.18, 0.002)
    shards = pings(n, rng, 90, 0.0, 0.5, 2200, 10500, 0.004, 0.025, cluster=1.9, fade=0.18)
    cloud = shimmer(n, rng, 8, 1500, 5200, 0.55) * decay(n, 0.2, 0.004)
    low = thump(n, 240, 90, 0.07, 0.03)
    mix = (unit(snap) * 0.8) + (bell * 0.6) + (unit(shards) * 0.75) + (unit(cloud) * 0.45) + (low * 0.5)
    return finish(mix, 0.7, rng, drive=1.5, room=0.4, wet=0.26, room_lo=600, room_hi=11000, fade_out=0.15)


def freeze(rng):
    """Ice taking hold: a creeping crackle that closes into one hard ring."""
    n = samples(0.55)
    lock = 0.3
    creep = grains(n, rng, 70, 0.0, lock, 0.0009, 2500, 13000, cluster=0.55)
    creep *= np.clip(np.arange(n) / SR / lock, 0.05, 1.0) ** 1.2
    squeak = osc(glide(1150, 2900, n, 1.2)) * swell(n, lock, 2.0, 0.02)
    hiss = spectral(noise(n, rng), 6000, 15000) * swell(n, lock, 1.5, 0.08)
    ring = np.zeros(n)
    place(ring, partials(samples(0.25), 1760.0, GLASS, (0.11, 0.09, 0.08, 0.06, 0.05, 0.04, 0.03),
                         (1.0, 0.8, 0.6, 0.45, 0.35, 0.25, 0.2), rng), lock)
    tick = np.zeros(n)
    place(tick, crack(samples(0.1), rng, 0.004, 2000, 16000), lock)
    clunk = np.zeros(n)
    place(clunk, thump(samples(0.2), 330, 150, 0.04, 0.015), lock)
    mix = ((unit(creep) * 0.6) + (squeak * 0.2) + (unit(hiss) * 0.3) + (unit(ring) * 0.85) + (unit(tick) * 0.7)
           + (clunk * 0.5))
    return finish(mix, 0.6, rng, drive=1.3, room=0.3, wet=0.22, room_lo=900, room_hi=12000, fade_in=0.004,
                  fade_out=0.1)


def heal(rng):
    """Mending: a warm rising major arpeggio over a soft swell."""
    n = samples(0.75)
    t = np.arange(n) / SR
    notes = chime(n, (523.3, 659.3, 784.0, 1046.5), 0.085, 0.3)
    pad = ((np.sin(2.0 * np.pi * 261.6 * t) + (0.7 * np.sin(2.0 * np.pi * 392.0 * t))
            + (0.5 * np.sin(2.0 * np.pi * 262.9 * t))) * swell(n, 0.3, 1.2, 0.2))
    sparkle = pings(n, rng, 20, 0.08, 0.6, 2600, 7000, 0.008, 0.03, cluster=0.9, fade=0.4)
    air = svf(noise(n, rng), glide(1500, 5000, n), 2.0) * swell(n, 0.25, 1.0, 0.18)
    mix = (unit(notes) * 0.9) + (unit(pad) * 0.5) + (unit(sparkle) * 0.25) + (unit(air) * 0.14)
    return finish(mix, 0.8, rng, drive=1.15, room=0.4, wet=0.28, room_lo=400, room_hi=9000, fade_in=0.006,
                  fade_out=0.16)


def blink(rng):
    """A step through nothing: air pulled in, a pop, and a glint where the caster lands."""
    n = samples(0.4)
    pop_at = 0.11
    pull = svf(noise(n, rng), glide(450, 7500, n, 0.35), 2.5) * swell(n, pop_at, 1.6, 0.012)
    pop = np.zeros(n)
    place(pop, osc(glide(1900, 260, samples(0.12), 0.4)) * decay(samples(0.12), 0.022, 0.0008), pop_at)
    snap = np.zeros(n)
    place(snap, crack(samples(0.1), rng, 0.004, 1500, 14000), pop_at)
    glint = np.zeros(n)
    place(glint, shimmer(samples(0.28), rng, 5, 2600, 6400, 1.25) * decay(samples(0.28), 0.07, 0.004), pop_at + 0.01)
    low = np.zeros(n)
    place(low, thump(samples(0.2), 170, 75, 0.05, 0.02), pop_at)
    mix = (unit(pull) * 0.8) + (pop * 0.7) + (unit(snap) * 0.5) + (unit(glint) * 0.45) + (low * 0.6)
    return finish(mix, 0.4, rng, drive=1.5, room=0.25, wet=0.2, room_lo=500, room_hi=10000, fade_in=0.004,
                  fade_out=0.08)


SYNTHS = {
    "fire_cast": fire_cast, "fire_impact": fire_impact, "fire_blast": fire_blast,
    "frost_cast": frost_cast, "frost_impact": frost_impact, "frost_blast": frost_blast,
    "lightning_cast": lightning_cast, "lightning_impact": lightning_impact, "lightning_blast": lightning_blast,
    "arcane_cast": arcane_cast, "arcane_impact": arcane_impact, "arcane_blast": arcane_blast,
    "nature_cast": nature_cast, "nature_impact": nature_impact, "nature_blast": nature_blast,
    "necrotic_cast": necrotic_cast, "necrotic_impact": necrotic_impact, "necrotic_blast": necrotic_blast,
    "windup": windup, "fizzle": fizzle, "ward_break": ward_break, "freeze": freeze, "heal": heal,
    "blink": blink, "thunder": thunder,
}


def cue_names() -> list[str]:
    """The 25 file stems, in the order `SpellAudio.AllCues` lists the cues."""
    return [f"{school}_{event}" for school in SCHOOLS for event in EVENTS] + list(ONE_SHOTS)


def cue_id(name: str) -> str:
    if name in ONE_SHOTS:
        return CUE_PREFIX + name
    school, event = name.split("_")
    return f"{CUE_PREFIX}{school}.{event}"


def kind_of(name: str) -> str:
    return "oneshot" if name in ONE_SHOTS else name.split("_")[1]


# --------------------------------------------------------------------------------------------
# Render, verify, encode
# --------------------------------------------------------------------------------------------

def to_pcm(x: np.ndarray) -> np.ndarray:
    return np.round(x * 32767.0).astype("<i2")


def measure(name: str, x: np.ndarray, pcm: np.ndarray) -> tuple[dict, list[str]]:
    """The numbers the table prints, and what is wrong with the cue (nothing, when it is right)."""
    problems: list[str] = []
    if not np.all(np.isfinite(x)):
        problems.append("NaN or infinity in the signal")
        x = np.nan_to_num(x)
    seconds = len(pcm) / SR
    peak = float(np.max(np.abs(pcm))) / 32767.0
    rms = float(np.sqrt(np.mean((pcm / 32767.0) ** 2)))
    dc = float(np.mean(pcm / 32767.0))
    peak_db = 20.0 * math.log10(peak) if peak > 0.0 else -120.0
    rms_db = 20.0 * math.log10(rms) if rms > 0.0 else -120.0
    lo, hi = DURATION_RANGE[kind_of(name)]
    if not lo <= seconds <= hi:
        problems.append(f"length {seconds:.3f}s outside {lo}..{hi}")
    if abs(peak_db - PEAK_DBFS) > 0.05:
        problems.append(f"peak {peak_db:.2f} dBFS, wanted {PEAK_DBFS}")
    if int(np.max(np.abs(pcm.astype(np.int32)))) >= 32767:
        problems.append("clipped")
    if not -30.0 <= rms_db <= -9.0:
        problems.append(f"RMS {rms_db:.1f} dBFS is out of the usable band")
    if abs(dc) > 0.002:
        problems.append(f"DC offset {dc:.5f}")
    if pcm[0] != 0 or pcm[-1] != 0:
        problems.append("does not start and end on zero")
    if np.max(np.abs(pcm[:4])) > 400 or np.max(np.abs(pcm[-samples(0.001):])) > 400:
        problems.append("an end is not faded")
    floats = pcm / 32767.0
    power = np.abs(np.fft.rfft(floats)) ** 2
    freqs = np.fft.rfftfreq(len(floats), 1.0 / SR)
    centroid = float(np.sum(freqs * power) / np.sum(power))
    sub = float(np.sum(power[freqs < 120.0]) / np.sum(power))
    last = floats[-len(floats) // 5:]
    tail_db = 20.0 * math.log10(max(float(np.sqrt(np.mean(last ** 2))), 1e-6)) - rms_db
    if kind_of(name) == "blast" and not 0.15 <= sub <= 0.7:
        problems.append(f"a blast whose sub-bass body is missing or drowns it ({sub:.0%} of its energy under 120 Hz)")
    if kind_of(name) == "blast" and tail_db > -8.0:
        problems.append(f"a blast whose tail does not decay ({tail_db:.1f} dB against the whole)")
    stats = {
        "cue": cue_id(name),
        "file": name + ".ogg",
        "seed": SEED_BASE + cue_names().index(name),
        "seconds": round(seconds, 4),
        "peak_dbfs": round(peak_db, 2),
        "rms_dbfs": round(rms_db, 2),
        "dc": round(dc, 6),
        "centroid_hz": round(centroid),
        "sub_share": round(sub, 3),
        "tail_db": round(tail_db, 1),
        "pcm_sha256": hashlib.sha256(pcm.tobytes()).hexdigest(),
    }
    return stats, problems


def render_all() -> tuple[dict[str, np.ndarray], dict[str, dict], list[str]]:
    pcms: dict[str, np.ndarray] = {}
    table: dict[str, dict] = {}
    failures: list[str] = []
    for index, name in enumerate(cue_names()):
        rng = np.random.default_rng(SEED_BASE + index)
        signal = SYNTHS[name](rng)
        pcm = to_pcm(signal)
        stats, problems = measure(name, signal, pcm)
        pcms[name] = pcm
        table[name] = stats
        failures.extend(f"{name}: {problem}" for problem in problems)
    return pcms, table, failures


def print_table(table: dict[str, dict]) -> None:
    print(f"{'cue':<28} {'file':<22} {'sec':>6} {'peak dB':>8} {'rms dB':>7} {'crest':>6} {'dc':>9} {'centroid':>8} {'sub':>5} {'tail':>6}")
    for name, row in table.items():
        crest = row["peak_dbfs"] - row["rms_dbfs"]
        print(f"{row['cue']:<28} {row['file']:<22} {row['seconds']:>6.3f} {row['peak_dbfs']:>8.2f} "
              f"{row['rms_dbfs']:>7.2f} {crest:>6.1f} {row['dc']:>9.6f} {row['centroid_hz']:>8d} {row['sub_share']:>5.2f} "
              f"{row['tail_db']:>6.1f}")


def write_wav(path: Path, pcm: np.ndarray) -> None:
    with wave.open(str(path), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes(pcm.tobytes())


def encode(wav: Path, ogg: Path) -> None:
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(wav), "-map_metadata", "-1", "-fflags", "+bitexact",
         "-flags:a", "+bitexact", "-ac", "1", "-ar", str(SR), "-c:a", "libvorbis", "-q:a", "5", str(ogg)],
        check=True)


def check(table: dict[str, dict]) -> list[str]:
    if not MANIFEST.exists():
        return [f"no manifest at {MANIFEST}"]
    recorded = json.loads(MANIFEST.read_text(encoding="utf-8")).get("cues", {})
    problems = []
    for name, row in table.items():
        if name not in recorded:
            problems.append(f"{name}: not in the manifest")
        elif recorded[name].get("pcm_sha256") != row["pcm_sha256"]:
            problems.append(f"{name}: PCM differs from the manifest")
        if not (OUT_DIR / row["file"]).exists():
            problems.append(f"{name}: {row['file']} is missing")
    problems.extend(f"{name}: in the manifest but no longer generated" for name in recorded if name not in table)
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="compare PCM hashes with manifest.json; write nothing")
    parser.add_argument("--keep-wav", metavar="DIR", help="also copy the wavs here")
    args = parser.parse_args()

    pcms, table, failures = render_all()
    print_table(table)
    if failures:
        print("\nFAIL:")
        for failure in failures:
            print("  " + failure)
        return 1

    if args.check:
        problems = check(table)
        for problem in problems:
            print("  " + problem)
        print("\ncheck: " + ("FAIL" if problems else f"OK, {len(table)} cues match the manifest"))
        return 1 if problems else 0

    if shutil.which("ffmpeg") is None:
        print("ffmpeg is not on PATH")
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="embervale_spell_sfx_") as temp:
        for name, pcm in pcms.items():
            wav = Path(temp) / (name + ".wav")
            write_wav(wav, pcm)
            encode(wav, OUT_DIR / (name + ".ogg"))
            if args.keep_wav:
                Path(args.keep_wav).mkdir(parents=True, exist_ok=True)
                shutil.copy2(wav, Path(args.keep_wav) / wav.name)

    manifest = {
        "generator": "tools/gen_spell_sfx.py",
        "sample_rate": SR,
        "channels": 1,
        "bits": 16,
        "peak_dbfs": PEAK_DBFS,
        "encoder": "ffmpeg libvorbis -q:a 5",
        "cues": table,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"\nwrote {len(table)} cues to {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
