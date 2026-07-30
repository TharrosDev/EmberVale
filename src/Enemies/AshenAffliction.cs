using Embervale.Localization;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Turns an already-spawned enemy into its Ashen variant (Phase 34F): the same creature, taken by
/// Morthul's corruption. Stronger, charred, ember-lit, and worth more — but the *same archetype*,
/// so a corrupted wolf is still a wolf rather than a second `.tres` that drifts out of sync with
/// the first every time either is tuned.
///
/// Mirrors <c>WorldEventDirector.ApplyHealthMultiplier</c>, the codebase's only other spawn-time
/// enemy variation: build through the registry, then add named stat modifiers after the entity is
/// in the tree.
///
/// The look deliberately reuses the player's corruption language rather than inventing one — the
/// ash and ember colours below are <c>CorruptionAppearanceController.LookFor</c>'s, and ART_STYLE
/// §2.2's rule is "Materials, not new meshes".
/// </summary>
public static class AshenAffliction
{
    /// <summary>Modifier source tag, so the affliction's stat changes are identifiable and
    /// removable as a set (mirrors "world_event.champion").</summary>
    public const string ModifierSource = "ashen";

    /// <summary>Loc key for the nameplate, e.g. "Ashen Wolf".</summary>
    private const string NameKey = "enemy.ashen_prefix";

    /// <summary>Fractional bonuses — PercentMult, so they read as "+60% health" over whatever the
    /// base archetype authored rather than a flat number that would swamp a wisp and tickle a golem.</summary>
    public const float HealthBonus = 0.6f;
    public const float PowerBonus = 0.25f;
    private const float XpBonus = 0.5f;

    /// <summary>Ash the body is lerped toward, and the ember light in its cracks — both lifted from
    /// <c>CorruptionAppearanceController</c>'s Embers tier so a corrupted enemy and a corrupted
    /// player read as the same affliction.</summary>
    private static readonly Color Ash = new(0.20f, 0.17f, 0.17f);
    private static readonly Color Ember = new(0.82f, 0.34f, 0.10f);
    private const float AshBlend = 0.65f;
    private const float EmberEnergy = 0.55f;

    /// <summary>XP an Ashen kill is worth. Pure so the reward curve is unit-testable.</summary>
    public static int AfflictedXp(int baseXp)
    {
        if (baseXp <= 0)
        {
            return baseXp;
        }

        return Mathf.RoundToInt(baseXp * (1f + XpBonus));
    }

    /// <summary>
    /// Afflicts a spawned enemy. **Call after the entity is in the tree** — stat modifiers need
    /// <c>StatsComponent.OnInitialize</c> to have built the base values off the archetype's
    /// <c>AttributeSet</c> first, which is why the world-event precedent also sits post-add.
    ///
    /// Deliberately does *not* touch <c>TemplateId</c>: quest kill objectives match on it, so
    /// renaming the template would silently break every quest that targets the base creature.
    /// </summary>
    public static void Afflict(EnemyEntity enemy)
    {
        if (enemy.GetComponent<StatsComponent>() is { } stats)
        {
            stats.GetStat(StatType.Health).AddModifier(
                new StatModifier(HealthBonus, ModifierType.PercentMult, ModifierSource));
            stats.GetStat(StatType.PhysicalPower).AddModifier(
                new StatModifier(PowerBonus, ModifierType.PercentMult, ModifierSource));
            // Without this the enemy keeps its old current health against the new max.
            stats.RefillResources();
        }

        if (enemy.GetComponent<ExperienceComponent>() is { } xp)
        {
            xp.XpValue = AfflictedXp(xp.XpValue);
        }

        enemy.DisplayName = Loc.TF(NameKey, enemy.DisplayName);
        Char(enemy);
    }

    /// <summary>Chars every mesh surface under the enemy. Works for both visual branches the factory
    /// produces — <c>GetActiveMaterial</c> returns the placeholder capsule's <c>MaterialOverride</c>
    /// or an imported model's surface material alike.
    ///
    /// The <c>Duplicate</c> is load-bearing: without it the tint writes through to the shared
    /// imported resource and every *uncorrupted* instance of that model turns ashen too. Same reason
    /// <c>CorruptionAppearanceController.CollectSurfaces</c> duplicates.</summary>
    private static void Char(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is { } res)
        {
            for (int i = 0; i < res.GetSurfaceCount(); i++)
            {
                StandardMaterial3D owned = mesh.GetActiveMaterial(i) is StandardMaterial3D m
                    ? (StandardMaterial3D)m.Duplicate()
                    : new StandardMaterial3D();

                owned.AlbedoColor = owned.AlbedoColor.Lerp(Ash, AshBlend);
                owned.EmissionEnabled = true;
                owned.Emission = Ember;
                owned.EmissionEnergyMultiplier = EmberEnergy;
                mesh.SetSurfaceOverrideMaterial(i, owned);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            Char(child);
        }
    }
}
