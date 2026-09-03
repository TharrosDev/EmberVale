using System;

namespace Embervale.World;

public enum WorldGenerationDebugMode
{
    None, Elevation, Continentalness, Mountains, Erosion, Valleys, Temperature, Moisture,
    LowlandBiome, WetlandBiome, AlpineBiome, BarrenBiome, Slope, Rivers, WaterProximity,
    Wetness, Roads, AuthoredStamps,
}

/// <summary>Developer-only selection consumed when terrain meshes are built. Change it through the
/// <c>worldgen</c> console command, then reload the region to inspect one field in isolation.</summary>
public static class WorldGenerationDebug
{
    public static WorldGenerationDebugMode Mode { get; set; }

    public static bool TrySet(string value)
    {
        if (!Enum.TryParse(value.Replace("-", string.Empty).Replace("_", string.Empty), true,
                out WorldGenerationDebugMode mode))
            return false;
        Mode = mode;
        return true;
    }

    public static string Modes => string.Join(", ", Enum.GetNames<WorldGenerationDebugMode>());
}
