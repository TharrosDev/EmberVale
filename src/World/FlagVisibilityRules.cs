namespace Embervale.World;

/// <summary>Pure visibility decision for a world actor whose presence is story-gated (41E).</summary>
public static class FlagVisibilityRules
{
    /// <summary>Empty means ungated; a set flag hides the authored actor.</summary>
    public static bool ShouldHide(string? hiddenWhenFlagId, bool hasFlag) =>
        !string.IsNullOrEmpty(hiddenWhenFlagId) && hasFlag;
}
