using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

public class CombatComfortTests
{
    [Fact]
    public void FullSettingsPassThrough()
    {
        CombatComfort c = CombatComfort.From(0.6f, 0.4f, false, false, 0.8f, reducedMotion: false);
        Assert.Equal(new CombatComfort(0.6f, 0.4f, false, false, 0.8f), c);
    }

    [Fact]
    public void ReducedMotionCapsHitStopAndFlashButLeavesTheAids()
    {
        CombatComfort c = CombatComfort.From(1f, 1f, true, true, 1f, reducedMotion: true);
        Assert.Equal(CombatComfort.ReducedScale, c.HitStop);
        Assert.Equal(CombatComfort.ReducedScale, c.ScreenFlash);
        Assert.True(c.DamageNumbers);
        Assert.True(c.LockOnAssist);
        Assert.Equal(1f, c.AimAssist);
    }

    [Fact]
    public void ReducedMotionNeverRaisesAnAlreadyLowValue()
    {
        Assert.Equal(0.1f, CombatComfort.From(0.1f, 0.1f, true, true, 0f, reducedMotion: true).HitStop);
    }

    [Fact]
    public void OutOfRangeValuesClamp()
    {
        CombatComfort c = CombatComfort.From(5f, -1f, true, true, 9f, reducedMotion: false);
        Assert.Equal(1f, c.HitStop);
        Assert.Equal(0f, c.ScreenFlash);
        Assert.Equal(1f, c.AimAssist);
    }

    [Fact]
    public void NoSettingsServiceMeansFull()
    {
        Assert.Equal(CombatComfort.Full, CombatComfort.From((Embervale.Settings.Settings?)null));
    }

    [Fact]
    public void HitKindOrdinalsAreStable()
    {
        Assert.Equal(0, (int)HitKind.Normal);
        Assert.Equal(1, (int)HitKind.Heavy);
        Assert.Equal(2, (int)HitKind.Charged);
        Assert.Equal(3, (int)HitKind.Riposte);
        Assert.Equal(4, (int)HitKind.Backstab);
        Assert.Equal(5, (int)HitKind.Plunge);
        Assert.Equal(6, (int)HitKind.Ranged);
        Assert.Equal(7, (int)HitKind.Spell);
    }
}
