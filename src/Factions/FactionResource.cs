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

    // --- Guild hub and roster (Phase 42B) -----------------------------------
    //
    // A HUB IS A MAP LOCATION AND A ROSTER ENTRY IS A PLACED ACTOR. 42A's lesson was that the cheap
    // answer to "what kind of thing is this" is usually a kind that already exists, and it holds
    // twice here: a guild's home needs a name, a category, a pin, a cell and discovery rules, which
    // is a `MapLocationResource` entire; and a guild's officer needs a body, a collider, a schedule,
    // a faction and a conversation, which is an authored `Entity` in a cell scene. So there is no
    // `GuildHubResource` and no `GuildHubComponent` — these five strings are the only new surface,
    // and every one of them is an id into a register that already validates itself.
    //
    // ⚠️ THE POSITION IS NOT HERE, FOR THE REASON `MapLocationResource` HAS NO COORDINATES EITHER.
    // Where the hub stands is the placed `MapLocationComponent`'s transform in the cell scene.

    /// <summary>
    /// The <c>location.*</c> id of this guild's home. Empty on an ordinary faction; required on a
    /// guild. Map coverage, the breadcrumb, the pin and any future Reach objective all come free
    /// through <see cref="World.MapLocationResource"/> rather than being restated here.
    /// </summary>
    [Export] public string HubLocationId { get; set; } = string.Empty;

    /// <summary>
    /// The <c>npc.*</c> <see cref="Entities.Entity.TemplateId"/> of the officer who speaks for the
    /// guild — the one membership is offered and granted through. Required on a guild, and placed
    /// exactly once in a cell scene (<c>ContentValidator.ValidateGuildHubs</c> checks both sides).
    /// </summary>
    [Export] public string LeaderNpcId { get; set; } = string.Empty;

    /// <summary>The officer who equips and pays members. Required on a guild.</summary>
    [Export] public string QuartermasterNpcId { get; set; } = string.Empty;

    /// <summary>The officer who hands out the guild's work. Required on a guild.</summary>
    [Export] public string ContactNpcId { get; set; } = string.Empty;

    /// <summary>
    /// A member of the player's own standing — the peer whose reaction is how rank reads in the
    /// world rather than on a screen. ⚠️ <b>Optional on purpose:</b> a peer only means anything once
    /// there is a rank to be a peer of, so it is DECLARED here and PLACED by the arc sub-phase that
    /// first grants rank one (42C/E/G/I). Left empty it is skipped; set it must resolve like the rest.
    /// </summary>
    [Export] public string RankPeerNpcId { get; set; } = string.Empty;

    /// <summary>True when this faction is one of Phase 42's guilds — it declares ranks.</summary>
    public bool IsGuild => RankNameKeys.Count > 0;
}
