namespace Embervale.Quests;

/// <summary>What an objective measures. Each kind binds to a gameplay event the
/// <see cref="QuestLogComponent"/> listens for.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ObjectiveType
{
    /// <summary>Slay N actors whose <c>TemplateId</c> matches the objective target.</summary>
    Kill,

    /// <summary>Pick up N of an item whose id matches the objective target.</summary>
    Collect,

    /// <summary>
    /// Arrive at the map location whose id matches the objective target (Phase 41A).
    ///
    /// ⚠️ <b>Arrival is PROXIMITY, not discovery, and the difference is the whole trap.</b>
    /// <c>MapService</c> already tracks which locations the player has found, and driving this off
    /// that looks like free reuse — but a location authored <c>RevealWithCell</c> is discovered <b>on
    /// entering the REGION</b> (NOW.md invariant 1), so a discovery-driven Reach would tick complete
    /// the moment the player crossed into the Ember Crown from anywhere in it. The player would watch
    /// an objective they never travelled to satisfy itself. <see cref="QuestLogComponent"/> distance-
    /// tests instead.
    ///
    /// <b>Its <c>TargetId</c> is a <c>location.*</c> id, so a Reach objective supplies its own
    /// destination</b> — <see cref="ObjectiveResource.LocationId"/> is redundant on one and should be
    /// left empty. 39.5C measured that only 1 of ~20 objectives could name a location; every Reach
    /// objective names one by construction.
    /// </summary>
    Reach,

    /// <summary>
    /// Hold a conversation with the owner of the dialogue whose id matches the objective target
    /// (Phase 41A). Advances on <c>DialogueEndedEvent</c> — the conversation <em>happened</em> —
    /// rather than on the panel opening.
    /// </summary>
    Talk,
}

/// <summary>Lifecycle state of a quest in the player's log.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum QuestStatus
{
    Active,
    Completed,
}
