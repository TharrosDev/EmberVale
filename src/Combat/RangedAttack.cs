using Embervale.Combat.Actions;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Sends an arrow instead of opening a volume. Owned by one <see cref="CharacterActionComponent"/>,
/// which calls <see cref="Fire"/> on the action's release frame and <see cref="Clear"/> on teardown.
///
/// <para>⚠️ It spawns on the action's release frame like everything else — the string is drawn, and the
/// arrow leaves when the animation shows it leaving. A bow that fired on key-down would put the
/// arrow across the room before the draw finished, which is the melee desync all over again in a
/// system that never had it.</para>
///
/// <para>Split out of the executor so the ranged rules have one owner and the executor stays the
/// melee/cast hub.</para>
/// </summary>
public sealed class RangedAttack
{
    /// <summary>Pooled arrows, built on first use so a melee actor never pays for one.</summary>
    private Core.Pooling.NodePool<Arrow>? _arrows;

    /// <summary>Parked arrows are detached nodes and are not freed with their owner's scene.
    /// Release their render/physics resources when a streamed actor leaves the world.</summary>
    public void Clear()
    {
        _arrows?.Clear();
        _arrows = null;
    }

    private void ReleaseArrow(Arrow arrow)
    {
        if (_arrows != null)
            _arrows.Return(arrow);
        else
            arrow.QueueFree(); // A shot may finish after its owner has left the scene.
    }

    public void Fire(
        WeaponResource bow,
        ActionDefinitionResource definition,
        IEntity? shooter,
        Node3D body,
        StatsComponent? stats,
        CombatComponent? combat,
        MountComponent? mount,
        Vector3? aimPoint)
    {
        _arrows ??= new Core.Pooling.NodePool<Arrow>(() => new Arrow { Released = ReleaseArrow }, prewarm: 2);

        float mounted = MountedCombat.DamageScale(
            mount is { IsMounted: true }, mount is { IsGalloping: true });
        (float amount, bool isCrit) = CombatMath.RollAttack(
            bow.BaseDamage * definition.DamageScale * mounted, stats);

        Vector3 from = body.GlobalPosition + (Vector3.Up * 1.4f);
        Vector3 direction = aimPoint is { } aim && aim.DistanceSquaredTo(from) > 0.04f
            ? (aim - from).Normalized()
            : -body.GlobalBasis.Z;

        Arrow arrow = _arrows.Get();
        if (arrow.GetParent() == null)
        {
            // ⚠️ CurrentScene is null outside a normal game boot — every `--script` harness runs with
            // no current scene — and `CurrentScene?.AddChild` then silently does nothing. The arrow
            // is a live object that is not in the tree: no physics, no overlaps, no hit, no error.
            // Falling back to the tree root keeps it in the WORLD (never parented to the shooter,
            // which would carry it along) and keeps the probes honest.
            SceneTree tree = body.GetTree();
            (tree.CurrentScene ?? tree.Root).AddChild(arrow);
        }

        arrow.GlobalPosition = from;
        arrow.Launch(
            new DamagePacket(amount, bow.DamageType, shooter, isCrit,
                bow.PoiseDamage * definition.PoiseScale, HitKind.Ranged),
            shooter, combat?.Team ?? 0, direction,
            bow.ProjectileSpeed, bow.ProjectileRange, bow.ProjectileModelPath);
    }
}
