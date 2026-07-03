using Embervale.Core;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the pure gamepad-button → display-glyph map behind the 30.5J device-aware prompts.
/// The InputMap resolution runs in-engine; the label table is pure and load-bearing (a wrong
/// glyph teaches the player the wrong button), so it is pinned here.
/// </summary>
public class InputLabelTests
{
    [Theory]
    [InlineData(JoyButton.A, "A")]
    [InlineData(JoyButton.B, "B")]
    [InlineData(JoyButton.X, "X")]
    [InlineData(JoyButton.Y, "Y")]
    [InlineData(JoyButton.Start, "Start")]
    [InlineData(JoyButton.Back, "Select")]
    [InlineData(JoyButton.LeftShoulder, "LB")]
    [InlineData(JoyButton.RightShoulder, "RB")]
    [InlineData(JoyButton.DpadUp, "D-Up")]
    public void ButtonLabel_MapsCommonButtons(JoyButton button, string expected) =>
        Assert.Equal(expected, GameInput.ButtonLabel(button));

    [Fact]
    public void ButtonLabel_UnknownFallsBack()
    {
        Assert.Equal("?", GameInput.ButtonLabel(JoyButton.Invalid));
        Assert.Equal("?", GameInput.ButtonLabel(JoyButton.Paddle3));
    }
}
