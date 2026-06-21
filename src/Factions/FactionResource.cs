using Godot;

namespace Embervale.Factions;

/// <summary>
/// A designer-authored faction: a named group the player can hold standing with, its
/// starting reputation, the threshold at which its members turn hostile, and its web of
/// allied/enemy factions. Authored as a <c>.tres</c> under <c>data/factions/</c> and
/// indexed by <see cref="FactionDatabase"/> — a new faction is a new resource, no code.
///
/// Reputation propagates through the web: harming a faction (killing its members) also
/// pleases its <see cref="Enemies"/> and angers its <see cref="Allies"/>.
/// </summary>
[GlobalClass]
public partial class FactionResource : Resource
{
    /// <summary>Stable id, e.g. "faction.goblins". The save/database key.</summary>
    [Export] public string Id { get; set; } = "faction.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Faction";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The player's standing with this faction on a fresh game (≈ -100..100).</summary>
    [Export] public int DefaultReputation { get; set; } = 0;

    /// <summary>Members treat the player as an enemy while standing is at or below this tier.</summary>
    [Export] public ReputationTier HostileThreshold { get; set; } = ReputationTier.Unfriendly;

    /// <summary>Reputation lost with this faction when the player kills one of its members.</summary>
    [Export] public int KillReputationPenalty { get; set; } = 6;

    /// <summary>Faction ids that oppose this one (gain standing when this faction is harmed).</summary>
    [Export] public Godot.Collections.Array<string> Enemies { get; set; } = new();

    /// <summary>Faction ids allied with this one (lose standing when this faction is harmed).</summary>
    [Export] public Godot.Collections.Array<string> Allies { get; set; } = new();
}
