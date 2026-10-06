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
}
