using System.Collections.Generic;
using Embervale.Audio;
using Embervale.Combat;
using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>Which sound a spell makes, how far it is detuned and how loud it sits.</summary>
public class SpellAudioTests
{
    [Fact]
    public void ThereAreTwentyFiveCues_AllDifferent_AllPositionalOnTheSfxBus()
    {
        Assert.Equal(25, SpellAudio.AllCues.Count);
        Assert.Equal(25, new HashSet<string>(SpellAudio.AllCues).Count);
        Assert.All(SpellAudio.AllCues, cue =>
        {
            Assert.StartsWith(SpellAudio.Prefix, cue);
            Assert.Equal(AudioBuses.Sfx, AudioCueRouting.BusFor(cue));
            Assert.True(AudioCueRouting.IsPositional(cue));
            Assert.True(SpellAudio.IsSpellCue(cue));
        });
        Assert.False(SpellAudio.IsSpellCue(SpellAudio.FallbackCast));
    }

    [Theory]
    [InlineData(DamageType.Fire, SpellAudioEvent.Cast, "sfx.spell.fire.cast")]
    [InlineData(DamageType.Frost, SpellAudioEvent.Impact, "sfx.spell.frost.impact")]
    [InlineData(DamageType.Lightning, SpellAudioEvent.Blast, "sfx.spell.lightning.blast")]
    [InlineData(DamageType.Arcane, SpellAudioEvent.Cast, "sfx.spell.arcane.cast")]
    [InlineData(DamageType.Nature, SpellAudioEvent.Impact, "sfx.spell.nature.impact")]
    [InlineData(DamageType.Necrotic, SpellAudioEvent.Blast, "sfx.spell.necrotic.blast")]
    public void ASchoolAndAnEvent_NameTheCue(DamageType school, SpellAudioEvent audioEvent, string cue)
    {
        Assert.Equal(cue, SpellAudio.CueId(school, audioEvent));
        Assert.Contains(cue, SpellAudio.AllCues);
    }

    [Fact]
    public void ASchoolWithNoCues_FallsBackRatherThanGoingSilent()
    {
        Assert.Equal("sfx.cast", SpellAudio.CueId(DamageType.Physical, SpellAudioEvent.Cast));
        Assert.Equal("sfx.combat.hit", SpellAudio.CueId(DamageType.Physical, SpellAudioEvent.Impact));
        Assert.Equal("sfx.combat.hit", SpellAudio.CueId(DamageType.Physical, SpellAudioEvent.Blast));
    }

    [Fact]
    public void TheOneShots_AreTheSevenNamedInTheDesign()
    {
        Assert.Equal(
            new[]
            {
                "sfx.spell.windup", "sfx.spell.fizzle", "sfx.spell.ward_break", "sfx.spell.freeze",
                "sfx.spell.heal", "sfx.spell.blink", "sfx.spell.thunder",
            },
            SpellAudio.OneShots);
    }

    [Theory]
    [InlineData("sfx.spell.fire.cast", "res://assets/audio/sfx/spell/fire_cast.ogg")]
    [InlineData("sfx.spell.ward_break", "res://assets/audio/sfx/spell/ward_break.ogg")]
    public void ACue_HasOneRecordingPath(string cue, string path)
    {
        Assert.Equal(path, SpellAudio.AssetPath(cue));
    }

    [Fact]
    public void EveryCue_HasItsOwnRecordingPath()
    {
        var paths = new HashSet<string>();
        Assert.All(SpellAudio.AllCues, cue => Assert.True(paths.Add(SpellAudio.AssetPath(cue))));
    }

    [Fact]
    public void Pitch_StaysWithinFourPercent()
    {
        Assert.Equal(0.96f, SpellAudio.Pitch(0f), 4);
        Assert.Equal(1f, SpellAudio.Pitch(0.5f), 4);
        Assert.Equal(1.04f, SpellAudio.Pitch(1f), 4);
        Assert.Equal(1.04f, SpellAudio.Pitch(7f), 4);
        Assert.Equal(0.96f, SpellAudio.Pitch(-3f), 4);
    }

    [Fact]
    public void TheMix_IsImpactOverCast_AndBlastOverImpact()
    {
        Assert.Equal(0f, SpellAudio.VolumeDb(SpellAudioEvent.Cast, 0.5f, 0f, byPlayer: true), 4);
        Assert.Equal(2f, SpellAudio.VolumeDb(SpellAudioEvent.Impact, 0.5f, 0f, byPlayer: true), 4);
        Assert.Equal(5f, SpellAudio.VolumeDb(SpellAudioEvent.Blast, 0.5f, 0f, byPlayer: true), 4);
        Assert.Equal(-6f, SpellAudio.OneShot(SpellAudio.Windup, byPlayer: true, 0.5f).VolumeDb, 4);
        Assert.Equal(0f, SpellAudio.OneShot(SpellAudio.Heal, byPlayer: true, 0.5f).VolumeDb, 4);
    }

    [Fact]
    public void WeightAndCharge_MakeItLouder_AndAnEnemysSpellIsQuieter()
    {
        float plain = SpellAudio.VolumeDb(SpellAudioEvent.Impact, 0.5f, 0f, byPlayer: true);
        Assert.True(SpellAudio.VolumeDb(SpellAudioEvent.Impact, 1f, 0f, byPlayer: true) > plain);
        Assert.True(SpellAudio.VolumeDb(SpellAudioEvent.Impact, 0f, 0f, byPlayer: true) < plain);
        Assert.True(SpellAudio.VolumeDb(SpellAudioEvent.Impact, 0.5f, 1f, byPlayer: true) > plain);
        Assert.Equal(plain - 3f, SpellAudio.VolumeDb(SpellAudioEvent.Impact, 0.5f, 0f, byPlayer: false), 4);
        Assert.Equal(-9f, SpellAudio.OneShot(SpellAudio.Windup, byPlayer: false, 0.5f).VolumeDb, 4);

        // Out-of-range inputs are clamped, and so is the answer.
        Assert.Equal(
            SpellAudio.VolumeDb(SpellAudioEvent.Blast, 1f, 1f, byPlayer: true),
            SpellAudio.VolumeDb(SpellAudioEvent.Blast, 9f, 9f, byPlayer: true));
        Assert.InRange(SpellAudio.VolumeDb(SpellAudioEvent.Blast, 1f, 1f, byPlayer: true), SpellAudio.MinDb, SpellAudio.MaxDb);
    }

    [Fact]
    public void Resolve_PutsTheThreeAnswersTogether()
    {
        SpellCue cue = SpellAudio.Resolve(DamageType.Fire, SpellAudioEvent.Blast, 0.5f, 0f, byPlayer: false, 1f);
        Assert.Equal("sfx.spell.fire.blast", cue.CueId);
        Assert.Equal(1.04f, cue.PitchScale, 4);
        Assert.Equal(2f, cue.VolumeDb, 4);
    }

    [Fact]
    public void TheSameCueInsideFortyMilliseconds_IsOneSound()
    {
        Assert.True(SpellAudio.Coalesces("sfx.spell.fire.impact", 10.03, "sfx.spell.fire.impact", 10.0));
        Assert.False(SpellAudio.Coalesces("sfx.spell.fire.impact", 10.05, "sfx.spell.fire.impact", 10.0));
        Assert.False(SpellAudio.Coalesces("sfx.spell.fire.impact", 10.01, "sfx.spell.frost.impact", 10.0));
        Assert.False(SpellAudio.Coalesces("sfx.spell.fire.impact", 10.01, null, 0.0));
    }
}
