namespace Embervale.Movement;

/// <summary>
/// The mount's gallop pacing (Phase 39A) — pure, Godot-free, and therefore unit-testable.
/// <see cref="MountComponent"/> owns the state; this owns the arithmetic.
///
/// It is the same shape as <see cref="Stats.StaminaPacing"/> and deliberately not the same rule.
/// The player's stamina is a *spend* pool paced by an idle timer (attack, attack, attack drains it);
/// a horse's gallop is a *sustain*, so this drains while held and refills while not.
///
/// <b>The exhaustion latch is the whole reason this is a type rather than two lines in the
/// component.</b> Without it, a mount that empties its pool refuses to gallop for exactly one
/// frame, regenerates a sliver, gallops again, and empties again — the horse stutters between gaits
/// several times a second and the player cannot tell what the rule is. Once exhausted, gallop stays
/// refused until the pool has climbed back past <see cref="RecoverAt"/>, which is a real rest.
/// </summary>
public static class MountRules
{
    /// <summary>Full gallop pool. A flat number rather than a stat: it belongs to the horse, and the
    /// player's <c>StatType.Stamina</c> is what a rider spends swinging a sword.</summary>
    public const float StaminaMax = 100f;

    /// <summary>Drain per second at a gallop — five seconds of it from full.</summary>
    public const float GallopDrainPerSecond = 20f;

    /// <summary>Refill per second at anything slower, so a full recovery is about seven seconds.</summary>
    public const float RegenPerSecond = 14f;

    /// <summary>How far the pool must climb after bottoming out before a gallop is allowed again.
    /// This is the hysteresis; any value above zero kills the stutter, and a quarter of the pool is
    /// long enough for the refusal to read as "the horse is blown" rather than as an input drop.</summary>
    public const float RecoverAt = 25f;

    /// <summary>Multiplier on the rider's <c>StatType.MoveSpeed</c> while mounted. The sprint
    /// multiplier in <see cref="LocomotionComponent"/> then stacks on top for the gallop, so the
    /// two gaits are 1.7x and (1.7 x 1.6) 2.7x a walking player.
    /// ⚠️ Phase 56 owns the number; this is the first authored value for it.</summary>
    public const float SpeedMultiplier = 1.7f;

    /// <summary>The mount's gallop state for one frame.</summary>
    /// <param name="Stamina">Remaining gallop pool, 0..<see cref="StaminaMax"/>.</param>
    /// <param name="Exhausted">Latched once the pool bottoms out; cleared at <see cref="RecoverAt"/>.</param>
    /// <param name="Galloping">Whether the mount actually galloped this frame — which is not the
    /// same question as whether the player held sprint.</param>
    public readonly record struct GallopState(float Stamina, bool Exhausted, bool Galloping);

    /// <summary>A rested mount.</summary>
    public static GallopState Fresh => new(StaminaMax, false, false);

    /// <summary>
    /// Advances the pool one frame. <paramref name="wantGallop"/> is the player's held sprint; the
    /// returned <see cref="GallopState.Galloping"/> is whether the horse granted it.
    /// </summary>
    public static GallopState Step(GallopState state, bool wantGallop, float delta)
    {
        // A non-finite delta or pool would poison the speed modifier, and that modifier feeds
        // MoveSpeed, which is the exact route 37F's NaN took into a CharacterBody3D's velocity.
        // Cheaper to refuse it here than to explain it in a crash three systems away.
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f)
        {
            delta = 0f;
        }

        float stamina = float.IsNaN(state.Stamina) || float.IsInfinity(state.Stamina)
            ? StaminaMax
            : state.Stamina;
        bool exhausted = state.Exhausted;

        bool allowed = wantGallop && !exhausted && stamina > 0f;
        stamina = allowed
            ? stamina - (GallopDrainPerSecond * delta)
            : stamina + (RegenPerSecond * delta);

        if (stamina <= 0f)
        {
            stamina = 0f;
            exhausted = true;
            allowed = false; // the frame that empties the pool is the last galloping one
        }
        else if (stamina >= StaminaMax)
        {
            stamina = StaminaMax;
        }

        // ⚠️ THE LATCH CLEARS ONLY WHEN THE PLAYER STOPS ASKING, AND THE FIRST DRAFT DID NOT.
        // Recovering on the mark alone still sawtooths: a player who never lets go of sprint gallops
        // for 1.25 s, walks for 1.8 s, gallops again, forever — the same stutter the latch was
        // written to kill, just at a period slow enough to look deliberate. Requiring a frame of not
        // asking makes the rule one a player can learn: let the horse rest and it will run again.
        if (exhausted && !wantGallop && stamina >= RecoverAt)
        {
            exhausted = false;
        }

        return new GallopState(stamina, exhausted, allowed);
    }
}
