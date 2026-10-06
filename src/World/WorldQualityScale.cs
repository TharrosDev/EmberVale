using System;

namespace Embervale.World;

/// <summary>
/// The world-side dials of a graphics quality tier: how far the streamed world draws, how dense its
/// ground cover is, and how far an actor still casts a shadow.
///
/// These are SETTINGS, not session state — they outlive a session the way the chosen quality tier
/// does, which is why this class has no parameterless reset and is not in
/// <c>SessionLifecycleCoordinator.ResetSessionStatics</c>.
///
/// ⚠️ <b>1 / 1 IS THE AUTHORED WORLD.</b> Every consumer multiplies an authored distance by
/// <see cref="DrawDistance"/> and an authored instance count by <see cref="ScatterDensity"/>, and
/// does nothing at all while both are 1, so the default is bit-identical to a build without this
/// class. Main thread only: <see cref="Changed"/> handlers touch scene nodes.
/// </summary>
public static class WorldQualityScale
{
    public const float MinimumDrawDistance = 0.4f;
    public const float MaximumDrawDistance = 1.5f;

    /// <summary>Multiplier on scatter visibility ranges, the biome cull distance and the Far tier.</summary>
    public static float DrawDistance { get; private set; } = 1f;

    /// <summary>Fraction of ground-cover instances drawn, 0..1.</summary>
    public static float ScatterDensity { get; private set; } = 1f;

    /// <summary>The value a tier that never cuts actor shadows authors; also the value before any
    /// tier has been applied, so a world with no sky controller behaves as it always did.</summary>
    public const float UncutActorShadowDistance = 10000f;

    /// <summary>Metres from the player beyond which an actor stops casting a shadow.</summary>
    public static float ActorShadowDistance { get; private set; } = UncutActorShadowDistance;

    /// <summary>Raised after <see cref="Set"/> changed at least one value.</summary>
    public static event Action? Changed;

    public static void Set(float drawDistance, float scatterDensity, float actorShadowDistance)
    {
        drawDistance = Math.Clamp(drawDistance, MinimumDrawDistance, MaximumDrawDistance);
        scatterDensity = Math.Clamp(scatterDensity, 0f, 1f);
        actorShadowDistance = Math.Max(0f, actorShadowDistance);
        if (drawDistance.Equals(DrawDistance) && scatterDensity.Equals(ScatterDensity) &&
            actorShadowDistance.Equals(ActorShadowDistance))
        {
            return;
        }

        DrawDistance = drawDistance;
        ScatterDensity = scatterDensity;
        ActorShadowDistance = actorShadowDistance;
        Changed?.Invoke();
    }
}
