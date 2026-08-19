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

    /// <summary>
    /// Bring a recruited companion to a place alive (Phase 41B). <c>TargetId</c> is a
    /// <c>companion.*</c> id and <see cref="ObjectiveResource.LocationId"/> is the destination —
    /// this is the one objective shape where that field is <b>required</b>, the mirror of
    /// <see cref="Reach"/>, which refuses it.
    ///
    /// ⚠️ <b>The escortee is a party companion, and that is the whole implementation.</b> Nothing in
    /// this build could damage a villager NPC — they are <c>Entity</c> nodes with a static collider,
    /// no stats and no hurtbox — so an escort whose fail state is "your charge died" needs a body
    /// that can actually take a hit. <c>CompanionFactory</c> already builds one from a <c>.tres</c>,
    /// with health, combat, follow AI and persistence, and <c>DialogueEffect.RecruitCompanion</c>
    /// already puts it beside the player. So escorting is authored data rather than a new system.
    ///
    /// <b>Fails on <c>CompanionDownedEvent(Downed: true)</c></b> for that companion — the one event
    /// in the game that means *the person you were protecting has fallen*. A companion stands back
    /// up afterwards (they are story actors and are never permanently lost), so the failure is the
    /// moment they go down, not a corpse to find.
    /// </summary>
    Escort,

    /// <summary>
    /// Hold a place for a while (Phase 41B). <c>TargetId</c> is a <c>location.*</c> id and
    /// <see cref="ObjectiveResource.RequiredCount"/> is <b>seconds</b>, accumulated by the same 4 Hz
    /// poll <see cref="Reach"/> uses while the player stands inside
    /// <c>QuestLogComponent.DefendRadius</c>.
    ///
    /// ⚠️ <b>Leaving the site stops the count; it does not reset it.</b> A hold that silently rewinds
    /// is a fail state wearing no label — the player sees the number fall and has been told nothing.
    /// Walking away costs the time it costs, and the only fail here is the loud one below.
    ///
    /// ⚠️ <b>There is no attacker knob, deliberately.</b> The pressure is geography: site the
    /// objective outside a region's <c>SafeZoneRadius</c> and the <c>EncounterDirector</c> already
    /// spawns into it. Driving a <c>WorldEventDirector</c> raid from the quest was considered and
    /// refused — a randomly rolled raid of the same id is indistinguishable from the quest's own, so
    /// an unrelated timeout would fail the player's quest (41A's trap, one type later).
    ///
    /// <b>Fails on the player's own <c>EntityDiedEvent</c></b> while the hold is unfinished. Surviving
    /// is the objective, so dying is the only thing that can be a failure of it.
    /// </summary>
    Defend,
}

/// <summary>Lifecycle state of a quest in the player's log.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum QuestStatus
{
    Active,
    Completed,

    /// <summary>
    /// The quest was lost rather than finished (Phase 41B) — the escortee went down, or the player
    /// died mid-hold. The first state a quest could ever end in that is not a success.
    ///
    /// ⚠️ <b>A failed quest is retakeable, and that is a decision rather than an oversight.</b>
    /// <c>QuestLogComponent.CanStart</c> refuses any quest already in the log <em>except</em> a
    /// failed one, which restarts with fresh counts. The alternative — failure is permanent —
    /// silently deletes content from a save on one bad fight, with no warning and no way back to
    /// the giver who is still standing there.
    /// </summary>
    Failed,
}
