namespace Embervale.Enemies;

/// <summary>
/// The pure positioning brain for a pack (Phase 34A): where each member should approach a shared
/// target from, so a warband surrounds instead of forming a conga line down one corridor.
///
/// Members fan alternately left and right of the straight approach line — slot 0 charges dead on,
/// slot 1 swings right, slot 2 left, slot 3 further right, and so on — capped so nobody tries to
/// attack from directly behind through a wall. Godot-free, so the fan-out is unit-testable apart
/// from the navmesh and locomotion it eventually drives.
/// </summary>
public static class PackFlank
{
    /// <summary>Widest angle any member will swing to; beyond this they'd path the long way round.</summary>
    public const float MaxAngleDegrees = 120f;

    /// <summary>
    /// The signed angle (degrees, positive = the target's right) that pack member <paramref name="slot"/>
    /// should offset its approach by. Slot 0 — and any profile with no spread — goes straight in.
    /// </summary>
    public static float ApproachAngle(int slot, float spreadDegrees)
    {
        if (spreadDegrees <= 0f || slot <= 0)
        {
            return 0f;
        }

        int ring = (slot + 1) / 2;                 // 1,1,2,2,3,3…
        float sign = slot % 2 == 1 ? 1f : -1f;     // right, left, right, left…
        float angle = ring * spreadDegrees;
        return (angle > MaxAngleDegrees ? MaxAngleDegrees : angle) * sign;
    }
}
