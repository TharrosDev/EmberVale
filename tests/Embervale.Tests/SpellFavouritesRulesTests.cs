using System.Linq;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The spell wheel's slot bookkeeping. The promises: a spell sits in at most one slot, learning one
/// takes the first free slot, rearranging never loses a pin, and a restore keeps nothing the caster
/// does not know.
/// </summary>
public class SpellFavouritesRulesTests
{
    private static readonly string[] Ten = Enumerable.Range(0, 10).Select(i => $"spell.s{i}").ToArray();

    [Fact]
    public void Empty_IsEightEmptySlots()
    {
        string[] slots = SpellFavouritesRules.Empty();

        Assert.Equal(8, SpellFavouritesRules.SlotCount);
        Assert.Equal(SpellFavouritesRules.SlotCount, slots.Length);
        Assert.All(slots, slot => Assert.Equal(SpellFavouritesRules.None, slot));
        Assert.Equal(0, SpellFavouritesRules.FirstFree(slots));
    }

    [Fact]
    public void Default_IsTheFirstEightKnownInOrder()
    {
        Assert.Equal(Ten.Take(8), SpellFavouritesRules.Default(Ten));

        string[] few = SpellFavouritesRules.Default(new[] { "spell.a", "spell.b" });
        Assert.Equal(new[] { "spell.a", "spell.b", "", "", "", "", "", "" }, few);
        Assert.All(SpellFavouritesRules.Default(System.Array.Empty<string>()), slot => Assert.Equal("", slot));
    }

    [Fact]
    public void Pin_TakesTheFirstFreeSlot_Once()
    {
        string[] slots = SpellFavouritesRules.Empty();
        slots[0] = "spell.a";
        slots[2] = "spell.c";

        Assert.True(SpellFavouritesRules.Pin(slots, "spell.b"));
        Assert.Equal(1, SpellFavouritesRules.IndexOf(slots, "spell.b"));

        Assert.False(SpellFavouritesRules.Pin(slots, "spell.b")); // already pinned
        Assert.False(SpellFavouritesRules.Pin(slots, ""));
        Assert.Equal(1, slots.Count(s => s == "spell.b"));
    }

    [Fact]
    public void Pin_WithNoRoom_LeavesTheSlotsAlone()
    {
        string[] slots = SpellFavouritesRules.Default(Ten);

        Assert.Equal(-1, SpellFavouritesRules.FirstFree(slots));
        Assert.False(SpellFavouritesRules.Pin(slots, "spell.s9"));
        Assert.Equal(Ten.Take(8), slots);
    }

    [Fact]
    public void Set_PutsASpellInASlot_AndAnEmptyIdClearsIt()
    {
        string[] slots = SpellFavouritesRules.Empty();

        Assert.True(SpellFavouritesRules.Set(slots, 3, "spell.a"));
        Assert.Equal("spell.a", slots[3]);
        Assert.False(SpellFavouritesRules.Set(slots, 3, "spell.a")); // nothing changed

        Assert.True(SpellFavouritesRules.Set(slots, 3, ""));
        Assert.Equal("", slots[3]);
        Assert.False(SpellFavouritesRules.Set(slots, 3, ""));
    }

    [Fact]
    public void Set_MovesAPinnedSpell_AndTradesTheTwoSlots()
    {
        string[] slots = SpellFavouritesRules.Empty();
        slots[0] = "spell.a";
        slots[5] = "spell.b";

        Assert.True(SpellFavouritesRules.Set(slots, 0, "spell.b"));

        Assert.Equal("spell.b", slots[0]);
        Assert.Equal("spell.a", slots[5]); // the displaced spell is not lost
        Assert.Equal(1, slots.Count(s => s == "spell.a"));
        Assert.Equal(1, slots.Count(s => s == "spell.b"));

        // Moving into an empty slot leaves the old one empty.
        Assert.True(SpellFavouritesRules.Set(slots, 7, "spell.a"));
        Assert.Equal("", slots[5]);
        Assert.Equal("spell.a", slots[7]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(99)]
    public void Set_RefusesASlotThatDoesNotExist(int slot)
    {
        string[] slots = SpellFavouritesRules.Empty();

        Assert.False(SpellFavouritesRules.Set(slots, slot, "spell.a"));
        Assert.All(slots, s => Assert.Equal("", s));
    }

    [Fact]
    public void Clear_EmptiesTheSlotHoldingTheSpell()
    {
        string[] slots = SpellFavouritesRules.Default(new[] { "spell.a", "spell.b", "spell.c" });

        Assert.True(SpellFavouritesRules.Clear(slots, "spell.b"));
        Assert.Equal(new[] { "spell.a", "", "spell.c", "", "", "", "", "" }, slots);
        Assert.False(SpellFavouritesRules.Clear(slots, "spell.b"));
        Assert.False(SpellFavouritesRules.Clear(slots, ""));

        // The freed slot is the next one a learned spell takes.
        Assert.True(SpellFavouritesRules.Pin(slots, "spell.d"));
        Assert.Equal("spell.d", slots[1]);
    }

    [Fact]
    public void Restore_WithNoSavedList_IsTheDefault()
    {
        Assert.Equal(SpellFavouritesRules.Default(Ten), SpellFavouritesRules.Restore(null, Ten));
    }

    [Fact]
    public void Restore_KeepsSavedSlots_IncludingEmptyOnes()
    {
        string[] known = { "spell.a", "spell.b", "spell.c" };
        string[] saved = { "", "spell.c", "", "spell.a", "", "", "", "" };

        // Not the default: the player emptied slot 0 and left spell.b off the wheel on purpose.
        Assert.Equal(saved, SpellFavouritesRules.Restore(saved, known));
    }

    [Fact]
    public void Restore_DropsUnknownAndDuplicateIds_AndFixesTheLength()
    {
        string[] known = { "spell.a", "spell.b" };

        Assert.Equal(
            new[] { "spell.a", "", "", "spell.b", "", "", "", "" },
            SpellFavouritesRules.Restore(new[] { "spell.a", "spell.gone", "spell.a", "spell.b" }, known));

        string[] tooLong = Enumerable.Repeat("spell.a", 12).ToArray();
        string[] restored = SpellFavouritesRules.Restore(tooLong, known);
        Assert.Equal(SpellFavouritesRules.SlotCount, restored.Length);
        Assert.Equal(1, restored.Count(s => s == "spell.a"));

        Assert.All(SpellFavouritesRules.Restore(System.Array.Empty<string>(), known), s => Assert.Equal("", s));
    }

    [Fact]
    public void PreviousAfterSelect_IsTheSpellJustLeft()
    {
        Assert.Equal("spell.a", SpellFavouritesRules.PreviousAfterSelect("", "spell.a", "spell.b"));
        Assert.Equal("spell.b", SpellFavouritesRules.PreviousAfterSelect("spell.a", "spell.b", "spell.a"));

        // Re-selecting the prepared spell is not a swap.
        Assert.Equal("spell.z", SpellFavouritesRules.PreviousAfterSelect("spell.z", "spell.a", "spell.a"));
    }

    [Fact]
    public void RestorePrevious_KeepsOnlyAKnownSpellThatIsNotSelected()
    {
        string[] known = { "spell.a", "spell.b" };

        Assert.Equal("spell.b", SpellFavouritesRules.RestorePrevious("spell.b", "spell.a", known));
        Assert.Equal("", SpellFavouritesRules.RestorePrevious("spell.a", "spell.a", known));
        Assert.Equal("", SpellFavouritesRules.RestorePrevious("spell.gone", "spell.a", known));
        Assert.Equal("", SpellFavouritesRules.RestorePrevious("", "spell.a", known));
        Assert.Equal("", SpellFavouritesRules.RestorePrevious(null, "spell.a", known));
    }
}
