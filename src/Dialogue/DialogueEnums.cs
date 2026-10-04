namespace Embervale.Dialogue;

/// <summary>
/// Side effect a <see cref="DialogueChoice"/> fires when picked. Kept declarative
/// (an enum + a string argument) so conversations stay pure data — no scripting in
/// the <c>.tres</c>. <see cref="DialogueSession"/> applies the effect against the
/// speaking actor's components.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum DialogueEffect
{
    /// <summary>Nothing happens beyond navigating to the next node.</summary>
    None,

    /// <summary>Start the quest whose id is the choice's <c>EffectArg</c> on the player's log.</summary>
    StartQuest,

    /// <summary>Set the story flag named by the choice's <c>EffectArg</c>.</summary>
    SetFlag,

    /// <summary>Clear the story flag named by the choice's <c>EffectArg</c>.</summary>
    ClearFlag,

    /// <summary>Add the choice's <c>EffectArg</c> (an integer, may be negative) to the player's
    /// corruption. Dark dialogue choices raise it; atonement beats can lower it.</summary>
    AddCorruption,

    /// <summary>Recruit the companion whose id is the <c>EffectArg</c> into the party (Phase 32C).
    /// This is how a conversation turns into a party member with no bespoke code.</summary>
    RecruitCompanion,

    /// <summary>Dismiss the companion whose id is the <c>EffectArg</c>.</summary>
    DismissCompanion,

    /// <summary>Shift a companion's loyalty: <c>EffectArg</c> is <c>&lt;companionId&gt;:&lt;delta&gt;</c>
    /// (e.g. <c>companion.kael:10</c>; the delta may be negative).</summary>
    AddCompanionLoyalty,

    /// <summary>Teach the player the spell whose id is the <c>EffectArg</c> (Phase 35F). The Weave's
    /// rule (29.5E) is that spells are <em>recovered</em>, not vendored: this is the conversational
    /// half of that seam, where <see cref="Magic.SpellTomeComponent"/> is the found-object half. It
    /// goes through the same corruption-gated <c>Learn</c>, and works for a spell marked
    /// <c>PlayerLearnable = false</c> — which is the point, since such a spell can never be bought.</summary>
    LearnSpell,

    /// <summary>Open the shop whose id is the <c>EffectArg</c> (Phase 38E). This is how a merchant NPC
    /// trades without giving up the one interactable an entity gets — a <see cref="Economy.VendorComponent"/>
    /// behind a <see cref="DialogueComponent"/> never fires, and two of the three Ember Crown merchants
    /// carry live quest content that cannot be displaced by a menu. It also puts the shop id somewhere
    /// <c>ContentValidator</c> can read it, which a <c>.tscn</c> export never was.
    /// ⚠️ The choice carrying it must leave <c>Goto</c> empty: a conversation left open underneath the
    /// shop window returns the player to it when they close the shop.</summary>
    OpenShop,

    /// <summary>
    /// Use the service whose id is the <c>EffectArg</c> (Phase 38R) — the conversational half of
    /// <see cref="Economy.ServiceComponent"/>, and the member 38E deliberately did not append until
    /// there was an implementation behind it.
    ///
    /// It runs the whole 38D battery: the ordered refusal, the standing discount, the charge and the
    /// verb, through <c>ServiceComponent.TryUse</c>. What it does <em>not</em> carry is a prompt, so a
    /// choice offering a service says its price in authored text rather than deriving it — a
    /// conversation is written, not generated.
    ///
    /// ⚠️ The choice carrying it must leave <c>Goto</c> empty, for <see cref="OpenShop"/>'s reason: a
    /// conversation left open behind a vault or a crafting window returns when that window closes.
    ///
    /// ⚠️ <b>It cannot open a <c>Bank</c>.</b> That verb opens the <em>host entity's</em> inventory and
    /// a conversation has no host entity — <c>--validate</c> refuses the authoring outright rather than
    /// letting it warn at runtime.
    /// </summary>
    OpenService,

    /// <summary>
    /// Joins the guild named by <c>EffectArg</c> (a <c>faction.*</c> id) — the choke point 42B's
    /// retrospective named in advance: "every future join path — 42C's dialogue, 42I's contract
    /// board — has to route through" <see cref="Factions.GuildRules.CanJoin"/>, and this is where
    /// that routing lives. It runs the exact four flag writes the <c>guild join</c> console command
    /// does (clear refused, clear left, set offered, set joined), through
    /// <see cref="Factions.GuildRules"/>'s own flag-name builders rather than a literal string, so a
    /// guild join is never the hand-authored flag NOW.md invariant 18 forbids.
    ///
    /// ⚠️ Refuses silently when <see cref="Factions.GuildRules.CanJoin"/> says no (a left member of a
    /// guild with <c>RejoinAllowed = false</c>) — the same direction every other refusal in this
    /// enum takes, and the conversation's own text is what tells the player, not a toast.
    /// </summary>
    JoinGuild,

    /// <summary>
    /// Sets the player's rank in a guild to at least the value named by <c>EffectArg</c>
    /// (<c>&lt;factionId&gt;:&lt;rank&gt;</c>, e.g. <c>faction.iron_syndicate:1</c>) — the dialogue
    /// half of the <c>guild rank</c> console command, and for the same reason <see cref="JoinGuild"/>
    /// exists: a rank flag is <see cref="Factions.GuildRules"/>'s to name, never a literal in a
    /// <c>.tres</c>. Ranks are cumulative, so this sets every rank flag up to and including the
    /// named one and clears anything above it — the same "no gap" shape
    /// <see cref="Factions.GuildRules.Resolve"/> reads back.
    ///
    /// ⚠️ Idempotent, on purpose: a choice carrying this may be reachable more than once (an author
    /// gating it on a fresh state via <c>HasFlag</c>/<c>MissingFlag</c> against the rank flag itself
    /// would be exactly the hand-authored flag this exists to avoid), so re-granting a rank the
    /// player already holds is a no-op rather than a second promotion.
    /// </summary>
    GuildRank,

    /// <summary>Shift the player's standing with a faction: <c>EffectArg</c> is
    /// <c>&lt;factionId&gt;:&lt;delta&gt;</c> (e.g. <c>faction.dawnwardens:10</c>; the delta may be
    /// negative). Goes through <c>ReputationComponent.Add</c>, so it is clamped and announced like any
    /// other standing change. Appended by the campaign overhaul.</summary>
    AddReputation,

    /// <summary>Give the player items: <c>EffectArg</c> is <c>&lt;itemId&gt;:&lt;count&gt;</c> (count
    /// defaults to 1). Goes through <c>ItemGrant</c>, so a full pack spills the rest at the player's
    /// feet instead of destroying the gift.</summary>
    GiveItem,

    /// <summary>Take items from the player: <c>EffectArg</c> is <c>&lt;itemId&gt;:&lt;count&gt;</c>.
    /// The player must hold the full count; otherwise the effect is skipped (nothing is taken), so the
    /// choice carrying it should also carry a <see cref="DialogueCondition.HasItem"/> gate.</summary>
    TakeItem,

    /// <summary>Play a run of full-screen story cards: <c>EffectArg</c> is a locale key prefix, and the
    /// cards are <c>&lt;prefix&gt;.1</c>, <c>&lt;prefix&gt;.2</c>, ... until a key is missing. Published
    /// as <c>StoryCardsRequestedEvent</c> so dialogue code never references UI.</summary>
    PlayCards,

    /// <summary>Follow the quest whose id is <c>EffectArg</c> on the HUD (no-op unless it is active).</summary>
    TrackQuest,

    /// <summary>Request a chapter banner: <c>EffectArg</c> is a chapter key. Published as
    /// <c>StoryBannerRequestedEvent</c>; the UI renders it.</summary>
    Banner,
}

/// <summary>
/// Gate controlling whether a <see cref="DialogueChoice"/> is offered. Evaluated by
/// <see cref="DialogueSession"/> against the player's quest log, story flags and corruption;
/// a choice whose condition fails is hidden from the conversation.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum DialogueCondition
{
    /// <summary>Always shown.</summary>
    Always,

    /// <summary>Shown only while the quest (<c>ConditionArg</c>) can be started.</summary>
    QuestAvailable,

    /// <summary>Shown only while the quest is active in the log.</summary>
    QuestActive,

    /// <summary>Shown only once the quest has been completed.</summary>
    QuestCompleted,

    /// <summary>Shown only while the quest is not in the log at all.</summary>
    QuestNotStarted,

    /// <summary>Shown only when the story flag (<c>ConditionArg</c>) is set.</summary>
    HasFlag,

    /// <summary>Shown only when the story flag is not set.</summary>
    MissingFlag,

    /// <summary>Shown only while the player's corruption is at or above the threshold
    /// (<c>ConditionArg</c>, an integer 0–100).</summary>
    CorruptionAtLeast,

    /// <summary>Shown only while the player's corruption is below the threshold
    /// (<c>ConditionArg</c>, an integer 0–100).</summary>
    CorruptionBelow,

    /// <summary>Shown only while the companion (<c>ConditionArg</c>, an id) is in the party.</summary>
    CompanionRecruited,

    /// <summary>Shown only while the companion (<c>ConditionArg</c>, an id) is NOT in the party.</summary>
    CompanionNotRecruited,

    /// <summary>Shown only while a companion's loyalty is at or above a threshold:
    /// <c>ConditionArg</c> is <c>&lt;companionId&gt;:&lt;value&gt;</c> (e.g. <c>companion.kael:65</c>).
    /// This is the gate personal/loyalty content hangs off.</summary>
    CompanionLoyaltyAtLeast,

    /// <summary>Shown only while the shop (<c>ConditionArg</c>, a <c>shop.*</c> id) is trading —
    /// its authored hours against the <c>WorldClock</c> (Phase 38J). This is what a merchant's trade
    /// choice hangs off, so picking it is never a choice that does nothing.</summary>
    ShopOpen,

    /// <summary>Shown only while the shop is <em>shut</em> — the pair to <see cref="ShopOpen"/>, the
    /// same way <see cref="MissingFlag"/> pairs with <see cref="HasFlag"/>. It is what lets a merchant
    /// say she is closed in her own words rather than the game swallowing the choice.</summary>
    ShopClosed,

    /// <summary>
    /// Shown only while the player is a member of the guild at or above a rank:
    /// <c>ConditionArg</c> is <c>&lt;factionId&gt;:&lt;rank&gt;</c> (e.g.
    /// <c>faction.dawnwardens:2</c>), and <c>:0</c> — or a bare id — means a member of any rank.
    ///
    /// ⚠️ <b>This exists so that no conversation ever authors a guild flag by hand</b> (Phase 42B).
    /// Membership is <c>guild.&lt;slug&gt;.*</c> story-flag state that <see cref="Factions.GuildRules"/>
    /// DERIVES from the faction id; writing <c>guild.dawnwardens.rank2</c> into a <c>.tres</c> as a
    /// <see cref="HasFlag"/> argument would put a derived string in authored data where a typo
    /// becomes a branch that is simply never offered and nothing ever says so. It would also read a
    /// rank flag directly, which skips the cumulative-rank rule — a hand-set <c>rank3</c> with no
    /// <c>rank2</c> does not promote, and only <c>GuildRules.Resolve</c> knows that.
    /// </summary>
    GuildRankAtLeast,

    /// <summary>
    /// Shown only while the player is <em>not</em> a member of the guild named by
    /// <c>ConditionArg</c> — the pair to <see cref="GuildRankAtLeast"/>, and the condition a
    /// recruiting line hangs off. A player who left or was refused is not a member, so this is what
    /// re-offers the door; <c>GuildRules.CanJoin</c> remains the gate on whether it opens.
    /// </summary>
    GuildNotMember,

    /// <summary>
    /// Shown only while <c>GuildRules.CanJoin</c> would accept the player into the guild named by
    /// <c>ConditionArg</c> (a bare faction id). A <see cref="DialogueEffect.JoinGuild"/> choice that
    /// navigates to a "you're in" node must carry it: the effect refuses a left member of a guild with
    /// <c>RejoinAllowed = false</c>, but <c>Goto</c> fires regardless, so without this gate the refused
    /// player reads the success line with nothing granted. <c>--validate</c> enforces the pairing.
    /// </summary>
    GuildCanJoin,

    /// <summary>Shown only while the player's EARNED standing with a faction (<c>ReputationComponent.Get</c>,
    /// before the corruption "dread" penalty) is at or above a threshold: <c>ConditionArg</c> is
    /// <c>&lt;factionId&gt;:&lt;amount&gt;</c> (e.g. <c>faction.iron_syndicate:20</c>). Earned standing is
    /// used on purpose: an authored fork should read what the player did, not what their corruption
    /// currently shaves off it. Appended by the campaign overhaul.</summary>
    ReputationAtLeast,

    /// <summary>Shown only while the companion (<c>ConditionArg</c>, an id) is in the active party
    /// right now. The same test as <see cref="CompanionRecruited"/>; named for the party semantics the
    /// campaign's variant NPCs and optional objectives read.</summary>
    CompanionInParty,

    /// <summary>Shown only while the player holds the items: <c>ConditionArg</c> is
    /// <c>&lt;itemId&gt;:&lt;count&gt;</c> (count defaults to 1). The gate a <see cref="DialogueEffect.TakeItem"/>
    /// choice should carry.</summary>
    HasItem,
}
