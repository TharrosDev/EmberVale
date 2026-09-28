using Embervale.Player;
using Xunit;

namespace Embervale.Tests;

public class CameraComfortTests
{
    [Fact]
    public void FullSettingsPassThrough()
    {
        CameraComfort c = CameraComfort.From(1f, 0.6f, 0.4f, reducedMotion: false);
        Assert.Equal(new CameraComfort(1f, 0.6f, 0.4f), c);
    }

    [Fact]
    public void ReducedMotionKillsBobAndKickAndCapsShake()
    {
        CameraComfort c = CameraComfort.From(1f, 1f, 1f, reducedMotion: true);
        Assert.Equal(CameraComfort.ReducedShake, c.Shake);
        Assert.Equal(0f, c.Bob);
        Assert.Equal(0f, c.FovKick);
    }

    [Fact]
    public void ReducedMotionNeverRaisesAnAlreadyLowShake()
    {
        Assert.Equal(0.1f, CameraComfort.From(0.1f, 1f, 1f, reducedMotion: true).Shake);
    }

    [Fact]
    public void OutOfRangeValuesClamp()
    {
        CameraComfort c = CameraComfort.From(5f, -1f, 9f, reducedMotion: false);
        Assert.Equal(new CameraComfort(1f, 0f, 1f), c);
    }

    [Fact]
    public void NudgesCombineAdditivelyExceptDistanceWhichMultiplies()
    {
        var a = new CameraNudge(new Godot.Vector3(1, 0, 0), new Godot.Vector3(0, 0, 0.1f), 2f, 0.5f);
        var b = new CameraNudge(new Godot.Vector3(0, 1, 0), new Godot.Vector3(0, 0, 0.2f), 3f, 0.5f);
        CameraNudge sum = a.Combine(b);
        Assert.Equal(new Godot.Vector3(1, 1, 0), sum.Offset);
        Assert.Equal(0.3f, sum.Euler.Z, 4);
        Assert.Equal(5f, sum.FovOffset);
        Assert.Equal(0.25f, sum.DistanceScale);
        Assert.Equal(CameraNudge.Identity, CameraNudge.Identity.Combine(CameraNudge.Identity));
    }
}
