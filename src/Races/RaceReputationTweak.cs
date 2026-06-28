using Godot;

namespace Embervale.Races;

/// <summary>
/// A starting-standing adjustment a <see cref="RaceResource"/> applies to a faction at character
/// creation (e.g. the Umbral are distrusted). The <see cref="FactionId"/> resolves through the
/// <see cref="Embervale.Factions.FactionDatabase"/>; applied via <c>ReputationComponent.Add</c> at
/// spawn (Phase 26C). Authored as a sub-resource inside a race <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class RaceReputationTweak : Resource
{
    /// <summary>Faction id whose standing is tweaked, e.g. "faction.villagers".</summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    /// <summary>Signed starting reputation delta with that faction.</summary>
    [Export] public int Amount { get; set; }
}
