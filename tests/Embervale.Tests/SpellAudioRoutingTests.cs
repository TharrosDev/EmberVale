using System;
using System.IO;
using System.Text.Json;
using Embervale.Audio;
using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// How the spell cues are routed: which cue a cast takes, what repeats quietly and slowly (a
/// channel's ticks, a zone's pulses), when the riser and the thunder play, that a landed spell
/// takes no melee cue, and that every cue has the recording the generator made for it.
/// </summary>
public class SpellAudioRoutingTests
{
    [Fact]
    public void ACast_TakesItsSchoolsCue_UnlessItBlinksOrOnlyHeals()
    {
        SpellCue fire = SpellAudio.Cast(DamageType.Fire, false, false, false, 0.5f, byPlayer: true, 0.5f);
        Assert.Equal("sfx.spell.fire.cast", fire.CueId);
        Assert.Equal(0f, fire.VolumeDb, 4);

        Assert.Equal(SpellAudio.Blink, SpellAudio.Cast(DamageType.Arcane, true, false, false, 0.1f, true, 0.5f).CueId);
        Assert.Equal(SpellAudio.Heal, SpellAudio.Cast(DamageType.Nature, false, true, false, 0.15f, true, 0.5f).CueId);
        Assert.Equal(SpellAudio.Heal, SpellAudio.Cast(DamageType.Necrotic, false, true, false, 0.15f, true, 0.5f).CueId);

        // A school with no cues still makes the plain cast sound.
        Assert.Equal(SpellAudio.FallbackCast, SpellAudio.Cast(DamageType.Physical, false, false, false, 0.5f, true, 0.5f).CueId);
    }

    [Fact]
    public void AnEnemysCast_IsThreeDecibelsDown_WhateverCueItTakes()
    {
        foreach ((bool blinks, bool heals) in new[] { (false, false), (true, false), (false, true) })
        {
            float mine = SpellAudio.Cast(DamageType.Arcane, blinks, heals, false, 0.5f, byPlayer: true, 0.5f).VolumeDb;
            float theirs = SpellAudio.Cast(DamageType.Arcane, blinks, heals, false, 0.5f, byPlayer: false, 0.5f).VolumeDb;
            Assert.Equal(mine + SpellAudio.EnemyDb, theirs, 4);
        }
    }

    [Fact]
    public void AChannel_PulsesQuietlyAndSlowly_NotOnEveryTick()
    {
        float once = SpellAudio.Cast(DamageType.Lightning, false, false, false, 0.5f, true, 0.5f).VolumeDb;
        float held = SpellAudio.Cast(DamageType.Lightning, false, false, true, 0.5f, true, 0.5f).VolumeDb;
        Assert.Equal(once + SpellAudio.ChannelDb, held, 4);

        Assert.Equal(SpellAudio.CoalesceSeconds, SpellAudio.CastGap(channelled: false));
        Assert.Equal(SpellAudio.ChannelGapSeconds, SpellAudio.CastGap(channelled: true));

        // Storm Conduit ticks every 0.2 s: two of every three ticks are swallowed.
        Assert.True(SpellAudio.TooSoon(10.2, 10.0, SpellAudio.CastGap(true)));
        Assert.True(SpellAudio.TooSoon(10.4, 10.0, SpellAudio.CastGap(true)));
        Assert.False(SpellAudio.TooSoon(10.6, 10.0, SpellAudio.CastGap(true)));
    }

    [Fact]
    public void AZonesPulse_IsNotAMachineGun()
    {
        SpellCue burst = SpellAudio.Burst(DamageType.Frost, zonePulse: false, 0.4f, 0f, byPlayer: true, 0.5f);
        SpellCue pulse = SpellAudio.Burst(DamageType.Frost, zonePulse: true, 0.4f, 0f, byPlayer: true, 0.5f);
        Assert.Equal("sfx.spell.frost.blast", burst.CueId);
        Assert.Equal(burst.CueId, pulse.CueId);
        Assert.Equal(burst.VolumeDb + SpellAudio.ZoneDb, pulse.VolumeDb, 4);

        // Blizzard pulses once a second: every other pulse sounds.
        Assert.True(SpellAudio.TooSoon(11.0, 10.0, SpellAudio.BurstGap(zonePulse: true)));
        Assert.False(SpellAudio.TooSoon(12.0, 10.0, SpellAudio.BurstGap(zonePulse: true)));
        Assert.Equal(SpellAudio.CoalesceSeconds, SpellAudio.BurstGap(zonePulse: false));

        SpellCue hit = SpellAudio.Impact(DamageType.Frost, zonePulse: false, 0.4f, 0f, byPlayer: true, 0.5f);
        SpellCue tick = SpellAudio.Impact(DamageType.Frost, zonePulse: true, 0.4f, 0f, byPlayer: true, 0.5f);
        Assert.Equal("sfx.spell.frost.impact", hit.CueId);
        Assert.Equal(hit.VolumeDb + SpellAudio.ZoneDb, tick.VolumeDb, 4);

        // The floor still holds for the quietest zone pulse of the lightest enemy spell.
        Assert.True(SpellAudio.Burst(DamageType.Frost, true, 0f, 0f, byPlayer: false, 0.5f).VolumeDb >= SpellAudio.MinDb);
    }

    [Fact]
    public void ABlast_SitsOverAnImpact_AndBothFollowWeightAndCharge()
    {
        float impact = SpellAudio.Impact(DamageType.Fire, false, 0.5f, 0f, true, 0.5f).VolumeDb;
        float blast = SpellAudio.Burst(DamageType.Fire, false, 0.5f, 0f, true, 0.5f).VolumeDb;
        Assert.Equal(SpellAudio.ImpactDb, impact, 4);
        Assert.Equal(SpellAudio.BlastDb, blast, 4);
        Assert.True(SpellAudio.Burst(DamageType.Fire, false, 0.9f, 1f, true, 0.5f).VolumeDb > blast);
    }

    [Theory]
    [InlineData(0.15f, false)] // Emberlash, Blink, Thunder Step: on the caster before a riser could rise
    [InlineData(0.25f, false)]
    [InlineData(0.3f, true)]
    [InlineData(0.9f, true)]   // Sunfall
    public void TheRiser_OnlyPlaysUnderAWindupLongEnoughToHearIt(float windup, bool plays)
    {
        Assert.Equal(plays, SpellAudio.PlaysWindup(windup));
    }

    [Fact]
    public void TheRiser_IsPitchedToEndWithTheWindup()
    {
        Assert.Equal(1f, SpellAudio.WindupPitch(SpellAudio.WindupCueSeconds), 4);
        Assert.Equal(1.5f, SpellAudio.WindupPitch(0.4f), 4);
        Assert.Equal(SpellAudio.MinWindupPitch, SpellAudio.WindupPitch(3f), 4);
        Assert.Equal(SpellAudio.MaxWindupPitch, SpellAudio.WindupPitch(0.1f), 4);
        Assert.Equal(SpellAudio.MaxWindupPitch, SpellAudio.WindupPitch(0f), 4);

        SpellCue riser = SpellAudio.WindupRiser(0.4f, byPlayer: true);
        Assert.Equal(SpellAudio.Windup, riser.CueId);
        Assert.Equal(1.5f, riser.PitchScale, 4);
        Assert.Equal(SpellAudio.WindupDb, riser.VolumeDb, 4);
        Assert.Equal(SpellAudio.WindupDb + SpellAudio.EnemyDb, SpellAudio.WindupRiser(0.4f, byPlayer: false).VolumeDb, 4);
    }

    [Theory]
    [InlineData(DamageType.Lightning, 0.6f, 0f, true)]    // Thunder Step
    [InlineData(DamageType.Lightning, 0.5f, 0f, false)]   // Ball Lightning
    [InlineData(DamageType.Lightning, 0.5f, 1f, true)]    // ...unless fully charged
    [InlineData(DamageType.Lightning, 0.15f, 1f, false)]  // Storm Conduit
    [InlineData(DamageType.Fire, 0.9f, 1f, false)]        // Sunfall is loud, but it is not thunder
    public void Thunder_FollowsOnlyAHeavyLightningHit(DamageType school, float weight, float charge, bool thunders)
    {
        Assert.Equal(thunders, SpellAudio.Thunders(school, weight, charge));
    }

    [Fact]
    public void TooSoon_IsAHalfOpenWindowOnOneClock()
    {
        Assert.True(SpellAudio.TooSoon(5.0, 5.0, 0.5));
        Assert.True(SpellAudio.TooSoon(5.49, 5.0, 0.5));
        Assert.False(SpellAudio.TooSoon(5.5, 5.0, 0.5));
        Assert.False(SpellAudio.TooSoon(4.0, 5.0, 0.5)); // a clock that went backwards never mutes
    }

    [Theory]
    [InlineData(HitOutcome.Hit)]
    [InlineData(HitOutcome.Critical)]
    [InlineData(HitOutcome.PoiseBroken)]
    [InlineData(HitOutcome.Resisted)]
    public void ASpellThatLanded_TakesNoMeleeCue(HitOutcome outcome)
    {
        Assert.False(CombatFx.PlaysHitCue(outcome, spellImpact: true));
    }

    [Theory]
    [InlineData(HitOutcome.Blocked)]
    [InlineData(HitOutcome.Parried)]
    [InlineData(HitOutcome.GuardBroken)]
    public void ASpellAGuardMet_KeepsTheGuardsCue(HitOutcome outcome)
    {
        Assert.True(CombatFx.PlaysHitCue(outcome, spellImpact: true));
    }

    // The blow's kind is inferred from the attacker's last action, so a burn's tick, a detonation
    // or a combo's bonus soon after a cast reads as a spell with no impact event behind it.
    [Fact]
    public void EveryBlowWithNoSpellImpactBehindIt_KeepsItsCue()
    {
        foreach (HitOutcome outcome in Enum.GetValues<HitOutcome>())
        {
            Assert.True(CombatFx.PlaysHitCue(outcome, spellImpact: false));
        }
    }

    [Fact]
    public void AChannelsImpacts_AreSofterAndSlowerThanItsTicks()
    {
        SpellCue hit = SpellAudio.Impact(DamageType.Lightning, zonePulse: false, 0.15f, 0f, byPlayer: true, 0.5f);
        SpellCue tick = SpellAudio.ChannelTick(hit);
        Assert.Equal(hit.CueId, tick.CueId);
        Assert.Equal(hit.VolumeDb + SpellAudio.ChannelDb, tick.VolumeDb, 4);
        Assert.True(SpellAudio.ChannelTick(hit with { VolumeDb = SpellAudio.MinDb }).VolumeDb >= SpellAudio.MinDb);

        Assert.Equal(SpellAudio.CoalesceSeconds, SpellAudio.ImpactGap(channelled: false));
        Assert.Equal(SpellAudio.ChannelImpactGapSeconds, SpellAudio.ImpactGap(channelled: true));

        // Storm Conduit ticks every 0.2 s: every other tick sounds. A breath at 0.35 s sounds each.
        Assert.True(SpellAudio.TooSoon(10.2, 10.0, SpellAudio.ImpactGap(true)));
        Assert.False(SpellAudio.TooSoon(10.4, 10.0, SpellAudio.ImpactGap(true)));
        Assert.False(SpellAudio.TooSoon(10.35, 10.0, SpellAudio.ImpactGap(true)));
    }

    [Fact]
    public void EveryCue_HasTheRecordingTheGeneratorMade_AndTheManifestNamesIt()
    {
        string folder = Path.Combine(Root(), "assets", "audio", "sfx", "spell");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        JsonElement cues = manifest.RootElement.GetProperty("cues");

        int named = 0;
        foreach (JsonProperty entry in cues.EnumerateObject())
        {
            named++;
            string cue = entry.Value.GetProperty("cue").GetString()!;
            Assert.Contains(cue, SpellAudio.AllCues);

            // The file the manifest wrote is the file the library will ask for.
            string file = entry.Value.GetProperty("file").GetString()!;
            Assert.Equal(SpellAudio.AssetFolder + file, SpellAudio.AssetPath(cue));
            Assert.Equal(64, entry.Value.GetProperty("pcm_sha256").GetString()!.Length);
        }

        Assert.Equal(SpellAudio.AllCues.Count, named);
        Assert.All(SpellAudio.AllCues, cue =>
        {
            string file = SpellAudio.AssetPath(cue).Substring(SpellAudio.AssetFolder.Length);
            var info = new FileInfo(Path.Combine(folder, file));
            Assert.True(info.Exists, $"{cue} has no recording at {info.FullName}");
            Assert.True(info.Length > 2000, $"{file} is too small to be a sound");
        });
    }

    [Fact]
    public void TheRiserRecording_IsAsLongAsTheRuleThatPitchesItSays()
    {
        string path = Path.Combine(Root(), "assets", "audio", "sfx", "spell", "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(path));
        double seconds = manifest.RootElement.GetProperty("cues").GetProperty("windup").GetProperty("seconds").GetDouble();
        Assert.Equal(SpellAudio.WindupCueSeconds, seconds, 2);
    }

    private static string Root()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Embervale.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Embervale.sln was not found above the test binaries.");
    }
}
