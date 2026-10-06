using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The first-run preset pick. Adapter strings are the ones drivers really report; the device type is
/// <c>RenderingDevice.DeviceType</c> (1 integrated, 2 discrete, 4 CPU).
/// </summary>
public class GraphicsAutoDetectTests
{
    private const long Gb = 1024L * 1024L * 1024L;

    [Fact]
    public void IrisXeWithFourteenGigabytes_StartsOnPerformance_CappedAtSixty()
    {
        GraphicsRecommendation pick = GraphicsAutoDetect.Recommend(
            "Intel(R) Iris(R) Xe Graphics", "Intel", GraphicsAutoDetect.DeviceIntegrated, 14 * Gb, 8);
        Assert.Equal(GraphicsMath.Performance, pick.Tier);
        Assert.Equal(60, pick.MaxFps);
    }

    [Theory]
    [InlineData("Intel(R) UHD Graphics 620", "Intel", 8, 4)]
    [InlineData("AMD Radeon(TM) Vega 8 Graphics", "AMD", 8, 8)]
    [InlineData("AMD Radeon(TM) Graphics", "AMD", 12, 12)]
    public void IntegratedWithLittleMemory_StartsOnPerformance(string name, string vendor, int gigabytes, int threads)
    {
        Assert.Equal(GraphicsMath.Performance,
            GraphicsAutoDetect.Recommend(name, vendor, GraphicsAutoDetect.DeviceIntegrated, gigabytes * Gb, threads).Tier);
    }

    [Fact]
    public void IntegratedWithRoom_StartsOnLow()
    {
        Assert.Equal(GraphicsMath.Low,
            GraphicsAutoDetect.Recommend("AMD Radeon(TM) Graphics", "AMD", GraphicsAutoDetect.DeviceIntegrated, 32 * Gb, 16).Tier);
    }

    [Fact]
    public void IntegratedNameWins_WhenTheDriverReportsNoType()
    {
        Assert.Equal(GraphicsMath.Performance,
            GraphicsAutoDetect.Recommend("Intel(R) HD Graphics 520", "Intel", GraphicsAutoDetect.DeviceOther, 8 * Gb, 4).Tier);
        Assert.Equal(GraphicsMath.Performance,
            GraphicsAutoDetect.Recommend("Mesa Intel(R) Graphics (ADL GT2)", "Intel", GraphicsAutoDetect.DeviceOther, 8 * Gb, 8).Tier);
    }

    [Theory]
    [InlineData("AMD Custom GPU 0405 (RADV VANGOGH)")]
    [InlineData("AMD Custom GPU 0932 (RADV GALILEO)")]
    public void SteamDeck_StartsOnLow(string name)
    {
        GraphicsRecommendation pick = GraphicsAutoDetect.Recommend(name, "AMD", GraphicsAutoDetect.DeviceIntegrated, 16 * Gb, 8);
        Assert.Equal(GraphicsMath.Low, pick.Tier);
        Assert.Equal(60, pick.MaxFps);
    }

    [Fact]
    public void SoftwareRenderer_StartsOnPerformance_AtThirty()
    {
        GraphicsRecommendation pick = GraphicsAutoDetect.Recommend("llvmpipe (LLVM 15.0.7, 256 bits)", "Mesa", GraphicsAutoDetect.DeviceCpu, 32 * Gb, 16);
        Assert.Equal(new GraphicsRecommendation(GraphicsMath.Performance, 30), pick);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3060", 16, 12, GraphicsMath.High)]
    [InlineData("AMD Radeon RX 6700 XT", 32, 16, GraphicsMath.High)]
    [InlineData("NVIDIA GeForce RTX 3060", 8, 12, GraphicsMath.Medium)]
    [InlineData("NVIDIA GeForce RTX 3060", 16, 4, GraphicsMath.Medium)]
    [InlineData("NVIDIA GeForce GTX 1050 Ti", 16, 8, GraphicsMath.Medium)]
    [InlineData("NVIDIA GeForce MX450", 16, 8, GraphicsMath.Medium)]
    public void Discrete_StartsOnMediumOrHigh_Uncapped(string name, int gigabytes, int threads, int expected)
    {
        GraphicsRecommendation pick = GraphicsAutoDetect.Recommend(name, "", GraphicsAutoDetect.DeviceDiscrete, gigabytes * Gb, threads);
        Assert.Equal(expected, pick.Tier);
        Assert.Equal(0, pick.MaxFps);
    }

    [Fact]
    public void ArcDiscreteCard_IsNotMistakenForIntegratedIntel()
    {
        Assert.Equal(GraphicsMath.High,
            GraphicsAutoDetect.Recommend("Intel(R) Arc(TM) A770 Graphics", "Intel", GraphicsAutoDetect.DeviceDiscrete, 32 * Gb, 16).Tier);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void UnidentifiedAdapter_KeepsTheClassDefault(string? name, string? vendor)
    {
        Assert.Equal(new GraphicsRecommendation(GraphicsMath.Medium, 0),
            GraphicsAutoDetect.Recommend(name, vendor, GraphicsAutoDetect.DeviceOther, 0, 0));
    }
}
