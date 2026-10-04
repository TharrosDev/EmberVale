using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// One conditional entry point of a <see cref="DialogueResource"/>. When a conversation opens, the
/// resource's <see cref="DialogueResource.StartVariants"/> are tried top-down and the first whose
/// <see cref="Condition"/> holds picks the opening node (<see cref="NodeId"/>); when none holds the
/// conversation opens on <see cref="DialogueResource.StartNodeId"/>. Authored as a sub-resource.
/// </summary>
[GlobalClass]
public partial class DialogueStartVariant : Resource
{
    [Export] public DialogueCondition Condition { get; set; } = DialogueCondition.Always;

    /// <summary>Quest id, flag name, etc. the <see cref="Condition"/> tests, as appropriate.</summary>
    [Export] public string ConditionArg { get; set; } = string.Empty;

    /// <summary>Id of the node to open on when the condition holds.</summary>
    [Export] public string NodeId { get; set; } = string.Empty;
}
