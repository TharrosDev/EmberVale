using System.Collections.Generic;

namespace Embervale.Debugging;

/// <summary>
/// Pure (Godot-free) reachability analysis of a dialogue graph, split out from
/// <see cref="ContentValidator"/> so it is unit-testable under <c>dotnet test</c> without the
/// engine (the test project cannot construct Godot resources — see tests/README.md). The
/// validator projects each <c>DialogueResource</c> onto <see cref="Node"/>s and calls
/// <see cref="Analyze"/>; the formatting/database walk stays on the Godot side.
///
/// Two structural faults are detected:
/// <list type="bullet">
/// <item><b>Orphan</b> — a node not reachable from the start node by following choice gotos.</item>
/// <item><b>Dead-end</b> — a reachable node from which no path reaches a conversation end
/// (a closed loop with no terminal). A node is <em>terminal</em> when it has no choices or a
/// choice that ends the conversation (an empty goto); those are intentional ends, not faults.</item>
/// </list>
/// </summary>
public static class DialogueGraphAnalysis
{
    /// <summary>A dialogue node reduced to what reachability cares about.</summary>
    public readonly struct Node
    {
        public Node(string id, IReadOnlyList<string> gotos, bool isTerminal)
        {
            Id = id;
            Gotos = gotos;
            IsTerminal = isTerminal;
        }

        /// <summary>Node id, unique within its conversation.</summary>
        public string Id { get; }

        /// <summary>The non-empty goto targets of this node's choices.</summary>
        public IReadOnlyList<string> Gotos { get; }

        /// <summary>True when this node can end the conversation (no choices, or a choice
        /// with an empty goto) — i.e. it is an intentional terminal.</summary>
        public bool IsTerminal { get; }
    }

    /// <summary>The faults found in one graph: node ids only, for the caller to format.</summary>
    public readonly struct Result
    {
        public Result(IReadOnlyList<string> unreachable, IReadOnlyList<string> deadEnds)
        {
            Unreachable = unreachable;
            DeadEnds = deadEnds;
        }

        /// <summary>Ids of nodes not reachable from the start node.</summary>
        public IReadOnlyList<string> Unreachable { get; }

        /// <summary>Ids of reachable nodes that cannot reach any conversation end.</summary>
        public IReadOnlyList<string> DeadEnds { get; }
    }

    /// <summary>Analyses one graph for orphan and dead-end nodes.</summary>
    public static Result Analyze(string startId, IReadOnlyList<Node> nodes)
    {
        var byId = new Dictionary<string, Node>();
        foreach (Node node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Id))
            {
                byId[node.Id] = node;
            }
        }

        // Forward reachability from the start node along goto edges.
        var reachable = new HashSet<string>();
        if (!string.IsNullOrEmpty(startId) && byId.ContainsKey(startId))
        {
            var stack = new Stack<string>();
            stack.Push(startId);
            reachable.Add(startId);
            while (stack.Count > 0)
            {
                Node node = byId[stack.Pop()];
                foreach (string target in node.Gotos)
                {
                    if (byId.ContainsKey(target) && reachable.Add(target))
                    {
                        stack.Push(target);
                    }
                }
            }
        }

        // "Can reach an end": seed with terminals, then propagate backwards to fixed point —
        // a node can end if any of its goto targets can end.
        var canEnd = new HashSet<string>();
        foreach (Node node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Id) && node.IsTerminal)
            {
                canEnd.Add(node.Id);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Node node in nodes)
            {
                if (string.IsNullOrEmpty(node.Id) || canEnd.Contains(node.Id))
                {
                    continue;
                }

                foreach (string target in node.Gotos)
                {
                    if (byId.ContainsKey(target) && canEnd.Contains(target))
                    {
                        canEnd.Add(node.Id);
                        changed = true;
                        break;
                    }
                }
            }
        }

        var unreachable = new List<string>();
        var deadEnds = new List<string>();
        foreach (Node node in nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                continue;
            }

            if (!reachable.Contains(node.Id))
            {
                unreachable.Add(node.Id);
            }
            else if (!canEnd.Contains(node.Id))
            {
                deadEnds.Add(node.Id);
            }
        }

        return new Result(unreachable, deadEnds);
    }
}
