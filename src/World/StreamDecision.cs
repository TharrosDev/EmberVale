namespace Embervale.World;

/// <summary>What the <see cref="RegionStreamer"/> should do with a cell this frame.</summary>
public enum StreamAction
{
    /// <summary>Leave the cell as it is (loaded or not).</summary>
    Keep,

    /// <summary>Instance the cell scene (it is in range and not yet loaded).</summary>
    Load,

    /// <summary>Free the cell scene (the player has left it plus the hysteresis margin).</summary>
    Unload,
}

/// <summary>
/// The pure load/unload rule behind the <see cref="RegionStreamer"/> (Phase 25B), with hysteresis so
/// a player loitering on a boundary doesn't thrash the cell. Godot-free so it is unit-testable:
/// load once inside <paramref name="loadRadius"/>; keep loaded out to
/// <c>loadRadius + unloadMargin</c>; only then unload.
/// </summary>
public static class StreamDecision
{
    public static StreamAction Decide(float distance, float loadRadius, float unloadMargin, bool isLoaded)
    {
        if (isLoaded)
        {
            return distance > loadRadius + unloadMargin ? StreamAction.Unload : StreamAction.Keep;
        }

        return distance <= loadRadius ? StreamAction.Load : StreamAction.Keep;
    }

    /// <summary>True when a single cell is "settled" for the post-transition gate (Phase 25.5B):
    /// either out of load range, or already loaded. The streamer is settled — and the loading screen
    /// can clear — when this holds for every cell and nothing is queued to load.</summary>
    public static bool IsCellSettled(float distance, float loadRadius, bool isLoaded) =>
        distance > loadRadius || isLoaded;
}
