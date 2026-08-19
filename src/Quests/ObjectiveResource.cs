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

    [ExportGroup("Branching")]

    /// <summary>
    /// Story flag that must be SET for this objective to count at all (41D); empty = ungated.
    ///
    /// ⚠️ <b>A gated-off objective is INERT, not incomplete.</b> It cannot advance, it is skipped by
    /// <c>AllObjectivesMet</c>, and every surface that draws objectives hides it — so a quest with an
    /// A-path and a B-path is one quest whose journal card shows only the path you took. That is the
    /// whole of "branching": no quest graph, no node type, no new save state, because the flag itself
    /// is the state and <c>StoryFlagsComponent</c> already persists it.
    ///
    /// ⚠️ <b>The flag is a READER here, which makes it <c>ValidateStoryFlags</c>' business.</b> That
    /// rule cross-references readers against writers precisely because story flags are the one id
    /// family with no database behind them — a mistyped gate is a branch that never opens, silently
    /// and permanently. NOW.md named this sub-phase's question in advance: <b>who else writes this
    /// flag?</b> The answer has to be "something", or <c>--validate</c> fails.
    ///
    /// ⚠️ <b>Name it the same as <c>ShopStockEntry.RequiredFlagId</c> on purpose.</b> Same concept,
    /// same word, and the validator already knew that name.
    /// </summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;

    /// <summary>
    /// Story flag that makes this objective inert while it is SET (41D); empty = ungated. The pair to
    /// <see cref="RequiredFlagId"/>, exactly as <c>DialogueCondition.MissingFlag</c> pairs with
    /// <c>HasFlag</c> — a two-path fork is authorable as one flag rather than two, and the path that
    /// was not taken is the one carrying it.
    ///
    /// ⚠️ Authoring the same flag in both fields is refused by <c>--validate</c>: the objective could
    /// never be active, which is an objective that silently does not exist.
    /// </summary>
    [Export] public string ForbiddenFlagId { get; set; } = string.Empty;

    /// <summary>Whether this objective carries any branch gate at all — the cheap early-out for the
    /// common case, since almost every objective ever authored is ungated.</summary>
    public bool IsGated => RequiredFlagId.Length > 0 || ForbiddenFlagId.Length > 0;

    /// <summary>
    /// Whether this objective's branch gate is open for an actor whose flags <paramref name="hasFlag"/>
    /// answers. A null predicate means "no flag source", which reads every gate as OPEN — so a
    /// <see cref="QuestProgress"/> built outside a live actor behaves exactly as it did before 41D.
    /// </summary>
    public bool IsGateOpen(System.Func<string, bool>? hasFlag)
    {
        if (!IsGated || hasFlag == null)
        {
            return true;
        }

        return (RequiredFlagId.Length == 0 || hasFlag(RequiredFlagId))
            && (ForbiddenFlagId.Length == 0 || !hasFlag(ForbiddenFlagId));
    }

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
