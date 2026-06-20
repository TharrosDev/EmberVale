using Godot;

namespace Embervale.Quests;

/// <summary>
/// One objective of a <see cref="QuestResource"/>: a goal of a given
/// <see cref="ObjectiveType"/> against a <see cref="TargetId"/>, completed once
/// <see cref="RequiredCount"/> is reached. Authored as a sub-resource inside a quest
/// <c>.tres</c>. The <see cref="QuestLogComponent"/> advances it from gameplay events.
/// </summary>
[GlobalClass]
public partial class ObjectiveResource : Resource
{
    [Export] public ObjectiveType Type { get; set; } = ObjectiveType.Kill;

    /// <summary>For <see cref="ObjectiveType.Kill"/>: an entity <c>TemplateId</c>
    /// (e.g. "enemy.goblin"). For <see cref="ObjectiveType.Collect"/>: an item id.</summary>
    [Export] public string TargetId { get; set; } = string.Empty;

    [Export] public int RequiredCount { get; set; } = 1;

    /// <summary>Optional hand-written objective text; falls back to a generated line.</summary>
    [Export] public string Description { get; set; } = string.Empty;

    /// <summary>Count-free objective label for UI (the count is shown separately as
    /// "n/N"). Uses <see cref="Description"/> when authored.</summary>
    public string ShortLabel()
    {
        if (!string.IsNullOrEmpty(Description))
        {
            return Description;
        }

        return Type switch
        {
            ObjectiveType.Kill => $"Slay {TargetId}",
            ObjectiveType.Collect => $"Collect {TargetId}",
            _ => TargetId,
        };
    }
}
