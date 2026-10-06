using System;
using System.IO;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the gamepad glyph table (<c>UiGlyph</c>): which family a pad belongs to, and which shape and
/// letter each input is shown as. A wrong glyph teaches the player the wrong button.
/// </summary>
public class UiGlyphRulesTests
{
    [Theory]
    [InlineData("Xbox Series X Controller", PadFamily.Xbox)]
    [InlineData("XInput Gamepad", PadFamily.Xbox)]
    [InlineData("Steam Virtual Gamepad", PadFamily.Xbox)]
    [InlineData("PS5 Controller", PadFamily.PlayStation)]
    [InlineData("Sony DualSense Wireless Controller", PadFamily.PlayStation)]
    [InlineData("PS4 Controller", PadFamily.PlayStation)]
    [InlineData("Nintendo Switch Pro Controller", PadFamily.Generic)]
    [InlineData("", PadFamily.Generic)]
    [InlineData(null, PadFamily.Generic)]
    public void FamilyFor_ReadsThePadNameTheEngineReports(string? name, PadFamily expected) =>
        Assert.Equal(expected, UiGlyphRules.FamilyFor(name));

    [Theory]
    [InlineData(JoyButton.A, "A")]
    [InlineData(JoyButton.B, "B")]
    [InlineData(JoyButton.X, "X")]
    [InlineData(JoyButton.Y, "Y")]
    public void Xbox_FaceButtonsAreLetteredCircles(JoyButton button, string letter)
    {
        PadGlyph glyph = UiGlyphRules.ForButton(button, PadFamily.Xbox);
        Assert.Equal("face", glyph.Shape);
        Assert.Equal(letter, glyph.Label);
    }

    [Theory]
    [InlineData(JoyButton.A, "ps_cross", "Cross")]
    [InlineData(JoyButton.B, "ps_circle", "Circle")]
    [InlineData(JoyButton.X, "ps_square", "Square")]
    [InlineData(JoyButton.Y, "ps_triangle", "Triangle")]
    public void PlayStation_FaceButtonsAreSymbolsWithNoLetter(JoyButton button, string shape, string name)
    {
        PadGlyph glyph = UiGlyphRules.ForButton(button, PadFamily.PlayStation);
        Assert.Equal(shape, glyph.Shape);
        Assert.Equal(string.Empty, glyph.Label);
        Assert.Equal(name, glyph.Name);
    }

    [Theory]
    [InlineData(JoyButton.A, "face_south")]
    [InlineData(JoyButton.B, "face_east")]
    [InlineData(JoyButton.X, "face_west")]
    [InlineData(JoyButton.Y, "face_north")]
    public void AnUnknownPad_ShowsFaceButtonsByPositionNotByLetter(JoyButton button, string shape)
    {
        PadGlyph glyph = UiGlyphRules.ForButton(button, PadFamily.Generic);
        Assert.Equal(shape, glyph.Shape);
        Assert.Equal(string.Empty, glyph.Label);
    }

    [Fact]
    public void ShouldersAndTriggers_TakeTheNamesOfTheirFamily()
    {
        Assert.Equal(new PadGlyph("bumper", "LB", "LB"), UiGlyphRules.ForButton(JoyButton.LeftShoulder, PadFamily.Xbox));
        Assert.Equal(new PadGlyph("bumper", "R1", "R1"), UiGlyphRules.ForButton(JoyButton.RightShoulder, PadFamily.PlayStation));
        Assert.Equal(new PadGlyph("trigger", "LT", "LT"), UiGlyphRules.ForAxis(JoyAxis.TriggerLeft, PadFamily.Xbox));
        Assert.Equal(new PadGlyph("trigger", "R2", "R2"), UiGlyphRules.ForAxis(JoyAxis.TriggerRight, PadFamily.PlayStation));
        Assert.Equal("stick", UiGlyphRules.ForAxis(JoyAxis.RightX, PadFamily.Generic).Shape);
    }

    [Fact]
    public void EveryGlyph_HasANameToFallBackOnAndAShapeThatIsAFile()
    {
        string folder = Path.Combine(RepositoryRoot(), "assets", "ui", "glyphs");
        foreach (PadFamily family in Enum.GetValues<PadFamily>())
        {
            foreach (JoyButton button in Enum.GetValues<JoyButton>())
            {
                Check(folder, UiGlyphRules.ForButton(button, family), $"{family} {button}");
            }

            foreach (JoyAxis axis in Enum.GetValues<JoyAxis>())
            {
                Check(folder, UiGlyphRules.ForAxis(axis, family), $"{family} {axis}");
            }
        }
    }

    private static void Check(string folder, PadGlyph glyph, string what)
    {
        Assert.False(string.IsNullOrEmpty(glyph.Name), $"{what} has no name.");
        Assert.True(File.Exists(Path.Combine(folder, glyph.Shape + ".svg")),
            $"{what}: assets/ui/glyphs/{glyph.Shape}.svg is missing.");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("project.godot not found");
    }
}
