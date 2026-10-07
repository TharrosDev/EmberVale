using System;
using Embervale.Combat;
using Embervale.Magic.Vfx;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The second kit's pure rules: the shaped sprites every school is drawn with, the motifs that give
/// a wind-up and a bolt's head their school's structure, the ring that is cut back about a
/// first-person camera, and the screen-edge shimmer's comfort limits.
/// </summary>
public class VfxKitRulesTests
{
    private static readonly DamageType[] Schools =
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Arcane, DamageType.Nature,
        DamageType.Necrotic,
    };

    // --- shaped sprites -----------------------------------------------------------------------------

    [Fact]
    public void AFlameIsATongueTipUpAndNotADisc()
    {
        // Narrow near its tip (the top of the quad), wide low down, closed at its base.
        Assert.True(Width(VfxTextureRules.Flame, 0.15f) < Width(VfxTextureRules.Flame, 0.7f) * 0.6f);
        Assert.True(Width(VfxTextureRules.Flame, 0.7f) > 0.25f);
        Assert.True(VfxTextureRules.Flame(0.5f, 0.75f) > 0.8f);
    }

    [Fact]
    public void SnowLeadsWithItsHeadAndASparkIsKinked()
    {
        // A snow streak is brightest just behind its leading end and fades down its tail.
        Assert.True(VfxTextureRules.Snow(0.5f, 0.15f) > VfxTextureRules.Snow(0.5f, 0.6f));
        Assert.True(VfxTextureRules.Snow(0.5f, 0.6f) > VfxTextureRules.Snow(0.5f, 0.95f));

        // A jagged spark's line is off the centre of the quad between its ends, on both sides.
        float quarter = Centre(VfxTextureRules.Spark, 0.25f);
        float half = Centre(VfxTextureRules.Spark, 0.5f);
        Assert.True(quarter > 0.55f, $"the first kink sits at {quarter}");
        Assert.True(half < 0.45f, $"the second kink sits at {half}");
    }

    [Fact]
    public void AWispTapersAndAMoteIsTighterThanTheDot()
    {
        Assert.True(Width(VfxTextureRules.Wisp, 0.2f) > Width(VfxTextureRules.Wisp, 0.8f));

        // The mote is what the round dot was drawn for: at a third of the way out it has already
        // fallen well below the dot, off its points.
        Assert.True(VfxTextureRules.Mote(0.62f, 0.62f) < VfxTextureRules.Dot(0.62f, 0.62f) * 0.5f);
        Assert.Equal(1f, VfxTextureRules.Mote(0.5f, 0.5f), 2);
    }

    [Fact]
    public void ASchoolsShapeNeverChangesHowAPresetIsDrawn()
    {
        foreach (VfxParticles kind in Enum.GetValues<VfxParticles>())
        {
            if (kind == VfxParticles.None)
            {
                continue;
            }

            VfxBurstPreset preset = VfxBurstPresets.For(kind);
            foreach (DamageType school in Schools)
            {
                VfxSprite sprite = VfxBurstPresets.SchoolSprite(kind, school);
                Assert.True(VfxBurstPresets.Fits(sprite, preset.AlignVelocity), $"{kind} for {school} is {sprite}");
                Assert.NotEqual(VfxSprite.Dot, sprite);
            }
        }

        Assert.Equal(VfxSprite.Spark, VfxBurstPresets.SchoolSprite(VfxParticles.Sparks, DamageType.Lightning));
        Assert.Equal(VfxSprite.Streak, VfxBurstPresets.SchoolSprite(VfxParticles.Sparks, DamageType.Fire));
        Assert.Equal(VfxSprite.Snow, VfxBurstPresets.SchoolSprite(VfxParticles.Sparks, DamageType.Frost));
        Assert.Equal(VfxSprite.Flake, VfxBurstPresets.SchoolSprite(VfxParticles.Motes, DamageType.Frost));
        Assert.Equal(VfxSprite.Wisp, VfxBurstPresets.For(VfxParticles.Wisps).Sprite);
        Assert.Equal(VfxSprite.Flame, VfxBurstPresets.For(VfxEmitter.Flame).Sprite);
    }

    [Fact]
    public void EveryPresetsOwnSpriteSuitsIt()
    {
        for (int slot = 1; slot < VfxBurstPresets.SlotCount; slot++)
        {
            VfxBurstPreset preset = VfxBurstPresets.ForSlot(slot);
            if (preset.Amount > 0)
            {
                Assert.True(VfxBurstPresets.Fits(preset.Sprite, preset.AlignVelocity), $"slot {slot}");
            }
        }
    }

    [Fact]
    public void SnowIsManySmallFastStreaksAndAshHangs()
    {
        VfxBurstPreset snow = VfxBurstPresets.For(VfxEmitter.Snow);
        Assert.True(snow.AlignVelocity);
        Assert.True(snow.Amount >= 48);
        Assert.True(snow.SizeMax < 0.4f);
        Assert.True(snow.SpeedMin >= 5f);

        VfxBurstPreset ash = VfxBurstPresets.For(VfxEmitter.AshFlake);
        Assert.True(ash.Life >= 3f);
        Assert.True(ash.Occlude);
        Assert.True(ash.Gravity < 0f && ash.Gravity > -1f); // it drifts down; it does not drop

        VfxBurstPreset lick = VfxBurstPresets.For(VfxEmitter.FlameLick);
        Assert.True(lick.AlignVelocity && !lick.Occlude && lick.Gravity > 0f);
    }

    // --- motifs -------------------------------------------------------------------------------------

    [Fact]
    public void EverySchoolHasAWindupOfItsOwnAndArcaneKeepsItsGlyph()
    {
        Assert.True(VfxMotifRules.Windup(DamageType.Arcane).IsNone);
        var seen = new System.Collections.Generic.HashSet<(VfxSprite, VfxMotion)>();
        foreach (DamageType school in Schools)
        {
            VfxMotifStyle head = VfxMotifRules.Head(school);
            Assert.False(head.IsNone);
            Assert.Equal(VfxMotion.Lance, head.Motion);
            if (school == DamageType.Arcane)
            {
                continue;
            }

            VfxMotifStyle style = VfxMotifRules.Windup(school);
            Assert.False(style.IsNone);
            Assert.NotEqual(VfxMotion.Lance, style.Motion);
            Assert.True(seen.Add((style.Sprite, style.Motion)), $"{school} shares its wind-up with another school");
        }
    }

    [Fact]
    public void AMotifHasFewerPiecesOnALeanerTierAndNeverTooMany()
    {
        foreach (DamageType school in Schools)
        {
            foreach (VfxMotifStyle style in new[] { VfxMotifRules.Windup(school), VfxMotifRules.Head(school) })
            {
                if (style.IsNone)
                {
                    Assert.Equal(0, VfxMotifRules.CountAt(style, VfxTier.Ultra));
                    continue;
                }

                int previous = 0;
                foreach (VfxTier tier in Enum.GetValues<VfxTier>())
                {
                    int count = VfxMotifRules.CountAt(style, tier);
                    Assert.InRange(count, 1, VfxMotifRules.MaxCount);
                    Assert.True(count >= previous);
                    previous = count;
                }
            }
        }
    }

    [Theory]
    [InlineData(VfxMotion.Lick)]
    [InlineData(VfxMotion.Orbit)]
    [InlineData(VfxMotion.Crackle)]
    [InlineData(VfxMotion.Swirl)]
    [InlineData(VfxMotion.Inward)]
    [InlineData(VfxMotion.Lance)]
    public void EveryPieceStaysNearItsCentreAndIsDrawable(VfxMotion motion)
    {
        const float Radius = 0.5f;
        const float Size = 0.4f;
        for (int frame = 0; frame < 240; frame++)
        {
            float time = frame / 30f;
            for (int i = 0; i < 8; i++)
            {
                VfxMotifPose pose = VfxMotifRules.Pose(motion, i, 8, time, Radius, Size, Vector3.Forward, 0.3f, 17);
                Assert.True(float.IsFinite(pose.Offset.X) && float.IsFinite(pose.Offset.Y) && float.IsFinite(pose.Offset.Z));
                Assert.True(pose.Offset.Length() <= (Radius * 1.7f) + (Size * 1.5f), $"{motion} strays {pose.Offset.Length()}");
                Assert.InRange(pose.Alpha, 0f, 1f);
                Assert.InRange(pose.Scale, 0.2f, 1.5f);
                Assert.Equal(1f, pose.Along.Length(), 3);
            }
        }
    }

    [Fact]
    public void ALanceLeadsWithItsHeadAndStringsTheRestBehind()
    {
        Vector3 axis = new Vector3(1f, 0f, -1f).Normalized();
        VfxMotifPose head = VfxMotifRules.Pose(VfxMotion.Lance, 0, 4, 0.7f, 0f, 0.5f, axis, 0.2f, 3);
        Assert.Equal(Vector3.Zero, head.Offset);
        Assert.Equal(1f, head.Along.Dot(axis), 3);
        Assert.Equal(1f, head.Scale, 3);
        for (int i = 1; i < 4; i++)
        {
            VfxMotifPose follower = VfxMotifRules.Pose(VfxMotion.Lance, i, 4, 0.7f, 0f, 0.5f, axis, 0.2f, 3);
            Assert.True(follower.Offset.Dot(axis) < 0f);
            Assert.True(follower.Scale < head.Scale);
        }
    }

    [Fact]
    public void WispsAreDrawnInToTheCentre()
    {
        // Over one pass a wisp only gets nearer, and it points more inward than outward.
        float before = float.MaxValue;
        for (int step = 0; step < 10; step++)
        {
            VfxMotifPose pose = VfxMotifRules.Pose(VfxMotion.Inward, 0, 1, step * 0.1f, 1f, 0.3f, Vector3.Zero, 0f, 5);
            if (pose.Offset.Length() > before)
            {
                break; // it arrived and was born again at the rim
            }

            Assert.True(pose.Along.Dot(pose.Offset.Normalized()) < 0f);
            before = pose.Offset.Length();
        }

        Assert.True(before < 1f);
    }

    // --- a ring about the camera ----------------------------------------------------------------------

    [Fact]
    public void ARingAboutAFirstPersonCameraIsCutBackAndNoOtherRingIs()
    {
        // The player's own two-metre ring, seen from their own eye.
        Assert.Equal(VfxScreenRules.SelfRingFloor, VfxScreenRules.SelfRing(0.1f, 1.6f, 2f), 3);

        // The same ring from a third-person camera, and a ring about somebody else.
        Assert.Equal(1f, VfxScreenRules.SelfRing(3.8f, 2f, 2f), 3);
        Assert.Equal(1f, VfxScreenRules.SelfRing(9f, 1.6f, 2f), 3);

        // A nova: by the time it is well out it is drawn in full even from its centre.
        Assert.Equal(1f, VfxScreenRules.SelfRing(0.1f, 1.6f, 6f), 3);

        // Level with it or far above it, the camera is not standing in it.
        Assert.Equal(1f, VfxScreenRules.SelfRing(0.1f, 0.2f, 2f), 3);
        Assert.Equal(1f, VfxScreenRules.SelfRing(0.1f, 5f, 2f), 3);

        float previous = 0f;
        for (float radius = 0.5f; radius <= 7f; radius += 0.25f)
        {
            float drawn = VfxScreenRules.SelfRing(0.1f, 1.6f, radius);
            Assert.InRange(drawn, VfxScreenRules.SelfRingFloor - 0.001f, 1f);
            Assert.True(drawn >= previous - 0.0001f);
            previous = drawn;
        }
    }

    // --- the edge shimmer -----------------------------------------------------------------------------

    [Fact]
    public void TheEdgeShimmerIsCappedAndObeysTheFlashSetting()
    {
        Assert.Equal(VfxScreenRules.EdgeMaxPeak, VfxScreenRules.EdgePeak(5f, 1f, false), 4);
        Assert.Equal(0f, VfxScreenRules.EdgePeak(1f, 0f, false), 4);
        Assert.True(VfxScreenRules.EdgePeak(1f, 1f, true) <= VfxScreenRules.EdgeReducedCap);
        Assert.InRange(VfxScreenRules.EdgeClear, 0.5f, 0.9f); // the middle of the frame stays clear

        const float Peak = 0.25f;
        Assert.Equal(0f, VfxScreenRules.EdgeEnvelope(-0.1d, Peak, 0.9f));
        Assert.Equal(0f, VfxScreenRules.EdgeEnvelope(0.9d, Peak, 0.9f));
        for (double age = 0d; age < 0.9d; age += 0.03d)
        {
            Assert.InRange(VfxScreenRules.EdgeEnvelope(age, Peak, 0.9f), 0f, Peak + 0.0001f);
        }

        // Its colour keeps the school's hue: it is a tint at the edge, not a white wash.
        Color tint = VfxScreenRules.EdgeTint(new Color(0.2f, 0.5f, 1f));
        Assert.True(tint.B > tint.G && tint.G > tint.R);
    }

    [Fact]
    public void WhatIsDimmedAtTheEyeIsFullAtFightDistance()
    {
        float atHand = VfxCoverageRules.NearScale(VfxViewRules.HandOffset.Length());
        Assert.Equal(0f, VfxCoverageRules.NearShare(atHand), 3);
        Assert.Equal(1f, VfxCoverageRules.NearShare(VfxCoverageRules.NearScale(12f)), 3);
        Assert.InRange(VfxCoverageRules.NearShare(VfxCoverageRules.NearScale(3f)), 0.2f, 0.8f);
    }

    /// <summary>How much of a row of a sprite is lit, as a share of its width.</summary>
    private static float Width(Func<float, float, float> paint, float v)
    {
        int lit = 0;
        for (int x = 0; x < 64; x++)
        {
            if (paint((x + 0.5f) / 64f, v) > 0.15f)
            {
                lit++;
            }
        }

        return lit / 64f;
    }

    /// <summary>Where along a row a sprite is brightest (0..1).</summary>
    private static float Centre(Func<float, float, float> paint, float v)
    {
        float best = 0f;
        float at = 0.5f;
        for (int x = 0; x < 128; x++)
        {
            float u = (x + 0.5f) / 128f;
            float value = paint(u, v);
            if (value > best)
            {
                best = value;
                at = u;
            }
        }

        return at;
    }
}
