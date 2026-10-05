namespace Embervale.Appearance;

/// <summary>
/// The five independent looks a character picks. A profile holds one option id per slot.
/// Append-only: a <c>.tres</c> stores the index.
/// </summary>
public enum AppearanceSlot
{
    Skin = 0,
    Hair = 1,
    Eyes = 2,
    /// <summary>Colour of the glow that corruption brings up in the skin and eyes.</summary>
    Ember = 3,
    /// <summary>Body width (X/Z scale only); height never changes.</summary>
    Build = 4,
}
