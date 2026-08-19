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

    /// <summary>
    /// What the objective measures against, by type:
    /// <list type="bullet">
    /// <item><see cref="ObjectiveType.Kill"/> — an entity <c>TemplateId</c> (e.g. "enemy.goblin").</item>
    /// <item><see cref="ObjectiveType.Collect"/> — an item id.</item>
    /// <item><see cref="ObjectiveType.Reach"/> — a <c>location.*</c> map location id (41A).</item>
    /// <item><see cref="ObjectiveType.Talk"/> — a <c>dlg.*</c> dialogue id (41A).</item>
    /// <item><see cref="ObjectiveType.Escort"/> — a <c>companion.*</c> id; the destination is
    /// <see cref="LocationId"/>, which this type <b>requires</b> (41B).</item>
    /// <item><see cref="ObjectiveType.Defend"/> — a <c>location.*</c> id to hold, for
    /// <see cref="RequiredCount"/> seconds (41B).</item>
    /// </list>
    /// Every one of the six is checked by <c>--validate</c> against its own database, so a typo is a
    /// failed gate rather than an objective that can never advance.
    /// </summary>
    [Export] public string TargetId { get; set; } = string.Empty;

    /// <summary>How many of <see cref="TargetId"/> the objective needs — and for
    /// <see cref="ObjectiveType.Defend"/>, how many <b>seconds</b> the place must be held (41B).</summary>
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
    /// ⚠️ <b>An <see cref="ObjectiveType.Escort"/> objective is the one shape that REQUIRES it</b>
    /// (41B), and <c>--validate</c> refuses one without it. There the target is the person and this
    /// is where they are being taken, so the objective is unanswerable without both — the exact
    /// mirror of <see cref="ObjectiveType.Reach"/>, whose target already IS the destination and which
    /// therefore refuses this field.
    ///
    /// ⚠️ <b>It names a location, never a coordinate.</b> Where that location IS remains its
    /// marker's transform in a cell scene (39.5A's rule); this is a reference, and `--validate`
    /// fails on one that names a location the database does not have.
    /// </summary>
    [Export] public string LocationId { get; set; } = string.Empty;

    /// <summary>
    /// Count-free objective label for UI (the count is shown separately as "n/N"). Returns
    /// <see cref="Description"/>, which every caller passes through <c>Loc.T</c>.
    ///
    /// ⚠️ <b>The fallback deliberately names nothing (41A).</b> It used to build a line out of the
    /// target — <c>$"Slay {TargetId}"</c> — which puts a raw id like <c>enemy.goblin</c> on screen
    /// the first time anyone authors an objective without a <see cref="Description"/>, violating both
    /// §46 (hard-coded English) and §72/73 (no raw ids, no placeholders). It has never fired because
    /// all fourteen quests author one, which is exactly why it survived: a fallback nothing reaches
    /// is a defect nothing reports. <c>ValidateQuestStringsAreKeys</c> now makes an authored key a
    /// gate, so this path is unreachable by rule rather than by luck — and if it is reached anyway,
    /// it says "objective" instead of leaking an id.
    /// </summary>
    public string ShortLabel() =>
        !string.IsNullOrEmpty(Description) ? Description : "quest.objective.unnamed";
}
