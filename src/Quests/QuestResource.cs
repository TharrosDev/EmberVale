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
    /// <summary>Stable unique id, e.g. "quest.cull_goblins". The save/database key.</summary>
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

    [ExportGroup("Availability")]
    /// <summary>Optional quest id that must be completed first; empty = always available.</summary>
    [Export] public string PrerequisiteQuestId { get; set; } = string.Empty;

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
