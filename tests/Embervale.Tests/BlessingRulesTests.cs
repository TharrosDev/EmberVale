using System.Collections.Generic;
using Embervale.Shrines;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pure persistence semantics for shrine claims. Node-backed save round-trips are verified
/// in-engine; these cases pin the set rules that make a loaded state replace the live one.</summary>
public sealed class BlessingRulesTests
{
    [Fact]
    public void TryClaim_AcceptsFirstVisitAndRefusesTheSecond()
    {
        var claims = new HashSet<string>();

        Assert.True(BlessingRules.TryClaim(claims, "shrine.solaryn"));
        Assert.False(BlessingRules.TryClaim(claims, "shrine.solaryn"));
        Assert.Single(claims);
    }

    [Fact]
    public void TryClaim_RefusesAnEmptyId()
    {
        var claims = new HashSet<string>();

        Assert.False(BlessingRules.TryClaim(claims, string.Empty));
        Assert.Empty(claims);
    }

    [Theory]
    [InlineData(0, ShrineOutcome.Blessed)]
    [InlineData(39, ShrineOutcome.Blessed)]
    [InlineData(40, ShrineOutcome.Refused)]
    [InlineData(100, ShrineOutcome.Refused)]
    public void Decide_RefusesAtOrAboveTheAuthoredThreshold(int corruption, ShrineOutcome expected)
    {
        var claims = new HashSet<string>();

        Assert.Equal(expected, BlessingRules.Decide(claims, "shrine.solaryn", corruption, 40));
    }

    [Fact]
    public void Decide_LeavesTheClaimSetUntouchedOnARefusal()
    {
        var claims = new HashSet<string>();

        Assert.Equal(ShrineOutcome.Refused, BlessingRules.Decide(claims, "shrine.solaryn", 90, 40));

        Assert.Empty(claims);
    }

    /// <summary>Corruption gates the granting, never the granted passive: a player who claimed while
    /// clean and later fell keeps the blessing and reads the already-visited line, not a refusal.</summary>
    [Fact]
    public void Decide_KeepsAClaimedBlessingWhenCorruptionLaterPassesTheThreshold()
    {
        var claims = new HashSet<string> { "shrine.solaryn" };

        Assert.Equal(ShrineOutcome.AlreadyClaimed, BlessingRules.Decide(claims, "shrine.solaryn", 90, 40));
    }

    [Fact]
    public void Decide_TreatsAnEmptyIdAsNothingToGrant()
    {
        var claims = new HashSet<string>();

        Assert.Equal(ShrineOutcome.AlreadyClaimed, BlessingRules.Decide(claims, string.Empty, 0, 40));
        Assert.Empty(claims);
    }

    [Fact]
    public void ReplaceClaims_ClearsTheLiveRunBeforeRestoring()
    {
        var claims = new HashSet<string> { "shrine.solaryn", "shrine.old" };

        BlessingRules.ReplaceClaims(claims, new[] { "shrine.elyndra", "shrine.elyndra", string.Empty });

        Assert.Equal(new[] { "shrine.elyndra" }, claims);
    }
}
