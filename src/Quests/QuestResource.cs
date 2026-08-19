using Godot;

namespace Embervale.Quests;

/// <summary>
/// A designer-authored quest: an ordered set of <see cref="ObjectiveResource"/>s plus
/// the rewards granted when all are met. Authored as a <c>.tres</c> under
/// <c>data/quests/</c> and indexed by <see cref="QuestDatabase"/>; the
/// <see cref="QuestLogComponent"/> tracks per-player progress against it.
///
/// New quest = a <c>.tres</c>, no code change.
/// </summary>
[GlobalClass]
public partial class QuestResource : Resource
{
    /// <summary>Stable unique id, e.g. "quest.warband.bounty". The save/database key.</summary>
    [Export] public string Id { get; set; } = "quest.unknown";

    [Export] public string Title { get; set; } = "Untitled Quest";

    [Export(PropertyHint.MultilineText)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Objectives (all must complete). Untyped so authored sub-resource arrays
    /// bind cleanly; elements are read back as <see cref="ObjectiveResource"/>.</summary>
    [Export] public Godot.Collections.Array Objectives { get; set; } = new();

    [ExportGroup("Rewards")]
    [Export] public int XpReward { get; set; }
    [Export] public int GoldReward { get; set; }
    // Authored default (mirrors GameIds.Currency.Gold); kept literal for the Godot [Export] generator.
    [Export] public string GoldItemId { get; set; } = "item.currency.gold";
    /// <summary>Item grants on completion (elements are <see cref="QuestItemReward"/>).</summary>
    [Export] public Godot.Collections.Array RewardItems { get; set; } = new();

    /// <summary>Faction whose standing this quest moves on completion; empty = none (Phase 34.5C).
    /// Mirrors <c>WorldEventResource.FactionRewardId</c> — doing a faction's work is the counterpart
    /// to killing its members, which already costs standing automatically.</summary>
    [Export] public string FactionRewardId { get; set; } = string.Empty;

    /// <summary>Standing granted with <see cref="FactionRewardId"/>. May be negative.</summary>
    [Export] public int FactionRewardAmount { get; set; }

    /// <summary>
    /// Seconds the player has to finish this quest before it fails (Phase 41C); <b>0 = untimed</b>,
    /// which is what every quest authored before this was and what almost every quest should stay.
    ///
    /// ⚠️ <b>The deadline is a property of the ERRAND, not of one of its steps</b>, which is why
    /// there is no <c>Timed</c> objective type. <see cref="QuestProgress"/> stores one <c>int</c> per
    /// objective — a count — with nowhere to put a per-objective clock, and two ways to express a
    /// deadline is invariant 5's failure waiting to happen. It mirrors
    /// <c>WorldEventResource.TimeLimitSeconds</c>, which made the same call for the same reason.
    ///
    /// ⚠️ <b>It counts REAL seconds of play, and it does not run while the tree is paused</b> — so a
    /// player reading their journal or their inventory is not losing time. That falls out of the
    /// countdown living in an ordinary component's <c>_Process</c>, and it is correct: the clock is a
    /// pressure on the errand, not a punishment for opening a menu.
    /// </summary>
    [Export] public float TimeLimitSeconds { get; set; }

    [ExportGroup("Availability")]
    /// <summary>Optional quest id that must be completed first; empty = always available.</summary>
    [Export] public string PrerequisiteQuestId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this quest belongs to the main thread (Phase 37.5E). Drives the journal's Main/Side
    /// split and the HUD tracker's priority colour.
    ///
    /// It exists because there was no honest way to infer it. 37.5B briefly tinted the tracker by
    /// "has a `PrerequisiteQuestId`", which is not just wrong but **backwards** — a prerequisite
    /// chains a quest into a sequence, which if anything marks it as *more* story, not less. That
    /// heuristic was removed rather than shipped, and this is the field it was standing in for.
    ///
    /// Defaults to <c>false</c>: a quest is a side errand unless someone says otherwise, which is
    /// the safe default because miscolouring an errand as the main thread is the failure that
    /// misdirects a player.
    /// </summary>
    [Export] public bool IsMainQuest { get; set; }

    /// <summary>
    /// Declares that this quest may target a creature the world spawns only once — a lair boss rather
    /// than an encounter type (Phase 35F's Ancient-dragon errand is the first). Kill objectives are
    /// otherwise required to name something an encounter or world event can spawn again, because a
    /// quest whose only target is already permanently dead can never be completed and never leaves the
    /// journal.
    ///
    /// Setting this is a promise that <em>the conversation offering the quest gates on the target still
    /// being alive</em> — the validator cannot see dialogue gating, so this flag is where the author
    /// takes responsibility for it. Do not set it to silence the error.
    /// </summary>
    [Export] public bool AllowsOneShotTarget { get; set; }

    /// <summary>The objectives read back as their concrete type, skipping bad entries.</summary>
    public System.Collections.Generic.List<ObjectiveResource> ObjectiveList()
    {
        var list = new System.Collections.Generic.List<ObjectiveResource>();
        foreach (Variant element in Objectives)
        {
            if (element.As<ObjectiveResource>() is { } objective)
            {
                list.Add(objective);
            }
        }

        return list;
    }
}
