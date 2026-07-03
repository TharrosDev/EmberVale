using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The 30.5K legibility audit, kept executable: every token pair the UI renders text with
/// must hold WCAG AA (≥4.5:1), and the meaningful non-text pairs (bar fills on the trough)
/// ≥3:1. A palette retune that regresses readability fails here instead of shipping.
/// </summary>
public class UiContrastTests
{
    // The button-face surfaces from UiTheme.ApplyInteractiveStyle/ButtonStyle.
    private static readonly Color ButtonNormal = new(0.16f, 0.15f, 0.13f, 0.95f);
    private static readonly Color ButtonHover = new(0.23f, 0.21f, 0.18f, 0.98f);

    public static readonly TheoryData<string, Color, Color> TextPairs = new()
    {
        { "Text on PanelBg", UiTheme.Text, UiTheme.PanelBg },
        { "Dim on PanelBg", UiTheme.Dim, UiTheme.PanelBg },
        { "Accent on PanelBg", UiTheme.Accent, UiTheme.PanelBg },
        { "AccentHot on PanelBg", UiTheme.AccentHot, UiTheme.PanelBg },
        { "Good on PanelBg", UiTheme.Good, UiTheme.PanelBg },
        { "Bad on PanelBg", UiTheme.Bad, UiTheme.PanelBg },
        { "CorruptionText on PanelBg", UiTheme.CorruptionText, UiTheme.PanelBg },
        { "Text on Trough (keycaps)", UiTheme.Text, UiTheme.Trough },
        { "Dim on Trough", UiTheme.Dim, UiTheme.Trough },
        { "Text on buttons", UiTheme.Text, ButtonNormal },
        { "Dim on buttons (inactive tabs)", UiTheme.Dim, ButtonNormal },
        { "Accent on hovered buttons", UiTheme.Accent, ButtonHover },
    };

    public static readonly TheoryData<string, Color, Color> FillPairs = new()
    {
        { "Health fill on Trough", UiTheme.Health, UiTheme.Trough },
        { "Stamina fill on Trough", UiTheme.Stamina, UiTheme.Trough },
        { "Mana fill on Trough", UiTheme.Mana, UiTheme.Trough },
        { "Accent fill on Trough (XP)", UiTheme.Accent, UiTheme.Trough },

        // Deliberately absent: the deep Corruption fill (~2.5:1) — the gauge is a redundant
        // graphic (its tier/value always renders as CorruptionText beside it), and the art
        // bible's violet is not to be brightened (UI_STYLE §2). Corruption *text* is audited.
    };

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void TextPairs_HoldWcagAa(string pair, Color fg, Color bg)
    {
        double ratio = UiContrast.Ratio(fg, bg);
        Assert.True(ratio >= 4.5, $"{pair} = {ratio:0.00}:1, below the 4.5:1 AA floor");
    }

    [Theory]
    [MemberData(nameof(FillPairs))]
    public void FillPairs_HoldUiComponentFloor(string pair, Color fg, Color bg)
    {
        double ratio = UiContrast.Ratio(fg, bg);
        Assert.True(ratio >= 3.0, $"{pair} = {ratio:0.00}:1, below the 3:1 non-text floor");
    }

    [Fact]
    public void Ratio_IsOrderIndependentAndBounded()
    {
        Assert.Equal(UiContrast.Ratio(Colors.White, Colors.Black), UiContrast.Ratio(Colors.Black, Colors.White), 5);
        Assert.Equal(21.0, UiContrast.Ratio(Colors.White, Colors.Black), 1);
        Assert.Equal(1.0, UiContrast.Ratio(Colors.White, Colors.White), 5);
    }
}
