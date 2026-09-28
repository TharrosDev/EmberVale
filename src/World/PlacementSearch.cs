using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// Where <see cref="SafePlacementService"/> looks when the point it was asked for will not do: rings
/// of candidate offsets around it, nearest first.
///
/// ⚠️ <b>A PLACEMENT THAT FAILS SHOULD FAIL AFTER LOOKING, NOT INSTEAD OF IT.</b> The service used to
/// test the one desired point (and any anchors a caller passed, which none did) and give up. Every
/// caller then fell back to the analytic heightfield — no capsule check, no collision — so the one
/// case the service exists for, a point inside a wall or a crate or a slope too steep to stand on,
/// was exactly the case it handed back unvalidated. A metre or two sideways is almost always fine.
///
/// Rings are spaced <c>ringSpacing</c> apart; each carries as many points as fit at
/// <c>arcSpacing</c> round its circumference (never fewer than six), and each ring is rotated by the
/// golden angle against the last so no direction is favoured all the way out. Pure, allocation-light
/// and deterministic, so the unit suite pins it.
/// </summary>
public static class PlacementSearch
{
    /// <summary>Golden angle, radians. Consecutive rings start this far round from each other.</summary>
    private const float GoldenAngle = 2.39996323f;

    /// <summary>
    /// Horizontal offsets to try, nearest ring first, out to <paramref name="maxRadius"/>. Empty when
    /// the radius or spacing is not positive — the search is then off.
    /// </summary>
    public static IEnumerable<(float X, float Z)> RingOffsets(
        float maxRadius, float ringSpacing = 1f, float arcSpacing = 1f)
    {
        if (!(maxRadius > 0f) || !(ringSpacing > 0f) || !(arcSpacing > 0f))
        {
            yield break;
        }

        int ring = 0;
        for (float radius = ringSpacing; radius <= maxRadius + 0.0001f; radius += ringSpacing)
        {
            ring++;
            int count = Math.Max(6, (int)MathF.Round(MathF.Tau * radius / arcSpacing));
            float start = ring * GoldenAngle;
            for (int i = 0; i < count; i++)
            {
                float angle = start + (MathF.Tau * i / count);
                yield return (MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            }
        }
    }

    /// <summary>How many offsets <see cref="RingOffsets"/> yields — the worst-case physics cost of a
    /// search, which a caller budgeting a frame can check before asking for one.</summary>
    public static int CandidateCount(float maxRadius, float ringSpacing = 1f, float arcSpacing = 1f)
    {
        int count = 0;
        foreach ((float _, float _) in RingOffsets(maxRadius, ringSpacing, arcSpacing))
        {
            count++;
        }
        return count;
    }
}
