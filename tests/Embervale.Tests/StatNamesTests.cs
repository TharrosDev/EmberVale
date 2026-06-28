using System;
using System.Collections.Generic;
using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

public class StatNamesTests
{
    [Fact]
    public void EveryStatType_MapsToADistinctNonFallbackKey()
    {
        var keys = new HashSet<string>();
        foreach (StatType stat in Enum.GetValues<StatType>())
        {
            string key = StatNames.Key(stat);
            Assert.False(string.IsNullOrEmpty(key));
            Assert.NotEqual("stat.unknown", key); // a new enum value with no mapping would land here
            Assert.True(keys.Add(key), $"duplicate stat key '{key}' for {stat}");
        }
    }
}
