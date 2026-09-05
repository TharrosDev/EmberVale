using Godot;

namespace Embervale.Player;

/// <summary>What the camera is currently for. The profile is derived from gameplay state, never set
/// directly, so two systems can never disagree about which one is active.</summary>
// APPEND ONLY: ordinals may reach a .tres — never reorder/insert/remove (EnumStabilityTests).
public enum CameraContext
{
    /// <summary>Walking the world. The player's own distance and shoulder settings, untouched.</summary>
    Exploration,

    /// <summary>Sprinting. Pulls back and widens a little, which is most of what makes speed read as
    /// speed rather than as the world scrolling faster.</summary>
    Sprint,

    /// <summary>In a fight but not locked on. Slightly closer and tighter so the swing fills more of
    /// the frame.</summary>
    Combat,

    /// <summary>Locked on. Closer still, and higher, so both the player and their target fit.</summary>
    TargetLock,

    /// <summary>Aiming a bow or a spell. Close over the shoulder and narrow, which is what makes a
    /// ranged shot feel aimed rather than pointed.</summary>
    Aim,
}

/// <summary>
/// The camera's shape for one context: how far, how high, how wide, and how fast it gets there.
///
/// <para><b>What this replaced.</b> One global FOV setting and one distance slider, applied
/// identically whatever the player was doing. Sprinting looked like walking, a locked-on duel was
/// framed like an empty field, and aiming a spell across a valley used the same field of view as
/// standing in a corridor.</para>
///
/// <para>⚠️ <b>Every field is a MULTIPLIER or an OFFSET on the player's own settings, never an
/// absolute.</b> The distance slider and the FOV slider are player accessibility settings; a profile
/// that replaced them outright would quietly override an accessibility choice every time the player
/// drew a bow. A profile leans the camera; the player still decides where it rests.</para>
/// </summary>
public readonly record struct CameraProfile(
    float DistanceScale,
    float RiseOffset,
    float FovOffset,
    float ShoulderScale,
    float BlendSeconds)
{
    /// <summary>The neutral profile: exactly the player's settings, nothing added.</summary>
    public static readonly CameraProfile Neutral = new(1f, 0f, 0f, 1f, 0.25f);

    /// <summary>The shape for a context. A table rather than a resource because these are five
    /// tuning values a designer changes by editing this line, and a .tres per context would be five
    /// files nobody can diff meaningfully.</summary>
    public static CameraProfile For(CameraContext context) => context switch
    {
        // Back and wide: the classic speed cue, kept mild because a big FOV punch is nauseating at
        // the frequency a player sprints in an open world.
        CameraContext.Sprint => new(1.12f, 0.05f, 6f, 1f, 0.35f),

        // In closer so the weapon arc fills the frame.
        CameraContext.Combat => new(0.92f, 0.05f, -2f, 1f, 0.3f),

        // Closer and higher again: a duel wants both bodies in frame, and the extra height is what
        // keeps the target visible past the player's own shoulder.
        CameraContext.TargetLock => new(0.86f, 0.18f, -4f, 1.15f, 0.28f),

        // Tight over the shoulder and narrow, which reads as looking down a shaft.
        CameraContext.Aim => new(0.7f, 0.02f, -12f, 1.3f, 0.18f),

        _ => Neutral,
    };

    /// <summary>Eases one profile toward another. Every field blends, so a context change is a lean
    /// rather than a cut — a camera that snapped between these would be worse than not having
    /// them.</summary>
    public static CameraProfile Blend(CameraProfile from, CameraProfile to, float t) => new(
        Mathf.Lerp(from.DistanceScale, to.DistanceScale, t),
        Mathf.Lerp(from.RiseOffset, to.RiseOffset, t),
        Mathf.Lerp(from.FovOffset, to.FovOffset, t),
        Mathf.Lerp(from.ShoulderScale, to.ShoulderScale, t),
        Mathf.Lerp(from.BlendSeconds, to.BlendSeconds, t));

    /// <summary>
    /// Which context gameplay is in. Ordered by priority, most specific first — aiming beats a lock,
    /// a lock beats generic combat, and combat beats sprinting, because that is the order in which
    /// each one matters to what the player is trying to see.
    /// </summary>
    public static CameraContext Resolve(bool aiming, bool lockedOn, bool inCombat, bool sprinting) =>
        aiming ? CameraContext.Aim
        : lockedOn ? CameraContext.TargetLock
        : inCombat ? CameraContext.Combat
        : sprinting ? CameraContext.Sprint
        : CameraContext.Exploration;
}
