using System;
using Embervale.Combat;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>The HDR colours one school's effects are built from: a hot core, the body of the effect
/// and its cooler edge, each with the emission energy it is drawn at.</summary>
public readonly record struct VfxSchoolColors(
    Color Core, Color Mid, Color Edge, float CoreEnergy, float MidEnergy, float EdgeEnergy, float Halo)
{
    /// <summary>The same colours at a fraction of the energy and halo (an enemy's cast).</summary>
    public VfxSchoolColors Scaled(float energy, float halo) => this with
    {
        CoreEnergy = CoreEnergy * energy,
        MidEnergy = MidEnergy * energy,
        EdgeEnergy = EdgeEnergy * energy,
        Halo = Halo * halo,
    };
}

/// <summary>
/// Every spell effect's colours, derived from <see cref="SpellSchools.Color"/> and nothing else: the
/// hue is the school's, and only saturation, value and energy move. The school colour was tuned to
/// be legible as text and quiet in a desaturated world, which is the wrong brief for the heart of a
/// fireball; this is where it is made hot without a second palette appearing. A test pins every
/// colour here to within <see cref="HueToleranceDegrees"/> of its school.
/// </summary>
public static class VfxPalette
{
    /// <summary>How far a derived colour's hue may sit from its school's.</summary>
    public const float HueToleranceDegrees = 12f;

    /// <summary>An enemy's cast is drawn at this fraction of the energy, and its halo at
    /// <see cref="EnemyHaloScale"/>, so the player's own spells are the brightest thing in a fight.</summary>
    public const float EnemyEnergyScale = 0.7f;

    public const float EnemyHaloScale = 0.8f;

    // Emission energies. Glow's threshold is 1.2, so all three bloom where glow is on; on the
    // Performance preset glow is off and the layered core and halo have to read hot unaided.
    private const float CoreEnergy = 6f;
    private const float MidEnergy = 3.5f;
    private const float EdgeEnergy = 1.6f;

    /// <summary>Alpha of the additive halo sprite behind a core.</summary>
    private const float HaloAlpha = 0.35f;

    /// <summary>A school's effect colours at the player's strength.</summary>
    public static VfxSchoolColors For(DamageType school)
    {
        Color tint = SpellSchools.Color(school);
        float hue = tint.H;
        float saturation = tint.S;

        // The core is pulled toward white but never onto it: at zero saturation there is no hue left
        // to keep. The body and edge push the saturation the school colour deliberately holds back.
        Color core = Color.FromHsv(hue, Math.Clamp(saturation * 0.45f, 0.12f, 0.4f), 1f);
        Color mid = Color.FromHsv(hue, Math.Clamp((saturation * 1.5f) + 0.15f, 0.35f, 0.9f), 1f);
        Color edge = Color.FromHsv(hue, Math.Clamp((saturation * 1.8f) + 0.2f, 0.45f, 1f), 0.7f);
        return new VfxSchoolColors(core, mid, edge, CoreEnergy, MidEnergy, EdgeEnergy, HaloAlpha);
    }

    /// <summary>A school's effect colours for a cast by the player or by anyone else.</summary>
    public static VfxSchoolColors For(DamageType school, bool byPlayer) =>
        byPlayer ? For(school) : For(school).Scaled(EnemyEnergyScale, EnemyHaloScale);

    /// <summary>The smallest angle between two colours' hues, in degrees (0..180).</summary>
    public static float HueDistanceDegrees(Color a, Color b)
    {
        float delta = MathF.Abs(a.H - b.H) * 360f;
        return delta > 180f ? 360f - delta : delta;
    }
}
