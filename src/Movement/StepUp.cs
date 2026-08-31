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
/// ⚠️ <b><see cref="MaxHeight"/> is 0.5 m because that is what the navmesh was authored to say.</b>
/// Every cell asked for <c>agent_max_climb = 0.5</c>, so NPCs were pathed over ground the player could
/// not follow them onto — the mismatch was live, not theoretical. A <c>--validate</c> rule now pins the
/// two together, because the day someone raises a cell's <c>agent_max_climb</c> the bug returns silently.
///
/// ⚠️ <b>WHAT THE BAKE ACTUALLY USES IS SMALLER, AND IT IS SMALLER IN THE SAFE DIRECTION.</b> Recast
/// FLOORS <c>agent_max_climb</c> to whole <c>cell_height</c> voxels (it CEILS height and radius), so an
/// authored 0.5 on a 0.3 grid baked as <b>0.3</b> and nothing said so. The cells now author the floored
/// value, which changes no bake — it makes the file honest. The live rule is therefore: the player
/// climbs 0.5, an NPC is pathed up 0.3 (0.4 in the 0.5/0.4 wilderness cells), and raised ground taller
/// than that is <em>player-only</em> ground. Keep new raised ground at or under the cell's baked climb
/// unless somewhere is meant to be unreachable by NPCs.
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
    /// <summary>The tallest step a body will climb, in metres. ⚠️ A CEILING over every cell's
    /// <c>agent_max_climb</c>, held by a content rule — raise a cell's climb above this and
    /// <c>--validate</c> fails until this follows. It is not an equality: the baked climb is the
    /// authored one floored to a voxel, so it sits at or below this by design.</summary>
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
