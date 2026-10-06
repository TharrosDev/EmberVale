using Embervale.Core.Events;

namespace Embervale.Bootstrap;

/// <summary>
/// How far the loading gate has got, for the loading screen's progress line. Published by
/// <see cref="LoadingCoordinator"/> when a load opens (step 0, naming the destination) and each
/// time one of its wait stages clears; never per frame. A loading screen that hears none of these
/// (one shown outside a session) keeps its progress indeterminate.
/// </summary>
/// <param name="RegionId">The region being loaded into.</param>
/// <param name="Step">Stages cleared so far, 0..<see cref="Steps"/>.</param>
public readonly record struct LoadingProgressEvent(string RegionId, int Step) : IGameEvent
{
    /// <summary>The gate's stages: the landing cell, collision under the player, the rest of the
    /// realm, a clear place to stand.</summary>
    public const int Steps = 4;

    /// <summary>
    /// The step a load is on, from what the gate has recorded. Each stage is only counted once
    /// every earlier one has cleared, so the number never runs backwards within one load.
    /// </summary>
    public static int StepFor(bool streamerReady, bool groundReady, bool realmSettled, bool placed)
    {
        if (!streamerReady)
        {
            return 0;
        }

        if (!groundReady)
        {
            return 1;
        }

        if (!realmSettled)
        {
            return 2;
        }

        return placed ? Steps : 3;
    }
}
