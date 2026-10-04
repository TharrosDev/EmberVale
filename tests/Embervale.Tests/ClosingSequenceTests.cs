using System;
using System.Collections.Generic;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

public class ClosingSequenceTests
{
    [Theory]
    [InlineData(true, "closing.absorbed")]
    [InlineData(false, "closing.refused")]
    public void TheActOneClosingIsOneCardThatBranchesOnTheEmber(bool absorbed, string expected) =>
        Assert.Equal(new[] { expected }, ClosingSequence.CardsFor(absorbed));

    [Fact]
    public void TheClosingCardsHaveTextAndLeaveTheRoutingToTheElder()
    {
        Dictionary<string, string> rows = StringsCsv.Rows();
        foreach (string key in new[] { "closing.absorbed", "closing.refused" })
        {
            Assert.True(rows.TryGetValue(key, out string? text) && text.Length > 2, $"'{key}' has no text");

            // Missions 10 to 18 and the Elder's aftermath name the three realms; the card does not.
            foreach (string realm in new[] { "Stormcrown", "Frostfang", "Ashen Wilds", "Sunspire", "Southmarch", "END OF ACT" })
            {
                Assert.DoesNotContain(realm, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.False(rows.ContainsKey("closing.next"));
        Assert.False(rows.ContainsKey("closing.answer"));
    }
}
