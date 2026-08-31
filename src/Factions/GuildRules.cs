using System;

namespace Embervale.Factions;

/// <summary>Where the player stands with one guild. Ordered by how the rule reads, not by rank.</summary>
// APPEND ONLY: ordinals reach the dev console and the character screen — never reorder.
public enum GuildState
{
    /// <summary>The player has never been approached. Every guild starts here.</summary>
    Unknown,

    /// <summary>Membership was offered and has not been answered yet.</summary>
    Offered,

    /// <summary>The offer was declined. Terminal until the guild offers again.</summary>
    Refused,

    /// <summary>A member in good standing. <c>Rank</c> says how senior.</summary>
    Member,

    /// <summary>Joined once and walked away. Whether they can come back is authored, not derived.</summary>
    Left,

    /// <summary>The guild's arc is finished. Still a member; the story is over.</summary>
    Finale,
}

/// <summary>An authored state that cannot be true, named so the console and the validator can say
/// which one. Reported, never thrown — a bad save must still render.</summary>
public enum GuildContradiction
{
    None,

    /// <summary>Rank 3 without rank 2. Ranks are cumulative, so a gap means someone wrote a flag
    /// by hand instead of going through <see cref="GuildRules"/>.</summary>
    RankGap,

    /// <summary>A rank flag above the ranks the guild declares — nothing can name it.</summary>
    RankOutOfRange,

    /// <summary>Ranked without ever having joined.</summary>
    RankWithoutMembership,

    /// <summary>Refused and joined at once. Joining must clear the refusal; the two are answers to
    /// the same question.</summary>
    RefusedAndJoined,

    /// <summary>An ending for an arc that was never started.</summary>
    FinaleWithoutMembership,

    /// <summary>Left a guild that was never joined.</summary>
    LeftWithoutMembership,
}

/// <summary>The resolved standing: what to show, and what is wrong with it.</summary>
public readonly struct GuildStanding
{
    public GuildStanding(GuildState state, int rank, GuildContradiction contradiction)
    {
        State = state;
        Rank = rank;
        Contradiction = contradiction;
    }

    public GuildState State { get; }

    /// <summary>The highest CONTIGUOUS rank held, 0 when unranked. A gap does not promote.</summary>
    public int Rank { get; }

    public GuildContradiction Contradiction { get; }

    public bool IsMember => State is GuildState.Member or GuildState.Finale;
}

/// <summary>
/// The whole guild-membership contract (Phase 42A): the flag vocabulary all five guilds share, and
/// the one function that turns a player's story flags into a state. Godot-free and primitive-typed
/// so the ordinary unit suite pins it — the same shape as <c>Shrines.BlessingRules</c>.
///
/// ⚠️ <b>NOTHING HERE STORES ANYTHING.</b> `StoryFlagsComponent` is the only membership authority and
/// is already an `ISaveable` whose `Load` clears before restoring, so a loaded save REPLACES
/// membership rather than merging over it — for free, and only while no second ledger exists. The
/// UI, the console report and every quest from 42C on derive through this file.
///
/// ⚠️ <b>Flags are DERIVED, never authored.</b> A flag id written by hand into a dialogue `.tres` is
/// a string two files have to agree about forever (invariant 12). Everything comes from the faction
/// id and a rank number, so a typo cannot become a state.
/// </summary>
public static class GuildRules
{
    /// <summary>Every guild flag begins with this, so a report can find them without a registry.</summary>
    public const string FlagPrefix = "guild.";

    /// <summary>The most ranks a guild may declare. Beyond this the UI has no room and the rank
    /// vocabulary stops being readable; below one, a guild has nothing to rank up in. Both ends
    /// fail silently in authored data, so <c>ContentValidator</c> checks both (invariant 8).</summary>
    public const int MaxRanks = 5;

    /// <summary>Strips the <c>faction.</c> prefix: <c>faction.dawnwardens</c> → <c>dawnwardens</c>.</summary>
    public static string Slug(string factionId)
    {
        if (string.IsNullOrEmpty(factionId))
        {
            return string.Empty;
        }

        const string prefix = "faction.";
        return factionId.StartsWith(prefix, System.StringComparison.Ordinal)
            ? factionId[prefix.Length..]
            : factionId;
    }

    public static string OfferedFlag(string factionId) => Flag(factionId, "offered");

    public static string RefusedFlag(string factionId) => Flag(factionId, "refused");

    public static string JoinedFlag(string factionId) => Flag(factionId, "joined");

    public static string LeftFlag(string factionId) => Flag(factionId, "left");

    public static string FinaleFlag(string factionId) => Flag(factionId, "finale");

    /// <summary>The flag for one rank. Ranks are cumulative: holding rank 3 means holding 1 and 2.</summary>
    public static string RankFlag(string factionId, int rank) => Flag(factionId, $"rank{rank}");

    private static string Flag(string factionId, string suffix)
    {
        string slug = Slug(factionId);
        return slug.Length == 0 ? string.Empty : FlagPrefix + slug + "." + suffix;
    }

    /// <summary>Resolves a guild's state from the player's flags.</summary>
    public static GuildStanding Resolve(Predicate<string> has, FactionResource guild) =>
        Resolve(has, guild.Id, guild.RankNameKeys.Count);

    /// <summary>
    /// Resolves a guild's state from the player's flags.
    ///
    /// ⚠️ <b>The ORDER is the design</b>, exactly as in `BlessingRules.Decide`, and getting it wrong
    /// is invisible to a test that only checks one branch:
    /// <b>finale → left → joined → refused → offered → unknown</b>.
    /// A finished arc must not read as "Member, rank 3" (the arc is the more specific truth); a
    /// player who left must not read as a member because the join flag is still set — leaving does
    /// not erase having joined, it is a fact ON TOP of it; and a player who refused once and was
    /// asked again years later reads as a member the moment they join, because joining clears the
    /// refusal at the choke point rather than being out-voted by it here.
    /// </summary>
    /// <param name="has">Reads one flag. A predicate rather than a set so the live path can pass
    /// <c>StoryFlagsComponent.Has</c> straight in — no copy per UI rebuild — while the tests pass a
    /// <c>HashSet</c>'s <c>Contains</c> and stay Godot-free.</param>
    public static GuildStanding Resolve(Predicate<string> has, string factionId, int rankCount)
    {
        bool joined = Has(has, JoinedFlag(factionId));
        bool left = Has(has, LeftFlag(factionId));
        bool refused = Has(has, RefusedFlag(factionId));
        bool finale = Has(has, FinaleFlag(factionId));

        (int rank, GuildContradiction rankProblem) = ResolveRank(has, factionId, rankCount);

        GuildState state =
            finale ? GuildState.Finale
            : left ? GuildState.Left
            : joined ? GuildState.Member
            : refused ? GuildState.Refused
            : Has(has, OfferedFlag(factionId)) ? GuildState.Offered
            : GuildState.Unknown;

        return new GuildStanding(state, rank, Contradiction(joined, left, refused, finale, rank, rankProblem));
    }

    /// <summary>The highest contiguous rank held. ⚠️ A GAP DOES NOT PROMOTE — rank 3 without rank 2
    /// resolves to rank 1 and reports the gap, because the alternative is a hand-written flag
    /// silently granting seniority the arc never awarded.</summary>
    private static (int Rank, GuildContradiction Problem) ResolveRank(Predicate<string> has, string factionId, int rankCount)
    {
        int contiguous = 0;
        while (Has(has, RankFlag(factionId, contiguous + 1)))
        {
            contiguous++;
        }

        // Look past the contiguous run for the two things it cannot see: a gap, and a rank the
        // guild does not declare. Scanning to MaxRanks rather than to rankCount is deliberate —
        // an out-of-range flag is exactly what needs finding.
        for (int rank = contiguous + 1; rank <= MaxRanks; rank++)
        {
            if (Has(has, RankFlag(factionId, rank)))
            {
                return (contiguous, GuildContradiction.RankGap);
            }
        }

        return contiguous > rankCount
            ? (rankCount, GuildContradiction.RankOutOfRange)
            : (contiguous, GuildContradiction.None);
    }

    private static GuildContradiction Contradiction(
        bool joined, bool left, bool refused, bool finale, int rank, GuildContradiction rankProblem)
    {
        if (rankProblem != GuildContradiction.None)
        {
            return rankProblem;
        }

        if (rank > 0 && !joined)
        {
            return GuildContradiction.RankWithoutMembership;
        }

        if (joined && refused)
        {
            return GuildContradiction.RefusedAndJoined;
        }

        if (finale && !joined)
        {
            return GuildContradiction.FinaleWithoutMembership;
        }

        if (left && !joined)
        {
            return GuildContradiction.LeftWithoutMembership;
        }

        return GuildContradiction.None;
    }

    /// <summary>
    /// May this guild be joined right now? The one question the flags cannot answer on their own,
    /// because <see cref="FactionResource.RejoinAllowed"/> is authored fiction. It is a GATE, not a
    /// state: a rejoin clears the <c>left</c> flag at the choke point, so refusing here is the only
    /// place the policy can be enforced — after the fact there is nothing left to detect.
    /// </summary>
    public static bool CanJoin(GuildStanding standing, bool rejoinAllowed) => standing.State switch
    {
        GuildState.Member or GuildState.Finale => false,
        GuildState.Left => rejoinAllowed,
        _ => true,
    };

    private static bool Has(Predicate<string> has, string flag) => flag.Length > 0 && has(flag);
}
