using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

public class RespecRulesTests
{
    [Fact]
    public void NothingSpent_CostsNothing()
    {
        Assert.Equal(0, RespecRules.Cost(0, 0));
        Assert.Equal(0, RespecRules.Cost(0, 3));
        Assert.Equal(0, RespecRules.Cost(-4, 0));
    }

    [Theory]
    [InlineData(1, 0, 42)]     // 30 + 12
    [InlineData(10, 0, 150)]   // 30 + 120
    [InlineData(20, 0, 270)]
    [InlineData(54, 0, 678)]   // every point a character earns
    public void FirstRespec_IsBasePlusPerPoint(int spent, int respecs, int expected)
    {
        Assert.Equal(expected, RespecRules.Cost(spent, respecs));
    }

    [Theory]
    [InlineData(0, 270)]
    [InlineData(1, 338)]  // 270 * 1.25 = 337.5, rounds half up
    [InlineData(2, 405)]
    [InlineData(3, 473)]  // 472.5
    [InlineData(4, 540)]
    [InlineData(5, 540)]  // capped after four
    [InlineData(40, 540)]
    public void EachRepeatAddsAQuarter_CappedAfterFour(int respecs, int expected)
    {
        Assert.Equal(expected, RespecRules.Cost(20, respecs));
    }

    [Fact]
    public void NegativeRespecCount_IsTreatedAsZero()
    {
        Assert.Equal(RespecRules.Cost(20, 0), RespecRules.Cost(20, -3));
    }
}
