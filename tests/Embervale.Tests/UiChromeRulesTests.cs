using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins what a full-screen panel gives up for the strips drawn in its gutters. The row that matters
/// is the handheld one: 853x533 logical pixels (a 1280x800 screen at UI scale 1.5).
/// </summary>
public class UiChromeRulesTests
{
    private const float HandheldWidth = 853f;
    private const float HandheldHeight = 533f;

    [Fact]
    public void Gutter_ShrinksBelowTheNarrowWidth()
    {
        Assert.Equal(UiTheme.SpaceLg, UiChromeRules.Gutter(HandheldWidth));
        Assert.Equal(UiChromeRules.WideGutter, UiChromeRules.Gutter(1280f));
        Assert.Equal(UiChromeRules.WideGutter, UiChromeRules.Gutter(UiChromeRules.NarrowWidth));
    }

    [Fact]
    public void ADesktopPanel_GivesUpNothingForTheLegend()
    {
        int gutter = UiChromeRules.Gutter(1280f);
        Assert.Equal(gutter, UiChromeRules.BottomInset(gutter, legend: true));
    }

    [Fact]
    public void AHandheldPanel_LeavesTheLegendRowClear()
    {
        int gutter = UiChromeRules.Gutter(HandheldWidth);
        int inset = UiChromeRules.BottomInset(gutter, legend: true);

        Assert.True(inset >= UiChromeRules.LegendHeight + (UiChromeRules.ChromeMargin * 2));
        Assert.Equal(gutter, UiChromeRules.BottomInset(gutter, legend: false));

        // And costs the panel no more than a caption line of its height.
        Assert.True(inset - gutter <= UiTheme.CaptionFontSize);
    }

    [Fact]
    public void TheLegendRow_HoldsAGlyph()
    {
        Assert.True(UiChromeRules.LegendHeight >= UiGlyph.Height);
    }

    [Fact]
    public void ADesktopHubScreen_GivesUpNothingForTheStrip()
    {
        int gutter = UiChromeRules.Gutter(1280f);
        Assert.Equal(UiTheme.ControlHeight, UiChromeRules.HubHeight(720f));
        Assert.Equal(gutter, UiChromeRules.TopInset(gutter, hub: true, 720f));
    }

    [Fact]
    public void APanelOutsideTheHub_KeepsItsTopGutter()
    {
        int gutter = UiChromeRules.Gutter(HandheldWidth);
        Assert.Equal(gutter, UiChromeRules.TopInset(gutter, hub: false, HandheldHeight));
    }

    [Fact]
    public void AHandheldHubScreen_FitsTheStripTheLegendAndMostOfItsOldFrame()
    {
        int gutter = UiChromeRules.Gutter(HandheldWidth);
        int top = UiChromeRules.TopInset(gutter, hub: true, HandheldHeight);
        int bottom = UiChromeRules.BottomInset(gutter, legend: true);

        Assert.True(top >= UiChromeRules.HubHeight(HandheldHeight) + (UiChromeRules.ChromeMargin * 2));

        // The frame keeps at least nine tenths of the height it had before either strip existed.
        float before = HandheldHeight - (gutter * 2);
        float after = HandheldHeight - top - bottom;
        Assert.True(after >= before * 0.9f, $"The hub frame fell from {before} to {after} px.");
    }

    [Fact]
    public void FiveTabsAndBothEndGlyphs_FitAHandheldWidth()
    {
        // HubStrip: five 112 px tabs, a half-tab slot at each end, SpaceSm between the three parts.
        const int tab = 112;
        int strip = (tab * UiChromeRules.HubTabCount) + tab + (UiTheme.SpaceSm * 2);
        Assert.True(strip <= HandheldWidth - (UiTheme.SpaceLg * 2));
    }

    [Theory]
    [InlineData(HubTab.Character, 1, HubTab.Spellbook)]
    [InlineData(HubTab.Journal, -1, HubTab.Spellbook)]
    [InlineData(HubTab.Bestiary, 1, HubTab.Character)]
    [InlineData(HubTab.Character, -1, HubTab.Bestiary)]
    [InlineData(HubTab.Map, 5, HubTab.Map)]
    public void HubStep_WalksTheFixedOrderAndWraps(HubTab from, int delta, HubTab expected) =>
        Assert.Equal(expected, UiChromeRules.HubStep(from, delta));

    [Fact]
    public void TheHubOrder_IsTheOneTheStripDraws()
    {
        Assert.Equal(UiChromeRules.HubTabCount, System.Enum.GetValues<HubTab>().Length);
        Assert.Equal(0, (int)HubTab.Character);
        Assert.Equal(1, (int)HubTab.Spellbook);
        Assert.Equal(2, (int)HubTab.Journal);
        Assert.Equal(3, (int)HubTab.Map);
        Assert.Equal(4, (int)HubTab.Bestiary);
    }
}
