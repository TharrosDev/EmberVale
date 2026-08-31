using System.Collections.Generic;
using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The Phase 42A membership contract. `FactionResource` is a Godot type, so these drive the
/// primitive overload — which is the whole reason it exists (`Embervale.Tests.csproj` cannot
/// construct a GodotObject).
/// </summary>
public sealed class GuildRulesTests
{
    private const string Dawnwardens = "faction.dawnwardens";
    private const int Ranks = 3;

    private static GuildStanding Resolve(params string[] flags)
    {
        var set = new HashSet<string>(flags);
        return GuildRules.Resolve(set.Contains, Dawnwardens, Ranks);
    }

    [Fact]
    public void Flags_AreDerivedFromTheFactionId()
    {
        Assert.Equal("guild.dawnwardens.joined", GuildRules.JoinedFlag(Dawnwardens));
        Assert.Equal("guild.dawnwardens.refused", GuildRules.RefusedFlag(Dawnwardens));
        Assert.Equal("guild.dawnwardens.left", GuildRules.LeftFlag(Dawnwardens));
        Assert.Equal("guild.dawnwardens.offered", GuildRules.OfferedFlag(Dawnwardens));
        Assert.Equal("guild.dawnwardens.finale", GuildRules.FinaleFlag(Dawnwardens));
        Assert.Equal("guild.dawnwardens.rank2", GuildRules.RankFlag(Dawnwardens, 2));
    }

    [Fact]
    public void Slug_SurvivesAnIdWithoutThePrefix()
    {
        Assert.Equal("dawnwardens", GuildRules.Slug(Dawnwardens));
        Assert.Equal("dawnwardens", GuildRules.Slug("dawnwardens"));
        Assert.Equal(string.Empty, GuildRules.Slug(string.Empty));
    }

    /// <summary>An empty id must not produce a flag that a set could accidentally contain.</summary>
    [Fact]
    public void AnEmptyIdResolvesToUnknownAndNeverMatches()
    {
        var set = new HashSet<string> { string.Empty };

        GuildStanding standing = GuildRules.Resolve(set.Contains, string.Empty, Ranks);

        Assert.Equal(GuildState.Unknown, standing.State);
        Assert.Equal(GuildContradiction.None, standing.Contradiction);
    }

    // --- The five default and terminal states -------------------------------

    [Fact]
    public void NoFlags_IsUnknown() => Assert.Equal(GuildState.Unknown, Resolve().State);

    [Fact]
    public void Offered_ReadsAsOffered() =>
        Assert.Equal(GuildState.Offered, Resolve(GuildRules.OfferedFlag(Dawnwardens)).State);

    [Fact]
    public void Refused_ReadsAsRefused() =>
        Assert.Equal(GuildState.Refused, Resolve(GuildRules.OfferedFlag(Dawnwardens), GuildRules.RefusedFlag(Dawnwardens)).State);

    [Fact]
    public void Joined_ReadsAsMember() =>
        Assert.Equal(GuildState.Member, Resolve(GuildRules.OfferedFlag(Dawnwardens), GuildRules.JoinedFlag(Dawnwardens)).State);

    [Fact]
    public void Left_BeatsJoined()
    {
        // ⚠️ THE ORDER IS THE DESIGN. Leaving does not erase having joined — the join flag is still
        // set — so a rule that checked `joined` first would draw a departed player as a member.
        GuildStanding standing = Resolve(GuildRules.JoinedFlag(Dawnwardens), GuildRules.LeftFlag(Dawnwardens));

        Assert.Equal(GuildState.Left, standing.State);
        Assert.Equal(GuildContradiction.None, standing.Contradiction);
    }

    [Fact]
    public void Finale_BeatsBothMembershipAndDeparture()
    {
        GuildStanding standing = Resolve(
            GuildRules.JoinedFlag(Dawnwardens), GuildRules.FinaleFlag(Dawnwardens), GuildRules.RankFlag(Dawnwardens, 1));

        Assert.Equal(GuildState.Finale, standing.State);
        Assert.True(standing.IsMember);
    }

    // --- Ranks are cumulative ------------------------------------------------

    [Fact]
    public void Rank_IsTheHighestContiguousFlagHeld()
    {
        GuildStanding standing = Resolve(
            GuildRules.JoinedFlag(Dawnwardens),
            GuildRules.RankFlag(Dawnwardens, 1),
            GuildRules.RankFlag(Dawnwardens, 2));

        Assert.Equal(2, standing.Rank);
        Assert.Equal(GuildContradiction.None, standing.Contradiction);
    }

    [Fact]
    public void AGapDoesNotPromote()
    {
        // rank3 without rank2 is a hand-written flag. It must not read as seniority the arc never
        // awarded, and it must be reported rather than silently rounded down.
        GuildStanding standing = Resolve(
            GuildRules.JoinedFlag(Dawnwardens),
            GuildRules.RankFlag(Dawnwardens, 1),
            GuildRules.RankFlag(Dawnwardens, 3));

        Assert.Equal(1, standing.Rank);
        Assert.Equal(GuildContradiction.RankGap, standing.Contradiction);
    }

    [Fact]
    public void ARankAboveTheDeclaredCountIsOutOfRange()
    {
        var flags = new HashSet<string> { GuildRules.JoinedFlag(Dawnwardens) };
        for (int rank = 1; rank <= 4; rank++)
        {
            flags.Add(GuildRules.RankFlag(Dawnwardens, rank));
        }

        GuildStanding standing = GuildRules.Resolve(flags.Contains, Dawnwardens, Ranks);

        Assert.Equal(Ranks, standing.Rank);
        Assert.Equal(GuildContradiction.RankOutOfRange, standing.Contradiction);
    }

    // --- Contradictions ------------------------------------------------------

    [Theory]
    [InlineData("rank1", GuildContradiction.RankWithoutMembership)]
    [InlineData("finale", GuildContradiction.FinaleWithoutMembership)]
    [InlineData("left", GuildContradiction.LeftWithoutMembership)]
    public void AStateThatNeedsMembershipReportsItsAbsence(string suffix, GuildContradiction expected)
    {
        GuildStanding standing = Resolve("guild.dawnwardens." + suffix);

        Assert.Equal(expected, standing.Contradiction);
    }

    [Fact]
    public void RefusedAndJoined_IsAContradiction()
    {
        // Joining must CLEAR the refusal at the choke point; the two are answers to one question.
        GuildStanding standing = Resolve(GuildRules.RefusedFlag(Dawnwardens), GuildRules.JoinedFlag(Dawnwardens));

        Assert.Equal(GuildState.Member, standing.State);
        Assert.Equal(GuildContradiction.RefusedAndJoined, standing.Contradiction);
    }

    // --- The rejoin policy is a gate, not a state ---------------------------

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ADepartedPlayerMayRejoinOnlyWhenTheGuildAllowsIt(bool rejoinAllowed, bool expected)
    {
        GuildStanding standing = Resolve(GuildRules.JoinedFlag(Dawnwardens), GuildRules.LeftFlag(Dawnwardens));

        Assert.Equal(expected, GuildRules.CanJoin(standing, rejoinAllowed));
    }

    [Fact]
    public void AMemberCannotJoinAgainRegardlessOfPolicy()
    {
        GuildStanding member = Resolve(GuildRules.JoinedFlag(Dawnwardens));

        Assert.False(GuildRules.CanJoin(member, rejoinAllowed: true));
    }

    [Fact]
    public void ARefusalDoesNotBlockALaterOffer()
    {
        GuildStanding refused = Resolve(GuildRules.RefusedFlag(Dawnwardens));

        Assert.True(GuildRules.CanJoin(refused, rejoinAllowed: false));
    }

    /// <summary>
    /// The persistence contract, at the level this suite can reach: membership is nothing but a set
    /// of flags, so a load that REPLACES the set replaces the membership exactly — no second ledger
    /// can survive it. `StoryFlagsComponent.Load` clears before restoring; this pins that a cleared
    /// and repopulated set resolves to the saved state and keeps nothing from the abandoned one.
    /// </summary>
    [Fact]
    public void ReplacingTheFlagSetReplacesMembership()
    {
        var live = new HashSet<string>
        {
            GuildRules.JoinedFlag(Dawnwardens),
            GuildRules.RankFlag(Dawnwardens, 1),
            GuildRules.RankFlag(Dawnwardens, 2),
        };

        Assert.Equal(2, GuildRules.Resolve(live.Contains, Dawnwardens, Ranks).Rank);

        // What a load does: clear, then restore a save taken before the promotion.
        live.Clear();
        live.Add(GuildRules.OfferedFlag(Dawnwardens));

        GuildStanding restored = GuildRules.Resolve(live.Contains, Dawnwardens, Ranks);

        Assert.Equal(GuildState.Offered, restored.State);
        Assert.Equal(0, restored.Rank);
    }
}
