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
    public void ThePeakIsSlightAndNeverExceedsItsShareOfTheFlashSetting()
    {
        // The design allowed 0.35; the first renders showed that much dyes the whole view, so the
        // layer holds itself well under it.
        Assert.Equal(0.14f, VfxScreenRules.MaxPeak);
        Assert.True(VfxScreenRules.MaxPeak <= 0.35f);
        Assert.Equal(0.14f, VfxScreenRules.Peak(1f, 1f, false, true, false), 4);
        Assert.Equal(0.14f, VfxScreenRules.Peak(9f, 5f, false, true, false), 4);   // over-range inputs clamp
        Assert.Equal(0.07f, VfxScreenRules.Peak(1f, 0.5f, false, true, false), 4);
        Assert.Equal(0f, VfxScreenRules.Peak(1f, 0f, false, true, false));          // flashes switched off
    }

    [Fact]
    public void AnEnemysSpellFlashesOnlyWhenItStruckThePlayerAndThenNoBrighterThanItsCap()
    {
        Assert.Equal(0f, VfxScreenRules.Peak(1f, 1f, false, byPlayer: false, hitsPlayer: false));
        Assert.Equal(VfxScreenRules.EnemyHitCap, VfxScreenRules.Peak(1f, 1f, false, byPlayer: false, hitsPlayer: true));
        Assert.Equal(0.1f, VfxScreenRules.EnemyHitCap);
        Assert.True(VfxScreenRules.Peak(0.2f, 1f, false, false, true) < VfxScreenRules.EnemyHitCap);
        Assert.True(VfxScreenRules.EnemyHitCap < VfxScreenRules.MaxPeak);
    }

    [Fact]
    public void ReducedMotionCapsEveryFlash()
    {
        Assert.Equal(0.05f, VfxScreenRules.ReducedMotionCap);
        Assert.True(VfxScreenRules.ReducedMotionCap <= 0.09f); // the design's ceiling
        Assert.Equal(0.05f, VfxScreenRules.Peak(1f, 1f, reducedMotion: true, byPlayer: true, hitsPlayer: false));
        Assert.Equal(0.05f, VfxScreenRules.Peak(1f, 1f, reducedMotion: true, byPlayer: false, hitsPlayer: true));
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
    public void TheTintIsNearlyWhiteWhateverTheSchool()
    {
        foreach (DamageType school in new[]
                 {
                     DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Arcane, DamageType.Nature,
                     DamageType.Necrotic,
                 })
        {
            Color tint = VfxScreenRules.Tint(SpellSchools.Color(school));
            Assert.True(tint.S < 0.16f, $"{school} flash tint is a colour wash ({tint.S})");
            Assert.True(tint.V > 0.9f, $"{school} flash tint is dim ({tint.V})");
        }
    }

    [Fact]
    public void TheFlashIsBrief()
    {
        Assert.True(VfxScreenRules.AttackSeconds + VfxScreenRules.DecaySeconds <= 0.2f);
    }

    [Fact]
    public void OnlyABlastThePlayerStandsInFlashesTheScreen()
    {
        Assert.True(VfxScreenRules.PlayerCentred(0f, 3f));
        Assert.True(VfxScreenRules.PlayerCentred(3f + VfxScreenRules.CentredMargin, 3f));
        Assert.False(VfxScreenRules.PlayerCentred(3.1f + VfxScreenRules.CentredMargin, 3f));
        Assert.False(VfxScreenRules.PlayerCentred(float.MaxValue, 8f)); // no player at all
        Assert.False(VfxScreenRules.PlayerCentred(9f, 1.2f));           // a bolt landing across the field
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

    [Fact]
    public void AShellTheCameraIsInsideIsNotDrawn()
    {
        var blast = new Vector3(2.5f, 2.5f, 2.5f);

        Assert.True(VfxScreenRules.Engulfs(Vector3.Zero, blast));
        Assert.True(VfxScreenRules.Engulfs(new Vector3(0f, 2.4f, 0f), blast));
        Assert.True(VfxScreenRules.Engulfs(new Vector3(0f, 0f, 2.5f + (VfxScreenRules.EngulfMargin * 0.5f)), blast));
        Assert.False(VfxScreenRules.Engulfs(new Vector3(0f, 0f, 2.5f + VfxScreenRules.EngulfMargin + 0.01f), blast));
        Assert.False(VfxScreenRules.Engulfs(new Vector3(6f, 0f, 0f), blast));
    }

    [Fact]
    public void AWardOnTheFirstPersonPlayerEngulfsTheEyeButOneOnAFoeDoesNot()
    {
        // A ward shell is about a metre in radius around the chest; the eye is 0.65 m above it.
        var ward = new Vector3(1f, 1f, 1f);

        Assert.True(VfxScreenRules.Engulfs(new Vector3(0f, 0.65f, 0f), ward));
        Assert.False(VfxScreenRules.Engulfs(new Vector3(0f, 0.6f, 2.5f), ward));
    }

    [Fact]
    public void ABoltsBodyInTheCastingHandIsAlwaysDrawn()
    {
        // Smaller than a body: the eye may be within its margin and it is still no screen flash.
        var body = new Vector3(0.45f, 0.45f, 0.45f);

        Assert.True(body.X < VfxScreenRules.EngulfMinHalfExtent);
        Assert.False(VfxScreenRules.Engulfs(Vector3.Zero, body));
        Assert.False(VfxScreenRules.Engulfs(new Vector3(0f, 0f, 0.5f), body));
    }

    [Fact]
    public void ABreathsBodyIsJudgedAlongItsOwnAxes()
    {
        // Long down its aim (Z), narrow across: beside it is outside, down its length is inside.
        var gout = new Vector3(1.2f, 1.2f, 4f);

        Assert.True(VfxScreenRules.Engulfs(new Vector3(0f, 0f, 3.5f), gout));
        Assert.False(VfxScreenRules.Engulfs(new Vector3(3.5f, 0f, 0f), gout));
    }
}
