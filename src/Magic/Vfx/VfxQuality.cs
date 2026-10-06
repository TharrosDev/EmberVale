namespace Embervale.Magic.Vfx;

/// <summary>
/// The spell-effect tier in force and what it may spend. <see cref="SpellVfxDirector"/> writes it
/// from the settings when a session starts and on every <c>SettingsAppliedEvent</c>; every effect
/// reads it. Holds plain numbers only, so it carries no reference into a session, and
/// <see cref="Reset"/> puts it back to the default tier between sessions.
/// </summary>
public static class VfxQuality
{
    private const VfxTier DefaultTier = VfxTier.Medium;

    /// <summary>The tier effects are drawn at.</summary>
    public static VfxTier Tier { get; private set; } = DefaultTier;

    /// <summary>What that tier may spend.</summary>
    public static VfxBudget Budget { get; private set; } = VfxBudgetRules.For(DefaultTier);

    /// <summary>Reduced Motion: no distortion, bolts hold still, screen flashes are capped.</summary>
    public static bool ReducedMotion { get; private set; }

    /// <summary>Takes the saved settings: <paramref name="spellEffects"/> is <c>Settings.SpellEffects</c>
    /// and <paramref name="renderQuality"/> is <c>Settings.RenderQuality</c>.</summary>
    public static void Apply(int spellEffects, int renderQuality, bool reducedMotion)
    {
        Tier = VfxBudgetRules.Resolve(spellEffects, renderQuality);
        Budget = VfxBudgetRules.For(Tier);
        ReducedMotion = reducedMotion;
    }

    /// <summary>Back to the default tier. Called between sessions.</summary>
    public static void Reset()
    {
        Tier = DefaultTier;
        Budget = VfxBudgetRules.For(DefaultTier);
        ReducedMotion = false;
    }
}
