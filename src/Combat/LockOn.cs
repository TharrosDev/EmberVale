using System;
using System.Collections.Generic;

namespace Embervale.Combat;

/// <summary>
/// Pure lock-on selection maths (Phase 29H). Godot-free so the cycling/range logic is unit-testable;
/// <see cref="LockOnComponent"/> drives the target queries and applies these.
/// </summary>
public static class LockOn
{
    /// <summary>The next target index when cycling by <paramref name="dir"/> (+1 / -1), wrapping. A
    /// <paramref name="current"/> of -1 (no lock) starts the cycle at the first/last entry.</summary>
    public static int CycleIndex(int current, int count, int dir)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return dir >= 0 ? 0 : count - 1;
        }

        return ((current + dir) % count + count) % count;
    }

    /// <summary>Whether a candidate at <paramref name="distanceSq"/> is still within
    /// <paramref name="rangeSq"/> (both squared, to skip a sqrt).</summary>
    public static bool InRange(float distanceSq, float rangeSq) => distanceSq <= rangeSq;

    /// <summary>
    /// How good a lock-on candidate is. <b>Lower is better</b>, and a negative score means "not a
    /// candidate at all".
    ///
    /// <para>⚠️ <b>This used to be distance, and nothing else.</b> The nearest valid enemy won, so
    /// standing between two of them locked whichever was a hand's width closer, and an enemy behind
    /// the player beat one they were looking straight at. Angle is weighted heavily for exactly that
    /// reason: what the player is aiming at is a far better guess at what they mean than what they
    /// happen to be standing near.</para>
    ///
    /// <param name="distance">Metres to the candidate.</param>
    /// <param name="angleFromView">Radians between the camera's forward and the candidate.</param>
    /// <param name="maxDistance">Beyond this, not a candidate.</param>
    /// <param name="maxAngle">Beyond this off-screen, not a candidate — which is what stops the
    /// player locking something behind them.</param>
    /// <param name="hasLineOfSight">False for a candidate behind a wall.</param>
    /// </summary>
    public static float Score(
        float distance, float angleFromView, float maxDistance, float maxAngle, bool hasLineOfSight)
    {
        if (!hasLineOfSight || distance > maxDistance || angleFromView > maxAngle ||
            distance < 0f || angleFromView < 0f)
        {
            return -1f;
        }

        // Normalised so the weights mean something regardless of the ranges: at the edge of either
        // limit the term is 1. Angle is worth three times distance.
        float byAngle = maxAngle > 0f ? angleFromView / maxAngle : 0f;
        float byDistance = maxDistance > 0f ? distance / maxDistance : 0f;
        return (byAngle * 3f) + byDistance;
    }

    /// <summary>
    /// Whether a better-scoring candidate is worth switching to while a lock is already held.
    ///
    /// ⚠️ <b>Hysteresis, and it is the difference between a lock and a flicker.</b> Two enemies
    /// scoring within a hair of each other will trade places every frame the player's aim drifts,
    /// and the camera snaps back and forth between them. A challenger has to be meaningfully better,
    /// not merely better.
    /// </summary>
    public static bool ShouldSwitch(float currentScore, float challengerScore, float margin)
    {
        if (challengerScore < 0f)
        {
            return false;
        }

        return currentScore < 0f || challengerScore < currentScore - margin;
    }

    /// <summary>Seconds a held target may stay out of view or past the drop range before the lock is
    /// let go. A dodge behind a pillar or a step over the range edge should not throw the lock — and
    /// with it the whole camera framing — away.</summary>
    public const float LossGraceSeconds = 0.8f;

    /// <summary>How long a target has been continuously lost: 0 while it is in view and in range, else
    /// the running total.</summary>
    public static float StepLoss(float lostSeconds, bool inView, float dt) =>
        inView ? 0f : lostSeconds + Math.Max(dt, 0f);

    /// <summary>Whether a target lost for <paramref name="lostSeconds"/> is gone for good. A
    /// non-positive grace drops the instant it is lost, which is the pre-grace behaviour.</summary>
    public static bool ShouldDrop(float lostSeconds, float graceSeconds) =>
        lostSeconds > 0f && lostSeconds >= graceSeconds;

    /// <summary>
    /// The candidate to cycle to, chosen by where they stand on screen rather than by their rank in a
    /// score list.
    ///
    /// <para>⚠️ <b>Cycling used to walk the score-sorted list</b>, and scores shift as the player and
    /// the enemies move, so "next" jumped to an unpredictable foe and could bounce between two. Left to
    /// right by <paramref name="bearings"/> (radians, positive right) is stable and matches the
    /// direction the stick or wheel was pushed. Returns an index into <paramref name="bearings"/>, or
    /// -1 when it is empty; a <paramref name="current"/> of -1 starts at the far end for
    /// <paramref name="dir"/>.</para>
    /// </summary>
    public static int CycleByBearing(IReadOnlyList<float> bearings, int current, int dir)
    {
        int count = bearings.Count;
        if (count == 0)
        {
            return -1;
        }

        var order = new int[count];
        var keys = new float[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            keys[i] = bearings[i];
        }

        Array.Sort(keys, order);
        int slot = current < 0 ? -1 : Array.IndexOf(order, current);
        return order[CycleIndex(slot, count, dir)];
    }

    /// <summary>How much wider the acquisition cone gets with Lock-On Assist on, radians. A lock
    /// that catches something at the very edge of the screen is the assist.</summary>
    public const float AssistAngleBonus = 0.25f;

    /// <summary>The half-angle a candidate may sit at, with the assist's bonus if it is on.</summary>
    public static float AcquireAngle(float baseAngle, bool assist) =>
        assist ? baseAngle + AssistAngleBonus : baseAngle;

    /// <summary>Score multiplier for a candidate that is a threat right now (mid-swing at the player).</summary>
    public const float ThreatFactor = 0.7f;

    /// <summary>Score multiplier for a candidate close to death: the one to finish.</summary>
    public const float WoundedFactor = 0.9f;

    /// <summary>Health fraction at or below which a candidate counts as wounded.</summary>
    public const float WoundedFraction = 0.25f;

    /// <summary>
    /// The score after priority. <b>Lower is better</b>, and a negative score (not a candidate) stays
    /// negative. An enemy that is winding up or swinging outranks one standing idle at the same
    /// angle and range, because it is the one the player needs to watch; a nearly-dead one edges out
    /// a healthy one. Without the assist, priority is off and the plain score stands.
    /// </summary>
    public static float Prioritised(float score, bool assist, bool threat, float healthFraction)
    {
        if (score < 0f || !assist)
        {
            return score;
        }

        float adjusted = score;
        if (threat)
        {
            adjusted *= ThreatFactor;
        }

        if (healthFraction <= WoundedFraction)
        {
            adjusted *= WoundedFactor;
        }

        return adjusted;
    }

    /// <summary>How far, metres, a dead target's replacement may be for the assist to move the lock
    /// onto it rather than drop it.</summary>
    public const float AutoAdvanceRange = 10f;

    /// <summary>Whether a lock that just ended should pass to the next enemy instead of dropping.
    /// Only a kill does, and only with the assist on: a deliberate toggle, losing sight and running
    /// out of range all leave the player unlocked, because they are the player's or the world's call.</summary>
    public static bool ShouldAutoAdvance(bool assist, LockBreakReason reason) =>
        assist && reason == LockBreakReason.TargetDied;

    /// <summary>Mouse travel (accumulated pixels) that counts as a flick to switch targets.</summary>
    public const float FlickPixels = 140f;

    /// <summary>Seconds after a switch before another flick registers, so one sweep of the mouse is one
    /// step and not a spin through every enemy.</summary>
    public const float FlickCooldown = 0.4f;
}

/// <summary>Why a lock ended. <b>Append-only.</b></summary>
public enum LockBreakReason
{
    /// <summary>The player toggled it off.</summary>
    Toggled = 0,

    /// <summary>The target died.</summary>
    TargetDied = 1,

    /// <summary>The target left the drop range.</summary>
    OutOfRange = 2,

    /// <summary>The target stayed out of sight past the grace.</summary>
    LostSight = 3,

    /// <summary>The target became invalid: freed, or no longer hostile.</summary>
    Invalid = 4,
}

/// <summary>
/// Turns look input into "switch target" while locked on: a hard flick of the mouse or of the right
/// stick steps to the next target in that direction. Pure and clocked by the caller. The mouse
/// accumulates and bleeds off, so a slow drift never switches and a fast sweep does; the stick
/// switches on the edge of a full push, not while it is held.
/// </summary>
public sealed class FlickGate
{
    private float _accumulated;
    private float _cooldown;
    private bool _stickHeld;

    /// <summary>Feeds mouse motion (pixels, positive right); returns -1, 0 or 1 when a flick lands.</summary>
    public int FeedMouse(float dx, float dt)
    {
        _cooldown = Math.Max(0f, _cooldown - dt);
        _accumulated *= MathF.Exp(-Math.Max(dt, 0f) * 8f);
        _accumulated += dx;
        return Take(ref _accumulated);
    }

    /// <summary>Feeds the look stick's horizontal axis (-1..1); returns -1, 0 or 1 on the edge of a
    /// full push.</summary>
    public int FeedStick(float axis, float dt)
    {
        _cooldown = Math.Max(0f, _cooldown - dt);
        bool pushed = Math.Abs(axis) >= 0.85f;
        int result = 0;
        if (pushed && !_stickHeld && _cooldown <= 0f)
        {
            result = axis > 0f ? 1 : -1;
            _cooldown = LockOn.FlickCooldown;
        }

        if (Math.Abs(axis) < 0.5f)
        {
            _stickHeld = false;
        }
        else if (pushed)
        {
            _stickHeld = true;
        }

        return result;
    }

    /// <summary>Forgets the motion (a lock ended).</summary>
    public void Reset()
    {
        _accumulated = 0f;
        _cooldown = 0f;
        _stickHeld = false;
    }

    private int Take(ref float accumulated)
    {
        if (_cooldown > 0f || Math.Abs(accumulated) < LockOn.FlickPixels)
        {
            return 0;
        }

        int dir = accumulated > 0f ? 1 : -1;
        accumulated = 0f;
        _cooldown = LockOn.FlickCooldown;
        return dir;
    }
}
