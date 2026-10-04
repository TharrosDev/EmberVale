using System;
using System.Collections.Generic;

namespace Embervale.Dialogue;

/// <summary>
/// Pure (Godot-free) rules behind the campaign's dialogue extensions, so the argument formats, the
/// two-pair choice gate and the start-variant selection are unit-tested rather than re-derived by
/// <see cref="DialogueSession"/>, <c>ContentValidator</c> and the content tests.
/// </summary>
public static class DialogueRules
{
    /// <summary>
    /// Splits an <c>&lt;id&gt;:&lt;amount&gt;</c> argument (items, factions). The id is everything before
    /// the LAST colon (ids are dotted and never contain one); an argument with no colon yields
    /// <paramref name="defaultAmount"/>. Returns false for an empty id or an amount that is not an
    /// integer, which is how the validator spots a typo; the runtime treats false as "no effect".
    /// </summary>
    public static bool TryParseIdAmount(string? arg, int defaultAmount, out string id, out int amount)
    {
        id = string.Empty;
        amount = defaultAmount;
        if (string.IsNullOrWhiteSpace(arg))
        {
            return false;
        }

        int colon = arg.LastIndexOf(':');
        if (colon < 0)
        {
            id = arg.Trim();
            return id.Length > 0;
        }

        id = arg[..colon].Trim();
        return id.Length > 0 && int.TryParse(arg[(colon + 1)..].Trim(), out amount);
    }

    /// <summary>A choice is offered only when both of its conditions hold (an unset pair is Always).</summary>
    public static bool ChoiceVisible(
        DialogueCondition first, string firstArg, DialogueCondition second, string secondArg,
        Func<DialogueCondition, string, bool> evaluate) =>
        evaluate(first, firstArg) && evaluate(second, secondArg);

    /// <summary>
    /// The node a conversation opens on: the first start variant (top-down) whose condition holds and
    /// whose node exists, else <paramref name="defaultNodeId"/>.
    /// </summary>
    public static string SelectStartNode(
        IEnumerable<(DialogueCondition Condition, string Arg, string NodeId)> variants,
        Func<DialogueCondition, string, bool> evaluate, Func<string, bool> nodeExists, string defaultNodeId)
    {
        foreach ((DialogueCondition condition, string arg, string nodeId) in variants)
        {
            if (!string.IsNullOrEmpty(nodeId) && nodeExists(nodeId) && evaluate(condition, arg))
            {
                return nodeId;
            }
        }

        return defaultNodeId;
    }

    /// <summary>True for the effects that open a panel over the conversation; a node's OnEnter effect
    /// may not be one of these.</summary>
    public static bool OpensPanel(DialogueEffect effect) =>
        effect is DialogueEffect.OpenShop or DialogueEffect.OpenService;
}
