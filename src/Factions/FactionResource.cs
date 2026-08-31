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

    // --- Guilds (Phase 42A) -------------------------------------------------
    //
    // A GUILD IS A FACTION WITH RANKS. Phase 42 gives five organizations membership, rank and a
    // finale; every one of them already needed a faction for public standing, so a guild is this
    // resource with a non-empty <see cref="RankNameKeys"/> rather than a second resource kind, a
    // second database and a second thing to keep in step. `IsGuild` is the only test anywhere.
    //
    // ⚠️ NO MEMBERSHIP STATE LIVES HERE. Whether the player joined, refused, left, or holds rank 2
    // is persistent story-flag state on the player, derived through `GuildRules`. This resource is
    // authored data only — it says what ranks EXIST, never which one is held.

    /// <summary>
    /// Localization keys for this guild's ranks, lowest first — rank 1 is index 0. Empty on an
    /// ordinary faction; a non-empty list is what makes a faction a guild.
    ///
    /// Keys, not names: a rank's display text is `strings.csv`'s job in every locale, and a rank
    /// flag is derived from the guild id and the rank NUMBER, so renaming a rank never touches
    /// save state.
    /// </summary>
    [Export] public Godot.Collections.Array<string> RankNameKeys { get; set; } = new();

    /// <summary>
    /// May a player who left this guild join it again? Authored per guild because the answer is
    /// fiction: the Dawnwardens take back a repentant oath-breaker, the Emberbound do not admit
    /// anyone twice. Read by <see cref="GuildRules"/>; it is a policy, never a state.
    /// </summary>
    [Export] public bool RejoinAllowed { get; set; } = true;

    /// <summary>True when this faction is one of Phase 42's guilds — it declares ranks.</summary>
    public bool IsGuild => RankNameKeys.Count > 0;
}
