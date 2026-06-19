using Godot;

namespace Embervale.Save;

/// <summary>
/// Implemented by any object whose state must survive save/load. The
/// architectural rule for this project is that no gameplay system is built
/// without a persistence story — implementing this interface is that story.
///
/// State is exchanged as a Godot <see cref="Godot.Collections.Dictionary"/> so
/// it serializes directly to JSON via Godot's <c>Json</c> with no reflection.
/// </summary>
public interface ISaveable
{
    /// <summary>
    /// Stable, unique key identifying this object within a save file. Must be
    /// deterministic enough that the same logical object reuses the same id
    /// across sessions (e.g. derived from a runtime or template id).
    /// </summary>
    string SaveId { get; }

    /// <summary>Serializes this object's persistent state.</summary>
    Godot.Collections.Dictionary Save();

    /// <summary>Restores state previously produced by <see cref="Save"/>.</summary>
    void Load(Godot.Collections.Dictionary data);
}
