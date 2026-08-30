using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// The realm's standing promise that no piece of ground is a dead end: a player who ends up
/// somewhere they cannot walk out of is put back on the last dry, walkable ground they stood on.
///
/// ⚠️ <b>THIS EXISTS INSTEAD OF A SWIMMING SYSTEM AND INSTEAD OF INVISIBLE WALLS.</b> The 2026-08-29
/// overhaul gave the world real basins, precipices and crevasses — Hollowreach's open water is 4.5 m
/// deep behind a 53-degree drop-off, the Ancient Aerie's north face falls thirty metres onto a
/// trench floor, and Glacier Pass has two crevasses cut at a 3:1 falloff that are ten metres wide
/// and eight deep. Every one of those is deliberate and every one of them is a hole. Three answers
/// were available. A swimming system is a whole feature and this pass is not the place for it. A
/// ring of colliders round each is the artefact the overhaul was fought to remove: the land is
/// supposed to say "not that way", and it does. So the land keeps saying it, and this catches the
/// player when something else — a slide, a knockback, a dismount, a dragon, a jump they should not
/// have made — puts them somewhere the land cannot let them out of.
///
/// ⚠️ <b>IT RECOVERS, IT DOES NOT KILL.</b> Drowning or attrition damage would be a second, worse
/// trap: a player who can neither escape nor survive loses progress to a mistake the world never
/// warned them about.
///
/// <b>Two triggers, one response.</b>
/// <list type="number">
/// <item><b>Deep water</b> — over <see cref="WorldWater.DrownDepth"/> for <see cref="WaterGrace"/>
/// seconds. Immediate, because there is no version of standing in four metres of water that the
/// player meant.</item>
/// <item><b>A pit with no way out</b> — well below the surrounding ground, not making progress, and
/// a local walkability search finds no escape. Slower and much more cautious, because a canyon floor
/// with a road along it looks identical to a trap until you check whether it goes anywhere.</item>
/// </list>
///
/// ⚠️ <b>THE PIT CHECK IS LOCAL AND LAZY ON PURPOSE.</b> The obvious design was to precompute the
/// region's trap patches with <see cref="WorldTraversalAnalysis"/> at load and test membership. That
/// is sixteen thousand field samples on every region entry to answer a question that is almost never
/// asked, and it would go stale the moment anything moved. A forty-metre flood fill costs four
/// hundred samples and only runs after the player has already spent <see cref="PitGrace"/> seconds
/// failing to get out of a hole — which, in a normal session, is never.
/// </summary>
public sealed partial class WorldRecovery : Node
{
    /// <summary>Seconds over <see cref="WorldWater.DrownDepth"/> before recovery. Long enough that
    /// wading a deep channel on purpose is not interrupted mid-stride.</summary>
    private const float WaterGrace = 2.5f;

    /// <summary>Seconds stuck in a hole before the escape search runs at all.</summary>
    private const float PitGrace = 9f;

    /// <summary>How far below the surrounding ground counts as "in a hole".</summary>
    private const float PitDepth = 4f;

    /// <summary>Radius the surrounding ground is measured over, and the escape search's reach.</summary>
    private const float PitReach = 22f;

    /// <summary>Moving further than this resets the pit timer — the player is getting somewhere.</summary>
    private const float ProgressDistance = 7f;

    /// <summary>How often the safe point is refreshed while the player is on good ground.</summary>
    private const float SampleSeconds = 0.35f;

    /// <summary>Steepest ground a recovery point may sit on. A safe point on a 40-degree bank puts
    /// the player straight back down the slope they were recovered from.</summary>
    private const float MaxSafeSlope = 0.45f;

    private Vector3? _safePoint;
    private Vector3 _pitAnchor;
    private float _submerged;
    private float _stuck;
    private float _sampleTimer;

    public override void _Process(double delta)
    {
        if (WorldGround.Field is not { } field ||
            ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        Vector3 position = player.GlobalPosition;
        float depth = WorldWater.DepthAt(position.X, position.Z, field);

        if (depth >= WorldWater.DrownDepth)
        {
            _submerged += (float)delta;
            if (_submerged >= WaterGrace)
            {
                _submerged = 0f;
                Recover(player, field, position, $"{depth:0.0} m of water");
            }
            return;
        }

        if (depth <= WorldWater.WadeDepth)
        {
            _submerged = 0f;
            RememberSafePoint(field, position, depth, (float)delta);
        }

        TrackPit(player, field, position, (float)delta);
    }

    private void RememberSafePoint(WorldHeightfield field, Vector3 position, float depth, float delta)
    {
        _sampleTimer += delta;
        if (_sampleTimer < SampleSeconds)
        {
            return;
        }

        _sampleTimer = 0f;
        // ⚠️ Dry AND gentle AND not already in a hole. A safe point taken on the lip of the thing
        // the player is about to slide into is not a recovery, it is a loop.
        if (depth <= 0.05f && field.SlopeAt(position.X, position.Z) <= MaxSafeSlope &&
            !InHole(field, position))
        {
            _safePoint = position;
        }
    }

    private void TrackPit(PlayerCharacter player, WorldHeightfield field, Vector3 position, float delta)
    {
        if (!InHole(field, position))
        {
            _stuck = 0f;
            return;
        }

        if (_stuck <= 0f || position.DistanceTo(_pitAnchor) > ProgressDistance)
        {
            _pitAnchor = position;
            _stuck = 0.0001f;
            return;
        }

        _stuck += delta;
        if (_stuck < PitGrace)
        {
            return;
        }

        _stuck = 0f;
        if (HasEscape(field, position))
        {
            // A canyon with a road along it. Nothing to do; the next check is another grace away.
            return;
        }

        Recover(player, field, position, "a pit with no walkable exit");
    }

    /// <summary>Is the ground here well below everything around it?</summary>
    private static bool InHole(WorldHeightfield field, Vector3 position)
    {
        float here = field.Height(position.X, position.Z);
        float highest = here;
        for (int i = 0; i < 8; i++)
        {
            float angle = Mathf.Tau * i / 8f;
            highest = Mathf.Max(highest, field.Height(
                position.X + (Mathf.Cos(angle) * PitReach),
                position.Z + (Mathf.Sin(angle) * PitReach)));
        }
        return highest - here >= PitDepth;
    }

    /// <summary>
    /// A local walkability flood fill: can the player climb out of here within
    /// <see cref="PitReach"/> metres? Shares <see cref="WorldTraversalAnalysis"/>'s grade limit, so
    /// the runtime and the validator agree on what "walkable" means.
    /// </summary>
    private static bool HasEscape(WorldHeightfield field, Vector3 position)
    {
        const float Step = 2f;
        int span = Mathf.CeilToInt(PitReach / Step);
        int side = (span * 2) + 1;
        float climbLimit = Step * WorldTraversalAnalysis.MaxGrade;
        float start = field.Height(position.X, position.Z);

        var heights = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                heights[(z * side) + x] = field.Height(
                    position.X + ((x - span) * Step), position.Z + ((z - span) * Step));
            }
        }

        var seen = new bool[side * side];
        var queue = new System.Collections.Generic.Queue<int>();
        int origin = (span * side) + span;
        seen[origin] = true;
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            int here = queue.Dequeue();
            int hx = here % side;
            int hz = here / side;
            // Reaching ground a clear step above the pit floor, at the edge of the search, is an
            // exit: the player is on a slope that keeps going up and out.
            if (heights[here] - start >= PitDepth * 0.9f)
            {
                return true;
            }
            for (int direction = 0; direction < 4; direction++)
            {
                int nx = hx + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                int nz = hz + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= side || nz >= side)
                {
                    continue;
                }
                int next = (nz * side) + nx;
                if (seen[next] || heights[next] - heights[here] > climbLimit)
                {
                    continue;
                }
                seen[next] = true;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private void Recover(PlayerCharacter player, WorldHeightfield field, Vector3 from, string reason)
    {
        Vector3 target = _safePoint ?? NearestShore(field, from);
        player.GlobalPosition = new Vector3(
            target.X, field.Height(target.X, target.Z) + 0.6f, target.Z);
        if (player is CharacterBody3D body)
        {
            body.Velocity = Vector3.Zero;
        }
        Log.Info($"WorldRecovery: pulled the player out of {reason} at {from.Snapped(Vector3.One)} " +
                 $"back to {target.Snapped(Vector3.One)}.");
    }

    /// <summary>
    /// The fallback when nothing has been remembered yet — a save loaded straight into a hole, or a
    /// teleport. Walks outward on a coarse spiral for the first dry, walkable ground.
    /// </summary>
    private static Vector3 NearestShore(WorldHeightfield field, Vector3 from)
    {
        for (float radius = 6f; radius <= 90f; radius += 6f)
        {
            int samples = Mathf.Max(8, Mathf.RoundToInt(radius));
            for (int i = 0; i < samples; i++)
            {
                float angle = Mathf.Tau * i / samples;
                float x = from.X + (Mathf.Cos(angle) * radius);
                float z = from.Z + (Mathf.Sin(angle) * radius);
                if (WorldWater.DepthAt(x, z, field) <= 0.05f && field.SlopeAt(x, z) <= MaxSafeSlope &&
                    !InHole(field, new Vector3(x, 0f, z)))
                {
                    return new Vector3(x, field.Height(x, z), z);
                }
            }
        }

        Log.Warn("WorldRecovery: no walkable ground within 90 m of the player; leaving them in place.");
        return from;
    }
}
