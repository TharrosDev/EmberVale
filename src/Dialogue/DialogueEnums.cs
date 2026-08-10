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
}
