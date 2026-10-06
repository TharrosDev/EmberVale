namespace Embervale.Bootstrap;

/// <summary>What the loading gate is still waiting for. Ordered: each stage is only reached once
/// every earlier one has cleared.</summary>
public enum LoadingWait
{
    /// <summary>Nothing — the gate can open.</summary>
    None,

    /// <summary>The streamer has not made the landing cell resident and active.</summary>
    Streamer,

    /// <summary>The cell is active but the physics server has no collision under the player yet.</summary>
    Collision,

    /// <summary>Collision is there but no capsule-clear spot near the landing passed
    /// <c>SafePlacementService</c> yet.</summary>
    Placement,

    /// <summary>The landing is standable; the gate is giving the rest of the realm a bounded
    /// moment to stream in. Reported only, never a stage <see cref="LoadingGateRules.Pending"/>
    /// returns: it cannot fail and nothing waits on it past its deadline.</summary>
    Realm,
}

/// <summary>
/// Pure timing rules for <see cref="LoadingCoordinator"/>: which stage a load is stuck in, and when
/// to say so.
///
/// ⚠️ <b>A LOAD THAT TIMES OUT SILENTLY FOR THIRTY SECONDS IS UNDIAGNOSABLE.</b> The gate used to
/// say nothing between "Loading…" and either Playing or an abort, so a slow landing and a stuck one
/// looked identical until the cap fired — and the abort message then described the state at second
/// thirty, not what had been holding it since second one. The gate now names the stage it is
/// waiting on at a fixed interval, and records when each stage cleared.
/// </summary>
public static class LoadingGateRules
{
    /// <summary>The first stage that has not cleared.</summary>
    public static LoadingWait Pending(bool streamerReady, bool groundReady, bool placed)
    {
        if (!streamerReady)
        {
            return LoadingWait.Streamer;
        }
        if (!groundReady)
        {
            return LoadingWait.Collision;
        }
        return placed ? LoadingWait.None : LoadingWait.Placement;
    }

    /// <summary>Is a progress line due? True once per <paramref name="interval"/> seconds of
    /// waiting, never at zero (a load that settles quickly says nothing extra).</summary>
    public static bool ReportDue(double elapsed, double lastReportAt, double interval) =>
        interval > 0d && elapsed >= interval && elapsed - lastReportAt >= interval;

    /// <summary>Has the placement stage used up its retries? Placement retries are counted in physics
    /// frames, because each retry is one frame's worth of newly resident collision.</summary>
    public static bool PlacementExhausted(int attempts, int maxAttempts) => attempts >= maxAttempts;
}
