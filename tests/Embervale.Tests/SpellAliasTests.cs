using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

public class SpellAliasTests
{
    [Theory]
    [InlineData("spell.firebolt", "spell.emberlash")]
    [InlineData("spell.fireball", "spell.sunfall")]
    [InlineData("spell.arcane_lance", "spell.null_lance")]
    [InlineData("spell.lesser_heal", "spell.mending_bloom")]
    public void RetiredId_ResolvesToItsReplacement(string old, string current)
    {
        Assert.True(SpellAliases.IsRetired(old));
        Assert.Equal(current, SpellAliases.Resolve(old));
    }

    [Fact]
    public void LiveId_ResolvesToItself()
    {
        Assert.Equal("spell.blink", SpellAliases.Resolve("spell.blink"));
        Assert.False(SpellAliases.IsRetired("spell.blink"));
    }

    [Fact]
    public void NoAliasTargetIsItselfRetired()
    {
        foreach (string target in SpellAliases.All.Values)
        {
            Assert.False(SpellAliases.IsRetired(target));
        }
    }
}
