using System;
using System.Collections.Generic;

namespace Embervale.Animation;

/// <summary>
/// Where an actor sits in the locomotion blend space, and the shape of that space. Pure, free of
/// Godot, and unit-tested: <see cref="LocomotionTree"/> builds the space from <see cref="Points"/>
/// and <see cref="CharacterAnimationComponent"/> feeds it <see cref="Gait"/> every frame.
///
/// <para><b>The axes are gaits, not metres per second.</b> Y is forward speed over the actor's OWN
/// run speed and X is sideways speed over the same, so a slow brute at a full run and a quick scout
/// at a full run both sit on the run clip. The space used to be in raw m/s with the points at
/// 1.6 / 4.2 / 6.8, which matched nobody: the player runs at 5 and sprints at 8, so a plain run was
/// already a third of the way into the sprint clip.</para>
/// </summary>
public static class LocomotionBlend
{
    /// <summary>Where each gait sits on the forward axis, as a fraction of run speed. They are the
    /// motor's own multipliers (<c>LocomotionComponent.WalkMultiplier</c> 0.45, run 1,
    /// <c>SprintMultiplier</c> 1.6), which is what makes each clip play at the speed it is for.</summary>
    public const float WalkGait = 0.45f;
    public const float RunGait = 1f;
    public const float SprintGait = 1.6f;
    public const float BackGait = -0.45f;

    /// <summary>Where a full sideways step sits on the strafe axis.</summary>
    public const float StrafeGait = 1f;

    /// <summary>The extent of the space on each axis. A little past the outermost points so a body
    /// shoved faster than its sprint still resolves to the sprint clip rather than leaving the space.</summary>
    public const float MinStrafe = -1.2f;
    public const float MaxStrafe = 1.2f;
    public const float MinForward = -0.7f;
    public const float MaxForward = 1.8f;

    /// <summary>The run speed assumed for a body with no motor to ask: a scene-placed NPC walked along
    /// its schedule by position. It puts the walk clip at about 1.6 m/s, where the schedule walks.</summary>
    public const float FallbackRunSpeed = 3.6f;

    /// <summary>Speeds below this (m/s) are standing still.</summary>
    public const float StillSpeed = 0.05f;

    /// <summary>Seconds of being off the ground and moving vertically before the fall pose takes
    /// over, and the vertical speed (m/s) that counts as moving. The wait keeps a stair nose or a
    /// kerb from flashing the pose; the speed keeps a body that has never been stepped (and so has
    /// never been told it is on a floor) from hanging in it.</summary>
    public const float AirborneSeconds = 0.18f;
    public const float AirborneSpeed = 0.5f;

    /// <summary>One clip's place in the space. <see cref="Fallback"/> names the slot whose clip
    /// stands in when the body has none for <see cref="Slot"/>, or is empty when the point is simply
    /// left out.</summary>
    public readonly record struct Point(string Slot, float X, float Y, string Fallback = "");

    /// <summary>
    /// The space. The forward axis carries the gaits; the two strafes sit either side of the idle.
    /// A body with no strafe clip borrows its walk there, because a space with every point on one
    /// line has no area to blend across.
    /// </summary>
    public static readonly IReadOnlyList<Point> Points = new[]
    {
        new Point("walk_back", 0f, BackGait),
        new Point("idle", 0f, 0f),
        new Point("walk", 0f, WalkGait),
        new Point("run", 0f, RunGait),
        new Point("sprint", 0f, SprintGait),
        new Point("strafe_left", -StrafeGait, 0f, "walk"),
        new Point("strafe_right", StrafeGait, 0f, "walk"),
    };

    /// <summary>The speed a body's gaits are measured against: its move-speed stat, else its motor's
    /// base speed, else <see cref="FallbackRunSpeed"/>.</summary>
    public static float RunSpeed(float moveSpeedStat, float baseSpeed)
    {
        if (float.IsFinite(moveSpeedStat) && moveSpeedStat > 0.1f)
        {
            return moveSpeedStat;
        }

        return float.IsFinite(baseSpeed) && baseSpeed > 0.1f ? baseSpeed : FallbackRunSpeed;
    }

    /// <summary>
    /// The blend position for a velocity in the body's own frame: <paramref name="forward"/> and
    /// <paramref name="right"/> in m/s (negative is backwards, and left), over
    /// <paramref name="runSpeed"/>. Clamped to the space; standing still is exactly the idle point.
    /// </summary>
    public static (float X, float Y) Gait(float forward, float right, float runSpeed)
    {
        if (!float.IsFinite(forward) || !float.IsFinite(right))
        {
            return (0f, 0f);
        }

        if (Math.Abs(forward) < StillSpeed && Math.Abs(right) < StillSpeed)
        {
            return (0f, 0f);
        }

        float run = float.IsFinite(runSpeed) && runSpeed > 0.1f ? runSpeed : FallbackRunSpeed;
        return (
            Math.Clamp(right / run, MinStrafe, MaxStrafe),
            Math.Clamp(forward / run, MinForward, MaxForward));
    }

    /// <summary>
    /// One frame of the airborne timer. It runs while the body is off the floor AND moving
    /// vertically, clears on the floor, and drains while the body hangs still in the air, so the top
    /// of a jump does not drop the pose and a body nobody is stepping never takes it up.
    /// </summary>
    public static float AirborneStep(float timer, bool grounded, float verticalSpeed, float delta)
    {
        if (grounded)
        {
            return 0f;
        }

        float dt = Math.Max(delta, 0f);
        return Math.Abs(verticalSpeed) > AirborneSpeed ? timer + dt : Math.Max(timer - dt, 0f);
    }

    /// <summary>Whether the fall pose should be showing for this airborne timer.</summary>
    public static bool Falling(float airborneTimer) => airborneTimer >= AirborneSeconds;

    /// <summary>
    /// The triangles of the space over <paramref name="points"/> (each an X and a Y), as index
    /// triples: every point off the forward axis fans to each neighbouring pair on it. Written out
    /// rather than left to the engine's triangulator because five of the seven points are on one
    /// line, which is the case a Delaunay pass is least predictable on.
    /// </summary>
    public static List<(int A, int B, int C)> Triangles(IReadOnlyList<(float X, float Y)> points)
    {
        var axis = new List<int>();
        var sides = new List<int>();
        for (int i = 0; i < points.Count; i++)
        {
            (Math.Abs(points[i].X) < 0.0001f ? axis : sides).Add(i);
        }

        axis.Sort((a, b) => points[a].Y.CompareTo(points[b].Y));

        var triangles = new List<(int A, int B, int C)>();
        foreach (int side in sides)
        {
            for (int i = 0; i + 1 < axis.Count; i++)
            {
                triangles.Add((side, axis[i], axis[i + 1]));
            }
        }

        return triangles;
    }
}
