using System.Collections.Generic;
using Embervale.Core;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Input glyphs: the picture of the key or button an action is bound to.
///
/// On keyboard and mouse it is the keycap the HUD has always used (<see cref="UiTheme.KeyCap(string)"/>
/// with <see cref="GameInput.PromptLabel"/>). On a gamepad it is a hand-drawn shape from
/// <c>assets/ui/glyphs/</c> (a face button, a bumper, a trigger, a d-pad, a stick, a menu button)
/// with the button's letter set on top as live text, so the letter follows the pad's family
/// (<see cref="PadFamily"/>) and the text-scale and font settings, and nothing is baked into art.
///
/// A glyph is a snapshot: whoever shows one rebuilds it on <c>InputDeviceChangedEvent</c> and
/// <c>InputBindingsChangedEvent</c> (<see cref="UiLegend"/> does this for a footer).
/// </summary>
public static class UiGlyph
{
    /// <summary>Height of a gamepad glyph, in px. A keycap is as tall as its caption makes it.</summary>
    public const float Height = 22f;

    // The letter on a shape stops growing here: the shape does not grow with the text scale, and a
    // letter larger than its button is not more readable.
    private const int MaxLetterSize = 14;

    private const string Folder = "res://assets/ui/glyphs/";

    private static readonly Dictionary<string, Texture2D?> Shapes = new();
    private static bool _warnedMissingShape;

    /// <summary>The family of the first connected pad; <see cref="PadFamily.Generic"/> with none.</summary>
    public static PadFamily Family
    {
        get
        {
            Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
            return pads.Count > 0 ? UiGlyphRules.FamilyFor(Input.GetJoyName(pads[0])) : PadFamily.Generic;
        }
    }

    /// <summary>The glyph for <paramref name="action"/> on the device the player is using now.</summary>
    public static Control For(string action)
    {
        if (InputDevice.GamepadActive && GameInput.TryPadBinding(action, out JoyButton button, out JoyAxis axis))
        {
            PadFamily family = Family;
            PadGlyph glyph = button != JoyButton.Invalid
                ? UiGlyphRules.ForButton(button, family)
                : UiGlyphRules.ForAxis(axis, family);
            return Pad(glyph);
        }

        return UiTheme.KeyCap(GameInput.PromptLabel(action));
    }

    /// <summary>A gamepad glyph by description, for a prompt that names a button rather than an
    /// action. Falls back to a keycap carrying the glyph's name when the shape is not loadable
    /// (an unimported checkout), so a prompt is never blank.</summary>
    public static Control Pad(PadGlyph glyph)
    {
        if (Shape(glyph.Shape) is not { } texture)
        {
            return UiTheme.KeyCap(glyph.Name);
        }

        float width = Height * texture.GetWidth() / Mathf.Max(1f, texture.GetHeight());
        var root = new Control
        {
            CustomMinimumSize = new Vector2(width, Height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        var art = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(art);

        if (glyph.Label.Length > 0)
        {
            Label letter = UiTheme.Caption(glyph.Label, UiTheme.Text);
            letter.AddThemeFontSizeOverride("font_size",
                Mathf.Min(UiTheme.FontSize(UiTheme.CaptionFontSize), MaxLetterSize));
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            letter.VerticalAlignment = VerticalAlignment.Center;
            letter.MouseFilter = Control.MouseFilterEnum.Ignore;
            letter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(letter);
        }

        return root;
    }

    private static Texture2D? Shape(string name)
    {
        if (Shapes.TryGetValue(name, out Texture2D? cached))
        {
            return cached;
        }

        string path = $"{Folder}{name}.svg";
        Texture2D? loaded = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (loaded is null && !_warnedMissingShape)
        {
            // Once, not per shape: an unimported folder misses all of them together.
            _warnedMissingShape = true;
            Core.Diagnostics.Log.Warn($"UiGlyph: could not load '{path}'; gamepad prompts fall back to keycaps.");
        }

        Shapes[name] = loaded;
        return loaded;
    }
}
