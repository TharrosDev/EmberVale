using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// One selectable reply on a <see cref="DialogueNode"/>: the text the player clicks,
/// the node it leads to, an optional gating <see cref="Condition"/> (hidden when it
/// fails) and an optional <see cref="Effect"/> fired when picked. Authored as a
/// sub-resource inside a dialogue <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class DialogueChoice : Resource
{
    /// <summary>The reply text shown on the choice button.</summary>
    [Export] public string Text { get; set; } = "...";

    /// <summary>Id of the node this choice navigates to; empty ends the conversation.</summary>
    [Export] public string Goto { get; set; } = string.Empty;

    [ExportGroup("Condition")]
    [Export] public DialogueCondition Condition { get; set; } = DialogueCondition.Always;

    /// <summary>Quest id or flag name the <see cref="Condition"/> tests, as appropriate.</summary>
    [Export] public string ConditionArg { get; set; } = string.Empty;

    [ExportGroup("Effect")]
    [Export] public DialogueEffect Effect { get; set; } = DialogueEffect.None;

    /// <summary>Quest id or flag name the <see cref="Effect"/> acts on, as appropriate.</summary>
    [Export] public string EffectArg { get; set; } = string.Empty;

    // Second (condition, effect) pair, appended by the campaign overhaul. A choice is offered only
    // when BOTH conditions hold (an unset pair is Always), and picking it applies BOTH effects, the
    // first pair's first. Both default to "nothing", so every existing .tres is unchanged.
    [ExportGroup("Second pair")]
    [Export] public DialogueCondition Condition2 { get; set; } = DialogueCondition.Always;

    [Export] public string Condition2Arg { get; set; } = string.Empty;

    [Export] public DialogueEffect Effect2 { get; set; } = DialogueEffect.None;

    [Export] public string Effect2Arg { get; set; } = string.Empty;
}
