using System;
using System.Collections.Generic;
using Embervale.Magic.Vfx;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The coverage governor, the tier richness table and the first-person casting point: the rules
/// added after the first renders, which showed blasts as blank white frames, the tiers looking
/// alike and the player's own cast invisible in first person.
/// </summary>
public class VfxCoverageRulesTests
{
    public static readonly TheoryData<VfxTier> Tiers = new()
    {
        VfxTier.Performance, VfxTier.Low, VfxTier.Medium, VfxTier.High, VfxTier.Ultra,
    };

    // --- the governor ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void AnElementAtItsTiersShareLimitIsNeverMoreThanFaint(VfxTier tier)
    {
        float limit = VfxCoverageRules.ShareLimit(tier);

        Assert.Equal(VfxCoverageRules.Faint, VfxCoverageRules.Opacity(limit, tier), 3);
        for (float share = limit; share <= 1f; share += 0.01f)
        {
            Assert.True(VfxCoverageRules.Opacity(share, tier) <= VfxCoverageRules.Faint + 0.0005f);
        }
    }

    [Fact]
    public void NothingCoversMoreThanAboutAThirdOfTheFrameAtMoreThanFaintOpacity()
    {
        Assert.True(VfxCoverageRules.MaxShare <= 0.35f);
        Assert.True(VfxCoverageRules.Faint <= 0.15f);
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            Assert.InRange(VfxCoverageRules.ShareLimit(tier), VfxCoverageRules.PerformanceShare, VfxCoverageRules.MaxShare);
        }

        Assert.Equal(VfxCoverageRules.PerformanceShare, VfxCoverageRules.ShareLimit(VfxTier.Performance), 4);
        Assert.Equal(VfxCoverageRules.MaxShare, VfxCoverageRules.ShareLimit(VfxTier.Ultra), 4);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void OpacityFallsAsCoverageGrowsAndASmallElementIsUntouched(VfxTier tier)
    {
        Assert.Equal(1f, VfxCoverageRules.Opacity(0f, tier));
        Assert.Equal(1f, VfxCoverageRules.Opacity(VfxCoverageRules.FreeShare, tier));

        float last = 1f;
        for (float share = 0f; share <= 1.0001f; share += 0.005f)
        {
            float opacity = VfxCoverageRules.Opacity(share, tier);
            Assert.InRange(opacity, 0f, 1f);
            Assert.True(opacity <= last + 0.0001f, $"opacity rose at share {share}");
            last = opacity;
        }

        // The whole frame: next to nothing.
        Assert.True(VfxCoverageRules.Opacity(1f, tier) < 0.06f);
    }

    [Fact]
    public void ALowerTierAllowsLess()
    {
        for (int i = 1; i <= (int)VfxTier.Ultra; i++)
        {
            var lower = (VfxTier)(i - 1);
            var higher = (VfxTier)i;
            Assert.True(VfxCoverageRules.ShareLimit(higher) > VfxCoverageRules.ShareLimit(lower));
            Assert.True(VfxCoverageRules.MaxSpan(higher) > VfxCoverageRules.MaxSpan(lower));
            Assert.True(VfxCoverageRules.Opacity(0.25f, higher) > VfxCoverageRules.Opacity(0.25f, lower));
        }

        Assert.True(VfxCoverageRules.MaxSpan(VfxTier.Performance) <= 0.5f);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ThePopIsBrighterButOnlyForAFewFrames(VfxTier tier)
    {
        float settled = VfxCoverageRules.Opacity(0.3f, tier, 1d);
        float popping = VfxCoverageRules.Opacity(0.3f, tier, 0.02d);

        Assert.True(popping > settled);
        Assert.True(popping <= settled * VfxCoverageRules.PopBoost + 0.0001f);
        Assert.Equal(settled, VfxCoverageRules.Opacity(0.3f, tier, VfxCoverageRules.PopSeconds), 5);
        Assert.True(VfxCoverageRules.PopSeconds <= 0.1f); // about five frames at sixty a second
    }

    [Fact]
    public void ShareIsTheFractionOfTheFrameADiscCovers()
    {
        // A disc whose diameter is the frame's height covers pi/4 of a square, less on a wide frame.
        float distance = 10f;
        float radius = distance * VfxCoverageRules.TanHalfFov;
        Assert.Equal(1f, VfxCoverageRules.Span(radius, distance), 4);
        Assert.Equal(MathF.PI * 0.25f / VfxCoverageRules.Aspect, VfxCoverageRules.Share(radius, distance), 4);

        Assert.True(VfxCoverageRules.Share(1f, 5f) > VfxCoverageRules.Share(1f, 10f));
        Assert.True(VfxCoverageRules.Share(2f, 10f) > VfxCoverageRules.Share(1f, 10f));
        Assert.Equal(0f, VfxCoverageRules.Share(0f, 10f));
        Assert.Equal(0f, VfxCoverageRules.Share(-3f, 10f));

        // In the camera's face it is the whole frame, and never more than that.
        Assert.Equal(1f, VfxCoverageRules.Share(6f, 0f));
        Assert.Equal(VfxCoverageRules.Share(6f, VfxCoverageRules.MinDistance), VfxCoverageRules.Share(6f, 0.01f));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ASixMetreNovaAroundTheCameraIsFaintAndADistantOneIsWhole(VfxTier tier)
    {
        // The frost nova of the first renders: its halo was a near-white dome over the whole frame.
        float around = VfxCoverageRules.Opacity(6f, 3f, tier, 1d);
        float across = VfxCoverageRules.Opacity(0.6f, 30f, tier, 1d);

        Assert.True(around <= VfxCoverageRules.Faint);
        Assert.Equal(1f, across);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ASoftGlowIsShrunkToWhatItsTierMaySpan(VfxTier tier)
    {
        const float Distance = 8f;
        float clamped = VfxCoverageRules.ClampRadius(50f, Distance, tier);

        Assert.Equal(VfxCoverageRules.MaxSpan(tier), VfxCoverageRules.Span(clamped, Distance), 3);
        Assert.Equal(0.3f, VfxCoverageRules.ClampRadius(0.3f, Distance, tier)); // a small one is left alone
        Assert.Equal(0f, VfxCoverageRules.ClampRadius(-1f, Distance, tier));
    }

    [Fact]
    public void TheWhiteCoreIsCappedInSizeAndGrowsOnlySlowlyWithTheBlast()
    {
        Assert.Equal(0.3f, VfxCoverageRules.CoreRadius(0.3f));
        Assert.Equal(VfxCoverageRules.SmallCore, VfxCoverageRules.CoreRadius(VfxCoverageRules.SmallCore));
        Assert.Equal(0f, VfxCoverageRules.CoreRadius(-2f));

        float last = 0f;
        for (float radius = 0f; radius <= 12f; radius += 0.1f)
        {
            float core = VfxCoverageRules.CoreRadius(radius);
            Assert.True(core >= last - 0.0001f, "the core shrank as the blast grew");
            Assert.True(core <= radius + 0.0001f);
            last = core;
        }

        // A six-metre nova (a flare of about 3.3 m): a heart about a metre across, not a dome.
        Assert.True(VfxCoverageRules.CoreRadius(3.3f) < 1.2f);
        Assert.True(VfxCoverageRules.CoreRadius(12f) < 1.7f);
    }

    [Fact]
    public void ALargeCoreIsGoneInUnderFifteenHundredthsOfASecond()
    {
        Assert.True(VfxCoverageRules.CoreSeconds <= 0.15f);
        Assert.Equal(1f, VfxCoverageRules.CoreAlpha(0d, 0.6f, 3f), 4);
        Assert.Equal(0f, VfxCoverageRules.CoreAlpha(VfxCoverageRules.CoreSeconds, 0.6f, 3f), 4);
        Assert.Equal(0f, VfxCoverageRules.CoreAlpha(0.5d, 0.6f, 3f), 4);

        // A small hit keeps its core for its whole (short) life.
        Assert.True(VfxCoverageRules.CoreAlpha(0.15d, 0.3f, 0.4f) > 0.2f);
        Assert.Equal(0f, VfxCoverageRules.CoreAlpha(0.3d, 0.3f, 0.4f), 4);

        float last = 1f;
        for (double age = 0d; age <= 0.7d; age += 0.01d)
        {
            float alpha = VfxCoverageRules.CoreAlpha(age, 0.6f, 3f);
            Assert.InRange(alpha, 0f, 1f);
            Assert.True(alpha <= last + 0.0001f);
            last = alpha;
        }
    }

    // --- what a tier is built from ------------------------------------------------------------------

    [Fact]
    public void ThePerformanceTierIsLean()
    {
        VfxRichness lean = VfxBudgetRules.Richness(VfxTier.Performance);

        Assert.Equal(1, lean.BoltStrands);
        Assert.False(lean.Rays);
        Assert.False(lean.Billow);
        Assert.False(lean.SmokeColumn);
        Assert.Equal(0, lean.DebrisLayers);
        Assert.False(lean.SoftParticles);
        Assert.False(lean.Glints);
        Assert.False(lean.FullDisc);
        Assert.True(lean.LightRange <= 6f);
        Assert.True(lean.LifeScale < 1f);
    }

    [Fact]
    public void TheUltraTierHasEverything()
    {
        VfxRichness rich = VfxBudgetRules.Richness(VfxTier.Ultra);

        Assert.True(rich.BoltStrands >= 3);
        Assert.True(rich.Rays && rich.Billow && rich.SmokeColumn && rich.SoftParticles && rich.Glints && rich.FullDisc);
        Assert.True(rich.DebrisLayers >= 2);
        Assert.True(rich.LifeScale > 1f);
    }

    [Fact]
    public void EveryTierIsAtLeastAsRichAsTheOneBelowAndTheEndsDifferInEveryWay()
    {
        for (int i = 1; i < VfxBudgetRules.TierCount; i++)
        {
            VfxRichness lower = VfxBudgetRules.Richness((VfxTier)(i - 1));
            VfxRichness higher = VfxBudgetRules.Richness((VfxTier)i);

            Assert.True(higher.BoltStrands >= lower.BoltStrands);
            Assert.True(higher.DebrisLayers >= lower.DebrisLayers);
            Assert.True(higher.LightRange > lower.LightRange);
            Assert.True(higher.LifeScale > lower.LifeScale);
            Assert.True(!lower.Rays || higher.Rays);
            Assert.True(!lower.Billow || higher.Billow);
            Assert.True(!lower.SmokeColumn || higher.SmokeColumn);
            Assert.True(!lower.Glints || higher.Glints);
            Assert.NotEqual(lower, higher);
        }

        Assert.Equal(VfxBudgetRules.Richness(VfxTier.Performance), VfxBudgetRules.Richness((VfxTier)(-4)));
        Assert.Equal(VfxBudgetRules.Richness(VfxTier.Ultra), VfxBudgetRules.Richness((VfxTier)99));
    }

    [Fact]
    public void RichnessNeverPromisesWhatTheBudgetForbids()
    {
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            VfxBudget budget = VfxBudgetRules.For(tier);
            VfxRichness rich = VfxBudgetRules.Richness(tier);

            // Smoke and debris are secondary debris; a tier without it has none of them.
            Assert.True(budget.SecondaryDebris || (!rich.Billow && !rich.SmokeColumn && rich.DebrisLayers == 0));
            Assert.True(rich.BoltStrands >= 1);
        }
    }

    // --- the extra emitters -------------------------------------------------------------------------

    public static readonly TheoryData<VfxEmitter> Emitters = new()
    {
        VfxEmitter.Flame, VfxEmitter.Chunks, VfxEmitter.Mist, VfxEmitter.Crystals, VfxEmitter.Glints,
        VfxEmitter.Snow, VfxEmitter.AshFlake, VfxEmitter.FlameLick, VfxEmitter.Flurry,
    };

    [Theory]
    [MemberData(nameof(Emitters))]
    public void EveryExtraEmitterIsSane(VfxEmitter kind)
    {
        VfxBurstPreset preset = VfxBurstPresets.For(kind);

        Assert.True(preset.Amount > 0, $"{kind} throws nothing");
        Assert.InRange(preset.Life, 0.2f, 3f);
        Assert.True(preset.SpeedMin >= 0f && preset.SpeedMax >= preset.SpeedMin);
        Assert.True(preset.SizeMin > 0f && preset.SizeMax >= preset.SizeMin);
        Assert.InRange(preset.Explosiveness, 0f, 1f);
        Assert.True(preset.Damping >= 0f);
        Assert.True(preset.Energy > 0f);
        Assert.InRange(preset.Dissolve, 0f, 1f);
        Assert.InRange(preset.CoolOcclude, 0f, 1f);
    }

    [Fact]
    public void TheEmittersShareOneTableOfSlotsWithTheRecipePresets()
    {
        var seen = new HashSet<int>();
        foreach (VfxParticles kind in Enum.GetValues<VfxParticles>())
        {
            Assert.True(seen.Add((int)kind));
            Assert.True((int)kind < VfxBurstPresets.SlotCount);
            Assert.Equal(VfxBurstPresets.For(kind), VfxBurstPresets.ForSlot((int)kind));
        }

        foreach (VfxEmitter kind in Enum.GetValues<VfxEmitter>())
        {
            if (kind == VfxEmitter.None)
            {
                continue;
            }

            Assert.True(seen.Add((int)kind), $"{kind} shares a slot with a recipe preset");
            Assert.True((int)kind < VfxBurstPresets.SlotCount);
            Assert.Equal(VfxBurstPresets.For(kind), VfxBurstPresets.ForSlot((int)kind));
        }

        Assert.Equal(0, VfxBurstPresets.For(VfxEmitter.None).Amount);
    }

    [Fact]
    public void FlameCoolsToSmokeAndOnlyDarkThingsCover()
    {
        VfxBurstPreset flame = VfxBurstPresets.For(VfxEmitter.Flame);

        Assert.Equal(VfxRamp.Heat, flame.Ramp);
        Assert.False(flame.Occlude);          // added light while it burns
        Assert.True(flame.CoolOcclude > 0.5f); // covering smoke once it has cooled
        Assert.True(flame.Dissolve > 0f);
        Assert.True(flame.Grow);

        Assert.True(VfxBurstPresets.For(VfxEmitter.Chunks).Occlude);
        Assert.True(VfxBurstPresets.For(VfxEmitter.Chunks).Gravity < 0f);
        Assert.False(VfxBurstPresets.For(VfxEmitter.Mist).Occlude);
        Assert.True(VfxBurstPresets.For(VfxEmitter.Mist).Energy < 0.3f); // a faint haze, never a white fog
        Assert.True(VfxBurstPresets.For(VfxEmitter.Mist).Gravity <= 0f); // it sinks
        Assert.True(VfxBurstPresets.For(VfxEmitter.Crystals).AlignVelocity);
        Assert.Equal(VfxRamp.Twinkle, VfxBurstPresets.For(VfxEmitter.Glints).Ramp);
    }

    // --- lightning ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2f, 0.55f)]
    [InlineData(8f, 0.55f)]
    [InlineData(25f, 0.22f)]
    public void JitterForHoldsABoltToASwayInMetres(float length, float sway)
    {
        float jitter = VfxBoltPath.JitterFor(length, sway);

        Assert.Equal(sway, VfxBoltPath.MaxOffset(length, jitter), 3);

        var points = new List<Vector3>();
        var from = new Vector3(1f, 2f, 3f);
        Vector3 to = from + (new Vector3(0.6f, 0.1f, 0.79f).Normalized() * length);
        Vector3 axis = (to - from).Normalized();
        for (int seed = 1; seed <= 12; seed++)
        {
            VfxBoltPath.Generate(from, to, 32, jitter, seed, points);
            foreach (Vector3 point in points)
            {
                Vector3 along = from + (axis * (point - from).Dot(axis));
                Assert.True(point.DistanceTo(along) <= sway + 0.001f, "a point swayed past the bound");
            }
        }
    }

    // --- the first-person casting point ---------------------------------------------------------------

    [Fact]
    public void TheCameraInThePlayersHeadIsFirstPersonAndTheShoulderCameraIsNot()
    {
        Assert.True(VfxViewRules.IsFirstPerson(Vector3.Zero));
        Assert.True(VfxViewRules.IsFirstPerson(new Vector3(0.1f, 0.2f, -0.1f)));
        Assert.False(VfxViewRules.IsFirstPerson(new Vector3(0.6f, 0.4f, 2.6f)));
        Assert.False(VfxViewRules.IsFirstPerson(new Vector3(0f, VfxViewRules.FirstPersonReach, 0f)));
    }

    [Fact]
    public void TheFirstPersonCastingPointIsLowAndLeftAndClearOfTheCrosshair()
    {
        Vector2 screen = VfxViewRules.Screen(VfxViewRules.HandOffset);

        Assert.InRange(screen.X, -0.6f, -0.2f); // left of centre (the casting hand), inside the frame
        Assert.InRange(screen.Y, -0.7f, -0.3f); // below centre, above the bottom edge

        // Further from the crosshair than an aura's bright heart is wide (about 0.3 m at this depth).
        float fromCrosshair = new Vector2(VfxViewRules.HandOffset.X, VfxViewRules.HandOffset.Y).Length();
        Assert.True(fromCrosshair > 0.55f);
    }

    [Fact]
    public void TheFirstPersonCastingPointIsPastTheNearFadeEveryEffectShaderApplies()
    {
        // Every vfx shader fades between 0.3 m and 1.2 m of the camera.
        Assert.True(VfxViewRules.HandOffset.Length() > 1.2f);
        Assert.True(VfxViewRules.HandOffset.Z < 0f); // ahead of the camera
    }

    [Fact]
    public void TheCastingPointFollowsTheCamera()
    {
        var position = new Vector3(4f, 1.7f, -2f);
        Vector3 ahead = VfxViewRules.HandPoint(position, Basis.Identity);
        Assert.Equal(position + VfxViewRules.HandOffset, ahead);
        // Turned a quarter turn to the left (looking down -X): "left" is now +Z.
        var turned = new Basis(Vector3.Up, MathF.PI * 0.5f);
        Vector3 offset = VfxViewRules.HandPoint(position, turned) - position;
        Assert.Equal(VfxViewRules.HandOffset.Y, offset.Y, 4);
        Assert.Equal(VfxViewRules.HandOffset.Length(), offset.Length(), 4);
        Assert.True(offset.X < -1f);
        Assert.True(offset.Z > 0f);
    }

    [Fact]
    public void ASmallOrDistantParticleIsNotThinned()
    {
        // An ember, and a metre-wide puff ten metres off: both far under the limit.
        Assert.Equal(1f, VfxCoverageRules.SpriteOpacity(0.2f, 3f));
        Assert.Equal(1f, VfxCoverageRules.SpriteOpacity(1f, 10f));
        Assert.Equal(1f, VfxCoverageRules.SpriteOpacity(5f, 2f, limit: 0f)); // ungoverned sprites
    }

    [Fact]
    public void APuffThatFillsTheViewIsSeenThrough()
    {
        // A breath's puff, two and a half metres wide, one and a half metres from the eye.
        float near = VfxCoverageRules.SpriteOpacity(2.5f, 1.5f);
        Assert.InRange(near, VfxCoverageRules.PuffFloor, 0.3f);

        // Thinner the closer and the wider, and never under the floor.
        Assert.True(VfxCoverageRules.SpriteOpacity(2.5f, 3f) > near);
        Assert.True(VfxCoverageRules.SpriteOpacity(4f, 1.5f) < near);
        Assert.Equal(VfxCoverageRules.PuffFloor, VfxCoverageRules.SpriteOpacity(50f, 0.1f));
    }

    [Fact]
    public void AThinnedPuffAddsLightWithItsWidthNotItsArea()
    {
        // Past the limit, opacity times width on screen is constant.
        float a = VfxCoverageRules.SpriteOpacity(2f, 2f) * (2f / 2f);
        float b = VfxCoverageRules.SpriteOpacity(3f, 2f) * (3f / 2f);
        Assert.Equal(a, b, 4);
    }

    [Fact]
    public void AHeldGlowIsHandSizedAtTheFirstPersonCastingPoint()
    {
        // The view-fixed hand is a metre and a half out: a glow held there is drawn at well under
        // half its size, so the charge sits in the hand and not over a fifth of the frame.
        float atHand = VfxCoverageRules.NearScale(VfxViewRules.HandOffset.Length());
        Assert.InRange(atHand, VfxCoverageRules.NearFloor, 0.5f);

        // It never vanishes, never grows past its true size, and only grows with distance.
        Assert.Equal(VfxCoverageRules.NearFloor, VfxCoverageRules.NearScale(0f), 4);
        Assert.Equal(1f, VfxCoverageRules.NearScale(VfxCoverageRules.NearEnd), 4);
        Assert.Equal(1f, VfxCoverageRules.NearScale(40f), 4);
        float previous = 0f;
        for (float d = 0f; d <= 4f; d += 0.25f)
        {
            float scale = VfxCoverageRules.NearScale(d);
            Assert.True(scale >= previous);
            previous = scale;
        }
    }

    [Fact]
    public void TheFirstPersonCastingPointIsOnTheLeftWhereTheCastingHandIs()
    {
        // The left hand casts and the first-person arm that rises is the left one. Everything the
        // player's cast draws in first person starts from this one point.
        Assert.True(VfxViewRules.HandOffset.X < 0f);
        Assert.True(VfxViewRules.Screen(VfxViewRules.HandOffset).X < 0f);
        Assert.True(VfxViewRules.Screen(VfxViewRules.HandOffset).Y < 0f);
    }
}
