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

    [ExportGroup("Availability")]
    /// <summary>Optional quest id that must be completed first; empty = always available.</summary>
    [Export] public string PrerequisiteQuestId { get; set; } = string.Empty;

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
