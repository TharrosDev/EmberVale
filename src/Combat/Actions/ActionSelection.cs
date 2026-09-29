namespace Embervale.Combat.Actions;

/// <summary>
/// Which action an AI should use, given how far away its target is.
///
/// <para><b>This is the whole of §19's division of labour.</b> The AI decides it wants to attack
/// something at a distance; this decides which of the weapon's authored actions is the right one;
/// the action system executes it and the animation decides when it lands. The AI never guesses at
/// timing, never opens a hitbox, and never knows what a wind-up is.</para>
///
/// <para>Godot-free, so the range bands and the weighting are testable without an engine — and they
/// need to be, because an enemy that picks an out-of-range attack simply swings at air and looks
/// broken rather than throwing an error.</para>
/// </summary>
public static class ActionSelection
{
    /// <summary>
    /// The only thing selection needs to know about an action.
    ///
    /// ⚠️ <b>A plain struct, not the resource.</b> A Godot <c>Resource</c> cannot be constructed
    /// outside the engine, so a helper that took <see cref="ActionDefinitionResource"/> directly
    /// could not be unit-tested at all — and worse, a test that tried took the whole suite down with
    /// it rather than failing on its own. Every pure helper in this repo takes primitives for the
    /// same reason.
    /// </summary>
    public readonly record struct Candidate(float MinRange, float MaxRange, float Weight)
    {
        public static Candidate Of(ActionDefinitionResource action) =>
            new(action.AiMinRange, action.AiMaxRange, action.AiWeight);
    }

    /// <summary>
    /// Picks an action for a target at <paramref name="distance"/> metres, or -1 when nothing in the
    /// chain reaches.
    ///
    /// <para><paramref name="roll"/> is a 0..1 value used to choose among the candidates in
    /// proportion to their <see cref="ActionDefinitionResource.AiWeight"/>. Passed in rather than
    /// rolled here so the choice is deterministic under test — the caller owns the randomness.</para>
    ///
    /// <para>⚠️ <b>An <c>AiWeight</c> of 0 means player-only.</b> A finisher or a riposte a designer
    /// wants off the AI's menu says so with a zero, and this must never pick one — hence the weight
    /// filter rather than a "pick any in range" fallback.</para>
    /// </summary>
    public static int Choose(Candidate[] chain, float distance, float roll)
    {
        if (chain.Length == 0)
        {
            return -1;
        }

        float total = 0f;
        for (int i = 0; i < chain.Length; i++)
        {
            if (InRange(chain[i], distance))
            {
                total += chain[i].Weight;
            }
        }

        if (total <= 0f)
        {
            return -1;
        }

        float target = (roll < 0f ? 0f : roll > 1f ? 1f : roll) * total;
        float running = 0f;
        for (int i = 0; i < chain.Length; i++)
        {
            if (!InRange(chain[i], distance))
            {
                continue;
            }

            running += chain[i].Weight;
            if (target <= running)
            {
                return i;
            }
        }

        // Only reachable when floating point leaves `target` a hair past `running`; the last valid
        // candidate is the honest answer rather than a failure.
        for (int i = chain.Length - 1; i >= 0; i--)
        {
            if (InRange(chain[i], distance))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// As <see cref="Choose(Candidate[], float, float)"/>, but the action picked last time carries only
    /// <paramref name="repeatWeight"/> of its weight, so an enemy varies its attacks instead of
    /// repeating the one the player has just learned to answer. The penalty never removes the only
    /// option: with nothing else in range the repeat is still chosen.
    /// </summary>
    public static int Choose(Candidate[] chain, float distance, float roll, int lastPick, float repeatWeight)
    {
        if (lastPick < 0 || lastPick >= chain.Length || repeatWeight >= 1f)
        {
            return Choose(chain, distance, roll);
        }

        var adjusted = new Candidate[chain.Length];
        for (int i = 0; i < chain.Length; i++)
        {
            adjusted[i] = i == lastPick
                ? chain[i] with { Weight = chain[i].Weight * (repeatWeight < 0f ? 0f : repeatWeight) }
                : chain[i];
        }

        int pick = Choose(adjusted, distance, roll);
        return pick >= 0 ? pick : Choose(chain, distance, roll);
    }

    /// <summary>Whether this action reaches a target at <paramref name="distance"/>.</summary>
    public static bool InRange(Candidate action, float distance) =>
        action.Weight > 0f && distance >= action.MinRange && distance <= action.MaxRange;

    /// <summary>The furthest any action in the chain can reach — what an AI should close to before
    /// it bothers trying. Zero when nothing in the chain is available to AI at all.</summary>
    public static float MaxReach(Candidate[] chain)
    {
        float reach = 0f;
        foreach (Candidate action in chain)
        {
            if (action.Weight > 0f && action.MaxRange > reach)
            {
                reach = action.MaxRange;
            }
        }

        return reach;
    }
}
