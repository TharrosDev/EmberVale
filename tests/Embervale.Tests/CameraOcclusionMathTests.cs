using Embervale.Combat;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the rules behind the obstruction fade: when it runs, what it may touch, the volume it
/// looks through and that a released prop is left exactly as it was found.</summary>
public class CameraOcclusionMathTests
{
    [Theory]
    [InlineData(true, false, 0f, true)]     // third person
    [InlineData(true, true, 0f, false)]     // first person, camera at the eye
    [InlineData(true, true, 0.3f, false)]   // barely leaning out
    [InlineData(true, true, 1.5f, true)]    // pulled back in first person
    [InlineData(false, false, 3.8f, false)] // the setting off does nothing, in any view
    [InlineData(false, true, 3.8f, false)]
    public void Applies_NeedsTheSettingAndADistancedCamera(bool setting, bool firstPerson, float pullback, bool expected)
    {
        Assert.Equal(expected, CameraOcclusionMath.Applies(setting, firstPerson, pullback));
    }

    [Fact]
    public void IsFadeableLayer_NeverTouchesCameraBlockers()
    {
        Assert.False(CameraOcclusionMath.IsFadeableLayer(CombatLayers.CameraBlocker));
        Assert.False(CameraOcclusionMath.IsFadeableLayer(CombatLayers.WorldStatic | CombatLayers.CameraBlocker));
        Assert.True(CameraOcclusionMath.IsFadeableLayer(CombatLayers.WorldStatic));
        Assert.True(CameraOcclusionMath.IsFadeableLayer(CombatLayers.Npc));
        Assert.True(CameraOcclusionMath.IsFadeableLayer(CombatLayers.Enemy));
    }

    [Fact]
    public void IsFadeableMesh_KeepsTerrainAndMergedBlocksSolid()
    {
        Assert.True(CameraOcclusionMath.IsFadeableMesh(3f, ownedByEntity: false));
        Assert.False(CameraOcclusionMath.IsFadeableMesh(40f, ownedByEntity: false));
        Assert.False(CameraOcclusionMath.IsFadeableMesh(13f, ownedByEntity: false));
        Assert.True(CameraOcclusionMath.IsFadeableMesh(13f, ownedByEntity: true)); // a house or a dragon
        Assert.False(CameraOcclusionMath.IsFadeableMesh(200f, ownedByEntity: true));
    }

    [Fact]
    public void TrySegment_RunsFromTheCameraToShortOfTheTarget()
    {
        Vector3 camera = new(0f, 2f, 5f);
        Vector3 target = new(0f, 2f, 0f);

        Assert.True(CameraOcclusionMath.TrySegment(camera, target, out Vector3 centre, out float height, out Vector3 axis));

        Assert.Equal(new Vector3(0f, 0f, -1f), axis);
        float span = 5f - CameraOcclusionMath.EndMargin;
        Assert.Equal(camera.Z - (span * 0.5f), centre.Z, 4);
        Assert.Equal(span + (2f * CameraOcclusionMath.ProbeRadius), height, 4);

        // The far end of the capsule's axis stops EndMargin short of the target.
        float farEnd = centre.Z - (span * 0.5f);
        Assert.Equal(CameraOcclusionMath.EndMargin, farEnd - target.Z, 4);
    }

    [Fact]
    public void TrySegment_RefusesWhenThereIsNoRoom()
    {
        Vector3 p = new(1f, 1f, 1f);
        Assert.False(CameraOcclusionMath.TrySegment(p, p, out _, out _, out _));
        Assert.False(CameraOcclusionMath.TrySegment(p, p + new Vector3(0f, 0f, 0.7f), out _, out _, out _));
        Assert.True(CameraOcclusionMath.TrySegment(p, p + new Vector3(0f, 0f, 1.5f), out _, out _, out _));
    }

    [Theory]
    [InlineData(0f, 0f, -1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.3f, 0.5f, -0.8f)]
    [InlineData(0f, -1f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0.0001f, -1f, 0f)]
    public void CapsuleBasis_TurnsLocalYOntoTheAxis(float x, float y, float z)
    {
        Vector3 axis = new Vector3(x, y, z).Normalized();
        Vector3 turned = CameraOcclusionMath.CapsuleBasis(axis) * Vector3.Up;

        Assert.Equal(axis.X, turned.X, 3);
        Assert.Equal(axis.Y, turned.Y, 3);
        Assert.Equal(axis.Z, turned.Z, 3);
    }

    [Fact]
    public void StepFade_GoesInFasterThanOutAndClamps()
    {
        float fade = CameraOcclusionMath.StepFade(0f, true, CameraOcclusionMath.FadeInSeconds / 2f);
        Assert.Equal(0.5f, fade, 4);
        Assert.Equal(1f, CameraOcclusionMath.StepFade(fade, true, 5f));

        float half = CameraOcclusionMath.StepFade(1f, false, CameraOcclusionMath.FadeOutSeconds / 2f);
        Assert.Equal(0.5f, half, 4);
        Assert.Equal(0f, CameraOcclusionMath.StepFade(half, false, 5f));
        Assert.True(CameraOcclusionMath.FadeInSeconds < CameraOcclusionMath.FadeOutSeconds);
    }

    [Fact]
    public void TransparencyAt_IsExactlyTheOriginalAtZeroAndCappedAtFull()
    {
        Assert.Equal(0f, CameraOcclusionMath.TransparencyAt(0f, 0f));
        Assert.Equal(0.25f, CameraOcclusionMath.TransparencyAt(0.25f, 0f));
        Assert.Equal(CameraOcclusionMath.MaxTransparency, CameraOcclusionMath.TransparencyAt(0f, 1f), 5);
        Assert.InRange(CameraOcclusionMath.MaxTransparency, 0.5f, 0.95f); // thin, never invisible
    }

    [Fact]
    public void TransparencyAt_NeverLightensAnAlreadyMoreTransparentMesh()
    {
        // A mesh authored at 0.9 must not be pulled back to 0.8 by "fading" it.
        for (float f = 0f; f <= 1f; f += 0.1f)
        {
            Assert.Equal(0.9f, CameraOcclusionMath.TransparencyAt(0.9f, f), 5);
        }
    }

    [Fact]
    public void TransparencyAt_RisesMonotonicallyWithTheFade()
    {
        float last = 0f;
        for (float f = 0f; f <= 1.0001f; f += 0.05f)
        {
            float t = CameraOcclusionMath.TransparencyAt(0f, f);
            Assert.True(t >= last - 0.00001f);
            last = t;
        }
    }

    [Fact]
    public void IsReleased_OnlyWhenNoLongerWantedAndFullyFadedBack()
    {
        Assert.False(CameraOcclusionMath.IsReleased(0f, true));    // wanted, just appeared
        Assert.False(CameraOcclusionMath.IsReleased(0.4f, false)); // leaving but not there yet
        Assert.True(CameraOcclusionMath.IsReleased(0f, false));
    }

    [Fact]
    public void AFadeThatStartsAndEndsReturnsTheAuthoredValueExactly()
    {
        // The whole in-and-out cycle at frame rates that do not divide the durations evenly.
        const float original = 0.13f;
        float fade = 0f;
        for (int i = 0; i < 40; i++)
        {
            fade = CameraOcclusionMath.StepFade(fade, true, 1f / 60f);
        }

        Assert.Equal(1f, fade);
        int frames = 0;
        while (!CameraOcclusionMath.IsReleased(fade, false) && frames++ < 1000)
        {
            fade = CameraOcclusionMath.StepFade(fade, false, 1f / 90f);
        }

        Assert.True(CameraOcclusionMath.IsReleased(fade, false));
        Assert.Equal(original, CameraOcclusionMath.TransparencyAt(original, fade));
    }
}
