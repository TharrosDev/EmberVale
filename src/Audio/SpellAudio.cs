using System;
using System.Collections.Generic;
using Embervale.Combat;

namespace Embervale.Audio;

/// <summary>The three moments of a spell that take their sound from its school.</summary>
public enum SpellAudioEvent
{
    /// <summary>The spell leaves the caster.</summary>
    Cast,

    /// <summary>It lands on one target.</summary>
    Impact,

    /// <summary>It bursts over an area.</summary>
    Blast,
}

/// <summary>One sound to play: which cue, how far to detune it and how loud against the cast level.</summary>
public readonly record struct SpellCue(string CueId, float PitchScale, float VolumeDb);

/// <summary>
/// Which sound a spell makes. Pure: school and event pick the cue, a caller-supplied roll detunes it
/// by up to <see cref="PitchSpread"/> either way so a repeated cast is not one sample on a loop, and
/// the level comes from the event, the spell's <c>ImpactWeight</c>, the charge it was held to and
/// whose spell it is.
///
/// <para>Eighteen cues are a school times an event (<c>sfx.spell.fire.cast</c>); seven more are
/// shared one-shots. All are positional and on the SFX bus by their <c>sfx.</c> prefix
/// (<see cref="AudioCueRouting"/>), and each has a recording path (<see cref="AssetPath"/>) and a
/// procedural stand-in in <see cref="AudioLibrary"/>.</para>
/// </summary>
public static class SpellAudio
{
    public const string Prefix = "sfx.spell.";

    public const string Windup = Prefix + "windup";
    public const string Fizzle = Prefix + "fizzle";
    public const string WardBreak = Prefix + "ward_break";
    public const string Freeze = Prefix + "freeze";
    public const string Heal = Prefix + "heal";
    public const string Blink = Prefix + "blink";
    public const string Thunder = Prefix + "thunder";

    /// <summary>What a cast sounds like for a school with no cue of its own.</summary>
    public const string FallbackCast = "sfx.cast";

    /// <summary>What an impact or a blast sounds like for a school with no cue of its own.</summary>
    public const string FallbackImpact = "sfx.combat.hit";

    /// <summary>Where the recordings live.</summary>
    public const string AssetFolder = "res://assets/audio/sfx/spell/";

    /// <summary>The most a cue is detuned, either way.</summary>
    public const float PitchSpread = 0.04f;

    // The mix, in dB against a cast.
    public const float CastDb = 0f;
    public const float ImpactDb = 2f;
    public const float BlastDb = 5f;
    public const float WindupDb = -6f;

    /// <summary>A spell that is not the player's is this much quieter.</summary>
    public const float EnemyDb = -3f;

    /// <summary>The swing a spell's <c>ImpactWeight</c> has, from its lightest to its heaviest. The
    /// default weight, 0.5, adds nothing.</summary>
    public const float WeightRangeDb = 6f;

    /// <summary>What a full charge adds.</summary>
    public const float ChargeDb = 3f;

    public const float MinDb = -12f;
    public const float MaxDb = 9f;

    /// <summary>Two plays of one cue closer together than this are one sound.</summary>
    public const double CoalesceSeconds = 0.04;

    /// <summary>How long the wind-up riser recording runs. It ends on its peak, so the director
    /// pitches it to finish as the cast leaves.</summary>
    public const float WindupCueSeconds = 0.6f;

    /// <summary>A wind-up shorter than this has no riser: the cast is on it before it could rise.</summary>
    public const float MinWindupSeconds = 0.3f;

    public const float MinWindupPitch = 0.75f;
    public const float MaxWindupPitch = 1.6f;

    /// <summary>A lingering zone's pulse against a burst of its own: it repeats, so it sits back.</summary>
    public const float ZoneDb = -7f;

    /// <summary>One caster's zone sounds its blast no more often than this, however fast it pulses.</summary>
    public const double ZoneGapSeconds = 1.9;

    /// <summary>A channelled spell casts on every tick; its cast cue is the beam's pulse, and quieter.</summary>
    public const float ChannelDb = -4f;

    /// <summary>One caster's channel sounds its cast cue no more often than this.</summary>
    public const double ChannelGapSeconds = 0.55;

    /// <summary>The weight a lightning spell needs before thunder follows it.</summary>
    public const float ThunderWeight = 0.55f;

    /// <summary>How much of that weight a full charge supplies.</summary>
    public const float ThunderChargeWeight = 0.25f;

    /// <summary>Thunder does not roll over itself.</summary>
    public const double ThunderGapSeconds = 0.9;

    /// <summary>The schools that have cues, in the order the cue list is built.</summary>
    public static readonly IReadOnlyList<DamageType> Schools = new[]
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
    };

    /// <summary>The shared one-shots.</summary>
    public static readonly IReadOnlyList<string> OneShots = new[]
    {
        Windup, Fizzle, WardBreak, Freeze, Heal, Blink, Thunder,
    };

    /// <summary>Every spell cue: each school's cast, impact and blast, then the one-shots.</summary>
    public static readonly IReadOnlyList<string> AllCues = BuildAll();

    /// <summary>A school's name inside a cue id, or empty for one with no cues (Physical).</summary>
    public static string SchoolSlug(DamageType school) => school switch
    {
        DamageType.Fire => "fire",
        DamageType.Frost => "frost",
        DamageType.Lightning => "lightning",
        DamageType.Arcane => "arcane",
        DamageType.Nature => "nature",
        DamageType.Necrotic => "necrotic",
        _ => string.Empty,
    };

    /// <summary>An event's name inside a cue id.</summary>
    public static string EventSlug(SpellAudioEvent audioEvent) => audioEvent switch
    {
        SpellAudioEvent.Impact => "impact",
        SpellAudioEvent.Blast => "blast",
        _ => "cast",
    };

    /// <summary>The cue for <paramref name="school"/> and <paramref name="audioEvent"/>. A school
    /// with no cues falls back to the plain cast or the plain hit, so it is never silent.</summary>
    public static string CueId(DamageType school, SpellAudioEvent audioEvent)
    {
        string slug = SchoolSlug(school);
        if (slug.Length == 0)
        {
            return audioEvent == SpellAudioEvent.Cast ? FallbackCast : FallbackImpact;
        }

        return Prefix + slug + "." + EventSlug(audioEvent);
    }

    /// <summary>Whether <paramref name="cueId"/> is one of the spell cues.</summary>
    public static bool IsSpellCue(string cueId)
    {
        foreach (string cue in AllCues)
        {
            if (cue == cueId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The recording a spell cue loads: the id after the prefix with dots as underscores
    /// (<c>sfx.spell.fire.cast</c> is <c>fire_cast.ogg</c>, <c>sfx.spell.ward_break</c> is
    /// <c>ward_break.ogg</c>).</summary>
    public static string AssetPath(string cueId)
    {
        string name = cueId.StartsWith(Prefix, StringComparison.Ordinal) ? cueId.Substring(Prefix.Length) : cueId;
        return AssetFolder + name.Replace('.', '_') + ".ogg";
    }

    /// <summary>The pitch scale for <paramref name="roll01"/> (0..1, the caller's random number):
    /// 1 less the spread at 0, 1 at a half, 1 plus the spread at 1.</summary>
    public static float Pitch(float roll01) =>
        1f + (((Math.Clamp(roll01, 0f, 1f) * 2f) - 1f) * PitchSpread);

    /// <summary>The event's place in the mix, before the spell's own weight.</summary>
    public static float BaseDb(SpellAudioEvent audioEvent) => audioEvent switch
    {
        SpellAudioEvent.Impact => ImpactDb,
        SpellAudioEvent.Blast => BlastDb,
        _ => CastDb,
    };

    /// <summary>
    /// How loud, in dB against a cast. <paramref name="impactWeight"/> is the spell's authored 0..1
    /// weight and <paramref name="charge"/> the 0..1 a held cast reached.
    /// </summary>
    public static float VolumeDb(SpellAudioEvent audioEvent, float impactWeight, float charge, bool byPlayer)
    {
        float db = BaseDb(audioEvent)
            + ((Math.Clamp(impactWeight, 0f, 1f) - 0.5f) * WeightRangeDb)
            + (Math.Clamp(charge, 0f, 1f) * ChargeDb)
            + (byPlayer ? 0f : EnemyDb);
        return Math.Clamp(db, MinDb, MaxDb);
    }

    /// <summary>The whole answer for a school's cast, impact or blast.</summary>
    public static SpellCue Resolve(
        DamageType school, SpellAudioEvent audioEvent, float impactWeight, float charge, bool byPlayer, float roll01) =>
        new(CueId(school, audioEvent), Pitch(roll01), VolumeDb(audioEvent, impactWeight, charge, byPlayer));

    /// <summary>The whole answer for a shared one-shot. The wind-up riser sits under the cast; the
    /// rest play at the cast's level.</summary>
    public static SpellCue OneShot(string cueId, bool byPlayer, float roll01)
    {
        float db = (cueId == Windup ? WindupDb : CastDb) + (byPlayer ? 0f : EnemyDb);
        return new SpellCue(cueId, Pitch(roll01), db);
    }

    /// <summary>
    /// The sound of a spell leaving its caster. A blink and a pure heal have cues of their own
    /// whatever their school; a channelled spell's cue repeats, so it is <see cref="ChannelDb"/> down.
    /// </summary>
    public static SpellCue Cast(
        DamageType school, bool blinks, bool heals, bool channelled, float impactWeight, bool byPlayer, float roll01)
    {
        SpellCue cue = blinks ? OneShot(Blink, byPlayer, roll01)
            : heals ? OneShot(Heal, byPlayer, roll01)
            : Resolve(school, SpellAudioEvent.Cast, impactWeight, 0f, byPlayer, roll01);
        return channelled ? cue with { VolumeDb = Math.Max(MinDb, cue.VolumeDb + ChannelDb) } : cue;
    }

    /// <summary>A burst's sound: the school's blast, <see cref="ZoneDb"/> down when it is one pulse
    /// of a lingering zone.</summary>
    public static SpellCue Burst(
        DamageType school, bool zonePulse, float impactWeight, float charge, bool byPlayer, float roll01) =>
        Zone(Resolve(school, SpellAudioEvent.Blast, impactWeight, charge, byPlayer, roll01), zonePulse);

    /// <summary>An impact's sound: the school's impact, <see cref="ZoneDb"/> down when a zone's
    /// pulse landed it.</summary>
    public static SpellCue Impact(
        DamageType school, bool zonePulse, float impactWeight, float charge, bool byPlayer, float roll01) =>
        Zone(Resolve(school, SpellAudioEvent.Impact, impactWeight, charge, byPlayer, roll01), zonePulse);

    /// <summary>The shortest time between two blasts from one emitter.</summary>
    public static double BurstGap(bool zonePulse) => zonePulse ? ZoneGapSeconds : CoalesceSeconds;

    /// <summary>The shortest time between two cast cues from one caster.</summary>
    public static double CastGap(bool channelled) => channelled ? ChannelGapSeconds : CoalesceSeconds;

    /// <summary>Whether a wind-up of <paramref name="windupSeconds"/> is long enough for the riser.</summary>
    public static bool PlaysWindup(float windupSeconds) => windupSeconds >= MinWindupSeconds;

    /// <summary>The pitch that makes the riser end as a wind-up of <paramref name="windupSeconds"/>
    /// does, within what still sounds like one sound.</summary>
    public static float WindupPitch(float windupSeconds) => windupSeconds <= 0f
        ? MaxWindupPitch
        : Math.Clamp(WindupCueSeconds / windupSeconds, MinWindupPitch, MaxWindupPitch);

    /// <summary>The wind-up riser for a cast that takes <paramref name="windupSeconds"/>.</summary>
    public static SpellCue WindupRiser(float windupSeconds, bool byPlayer) =>
        OneShot(Windup, byPlayer, 0.5f) with { PitchScale = WindupPitch(windupSeconds) };

    /// <summary>Whether thunder follows a hit: only lightning, and only the heavy spells of it.</summary>
    public static bool Thunders(DamageType school, float impactWeight, float charge) =>
        school == DamageType.Lightning &&
        Math.Clamp(impactWeight, 0f, 1f) + (Math.Clamp(charge, 0f, 1f) * ThunderChargeWeight) >= ThunderWeight;

    /// <summary>Whether <paramref name="now"/> is still inside <paramref name="gap"/> seconds of
    /// <paramref name="last"/>, on one clock.</summary>
    public static bool TooSoon(double now, double last, double gap) => now >= last && now - last < gap;

    private static SpellCue Zone(SpellCue cue, bool zonePulse) =>
        zonePulse ? cue with { VolumeDb = Math.Max(MinDb, cue.VolumeDb + ZoneDb) } : cue;

    /// <summary>Whether a play of <paramref name="cueId"/> at <paramref name="now"/> folds into the
    /// last play of <paramref name="lastCueId"/> at <paramref name="lastTime"/> (seconds on one
    /// clock): a spell that lands on five targets in one frame is one sound.</summary>
    public static bool Coalesces(string cueId, double now, string? lastCueId, double lastTime) =>
        cueId == lastCueId && now >= lastTime && now - lastTime < CoalesceSeconds;

    private static string[] BuildAll()
    {
        var cues = new List<string>();
        foreach (DamageType school in Schools)
        {
            foreach (SpellAudioEvent audioEvent in Enum.GetValues<SpellAudioEvent>())
            {
                cues.Add(CueId(school, audioEvent));
            }
        }

        cues.AddRange(OneShots);
        return cues.ToArray();
    }
}
