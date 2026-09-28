using System;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// How far a bow has been drawn, and what that draw is worth. Pure (no nodes), so the rules that
/// decide a shot are unit-tested apart from the engine.
///
/// <para><b>The draw is the startup of the bow's action.</b> The arrow still leaves on the action's
/// release frame, which is what keeps it in step with the animation and with the enemy archer's
/// telegraph. What the player controls is how much of that startup they kept the string held for:
/// hold the button through it and the shot is fully drawn; let go early, or run out of stamina, and
/// the arrow leaves on the same frame but weak, slow and loose. A tapped, mashed or buffered press
/// is therefore a snap shot, which is the point.</para>
/// </summary>
public static class RangedMath
{
    /// <summary>Seconds of held button that make a full draw. Shorter than the bow's 0.86 s startup on
    /// purpose, so a player who reacts a beat late still gets their full draw.</summary>
    public const float DrawSeconds = 0.72f;

    /// <summary>Stamina drained a second while the string is being drawn. With the action's own
    /// 4-point cost this totals the 9 the bow always cost, spent as effort rather than up front.</summary>
    public const float DrawStaminaPerSecond = 7f;

    /// <summary>What an undrawn (snap) shot is worth, as a fraction of a full draw.</summary>
    public const float SnapDamage = 0.45f;
    public const float SnapPoise = 0.35f;
    public const float SnapSpeed = 0.55f;

    /// <summary>Cone half-angle, in degrees, an undrawn shot scatters into. A full draw is exact.</summary>
    public const float SnapSpreadDegrees = 4f;

    /// <summary>Downward acceleration on an arrow, metres per second squared. The launch direction is
    /// solved so the arrow passes through the aim point, so this shows as an arc rather than a miss.</summary>
    public const float ArrowGravity = 9.8f;

    /// <summary>Fraction of the bow's flight range the ballistic solve treats as reachable.</summary>
    public const float ReachFraction = 0.9f;

    /// <summary>Headshot scale on a single-zone body whose upper slice was struck.</summary>
    public const float HeadshotDamage = 1.5f;
    public const float HeadshotPoise = 1.3f;

    /// <summary>The upper share of a tall body that counts as its head, and the shortest body that
    /// has one at all (a wolf or a rat has no "head zone" a bow should reward by height).</summary>
    public const float HeadFraction = 0.18f;
    public const float MinHeadedHeight = 1.4f;

    /// <summary>The state of one draw. <see cref="Released"/> latches when the button is let go: a
    /// draw cannot be resumed, which is what makes letting go a decision.</summary>
    public readonly record struct Draw(float Held, bool Strained, bool Released);

    /// <summary>0 (undrawn) to 1 (full draw) for <paramref name="held"/> seconds of held button.</summary>
    public static float Charge(float held, float drawSeconds = DrawSeconds) =>
        drawSeconds <= 0f ? 1f : Math.Clamp(held / drawSeconds, 0f, 1f);

    /// <summary>
    /// Advances a draw by one frame. While the button is held the draw grows and costs stamina; with
    /// too little stamina the arms give out (<c>Strained</c>) and the draw stops growing until there
    /// is some. Letting go latches the draw. <paramref name="staminaSpent"/> is what to take off the
    /// actor this frame.
    /// </summary>
    public static Draw Advance(
        Draw draw, float dt, bool holding, float staminaAvailable, out float staminaSpent,
        float drawSeconds = DrawSeconds, float staminaPerSecond = DrawStaminaPerSecond)
    {
        staminaSpent = 0f;
        if (draw.Released)
        {
            return draw;
        }

        if (!holding)
        {
            return draw with { Released = true, Strained = false };
        }

        if (draw.Held >= drawSeconds)
        {
            return draw with { Strained = false };
        }

        float want = Math.Max(0f, staminaPerSecond * dt);
        if (staminaAvailable >= want)
        {
            staminaSpent = want;
            return draw with { Held = draw.Held + dt, Strained = false };
        }

        // Out of breath: spend what is left, but the string does not come any further back.
        staminaSpent = Math.Max(0f, staminaAvailable);
        return draw with { Strained = true };
    }

    /// <summary>Damage share of a full draw: <see cref="SnapDamage"/> undrawn, 1 fully drawn.</summary>
    public static float DamageScale(float charge) => Lerp(SnapDamage, 1f, charge);

    public static float PoiseScale(float charge) => Lerp(SnapPoise, 1f, charge);

    public static float SpeedScale(float charge) => Lerp(SnapSpeed, 1f, charge);

    /// <summary>Cone half-angle in degrees; falls off with the square of the draw, so a nearly full draw
    /// is already almost exact and the loose end is where the scatter lives.</summary>
    public static float SpreadDegrees(float charge)
    {
        float loose = 1f - Math.Clamp(charge, 0f, 1f);
        return SnapSpreadDegrees * loose * loose;
    }

    /// <summary>
    /// Bends <paramref name="direction"/> by a random angle inside the draw's spread cone.
    /// <paramref name="u1"/> and <paramref name="u2"/> are uniform 0..1 draws, passed in so the rule
    /// is deterministic under test.
    /// </summary>
    public static Vector3 Scatter(Vector3 direction, float charge, float u1, float u2)
    {
        float maxAngle = Mathf.DegToRad(SpreadDegrees(charge));
        if (maxAngle <= 0f)
        {
            return direction;
        }

        // Uniform over the cone's disc: sqrt keeps the density even instead of clumping at the axis.
        float angle = maxAngle * MathF.Sqrt(Math.Clamp(u1, 0f, 1f));
        float around = Math.Clamp(u2, 0f, 1f) * MathF.Tau;

        Vector3 axis = direction.Normalized();
        Vector3 helper = Math.Abs(axis.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        Vector3 right = axis.Cross(helper).Normalized();
        Vector3 up = right.Cross(axis).Normalized();
        Vector3 offset = (right * MathF.Cos(around)) + (up * MathF.Sin(around));
        return ((axis * MathF.Cos(angle)) + (offset * MathF.Sin(angle))).Normalized();
    }

    /// <summary>
    /// The unit direction that carries an arrow leaving <paramref name="from"/> at
    /// <paramref name="speed"/> through <paramref name="target"/> under <paramref name="gravity"/>
    /// (the flat, low-arc solution). A target beyond <paramref name="reach"/> is aimed at the furthest
    /// point the arrow can reach along the same line, and one that cannot be reached at all gets a
    /// 45 degree lob, the arrow's longest throw.
    /// </summary>
    public static Vector3 LaunchDirection(
        Vector3 from, Vector3 target, float speed, float gravity, float reach)
    {
        Vector3 to = target - from;
        float distance = to.Length();
        if (distance < 0.001f)
        {
            return Vector3.Forward;
        }

        if (gravity <= 0f || speed <= 0f)
        {
            return to / distance;
        }

        if (distance > reach && reach > 0f)
        {
            to *= reach / distance;
        }

        var flat = new Vector3(to.X, 0f, to.Z);
        float x = flat.Length();
        if (x < 0.01f)
        {
            return to.Normalized();
        }

        float v2 = speed * speed;
        float discriminant = (v2 * v2) - (gravity * ((gravity * x * x) + (2f * to.Y * v2)));
        float tan = discriminant >= 0f
            ? (v2 - MathF.Sqrt(discriminant)) / (gravity * x)
            : 1f;

        Vector3 along = flat / x;
        return new Vector3(along.X, tan, along.Z).Normalized();
    }

    /// <summary>Whether a hit at <paramref name="hitY"/> on a body spanning
    /// <paramref name="bottomY"/> to <paramref name="topY"/> struck its head.</summary>
    public static bool IsHeadHeight(float hitY, float bottomY, float topY)
    {
        float height = topY - bottomY;
        return height >= MinHeadedHeight && hitY >= topY - (height * HeadFraction);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
