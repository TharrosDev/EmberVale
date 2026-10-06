using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The comfort limits on the one screen flash spells share. These are the numbers the design
/// promises a player who is sensitive to flashing, so each is pinned.
/// </summary>
public class VfxScreenRulesTests
{
    [Fact]
    public void ThePeakNeverExceedsThirtyFivePercentOfTheFlashSetting()
    {
        Assert.Equal(0.35f, VfxScreenRules.MaxPeak);
        Assert.Equal(0.35f, VfxScreenRules.Peak(1f, 1f, false, true, false), 4);
        Assert.Equal(0.35f, VfxScreenRules.Peak(9f, 5f, false, true, false), 4);   // over-range inputs clamp
        Assert.Equal(0.175f, VfxScreenRules.Peak(1f, 0.5f, false, true, false), 4);
        Assert.Equal(0f, VfxScreenRules.Peak(1f, 0f, false, true, false));          // flashes switched off
    }

    [Fact]
    public void AnEnemysSpellFlashesOnlyWhenItStruckThePlayerAndThenNoBrighterThanItsCap()
    {
        Assert.Equal(0f, VfxScreenRules.Peak(1f, 1f, false, byPlayer: false, hitsPlayer: false));
        Assert.Equal(VfxScreenRules.EnemyHitCap, VfxScreenRules.Peak(1f, 1f, false, byPlayer: false, hitsPlayer: true));
        Assert.Equal(0.2f, VfxScreenRules.EnemyHitCap);
        Assert.True(VfxScreenRules.Peak(0.2f, 1f, false, false, true) < VfxScreenRules.EnemyHitCap);
    }

    [Fact]
    public void ReducedMotionCapsEveryFlash()
    {
        Assert.Equal(0.09f, VfxScreenRules.ReducedMotionCap);
        Assert.Equal(0.09f, VfxScreenRules.Peak(1f, 1f, reducedMotion: true, byPlayer: true, hitsPlayer: false));
        Assert.Equal(0.09f, VfxScreenRules.Peak(1f, 1f, reducedMotion: true, byPlayer: false, hitsPlayer: true));
    }

    [Fact]
    public void TheComfortSettingsOwnReducedScaleAlreadyLandsUnderTheCap()
    {
        // Reduced Motion also caps CombatComfort.ScreenFlash, and the two limits must agree: the
        // flash setting as the game hands it over under Reduced Motion never needs the second cap.
        CombatComfort reduced = CombatComfort.From(1f, 1f, true, true, 0.5f, reducedMotion: true);

        float peak = VfxScreenRules.Peak(1f, reduced.ScreenFlash, false, true, false);

        Assert.True(peak <= VfxScreenRules.ReducedMotionCap);
    }

    [Fact]
    public void FlashesKeepThreeTenthsOfASecondApart()
    {
        Assert.Equal(0.3d, VfxScreenRules.MinInterval);
        Assert.False(VfxScreenRules.Allowed(10.29d, 10d));
        Assert.True(VfxScreenRules.Allowed(10.3d, 10d));
        Assert.True(VfxScreenRules.Allowed(0d, -10d)); // the first flash of a session
    }

    [Theory]
    [InlineData(DamageType.Fire)]
    [InlineData(DamageType.Frost)]
    [InlineData(DamageType.Lightning)]
    [InlineData(DamageType.Arcane)]
    [InlineData(DamageType.Nature)]
    [InlineData(DamageType.Necrotic)]
    public void TheTintIsPulledMostOfTheWayToWarmWhite(DamageType school)
    {
        Color colour = SpellSchools.Color(school);
        Color tint = VfxScreenRules.Tint(colour);

        // Never a saturated full-screen colour, and never a dark one.
        Assert.True(tint.S < 0.45f, $"{school} flash tint is too saturated ({tint.S})");
        Assert.True(tint.S < colour.S || colour.S < 0.01f);
        Assert.True(tint.V > 0.75f, $"{school} flash tint is too dark ({tint.V})");

        // Warm: red is not the weakest channel of the white it is pulled toward.
        Color white = VfxScreenRules.Tint(Colors.White);
        Assert.True(white.R >= white.G && white.G >= white.B);
    }

    [Fact]
    public void TheFlashRisesFastAndFallsSlower()
    {
        const float Peak = 0.3f;

        Assert.Equal(0f, VfxScreenRules.Envelope(0d, Peak));
        Assert.Equal(Peak, VfxScreenRules.Envelope(VfxScreenRules.AttackSeconds, Peak), 4);
        Assert.Equal(0f, VfxScreenRules.Envelope(VfxScreenRules.AttackSeconds + VfxScreenRules.DecaySeconds, Peak));
        Assert.Equal(0f, VfxScreenRules.Envelope(5d, Peak));
        Assert.True(VfxScreenRules.DecaySeconds > VfxScreenRules.AttackSeconds);

        for (double age = 0d; age < 0.4d; age += 0.005d)
        {
            Assert.InRange(VfxScreenRules.Envelope(age, Peak), 0f, Peak + 0.0001f);
        }
    }
}
