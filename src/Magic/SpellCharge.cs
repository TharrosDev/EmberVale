using Godot;

namespace Embervale.Magic;

/// <summary>
/// Pure charged-cast maths (Phase 29.5A). Godot-free apart from <see cref="Mathf"/> so the power curve is
/// unit-testable; <see cref="SpellcastingComponent"/> applies it. A min-charge release deals 1x; a full
/// hold deals <c>maxMultiplier</c>x, scaling linearly in between.
/// </summary>
public static class SpellCharge
{
    /// <summary>Damage/healing multiplier for a charge held <paramref name="elapsed"/> seconds.</summary>
    public static float PowerMultiplier(float elapsed, float chargeTime, float maxMultiplier)
    {
        if (chargeTime <= 0f)
        {
            return maxMultiplier;
        }

        float t = Mathf.Clamp(elapsed / chargeTime, 0f, 1f);
        return Mathf.Lerp(1f, maxMultiplier, t);
    }
}
