using Embervale.Entities;
using Godot;

namespace Embervale.Factions;

/// <summary>
/// Tags an actor as belonging to a faction (resolved through the
/// <see cref="FactionDatabase"/>). The enemy AI reads it to decide whether the player is
/// a target (via the player's <see cref="ReputationComponent"/> standing), and the
/// reputation system reads it on death to attribute a kill to a faction. It is a static
/// archetype tag, so it carries no state and isn't persisted.
/// </summary>
[GlobalClass]
public partial class FactionComponent : EntityComponent
{
    [Export] public string FactionId { get; set; } = string.Empty;

    public FactionResource? Faction => FactionDatabase.Get(FactionId);
}
