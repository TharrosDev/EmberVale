using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Covers the pure presentation rules: how hard a body is thrown, the swing trail, and the
/// floating damage numbers.</summary>
public class CombatFeedbackPresentationTests
{
    // --- hit reaction ---------------------------------------------------------------

    [Fact]
    public void Recoil_HeavierOutcomesThrowFurtherLeanMoreAndSettleSlower()
    {
        Recoil block = HitReactionMath.For(HitOutcome.Blocked, HitKind.Normal, 20f, false, 0.18f);
        Recoil hit = HitReactionMath.For(HitOutcome.Hit, HitKind.Normal, 20f, false, 0.18f);
        Recoil guardBreak = HitReactionMath.For(HitOutcome.GuardBroken, HitKind.Normal, 30f, false, 0.18f);
        Recoil poise = HitReactionMath.For(HitOutcome.PoiseBroken, HitKind.Normal, 30f, true, 0.18f);

        Assert.True(block.Distance < hit.Distance);
        Assert.True(hit.Distance < guardBreak.Distance);
        Assert.True(guardBreak.Distance <= poise.Distance);
        Assert.True(block.LeanRadians < hit.LeanRadians);
        Assert.True(hit.SettleSeconds < poise.SettleSeconds);
    }

    [Fact]
    public void Recoil_AScratchStillNudgesButLessThanAHeavyBlow()
    {
        Recoil scratch = HitReactionMath.For(HitOutcome.Hit, HitKind.Normal, 2f, false, 0.18f);
        Recoil heavy = HitReactionMath.For(HitOutcome.Hit, HitKind.Heavy, 40f, false, 0.18f);
        Assert.True(scratch.Distance > 0f);
        Assert.True(scratch.Distance < heavy.Distance);
    }

    [Fact]
    public void Recoil_IsBoundedWhateverIsThrownAtIt()
    {
        Recoil worst = HitReactionMath.For(HitOutcome.PoiseBroken, HitKind.Charged, 9999f, true, 0.18f);
        Assert.True(worst.Distance <= 0.18f * HitReactionMath.MaxDistanceScale + 1e-4f);
        Assert.True(worst.LeanRadians <= HitReactionMath.MaxLean + 1e-4f);
        Assert.InRange(worst.SettleSeconds, HitReactionMath.MinSettleSeconds, HitReactionMath.MaxSettleSeconds);
    }

    [Fact]
    public void Recoil_ZeroAuthoredDistanceIsNoLurch()
    {
        Assert.Equal(0f, HitReactionMath.For(HitOutcome.Hit, HitKind.Normal, 20f, false, 0f).Distance);
    }

    [Fact]
    public void Spring_SettlesToRestAndOvershootsOnce()
    {
        float x = 0.3f;
        float v = 0f;
        bool crossed = false;
        for (int i = 0; i < 240; i++)
        {
            (x, v) = HitReactionMath.Step(x, v, 1f / 60f, 0.3f);
            if (x < 0f)
            {
                crossed = true;
            }
        }

        Assert.True(crossed, "an underdamped spring rocks past rest once");
        Assert.InRange(x, -0.01f, 0.01f);
    }

    [Fact]
    public void Spring_ASingleHugeFrameCannotBlowItUp()
    {
        (float x, float v) = HitReactionMath.Step(0.3f, 0f, 5f, 0.3f);
        Assert.True(float.IsFinite(x) && float.IsFinite(v));
        Assert.InRange(System.Math.Abs(x), 0f, 0.31f);
    }

    [Fact]
    public void Spring_FrozenDeltaHoldsThePose()
    {
        // Hit-stop runs the clock at zero: the pose must hold rather than snap or drift.
        (Vector3 x, Vector3 v) = HitReactionMath.Step(new Vector3(0.2f, 0f, 0.1f), Vector3.Zero, 0f, 0.3f);
        Assert.Equal(new Vector3(0.2f, 0f, 0.1f), x);
        Assert.Equal(Vector3.Zero, v);
    }

    [Fact]
    public void LeanAxis_TipsTheHeadAlongTheBlow()
    {
        Vector3 away = Vector3.Back; // the blow pushes toward +Z
        Vector3 axis = HitReactionMath.LeanAxis(away);
        Vector3 head = Vector3.Up * 1.8f;
        Vector3 moved = new Basis(axis, 0.1f) * head;
        Assert.True(moved.Z > 0.05f, "the top of the body should move along the blow");
        Assert.Equal(Vector3.Zero, HitReactionMath.LeanAxis(Vector3.Up));
    }

    // --- weapon trail ---------------------------------------------------------------

    [Fact]
    public void Trail_LightChainAlternatesDirectionAndSlant()
    {
        TrailStyle first = TrailStyles.For(ActionKind.Attack, 0, false, 0.2f, 0.34f, 0.3f, 0.6f);
        TrailStyle second = TrailStyles.For(ActionKind.Attack, 1, false, 0.2f, 0.34f, 0.3f, 0.6f);
        Assert.Equal(-first.Direction, second.Direction);
        Assert.Equal(-first.TiltDegrees, second.TiltDegrees);
        Assert.False(first.Vertical);
    }

    [Fact]
    public void Trail_AHeavyIsAVerticalChopAndWarmer()
    {
        TrailStyle light = TrailStyles.For(ActionKind.Attack, 0, false, 0.2f, 0.34f, 0.3f, 0.6f);
        TrailStyle heavy = TrailStyles.For(ActionKind.HeavyAttack, 0, false, 0.2f, 0.34f, 0.3f, 0.6f);
        Assert.True(heavy.Vertical);
        Assert.True(heavy.Radius > light.Radius);
        Assert.True(heavy.R > heavy.B, "an ember-warm chop");
    }

    [Fact]
    public void Trail_HostileArcsReadDifferentlyFromThePlayers()
    {
        TrailStyle mine = TrailStyles.For(ActionKind.Attack, 0, false, 0.2f, 0.34f, 0.3f, 0.6f);
        TrailStyle theirs = TrailStyles.For(ActionKind.Attack, 0, true, 0.2f, 0.34f, 0.3f, 0.6f);
        Assert.NotEqual((mine.R, mine.G, mine.B), (theirs.R, theirs.G, theirs.B));
    }

    [Fact]
    public void Trail_LifeFollowsTheAuthoredWindowAndIsClamped()
    {
        // A slow action (long wind-up) leaves a longer streak than a quick one, within bounds.
        TrailStyle quick = TrailStyles.For(ActionKind.Attack, 0, false, 0.1f, 0.34f, 0.3f, 0.6f);
        TrailStyle slow = TrailStyles.For(ActionKind.Attack, 0, false, 0.6f, 0.34f, 0.3f, 0.6f);
        Assert.True(slow.FadeSeconds > quick.FadeSeconds);
        Assert.InRange(quick.FadeSeconds, TrailStyles.MinFade, TrailStyles.MaxFade);
        Assert.InRange(slow.FadeSeconds, TrailStyles.MinFade, TrailStyles.MaxFade);
        Assert.InRange(TrailStyles.For(ActionKind.Attack, 0, false, 0f, 0f, 0.5f, 0.5f).FadeSeconds,
            TrailStyles.MinFade, TrailStyles.MaxFade);
    }

    [Fact]
    public void Trail_LeadingEdgeTravelsAndTheStreakFadesOut()
    {
        TrailStyle style = TrailStyles.For(ActionKind.Attack, 0, false, 0.2f, 0.34f, 0.3f, 0.6f);
        Assert.Equal(-style.TravelDegrees * 0.5f, TrailStyles.LeadDegrees(style, 0f), 3);
        Assert.Equal(style.TravelDegrees * 0.5f, TrailStyles.LeadDegrees(style, 1f), 3);
        Assert.True(TrailStyles.LeadDegrees(style, 0.5f) > 0f, "eased out: past halfway by half time");
        Assert.Equal(1f, TrailStyles.Fade(0f));
        Assert.Equal(0f, TrailStyles.Fade(1f));
    }

    // --- damage numbers -------------------------------------------------------------

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(0.2f, "1")]
    [InlineData(12.4f, "12")]
    [InlineData(12.6f, "13")]
    public void Number_Text_IsWholePoints(float amount, string expected) =>
        Assert.Equal(expected, DamageNumberMath.Text(amount));

    [Fact]
    public void Number_EveryOutcomeLooksDifferent()
    {
        var seen = new HashSet<(float, float, float, float, bool, string?, bool)>();
        foreach (HitOutcome outcome in System.Enum.GetValues<HitOutcome>())
        {
            NumberStyle s = DamageNumberMath.Style(outcome, false);
            Assert.True(seen.Add((s.R, s.G, s.B, s.Scale, s.Italic, s.WordKey, s.ShowNumber)), $"{outcome} repeats a look");
        }
    }

    [Fact]
    public void Number_CritIsBiggerAndBangedBlockIsSmallAndParenthesised()
    {
        Assert.True(DamageNumberMath.Style(HitOutcome.Critical, false).Scale > DamageNumberMath.Style(HitOutcome.Hit, false).Scale);
        Assert.True(DamageNumberMath.Style(HitOutcome.Blocked, false).Scale < DamageNumberMath.Style(HitOutcome.Hit, false).Scale);
        Assert.Equal("25!", DamageNumberMath.Compose(HitOutcome.Critical, 25f, null));
        Assert.Equal("(6)", DamageNumberMath.Compose(HitOutcome.Blocked, 6f, null));
        Assert.Equal("9", DamageNumberMath.Compose(HitOutcome.Hit, 9f, null));
    }

    [Fact]
    public void Number_ResistedIsItalicAndCarriesItsWord()
    {
        NumberStyle s = DamageNumberMath.Style(HitOutcome.Resisted, false);
        Assert.True(s.Italic);
        Assert.NotNull(s.WordKey);
        Assert.Equal("3 resisted", DamageNumberMath.Compose(HitOutcome.Resisted, 3f, "resisted"));
    }

    [Fact]
    public void Number_AParryIsAWordWithNoNumber()
    {
        Assert.False(DamageNumberMath.Style(HitOutcome.Parried, false).ShowNumber);
        Assert.Equal("PARRIED", DamageNumberMath.Compose(HitOutcome.Parried, 0f, "PARRIED"));
    }

    [Fact]
    public void Number_DamageTakenIsRedAndDealtIsPale()
    {
        NumberStyle dealt = DamageNumberMath.Style(HitOutcome.Hit, false);
        NumberStyle taken = DamageNumberMath.Style(HitOutcome.Hit, true);
        Assert.True(taken.R > taken.G + 0.3f);
        Assert.True(dealt.G > taken.G);
    }

    [Fact]
    public void Number_WordKeysAreInTheFeedbackNamespace()
    {
        foreach (HitOutcome outcome in System.Enum.GetValues<HitOutcome>())
        {
            string? key = DamageNumberMath.Style(outcome, false).WordKey;
            if (key != null)
            {
                Assert.StartsWith("combat.feedback.", key);
            }
        }
    }

    [Fact]
    public void Number_SizeGrowsWithDamageWithinBounds()
    {
        Assert.Equal(DamageNumberMath.MinSize, DamageNumberMath.SizeScale(0f), 3);
        Assert.True(DamageNumberMath.SizeScale(30f) > DamageNumberMath.SizeScale(5f));
        Assert.Equal(DamageNumberMath.MaxSize, DamageNumberMath.SizeScale(9999f), 3);
    }

    [Fact]
    public void Number_MotionRisesFadesAndPops()
    {
        Assert.Equal(0f, DamageNumberMath.Rise(0f));
        Assert.Equal(1f, DamageNumberMath.Rise(1f));
        Assert.True(DamageNumberMath.Rise(0.3f) > 0.3f, "fast off the mark");
        Assert.Equal(1f, DamageNumberMath.Alpha(0.5f));
        Assert.Equal(0f, DamageNumberMath.Alpha(1f), 3);
        Assert.True(DamageNumberMath.Pop(0f) > 1.3f);
        Assert.Equal(1f, DamageNumberMath.Pop(0.5f));
        Assert.True(DamageNumberMath.Life(HitOutcome.Critical) > DamageNumberMath.Life(HitOutcome.Hit));
    }

    [Fact]
    public void Number_DriftIsStableAndBounded()
    {
        Assert.Equal(DamageNumberMath.Drift(1234UL), DamageNumberMath.Drift(1234UL));
        for (ulong seed = 0; seed < 500; seed++)
        {
            Assert.InRange(DamageNumberMath.Drift(seed), -1f, 1f);
        }
    }

    [Fact]
    public void Number_RapidSameOutcomeHitsOnOneTargetMerge()
    {
        Assert.True(DamageNumberMath.ShouldMerge(0.1f, true, HitOutcome.Hit, HitOutcome.Hit));
        Assert.False(DamageNumberMath.ShouldMerge(0.5f, true, HitOutcome.Hit, HitOutcome.Hit));      // too old
        Assert.False(DamageNumberMath.ShouldMerge(0.1f, false, HitOutcome.Hit, HitOutcome.Hit));     // other target
        Assert.False(DamageNumberMath.ShouldMerge(0.1f, true, HitOutcome.Hit, HitOutcome.Blocked));  // other outcome
        Assert.False(DamageNumberMath.ShouldMerge(0.1f, true, HitOutcome.Critical, HitOutcome.Critical)); // crits stand alone
    }
}
