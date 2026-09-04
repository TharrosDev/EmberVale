using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The perception and coordination rules, as pure functions.
///
/// <para>Each of these was a branch inside <see cref="EnemyAIComponent"/> with a paragraph of
/// comment above it explaining a bug it had cost, and none of them had a test — because the branch
/// sat inside a 1229-line Godot node that xUnit cannot construct. Pulled out here they are ordinary
/// arithmetic, and <c>AiSenseRulesTests</c> pins every one of the guarantees the comments describe.
/// </para>
///
/// <para>This is the same shape the repo already uses for <see cref="EnemyPerception"/>,
/// <see cref="CasterDecision"/>, <see cref="PackFlank"/>, <see cref="GuardCycle"/> and
/// <see cref="TerritoryLeash"/>: engine-free logic beside the node that calls it.</para>
/// </summary>
public static class AiSenseRules
{
    /// <summary>Metres of vertical separation beyond which a ground actor does not engage.</summary>
    public const float VerticalVisionLimit = 8f;

    /// <summary>Vertical reach of a melee swing, in metres.</summary>
    public const float AttackVerticalReach = 2.5f;

    /// <summary>
    /// Can a ground-bound actor see something this far above or below it?
    ///
    /// ⚠️ <b>RANGE WAS PURELY HORIZONTAL, AND THE WORLD HAS THIRTY-METRE CLIFFS IN IT.</b> A player
    /// on the Ancient Aerie's rim is a couple of metres from the trench floor in plan and thirty
    /// metres up in fact; the line of sight is clear open air, so every creature below engaged, could
    /// not path to them (an unreachable goal), and stood there provoked for the rest of the session.
    ///
    /// <paramref name="canFly"/> is exempt <b>in both directions</b>, whether or not the actor is off
    /// the ground this instant: closing a vertical gap is exactly what a flier does.
    /// </summary>
    public static bool PassesVerticalVisionGate(float deltaY, bool canFly) =>
        canFly || Mathf.Abs(deltaY) <= VerticalVisionLimit;

    /// <summary>
    /// May this actor swing right now? Two gates, both of them documented bugs: a flier hovering
    /// overhead was being hit by ground melee, and a target standing on a two-metre ledge was being
    /// hit through it.
    /// </summary>
    public static bool CanSwing(bool airborne, float deltaY) =>
        !airborne && Mathf.Abs(deltaY) <= AttackVerticalReach;

    /// <summary>
    /// Whether an actor answers another's alert shout. Four filters, in order, each of which exists
    /// because the absence of it was visible in play:
    /// <list type="number">
    /// <item>Not its own shout.</item>
    /// <item>An ambusher holds its trap even when the pack starts shouting — walking to the noise is
    /// exactly what would give the ambush away.</item>
    /// <item>Only the shouter's own kind answers, matched <b>ordinally</b>. Without this a goblin's
    /// yell put the town guard, the Ashen and every other faction's actors in earshot onto the
    /// player's position.</item>
    /// <item>An actor with no quarrel with the player does not go looking for one because a
    /// neighbour shouted. Provocation is personal; standing decides the rest.</item>
    /// </list>
    /// </summary>
    public static bool AnswersAlert(bool isOwnShout, bool isAmbusher, string listenerFaction, string shouterFaction, bool playerIsTarget)
    {
        if (isOwnShout || isAmbusher)
        {
            return false;
        }

        if (!string.Equals(listenerFaction, shouterFaction, System.StringComparison.Ordinal))
        {
            return false;
        }

        return playerIsTarget;
    }

    /// <summary>
    /// Is the shout audible? ⚠️ <b>Measured in three dimensions, against the SHOUTER's radius.</b>
    /// Horizontal distance let a shout carry up a thirty-metre cliff face, and the listener's own
    /// radius is what it uses when it is the one shouting.
    /// </summary>
    public static bool HearsAlert(Vector3 listenerPosition, Vector3 shoutPosition, float shoutRadius) =>
        listenerPosition.DistanceTo(shoutPosition) <= shoutRadius;

    /// <summary>
    /// Whether an actor gives its position away when it engages. <c>0</c> — the ambusher's default —
    /// means it fights silently. <b>Silent is not deaf</b>: this governs shouting, not listening.
    /// </summary>
    public static bool ShoutsOnEngage(float alertRadius) => alertRadius > 0f;

    /// <summary>
    /// An ambusher sees its target long before it springs, and holds until they walk into the trap.
    /// A profile with no ambush range springs the moment it sees anything.
    /// </summary>
    public static bool SpringsAmbush(bool isAmbusher, float distance, float ambushRange) =>
        !isAmbusher || distance <= ambushRange;
}
