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
}
