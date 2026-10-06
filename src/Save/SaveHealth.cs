namespace Embervale.Save;

/// <summary>
/// What <see cref="SaveManager.InspectSlot"/> found when it looked at a slot. Runtime only: it is a
/// finding about a file and is never written into one.
/// </summary>
public enum SaveHealth
{
    /// <summary>Readable and loadable by this build (an older format it can migrate included).</summary>
    Ok,

    /// <summary>Present but unreadable: not JSON, not an object, or missing its version or objects.</summary>
    Corrupt,

    /// <summary>Written by a later save format than this build understands.</summary>
    Newer,

    /// <summary>The slot holds no save.</summary>
    Missing,
}
