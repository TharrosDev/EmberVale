namespace Embervale.World;

/// <summary>Pure visibility decision for a world actor whose presence is story-gated (41E).</summary>
public static class FlagVisibilityRules
{
    /// <summary>Empty means ungated; a set flag hides the authored actor.</summary>
    public static bool ShouldHide(string? hiddenWhenFlagId, bool hasFlag) =>
        !string.IsNullOrEmpty(hiddenWhenFlagId) && hasFlag;

    /// <summary>
    /// Presence from both gates: hidden once <paramref name="hiddenWhenFlagId"/> is set, and — when
    /// <paramref name="visibleWhenFlagId"/> is named — hidden until THAT flag is set. Both empty means
    /// always present. When both apply, hidden wins (an actor that has left stays gone).
    /// </summary>
    public static bool ShouldBePresent(
        string? hiddenWhenFlagId, bool hiddenFlagSet, string? visibleWhenFlagId, bool visibleFlagSet) =>
        !ShouldHide(hiddenWhenFlagId, hiddenFlagSet) &&
        (string.IsNullOrEmpty(visibleWhenFlagId) || visibleFlagSet);
}
