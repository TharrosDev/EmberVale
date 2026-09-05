namespace Embervale.World;

/// <summary>One naming convention for generated production-world artifacts.</summary>
public static class WorldBakePaths
{
    public const string Root = "res://data/world_bake";
    public const string Manifest = Root + "/manifest.json";

    public static string Region(string regionId) => $"{Root}/regions/{Slug(regionId)}.res";

    public static string Cell(string regionId, string cellId) =>
        $"{Root}/cells/{Slug(regionId)}/{Slug(cellId)}.scn";

    private static string Slug(string id) => id.Replace('.', '_').Replace(':', '_');
}
