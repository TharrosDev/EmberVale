using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Magic;
using Embervale.Onboarding;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>Where a pin lands in the spellbook, what the HUD spell row says about the wheel, and
/// the copy both of them and the wheel's tutorial hint read.</summary>
public class SpellPinRulesTests
{
    private static string[] Slots(params string[] pinned)
    {
        string[] slots = SpellFavouritesRules.Empty();
        Array.Copy(pinned, slots, pinned.Length);
        return slots;
    }

    private static string[] Full() => Enumerable.Range(0, SpellFavouritesRules.SlotCount).Select(i => $"spell.{i}").ToArray();

    [Fact]
    public void AnUnpinnedSpell_TakesTheFirstFreeSlot()
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 2), SpellPinRules.Decide(Slots("a", "b"), "c", SpellPinRules.NoSlot));
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 1), SpellPinRules.Decide(Slots("a", "", "b"), "c", SpellPinRules.NoSlot));
    }

    [Fact]
    public void APinnedSpell_IsUnpinnedFromItsOwnSlot()
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Unpin, 1), SpellPinRules.Decide(Slots("a", "b"), "b", SpellPinRules.NoSlot));
    }

    [Fact]
    public void WithEverySlotTakenAndNoneChosen_ThePinIsRefused()
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Full, SpellPinRules.NoSlot), SpellPinRules.Decide(Full(), "new", SpellPinRules.NoSlot));
    }

    [Fact]
    public void AChosenSlot_IsReplaced_EvenWhenOthersAreFree()
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 0), SpellPinRules.Decide(Slots("a"), "c", 0));
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 5), SpellPinRules.Decide(Full(), "new", 5));

        // A spell pinned elsewhere moves to the chosen slot (the caster trades the two).
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 3), SpellPinRules.Decide(Slots("a", "b"), "a", 3));
    }

    [Fact]
    public void TheSpellAlreadyInTheChosenSlot_IsUnpinnedFromIt()
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Unpin, 1), SpellPinRules.Decide(Slots("a", "b"), "b", 1));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(SpellFavouritesRules.SlotCount)]
    public void AChosenSlotThatDoesNotExist_IsNoChoice(int chosen)
    {
        Assert.Equal(new SpellPinChoice(SpellPinKind.Pin, 1), SpellPinRules.Decide(Slots("a"), "c", chosen));
    }

    [Fact]
    public void EveryDecision_IsOneTheFavouritesAccept()
    {
        // Whatever Decide answers, applying it through the slot rules leaves the spell where the
        // button said it would be.
        var cases = new List<(string[] Slots, string Id, int Chosen)>
        {
            (Slots("a", "b"), "c", SpellPinRules.NoSlot),
            (Slots("a", "b"), "a", SpellPinRules.NoSlot),
            (Slots("a", "b"), "a", 1),
            (Full(), "spell.3", 6),
            (Full(), "new", 0),
        };
        foreach ((string[] slots, string id, int chosen) in cases)
        {
            SpellPinChoice choice = SpellPinRules.Decide(slots, id, chosen);
            Assert.True(SpellFavouritesRules.Set(slots, choice.Slot, choice.Kind == SpellPinKind.Pin ? id : SpellFavouritesRules.None));
            Assert.Equal(choice.Kind == SpellPinKind.Pin ? choice.Slot : -1, SpellFavouritesRules.IndexOf(slots, id));
        }
    }

    [Fact]
    public void SlotsAreNumberedFromOne_AsTheWheelDrawsThemFromTheTop()
    {
        Assert.Equal(1, SpellPinRules.SlotNumber(0));
        Assert.Equal(SpellFavouritesRules.SlotCount, SpellPinRules.SlotNumber(SpellFavouritesRules.SlotCount - 1));
        Assert.Equal(SpellFavouritesRules.SlotCount, SpellWheelRules.FavouriteWedges);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(25, true)]
    public void TheHudNamesTheWheel_OnlyWithASecondSpell(int known, bool shown)
    {
        Assert.Equal(shown, SpellPinRules.ShowsWheelHint(known));
    }

    [Theory]
    [InlineData("spell.a", "spell.b", "spell.a")]
    [InlineData("", "spell.b", "")]
    [InlineData(null, "spell.b", "")]
    [InlineData("spell.b", "spell.b", "")]
    public void TheGhost_IsThePreviousSpell_UnlessItIsThePreparedOne(string? previous, string selected, string ghost)
    {
        Assert.Equal(ghost, SpellPinRules.Ghost(previous, selected));
    }

    [Fact]
    public void TheWheelHint_SaysHoldAndTap_WithItsGlyph_AndNoEmDash()
    {
        Dictionary<string, string> catalogue = Catalogue();
        string key = TutorialScript.HintKey(TutorialStep.SpellWheel);
        Assert.True(catalogue.TryGetValue(key, out string? copy), $"{key} is not in strings.csv");
        Assert.Equal("Hold {0} for the spell wheel. Tap it to swap back to your last spell.", copy);
        Assert.Equal(Embervale.Core.GameInput.CycleSpell, TutorialScript.ActionFor(TutorialStep.SpellWheel));
    }

    [Fact]
    public void EveryKeyTheRowAndTheBookAdded_IsInTheCatalogue_WithNoEmDash()
    {
        Dictionary<string, string> catalogue = Catalogue();
        string[] keys =
        {
            "hud.spell.wheel_hold", "hud.spell.wheel_tap", "spellbook.pins", "spellbook.pins_chosen",
            "spellbook.pins_full", "spellbook.pin", "spellbook.pin_to", "spellbook.unpin", "spellbook.prepare",
            "spellbook.prepared", "spellbook.legend.pin", "tutorial.spell_wheel_hold", "wheel.slot.empty", "wheel.slot.hint",
        };
        foreach (string key in keys)
        {
            Assert.True(catalogue.TryGetValue(key, out string? copy), $"{key} is not in strings.csv");
            Assert.DoesNotContain('—', copy);
        }

        Assert.Contains("{0}", catalogue["spellbook.pins_chosen"]);
        Assert.Contains("{0}", catalogue["spellbook.pin_to"]);
    }

    private static Dictionary<string, string> Catalogue()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Embervale.sln")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
        }

        var catalogue = new Dictionary<string, string>();
        foreach (string line in File.ReadLines(Path.Combine(root, "data", "locale", "strings.csv")))
        {
            int comma = line.IndexOf(',');
            if (line.Length == 0 || line[0] == '#' || comma <= 0)
            {
                continue;
            }

            catalogue[line[..comma]] = line[(comma + 1)..].Trim().Trim('"');
        }

        return catalogue;
    }
}
