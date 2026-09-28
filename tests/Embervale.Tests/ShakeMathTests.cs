using System;
using Embervale.Combat;
using Embervale.Player;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure camera-shake rules: the trauma curve, per-source caps and the comfort ceiling, the
/// tier of each combat state, smooth noise, and the directional kick with its spring.
/// </summary>
public class ShakeMathTests
{
    [Fact]
    public void Amplitude_IsQuadratic_AndZeroAtRest()
    {
        Assert.Equal(0f, ShakeMath.Amplitude(0f));
        Assert.Equal(0.25f, ShakeMath.Amplitude(0.5f), 4);
        Assert.True(ShakeMath.Amplitude(1f) > ShakeMath.Amplitude(0.5f));
        Assert.Equal(1f, ShakeMath.Amplitude(3f)); // over-range trauma clamps
    }

    [Fact]
    public void StateTrauma_OrderedCritStaggerBlock()
    {
        Assert.True(ShakeMath.CritTrauma >= ShakeMath.StaggerTrauma);
        Assert.True(ShakeMath.StaggerTrauma >= ShakeMath.BlockTrauma);
    }

    [Fact]
    public void Add_ClampsToOne()
    {
        Assert.Equal(1f, ShakeMath.Add(0.8f, 0.8f));
        Assert.Equal(0.5f, ShakeMath.Add(0.2f, 0.3f), 3);
    }

    [Fact]
    public void Decay_FallsTowardZero_NeverBelow()
    {
        float t = ShakeMath.Decay(1f, 0.1f);
        Assert.True(t < 1f && t >= 0f);
        Assert.Equal(0f, ShakeMath.Decay(0.05f, 1f)); // a big step floors at 0, no underflow
    }

    [Fact]
    public void ComfortCeiling_IsBelowFullTrauma_SoShakeIsRestrained()
    {
        Assert.True(ShakeMath.ComfortCeiling < 1f);
        // Even at the ceiling the peak movement stays small: centimetres and a couple of degrees.
        float amp = ShakeMath.Amplitude(ShakeMath.ComfortCeiling);
        Assert.True(amp * ShakeMath.MaxOffset < 0.04f);
        Assert.True(amp * ShakeMath.MaxRoll < 0.02f);
    }

    [Fact]
    public void Pool_CapsEachSource()
    {
        var pool = new TraumaPool();
        for (int i = 0; i < 20; i++)
        {
            pool.Add(ShakeSource.Block, ShakeMath.BlockTrauma);
        }

        Assert.Equal(ShakeMath.Cap(ShakeSource.Block), pool.Level(ShakeSource.Block), 4);
        Assert.Equal(0f, pool.Level(ShakeSource.Crit));
    }

    [Fact]
    public void Pool_StackedSourcesNeverExceedComfortCeiling()
    {
        var pool = new TraumaPool();
        foreach (ShakeSource source in Enum.GetValues<ShakeSource>())
        {
            pool.Add(source, 5f);
        }

        Assert.Equal(ShakeMath.ComfortCeiling, pool.Total, 4);
    }

    [Fact]
    public void Pool_IgnoresNonPositiveAmounts()
    {
        var pool = new TraumaPool();
        pool.Add(ShakeSource.Hit, -1f);
        pool.Add(ShakeSource.Hit, 0f);
        Assert.Equal(0f, pool.Total);
    }

    [Fact]
    public void Pool_DecaysEverySourceToZero()
    {
        var pool = new TraumaPool();
        pool.Add(ShakeSource.Crit, 0.5f);
        pool.Add(ShakeSource.Spell, 0.3f);
        float before = pool.Total;
        pool.Step(0.1f);
        Assert.True(pool.Total < before);
        pool.Step(10f);
        Assert.Equal(0f, pool.Total);
        Assert.Equal(0f, pool.Level(ShakeSource.Crit));
    }

    [Fact]
    public void Pool_ClearEmptiesIt()
    {
        var pool = new TraumaPool();
        pool.Add(ShakeSource.Heavy, 0.4f);
        pool.Clear();
        Assert.Equal(0f, pool.Total);
    }

    [Fact]
    public void Cap_IsPositiveForEverySource_AndAtMostOne()
    {
        foreach (ShakeSource source in Enum.GetValues<ShakeSource>())
        {
            Assert.InRange(ShakeMath.Cap(source), 0.01f, 1f);
        }

        Assert.Equal(Enum.GetValues<ShakeSource>().Length, ShakeMath.SourceCount);
    }

    [Fact]
    public void Total_HoldsTheSumToTheCeiling()
    {
        Assert.Equal(0.3f, ShakeMath.Total(new[] { 0.1f, 0.2f }), 4);
        Assert.Equal(ShakeMath.ComfortCeiling, ShakeMath.Total(new[] { 0.6f, 0.6f }));
    }

    [Fact]
    public void HitTaken_BlockedIsBlockTier_RegardlessOfDamage()
    {
        ShakeHit hit = ShakeMath.HitTaken(50f, 100f, blocked: true, crit: true);
        Assert.Equal(ShakeSource.Block, hit.Source);
        Assert.Equal(ShakeMath.BlockTrauma, hit.Trauma);
    }

    [Fact]
    public void HitTaken_ScalesFromScratchToHeavy()
    {
        ShakeHit scratch = ShakeMath.HitTaken(1f, 100f, blocked: false, crit: false);
        ShakeHit mid = ShakeMath.HitTaken(12f, 100f, blocked: false, crit: false);
        ShakeHit heavy = ShakeMath.HitTaken(40f, 100f, blocked: false, crit: false);

        Assert.Equal(ShakeSource.Hit, scratch.Source);
        Assert.True(scratch.Trauma >= ShakeMath.LightHitTrauma);
        Assert.True(mid.Trauma > scratch.Trauma);
        Assert.Equal(ShakeSource.Heavy, heavy.Source);
        Assert.Equal(ShakeMath.HeavyHitTrauma, heavy.Trauma, 4);
    }

    [Fact]
    public void HitTaken_CritIsAtLeastCritTier()
    {
        ShakeHit hit = ShakeMath.HitTaken(2f, 100f, blocked: false, crit: true);
        Assert.Equal(ShakeSource.Crit, hit.Source);
        Assert.True(hit.Trauma >= ShakeMath.CritTrauma);
    }

    [Fact]
    public void HitTaken_NoDamageAddsNothing_AndBadMaxHealthIsSafe()
    {
        Assert.Equal(0f, ShakeMath.HitTaken(0f, 100f, blocked: false, crit: false).Trauma);
        ShakeHit hit = ShakeMath.HitTaken(5f, 0f, blocked: false, crit: false);
        Assert.Equal(ShakeMath.HeavyHitTrauma, hit.Trauma, 4); // treated as a full-health blow
    }

    [Fact]
    public void HitDealt_OnlyAnUnblockedCritIsFelt()
    {
        Assert.Equal(ShakeMath.CritTrauma, ShakeMath.HitDealt(crit: true, blocked: false).Trauma);
        Assert.Equal(0f, ShakeMath.HitDealt(crit: true, blocked: true).Trauma);
        Assert.Equal(0f, ShakeMath.HitDealt(crit: false, blocked: false).Trauma);
    }

    [Fact]
    public void Stagger_PlayerFullTier_OthersOnlyNearAndSofter()
    {
        Assert.Equal(ShakeMath.StaggerTrauma, ShakeMath.Stagger(true, 0f).Trauma);
        ShakeHit near = ShakeMath.Stagger(false, ShakeMath.NearStaggerMetres - 1f);
        Assert.Equal(ShakeMath.NearStaggerTrauma, near.Trauma);
        Assert.True(near.Trauma < ShakeMath.StaggerTrauma);
        Assert.Equal(0f, ShakeMath.Stagger(false, ShakeMath.NearStaggerMetres + 1f).Trauma);
    }

    [Fact]
    public void LandingTrauma_ZeroBelowThreshold_RisesAndTopsOut()
    {
        Assert.Equal(0f, ShakeMath.LandingTrauma(ShakeMath.LandingShakeDrop));
        Assert.Equal(0f, ShakeMath.LandingTrauma(1f));
        float mid = ShakeMath.LandingTrauma((ShakeMath.LandingShakeDrop + ShakeMath.LandingShakeFullDrop) / 2f);
        Assert.True(mid > 0f && mid < ShakeMath.LandingMaxTrauma);
        Assert.Equal(ShakeMath.LandingMaxTrauma, ShakeMath.LandingTrauma(50f), 4);
    }

    [Fact]
    public void Noise_IsBoundedAndDeterministic()
    {
        for (float x = -20f; x < 20f; x += 0.137f)
        {
            float n = ShakeMath.Noise(x, 1);
            Assert.InRange(n, -1f, 1f);
            Assert.Equal(n, ShakeMath.Noise(x, 1));
        }
    }

    [Fact]
    public void Noise_IsSmooth_UnlikeWhiteNoise()
    {
        // Sampled every 1/240 s at the shake's pace, neighbouring values barely differ.
        float step = ShakeMath.NoiseHz / 240f;
        float worst = 0f;
        for (float x = 0f; x < 50f; x += step)
        {
            worst = Math.Max(worst, Math.Abs(ShakeMath.Noise(x + step, 0) - ShakeMath.Noise(x, 0)));
        }

        Assert.True(worst < 0.2f, $"largest frame-to-frame jump was {worst}");
    }

    [Fact]
    public void Noise_ChannelsAreIndependent()
    {
        int differing = 0;
        for (float x = 0.3f; x < 10f; x += 0.5f)
        {
            if (Math.Abs(ShakeMath.Noise(x, 0) - ShakeMath.Noise(x, 1)) > 0.01f)
            {
                differing++;
            }
        }

        Assert.True(differing > 10);
    }

    [Fact]
    public void Shake_ZeroAtRest_AndBoundedByCaps()
    {
        Assert.Equal(new Shudder(0f, 0f, 0f), ShakeMath.Shake(0f, 3.3f));

        for (float t = 0f; t < 10f; t += 0.05f)
        {
            Shudder s = ShakeMath.Shake(ShakeMath.ComfortCeiling, t);
            float amp = ShakeMath.Amplitude(ShakeMath.ComfortCeiling);
            Assert.InRange(s.OffsetX, -amp * ShakeMath.MaxOffset, amp * ShakeMath.MaxOffset);
            Assert.InRange(s.OffsetY, -amp * ShakeMath.MaxOffset, amp * ShakeMath.MaxOffset);
            Assert.InRange(s.Roll, -amp * ShakeMath.MaxRoll, amp * ShakeMath.MaxRoll);
        }
    }

    [Fact]
    public void Shake_GrowsWithTrauma()
    {
        float Peak(float trauma)
        {
            float peak = 0f;
            for (float t = 0f; t < 10f; t += 0.02f)
            {
                peak = Math.Max(peak, Math.Abs(ShakeMath.Shake(trauma, t).Roll));
            }

            return peak;
        }

        Assert.True(Peak(0.6f) > Peak(0.3f));
    }

    [Fact]
    public void ToLocal_RightAndForwardAtZeroYaw()
    {
        // Godot: forward is -Z, right is +X.
        (float side, float forward) = ShakeMath.ToLocal(1f, 0f, 0f);
        Assert.Equal(1f, side, 4);
        Assert.Equal(0f, forward, 4);

        (side, forward) = ShakeMath.ToLocal(0f, -1f, 0f);
        Assert.Equal(0f, side, 4);
        Assert.Equal(1f, forward, 4);
    }

    [Fact]
    public void ToLocal_FollowsBodyYaw()
    {
        // Facing +X after a -90 degree turn about Y: a blow from +X is dead ahead.
        (float side, float forward) = ShakeMath.ToLocal(1f, 0f, -MathF.PI / 2f);
        Assert.Equal(0f, side, 4);
        Assert.Equal(1f, forward, 4);
    }

    [Fact]
    public void KickFor_BlowFromRightPushesLeftAndRollsLeft()
    {
        DirectionalKick k = ShakeMath.KickFor(1f, side: 1f, forward: 0f);
        Assert.True(k.OffsetX < 0f);
        Assert.True(k.Roll > 0f);
        Assert.Equal(0f, k.OffsetZ, 5);
    }

    [Fact]
    public void KickFor_MirrorsForBlowFromLeft()
    {
        DirectionalKick right = ShakeMath.KickFor(0.7f, 1f, 0f);
        DirectionalKick left = ShakeMath.KickFor(0.7f, -1f, 0f);
        Assert.Equal(-right.OffsetX, left.OffsetX, 5);
        Assert.Equal(-right.Roll, left.Roll, 5);
    }

    [Fact]
    public void KickFor_FrontShovesBack_BehindShovesForward()
    {
        Assert.True(ShakeMath.KickFor(1f, 0f, 1f).OffsetZ > 0f);
        Assert.True(ShakeMath.KickFor(1f, 0f, -1f).OffsetZ < 0f);
    }

    [Fact]
    public void KickFor_ScalesWithTrauma_AndIsZeroWithoutDirectionOrTrauma()
    {
        Assert.True(Math.Abs(ShakeMath.KickFor(1f, 1f, 0f).Roll) > Math.Abs(ShakeMath.KickFor(0.3f, 1f, 0f).Roll));
        Assert.Equal(DirectionalKick.None, ShakeMath.KickFor(1f, 0f, 0f));
        Assert.Equal(DirectionalKick.None, ShakeMath.KickFor(0f, 1f, 0f));
    }

    [Fact]
    public void KickFor_DiagonalIsNormalised()
    {
        // The same blow at any distance gives the same kick: only direction matters.
        Assert.Equal(ShakeMath.KickFor(1f, 3f, 4f), ShakeMath.KickFor(1f, 0.3f, 0.4f));
    }

    [Fact]
    public void Kick_PeaksAtTheRequestedSizeThenSettlesWithoutOvershoot()
    {
        var spring = new CriticalSpring();
        const float omega = ShakeMath.KickOmega;
        spring.Kick(0.03f, omega, 1f);

        float peak = 0f;
        for (int i = 0; i < 600; i++)
        {
            spring.Step(1f / 240f, omega);
            peak = Math.Max(peak, spring.X);
            Assert.True(spring.X >= -1e-6f, "critically damped: never crosses back through zero");
        }

        Assert.Equal(0.03f, peak, 3);
        Assert.Equal(0f, spring.X, 4);
    }
}
