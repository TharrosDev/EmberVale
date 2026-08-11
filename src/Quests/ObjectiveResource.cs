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

    /// <summary>
    /// Optional <c>location.*</c> id naming <b>where the player should go</b> for this objective.
    ///
    /// ⚠️ <b>This is the field the map has been waiting for since 37.5E, and its absence is why
    /// quest markers sat on the deferred table for two sub-phases.</b> An objective names a
    /// <see cref="TargetId"/> — a template or an item — and a template is not a place: "kill an ash
    /// dragon" is answerable by <see cref="ObjectiveLocator"/> scanning loaded actors, but only if
    /// one happens to be loaded. Across a region boundary there is nothing to scan, so the compass
    /// pointed nowhere and the map could draw nothing.
    ///
    /// ⚠️ <b>Deliberately optional, and an empty value is a real answer.</b> A Collect objective for
    /// a herb that grows everywhere has no one place, and inventing one would send the player to a
    /// spot no better than any other — worse than admitting the game does not know. Author it only
    /// where a destination genuinely exists.
    ///
    /// ⚠️ <b>It names a location, never a coordinate.</b> Where that location IS remains its
    /// marker's transform in a cell scene (39.5A's rule); this is a reference, and `--validate`
    /// fails on one that names a location the database does not have.
    /// </summary>
    [Export] public string LocationId { get; set; } = string.Empty;

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
