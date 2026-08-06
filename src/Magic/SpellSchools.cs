using Embervale.Combat;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Presentation helpers for magic schools. A spell's school is simply its
/// <see cref="DamageType"/> (Fire/Frost/Lightning/Arcane/Nature/Necrotic), so the same value drives
/// mitigation in <see cref="CombatMath"/> — via <see cref="CombatMath.ResistanceStat"/>, which maps
/// it to the matching resistance since Phase 34E — and the colour used to tint projectiles and UI
/// here. Centralised so every school looks consistent.
/// </summary>
public static class SpellSchools
{
    /// <summary>
    /// A school's colour — **the single authority**, tinting projectiles, impact flashes, status
    /// particles and cast flares in the world, and spell cards, school headers and status chips in
    /// the UI. <c>UiTheme.SchoolColor</c> delegates here rather than keeping its own copy, for the
    /// same reason <c>ItemRarities.Color</c> owns the rarity ramp: a firebolt that is one orange in
    /// flight and another in the spellbook is not one school, it is two.
    ///
    /// **Retuned in Phase 37.5B** off the stock saturated set it had carried since Phase 12. Two
    /// pressures had to be satisfied at once and the old values met neither:
    /// - **World:** ART_STYLE's saturation discipline. A `1.0, 0.45, 0.12` firebolt was the most
    ///   saturated thing in a deliberately desaturated, dying world.
    /// - **UI:** these render as *text* on panel and card grounds, so every one of them is pinned
    ///   to WCAG AA by `UiContrastTests`. The old Necrotic (`0.55, 0.30, 0.55`) failed at ~2.6:1 —
    ///   a school name the player could not read.
    ///
    /// Arcane is deliberately silver-blue rather than violet: violet is the corruption identity
    /// (UI_STYLE §1), and arcane is the spellbook's own school, so it takes the glyph light.
    /// </summary>
    public static Color Color(DamageType school) => school switch
    {
        DamageType.Physical => new Color(0.76f, 0.74f, 0.70f),   // bone and steel
        DamageType.Fire => new Color(0.93f, 0.55f, 0.28f),       // ember
        DamageType.Frost => new Color(0.58f, 0.78f, 0.88f),      // pale ice
        DamageType.Lightning => new Color(0.86f, 0.84f, 0.52f),  // arc-white gold
        DamageType.Arcane => new Color(0.68f, 0.74f, 0.92f),     // glyph light
        DamageType.Nature => new Color(0.48f, 0.74f, 0.60f),     // verdigris, pushed teal to part from Uncommon
        DamageType.Necrotic => new Color(0.64f, 0.54f, 0.66f),   // bruised ash-violet
        _ => new Color(0.85f, 0.85f, 0.85f),
    };
}
