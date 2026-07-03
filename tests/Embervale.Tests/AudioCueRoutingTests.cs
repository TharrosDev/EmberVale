using Embervale.Audio;
using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the pure cue-id → bus / positional routing (Phase 31A). This is the naming convention the whole
/// game answers to when it requests a sound, so a wrong prefix map silently routes cues to the wrong bus
/// (e.g. a swing on the Music bus, unaffected by the SFX slider). Godot-free, so it runs under dotnet test.
/// </summary>
public class AudioCueRoutingTests
{
    [Theory]
    [InlineData("sfx.combat.swing", AudioBuses.Sfx)]
    [InlineData("sfx.pickup", AudioBuses.Sfx)]
    [InlineData("step.stone", AudioBuses.Sfx)]
    [InlineData("music.boss_defeat", AudioBuses.Music)]
    [InlineData("amb.forest.day", AudioBuses.Ambience)]
    [InlineData("ui.click", AudioBuses.Ui)]
    [InlineData("voice.kael.greeting", AudioBuses.Voice)]
    public void BusFor_RoutesByPrefix(string cueId, string expectedBus) =>
        Assert.Equal(expectedBus, AudioCueRouting.BusFor(cueId));

    [Theory]
    [InlineData("")]
    [InlineData("unprefixed")]
    [InlineData("sound.weird")]
    public void BusFor_UnknownPrefixFallsBackToMaster(string cueId) =>
        Assert.Equal(AudioBuses.Master, AudioCueRouting.BusFor(cueId));

    [Theory]
    [InlineData("sfx.combat.hit", true)]
    [InlineData("step.grass", true)]
    [InlineData("music.explore", false)]
    [InlineData("ui.confirm", false)]
    [InlineData("amb.cave", false)]
    [InlineData("voice.line", false)]
    [InlineData("", false)]
    public void IsPositional_TrueOnlyForWorldSounds(string cueId, bool expected) =>
        Assert.Equal(expected, AudioCueRouting.IsPositional(cueId));
}
