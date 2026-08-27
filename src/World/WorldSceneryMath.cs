namespace Embervale.World;

/// <summary>Stable, engine-free placement math shared by runtime scenery and unit tests.</summary>
public static class WorldSceneryMath
{
    public static uint Hash(int seed, int index)
    {
        uint value = unchecked((uint)seed) ^ unchecked((uint)index * 0x9E3779B9u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    public static float Unit(int seed, int index) => (Hash(seed, index) & 0x00FFFFFFu) / 16777215f;

    public static float RidgeHeight(int seed, int index, float baseHeight) =>
        baseHeight * (0.55f + (Unit(seed, index) * 0.75f));
}
