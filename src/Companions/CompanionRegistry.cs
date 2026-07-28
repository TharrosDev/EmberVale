using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Companions;

/// <summary>One recruitable companion archetype: its stable id, its name <c>Loc</c> key, and the
/// builder that assembles the actor.</summary>
public sealed record CompanionArchetype(string Id, string NameKey, Func<Vector3, CompanionEntity> Build);

/// <summary>
/// Process-wide registry mapping a stable companion id (e.g. <c>companion.kael</c>) to the archetype
/// that builds it — the same data-driven seam <see cref="Enemies.EnemyTemplateRegistry"/> gives
/// enemies. The roster, the dev console and (later) dialogue recruit <em>by id</em>; none of them
/// know a factory. Phase 32C swaps the hand-registered archetypes below for authored
/// <c>CompanionResource</c> entries without touching a caller.
/// </summary>
public static class CompanionRegistry
{
    private static readonly Dictionary<string, CompanionArchetype> Archetypes = new();

    /// <summary>All registered companion ids.</summary>
    public static IReadOnlyCollection<string> Ids => Archetypes.Keys;

    /// <summary>Registers (or replaces) an archetype.</summary>
    public static void Register(CompanionArchetype archetype)
    {
        if (archetype == null || string.IsNullOrEmpty(archetype.Id))
        {
            Log.Warn("CompanionRegistry.Register ignored a null/empty archetype.");
            return;
        }

        Archetypes[archetype.Id] = archetype;
    }

    /// <summary>Seeds the built-in companions. Called once from the bootstrap.</summary>
    public static void Initialize()
    {
        Archetypes.Clear();
        Register(new CompanionArchetype(
            GameIds.Companions.Kael,
            "companion.kael.name",
            position => CompanionFactory.CreateWarrior(GameIds.Companions.Kael, "companion.kael.name", position)));
        Log.Info($"CompanionRegistry seeded {Archetypes.Count} companion(s).");
    }

    public static bool IsRegistered(string companionId) =>
        !string.IsNullOrEmpty(companionId) && Archetypes.ContainsKey(companionId);

    public static CompanionArchetype? Get(string companionId) =>
        !string.IsNullOrEmpty(companionId) && Archetypes.TryGetValue(companionId, out CompanionArchetype? a) ? a : null;

    /// <summary>Builds the companion of <paramref name="companionId"/>, or null when it is unknown —
    /// unlike an enemy, a wrong companion is worse than none, so there is no fallback archetype.</summary>
    public static CompanionEntity? Create(string companionId, Vector3 position)
    {
        CompanionArchetype? archetype = Get(companionId);
        if (archetype == null)
        {
            Log.Warn($"Companion '{companionId}' is not registered; nothing spawned.");
            return null;
        }

        return archetype.Build(position);
    }
}
