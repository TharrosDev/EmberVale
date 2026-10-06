using System;
using Embervale.Core;
using Godot;

namespace Embervale.UI;

/// <summary>Whose button names and shapes a gamepad glyph is drawn in.</summary>
public enum PadFamily
{
    /// <summary>A pad that is neither of the below (a Nintendo layout, an unnamed device). Face
    /// buttons are shown by position, because their letters are not the ones on the pad.</summary>
    Generic,

    /// <summary>A, B, X, Y; LB, RB, LT, RT.</summary>
    Xbox,

    /// <summary>Cross, circle, square, triangle; L1, R1, L2, R2.</summary>
    PlayStation,
}

/// <summary>
/// One gamepad glyph: the hand-drawn <paramref name="Shape"/> (a file under
/// <c>assets/ui/glyphs/</c>, without its extension), the live <paramref name="Label"/> set on top
/// of it (empty when the shape says it all), and a <paramref name="Name"/> that is never empty,
/// for the keycap drawn when the shape cannot be loaded.
/// </summary>
public readonly record struct PadGlyph(string Shape, string Label, string Name);

/// <summary>
/// Which glyph a gamepad input is shown as, per pad family. Pure, so the table is unit-tested: a
/// wrong glyph teaches the player the wrong button.
/// </summary>
public static class UiGlyphRules
{
    /// <summary>The family of the pad the engine reports as <paramref name="joyName"/>.</summary>
    public static PadFamily FamilyFor(string? joyName)
    {
        if (string.IsNullOrWhiteSpace(joyName))
        {
            return PadFamily.Generic;
        }

        if (ContainsAny(joyName, "playstation", "dualsense", "dualshock", "ps3", "ps4", "ps5", "sony"))
        {
            return PadFamily.PlayStation;
        }

        // The Steam Deck and Steam's virtual pad are laid out and lettered as an Xbox pad.
        if (ContainsAny(joyName, "xbox", "x-box", "xinput", "microsoft", "steam"))
        {
            return PadFamily.Xbox;
        }

        return PadFamily.Generic;
    }

    /// <summary>The glyph for a pad button.</summary>
    public static PadGlyph ForButton(JoyButton button, PadFamily family)
    {
        bool sony = family == PadFamily.PlayStation;
        string name = GameInput.ButtonLabel(button);
        switch (button)
        {
            case JoyButton.A:
                return Face("face_south", "ps_cross", "Cross", name, family);
            case JoyButton.B:
                return Face("face_east", "ps_circle", "Circle", name, family);
            case JoyButton.X:
                return Face("face_west", "ps_square", "Square", name, family);
            case JoyButton.Y:
                return Face("face_north", "ps_triangle", "Triangle", name, family);
            case JoyButton.LeftShoulder:
                return Lettered("bumper", sony ? "L1" : name);
            case JoyButton.RightShoulder:
                return Lettered("bumper", sony ? "R1" : name);
            case JoyButton.LeftStick:
                return Lettered("stick", sony ? "L3" : "L", sony ? "L3" : name);
            case JoyButton.RightStick:
                return Lettered("stick", sony ? "R3" : "R", sony ? "R3" : name);
            case JoyButton.Back:
                return new PadGlyph("view", string.Empty, sony ? "Create" : name);
            case JoyButton.Start:
                return new PadGlyph("menu", string.Empty, sony ? "Options" : name);
            case JoyButton.DpadUp:
                return new PadGlyph("dpad_up", string.Empty, name);
            case JoyButton.DpadDown:
                return new PadGlyph("dpad_down", string.Empty, name);
            case JoyButton.DpadLeft:
                return new PadGlyph("dpad_left", string.Empty, name);
            case JoyButton.DpadRight:
                return new PadGlyph("dpad_right", string.Empty, name);
            default:
                return Lettered("face", name);
        }
    }

    /// <summary>The glyph for a pad axis: a trigger, or the stick the axis belongs to.</summary>
    public static PadGlyph ForAxis(JoyAxis axis, PadFamily family)
    {
        bool sony = family == PadFamily.PlayStation;
        string name = GameInput.AxisLabel(axis);
        return axis switch
        {
            JoyAxis.TriggerLeft => Lettered("trigger", sony ? "L2" : name),
            JoyAxis.TriggerRight => Lettered("trigger", sony ? "R2" : name),
            JoyAxis.LeftX or JoyAxis.LeftY => Lettered("stick", "L", name),
            JoyAxis.RightX or JoyAxis.RightY => Lettered("stick", "R", name),
            _ => Lettered("face", name),
        };
    }

    private static PadGlyph Face(string position, string symbol, string symbolName, string letter, PadFamily family) =>
        family switch
        {
            PadFamily.Xbox => new PadGlyph("face", letter, letter),
            PadFamily.PlayStation => new PadGlyph(symbol, string.Empty, symbolName),
            _ => new PadGlyph(position, string.Empty, letter),
        };

    private static PadGlyph Lettered(string shape, string label) => new(shape, label, label);

    private static PadGlyph Lettered(string shape, string label, string name) => new(shape, label, name);

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
