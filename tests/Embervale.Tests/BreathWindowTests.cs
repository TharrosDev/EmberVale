using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// When a breath fires (Phase 35C). The asymmetry between grounded and airborne is deliberate and is
/// the thing worth pinning: on the ground the dragon must turn to breathe, which is what keeps 35A's
/// flanking a real defence; in the air it is overhead and pitches its aim down, so a facing gate
/// there would only make the hover window fire at random.
/// </summary>
public class BreathWindowTests
{
    private const float Length = 14f;
    private const float Angle = 55f;   // ±27.5° on the ground

    private static bool Should(bool ready, bool airborne, float bearing, float distance) =>
        BreathWindow.ShouldBreathe(ready, airborne, bearing, distance, Length, Angle);

    [Fact]
    public void OnCooldown_NeverBreathes()
    {
        Assert.False(Should(ready: false, airborne: false, bearing: 0f, distance: 5f));
        Assert.False(Should(ready: false, airborne: true, bearing: 0f, distance: 5f));
    }

    [Fact]
    public void OutOfReach_NeverBreathes()
    {
        // Even from the air: the cone's length is the cone's length.
        Assert.False(Should(ready: true, airborne: false, bearing: 0f, distance: Length + 1f));
        Assert.False(Should(ready: true, airborne: true, bearing: 0f, distance: Length + 1f));
    }

    [Fact]
    public void Grounded_BreathesOnlyAtWhatItFaces()
    {
        Assert.True(Should(ready: true, airborne: false, bearing: 0f, distance: 8f));
        Assert.True(Should(ready: true, airborne: false, bearing: 20f, distance: 8f));
        Assert.False(Should(ready: true, airborne: false, bearing: 90f, distance: 8f));
        Assert.False(Should(ready: true, airborne: false, bearing: 180f, distance: 8f));
    }

    [Fact]
    public void Grounded_FacingIsSymmetric()
    {
        Assert.Equal(
            Should(ready: true, airborne: false, bearing: 20f, distance: 8f),
            Should(ready: true, airborne: false, bearing: -20f, distance: 8f));
        Assert.Equal(
            Should(ready: true, airborne: false, bearing: 90f, distance: 8f),
            Should(ready: true, airborne: false, bearing: -90f, distance: 8f));
    }

    [Fact]
    public void Grounded_TheAngleIsFullWidth()
    {
        Assert.True(Should(ready: true, airborne: false, bearing: Angle * 0.5f, distance: 8f));
        Assert.False(Should(ready: true, airborne: false, bearing: (Angle * 0.5f) + 1f, distance: 8f));
    }

    [Fact]
    public void Airborne_BreathesRegardlessOfFacing()
    {
        // The hover window is the swoop's payoff; it must not depend on which way a level body points.
        Assert.True(Should(ready: true, airborne: true, bearing: 0f, distance: 8f));
        Assert.True(Should(ready: true, airborne: true, bearing: 90f, distance: 8f));
        Assert.True(Should(ready: true, airborne: true, bearing: 180f, distance: 8f));
    }
}
