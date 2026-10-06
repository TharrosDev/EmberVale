using System;

namespace Embervale.Settings;

/// <summary>What a first run starts on. The player's own choice replaces it the moment they make one.</summary>
public readonly record struct GraphicsRecommendation(int Tier, int MaxFps);

/// <summary>
/// First-run quality pick from what the machine reports. Pure: the caller reads the adapter, memory
/// and core count from the engine and passes them in. It runs only when no settings file exists.
/// </summary>
public static class GraphicsAutoDetect
{
    // RenderingDevice.DeviceType, restated so this file stays Godot-free.
    public const int DeviceOther = 0;
    public const int DeviceIntegrated = 1;
    public const int DeviceDiscrete = 2;
    public const int DeviceVirtual = 3;
    public const int DeviceCpu = 4;

    private const long Gigabyte = 1024L * 1024L * 1024L;

    // An integrated GPU's video memory is system memory. Below this much installed, the lowest tier
    // is the only one that leaves the game and the OS room together.
    private const long IntegratedComfortBytes = 15L * Gigabyte;
    private const long DiscreteHighBytes = 15L * Gigabyte;

    private static readonly string[] IntegratedNames =
    {
        "iris", "uhd graphics", "hd graphics", "intel(r) graphics", "vega", "radeon graphics",
        "radeon(tm) graphics", "adreno", "mali", "apple m",
    };

    // The Steam Deck reports its APU by codename or as a custom part rather than by a retail name.
    private static readonly string[] SteamDeckNames = { "vangogh", "van gogh", "galileo", "sephiroth", "custom gpu 0405", "custom gpu 0932" };

    private static readonly string[] SoftwareNames = { "llvmpipe", "swiftshader", "lavapipe", "softpipe", "basic render" };

    // Discrete parts that are real GPUs but not High-tier ones at native resolution.
    private static readonly string[] ModestDiscreteNames =
    {
        "geforce mx", "geforce gt ", "gtx 7", "gtx 9", "gtx 10", "gtx 16", "radeon rx 5", "radeon r5", "radeon r7", "radeon r9", "arc a3",
    };

    public static GraphicsRecommendation Recommend(
        string? adapterName, string? adapterVendor, int deviceType, long physicalMemoryBytes, int processorCount)
    {
        string name = (adapterName ?? string.Empty).ToLowerInvariant();
        string vendor = (adapterVendor ?? string.Empty).ToLowerInvariant();

        if (deviceType == DeviceCpu || ContainsAny(name, SoftwareNames))
        {
            return new GraphicsRecommendation(GraphicsMath.Performance, 30);
        }

        if (ContainsAny(name, SteamDeckNames))
        {
            return new GraphicsRecommendation(GraphicsMath.Low, 60);
        }

        bool namedIntegrated = ContainsAny(name, IntegratedNames);
        bool integrated = deviceType == DeviceIntegrated || deviceType == DeviceVirtual ||
            (deviceType != DeviceDiscrete && namedIntegrated) ||
            // Some drivers report an integrated part as "Other"; an Intel adapter that is not an Arc
            // discrete card is integrated.
            (deviceType == DeviceOther && vendor.Contains("intel", StringComparison.Ordinal) && !name.Contains("arc", StringComparison.Ordinal));

        if (integrated)
        {
            bool roomy = physicalMemoryBytes >= IntegratedComfortBytes && processorCount >= 8;
            return new GraphicsRecommendation(roomy ? GraphicsMath.Low : GraphicsMath.Performance, 60);
        }

        if (deviceType == DeviceDiscrete)
        {
            bool strong = physicalMemoryBytes >= DiscreteHighBytes && processorCount >= 8 &&
                !ContainsAny(name, ModestDiscreteNames);
            // V-Sync is on by default and bounds a discrete card to the display; no extra cap.
            return new GraphicsRecommendation(strong ? GraphicsMath.High : GraphicsMath.Medium, 0);
        }

        // Nothing identifiable (a headless or unknown adapter): the class default, unchanged.
        return new GraphicsRecommendation(GraphicsMath.Medium, 0);
    }

    private static bool ContainsAny(string text, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
