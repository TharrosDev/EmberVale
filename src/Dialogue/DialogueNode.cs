using System.Collections.Generic;
using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// One beat of a conversation: a line of spoken <see cref="Text"/> plus the
/// <see cref="DialogueChoice"/>es offered in response. Nodes are addressed by
/// <see cref="Id"/> within a <see cref="DialogueResource"/>; choices navigate
/// between them by id. Authored as a sub-resource inside a dialogue <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class DialogueNode : Resource
{
    /// <summary>Node id, unique within its conversation. Choices target it by this.</summary>
    [Export] public string Id { get; set; } = "node";

    /// <summary>Optional speaker override for this line; empty uses the conversation's
    /// <see cref="DialogueResource.SpeakerName"/>.</summary>
    [Export] public string Speaker { get; set; } = string.Empty;

    [Export(PropertyHint.MultilineText)]
    public string Text { get; set; } = string.Empty;

    /// <summary>Replies offered on this node. Untyped so authored sub-resource arrays
    /// bind cleanly; elements are read back as <see cref="DialogueChoice"/>.</summary>
    [Export] public Godot.Collections.Array Choices { get; set; } = new();

    /// <summary>The choices read back as their concrete type, skipping bad entries.</summary>
    public List<DialogueChoice> ChoiceList()
    {
        var list = new List<DialogueChoice>();
        foreach (Variant element in Choices)
        {
            if (element.As<DialogueChoice>() is { } choice)
            {
                list.Add(choice);
            }
        }

        return list;
    }
}
