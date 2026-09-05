using Embervale.World;
using Godot;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldStreamingPolicyTests
{
    private static readonly WorldStreamingLimits Limits = new(
        85f, 170f, 300f, 460f, 30f, 2f, 0.65f);

    [Fact]
    public void CellContainingPlayer_IsNear()
    {
        Assert.Equal(WorldStreamingTier.Near, WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, Vector3.Zero, Vector3.Zero, new Vector2(40f, 40f),
            WorldStreamingTier.Unloaded, Limits, false));
    }

    [Fact]
    public void MotionPreloadsCellAhead()
    {
        var center = new Vector3(220f, 0f, 0f);
        WorldStreamingTier still = WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, Vector3.Zero, center, Vector2.Zero,
            WorldStreamingTier.Unloaded, Limits, false);
        WorldStreamingTier moving = WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, new Vector3(60f, 0f, 0f), center, Vector2.Zero,
            WorldStreamingTier.Unloaded, Limits, false);

        Assert.Equal(WorldStreamingTier.Far, still);
        Assert.Equal(WorldStreamingTier.Near, moving);
    }

    [Fact]
    public void HysteresisPreventsBoundaryThrash()
    {
        var center = new Vector3(Limits.NearDistance + 10f, 0f, 0f);
        Assert.Equal(WorldStreamingTier.Near, WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, Vector3.Zero, center, Vector2.Zero,
            WorldStreamingTier.Near, Limits, false));
        Assert.Equal(WorldStreamingTier.Mid, WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, Vector3.Zero, center, Vector2.Zero,
            WorldStreamingTier.Unloaded, Limits, false));
    }

    [Fact]
    public void RequiredLanding_IsAlwaysNear()
    {
        Assert.Equal(WorldStreamingTier.Near, WorldStreamingPolicy.DesiredTier(
            Vector3.Zero, Vector3.Zero, new Vector3(5000f, 0f, 0f), Vector2.Zero,
            WorldStreamingTier.Unloaded, Limits, true));
    }
}
