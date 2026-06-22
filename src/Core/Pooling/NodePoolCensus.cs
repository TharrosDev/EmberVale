namespace Embervale.Core.Pooling;

/// <summary>
/// Process-wide tally of nodes currently <em>parked</em> (detached from the tree, awaiting reuse)
/// across every <see cref="NodePool{T}"/>. By Godot's accounting a parked pool node is an
/// "orphan node" (created, not in the tree, not freed) — but it is an intentional part of the
/// pool's working set, not a leak. The <see cref="Embervale.Debugging.WorldIntegrityChecker"/>
/// subtracts this count so its orphan-leak invariant flags only the <em>excess</em> (real leaks),
/// not pooled nodes. Single-threaded (all node ops run on the main thread), so no locking.
///
/// Counts the <em>whole detached subtree</em>, not just the top-level pooled node: Godot's
/// orphan-node count includes a parked node's descendants (e.g. a pooled projectile's mesh,
/// collision and light children), so the pool reports the full subtree size to keep the
/// integrity checker's <c>orphans − pooled</c> arithmetic honest.
/// </summary>
public static class NodePoolCensus
{
    /// <summary>Nodes currently parked across all pools (counting parked subtrees in full).</summary>
    public static int Parked { get; private set; }

    internal static void OnParked(int nodeCount) => Parked += nodeCount;

    internal static void OnUnparked(int nodeCount) => Parked -= nodeCount;
}
