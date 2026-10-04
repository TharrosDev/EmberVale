using Embervale.Core;

namespace Embervale.Narrative;

/// <summary>Pure rule for when the vertical slice's closing cards play.</summary>
public static class SliceClosingRules
{
    /// <summary>
    /// The cards play once, on the first region change after the Iron King has fallen that actually
    /// LEAVES the Ember Crown (a realm crossing out of the arena's realm). A change that is not a
    /// departure from the Crown — a load that lands the player somewhere else from a stale "from" —
    /// does not count, and neither does one back into it.
    /// </summary>
    public static bool ShouldPlay(bool ironKingDefeated, bool alreadyPlayed, string? fromRegionId, string? toRegionId) =>
        ironKingDefeated && !alreadyPlayed &&
        fromRegionId == GameIds.Regions.EmberCrown &&
        !string.IsNullOrEmpty(toRegionId) && toRegionId != GameIds.Regions.EmberCrown;
}
