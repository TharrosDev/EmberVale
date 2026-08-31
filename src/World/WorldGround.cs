using Godot;

namespace Embervale.World;

/// <summary>
/// The one place anything outside the streaming pipeline can ask "how high is the ground here?"
/// (the 2026-08-29 geography overhaul).
///
/// ⚠️ <b>IT EXISTS BECAUSE THE WORLD STOPPED BEING FLAT AND SEVERAL SYSTEMS STILL ASSUMED IT WAS.</b>
/// Three of them wrote a literal Y and were silently correct for as long as every floor's top face
/// was exactly y = 0: <c>ScheduleComponent.MoveToward</c> slides an NPC's <c>GlobalPosition</c> and
/// keeps <c>pos.Y</c>, so a merchant walking a routine across a terrace would have held the height
/// of wherever it spawned; <c>SafeZones.TryRingPointOutside</c> hands the encounter and world-event
/// directors a spawn point at y = 0.5, which on real terrain is inside a hillside as often as above
/// it; and the save migration needs to put an old absolute player position back on the ground. None
/// of those failures would have looked like a terrain change.
///
/// A raycast would also answer the question and would have been the reflex. This is cheaper and
/// honest: the field is a pure function the streamer already holds, it needs no physics frame, no
/// collision mask and no fallback when the query fires before the world is built — it simply
/// returns 0 when no region is resident, which is the old behaviour exactly.
/// </summary>
public static class WorldGround
{
    /// <summary>The active region's heightfield, or null outside a region (the sandbox).</summary>
    public static WorldHeightfield? Field { get; private set; }

    /// <summary>Set by <see cref="RegionStreamer.Configure"/>; cleared when no region is active.</summary>
    public static void Set(WorldHeightfield? field) => Field = field;

    /// <summary>Ground height at a world X/Z, or 0 when no region is resident.</summary>
    public static float HeightAt(float worldX, float worldZ) => Field?.Height(worldX, worldZ) ?? 0f;

    /// <summary>The same point with its Y replaced by the ground under it, plus a clearance.</summary>
    public static Vector3 OnGround(Vector3 point, float clearance = 0f) =>
        new(point.X, HeightAt(point.X, point.Z) + clearance, point.Z);

    /// <summary>
    /// The same point, raised onto the ground if it is under it — and left alone if it is above.
    ///
    /// ⚠️ <b>THE ONE PLACE THE "NEVER PUT AN ACTOR UNDER THE GROUND" RULE LIVES.</b> It lifts and
    /// never lowers, so a legitimate position on a terrace, a bridge or a rooftop is untouched while
    /// a point that has drifted below the surface — a hand-edited save, a fast-travel point recorded
    /// before a cell was re-cut, a formation slot copied from a player standing further up a slope —
    /// comes back out of the hillside. Every teleport in the game should go through here or through
    /// <see cref="OnGround"/>; three of them used to write a literal Y and were correct only for as
    /// long as the world was flat.
    /// </summary>
    public static bool IsBelowGround(Vector3 point, float tolerance = 0.05f) =>
        point.Y < HeightAt(point.X, point.Z) + tolerance;

    /// <inheritdoc cref="IsBelowGround"/>
    public static Vector3 Lift(Vector3 point, float clearance = 0.1f) =>
        IsBelowGround(point) ? OnGround(point, clearance) : point;
}
