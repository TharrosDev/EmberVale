using System.Collections.Generic;
using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// A designer-authored conversation: a graph of <see cref="DialogueNode"/>s linked by
/// <see cref="DialogueChoice"/>es, entered at <see cref="StartNodeId"/>. Authored as a
/// <c>.tres</c> under <c>data/dialogue/</c> and indexed by <see cref="DialogueDatabase"/>;
/// a <see cref="DialogueComponent"/> on an NPC plays it and <see cref="DialogueSession"/>
/// walks it at runtime.
///
/// New conversation = a <c>.tres</c>, no code change.
/// </summary>
[GlobalClass]
public partial class DialogueResource : Resource
{
    /// <summary>Stable unique id, e.g. "dialogue.elder". The database key.</summary>
    [Export] public string Id { get; set; } = "dialogue.unknown";

    /// <summary>Default speaker name, used by nodes that don't override it.</summary>
    [Export] public string SpeakerName { get; set; } = "NPC";

    /// <summary>Id of the node the conversation opens on.</summary>
    [Export] public string StartNodeId { get; set; } = "root";

    /// <summary>Conversation nodes. Untyped so authored sub-resource arrays bind cleanly;
    /// elements are read back as <see cref="DialogueNode"/>.</summary>
    [Export] public Godot.Collections.Array Nodes { get; set; } = new();

    /// <summary>The nodes read back as their concrete type, skipping bad entries.</summary>
    public List<DialogueNode> NodeList()
    {
        var list = new List<DialogueNode>();
        foreach (Variant element in Nodes)
        {
            if (element.As<DialogueNode>() is { } node)
            {
                list.Add(node);
            }
        }

        return list;
    }

    /// <summary>Finds a node by id, or null when absent.</summary>
    public DialogueNode? FindNode(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (DialogueNode node in NodeList())
        {
            if (node.Id == id)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>The entry node (<see cref="StartNodeId"/>), or the first node as a fallback.</summary>
    public DialogueNode? StartNode()
    {
        DialogueNode? start = FindNode(StartNodeId);
        if (start != null)
        {
            return start;
        }

        List<DialogueNode> nodes = NodeList();
        return nodes.Count > 0 ? nodes[0] : null;
    }
}
