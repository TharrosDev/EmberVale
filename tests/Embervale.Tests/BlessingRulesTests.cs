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

    [Fact]
    public void ReplaceClaims_ClearsTheLiveRunBeforeRestoring()
    {
        var claims = new HashSet<string> { "shrine.solaryn", "shrine.old" };

        BlessingRules.ReplaceClaims(claims, new[] { "shrine.elyndra", "shrine.elyndra", string.Empty });

        Assert.Equal(new[] { "shrine.elyndra" }, claims);
    }
}
