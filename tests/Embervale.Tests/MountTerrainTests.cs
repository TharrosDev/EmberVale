using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The ground a horse refuses (the Movement upgrade). The two cases that matter most are the ones
/// where it must <em>not</em> refuse: a bridge over deep water, and the way out of a river it is
/// already standing in.
/// </summary>
public class MountTerrainTests
{
    private const float RefuseDepth = 0.9f;
    private const float StepDown = 1.2f;
    private const float Grade = 1.2f;

    private static MountRefusal Judge(float? groundAhead, float? water = null, float depthHere = 0f, float distance = 3f) =>
        MountTerrain.Judge(10f, groundAhead, water, depthHere, distance, RefuseDepth, StepDown, Grade);

    [Fact]
    public void LevelDryGroundIsFine() => Assert.Equal(MountRefusal.None, Judge(10f));

    [Fact]
    public void UphillIsNeverADrop() => Assert.Equal(MountRefusal.None, Judge(14f));

    [Fact]
    public void NoGroundWithinReachIsADrop() => Assert.Equal(MountRefusal.SteepDrop, Judge(null));

    [Fact]
    public void AStepDownIsAStepAtAnyGrade() => Assert.Equal(MountRefusal.None, Judge(9f, distance: 0.2f));

    [Fact]
    public void ACliffIsRefused() => Assert.Equal(MountRefusal.SteepDrop, Judge(5f, distance: 3f));

    /// <summary>Four metres down over ten is a steep hillside, not a cliff — a horse takes it.</summary>
    [Fact]
    public void ALongSlopeDownIsRidden() => Assert.Equal(MountRefusal.None, Judge(6f, distance: 10f));

    [Fact]
    public void DeepWaterIsRefused() => Assert.Equal(MountRefusal.DeepWater, Judge(9f, water: 10.5f));

    [Fact]
    public void ShallowWaterIsRidden() => Assert.Equal(MountRefusal.None, Judge(9.8f, water: 10.3f));

    /// <summary>
    /// ⚠️ The case the heightfield would get wrong. The probe hit the deck, not the riverbed, so the
    /// water under the bridge is not in front of the horse's feet.
    /// </summary>
    [Fact]
    public void ABridgeOverDeepWaterIsRidden() => Assert.Equal(MountRefusal.None, Judge(10f, water: 7f));

    /// <summary>A horse already chest-deep is never refused the way out — only the way further in.</summary>
    [Fact]
    public void TheWayOutOfARiverIsNeverRefused()
    {
        Assert.Equal(MountRefusal.None, Judge(9.2f, water: 10.4f, depthHere: 1.4f));
        Assert.Equal(MountRefusal.DeepWater, Judge(8.5f, water: 10.4f, depthHere: 1.4f));
    }

    [Fact]
    public void BadDataNeverStrandsARider()
    {
        Assert.Equal(MountRefusal.None,
            MountTerrain.Judge(float.NaN, null, null, 0f, 3f, RefuseDepth, StepDown, Grade));
        Assert.Equal(MountRefusal.None, Judge(10f, water: float.NaN));
    }

    [Fact]
    public void TheProbeReachesTheStoppingDistance()
    {
        // 12 m/s at 20 m/s² stops in 3.6 m; plus the margin.
        Assert.Equal(3.6f + 1.4f, MountTerrain.ProbeDistance(12f, 20f, 1.4f, 12f), 3);

        // Standing still still looks one margin ahead; nothing looks further than the cap.
        Assert.Equal(1.4f, MountTerrain.ProbeDistance(0f, 20f, 1.4f, 12f), 3);
        Assert.Equal(12f, MountTerrain.ProbeDistance(1000f, 20f, 1.4f, 12f), 3);
        Assert.Equal(1.4f, MountTerrain.ProbeDistance(float.NaN, 20f, 1.4f, 12f), 3);
    }
}
