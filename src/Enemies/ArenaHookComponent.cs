using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Lets an arena react to the fight inside it (Phase 36D). Authored as a node in a region cell's
/// <c>.tscn</c> — the same "declare it in the scene, resolve it at runtime" shape
/// <see cref="LairSpawnComponent"/> uses — it reveals the nodes named in <see cref="Reveals"/> once
/// the boss reaches <see cref="ActivateAtPhase"/>, and hides them again when the boss dies.
///
/// A plain <see cref="Node"/> rather than an <c>EntityComponent</c>: it belongs to the arena, not to
/// an actor, and binding it to one would mean an arena could only ever react to a boss that was
/// standing in a particular spot.
///
/// The reset is not tidiness. <see cref="BossSummonComponent"/> deliberately re-arms after a defeat
/// until 28D persists it, so an arena that stayed lit would show a second challenger the last
/// fight's final phase from the moment they walked in.
/// </summary>
[GlobalClass]
public partial class ArenaHookComponent : Node
{
    /// <summary>Boss phase (1-based) at or beyond which the hook fires.</summary>
    [Export] public int ActivateAtPhase { get; set; } = 2;

    /// <summary>Nodes revealed when it does. Anything deriving <see cref="Node3D"/> — a light, a
    /// mesh, a particle emitter — authored hidden in the scene and shown here.</summary>
    [Export] public Godot.Collections.Array<NodePath> Reveals { get; set; } = new();

    public override void _Ready()
    {
        SetRevealed(false);
        EventBus.Instance?.Subscribe<BossPhaseChangedEvent>(OnPhaseChanged);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<BossPhaseChangedEvent>(OnPhaseChanged);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
    }

    private void OnPhaseChanged(BossPhaseChangedEvent e)
    {
        if (e.Phase >= ActivateAtPhase)
        {
            SetRevealed(true);
        }
    }

    private void OnDied(EntityDiedEvent e)
    {
        if (e.Entity is BossEntity)
        {
            SetRevealed(false);
        }
    }

    private void SetRevealed(bool revealed)
    {
        foreach (NodePath path in Reveals)
        {
            if (path is not null && GetNodeOrNull<Node3D>(path) is { } node)
            {
                node.Visible = revealed;
            }
        }
    }
}
