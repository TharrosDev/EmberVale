using Godot;

namespace Embervale.Companions;

/// <summary>
/// Where each companion stands relative to the player (Phase 32A). Followers that all steer at the
/// player's feet shove each other (and the player) around, so every companion gets its own slot: a
/// fan behind the player, alternating left/right and stepping back a rank each pair. Deterministic in
/// the companion's roster index, so a given companion keeps the same shoulder across frames and
/// across a save/load rather than swapping sides.
///
/// Pure (Godot vector maths only, no scene tree) so the layout is unit-testable.
/// </summary>
public static class CompanionFormation
{
    /// <summary>Degrees off the player's back each flank sits at.</summary>
    public const float FlankDegrees = 32f;

    /// <summary>Extra metres each successive rank falls back.</summary>
    public const float RankSpacing = 1.2f;

    /// <summary>
    /// The world-space slot for the companion at <paramref name="index"/> (0-based) behind a player at
    /// <paramref name="playerPosition"/> facing <paramref name="playerForward"/>, at
    /// <paramref name="distance"/> metres. The slot keeps the player's Y — followers walk the ground,
    /// they don't inherit a camera's height.
    /// </summary>
    public static Vector3 Slot(Vector3 playerPosition, Vector3 playerForward, int index, float distance)
    {
        Vector3 forward = new(playerForward.X, 0f, playerForward.Z);
        forward = forward.LengthSquared() < 0.0001f ? Vector3.Forward : forward.Normalized();

        if (index < 0)
        {
            index = 0;
        }

        // Even indices take the left shoulder, odd the right; every pair steps one rank further back.
        float side = index % 2 == 0 ? -1f : 1f;
        int rank = index / 2;
        float angle = Mathf.DegToRad(180f + (FlankDegrees * side));
        Vector3 direction = forward.Rotated(Vector3.Up, angle);

        Vector3 slot = playerPosition + (direction * (distance + (rank * RankSpacing)));
        slot.Y = playerPosition.Y;
        return slot;
    }
}
