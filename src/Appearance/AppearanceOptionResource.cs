using Godot;

namespace Embervale.Appearance;

/// <summary>
/// One pickable look in one <see cref="AppearanceSlot"/>, as authored data: a skin tone, a hair colour,
/// an eye colour, an ember glow colour or a build. Indexed by <see cref="AppearanceDatabase"/>; each race
/// lists the ids it offers in <c>RaceResource.AppearanceOptionIds</c>. New option = a <c>.tres</c> in
/// <c>data/appearance/</c> plus its Loc row, no code change.
/// </summary>
[GlobalClass]
public partial class AppearanceOptionResource : Resource
{
    /// <summary>Stable unique id, e.g. "appearance.skin.tan". The database key and the value saved in the profile.</summary>
    [Export] public string Id { get; set; } = "appearance.unknown";

    /// <summary>Loc key for the display name.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    [Export] public AppearanceSlot Slot { get; set; } = AppearanceSlot.Skin;

    /// <summary>The colour the region average takes (Skin, Hair, Eyes) or the glow colour (Ember). Also the
    /// creator swatch. Unused by Build.</summary>
    [Export] public Color Tint { get; set; } = Colors.White;

    /// <summary>Build only: X/Z scale of the body (1 = unchanged).</summary>
    [Export] public float BuildScale { get; set; } = 1f;

    /// <summary>True for the one option per slot that reproduces the unmodified model; used when a profile
    /// names nothing (old saves, headless profiles) and always offered by the creator.</summary>
    [Export] public bool IsDefault { get; set; }
}
