using System;

namespace Embervale.Combat;

/// <summary>Where a piece of trauma came from. Each source keeps its own level and cap in a
/// <see cref="TraumaPool"/>, so spamming one kind (a run of blocks) cannot climb past that kind's cap.</summary>
public enum ShakeSource
{
    /// <summary>A landed blow on the player that did not block, below the heavy threshold.</summary>
    Hit,

    /// <summary>A blow that took a large share of the player's health.</summary>
    Heavy,

    /// <summary>A blow the player's guard took.</summary>
    Block,

    /// <summary>A critical hit, dealt or taken.</summary>
    Crit,

    /// <summary>A poise break: the player's own, or one close by.</summary>
    Stagger,

    /// <summary>A spell cast's authored kick.</summary>
    Spell,

    /// <summary>Any other action's authored kick (<c>ActionDefinitionResource.CameraImpulse</c>).</summary>
    Action,

    /// <summary>A hard landing.</summary>
    Landing,
}

/// <summary>A resolved shake: which source it feeds and how much trauma it adds. <see cref="None"/>
/// when the event does not shake the player's camera.</summary>
public readonly record struct ShakeHit(ShakeSource Source, float Trauma)
{
    public static readonly ShakeHit None = new(ShakeSource.Hit, 0f);
}

/// <summary>The continuous shudder for one frame: camera-space offsets in metres and roll in radians.</summary>
public readonly record struct Shudder(float OffsetX, float OffsetY, float Roll);

/// <summary>A one-off directional kick's peak: offsets in metres and roll in radians, in the camera's
/// rest space. See <see cref="ShakeMath.KickFor"/> for the sign convention.</summary>
public readonly record struct DirectionalKick(float OffsetX, float OffsetZ, float Roll)
{
    public static readonly DirectionalKick None = new(0f, 0f, 0f);
}

/// <summary>
/// Pure tuning maths for <b>camera shake</b>: the trauma model. Combat states add trauma per source;
/// the visible amplitude is trauma² (light hits barely nudge, big ones snap), and a slow smooth noise
/// (not white noise) turns it into a shudder that reads as weight rather than static. A hit from a
/// known side also gives a one-off directional kick that a critically damped spring settles.
/// Godot-free so the feel curve is unit-testable; <see cref="CameraShake"/> feeds it events and the
/// camera rig sums the result. All knobs live here.
///
/// <para>Restraint is deliberate: full trauma is unreachable (<see cref="ComfortCeiling"/>), so the
/// worst stack of everything at once still moves the camera by centimetres and about a degree.</para>
/// </summary>
public static class ShakeMath
{
    /// <summary>Trauma added per combat state. Crit hits hardest, a block softest.</summary>
    public const float CritTrauma = 0.5f;
    public const float StaggerTrauma = 0.4f;
    public const float BlockTrauma = 0.22f;

    /// <summary>What a stagger near the player (not the player's own) adds; and how near is near.</summary>
    public const float NearStaggerTrauma = 0.15f;
    public const float NearStaggerMetres = 8f;

    /// <summary>Trauma from a landed blow: a scratch, up to the heaviest a single hit adds.</summary>
    public const float LightHitTrauma = 0.12f;
    public const float HeavyHitTrauma = 0.5f;

    /// <summary>The share of max health at which a blow counts as fully heavy.</summary>
    public const float HeavyHitFraction = 0.25f;

    /// <summary>Hard landings: no shake below <see cref="LandingShakeDrop"/> metres, full at
    /// <see cref="LandingShakeFullDrop"/>, where <see cref="LandingTrauma"/> tops out.</summary>
    public const float LandingShakeDrop = 2.5f;
    public const float LandingShakeFullDrop = 10f;
    public const float LandingMaxTrauma = 0.45f;

    /// <summary>Trauma bled off per second.</summary>
    public const float DecayPerSecond = 1.4f;

    /// <summary>The most trauma every source together can hold. Below 1 on purpose: stacking a crit,
    /// a stagger and a spell cannot push the camera past a comfortable shudder.</summary>
    public const float ComfortCeiling = 0.8f;

    /// <summary>Peak shudder offset (metres) and roll (radians) at amplitude 1, which the ceiling keeps
    /// out of reach (0.8² = 0.64 of it).</summary>
    public const float MaxOffset = 0.05f;
    public const float MaxRoll = 0.03f;

    /// <summary>Vertical shudder is a fraction of the horizontal one: heads rock more than they bob.</summary>
    public const float VerticalShare = 0.6f;

    /// <summary>Noise lattice points per second — the shudder's pace. Low enough to read as a heavy
    /// wobble, not a buzz.</summary>
    public const float NoiseHz = 9f;

    /// <summary>Peak directional kick at trauma 1: sideways and back offsets in metres, roll in radians.</summary>
    public const float KickOffset = 0.03f;
    public const float KickBack = 0.03f;
    public const float KickRoll = 0.025f;

    /// <summary>Spring stiffness (rad/s) the kick settles with; ~1/omega seconds to its peak.</summary>
    public const float KickOmega = 13f;

    /// <summary>The biggest kick peak a stack of kicks may reach, as a multiple of a full kick.</summary>
    public const float KickLimit = 1.25f;

    /// <summary>How many sources <see cref="TraumaPool"/> tracks.</summary>
    public static int SourceCount => Enum.GetValues<ShakeSource>().Length;

    /// <summary>The most trauma one source may hold, whatever is thrown at it.</summary>
    public static float Cap(ShakeSource source) => source switch
    {
        ShakeSource.Block => 0.3f,
        ShakeSource.Hit => 0.4f,
        ShakeSource.Heavy => 0.6f,
        ShakeSource.Crit => 0.6f,
        ShakeSource.Stagger => 0.5f,
        ShakeSource.Spell => 0.4f,
        ShakeSource.Action => 0.5f,
        ShakeSource.Landing => LandingMaxTrauma,
        _ => 0.4f,
    };

    /// <summary>Visible shake amplitude (0..1) for a trauma level — quadratic for a snappy feel.</summary>
    public static float Amplitude(float trauma)
    {
        float t = trauma < 0f ? 0f : trauma > 1f ? 1f : trauma;
        return t * t;
    }

    /// <summary>Adds trauma, clamped to [0, 1].</summary>
    public static float Add(float trauma, float amount)
    {
        float t = trauma + amount;
        return t < 0f ? 0f : t > 1f ? 1f : t;
    }

    /// <summary>Bleeds trauma toward 0 over <paramref name="dt"/> seconds, never below 0.</summary>
    public static float Decay(float trauma, float dt)
    {
        float t = trauma - (DecayPerSecond * dt);
        return t < 0f ? 0f : t;
    }

    /// <summary>What a blow the player took adds. A blocked blow is the block tier; otherwise a scratch
    /// up to <see cref="HeavyHitTrauma"/> as the damage nears <see cref="HeavyHitFraction"/> of max
    /// health (heavy source once it gets there); a crit is at least the crit tier. Zero damage adds
    /// nothing unless it was blocked.</summary>
    public static ShakeHit HitTaken(float damage, float maxHealth, bool blocked, bool crit)
    {
        if (blocked)
        {
            return new ShakeHit(ShakeSource.Block, BlockTrauma);
        }

        if (damage <= 0f)
        {
            return ShakeHit.None;
        }

        float share = maxHealth > 0f ? damage / maxHealth : 1f;
        float t = Math.Clamp(share / HeavyHitFraction, 0f, 1f);
        float trauma = LightHitTrauma + ((HeavyHitTrauma - LightHitTrauma) * t);
        if (crit)
        {
            return new ShakeHit(ShakeSource.Crit, Math.Max(trauma, CritTrauma));
        }

        return new ShakeHit(t >= 1f ? ShakeSource.Heavy : ShakeSource.Hit, trauma);
    }

    /// <summary>What a blow the player dealt adds: only an unblocked crit is felt; ordinary hits already
    /// have hit-stop and the authored action impulse.</summary>
    public static ShakeHit HitDealt(bool crit, bool blocked) =>
        crit && !blocked ? new ShakeHit(ShakeSource.Crit, CritTrauma) : ShakeHit.None;

    /// <summary>Trauma of a parry: a hard, short jolt on the stagger source (the clash).</summary>
    public const float ParryTrauma = 0.35f;

    /// <summary>Trauma of the player's guard breaking.</summary>
    public const float GuardBreakTrauma = 0.5f;

    /// <summary>A parry the player made or suffered.</summary>
    public static ShakeHit Parried() => new(ShakeSource.Stagger, ParryTrauma);

    /// <summary>The player's guard giving out.</summary>
    public static ShakeHit GuardBroken() => new(ShakeSource.Stagger, GuardBreakTrauma);

    /// <summary>What a stagger adds: the player's own is the full tier; another actor's only registers
    /// when it is within <see cref="NearStaggerMetres"/>, and softly.</summary>
    public static ShakeHit Stagger(bool isPlayer, float distanceMetres)
    {
        if (isPlayer)
        {
            return new ShakeHit(ShakeSource.Stagger, StaggerTrauma);
        }

        return distanceMetres <= NearStaggerMetres
            ? new ShakeHit(ShakeSource.Stagger, NearStaggerTrauma)
            : ShakeHit.None;
    }

    /// <summary>Trauma for a landing after a drop of <paramref name="dropMetres"/>: none below
    /// <see cref="LandingShakeDrop"/>, rising to <see cref="LandingMaxTrauma"/> at
    /// <see cref="LandingShakeFullDrop"/>.</summary>
    public static float LandingTrauma(float dropMetres)
    {
        float t = (dropMetres - LandingShakeDrop) / (LandingShakeFullDrop - LandingShakeDrop);
        return t <= 0f ? 0f : LandingMaxTrauma * Math.Min(t, 1f);
    }

    /// <summary>Total trauma of a set of per-source levels: the sum, held to the comfort ceiling.</summary>
    public static float Total(ReadOnlySpan<float> levels)
    {
        float sum = 0f;
        foreach (float level in levels)
        {
            sum += level;
        }

        return Math.Min(sum, ComfortCeiling);
    }

    /// <summary>
    /// Smooth 1-D value noise in -1..1: a hashed value at each integer of <paramref name="x"/>, blended
    /// with a quintic ease so it is continuous in value and slope. <paramref name="channel"/> picks an
    /// independent stream. This is what replaces per-frame random jitter, which reads as static and
    /// tires the eye; here the camera drifts and wobbles instead.
    /// </summary>
    public static float Noise(float x, int channel)
    {
        float floor = MathF.Floor(x);
        int i = (int)floor;
        float f = x - floor;
        float u = f * f * f * ((f * ((f * 6f) - 15f)) + 10f);
        float a = Lattice(i, channel);
        float b = Lattice(i + 1, channel);
        return a + ((b - a) * u);
    }

    private static float Lattice(int i, int channel)
    {
        unchecked
        {
            uint h = ((uint)i * 374761393u) + ((uint)channel * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return ((h & 0xFFFFFF) / 8388607.5f) - 1f;
        }
    }

    /// <summary>The continuous shudder at <paramref name="time"/> seconds for a trauma level. Zero at
    /// zero trauma, and never beyond <see cref="MaxOffset"/> / <see cref="MaxRoll"/> times the amplitude.</summary>
    public static Shudder Shake(float trauma, float time)
    {
        float amp = Amplitude(trauma);
        if (amp <= 0f)
        {
            return new Shudder(0f, 0f, 0f);
        }

        float x = time * NoiseHz;
        return new Shudder(
            Noise(x, 0) * amp * MaxOffset,
            Noise(x, 1) * amp * MaxOffset * VerticalShare,
            Noise(x, 2) * amp * MaxRoll);
    }

    /// <summary>
    /// A blow's world direction (the horizontal vector from the player toward where it came from,
    /// <paramref name="dirX"/>/<paramref name="dirZ"/>) in the player's own frame, given the body's yaw in
    /// radians. Returns (side, forward): side is +1 when the blow came from the player's right,
    /// forward +1 when from straight ahead.
    /// </summary>
    public static (float Side, float Forward) ToLocal(float dirX, float dirZ, float yaw)
    {
        float s = MathF.Sin(yaw);
        float c = MathF.Cos(yaw);
        return ((dirX * c) - (dirZ * s), (-dirX * s) - (dirZ * c));
    }

    /// <summary>
    /// The peak of the directional kick for a blow from (<paramref name="side"/>, <paramref name="forward"/>)
    /// in the player's frame, at <paramref name="trauma"/>. The head is pushed away from the blow: from the
    /// right, the camera slides left (negative X) and rolls left (positive roll, camera counter-clockwise);
    /// from the front it is shoved back (positive Z); from behind, forward. No pitch or yaw: those would
    /// move the crosshair. Zero for a zero-length direction.
    /// </summary>
    public static DirectionalKick KickFor(float trauma, float side, float forward)
    {
        float len = MathF.Sqrt((side * side) + (forward * forward));
        float mag = Math.Clamp(trauma, 0f, 1f);
        if (len < 1e-4f || mag <= 0f)
        {
            return DirectionalKick.None;
        }

        float s = side / len;
        float f = forward / len;
        return new DirectionalKick(-s * KickOffset * mag, f * KickBack * mag, s * KickRoll * mag);
    }
}

/// <summary>
/// Per-source trauma. Each <see cref="ShakeSource"/> holds its own level, capped at
/// <see cref="ShakeMath.Cap"/>, and every level bleeds at <see cref="ShakeMath.DecayPerSecond"/>. The
/// <see cref="Total"/> the camera reads is their sum held to <see cref="ShakeMath.ComfortCeiling"/>, so
/// however a fight stacks up, the shake cannot exceed the ceiling.
/// </summary>
public sealed class TraumaPool
{
    private readonly float[] _levels = new float[ShakeMath.SourceCount];

    /// <summary>Adds trauma to one source, clamped to that source's cap. Non-positive amounts are ignored.</summary>
    public void Add(ShakeSource source, float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        ref float level = ref _levels[(int)source];
        level = Math.Min(level + amount, ShakeMath.Cap(source));
    }

    /// <summary>The current level of one source.</summary>
    public float Level(ShakeSource source) => _levels[(int)source];

    /// <summary>Bleeds every source toward zero.</summary>
    public void Step(float dt)
    {
        for (int i = 0; i < _levels.Length; i++)
        {
            _levels[i] = ShakeMath.Decay(_levels[i], dt);
        }
    }

    /// <summary>All sources together, held to the comfort ceiling.</summary>
    public float Total => ShakeMath.Total(_levels);

    public void Clear() => Array.Clear(_levels);
}
