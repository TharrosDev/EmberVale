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

    /// <summary>On a mount. Higher and further back than on foot, because a horse is a bigger thing
    /// to see over and around; it leans further toward a gallop as the mount picks up speed.</summary>
    Mounted,
}

/// <summary>
/// The camera's shape for one context: how far, how high, how wide, how fast it gets there and how
/// far the player may look up and down while in it.
///
/// <para><b>What this replaced.</b> One global FOV setting and one distance slider, applied
/// identically whatever the player was doing. Sprinting looked like walking, a locked-on duel was
/// framed like an empty field, and aiming a spell across a valley used the same field of view as
/// standing in a corridor.</para>
///
/// <para>⚠️ <b>Every framing field is a MULTIPLIER or an OFFSET on the player's own settings, never an
/// absolute.</b> The distance slider and the FOV slider are player accessibility settings; a profile
/// that replaced them outright would quietly override an accessibility choice every time the player
/// drew a bow. A profile leans the camera; the player still decides where it rests.</para>
/// </summary>
public readonly record struct CameraProfile(
    float DistanceScale,
    float RiseOffset,
    float FovOffset,
    float ShoulderScale,
    float BlendSeconds,
    float ReleaseSeconds,
    float PitchLimit)
{
    /// <summary>The neutral profile: exactly the player's settings, nothing added. Its pitch limit is
    /// the widest any context allows.</summary>
    public static readonly CameraProfile Neutral = new(1f, 0f, 0f, 1f, 0.25f, 0.25f, 1.45f);

    /// <summary>The mounted framing at a full gallop, which <see cref="ForSpeed"/> leans toward.</summary>
    private static readonly CameraProfile MountedGallop = new(1.35f, 0.4f, 9f, 1f, 0.5f, 0.5f, 1.25f);

    /// <summary>How far an aim (a drawn bow, a held cast) widens the shoulder offset.</summary>
    public const float AimShoulderScale = 1.12f;

    /// <summary>Fraction of sprint speed a walk sits at (1 over the 1.6 sprint multiplier). A sprint
    /// lean is measured from here, so simply walking does not already half-apply it.</summary>
    public const float SprintLeanFloor = 0.6f;

    /// <summary>The shape for a context. A table rather than a resource because these are a handful
    /// of tuning values a designer changes by editing this line, and a .tres per context would be
    /// files nobody can diff meaningfully.
    ///
    /// <para>The two durations are deliberately different. <c>BlendSeconds</c> is how fast the camera
    /// leans INTO the context, <c>ReleaseSeconds</c> how fast it settles back OUT of it — see
    /// <see cref="TransitionSeconds"/>. Aim snaps in and lets go slowly; combat is sticky, so the
    /// framing does not pump in and out between swings.</para></summary>
    public static CameraProfile For(CameraContext context) => context switch
    {
        // Back and wide: the classic speed cue, kept mild because a big FOV punch is nauseating at
        // the frequency a player sprints in an open world. ForSpeed scales it by actual speed.
        CameraContext.Sprint => new(1.12f, 0.05f, 6f, 1f, 0.35f, 0.5f, 1.4f),

        // In closer so the weapon arc fills the frame. Sticky: the release is slow on purpose.
        CameraContext.Combat => new(0.92f, 0.05f, -2f, 1f, 0.3f, 0.8f, 1.4f),

        // Closer and higher again: a duel wants both bodies in frame, and the extra height is what
        // keeps the target visible past the player's own shoulder. A tighter pitch, because the
        // target is rarely far above or below the horizon.
        CameraContext.TargetLock => new(0.86f, 0.18f, -4f, 1.15f, 0.28f, 0.45f, 1.1f),

        // Tight over the shoulder and narrow, which reads as looking down a shaft. The shoulder
        // widens only a little: at 1.3 an aimed cast swung the camera far enough out that the
        // caster's back left the frame, and third person is meant to sit behind it.
        CameraContext.Aim => new(0.7f, 0.02f, -12f, AimShoulderScale, 0.18f, 0.3f, 1.35f),

        // The resting seat on a mount: higher and further back, the walk-pace framing.
        CameraContext.Mounted => new(1.2f, 0.3f, 3f, 1f, 0.5f, 0.5f, 1.25f),

        _ => Neutral,
    };

    /// <summary>
    /// The profile for a context at a given speed, so the framing follows the speed rather than the
    /// button. A sprint stopped dead against a wall, or a horse slowed to a walk, stops looking like
    /// a chase.
    ///
    /// <para>Sprint leans from neutral toward its full profile between walking pace and full speed;
    /// Mounted leans from the resting seat toward the gallop across the whole speed range. Every
    /// other context ignores speed.</para>
    /// </summary>
    public static CameraProfile ForSpeed(CameraContext context, float speed01)
    {
        float speed = Mathf.Clamp(speed01, 0f, 1f);
        return context switch
        {
            CameraContext.Sprint => Blend(
                Neutral,
                For(CameraContext.Sprint),
                CameraRigMath.Ease((speed - SprintLeanFloor) / (1f - SprintLeanFloor))),
            CameraContext.Mounted => Blend(For(CameraContext.Mounted), MountedGallop, CameraRigMath.Ease(speed)),
            _ => For(context),
        };
    }

    /// <summary>
    /// How long the lean from <paramref name="from"/> to <paramref name="to"/> takes — the asymmetry
    /// that makes the camera feel weighty rather than mechanical. Moving to something more specific
    /// uses the destination's own <c>BlendSeconds</c>; settling back to something less specific uses
    /// the context being left's <c>ReleaseSeconds</c>.
    /// </summary>
    public static float TransitionSeconds(CameraContext from, CameraContext to) =>
        Priority(to) < Priority(from) ? For(from).ReleaseSeconds : For(to).BlendSeconds;

    /// <summary>How specific a context is, in the order <see cref="Resolve"/> ranks them.</summary>
    private static int Priority(CameraContext context) => context switch
    {
        CameraContext.Aim => 5,
        CameraContext.TargetLock => 4,
        CameraContext.Mounted => 3,
        CameraContext.Combat => 2,
        CameraContext.Sprint => 1,
        _ => 0,
    };

    /// <summary>Eases one profile toward another. Every field blends, so a context change is a lean
    /// rather than a cut — a camera that snapped between these would be worse than not having
    /// them.</summary>
    public static CameraProfile Blend(CameraProfile from, CameraProfile to, float t) => new(
        Mathf.Lerp(from.DistanceScale, to.DistanceScale, t),
        Mathf.Lerp(from.RiseOffset, to.RiseOffset, t),
        Mathf.Lerp(from.FovOffset, to.FovOffset, t),
        Mathf.Lerp(from.ShoulderScale, to.ShoulderScale, t),
        Mathf.Lerp(from.BlendSeconds, to.BlendSeconds, t),
        Mathf.Lerp(from.ReleaseSeconds, to.ReleaseSeconds, t),
        Mathf.Lerp(from.PitchLimit, to.PitchLimit, t));

    /// <summary>
    /// Which context gameplay is in. Ordered by priority, most specific first — aiming beats a lock,
    /// a lock beats riding, riding beats generic combat (a swing from the saddle must not pump the
    /// framing), and combat beats sprinting, because that is the order in which each one matters to
    /// what the player is trying to see.
    /// </summary>
    public static CameraContext Resolve(
        bool aiming, bool lockedOn, bool inCombat, bool sprinting, bool mounted = false) =>
        aiming ? CameraContext.Aim
        : lockedOn ? CameraContext.TargetLock
        : mounted ? CameraContext.Mounted
        : inCombat ? CameraContext.Combat
        : sprinting ? CameraContext.Sprint
        : CameraContext.Exploration;
}

/// <summary>
/// What the input router reads off the player each frame that the camera cares about, handed to the
/// rig in one call. The rig turns it into a context, a snapshot for the layers and a profile, so the
/// router stays a translator and the rig never has to know about lock-on, combat or the mount.
/// </summary>
public readonly record struct CameraInputs(
    bool Aiming,
    bool LockedOn,
    bool InCombat,
    bool Mounted,
    bool Sprinting,
    bool Grounded,
    bool Dodging,
    float Speed01)
{
    /// <summary>Nothing happening: standing on the ground, at rest.</summary>
    public static readonly CameraInputs Idle = new(false, false, false, false, false, true, false, 0f);

    /// <summary>The context these inputs mean. A sprint only frames as one on the ground — a
    /// sprinting jump does not keep pulling the camera back and widening it in the air.</summary>
    public CameraContext Context =>
        CameraProfile.Resolve(Aiming, LockedOn, InCombat, Sprinting && Grounded, Mounted);
}
