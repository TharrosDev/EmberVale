namespace Embervale.Movement;

/// <summary>
/// Whether a step attempt is worth keeping (Phase 39C) — pure, Godot-free, unit-tested. The climbing
/// itself is three engine moves in <see cref="LocomotionComponent"/>; this is the accept/revert rule.
///
/// <b>Why this exists at all.</b> Godot's <c>CharacterBody3D</c> has no step offset — <c>MoveAndSlide</c>
/// climbs nothing taller than <c>floor_snap_length</c> (0.1 m, and this project never raised it). Every
/// cell in the game is authored around that absence: <c>embermarket.tscn</c>'s header recorded a 0.3 m
/// plaza dais being <em>deleted</em> over it, and every ground slab in the realm is a 5–6 cm decorative
/// skin with no collider. So this is not a traversal flourish — it is what lets the world have raised
/// ground at all, which Phase 44 needs before it can block out five realms.
///
/// ⚠️ <b><see cref="MaxHeight"/> is 0.5 m because the navmesh already says 0.5.</b> Every cell authors
/// <c>agent_max_climb = 0.5</c>, so NPCs have always been pathed over ground the player could not
/// follow them onto — the mismatch was live, not theoretical. A <c>--validate</c> rule now pins the two
/// together, because the day someone raises a cell's <c>agent_max_climb</c> the bug returns silently.
///
/// ⚠️ <b>THIS TYPE USED TO DO THE ARITHMETIC ITSELF AND THE ARITHMETIC WAS WRONG.</b> The first version
/// computed the lift as <c>maxHeight - dropDistance</c> from a downward probe. Against a real capsule
/// that under-reports every time: the capsule's rounded bottom catches the dais <em>corner</em> rather
/// than its top face, so a 0.3 m step measured as 0.156 m and the body lifted a fraction of what it
/// needed, every frame, forever. Six unit tests agreed with it. Only walking a body at the actual
/// geometry found it — which is why the climb is now simulated by the engine and this decides only
/// whether the result is acceptable.
/// </summary>
public static class StepUp
{
    /// <summary>The tallest step a body will climb, in metres. ⚠️ Matched to the navmesh's
    /// <c>agent_max_climb</c> by a content rule — change one and <c>--validate</c> fails until the
    /// other follows.</summary>
    public const float MaxHeight = 0.5f;

    /// <summary>Below this a step is not worth taking — it is inside what <c>floor_snap_length</c>
    /// already climbs, so committing to it is a visible twitch that buys nothing.</summary>
    public const float MinimumRise = 0.02f;

    /// <summary>
    /// Whether a completed step attempt should be kept rather than rolled back.
    ///
    /// <paramref name="climbed"/> is how much higher the body ended up, <paramref name="advanced"/>
    /// how far it got along its intended direction. <b>Both are required and the second is the one a
    /// naive rule misses:</b> a body that rose without advancing is standing on the face of the kerb
    /// it failed to climb, and keeping that leaves it hovering.
    /// </summary>
    public static bool Accept(float climbed, float advanced, float maxHeight)
    {
        // ⚠️ 37F's invariant at the one door that matters here: accepting means leaving a
        // CharacterBody3D at a new POSITION, and that body keeps its state between frames. One
        // non-finite result puts it somewhere no later frame undoes, and the crash surfaces in
        // whatever moves it next rather than here.
        if (!float.IsFinite(climbed) || !float.IsFinite(advanced) || !float.IsFinite(maxHeight))
        {
            return false;
        }

        return climbed > MinimumRise
            && climbed <= maxHeight + Tolerance
            && advanced > MinimumRise;
    }

    /// <summary>Slack on the height ceiling: the engine resolves the climb, so the result lands within
    /// a collision margin of the requested height rather than exactly on it.</summary>
    private const float Tolerance = 0.01f;
}
