using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry mapping a stable enemy template id (e.g. <c>"enemy.goblin"</c>)
/// to the factory that builds that archetype. This is the data-driven seam that lets
/// content (encounters, world events, quest kill targets) reference enemies <em>by id</em>
/// instead of every spawner hard-calling a single factory:
///   * spawners resolve a builder via <see cref="Create"/> and fall back gracefully,
///   * the content validator treats <see cref="IsRegistered"/> as the source of truth for
///     which <c>EnemyTemplateId</c> values are spawnable.
///
/// Adding a new enemy archetype is a new factory plus one <see cref="Register"/> line in the
/// bootstrap — no spawner changes. Until more factories exist, only the goblin is registered.
/// </summary>
public static class EnemyTemplateRegistry
{
    private static readonly Dictionary<string, Func<Vector3, EnemyEntity>> Builders = new();

    /// <summary>The default archetype used when a requested template id is unknown.</summary>
    public const string FallbackTemplateId = GameIds.Enemies.Goblin;

    /// <summary>All registered template ids (the validator's source of truth).</summary>
    public static IReadOnlyCollection<string> TemplateIds => Builders.Keys;

    /// <summary>Registers (or replaces) the builder for a template id.</summary>
    public static void Register(string templateId, Func<Vector3, EnemyEntity> builder)
    {
        if (string.IsNullOrEmpty(templateId) || builder == null)
        {
            Log.Warn("EnemyTemplateRegistry.Register ignored a null/empty template id or builder.");
            return;
        }

        if (Builders.ContainsKey(templateId))
        {
            Log.Warn($"Enemy template '{templateId}' is being replaced in the registry.");
        }

        Builders[templateId] = builder;
    }

    /// <summary>Seeds the built-in archetypes. Called once from the bootstrap.</summary>
    public static void Initialize()
    {
        Builders.Clear();
        Register(FallbackTemplateId, EnemyFactory.Create);
        Register(GameIds.Enemies.IronKing, BossFactory.Create);
        Register(GameIds.Enemies.AshenAcolyte, AshenAcolyteFactory.Create);
        Log.Info($"EnemyTemplateRegistry seeded {Builders.Count} archetype(s).");
    }

    public static bool IsRegistered(string templateId)
    {
        return !string.IsNullOrEmpty(templateId) && Builders.ContainsKey(templateId);
    }

    /// <summary>
    /// Builds an enemy of the given template at a position. An unknown id falls back to the
    /// default archetype with a warning, so a content typo degrades to "wrong enemy" rather
    /// than "nothing spawns" — the validator flags the typo separately.
    /// </summary>
    public static EnemyEntity Create(string templateId, Vector3 position)
    {
        if (Builders.TryGetValue(templateId, out Func<Vector3, EnemyEntity>? builder))
        {
            return builder(position);
        }

        Log.Warn($"Enemy template '{templateId}' is not registered; spawning the fallback '{FallbackTemplateId}'.");
        return Builders.TryGetValue(FallbackTemplateId, out Func<Vector3, EnemyEntity>? fallback)
            ? fallback(position)
            : EnemyFactory.Create(position);
    }
}
