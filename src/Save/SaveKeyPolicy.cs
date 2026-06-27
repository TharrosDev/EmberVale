namespace Embervale.Save;

/// <summary>
/// The pure policy behind per-actor component persistence (Phase 25.5A). A component's state is
/// keyed by its owner's identity: a stable <see cref="Embervale.Entities.IEntity.PersistentId"/> when
/// the actor is meant to survive save/load, otherwise the volatile runtime id. The crux:
/// <b>transient actors must not be persisted at all</b> — a runtime-keyed entry can never be reclaimed
/// after a world rebuild (the reloaded actor gets a fresh runtime id), so it both fails to restore and
/// lingers as orphaned state. Godot-free so it is unit-testable.
/// </summary>
public static class SaveKeyPolicy
{
    /// <summary>An actor's component state persists only when the actor has a stable
    /// <c>PersistentId</c>. Transient actors (spawned mobs, the dummy) are session-only.</summary>
    public static bool ShouldPersist(string? persistentId) => !string.IsNullOrEmpty(persistentId);

    /// <summary>Builds a component save key: the stable <c>PersistentId</c> when present, else the
    /// volatile runtime id (only reached for diagnostics on a non-persistent actor).</summary>
    public static string Key(string prefix, string? persistentId, ulong runtimeId) =>
        ShouldPersist(persistentId) ? $"{prefix}:{persistentId}" : $"{prefix}:{runtimeId}";

    /// <summary>True when a save key carries a volatile runtime id — its suffix after the last
    /// <c>:</c> is all digits — rather than a stable id, i.e. it would orphan across a reload. The
    /// <c>savecheck</c> dev command flags these; after the 25.5A fix there should be none, because
    /// transient actors no longer register. World-service ids (no <c>:</c>) are never volatile.</summary>
    public static bool IsVolatile(string saveId)
    {
        int colon = saveId.LastIndexOf(':');
        if (colon < 0 || colon == saveId.Length - 1)
        {
            return false;
        }

        for (int i = colon + 1; i < saveId.Length; i++)
        {
            if (!char.IsDigit(saveId[i]))
            {
                return false;
            }
        }

        return true;
    }
}
