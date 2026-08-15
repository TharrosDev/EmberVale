using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Magic;

/// <summary>
/// Process-wide registry of <see cref="StatusEffectResource"/>s, scanned once at
/// startup from <c>res://data/status_effects</c> (mirrors <see cref="SpellDatabase"/>).
/// Spells resolve the effect they apply by id through here. New effect = drop a
/// <c>.tres</c>, no code change.
/// </summary>
public static class StatusEffectDatabase
{
    private const string DefaultDirectory = "res://data/status_effects";

    private static readonly Dictionary<string, StatusEffectResource> ById = new();

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<StatusEffectResource>(
            directory, "status effect", effect => effect.Id, ById);
    }

    public static StatusEffectResource? Get(string id)
    {
        return ById.TryGetValue(id, out StatusEffectResource? effect) ? effect : null;
    }

    /// <summary>Every loaded effect. Added for the 39.5B screenshot harness, which needs to apply
    /// whatever effects the game actually has rather than naming ids it hopes exist — a harness that
    /// hard-codes content is one content rename away from silently photographing an empty row.</summary>
    public static IEnumerable<StatusEffectResource> All() => ById.Values;
}
