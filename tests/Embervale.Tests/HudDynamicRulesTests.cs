using System;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The per-element HUD modes: what Always, Dynamic and Hidden mean for each element, the preset
/// tables, and the conversion to and from the saved list.
/// </summary>
public class HudDynamicRulesTests
{
    private static readonly HudSignals Quiet = default;
    private static readonly HudSignals Everything = new(true, true, true, true, true);

    public static TheoryData<HudElement> Elements()
    {
        var data = new TheoryData<HudElement>();
        foreach (HudElement element in Enum.GetValues<HudElement>())
        {
            data.Add(element);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void Always_ShowsWhateverIsHappening(HudElement element)
    {
        Assert.True(HudDynamicRules.Visible(element, HudElementMode.Always, Quiet));
        Assert.True(HudDynamicRules.Visible(element, HudElementMode.Always, Everything));
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void Hidden_StaysHiddenEvenOnRecall(HudElement element)
    {
        Assert.False(HudDynamicRules.Visible(element, HudElementMode.Hidden, Everything));
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void Recall_BringsBackEveryDynamicElement(HudElement element)
    {
        Assert.True(HudDynamicRules.Visible(element, HudElementMode.Dynamic, Quiet with { RecallHeld = true }));
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void ADynamicElement_ShowsWhenItsOwnContentChanged(HudElement element)
    {
        Assert.True(HudDynamicRules.Visible(element, HudElementMode.Dynamic, Quiet with { RecentlyChanged = true }));
    }

    [Theory]
    [InlineData(HudElement.Vitals)]
    [InlineData(HudElement.Hotbar)]
    [InlineData(HudElement.Compass)]
    [InlineData(HudElement.Minimap)]
    [InlineData(HudElement.Clock)]
    [InlineData(HudElement.QuestTracker)]
    [InlineData(HudElement.Party)]
    [InlineData(HudElement.TargetPlate)]
    [InlineData(HudElement.EnemyPlates)]
    [InlineData(HudElement.Crosshair)]
    public void ASteadyElement_StepsBackWhenNothingIsHappening(HudElement element)
    {
        Assert.False(HudDynamicRules.Visible(element, HudElementMode.Dynamic, Quiet));
    }

    [Theory]
    [InlineData(HudElement.DamageNumbers)]
    [InlineData(HudElement.Prompts)]
    [InlineData(HudElement.Toasts)]
    [InlineData(HudElement.Subtitles)]
    public void ATransientElement_IsUnchangedByDynamic(HudElement element)
    {
        Assert.True(HudDynamicRules.Visible(element, HudElementMode.Dynamic, Quiet));
    }

    [Fact]
    public void Vitals_ComeUpInAFightWhenHurtAndUnderAMenu()
    {
        Assert.True(HudDynamicRules.Visible(HudElement.Vitals, HudElementMode.Dynamic, Quiet with { InCombat = true }));
        Assert.True(HudDynamicRules.Visible(HudElement.Vitals, HudElementMode.Dynamic, Quiet with { BelowMax = true }));
        Assert.True(HudDynamicRules.Visible(HudElement.Vitals, HudElementMode.Dynamic, Quiet with { MenuOpen = true }));
    }

    [Fact]
    public void Navigation_DoesNotComeUpForAFight()
    {
        var fight = new HudSignals(InCombat: true, BelowMax: true, RecentlyChanged: false, RecallHeld: false, MenuOpen: false);
        Assert.False(HudDynamicRules.Visible(HudElement.Compass, HudElementMode.Dynamic, fight));
        Assert.False(HudDynamicRules.Visible(HudElement.Minimap, HudElementMode.Dynamic, fight));
        Assert.False(HudDynamicRules.Visible(HudElement.QuestTracker, HudElementMode.Dynamic, fight));
        Assert.True(HudDynamicRules.Visible(HudElement.Crosshair, HudElementMode.Dynamic, fight));
        Assert.True(HudDynamicRules.Visible(HudElement.Hotbar, HudElementMode.Dynamic, fight));
    }

    [Fact]
    public void Lingering_CoversTheWindowAndNothingElse()
    {
        Assert.True(HudDynamicRules.Lingering(10.0, 10.0, 4.0));
        Assert.True(HudDynamicRules.Lingering(13.9, 10.0, 4.0));
        Assert.False(HudDynamicRules.Lingering(14.0, 10.0, 4.0));
        Assert.False(HudDynamicRules.Lingering(9.0, 10.0, 4.0));
        Assert.False(HudDynamicRules.Lingering(10.0, double.NegativeInfinity, 4.0));
    }

    [Fact]
    public void TheFullPreset_IsEveryElementAlways()
    {
        Assert.All(HudPresets.Modes(HudPreset.Full), mode => Assert.Equal(HudElementMode.Always, mode));
        Assert.All(HudPresets.Modes(HudPreset.Custom), mode => Assert.Equal(HudElementMode.Always, mode));
    }

    [Fact]
    public void EveryPreset_CoversEveryElement()
    {
        Assert.Equal(Enum.GetValues<HudElement>().Length, HudPresets.ElementCount);
        foreach (HudPreset preset in Enum.GetValues<HudPreset>())
        {
            Assert.Equal(HudPresets.ElementCount, HudPresets.Modes(preset).Length);
        }
    }

    [Fact]
    public void NoPreset_HidesWhatAPlayerCannotPlayWithout()
    {
        foreach (HudPreset preset in Enum.GetValues<HudPreset>())
        {
            Assert.Equal(HudElementMode.Always, HudPresets.Mode(preset, HudElement.Prompts));
            Assert.Equal(HudElementMode.Always, HudPresets.Mode(preset, HudElement.Subtitles));
            Assert.Equal(HudElementMode.Always, HudPresets.Mode(preset, HudElement.Toasts));
            Assert.NotEqual(HudElementMode.Hidden, HudPresets.Mode(preset, HudElement.Vitals));
        }
    }

    [Theory]
    [InlineData(HudPreset.Full)]
    [InlineData(HudPreset.Dynamic)]
    [InlineData(HudPreset.Minimal)]
    public void APreset_SurvivesTheSavedListAndIsRecognised(HudPreset preset)
    {
        int[] saved = HudPresets.ToSaved(HudPresets.Modes(preset));

        Assert.Equal(HudPresets.Modes(preset), HudPresets.FromSaved(saved));
        Assert.Equal(preset, HudPresets.Detect(saved));
    }

    [Fact]
    public void AnEmptyOrMissingSavedList_IsTheFullPreset()
    {
        Assert.Equal(HudPreset.Full, HudPresets.Detect((int[]?)null));
        Assert.Equal(HudPreset.Full, HudPresets.Detect(Array.Empty<int>()));
        Assert.Equal(HudPreset.Full, HudPresets.Detect(new[] { 0, 0, 99, -3 }));
    }

    [Fact]
    public void OneElementMovedOffAPreset_IsCustom()
    {
        HudElementMode[] modes = HudPresets.Modes(HudPreset.Dynamic);
        modes[(int)HudElement.Minimap] = HudElementMode.Hidden;

        Assert.Equal(HudPreset.Custom, HudPresets.Detect(modes));
        Assert.Equal(HudPreset.Custom, HudPresets.Detect(new[] { HudElementMode.Always }));
    }
}
